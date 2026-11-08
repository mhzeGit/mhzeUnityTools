#if DEVELOPMENT_BUILD || UNITY_EDITOR || AGENT_PLAY
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
#if AGENTIC_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace Gigaduck.AgenticTools.AgentPlay
{
    /// <summary>A harness op: reads <c>command</c>, fills <c>result</c>, calls <c>fail</c> with a reason on failure.</summary>
    public delegate IEnumerator AgentPlayOp(Dictionary<string, object> command, Dictionary<string, object> result, Action<string> fail);

    /// <summary>
    /// Command server that lets an agent drive a standalone player without the Unity editor (see <c>Docs/AGENT_PLAY.md</c> and
    /// <c>tools/agentplay/agentplay.py</c>): load a build scene, move the player, press keys, take pictures from any camera pose
    /// and collect console errors. It only wakes up when the player was started with <c>-agent &lt;name&gt;</c>; otherwise it does
    /// nothing, and it is compiled only into editor, Development and agent builds (<c>AGENT_PLAY</c>, set by <c>AgentPlayBuild</c>
    /// in the editor assembly). Commands arrive as JSON files in <c>&lt;out&gt;/inbox</c>, answers appear in <c>&lt;out&gt;/outbox</c>,
    /// pictures in <c>&lt;out&gt;/shots</c>, and a heartbeat in <c>&lt;out&gt;/heartbeat.txt</c>. Each instance has its own folder, so
    /// several players can run at once. A project adds ops with <see cref="RegisterOp"/> (a registered op replaces a built-in one
    /// of the same name), status fields with <see cref="StatusExtras"/>, and redirects its save folder in <see cref="Starting"/>.
    /// A project with its own complete harness marks its assembly with <see cref="CustomAgentPlayHarnessAttribute"/> and this
    /// one stays off.
    /// </summary>
    public sealed class AgentPlayHarness : MonoBehaviour
    {
        private static readonly Dictionary<string, AgentPlayOp> Ops = new Dictionary<string, AgentPlayOp>();

        /// <summary>Adds or replaces an op. Call it from a <c>[RuntimeInitializeOnLoadMethod]</c> of the project.</summary>
        public static void RegisterOp(string name, AgentPlayOp op) => Ops[name] = op;

        /// <summary>Adds project fields (level, checkpoint, health...) to every <c>status</c> answer.</summary>
        public static event Action<Dictionary<string, object>> StatusExtras;

        /// <summary>Raised once before the first scene loads with this instance's output folder: point the game's save
        /// folder into it (<c>Path.Combine(folder, "save")</c>) so parallel players never share progress.</summary>
        public static event Action<string> Starting;

        /// <summary>The camera pictures are taken with; default the main camera, else the first enabled camera.</summary>
        public static Func<Camera> CameraProvider;

        /// <summary>The object <c>teleport</c> moves and <c>status</c> reports; default the object tagged Player.</summary>
        public static Func<Transform> PlayerProvider;

        /// <summary>True in a player started by agentplay.py.</summary>
        public static bool IsActive => _out != null;

        /// <summary>This instance's output folder (null when not active).</summary>
        public static string OutputFolder => _out;

        /// <summary>Reports a finding (sev: FATAL, ERR, STALL, SOFT, WARN, INFO) in the summary, counted per key.</summary>
        public static void Report(string sev, string key, string text) => _instance?.Note(sev, key, text, _instance._doing);

        // What a player without a GPU (-nographics) cannot do. These are not game bugs, so they must not read as findings.
        private static readonly string[] NoGpuNoise =
        {
            "RenderTexture.Create", "compute shader", "Blitter", "Assertion failed", "is not supported or has been stripped",
            "SetConstantBuffer", "active render target", "graphics buffer",
        };

        private static string _out;
        private static bool _noGpu;
        private static AgentPlayHarness _instance;
        private readonly Queue<Dictionary<string, object>> _queue = new Queue<Dictionary<string, object>>();
        private readonly Dictionary<string, Issue> _issues = new Dictionary<string, Issue>();
        private readonly object _gate = new object();
        private readonly StringBuilder _console = new StringBuilder();
        private bool _busy, _quit;
        private string _doing = "idle";
        private float _nextPoll, _nextBeat, _frameSum, _worst;
        private int _frames;
        private Vector3 _lastGood;
        private bool _haveGood;
#if AGENTIC_INPUT_SYSTEM
        private Keyboard _keyboard;
        private Mouse _mouse;
        private bool _leftButton;
        private readonly HashSet<Key> _held = new HashSet<Key>();
#endif

        private sealed class Issue { public string Sev, Text, Where; public int Count; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            string[] args = Environment.GetCommandLineArgs();
            string name = Arg(args, "-agent");
            if (name == null || _instance != null) return;
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.IsDefined(typeof(CustomAgentPlayHarnessAttribute), false))) return;

            _out = Arg(args, "-agent-out") ?? Path.Combine(Directory.GetCurrentDirectory(), "agent-" + name);
            Directory.CreateDirectory(_out);
            Directory.CreateDirectory(Path.Combine(_out, "inbox"));
            Directory.CreateDirectory(Path.Combine(_out, "outbox"));
            Directory.CreateDirectory(Path.Combine(_out, "shots"));

            _noGpu = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = int.TryParse(Arg(args, "-agent-fps"), out int fps) ? fps : 30;
            Time.timeScale = float.TryParse(Arg(args, "-agent-scale"), NumberStyles.Float, CultureInfo.InvariantCulture, out float scale) ? scale : 1f;
            try { Starting?.Invoke(_out); }
            catch (Exception e) { Debug.LogException(e); }

            var go = new GameObject("AgentPlayHarness");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AgentPlayHarness>();
            _instance.Begin(args);
        }

        private static string Arg(string[] args, string key)
        {
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                    return i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : "";
            return null;
        }

        private void Begin(string[] args)
        {
            Application.logMessageReceivedThreaded += OnLog;
            string scene = Arg(args, "-agent-scene");
            // The boot command always answers, so agentplay.py start knows the player is ready: it loads the asked scene,
            // or reports the status of the first build scene the player opened by itself.
            if (string.IsNullOrEmpty(scene) || scene == "none") _queue.Enqueue(new Dictionary<string, object> { ["id"] = "boot", ["op"] = "status", ["settle"] = 1.0 });
            else _queue.Enqueue(new Dictionary<string, object> { ["id"] = "boot", ["op"] = "scene", ["name"] = scene });
            string script = Arg(args, "-agent-script");
            if (!string.IsNullOrEmpty(script) && File.Exists(script))
                foreach (var cmd in AgentPlayJson.ParseArray(File.ReadAllText(script))) _queue.Enqueue(cmd);
            Heartbeat();
        }

        private void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            if (_instance == this) _instance = null;
        }

        // ------------------------------------------------------------------ main loop

        private void Update()
        {
            _frames++;
            _frameSum += Time.unscaledDeltaTime;
            _worst = Mathf.Max(_worst, Time.unscaledDeltaTime);
            SampleWorld();
            if (Time.unscaledTime >= _nextBeat) { _nextBeat = Time.unscaledTime + 1f; Heartbeat(); }
            if (_busy) return;
            if (_queue.Count == 0 && Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + 0.2f;
                PollInbox();
            }

            if (_queue.Count > 0) StartCoroutine(Run(_queue.Dequeue()));
        }

        private void PollInbox()
        {
            string[] files = Directory.GetFiles(Path.Combine(_out, "inbox"), "*.json");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string file in files)
            {
                string text;
                try { text = File.ReadAllText(file); File.Delete(file); }
                catch (IOException) { continue; } // still being written; next poll
                try
                {
                    foreach (var cmd in AgentPlayJson.ParseArray(text.TrimStart().StartsWith("[") ? text : "[" + text + "]"))
                    {
                        if (!cmd.ContainsKey("id")) cmd["id"] = Path.GetFileNameWithoutExtension(file);
                        _queue.Enqueue(cmd);
                    }
                }
                catch (Exception e)
                {
                    Reply(Path.GetFileNameWithoutExtension(file), false, "bad command file: " + e.Message, null);
                }
            }
        }

        private IEnumerator Run(Dictionary<string, object> cmd)
        {
            _busy = true;
            string id = S(cmd, "id", "cmd"), op = S(cmd, "op", "");
            _doing = op;
            var result = new Dictionary<string, object>();
            string error = null;
            IEnumerator body = Dispatch(op, cmd, result, e => error = e);
            var stack = new Stack<IEnumerator>();
            stack.Push(body);
            while (stack.Count > 0)
            {
                bool more;
                try { more = stack.Peek().MoveNext(); }
                catch (Exception e) { error = e.GetType().Name + ": " + e.Message; break; }
                if (!more) { stack.Pop(); continue; }
                if (stack.Peek().Current is IEnumerator nested) stack.Push(nested); // ops may yield other ops' enumerators
                else yield return stack.Peek().Current;
            }

            Reply(id, error == null, error, result);
            _doing = "idle";
            _busy = false;
            if (_quit) { Heartbeat("quit"); File.WriteAllText(Path.Combine(_out, "done.txt"), "done"); Application.Quit(0); }
        }

        private IEnumerator Dispatch(string op, Dictionary<string, object> c, Dictionary<string, object> r, Action<string> fail)
        {
            if (Ops.TryGetValue(op, out AgentPlayOp custom))
            {
                yield return custom(c, r, fail);
                yield break;
            }

            switch (op)
            {
                case "status":
                    if (c.ContainsKey("settle")) yield return new WaitForSecondsRealtime(F(c, "settle", 0f));
                    Status(r);
                    break;
                case "scene": yield return LoadScene(S(c, "name", ""), r, fail); break;
                case "teleport": Teleport(c, r, fail); break;
                case "key": yield return PressKey(c, r, fail); break;
                case "hold": SetKey(c, true, fail); break;
                case "release": SetKey(c, false, fail); break;
                case "click": yield return Click(c, fail); break;
                case "look": yield return Look(c, fail); break;
                case "shot": yield return Shot(c, r, fail); break;
                case "shots": yield return Shots(c, r, fail); break;
                case "find": Find(S(c, "name", ""), r); break;
                case "wait": yield return new WaitForSeconds(F(c, "s", 1f)); break;
                case "scale": Time.timeScale = F(c, "value", 1f); r["scale"] = Time.timeScale; break;
                case "errors": r["issues"] = IssueLines(); break;
                case "summary": File.WriteAllText(Path.Combine(_out, "summary.txt"), Summary()); r["file"] = "summary.txt"; break;
                case "quit": File.WriteAllText(Path.Combine(_out, "summary.txt"), Summary()); _quit = true; break;
                default:
                    fail("unknown op '" + op + "' (built in: status, scene, teleport, key, hold, release, click, look, shot, shots, find, wait, " +
                         "scale, errors, summary, quit" + (Ops.Count > 0 ? "; project: " + string.Join(", ", Ops.Keys) : "") + ")");
                    break;
            }
        }

        // ------------------------------------------------------------------ operations

        private IEnumerator LoadScene(string name, Dictionary<string, object> r, Action<string> fail)
        {
            if (name.Length == 0) { fail("scene needs a 'name'"); yield break; }
            AsyncOperation load = SceneManager.LoadSceneAsync(name, LoadSceneMode.Single);
            if (load == null) { fail("scene '" + name + "' is not in the build"); yield break; }
            while (!load.isDone) yield return null;
            _haveGood = false;
            yield return new WaitForSeconds(0.5f);
            Status(r);
        }

        private static Transform Player()
        {
            if (PlayerProvider != null) return PlayerProvider();
            GameObject tagged = GameObject.FindWithTag("Player");
            return tagged != null ? tagged.transform : null;
        }

        private void Teleport(Dictionary<string, object> c, Dictionary<string, object> r, Action<string> fail)
        {
            Transform player = c.ContainsKey("name") ? FindByName(S(c, "name", "")) : Player();
            if (player == null) { fail("no player in this scene (tag it Player, set AgentPlayHarness.PlayerProvider, or pass 'name')"); return; }
            var controller = player.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (wasEnabled) controller.enabled = false; // a CharacterController would undo the move
            player.SetPositionAndRotation(V3(c, "pos", player.position), Quaternion.Euler(0f, F(c, "yaw", player.eulerAngles.y), 0f));
            var body = player.GetComponent<Rigidbody>();
            if (body != null) { body.position = player.position; body.rotation = player.rotation; }
            if (wasEnabled) controller.enabled = true;
            Physics.SyncTransforms();
            _haveGood = false;
            Status(r);
        }

        private static Transform FindByName(string name)
        {
            foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == name) return t;
            return null;
        }

#if AGENTIC_INPUT_SYSTEM
        private void EnsureInput()
        {
            if (_keyboard != null) return;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _keyboard = InputSystem.AddDevice<Keyboard>("AgentKeyboard");
            _keyboard.MakeCurrent();
            _mouse = InputSystem.AddDevice<Mouse>("AgentMouse");
            _mouse.MakeCurrent();
        }

        private static bool ParseKey(Dictionary<string, object> c, Action<string> fail, out Key key)
        {
            if (Enum.TryParse(S(c, "key", ""), true, out key) && key != Key.None) return true;
            fail("unknown key '" + S(c, "key", "") + "' (Input System names: W, E, Space, LeftShift ...)");
            return false;
        }

        private void SendKeys() => InputSystem.QueueStateEvent(_keyboard, new KeyboardState(_held.ToArray()));

        private void SendMouse(Vector2 delta)
        {
            var state = new MouseState { position = _mouse.position.ReadValue(), delta = delta };
            InputSystem.QueueStateEvent(_mouse, state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left, _leftButton));
        }

        private IEnumerator PressKey(Dictionary<string, object> c, Dictionary<string, object> r, Action<string> fail)
        {
            if (!ParseKey(c, fail, out Key key)) yield break;
            EnsureInput();
            _held.Add(key);
            SendKeys();
            yield return new WaitForSecondsRealtime(F(c, "s", 0.12f));
            _held.Remove(key);
            SendKeys();
            yield return null;
            Status(r);
        }

        private void SetKey(Dictionary<string, object> c, bool down, Action<string> fail)
        {
            if (!ParseKey(c, fail, out Key key)) return;
            EnsureInput();
            if (down) _held.Add(key);
            else _held.Remove(key);
            SendKeys();
        }

        private IEnumerator Click(Dictionary<string, object> c, Action<string> fail)
        {
            EnsureInput();
            _leftButton = true;
            SendMouse(Vector2.zero);
            yield return new WaitForSecondsRealtime(F(c, "s", 0.12f));
            _leftButton = false;
            SendMouse(Vector2.zero);
            yield return null;
        }

        private IEnumerator Look(Dictionary<string, object> c, Action<string> fail)
        {
            EnsureInput();
            int frames = Mathf.Max(1, I(c, "frames", 10));
            Vector2 step = new Vector2(F(c, "dx", 0f), F(c, "dy", 0f)) / frames;
            for (int i = 0; i < frames; i++)
            {
                SendMouse(step);
                yield return null;
            }
        }
#else
        private const string NoInputSystem = "keys and mouse need the Input System package (com.unity.inputsystem)";
        private IEnumerator PressKey(Dictionary<string, object> c, Dictionary<string, object> r, Action<string> fail) { fail(NoInputSystem); yield break; }
        private void SetKey(Dictionary<string, object> c, bool down, Action<string> fail) => fail(NoInputSystem);
        private IEnumerator Click(Dictionary<string, object> c, Action<string> fail) { fail(NoInputSystem); yield break; }
        private IEnumerator Look(Dictionary<string, object> c, Action<string> fail) { fail(NoInputSystem); yield break; }
#endif

        private IEnumerator Shots(Dictionary<string, object> c, Dictionary<string, object> r, Action<string> fail)
        {
            var saved = new List<object>();
            if (!(c.TryGetValue("list", out object raw) && raw is List<object> list)) { fail("shots needs a 'list' of shot objects"); yield break; }
            foreach (object item in list)
            {
                if (!(item is Dictionary<string, object> one)) continue;
                string err = null;
                var rr = new Dictionary<string, object>();
                yield return Shot(one, rr, e => err = e);
                if (err != null) { fail(err); yield break; }
                saved.Add(rr["file"]);
            }

            r["files"] = saved;
        }

        private static Camera PictureCamera()
        {
            Camera cam = CameraProvider != null ? CameraProvider() : null;
            if (cam == null) cam = Camera.main;
            if (cam == null) cam = Camera.allCameras.FirstOrDefault(k => k.enabled);
            return cam;
        }

        /// <summary>
        /// Renders the game's own camera (its pipeline settings and volumes) from a free pose into a PNG. The pose is pos/euler, or
        /// pos/look_at; no pose = the current view. fov, w, h and settle are optional; hide names objects left out of the picture.
        /// A free pose hides what hangs under the camera (first-person arms) unless arms is true.
        /// </summary>
        private IEnumerator Shot(Dictionary<string, object> c, Dictionary<string, object> r, Action<string> fail)
        {
            if (_noGpu) { fail("this player runs with -nographics; start it without --headless to take pictures"); yield break; }
            Camera cam = PictureCamera();
            if (cam == null) { fail("no camera in this scene"); yield break; }

            Transform t = cam.transform;
            Vector3 oldPos = t.position;
            Quaternion oldRot = t.rotation;
            float oldFov = cam.fieldOfView;
            RenderTexture oldTarget = cam.targetTexture;
            int w = I(c, "w", 1280), h = I(c, "h", 720);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            Texture2D tex = null;
            var hidden = new List<Renderer>();
            if (c.ContainsKey("pos") && !B(c, "arms", false)) Hide(cam.GetComponentsInChildren<Renderer>(false), hidden);
            if (c.TryGetValue("hide", out object hideRaw) && hideRaw is List<object> hideNames)
                foreach (Transform candidate in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    foreach (object needle in hideNames)
                        if (needle != null && candidate.name.IndexOf(needle.ToString(), StringComparison.OrdinalIgnoreCase) >= 0)
                            Hide(candidate.GetComponentsInChildren<Renderer>(false), hidden);

            try
            {
                if (c.ContainsKey("pos"))
                {
                    Vector3 pos = V3(c, "pos", oldPos);
                    Quaternion rot = c.ContainsKey("look_at")
                        ? Quaternion.LookRotation((V3(c, "look_at", pos + Vector3.forward) - pos).normalized, Vector3.up)
                        : Quaternion.Euler(V3(c, "euler", oldRot.eulerAngles));
                    t.SetPositionAndRotation(pos, rot);
                }

                if (c.ContainsKey("fov")) cam.fieldOfView = F(c, "fov", oldFov);
                if (c.ContainsKey("settle")) yield return new WaitForSeconds(F(c, "settle", 0f));
                Render(cam, rt);
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply(false);
                RenderTexture.active = previous;
            }
            finally
            {
                foreach (Renderer renderer in hidden)
                    if (renderer != null) renderer.forceRenderingOff = false;
                cam.targetTexture = oldTarget;
                cam.fieldOfView = oldFov;
                t.SetPositionAndRotation(oldPos, oldRot);
                rt.Release();
                Destroy(rt);
            }

            string rel = "shots/" + San(S(c, "name", "shot_" + DateTime.Now.ToString("HHmmss_fff"))) + ".png";
            File.WriteAllBytes(Path.Combine(_out, rel), tex.EncodeToPNG());
            Destroy(tex);
            r["file"] = rel;
        }

        /// <summary>A full render of the camera into <paramref name="rt"/>: a render request where the pipeline supports one
        /// (post-processing and volumes included), else a plain <c>Camera.Render</c>.</summary>
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
            cam.targetTexture = rt;
            cam.Render();
        }

        private static void Hide(IEnumerable<Renderer> renderers, List<Renderer> hidden)
        {
            foreach (Renderer renderer in renderers)
            {
                if (renderer.forceRenderingOff) continue;
                renderer.forceRenderingOff = true;
                hidden.Add(renderer);
            }
        }

        private void Find(string needle, Dictionary<string, object> r)
        {
            var hits = new List<object>();
            foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (needle.Length == 0 || t.name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                hits.Add(t.name + " @ " + Fmt(t.position) + (t.gameObject.activeInHierarchy ? "" : " (inactive)"));
                if (hits.Count >= 60) break;
            }

            r["matches"] = hits;
        }

        // ------------------------------------------------------------------ findings

        private void SampleWorld()
        {
            Transform player = Player();
            if (player == null) return;
            Vector3 p = player.position;
            if (!_haveGood) { _lastGood = p; _haveGood = true; return; }
            if (p.y < _lastGood.y - 25f)
            {
                Note("FATAL", "fell|" + SceneManager.GetActiveScene().name, "Player fell out of the world at " + Fmt(p) + " (was " + Fmt(_lastGood) + ")", _doing);
                _haveGood = false;
            }
            else if (p.y > _lastGood.y - 2f) _lastGood = p;
        }

        private void OnLog(string condition, string stack, LogType type)
        {
            bool bad = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            if (_noGpu && NoGpuNoise.Any(n => condition.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                Note("INFO", "no-gpu", "Rendering messages ignored: this player has no GPU, so only pictures from a player with graphics can judge rendering", "");
                return;
            }

            lock (_gate)
            {
                if (_console.Length < 2_000_000) _console.Append(type).Append(' ').AppendLine(condition);
            }

            if (!bad)
            {
                if (type == LogType.Warning) Note("WARN", "w|" + (condition.Length > 60 ? condition.Substring(0, 60) : condition), condition.Split('\n')[0], _doing);
                return;
            }

            string first = condition.Split('\n')[0];
            Note("ERR", first.Length > 80 ? first.Substring(0, 80) : first, first, _doing);
        }

        private void Note(string sev, string key, string text, string where)
        {
            lock (_gate)
            {
                if (_issues.TryGetValue(sev + key, out Issue issue)) { issue.Count++; return; }
                _issues[sev + key] = new Issue { Sev = sev, Text = text, Where = where, Count = 1 };
            }
        }

        private List<object> IssueLines()
        {
            lock (_gate)
            {
                string[] order = { "FATAL", "ERR", "STALL", "SOFT", "WARN", "INFO" };
                return _issues.Values.OrderBy(i => Array.IndexOf(order, i.Sev)).Select(i =>
                    (object)(i.Sev + " x" + i.Count + " @" + i.Where + ": " + i.Text)).Take(60).ToList();
            }
        }

        private string Summary()
        {
            var sb = new StringBuilder();
            int errs;
            lock (_gate) errs = _issues.Values.Count(i => i.Sev == "FATAL" || i.Sev == "ERR" || i.Sev == "STALL");
            sb.AppendLine(errs == 0 ? "CLEAN: no FATAL/ERR/STALL" : "FINDINGS: " + errs + " FATAL/ERR/STALL");
            foreach (object line in IssueLines()) sb.AppendLine(line.ToString());
            sb.AppendLine("--- frames: " + _frames + ", worst " + Mathf.RoundToInt(_worst * 1000f) + " ms");
            lock (_gate) File.WriteAllText(Path.Combine(_out, "console.txt"), _console.ToString());
            return sb.ToString();
        }

        private void Status(Dictionary<string, object> r)
        {
            r["scene"] = SceneManager.GetActiveScene().name;
            r["fps"] = _frames > 0 ? Mathf.RoundToInt(_frames / Mathf.Max(0.001f, _frameSum)) : 0;
            r["timeScale"] = Time.timeScale;
            r["graphics"] = SystemInfo.graphicsDeviceType.ToString();
            Transform player = Player();
            if (player != null) { r["player"] = Fmt(player.position); r["yaw"] = player.eulerAngles.y; }
            try { StatusExtras?.Invoke(r); }
            catch (Exception e) { r["statusError"] = e.GetType().Name + ": " + e.Message; }
        }

        // ------------------------------------------------------------------ plumbing

        private void Heartbeat(string state = null)
        {
            try
            {
                File.WriteAllText(Path.Combine(_out, "heartbeat.txt"),
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds() + " " + (state ?? (_busy ? "busy:" + _doing : "idle")) + " " + SceneManager.GetActiveScene().name);
            }
            catch (IOException) { }
        }

        private static void Reply(string id, bool ok, string error, Dictionary<string, object> result)
        {
            var o = new Dictionary<string, object> { ["id"] = id, ["ok"] = ok };
            if (error != null) o["error"] = error;
            if (result != null) foreach (var kv in result) o[kv.Key] = kv.Value;
            string tmp = Path.Combine(_out, "outbox", San(id) + ".tmp");
            File.WriteAllText(tmp, AgentPlayJson.Write(o));
            string final = Path.Combine(_out, "outbox", San(id) + ".json");
            if (File.Exists(final)) File.Delete(final);
            File.Move(tmp, final);
        }

        // Helpers for ops: command values (numbers arrive as double), formatting.
        public static string S(Dictionary<string, object> c, string k, string d) => c.TryGetValue(k, out object v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : d;
        public static int I(Dictionary<string, object> c, string k, int d) => c.TryGetValue(k, out object v) && v is double n ? (int)n : d;
        public static float F(Dictionary<string, object> c, string k, float d) => c.TryGetValue(k, out object v) && v is double n ? (float)n : d;
        public static bool B(Dictionary<string, object> c, string k, bool d) => c.TryGetValue(k, out object v) && v is bool b ? b : d;

        public static Vector3 V3(Dictionary<string, object> c, string k, Vector3 d)
        {
            if (!c.TryGetValue(k, out object v) || !(v is List<object> a) || a.Count < 3) return d;
            return new Vector3(Convert.ToSingle(a[0], CultureInfo.InvariantCulture), Convert.ToSingle(a[1], CultureInfo.InvariantCulture), Convert.ToSingle(a[2], CultureInfo.InvariantCulture));
        }

        public static string Fmt(Vector3 v) => v.x.ToString("F2", CultureInfo.InvariantCulture) + "," + v.y.ToString("F2", CultureInfo.InvariantCulture) + "," + v.z.ToString("F2", CultureInfo.InvariantCulture);

        private static string San(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_').ToArray());
    }
}
#endif
