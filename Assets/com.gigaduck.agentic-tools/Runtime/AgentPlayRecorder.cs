#if DEVELOPMENT_BUILD || UNITY_EDITOR || AGENT_PLAY
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Gigaduck.AgenticTools.AgentPlay
{
    /// <summary>
    /// Records what the player's camera shows while an agent plays (the <c>record</c> op; <c>agentplay.py record</c>).
    /// Each captured frame is rendered from the game camera into a small render texture, read back from the GPU without a
    /// stall (<see cref="AsyncGPUReadback"/>) and handed to a worker thread, which streams raw frames into ffmpeg (H.264 at a
    /// constant frame rate: a hitch repeats the last frame, so the video keeps real time) or, without ffmpeg, writes JPEG
    /// frames. Every <c>keyEvery</c> seconds it also saves a small JPEG keyframe, so an agent can look at a recording as a
    /// contact sheet instead of a video. Events (the op the agent sent, its result, marks, the stage label and on-screen text
    /// when they change) go to <c>events.jsonl</c> and become the video's captions. One recording at a time per player;
    /// files go to <c>&lt;out&gt;/recordings/&lt;id&gt;/</c>.
    /// </summary>
    public sealed class AgentPlayRecorder : MonoBehaviour
    {
        /// <summary>Recording settings, read from the <c>record start</c> command.</summary>
        public sealed class Options
        {
            public string Id, Folder, Label, Ffmpeg, Codec = "libx264", Preset = "veryfast";
            public int Fps = 15, Width = 960, Height = 540, Quality = 72, Crf = 26;
            public float MaxSeconds = 3600f, KeyEvery = 2f;
            public bool? Flip;
        }

        /// <summary>The running recording, or null.</summary>
        public static AgentPlayRecorder Active { get; private set; }

        /// <summary>What the last finished recording wrote to its recording.json (one that stopped at its time limit too).</summary>
        public static Dictionary<string, object> LastMeta { get; private set; }

        /// <summary>The camera to record; default the harness picture camera.</summary>
        public static Func<Camera> CameraProvider;

        /// <summary>A short label of where the game is (level, stage); written as a "label" event whenever it changes.</summary>
        public static Func<string> LabelProvider;

        /// <summary>The text on screen (subtitles, prompts); written as a "text" event whenever it changes.</summary>
        public static Func<string> TextProvider;

        public string Id => _o.Id;
        public string Folder => _o.Folder;
        public bool Stopped => _finished;
        public float Elapsed => _stopping ? _stoppedAt : Time.realtimeSinceStartup - _began;

        private sealed class Frame { public byte[] Data; public float T; public bool Key, TopFirst; }

        private Options _o;
        private float _began, _nextCapture, _nextLabel, _nextKey, _stoppedAt;
        private bool _stopping, _finished, _flip, _useAsync;
        private RenderTexture _rt;
        private Texture2D _syncTex;
        private int _captured, _dropped, _pending, _emitted, _keyframes, _written;
        private string _sink = "jpeg", _sinkError, _lastLabel, _lastText;
        private readonly Queue<Frame> _queue = new Queue<Frame>();
        private readonly Stack<byte[]> _pool = new Stack<byte[]>();
        private readonly object _gate = new object();
        private readonly List<object> _keys = new List<object>();
        private Thread _worker;
        private Process _ffmpeg;
        private Stream _stdin;
        private readonly StringBuilder _ffmpegErr = new StringBuilder();
        private StreamWriter _events;
        private const int MaxQueued = 24;

        // ------------------------------------------------------------------ control

        /// <summary>Starts a recording. Returns null and the recorder, or an error text.</summary>
        public static string Begin(Options o, out AgentPlayRecorder recorder)
        {
            recorder = null;
            if (Active != null && !Active._finished) return "already recording '" + Active.Id + "' (record stop first)";
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return "this player has no GPU (-nographics): start it without --headless to record";
            o.Width = Mathf.Clamp(o.Width & ~1, 64, 3840);
            o.Height = Mathf.Clamp(o.Height & ~1, 64, 2160);
            o.Fps = Mathf.Clamp(o.Fps, 1, 60);
            o.Quality = Mathf.Clamp(o.Quality, 10, 100);
            Directory.CreateDirectory(o.Folder);
            Directory.CreateDirectory(Path.Combine(o.Folder, "keyframes"));
            var go = new GameObject("AgentPlayRecorder");
            DontDestroyOnLoad(go);
            recorder = go.AddComponent<AgentPlayRecorder>();
            recorder.Open(o);
            Active = recorder;
            return null;
        }

        /// <summary>Adds an event to the running recording (kind: op, result, mark, segment, finding...).</summary>
        public static void Mark(string kind, string text)
        {
            AgentPlayRecorder r = Active;
            if (r == null || r._stopping || string.IsNullOrEmpty(text)) return;
            r.Event(kind, text);
        }

        /// <summary>Saves a keyframe with the next captured frame.</summary>
        public static void Snap()
        {
            if (Active != null) Active._nextKey = 0f;
        }

        private void Open(Options o)
        {
            _o = o;
            _began = Time.realtimeSinceStartup;
            _useAsync = SystemInfo.supportsAsyncGPUReadback;
            _flip = o.Flip ?? ReadbackIsBottomUp();
            _rt = new RenderTexture(o.Width, o.Height, 24, RenderTextureFormat.ARGB32) { name = "AgentPlayRecorder" };
            _rt.Create();
            _events = new StreamWriter(Path.Combine(o.Folder, "events.jsonl"), false, new UTF8Encoding(false)) { AutoFlush = true };
            if (!string.IsNullOrEmpty(o.Ffmpeg) && File.Exists(o.Ffmpeg))
            {
                try { StartFfmpeg(); _sink = "ffmpeg"; }
                catch (Exception e) { _sinkError = "ffmpeg did not start: " + e.Message; _sink = "jpeg"; }
            }
            else if (!string.IsNullOrEmpty(o.Ffmpeg)) _sinkError = "ffmpeg not found at " + o.Ffmpeg;

            if (_sink == "jpeg") Directory.CreateDirectory(Path.Combine(o.Folder, "frames"));
            _worker = new Thread(Work) { IsBackground = true, Name = "AgentPlayRecorder" };
            _worker.Start();
            if (!string.IsNullOrEmpty(o.Label)) Event("label", o.Label);
        }

        /// <summary>Stops, waits for the encoder, writes <c>recording.json</c> and fills <paramref name="result"/> with it.</summary>
        public IEnumerator Stop(Dictionary<string, object> result)
        {
            if (!_stopping)
            {
                _stopping = true;
                _stoppedAt = Time.realtimeSinceStartup - _began;
                if (_useAsync) AsyncGPUReadback.WaitAllRequests();
                lock (_gate) { _queue.Enqueue(null); Monitor.Pulse(_gate); }
            }

            float until = Time.realtimeSinceStartup + 180f;
            while (_worker != null && _worker.IsAlive && Time.realtimeSinceStartup < until) yield return null;
            Finish(result);
        }

        private void Finish(Dictionary<string, object> result)
        {
            if (!_finished)
            {
                _finished = true;
                try { _events?.Dispose(); } catch (IOException) { }
                _events = null;
                if (_rt != null) { _rt.Release(); Destroy(_rt); }
                if (_syncTex != null) Destroy(_syncTex);
                LastMeta = Meta();
                File.WriteAllText(Path.Combine(_o.Folder, "recording.json"), AgentPlayJson.Write(LastMeta));
                if (Active == this) Active = null;
                Destroy(gameObject, 0.1f);
            }

            if (result != null) foreach (var kv in Meta()) result[kv.Key] = kv.Value;
        }

        public Dictionary<string, object> Meta()
        {
            var m = new Dictionary<string, object>
            {
                ["id"] = _o.Id, ["folder"] = _o.Folder, ["label"] = _o.Label ?? "", ["sink"] = _sink,
                ["video"] = _sink == "ffmpeg" ? "video.mp4" : null, ["fps"] = _o.Fps, ["width"] = _o.Width, ["height"] = _o.Height,
                ["duration"] = Round(Elapsed), ["captured"] = _captured, ["frames"] = _sink == "ffmpeg" ? _emitted : _written,
                ["dropped"] = _dropped, ["keyframes"] = KeyList(), ["flipped"] = _flip, ["readback"] = _useAsync ? "async" : "sync",
                ["started"] = DateTime.Now.AddSeconds(-Elapsed).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ["stopped"] = _finished,
            };
            lock (_gate)
            {
                if (_sinkError != null) m["sinkError"] = _sinkError;
                if (_ffmpegErr.Length > 0) m["ffmpegLog"] = _ffmpegErr.ToString();
            }

            return m;
        }

        private List<object> KeyList()
        {
            lock (_gate) return new List<object>(_keys);
        }

        public void Status(Dictionary<string, object> r)
        {
            r["recording"] = !_stopping;
            r["id"] = _o.Id;
            r["elapsed"] = Round(Elapsed);
            r["captured"] = _captured;
            r["dropped"] = _dropped;
            r["sink"] = _sink;
            lock (_gate) if (_sinkError != null) r["sinkError"] = _sinkError;
        }

        // ------------------------------------------------------------------ capture (main thread)

        private void LateUpdate()
        {
            if (_stopping) return;
            float t = Time.realtimeSinceStartup - _began;
            if (t >= _o.MaxSeconds)
            {
                Event("mark", "recording stopped: reached the " + Mathf.RoundToInt(_o.MaxSeconds) + " s limit");
                StartCoroutine(Stop(null));
                return;
            }

            if (t >= _nextLabel)
            {
                _nextLabel = t + 1f;
                SampleText(LabelProvider, ref _lastLabel, "label");
                SampleText(TextProvider, ref _lastText, "text");
            }

            if (t < _nextCapture) return;
            float step = 1f / _o.Fps;
            _nextCapture = t - _nextCapture > step ? t + step : _nextCapture + step;
            Capture(t);
        }

        private void SampleText(Func<string> provider, ref string last, string kind)
        {
            if (provider == null) return;
            string now;
            try { now = provider(); }
            catch (Exception) { return; }
            now = now ?? "";
            if (now == last) return;
            last = now;
            if (now.Length > 0) Event(kind, now);
        }

        private void Capture(float t)
        {
            Camera cam = CameraProvider != null ? CameraProvider() : null;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            lock (_gate)
            {
                if (_pending + _queue.Count >= MaxQueued) { _dropped++; return; }
            }

            bool key = t >= _nextKey;
            if (key) _nextKey = t + Mathf.Max(0.25f, _o.KeyEvery);
            RenderTexture oldTarget = cam.targetTexture;
            try
            {
#if UNITY_2023_1_OR_NEWER
                var request = new RenderPipeline.StandardRequest { destination = _rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
                else
#endif
                {
                    cam.targetTexture = _rt;
                    cam.Render();
                }
            }
            catch (Exception e)
            {
                _dropped++;
                lock (_gate) _sinkError = _sinkError ?? ("camera render failed: " + e.Message);
                return;
            }
            finally
            {
                cam.targetTexture = oldTarget;
            }

            _captured++;
            if (_useAsync)
            {
                lock (_gate) _pending++;
                AsyncGPUReadback.Request(_rt, 0, TextureFormat.RGBA32, req => OnReadback(req, t, key));
            }
            else
            {
                if (_syncTex == null) _syncTex = new Texture2D(_o.Width, _o.Height, TextureFormat.RGBA32, false);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = _rt;
                _syncTex.ReadPixels(new Rect(0, 0, _o.Width, _o.Height), 0, 0, false);
                RenderTexture.active = previous;
                NativeArray<byte> data = _syncTex.GetRawTextureData<byte>();
                Enqueue(data, t, key, topFirst: false); // Texture2D data is bottom row first
            }
        }

        private void OnReadback(AsyncGPUReadbackRequest req, float t, bool key)
        {
            lock (_gate) _pending--;
            if (_finished) return;
            if (req.hasError) { _dropped++; return; }
            Enqueue(req.GetData<byte>(), t, key, topFirst: !_flip);
        }

        /// <summary>Queues a copy of the pixels (the only main-thread work); the worker turns them top row first.</summary>
        private void Enqueue(NativeArray<byte> data, float t, bool key, bool topFirst)
        {
            byte[] buffer;
            lock (_gate) buffer = _pool.Count > 0 ? _pool.Pop() : null;
            if (buffer == null || buffer.Length != data.Length) buffer = new byte[data.Length];
            data.CopyTo(buffer);
            lock (_gate)
            {
                _queue.Enqueue(new Frame { Data = buffer, T = t, Key = key, TopFirst = topFirst });
                Monitor.Pulse(_gate);
            }
        }

        private static void FlipRows(byte[] data, int stride, int rows)
        {
            var tmp = new byte[stride];
            for (int top = 0, bottom = rows - 1; top < bottom; top++, bottom--)
            {
                Buffer.BlockCopy(data, top * stride, tmp, 0, stride);
                Buffer.BlockCopy(data, bottom * stride, data, top * stride, stride);
                Buffer.BlockCopy(tmp, 0, data, bottom * stride, stride);
            }
        }

        /// <summary>
        /// True when GPU readback returns the bottom row first (like Texture2D data). Found once by blitting a texture that
        /// is black at the bottom and white at the top, so the recording is upright on every graphics API.
        /// </summary>
        private static bool ReadbackIsBottomUp()
        {
            if (!SystemInfo.supportsAsyncGPUReadback) return true;
            var tex = new Texture2D(1, 2, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.black);
            tex.SetPixel(0, 1, Color.white);
            tex.Apply(false);
            RenderTexture rt = RenderTexture.GetTemporary(1, 2, 0, RenderTextureFormat.ARGB32);
            try
            {
                Graphics.Blit(tex, rt);
                AsyncGPUReadbackRequest req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
                req.WaitForCompletion();
                if (req.hasError) return true;
                NativeArray<byte> data = req.GetData<byte>();
                return data[0] < 128;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
                Destroy(tex);
            }
        }

        // ------------------------------------------------------------------ encoder (worker thread)

        private void StartFfmpeg()
        {
            string output = Path.Combine(_o.Folder, "video.mp4");
            string codec = string.IsNullOrEmpty(_o.Codec) ? "libx264" : _o.Codec;
            string quality = codec == "libx264" ? "-preset " + _o.Preset + " -crf " + _o.Crf + " -tune zerolatency" : "-q:v 5";
            string args = "-hide_banner -loglevel error -nostats -y -f rawvideo -pix_fmt rgba -s " + _o.Width + "x" + _o.Height +
                          " -r " + _o.Fps + " -i - -c:v " + codec + " " + quality + " -pix_fmt yuv420p -g " + (_o.Fps * 2) +
                          " -movflags +frag_keyframe+empty_moov+default_base_moof \"" + output + "\"";
            var info = new ProcessStartInfo(_o.Ffmpeg, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true,
            };
            _ffmpeg = new Process { StartInfo = info };
            _ffmpeg.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                lock (_gate) if (_ffmpegErr.Length < 4000) _ffmpegErr.AppendLine(e.Data);
            };
            _ffmpeg.Start();
            _ffmpeg.BeginErrorReadLine();
            _stdin = _ffmpeg.StandardInput.BaseStream;
        }

        private void Work()
        {
            byte[] last = null;
            while (true)
            {
                Frame frame;
                lock (_gate)
                {
                    while (_queue.Count == 0) Monitor.Wait(_gate);
                    frame = _queue.Dequeue();
                }

                if (frame == null) break;
                if (!frame.TopFirst) FlipRows(frame.Data, _o.Width * 4, _o.Height); // ffmpeg and Jpeg() take the top row first
                try
                {
                    if (frame.Key) WriteKeyframe(frame);
                    if (_stdin != null) WriteVideo(frame);
                    else WriteJpegFrame(frame);
                }
                catch (Exception e)
                {
                    lock (_gate) _sinkError = _sinkError ?? (_sink + " sink failed: " + e.Message);
                    if (_stdin != null) FallBackToJpeg();
                }

                if (last != null) lock (_gate) _pool.Push(last);
                last = frame.Data;
            }

            CloseFfmpeg();
        }

        private void WriteVideo(Frame frame)
        {
            // Constant frame rate: the frame shown at time T is number floor(T * fps). A hitch repeats this frame until the
            // count catches up, so the video keeps real time; a frame that arrives early (ahead of its slot) is skipped.
            int target = (int)Math.Floor(frame.T * _o.Fps) + 1;
            int copies = Math.Min(target - _emitted, _o.Fps * 30);
            for (int i = 0; i < copies; i++) _stdin.Write(frame.Data, 0, frame.Data.Length);
            _emitted += Math.Max(0, copies);
            if (target - _emitted > 0) _emitted = target; // a stall longer than 30 s: jump instead of writing minutes of copies
        }

        private void WriteJpegFrame(Frame frame)
        {
            byte[] jpg = Jpeg(frame.Data);
            File.WriteAllBytes(Path.Combine(_o.Folder, "frames", "f_" + _written.ToString("000000", CultureInfo.InvariantCulture) + ".jpg"), jpg);
            File.AppendAllText(Path.Combine(_o.Folder, "frames.jsonl"),
                "{\"i\":" + _written + ",\"t\":" + Round(frame.T).ToString("R", CultureInfo.InvariantCulture) + "}\n");
            _written++;
        }

        private void WriteKeyframe(Frame frame)
        {
            string name = "k_" + ((int)Math.Round(frame.T * 1000)).ToString("0000000", CultureInfo.InvariantCulture) + ".jpg";
            File.WriteAllBytes(Path.Combine(_o.Folder, "keyframes", name), Jpeg(frame.Data));
            lock (_gate) _keys.Add(new Dictionary<string, object> { ["t"] = Round(frame.T), ["file"] = "keyframes/" + name });
            _keyframes++;
        }

        /// <summary>JPEG from top-row-first RGBA (the encoder wants Texture2D order, bottom row first, so flip a copy).</summary>
        private byte[] Jpeg(byte[] topFirst)
        {
            var copy = (byte[])topFirst.Clone();
            FlipRows(copy, _o.Width * 4, _o.Height);
            return ImageConversion.EncodeArrayToJPG(copy, GraphicsFormat.R8G8B8A8_UNorm, (uint)_o.Width, (uint)_o.Height, 0, _o.Quality);
        }

        private void FallBackToJpeg()
        {
            CloseFfmpeg();
            _sink = "jpeg";
            Directory.CreateDirectory(Path.Combine(_o.Folder, "frames"));
        }

        private void CloseFfmpeg()
        {
            if (_ffmpeg == null) return;
            try { _stdin?.Flush(); _stdin?.Dispose(); } catch (IOException) { }
            _stdin = null;
            try
            {
                if (!_ffmpeg.WaitForExit(120000)) { _ffmpeg.Kill(); lock (_gate) _sinkError = "ffmpeg did not finish within 120 s"; }
                else if (_ffmpeg.ExitCode != 0) lock (_gate) _sinkError = _sinkError ?? ("ffmpeg exited with code " + _ffmpeg.ExitCode);
            }
            catch (Exception e) { lock (_gate) _sinkError = _sinkError ?? ("ffmpeg: " + e.Message); }
            _ffmpeg.Dispose();
            _ffmpeg = null;
        }

        // ------------------------------------------------------------------ events

        private void Event(string kind, string text)
        {
            if (_events == null) return;
            float t = Time.realtimeSinceStartup - _began;
            if (text.Length > 400) text = text.Substring(0, 400);
            try
            {
                _events.WriteLine(AgentPlayJson.Write(new Dictionary<string, object> { ["t"] = Round(t), ["kind"] = kind, ["text"] = text }));
            }
            catch (IOException) { }
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            if (_finished) return;
            // Destroyed without Stop (player quitting): end the encoder so the video file is complete.
            _stopping = true;
            lock (_gate) { _queue.Enqueue(null); Monitor.Pulse(_gate); }
            _worker?.Join(30000);
            try { _events?.Dispose(); } catch (IOException) { }
            try { File.WriteAllText(Path.Combine(_o.Folder, "recording.json"), AgentPlayJson.Write(Meta())); } catch (IOException) { }
        }

        private static float Round(float v) => Mathf.Round(v * 1000f) / 1000f;
    }
}
#endif
