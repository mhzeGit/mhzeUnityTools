#if DEVELOPMENT_BUILD || UNITY_EDITOR || AGENT_PLAY
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Gigaduck.AgenticTools.AgentPlay
{
    /// <summary>
    /// Content Studio, player side (the <c>studio</c> op; <c>tools/studio/studio.py</c>): renders marketing footage from a
    /// running agent player without touching scenes or the real game. A <b>take</b> renders the game on fixed time steps
    /// (<see cref="Time.captureDeltaTime"/>, "offline": every frame is perfect at any resolution and speed) or in real time,
    /// into separate layers: <c>clean.mp4</c> (the picture with no overlay UI), <c>ui/ui_00000.png...</c> (the overlay UI alone,
    /// with alpha, at the same size, laid out for that aspect ratio), <c>audio.wav</c> (the game's sound, 32-bit float) and
    /// <c>events.jsonl</c> (on-screen text, stage labels, marks: captions and edit markers). Layers are combined later
    /// (studio.py export, the Blender edit), so "with or without UI / sound" never needs a new take.
    /// The camera is the player's eye, or a studio camera (pose, face close-up, follow, keyframed path, hand-held shake) that
    /// copies the player camera's pipeline settings. Overlay canvases are moved to a private UI camera for the take and put
    /// back afterwards. <c>solo</c> mutes every audio source outside one stem (voice, music, ambience, sfx) for stem passes.
    /// A <b>still</b> renders one frame as PNG layers. Only in editor, Development and agent builds; nothing runs unless an
    /// agent sends the op.
    /// </summary>
    public sealed class ContentStudio : MonoBehaviour
    {
        /// <summary>Optional project hook: the stem of an audio source ("voice", "music", "ambience", "sfx"), or null to use the rules.</summary>
        public static Func<AudioSource, string> StemOf;

        /// <summary>Raised when a take or still begins (true) and ends (false): a project can hide its crosshair, debug overlays...</summary>
        public static event Action<bool> Staging;

        /// <summary>The running take, or null.</summary>
        public static ContentStudio Active { get; private set; }

        /// <summary>Game seconds since the running take began (frames / fps in an offline take).</summary>
        public float TakeTime => _offline ? _frames / (float)_fps : Time.realtimeSinceStartup - _began;

        private string _id, _folder, _ffmpeg, _solo;
        private int _w, _h, _fps, _crf, _frames, _emitted, _maxFrames;
        private bool _offline, _wantUi, _wantAudio, _flip, _stopping, _finished, _audioOffline, _staged;
        private float _began, _nextScan, _nextText;
        private string _lastLabel, _lastText, _error;
        private StudioRig _rig;
        private RenderTexture _cleanRt, _uiRt;
        private FrameSink _cleanSink, _uiSink;
        private WavWriter _wav;
        private ListenerTap _tap;
        private StreamWriter _events;
        private Dictionary<string, List<string>> _stemRules;
        private readonly Dictionary<AudioSource, bool> _muted = new Dictionary<AudioSource, bool>();
        private readonly Dictionary<string, int> _stemCounts = new Dictionary<string, int>();
        private readonly Queue<Pending> _pending = new Queue<Pending>();
        private Dictionary<string, object> _options;

        private struct Pending { public AsyncGPUReadbackRequest Req; public FrameSink Sink; public int Copies; }

        // ------------------------------------------------------------------ the op

        /// <summary>The <c>studio</c> op: action start | stop | wait | mark | camera | hide | still | stems | status.</summary>
        public static IEnumerator Op(Dictionary<string, object> c, Dictionary<string, object> r, Action<string> fail)
        {
            string action = AgentPlayOps.S(c, "action", "status");
            switch (action)
            {
                case "start":
                    string err = Begin(c);
                    if (err != null) { fail(err); yield break; }
                    Active.StatusInto(r);
                    yield return null; // the first frame is rendered before the answer
                    break;
                case "stop":
                    if (Active == null) { fail("no take is running"); yield break; }
                    yield return Active.Stop(r);
                    break;
                case "wait":
                {
                    // Game time (an offline take advances exactly 1/fps per frame, whatever the real time).
                    float s = AgentPlayOps.F(c, "s", 1f);
                    if (Active == null) { yield return new WaitForSeconds(s); break; }
                    float until = Active.TakeTime + s;
                    while (Active != null && !Active._stopping && Active.TakeTime < until) yield return null;
                    if (Active != null) Active.StatusInto(r);
                    break;
                }
                case "mark":
                    if (Active == null) { fail("no take is running"); yield break; }
                    Active.Event(AgentPlayOps.S(c, "kind", "mark"), AgentPlayOps.S(c, "text", ""));
                    Active.StatusInto(r);
                    break;
                case "camera":
                    if (Active == null) { fail("no take is running"); yield break; }
                    string cerr = Active._rig.SetCamera(c.TryGetValue("camera", out object cam) ? cam as Dictionary<string, object> : c);
                    if (cerr != null) { fail(cerr); yield break; }
                    Active.Event("cut", AgentPlayJson.Write(Active._rig.CameraSpec));
                    Active.StatusInto(r);
                    break;
                case "hide":
                    if (Active == null) { fail("no take is running"); yield break; }
                    r["hidden"] = Active._rig.Hide(Strings(c, "names"));
                    break;
                case "still":
                    yield return Still(c, r, fail);
                    break;
                case "stems":
                    r["sources"] = DescribeStems(Rules(c));
                    break;
                case "status":
                    if (Active != null) Active.StatusInto(r);
                    else r["take"] = false;
                    break;
                default:
                    fail("studio action is start, stop, wait, mark, camera, hide, still, stems or status (got '" + action + "')");
                    break;
            }
        }

        // ------------------------------------------------------------------ take: start

        private static string Begin(Dictionary<string, object> c)
        {
            if (Active != null && !Active._finished) return "take '" + Active._id + "' is running (studio stop first)";
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return "this player has no GPU: start it with a window to render";
            if (!SystemInfo.supportsAsyncGPUReadback) return "this graphics device has no async GPU readback";
            string folder = AgentPlayOps.S(c, "folder", null);
            if (string.IsNullOrEmpty(folder)) return "studio start needs folder (where the take's files go)";
            string ffmpeg = AgentPlayOps.S(c, "ffmpeg", null);
            if (string.IsNullOrEmpty(ffmpeg) || !File.Exists(ffmpeg)) return "studio start needs ffmpeg (path to ffmpeg.exe), got '" + ffmpeg + "'";

            var go = new GameObject("ContentStudio");
            DontDestroyOnLoad(go);
            var s = go.AddComponent<ContentStudio>();
            string err = s.Open(c, folder, ffmpeg);
            if (err != null)
            {
                s.Restore();
                Destroy(go);
                return err;
            }

            Active = s;
            return null;
        }

        private string Open(Dictionary<string, object> c, string folder, string ffmpeg)
        {
            _options = c;
            _id = AgentPlayOps.S(c, "id", "take");
            _folder = folder;
            _ffmpeg = ffmpeg;
            _w = Mathf.Clamp(AgentPlayOps.I(c, "w", 1080) & ~1, 64, 7680);
            _h = Mathf.Clamp(AgentPlayOps.I(c, "h", 1920) & ~1, 64, 7680);
            _fps = Mathf.Clamp(AgentPlayOps.I(c, "fps", 30), 1, 120);
            _crf = Mathf.Clamp(AgentPlayOps.I(c, "crf", 14), 0, 40);
            _offline = AgentPlayOps.S(c, "mode", "offline") != "realtime";
            _wantUi = AgentPlayOps.B(c, "ui", true);
            _wantAudio = AgentPlayOps.B(c, "audio", true);
            _solo = AgentPlayOps.S(c, "solo", null);
            if (_solo == "") _solo = null;
            _maxFrames = Mathf.RoundToInt(AgentPlayOps.F(c, "max", 600f) * _fps);
            _stemRules = Rules(c);
            Directory.CreateDirectory(folder);

            _rig = new StudioRig();
            string err = _rig.Setup(c, _w, _h);
            if (err != null) return err;
            _staged = true;
            Staging?.Invoke(true);

            _flip = AgentPlayRecorder.ReadbackIsBottomUp();
            _cleanRt = NewRt("ContentStudioClean");
            if (_wantUi)
            {
                _uiRt = NewRt("ContentStudioUi");
                Directory.CreateDirectory(Path.Combine(folder, "ui"));
            }

            string vflip = _flip ? "-vf vflip " : "";
            string input = "-hide_banner -loglevel error -nostats -y -f rawvideo -pix_fmt rgba -s " + _w + "x" + _h + " -r " + _fps + " -i - ";
            try
            {
                _cleanSink = new FrameSink(ffmpeg, input + vflip + "-c:v libx264 -preset medium -crf " + _crf +
                                                   " -pix_fmt yuv420p -g " + _fps + " -movflags +faststart \"" + Path.Combine(folder, "clean.mp4") + "\"");
                if (_wantUi)
                    _uiSink = new FrameSink(ffmpeg, input + vflip + "-c:v png -pix_fmt rgba -start_number 0 \"" +
                                                    Path.Combine(folder, "ui", "ui_%05d.png") + "\"");
            }
            catch (Exception e)
            {
                return "ffmpeg did not start: " + e.Message;
            }

            if (_offline) Time.captureDeltaTime = 1f / _fps;
            if (_wantAudio)
            {
                _wav = new WavWriter(Path.Combine(folder, "audio.wav"), AudioSettings.outputSampleRate, SpeakerChannels());
                _audioOffline = _offline && AudioRenderer.Start();
                if (!_audioOffline)
                {
                    AudioListener listener = FindObjectsByType<AudioListener>(FindObjectsSortMode.None).FirstOrDefault(l => l.isActiveAndEnabled);
                    if (listener != null) _tap = listener.gameObject.AddComponent<ListenerTap>();
                    else _error = "no active AudioListener: the take has no sound";
                }
            }

            _events = new StreamWriter(Path.Combine(folder, "events.jsonl"), false, new UTF8Encoding(false)) { AutoFlush = true };
            _began = Time.realtimeSinceStartup;
            Event("camera", AgentPlayJson.Write(_rig.CameraSpec));
            string label = AgentPlayOps.S(c, "label", "");
            if (label.Length > 0) Event("label", label);
            ApplySolo();
            StartCoroutine(EndOfFrames());
            return null;
        }

        private RenderTexture NewRt(string name)
        {
            var rt = new RenderTexture(_w, _h, 24, RenderTextureFormat.ARGB32) { name = name, antiAliasing = 1 };
            rt.Create();
            return rt;
        }

        // ------------------------------------------------------------------ take: every frame

        private IEnumerator EndOfFrames()
        {
            var end = new WaitForEndOfFrame();
            while (!_stopping)
            {
                yield return end;
                if (_stopping) break;
                try { CaptureFrame(); }
                catch (Exception e)
                {
                    _error = _error ?? ("capture failed: " + e.Message);
                    Debug.LogException(e);
                }
            }
        }

        private void CaptureFrame()
        {
            float t = TakeTime;
            if (_offline ? _frames >= _maxFrames : t >= _maxFrames / (float)_fps)
            {
                Event("mark", "take stopped: reached its length limit");
                StartCoroutine(Stop(null));
                return;
            }

            if (t >= _nextScan)
            {
                _nextScan = t + 0.5f;
                _rig.Rescan();
            }

            if (_solo != null) ApplySolo();
            if (t >= _nextText)
            {
                _nextText = t + 0.25f;
                Sample(AgentPlayRecorder.LabelProvider, ref _lastLabel, "label");
                Sample(AgentPlayRecorder.TextProvider, ref _lastText, "text");
            }

            int copies = 1;
            if (!_offline)
            {
                // Real time: the frame for time t is number floor(t * fps); a hitch repeats a frame so the video keeps time.
                int target = (int)Math.Floor(t * _fps) + 1;
                copies = target - _emitted;
                if (copies <= 0) { CaptureAudio(); return; }
                _emitted = target;
            }

            _rig.Advance(_offline ? 1f / _fps : Time.unscaledDeltaTime, t);
            _rig.RenderClean(_cleanRt);
            Request(_cleanRt, _cleanSink, copies);
            if (_wantUi)
            {
                _rig.RenderUi(_uiRt);
                Request(_uiRt, _uiSink, copies);
            }

            _frames += _offline ? 1 : copies;
            CaptureAudio();
            Drain(false);
        }

        private void Request(RenderTexture rt, FrameSink sink, int copies)
        {
            _pending.Enqueue(new Pending { Req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32), Sink = sink, Copies = copies });
        }

        /// <summary>Hands finished readbacks to their encoders in order; waits when too many are in flight (never drops a frame).</summary>
        private void Drain(bool all)
        {
            while (_pending.Count > 0)
            {
                Pending p = _pending.Peek();
                if (!p.Req.done)
                {
                    if (!all && _pending.Count < 8) return;
                    p.Req.WaitForCompletion();
                }

                _pending.Dequeue();
                if (p.Req.hasError) { _error = _error ?? "a GPU readback failed"; continue; }
                p.Sink.Write(p.Req.GetData<byte>(), p.Copies);
            }
        }

        private void CaptureAudio()
        {
            if (_wav == null) return;
            if (_audioOffline)
            {
                int n = AudioRenderer.GetSampleCountForCaptureFrame();
                if (n <= 0) return;
                var buffer = new NativeArray<float>(n * _wav.Channels, Allocator.Temp);
                AudioRenderer.Render(buffer);
                _wav.Write(buffer);
                buffer.Dispose();
            }
            else if (_tap != null)
            {
                float[] data = _tap.Take(out int channels);
                if (data != null && data.Length > 0) _wav.Write(data, channels);
            }
        }

        // ------------------------------------------------------------------ take: stop

        private IEnumerator Stop(Dictionary<string, object> result)
        {
            if (!_stopping)
            {
                _stopping = true;
                Drain(true);
                _cleanSink?.Close();
                _uiSink?.Close();
                float until = Time.realtimeSinceStartup + 300f;
                while (Time.realtimeSinceStartup < until && ((_cleanSink != null && !_cleanSink.Done) || (_uiSink != null && !_uiSink.Done)))
                    yield return null;
                Finish();
            }
            else
            {
                while (!_finished) yield return null;
            }

            if (result != null) foreach (var kv in Meta()) result[kv.Key] = kv.Value;
        }

        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            if (_audioOffline) AudioRenderer.Stop();
            if (_tap != null) Destroy(_tap);
            _wav?.Close();
            Restore();
            try { _events?.Dispose(); } catch (IOException) { }
            _events = null;
            File.WriteAllText(Path.Combine(_folder, "take.json"), AgentPlayJson.Write(Meta()));
            if (Active == this) Active = null;
            Destroy(gameObject, 0.1f);
        }

        /// <summary>Puts the game back: time step, canvases, hidden objects, cameras, muted sources.</summary>
        private void Restore()
        {
            Time.captureDeltaTime = 0f;
            foreach (var kv in _muted) if (kv.Key != null) kv.Key.mute = kv.Value;
            _muted.Clear();
            if (_rig != null)
            {
                _rig.Dispose();
                _rig = null;
                if (_staged) Staging?.Invoke(false);
                _staged = false;
            }

            if (_cleanRt != null) { _cleanRt.Release(); Destroy(_cleanRt); }
            if (_uiRt != null) { _uiRt.Release(); Destroy(_uiRt); }
        }

        private Dictionary<string, object> Meta()
        {
            var m = new Dictionary<string, object>
            {
                ["id"] = _id, ["folder"] = _folder, ["width"] = _w, ["height"] = _h, ["fps"] = _fps, ["frames"] = _frames,
                ["duration"] = Round(_frames / (float)_fps), ["mode"] = _offline ? "offline" : "realtime",
                ["clean"] = "clean.mp4", ["ui"] = _wantUi ? "ui/ui_%05d.png" : null,
                ["audio"] = _wav != null ? "audio.wav" : null, ["audioRate"] = AudioSettings.outputSampleRate,
                ["audioChannels"] = _wav?.Channels ?? 0, ["audioSource"] = _audioOffline ? "AudioRenderer" : _tap != null ? "listener" : "none",
                ["solo"] = _solo, ["camera"] = _rig?.CameraSpec, ["canvases"] = _rig?.CanvasNames ?? new List<object>(),
                ["stems"] = _stemCounts.ToDictionary(k => k.Key, k => (object)k.Value), ["events"] = "events.jsonl",
                ["finished"] = _finished, ["created"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            };
            string ffErr = (_cleanSink?.Errors ?? "") + (_uiSink?.Errors ?? "");
            if (ffErr.Length > 0) m["ffmpegLog"] = ffErr;
            if (_error != null) m["error"] = _error;
            return m;
        }

        private void StatusInto(Dictionary<string, object> r)
        {
            r["take"] = !_stopping;
            r["id"] = _id;
            r["t"] = Round(TakeTime);
            r["frames"] = _frames;
            r["size"] = _w + "x" + _h;
            r["fps"] = _fps;
            r["mode"] = _offline ? "offline" : "realtime";
            r["camera"] = _rig?.CameraSpec;
            if (_error != null) r["error"] = _error;
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            if (_finished) return;
            // Destroyed without stop (player quitting): close the encoders so the files are complete, then put the game back.
            _stopping = true;
            try { Drain(true); } catch (Exception) { }
            _cleanSink?.Close();
            _uiSink?.Close();
            _cleanSink?.Join(30000);
            _uiSink?.Join(30000);
            try { Finish(); } catch (Exception) { }
        }

        // ------------------------------------------------------------------ events and audio stems

        private void Event(string kind, string text)
        {
            if (_events == null || string.IsNullOrEmpty(text)) return;
            if (text.Length > 600) text = text.Substring(0, 600);
            try
            {
                _events.WriteLine(AgentPlayJson.Write(new Dictionary<string, object>
                {
                    ["t"] = Round(TakeTime), ["frame"] = _frames, ["kind"] = kind, ["text"] = text,
                }));
            }
            catch (IOException) { }
        }

        private void Sample(Func<string> provider, ref string last, string kind)
        {
            if (provider == null) return;
            string now;
            try { now = provider() ?? ""; }
            catch (Exception) { return; }
            if (now == last) return;
            last = now;
            Event(kind, now.Length > 0 ? now : "(none)");
        }

        private void ApplySolo()
        {
            if (_solo == null) return;
            foreach (AudioSource src in FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (_muted.ContainsKey(src)) continue;
                string stem = Stem(src, _stemRules);
                _stemCounts[stem] = _stemCounts.TryGetValue(stem, out int n) ? n + 1 : 1;
                _muted[src] = src.mute;
                if (stem != _solo) src.mute = true;
            }
        }

        private static readonly Dictionary<string, string[]> DefaultRules = new Dictionary<string, string[]>
        {
            ["voice"] = new[] { "voice", "dialog", "dialogue", "babble", "speech", "vo_" },
            ["music"] = new[] { "music", "bgm", "song", "score" },
            ["ambience"] = new[] { "ambience", "ambient", "roomtone", "room tone", "wind", "rain" },
        };

        private static Dictionary<string, List<string>> Rules(Dictionary<string, object> c)
        {
            var rules = new Dictionary<string, List<string>>();
            if (c.TryGetValue("stemRules", out object v) && v is Dictionary<string, object> d)
                foreach (var kv in d)
                    if (kv.Value is List<object> words) rules[kv.Key] = words.Select(w => Convert.ToString(w, CultureInfo.InvariantCulture).ToLowerInvariant()).ToList();
            foreach (var kv in DefaultRules)
                if (!rules.ContainsKey(kv.Key)) rules[kv.Key] = kv.Value.ToList();
            return rules;
        }

        /// <summary>The stem of a source: the project hook, else the first rule whose word is in its mixer group, path or clip name; else "sfx".</summary>
        public static string Stem(AudioSource src, Dictionary<string, List<string>> rules)
        {
            if (StemOf != null)
            {
                try
                {
                    string s = StemOf(src);
                    if (!string.IsNullOrEmpty(s)) return s;
                }
                catch (Exception e) { Debug.LogException(e); }
            }

            string hay = ((src.outputAudioMixerGroup != null ? src.outputAudioMixerGroup.name : "") + " | " + PathOf(src.transform) + " | " +
                          (src.clip != null ? src.clip.name : "")).ToLowerInvariant();
            foreach (var kv in rules)
                foreach (string word in kv.Value)
                    if (word.Length > 0 && hay.Contains(word)) return kv.Key;
            return "sfx";
        }

        private static List<object> DescribeStems(Dictionary<string, List<string>> rules)
        {
            var list = new List<object>();
            foreach (AudioSource src in FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                list.Add(new Dictionary<string, object>
                {
                    ["path"] = PathOf(src.transform), ["clip"] = src.clip != null ? src.clip.name : null, ["stem"] = Stem(src, rules),
                    ["mixer"] = src.outputAudioMixerGroup != null ? src.outputAudioMixerGroup.name : null, ["playing"] = src.isPlaying,
                });
            return list;
        }

        // ------------------------------------------------------------------ still

        private static IEnumerator Still(Dictionary<string, object> c, Dictionary<string, object> r, Action<string> fail)
        {
            string folder = AgentPlayOps.S(c, "folder", null);
            if (string.IsNullOrEmpty(folder)) { fail("studio still needs folder"); yield break; }
            int w = Mathf.Clamp(AgentPlayOps.I(c, "w", 1920), 16, 7680), h = Mathf.Clamp(AgentPlayOps.I(c, "h", 1080), 16, 7680);
            bool ownRig = Active == null;
            StudioRig rig = ownRig ? new StudioRig() : Active._rig;
            if (ownRig)
            {
                string err = rig.Setup(c, w, h);
                if (err != null) { rig.Dispose(); fail(err); yield break; }
                Staging?.Invoke(true);
            }

            try
            {
                Directory.CreateDirectory(folder);
                yield return new WaitForEndOfFrame();
                rig.Rescan();
                rig.Advance(0f, 0f);
                string name = AgentPlayOps.S(c, "name", "still");
                r["clean"] = SavePng(rt => rig.RenderClean(rt), w, h, Path.Combine(folder, name + "_clean.png"));
                if (AgentPlayOps.B(c, "ui", true)) r["ui"] = SavePng(rt => rig.RenderUi(rt), w, h, Path.Combine(folder, name + "_ui.png"));
                r["size"] = w + "x" + h;
                r["camera"] = rig.CameraSpec;
            }
            finally
            {
                if (ownRig)
                {
                    rig.Dispose();
                    Staging?.Invoke(false);
                }
            }
        }

        private static string SavePng(Action<RenderTexture> render, int w, int h, string path)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                render(rt);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                tex.Apply(false);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                return path;
            }
            finally
            {
                RenderTexture.active = previous;
                rt.Release();
                Destroy(rt);
                Destroy(tex);
            }
        }

        // ------------------------------------------------------------------ helpers

        internal static List<string> Strings(Dictionary<string, object> c, string key)
        {
            if (!c.TryGetValue(key, out object v) || v == null) return new List<string>();
            if (v is List<object> list) return list.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).ToList();
            return new List<string> { Convert.ToString(v, CultureInfo.InvariantCulture) };
        }

        internal static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (Transform p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        private static int SpeakerChannels()
        {
            switch (AudioSettings.speakerMode)
            {
                case AudioSpeakerMode.Mono: return 1;
                case AudioSpeakerMode.Quad: return 4;
                case AudioSpeakerMode.Surround: return 5;
                case AudioSpeakerMode.Mode5point1: return 6;
                case AudioSpeakerMode.Mode7point1: return 8;
                default: return 2;
            }
        }

        private static float Round(float v) => Mathf.Round(v * 1000f) / 1000f;

        // ------------------------------------------------------------------ encoder: raw RGBA frames into ffmpeg on a worker thread

        private sealed class FrameSink
        {
            private readonly Process _proc;
            private readonly Stream _stdin;
            private readonly Thread _thread;
            private readonly Queue<(byte[] data, int copies)> _queue = new Queue<(byte[], int)>();
            private readonly Stack<byte[]> _pool = new Stack<byte[]>();
            private readonly object _gate = new object();
            private readonly StringBuilder _err = new StringBuilder();
            private bool _closed;

            public bool Done => !_thread.IsAlive;
            public string Errors { get { lock (_gate) return _err.ToString(); } }

            public FrameSink(string ffmpeg, string args)
            {
                var info = new ProcessStartInfo(ffmpeg, args)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true,
                };
                _proc = new Process { StartInfo = info };
                _proc.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    lock (_gate) if (_err.Length < 4000) _err.AppendLine(e.Data);
                };
                _proc.Start();
                _proc.BeginErrorReadLine();
                _stdin = _proc.StandardInput.BaseStream;
                _thread = new Thread(Work) { IsBackground = true, Name = "ContentStudioSink" };
                _thread.Start();
            }

            public void Write(NativeArray<byte> data, int copies)
            {
                byte[] buffer;
                lock (_gate) buffer = _pool.Count > 0 ? _pool.Pop() : null;
                if (buffer == null || buffer.Length != data.Length) buffer = new byte[data.Length];
                data.CopyTo(buffer);
                lock (_gate)
                {
                    _queue.Enqueue((buffer, copies));
                    Monitor.Pulse(_gate);
                }
            }

            public void Close()
            {
                lock (_gate)
                {
                    if (_closed) return;
                    _closed = true;
                    _queue.Enqueue((null, 0));
                    Monitor.Pulse(_gate);
                }
            }

            public void Join(int ms) => _thread.Join(ms);

            private void Work()
            {
                while (true)
                {
                    (byte[] data, int copies) item;
                    lock (_gate)
                    {
                        while (_queue.Count == 0) Monitor.Wait(_gate);
                        item = _queue.Dequeue();
                    }

                    if (item.data == null) break;
                    try
                    {
                        for (int i = 0; i < item.copies; i++) _stdin.Write(item.data, 0, item.data.Length);
                    }
                    catch (Exception e)
                    {
                        lock (_gate) if (_err.Length < 4000) _err.AppendLine("write failed: " + e.Message);
                        break;
                    }

                    lock (_gate) _pool.Push(item.data);
                }

                try { _stdin.Flush(); _stdin.Dispose(); } catch (IOException) { }
                try
                {
                    if (!_proc.WaitForExit(240000)) { _proc.Kill(); lock (_gate) _err.AppendLine("ffmpeg did not finish within 240 s"); }
                    else if (_proc.ExitCode != 0) lock (_gate) _err.AppendLine("ffmpeg exited with code " + _proc.ExitCode);
                }
                catch (Exception e) { lock (_gate) _err.AppendLine("ffmpeg: " + e.Message); }
                _proc.Dispose();
            }
        }

        // ------------------------------------------------------------------ audio: 32-bit float WAV

        private sealed class WavWriter
        {
            private readonly FileStream _fs;
            private readonly BinaryWriter _bw;
            private long _samples;
            public readonly int Channels, Rate;

            public WavWriter(string path, int rate, int channels)
            {
                Rate = rate;
                Channels = channels;
                _fs = new FileStream(path, FileMode.Create, FileAccess.Write);
                _bw = new BinaryWriter(_fs);
                Header();
            }

            private void Header()
            {
                long dataBytes = _samples * 4;
                _bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                _bw.Write((uint)(36 + dataBytes));
                _bw.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                _bw.Write(16u);
                _bw.Write((ushort)3); // IEEE float
                _bw.Write((ushort)Channels);
                _bw.Write((uint)Rate);
                _bw.Write((uint)(Rate * Channels * 4));
                _bw.Write((ushort)(Channels * 4));
                _bw.Write((ushort)32);
                _bw.Write(Encoding.ASCII.GetBytes("data"));
                _bw.Write((uint)dataBytes);
            }

            public void Write(NativeArray<float> data)
            {
                for (int i = 0; i < data.Length; i++) _bw.Write(data[i]);
                _samples += data.Length;
            }

            public void Write(float[] data, int channels)
            {
                if (channels == Channels)
                {
                    for (int i = 0; i < data.Length; i++) _bw.Write(data[i]);
                    _samples += data.Length;
                    return;
                }

                // Another channel count than the speaker mode: fold or spread into ours.
                int frames = data.Length / Math.Max(1, channels);
                for (int f = 0; f < frames; f++)
                    for (int ch = 0; ch < Channels; ch++)
                        _bw.Write(data[f * channels + Math.Min(ch, channels - 1)]);
                _samples += frames * Channels;
            }

            public void Close()
            {
                try
                {
                    _bw.Flush();
                    _fs.Seek(0, SeekOrigin.Begin);
                    Header();
                    _bw.Flush();
                }
                finally
                {
                    _bw.Dispose();
                }
            }
        }

        /// <summary>Real-time takes: copies what the active listener hears (the final mix) on the audio thread.</summary>
        private sealed class ListenerTap : MonoBehaviour
        {
            private readonly List<float> _buffer = new List<float>(48000 * 2);
            private readonly object _gate = new object();
            private int _channels = 2;

            private void OnAudioFilterRead(float[] data, int channels)
            {
                lock (_gate)
                {
                    _channels = channels;
                    if (_buffer.Count < 48000 * 8 * 10) _buffer.AddRange(data);
                }
            }

            public float[] Take(out int channels)
            {
                lock (_gate)
                {
                    channels = _channels;
                    if (_buffer.Count == 0) return null;
                    float[] copy = _buffer.ToArray();
                    _buffer.Clear();
                    return copy;
                }
            }
        }
    }

    /// <summary>
    /// The cameras and UI of a take or still: the shot camera (the player's eye or a studio camera that copies its pipeline
    /// settings), a private UI camera that draws the overlay canvases into their own transparent layer at the output size,
    /// and objects hidden for the shot. <see cref="Dispose"/> puts every canvas, camera and renderer back as it was.
    /// </summary>
    internal sealed class StudioRig : IDisposable
    {
        private sealed class CanvasState { public RenderMode Mode; public Camera Cam; public float Plane; public bool Enabled; }

        private int _w, _h;
        private Camera _player, _studio, _ui;
        private GameObject _root;
        private Dictionary<string, object> _spec;
        private string _mode = "player";
        private float _fov, _shakeAmount, _shakeFreq, _seed;
        private readonly Dictionary<Canvas, CanvasState> _canvases = new Dictionary<Canvas, CanvasState>();
        private readonly List<string> _hideNames = new List<string>(), _hideUiNames = new List<string>();
        private readonly Dictionary<Renderer, bool> _hiddenRenderers = new Dictionary<Renderer, bool>();
        private readonly Dictionary<CanvasRenderer, bool> _hiddenUi = new Dictionary<CanvasRenderer, bool>();
        private readonly List<AudioListener> _listenersOff = new List<AudioListener>();
        private AudioListener _studioListener;
        private int _uiMask;
        private Vector3 _pos;
        private Quaternion _rot;
        private bool _hasPose;

        public Dictionary<string, object> CameraSpec => _spec;
        public List<object> CanvasNames => _canvases.Keys.Where(k => k != null).Select(k => (object)k.name).ToList();

        public string Setup(Dictionary<string, object> c, int w, int h)
        {
            _w = w;
            _h = h;
            _seed = UnityEngine.Random.value * 100f;
            _root = new GameObject("ContentStudioRig");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _ui = NewCamera("StudioUiCamera");
            _ui.clearFlags = CameraClearFlags.SolidColor;
            _ui.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _ui.allowHDR = false;
            _ui.allowMSAA = false;
            _ui.nearClipPlane = 0.1f;
            _ui.farClipPlane = 100f;
            _ui.transform.position = new Vector3(0f, -20000f, 0f);
            _ui.cullingMask = 1 << 5;
            _hideNames.AddRange(ContentStudio.Strings(c, "hide"));
            _hideUiNames.AddRange(ContentStudio.Strings(c, "hideUi"));
            Rescan();
            object cam = c.TryGetValue("camera", out object v) ? v : null;
            return SetCamera(cam as Dictionary<string, object> ?? new Dictionary<string, object> { ["mode"] = "player" });
        }

        private Camera NewCamera(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            return cam;
        }

        private Camera PlayerCamera()
        {
            Camera cam = AgentPlayOps.ViewCamera != null ? AgentPlayOps.ViewCamera() : null;
            if (cam == null) cam = Camera.main;
            if (cam == null) cam = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault(k => k.enabled && k.targetTexture == null && k != _ui && k != _studio);
            return cam;
        }

        // ------------------------------------------------------------------ camera

        /// <summary>
        /// Camera spec: mode player | pose | face | follow | path; pos [x,y,z]; lookAt [x,y,z] | "Name" | {target, bone, offset};
        /// target, bone, distance, height, offset, local (follow: offset in the target's space); keys [{t, pos, lookAt, fov,
        /// ease: linear|smooth|in|out}]; fov; shake {amount: degrees, freq: Hz}; listener: "camera" hears from the shot camera.
        /// </summary>
        public string SetCamera(Dictionary<string, object> spec)
        {
            spec = spec ?? new Dictionary<string, object>();
            string mode = AgentPlayOps.S(spec, "mode", "player");
            if (mode != "player" && mode != "pose" && mode != "face" && mode != "follow" && mode != "path")
                return "camera mode is player, pose, face, follow or path (got '" + mode + "')";
            _player = PlayerCamera();
            if (_player == null) return "no camera to render (no main camera)";
            _spec = spec;
            _mode = mode;
            _fov = AgentPlayOps.F(spec, "fov", 0f);
            _shakeAmount = 0f;
            _shakeFreq = 0.7f;
            if (spec.TryGetValue("shake", out object sh) && sh is Dictionary<string, object> shake)
            {
                _shakeAmount = AgentPlayOps.F(shake, "amount", 0.5f);
                _shakeFreq = AgentPlayOps.F(shake, "freq", 0.7f);
            }
            else if (sh is double amount) _shakeAmount = (float)amount;

            _hasPose = false;
            if (mode != "player")
            {
                if (_studio == null) _studio = NewCamera("StudioShotCamera");
                CopyCamera(_player, _studio);
                if ((mode == "face" || mode == "follow") && Target(spec) == null) return "camera target '" + AgentPlayOps.S(spec, "target", "") + "' not found";
            }

            SetListener(AgentPlayOps.S(spec, "listener", "player") == "camera" && mode != "player");
            Rehide();
            return null;
        }

        private static void CopyCamera(Camera from, Camera to)
        {
            to.CopyFrom(from);
            to.targetTexture = null;
            to.enabled = false;
            // The render pipeline's per-camera settings (URP/HDRP additional data: post-processing, anti-aliasing, renderer),
            // copied by serialization so this package needs no pipeline reference; a copied camera stack is cleared, since
            // overlay cameras (a first-person arm) belong to the player's eye, not to an outside shot.
            foreach (MonoBehaviour comp in from.GetComponents<MonoBehaviour>())
            {
                if (comp == null) continue;
                Type t = comp.GetType();
                if (t.Name != "UniversalAdditionalCameraData" && t.Name != "HDAdditionalCameraData") continue;
                Component dst = to.GetComponent(t) ?? to.gameObject.AddComponent(t);
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(comp), dst);
                try
                {
                    if (t.GetProperty("cameraStack")?.GetValue(dst) is IList stack) stack.Clear();
                }
                catch (Exception) { }
            }
        }

        private void SetListener(bool onCamera)
        {
            if (onCamera && _studioListener == null)
            {
                foreach (AudioListener l in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                    if (l.enabled) { l.enabled = false; _listenersOff.Add(l); }
                _studioListener = _studio.gameObject.AddComponent<AudioListener>();
            }
            else if (!onCamera && _studioListener != null)
            {
                UnityEngine.Object.Destroy(_studioListener);
                _studioListener = null;
                foreach (AudioListener l in _listenersOff) if (l != null) l.enabled = true;
                _listenersOff.Clear();
            }
        }

        private Transform Target(Dictionary<string, object> spec)
        {
            string name = AgentPlayOps.S(spec, "target", null);
            if (string.IsNullOrEmpty(name)) return null;
            GameObject go = AgentPlayOps.FindObject(name, false);
            return go != null ? go.transform : null;
        }

        /// <summary>A bone of a character: the humanoid bone, else the first child whose name contains it (head, neck...).</summary>
        private static Transform Bone(Transform root, string bone)
        {
            if (root == null || string.IsNullOrEmpty(bone)) return root;
            Animator anim = root.GetComponentInChildren<Animator>();
            if (anim != null && anim.isHuman && Enum.TryParse(bone, true, out HumanBodyBones hb))
            {
                Transform t = anim.GetBoneTransform(hb);
                if (t != null) return t;
            }

            string b = bone.ToLowerInvariant();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.ToLowerInvariant().Contains(b)) return t;
            return root;
        }

        private static Vector3? Vec(object v)
        {
            if (v is List<object> l && l.Count >= 3)
                return new Vector3(Convert.ToSingle(l[0], CultureInfo.InvariantCulture), Convert.ToSingle(l[1], CultureInfo.InvariantCulture), Convert.ToSingle(l[2], CultureInfo.InvariantCulture));
            return null;
        }

        /// <summary>A point from [x,y,z], an object name (its focus point), or {target, bone, offset}.</summary>
        private static Vector3? Point(object v)
        {
            Vector3? p = Vec(v);
            if (p.HasValue) return p;
            if (v is string name)
            {
                GameObject go = AgentPlayOps.FindObject(name, false);
                return go != null ? AgentPlayOps.Focus(go) : (Vector3?)null;
            }

            if (v is Dictionary<string, object> d)
            {
                GameObject go = AgentPlayOps.FindObject(AgentPlayOps.S(d, "target", ""), false);
                if (go == null) return null;
                Transform bone = Bone(go.transform, AgentPlayOps.S(d, "bone", null));
                Vector3 baseP = bone != go.transform ? bone.position : AgentPlayOps.Focus(go);
                Vector3 off = Vec(d.TryGetValue("offset", out object o) ? o : null) ?? Vector3.zero;
                return baseP + (AgentPlayOps.B(d, "local", false) ? go.transform.rotation * off : off);
            }

            return null;
        }

        /// <summary>Moves the shot camera for this frame (time t of the take).</summary>
        public void Advance(float dt, float t)
        {
            if (_mode == "player" || _studio == null) return;
            Vector3 pos = _hasPose ? _pos : _player.transform.position;
            Quaternion rot = _hasPose ? _rot : _player.transform.rotation;
            float fov = _fov > 0f ? _fov : _player.fieldOfView;
            Dictionary<string, object> s = _spec;
            switch (_mode)
            {
                case "pose":
                {
                    pos = Vec(s.TryGetValue("pos", out object p) ? p : null) ?? pos;
                    Vector3? look = Point(s.TryGetValue("lookAt", out object l) ? l : null);
                    if (look.HasValue && (look.Value - pos).sqrMagnitude > 1e-6f) rot = Quaternion.LookRotation(look.Value - pos);
                    else if (Vec(s.TryGetValue("euler", out object e) ? e : null) is Vector3 euler) rot = Quaternion.Euler(euler);
                    break;
                }
                case "face":
                {
                    // In front of a character's face, looking into it: the "staring into the lens" shot.
                    Transform target = Target(s);
                    if (target == null) break;
                    Transform head = Bone(target, AgentPlayOps.S(s, "bone", "Head"));
                    Vector3 eye = head.position + Vector3.up * AgentPlayOps.F(s, "eyes", 0.06f);
                    Vector3 fwd = Vector3.ProjectOnPlane(target.forward, Vector3.up).normalized;
                    if (fwd.sqrMagnitude < 0.01f) fwd = target.forward;
                    float yaw = AgentPlayOps.F(s, "yaw", 0f);
                    fwd = Quaternion.AngleAxis(yaw, Vector3.up) * fwd;
                    pos = eye + fwd * AgentPlayOps.F(s, "distance", 0.7f) + Vector3.up * AgentPlayOps.F(s, "height", 0f);
                    rot = Quaternion.LookRotation(eye - pos);
                    break;
                }
                case "follow":
                {
                    Transform target = Target(s);
                    if (target == null) break;
                    Vector3 off = Vec(s.TryGetValue("offset", out object o) ? o : null) ?? new Vector3(0f, 1.6f, -2f);
                    Vector3 want = AgentPlayOps.B(s, "local", true) ? target.TransformPoint(off) : target.position + off;
                    float smooth = AgentPlayOps.F(s, "smooth", 0.15f);
                    pos = _hasPose && smooth > 0f ? Vector3.Lerp(_pos, want, 1f - Mathf.Exp(-dt / smooth)) : want;
                    Vector3 look = Point(s.TryGetValue("lookAt", out object l) ? l : null) ?? AgentPlayOps.Focus(target.gameObject);
                    if ((look - pos).sqrMagnitude > 1e-6f) rot = Quaternion.LookRotation(look - pos);
                    break;
                }
                case "path":
                    PathPose(t, ref pos, ref rot, ref fov);
                    break;
            }

            _pos = pos;
            _rot = rot;
            _hasPose = true;
            if (_shakeAmount > 0f)
            {
                // Hand-held: smooth noise on pitch, yaw and roll (amount in degrees, freq in Hz), the same for a given take.
                float ft = t * _shakeFreq;
                var shake = new Vector3(Mathf.PerlinNoise(_seed, ft) - 0.5f, Mathf.PerlinNoise(_seed + 17f, ft) - 0.5f,
                                        (Mathf.PerlinNoise(_seed + 41f, ft) - 0.5f) * 0.4f) * (2f * _shakeAmount);
                rot *= Quaternion.Euler(shake);
            }

            _studio.transform.SetPositionAndRotation(pos, rot);
            _studio.fieldOfView = fov;
        }

        private void PathPose(float t, ref Vector3 pos, ref Quaternion rot, ref float fov)
        {
            if (!_spec.TryGetValue("keys", out object k) || !(k is List<object> list) || list.Count == 0) return;
            var keys = list.OfType<Dictionary<string, object>>().OrderBy(d => AgentPlayOps.F(d, "t", 0f)).ToList();
            Dictionary<string, object> a = keys[0], b = keys[0];
            for (int i = 0; i < keys.Count; i++)
            {
                if (AgentPlayOps.F(keys[i], "t", 0f) <= t) a = keys[i];
                if (AgentPlayOps.F(keys[i], "t", 0f) >= t) { b = keys[i]; break; }
                b = keys[i];
            }

            float ta = AgentPlayOps.F(a, "t", 0f), tb = AgentPlayOps.F(b, "t", 0f);
            float u = tb > ta ? Mathf.Clamp01((t - ta) / (tb - ta)) : 1f;
            u = Ease(AgentPlayOps.S(b, "ease", "smooth"), u);
            Vector3 pa = KeyPos(a) ?? pos, pb = KeyPos(b) ?? pa;
            pos = Vector3.LerpUnclamped(pa, pb, u);
            Quaternion ra = KeyRot(a, pa) ?? rot, rb = KeyRot(b, pb) ?? ra;
            rot = Quaternion.Slerp(ra, rb, u);
            float fa = AgentPlayOps.F(a, "fov", fov), fb = AgentPlayOps.F(b, "fov", fa);
            fov = Mathf.Lerp(fa, fb, u);
        }

        private static Vector3? KeyPos(Dictionary<string, object> key) => Point(key.TryGetValue("pos", out object p) ? p : null);

        private static Quaternion? KeyRot(Dictionary<string, object> key, Vector3 pos)
        {
            Vector3? look = Point(key.TryGetValue("lookAt", out object l) ? l : null);
            if (look.HasValue && (look.Value - pos).sqrMagnitude > 1e-6f) return Quaternion.LookRotation(look.Value - pos);
            Vector3? euler = Vec(key.TryGetValue("euler", out object e) ? e : null);
            return euler.HasValue ? Quaternion.Euler(euler.Value) : (Quaternion?)null;
        }

        /// <summary>Easing by name (recipe data, editable per key): linear, smooth (in-out), in, out.</summary>
        private static float Ease(string name, float u)
        {
            switch (name)
            {
                case "linear": return u;
                case "in": return u * u * u;
                case "out": return 1f - Mathf.Pow(1f - u, 3f);
                default: return u * u * (3f - 2f * u);
            }
        }

        // ------------------------------------------------------------------ render

        public void RenderClean(RenderTexture rt)
        {
            Camera cam = _mode == "player" || _studio == null ? PlayerCamera() : _studio;
            if (cam == null) return;
            float oldFov = cam.fieldOfView;
            if (_mode == "player" && _fov > 0f) cam.fieldOfView = _fov;
            cam.aspect = rt.width / (float)rt.height;
            try { Render(cam, rt); }
            finally
            {
                cam.ResetAspect();
                if (_mode == "player") cam.fieldOfView = oldFov;
            }
        }

        public void RenderUi(RenderTexture rt)
        {
            RenderTexture old = _ui.targetTexture;
            _ui.targetTexture = rt; // a screen-space-camera canvas lays itself out for its camera's target: this aspect ratio
            try
            {
                Canvas.ForceUpdateCanvases();
                Render(_ui, rt);
            }
            finally { _ui.targetTexture = old; }
        }

        private static void Render(Camera cam, RenderTexture rt)
        {
#if UNITY_2023_1_OR_NEWER
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                RenderPipeline.SubmitRenderRequest(cam, request);
                return;
            }
#endif
            RenderTexture old = cam.targetTexture;
            cam.targetTexture = rt;
            try { cam.Render(); }
            finally { cam.targetTexture = old; }
        }

        // ------------------------------------------------------------------ canvases and hidden objects

        /// <summary>Moves new overlay canvases onto the UI camera and re-applies the hide lists (objects appear during a take).</summary>
        public void Rescan()
        {
            foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (canvas == null || !canvas.isRootCanvas || _canvases.ContainsKey(canvas)) continue;
                if (canvas.renderMode == RenderMode.WorldSpace) continue;
                if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera != null && canvas.worldCamera.targetTexture != null) continue;
                var state = new CanvasState { Mode = canvas.renderMode, Cam = canvas.worldCamera, Plane = canvas.planeDistance, Enabled = canvas.enabled };
                _canvases[canvas] = state;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = _ui;
                canvas.planeDistance = 10f;
                _uiMask |= 1 << canvas.gameObject.layer;
                if (Matches(canvas.transform, _hideUiNames)) canvas.enabled = false;
            }

            _ui.cullingMask = _uiMask == 0 ? 1 << 5 : _uiMask;
            Rehide();
        }

        public List<object> Hide(List<string> names)
        {
            _hideNames.AddRange(names);
            Rehide();
            return _hideNames.Cast<object>().ToList();
        }

        private void Rehide()
        {
            if (_hideNames.Count > 0)
                foreach (Renderer r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (r == null || _hiddenRenderers.ContainsKey(r) || !Matches(r.transform, _hideNames)) continue;
                    _hiddenRenderers[r] = r.forceRenderingOff;
                    r.forceRenderingOff = true;
                }

            if (_hideUiNames.Count > 0)
                foreach (CanvasRenderer cr in UnityEngine.Object.FindObjectsByType<CanvasRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (cr == null || _hiddenUi.ContainsKey(cr) || !Matches(cr.transform, _hideUiNames)) continue;
                    _hiddenUi[cr] = cr.cull;
                    cr.cull = true;
                }
        }

        /// <summary>True when the object or one of its parents is named like one of the names (case-insensitive, part of the name).</summary>
        private static bool Matches(Transform t, List<string> names)
        {
            for (Transform p = t; p != null; p = p.parent)
                foreach (string n in names)
                    if (n.Length > 0 && p.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public void Dispose()
        {
            foreach (var kv in _canvases)
            {
                Canvas c = kv.Key;
                if (c == null) continue;
                c.renderMode = kv.Value.Mode;
                c.worldCamera = kv.Value.Cam;
                c.planeDistance = kv.Value.Plane;
                c.enabled = kv.Value.Enabled;
            }

            _canvases.Clear();
            foreach (var kv in _hiddenRenderers) if (kv.Key != null) kv.Key.forceRenderingOff = kv.Value;
            _hiddenRenderers.Clear();
            foreach (var kv in _hiddenUi) if (kv.Key != null) kv.Key.cull = kv.Value;
            _hiddenUi.Clear();
            SetListener(false);
            if (_root != null) UnityEngine.Object.Destroy(_root);
        }
    }
}
#endif
