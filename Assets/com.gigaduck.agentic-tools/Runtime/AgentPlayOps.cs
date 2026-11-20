#if DEVELOPMENT_BUILD || UNITY_EDITOR || AGENT_PLAY
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Gigaduck.AgenticTools.AgentPlay
{
    /// <summary>
    /// Ops every agent harness shares (the package's and a project's own), so an agent can play like a person and the
    /// learning tools in agentplay.py have one vocabulary:
    /// <c>observe</c> (what the player can see and use: objects near, the crosshair target, text on screen, project hints),
    /// <c>aim</c> (turns the view onto an object with real mouse input, closed loop), <c>interact</c> (walks close, aims,
    /// checks the object is under the crosshair, presses the interact key, holding it when the object needs a hold),
    /// <c>approach</c>, <c>until</c> (waits for a condition, see <see cref="AgentPlayConditions"/>), <c>record</c>
    /// (<see cref="AgentPlayRecorder"/>) and <c>screen</c>. A harness calls <see cref="Init"/>, sets the hooks it can fill
    /// (<see cref="Status"/>, <see cref="Walker"/>, <see cref="CrosshairTarget"/> ...), sends unknown ops to <see cref="Run"/>
    /// and reports each command to <see cref="CommandStarted"/>/<see cref="CommandFinished"/> (they caption recordings).
    /// </summary>
    public static class AgentPlayOps
    {
        /// <summary>Moves the player to a point; fills result["reached"] (and "why" when it did not).</summary>
        public delegate IEnumerator WalkerFn(Vector3 destination, float tolerance, float timeout, Dictionary<string, object> result);

        /// <summary>The player's eye. Default: the main camera.</summary>
        public static Func<Camera> ViewCamera;

        /// <summary>The player's root. Default: the object tagged Player.</summary>
        public static Func<Transform> Player;

        /// <summary>Fills a status answer (scene, level, stage, health...); <c>until</c> and <c>observe</c> read it.</summary>
        public static Action<Dictionary<string, object>> Status;

        /// <summary>Walks the player (NavMesh, force move...). Default: <see cref="SteerWalk"/>, real keys and mouse in a straight line.</summary>
        public static WalkerFn Walker;

        /// <summary>What an interact press would act on now. Default: what a ray from the screen centre hits.</summary>
        public static Func<GameObject> CrosshairTarget;

        /// <summary>How long <c>interact</c> holds the key on an object (0 = a tap). Default 0.</summary>
        public static Func<GameObject, float> HoldSeconds;

        /// <summary>The objects a player can use; <c>observe</c> lists them and <c>interact</c> resolves names among them first.</summary>
        public static Func<IEnumerable<GameObject>> Interactables;

        /// <summary>Adds project fields (usable, hold, prompt...) to an object entry of <c>observe</c>.</summary>
        public static Action<GameObject, Dictionary<string, object>> DescribeObject;

        /// <summary>Adds project parts to an <c>observe</c> answer (what the game waits for, the held item...).</summary>
        public static event Action<Dictionary<string, object>> ObserveExtras;

        public static string InteractKey = "E";
        public static float Reach = 2.2f;

        public static readonly string[] Names = { "record", "studio", "until", "observe", "aim", "interact", "approach", "screen", "perf", "attention", "point" };

        // Ops that only read or that the recorder would only clutter with captions.
        private static readonly HashSet<string> Quiet = new HashSet<string>
        {
            "status", "observe", "record", "studio", "errors", "summary", "find", "screen", "shot", "shots", "sheet", "perf", "attention",
        };

        private static string _out;
        private static bool _hooked;
        private static readonly object LogGate = new object();
        private static readonly List<string> LogLines = new List<string>();
        private static int _logSeq;
        internal static float _kx = 0.12f, _ky = 0.12f; // degrees of view per mouse unit, learned by aim and walking (sign included)

        /// <summary>The op the harness runs now (null when idle); attention stays out of the way of ops that aim.</summary>
        public static string CurrentOp { get; private set; }

        public static string OutputFolder => _out;

        /// <summary>Call once when the harness starts, with its output folder.</summary>
        public static void Init(string outputFolder)
        {
            _out = outputFolder;
            if (!_hooked)
            {
                _hooked = true;
                Application.logMessageReceivedThreaded += (condition, stack, type) =>
                {
                    lock (LogGate)
                    {
                        _logSeq++;
                        LogLines.Add(type + " " + condition.Split('\n')[0]);
                        if (LogLines.Count > 400) LogLines.RemoveAt(0);
                    }
                };
            }

            if (AgentPlayRecorder.CameraProvider == null) AgentPlayRecorder.CameraProvider = Cam;
            if (AgentPlayRecorder.TextProvider == null) AgentPlayRecorder.TextProvider = ScreenTextLine;
            if (_sampler == null)
            {
                var go = new GameObject("AgentPlayPerf");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _sampler = go.AddComponent<FrameSampler>();
            }

            // Agents look where they walk and at what matters, like players (AgentPlayAttention); -agent-attention off|movement|auto.
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-agent-attention");
            if (at >= 0 && at + 1 < args.Length && Enum.TryParse(args[at + 1], true, out AgentPlayAttention.Mode mode)) AgentPlayAttention.Current = mode;
            AgentPlayAttention.Create();
        }

        // ------------------------------------------------------------------ perf: frame times since the last reset

        private sealed class FrameSampler : MonoBehaviour
        {
            private void Update() => Sample(Time.unscaledDeltaTime);
        }

        private static FrameSampler _sampler;
        private static readonly List<float> Frames = new List<float>(8192);
        private static float _frameSum, _frameWorst;

        private static void Sample(float dt)
        {
            _frameSum += dt;
            if (dt > _frameWorst) _frameWorst = dt;
            if (Frames.Count < 200000) Frames.Add(dt);
        }

        /// <summary>Frame times since the last <c>perf {reset:true}</c>: fps, worst, 95th percentile, frames over 50 ms.</summary>
        private static void Perf(Dictionary<string, object> c, Dictionary<string, object> r)
        {
            int n = Frames.Count;
            r["frames"] = n;
            r["seconds"] = Round(_frameSum);
            r["fps"] = n > 0 ? Round(n / Mathf.Max(0.001f, _frameSum)) : 0f;
            r["worstMs"] = Round(_frameWorst * 1000f);
            if (n > 0)
            {
                var sorted = new List<float>(Frames);
                sorted.Sort();
                r["p95Ms"] = Round(sorted[Mathf.Min(n - 1, (int)(n * 0.95f))] * 1000f);
                r["over50ms"] = sorted.Count(f => f > 0.05f);
            }

            if (B(c, "reset", false))
            {
                Frames.Clear();
                _frameSum = _frameWorst = 0f;
            }
        }

        public static bool Handles(string op) => Array.IndexOf(Names, op) >= 0;

        public static IEnumerator Run(string op, Dictionary<string, object> c, Dictionary<string, object> r, Action<string> fail)
        {
            switch (op)
            {
                case "record": return Record(c, r, fail);
                case "studio": return ContentStudio.Op(c, r, fail);
                case "until": return Until(c, r, fail);
                case "observe": return Once(() => Observe(c, r));
                case "aim": return Aim(c, r, fail);
                case "interact": return Interact(c, r, fail);
                case "approach": return Approach(c, r, fail);
                case "screen": return Once(() => r["texts"] = ScreenTexts(I(c, "max", 20)));
                case "perf": return Once(() => Perf(c, r));
                case "attention": return Once(() => Attention(c, r, fail));
                case "point": return Point(c, r, fail);
                default: return Once(() => fail("not a shared op: " + op));
            }
        }

        private static IEnumerator Once(Action action)
        {
            action();
            yield break;
        }

        /// <summary>Adds the recording state and what the player looks at to a status answer.</summary>
        public static void AddStatus(Dictionary<string, object> r)
        {
            AgentPlayRecorder rec = AgentPlayRecorder.Active;
            r["recording"] = rec != null && !rec.Stopped ? rec.Id : (object)false;
            r["looking"] = AgentPlayAttention.Focus;
        }

        /// <summary>attention {mode: auto|movement|off} | {focus: name, for: seconds} | {clear: true}; answers mode, focus, hotspots.</summary>
        private static void Attention(Dictionary<string, object> c, Dictionary<string, object> r, Action<string> fail)
        {
            if (c.ContainsKey("mode"))
            {
                if (!Enum.TryParse(S(c, "mode", ""), true, out AgentPlayAttention.Mode mode)) { fail("attention mode is auto, movement or off"); return; }
                AgentPlayAttention.Current = mode;
            }

            if (c.ContainsKey("focus"))
            {
                string error = AgentPlayAttention.Pin(S(c, "focus", ""), F(c, "for", 4f));
                if (error != null) { fail(error); return; }
            }

            if (B(c, "clear", false)) AgentPlayAttention.Unpin();
            AgentPlayAttention.Describe(r);
        }

        // ------------------------------------------------------------------ command captions

        public static void CommandStarted(string op, Dictionary<string, object> c)
        {
            CurrentOp = op;
            if (AgentPlayRecorder.Active == null || Quiet.Contains(op)) return;
            AgentPlayRecorder.Mark("op", Compact(op, c));
        }

        public static void CommandFinished(string op, bool ok, string error, Dictionary<string, object> r)
        {
            CurrentOp = null;
            if (AgentPlayRecorder.Active == null || Quiet.Contains(op)) return;
            var sb = new StringBuilder(op).Append(ok ? " ok" : " FAILED: " + error);
            if (r != null)
                foreach (string key in new[] { "reached", "why", "met", "targeted", "aimed", "stage", "checkpoint" })
                    if (r.TryGetValue(key, out object v) && v != null) sb.Append(' ').Append(key).Append('=').Append(AgentPlayJson.Write(v));
            AgentPlayRecorder.Mark("result", sb.ToString());
        }

        private static string Compact(string op, Dictionary<string, object> c)
        {
            var sb = new StringBuilder(op);
            foreach (var kv in c)
            {
                if (kv.Key == "op" || kv.Key == "id") continue;
                sb.Append(' ').Append(kv.Key).Append('=').Append(AgentPlayJson.Write(kv.Value));
                if (sb.Length > 160) break;
            }

            return sb.Length > 160 ? sb.ToString(0, 160) + "..." : sb.ToString();
        }

        // ------------------------------------------------------------------ record

        private static IEnumerator Record(Dictionary<string, object> c, Dictionary<string, object> r, Action<string> fail)
        {
            string action = S(c, "action", "status");
            AgentPlayRecorder active = AgentPlayRecorder.Active;
            switch (action)
            {
                case "start":
                    var o = new AgentPlayRecorder.Options
                    {
                        Id = San(S(c, "id", "rec_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture))),
                        Label = S(c, "label", null), Ffmpeg = S(c, "ffmpeg", null), Codec = S(c, "codec", "libx264"),
                        Preset = S(c, "preset", "veryfast"), Fps = I(c, "fps", 15), Width = I(c, "w", 960), Height = I(c, "h", 540),
                        Quality = I(c, "quality", 72), Crf = I(c, "crf", 26), MaxSeconds = F(c, "max", 3600f), KeyEvery = F(c, "key_every", 2f),
                    };
                    if (c.ContainsKey("flip")) o.Flip = B(c, "flip", false);
                    if (c.ContainsKey("screen_flip")) o.ScreenFlip = B(c, "screen_flip", false);
                    o.Source = S(c, "source", "screen");
                    o.Folder = Path.Combine(_out ?? Directory.GetCurrentDirectory(), "recordings", o.Id);
                    string error = AgentPlayRecorder.Begin(o, out AgentPlayRecorder started);
                    if (error != null) { fail(error); yield break; }
                    started.Status(r);
                    r["folder"] = "recordings/" + o.Id;
                    break;
                case "stop":
                    if (active == null)
                    {
                        if (AgentPlayRecorder.LastMeta == null) { fail("not recording"); yield break; }
                        foreach (var kv in AgentPlayRecorder.LastMeta) r[kv.Key] = kv.Value;
                        r["already"] = "the recording had stopped by itself";
                    }
                    else yield return active.Stop(r);

                    r["folder"] = "recordings/" + S(r, "id", "");
                    break;
                case "mark": AgentPlayRecorder.Mark(S(c, "kind", "mark"), S(c, "text", "")); break;
                case "snap": AgentPlayRecorder.Snap(); break;
                case "status":
                    if (active != null) active.Status(r);
                    else r["recording"] = false;
                    break;
                default: fail("record action must be start, stop, mark, snap or status"); break;
            }
        }

        // ------------------------------------------------------------------ until

        private static IEnumerator Until(Dictionary<string, object> c, Dictionary<string, object> r, Action<string> fail)
        {
            if (!c.TryGetValue("cond", out object cond)) { fail("until needs a 'cond' (see AgentPlayConditions)"); yield break; }
            float timeout = F(c, "timeout", 10f), poll = Mathf.Max(0.05f, F(c, "poll", 0.2f));
            var ctx = new AgentPlayConditionContext { Start = Snapshot(), LogFrom = LogSeq };
            float began = Time.realtimeSinceStartup;
            bool met;
            while (true)
            {
                ctx.Status = Snapshot();
                try { met = AgentPlayConditions.Evaluate(cond, ctx); }
                catch (ArgumentException e) { fail(e.Message); yield break; }
                if (met || Time.realtimeSinceStartup - began >= timeout) break;
                yield return new WaitForSecondsRealtime(poll);
            }

            r["met"] = met;
            r["waited"] = Round(Time.realtimeSinceStartup - began);
            r["status"] = ctx.Status;
            if (!met && !B(c, "soft", false)) fail("condition not met within " + timeout.ToString("0.#", CultureInfo.InvariantCulture) + " s: " + AgentPlayConditions.Describe(cond));
        }

        public static Dictionary<string, object> Snapshot()
        {
            var d = new Dictionary<string, object>();
            try { Status?.Invoke(d); }
            catch (Exception e) { d["statusError"] = e.GetType().Name + ": " + e.Message; }
            return d;
        }

        public static int LogSeq { get { lock (LogGate) return _logSeq; } }

        /// <summary>True when a console line logged since sequence number <paramref name="from"/> contains the text.</summary>
        public static bool LogSince(int from, string needle)
        {
            lock (LogGate)
            {
                int newer = Mathf.Min(LogLines.Count, _logSeq - from);
                for (int i = LogLines.Count - newer; i < LogLines.Count; i++)
                    if (LogLines[i].IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ observe

        private static void Observe(Dictionary<string, object> c, Dictionary<string, object> r)
        {
            float radius = F(c, "radius", 12f);
            int max = I(c, "max", 25);
            r["status"] = Snapshot();
            Camera cam = Cam();
            Transform player = PlayerTransform();
            Vector3 origin = player != null ? player.position : cam != null ? cam.transform.position : Vector3.zero;
            if (cam != null)
            {
                Vector3 f = cam.transform.forward;
                r["view"] = new Dictionary<string, object>
                {
                    ["eye"] = V(cam.transform.position), ["yaw"] = Round(Yaw(f)), ["pitch"] = Round(Pitch(f)), ["fov"] = Round(cam.fieldOfView),
                };
            }

            GameObject under = UnderCrosshair();
            r["crosshair"] = under != null ? Path3(under.transform) : null;
            r["texts"] = ScreenTexts(12);
            var attention = new Dictionary<string, object>();
            AgentPlayAttention.Describe(attention);
            r["attention"] = attention;

            IEnumerable<GameObject> pool = Interactables != null ? Interactables() : Scripted(origin, radius);
            var entries = new List<(float, Dictionary<string, object>)>();
            foreach (GameObject go in pool.Distinct())
            {
                if (go == null || !go.activeInHierarchy) continue;
                Vector3 focus = Focus(go);
                float dist = Vector3.Distance(origin, focus);
                if (dist > radius) continue;
                var e = new Dictionary<string, object> { ["name"] = go.name, ["path"] = Path3(go.transform), ["pos"] = V(focus), ["dist"] = Round(dist) };
                if (cam != null)
                {
                    Vector3 vp = cam.WorldToViewportPoint(focus);
                    bool inView = vp.z > 0f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f;
                    e["inView"] = inView;
                    e["clear"] = LineOfSight(cam.transform.position, focus, go);
                }

                if (under != null && (under == go || under.transform.IsChildOf(go.transform))) e["underCrosshair"] = true;
                try { DescribeObject?.Invoke(go, e); }
                catch (Exception ex) { e["describeError"] = ex.Message; }
                entries.Add((dist, e));
            }

            r["objects"] = entries.OrderBy(x => x.Item1).Take(max).Select(x => (object)x.Item2).ToList();
            try { ObserveExtras?.Invoke(r); }
            catch (Exception e) { r["extrasError"] = e.GetType().Name + ": " + e.Message; }
        }

        /// <summary>Objects near a point that carry a project script (a stand-in when the project gives no Interactables).</summary>
        private static IEnumerable<GameObject> Scripted(Vector3 origin, float radius)
        {
            Transform player = PlayerTransform();
            var seen = new HashSet<GameObject>();
            foreach (Collider col in Physics.OverlapSphere(origin, radius, ~0, QueryTriggerInteraction.Collide))
            {
                if (player != null && col.transform.IsChildOf(player)) continue;
                foreach (MonoBehaviour mb in col.GetComponentsInParent<MonoBehaviour>())
                {
                    string ns = mb.GetType().Namespace ?? "";
                    if (ns.StartsWith("UnityEngine") || ns.StartsWith("Unity.") || ns.StartsWith("TMPro")) continue;
                    if (seen.Add(mb.gameObject)) yield return mb.gameObject;
                    break;
                }
            }
        }

        private static bool LineOfSight(Vector3 eye, Vector3 point, GameObject target)
        {
            Vector3 d = point - eye;
            float len = d.magnitude;
            if (len < 0.05f) return true;
            if (!Physics.Raycast(eye, d / len, out RaycastHit hit, len - 0.05f, ~0, QueryTriggerInteraction.Ignore)) return true;
            Transform player = PlayerTransform();
            if (player != null && hit.transform.IsChildOf(player)) return true;
            return hit.transform == target.transform || hit.transform.IsChildOf(target.transform) || target.transform.IsChildOf(hit.transform);
        }

        // ------------------------------------------------------------------ screen text

        private static readonly List<(Type type, PropertyInfo text)> TextTypes = new List<(Type, PropertyInfo)>();
        private static bool _textTypesFound;
        private static readonly Regex Tags = new Regex("<[^>]+>", RegexOptions.Compiled);

        /// <summary>Visible UI text (UGUI Text and TextMeshPro), tags removed, without duplicates.</summary>
        public static List<object> ScreenTexts(int max)
        {
            FindTextTypes();
            var found = new List<object>();
            var seen = new HashSet<string>();
            foreach (var (type, prop) in TextTypes)
            {
                foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (!(o is Behaviour b) || !b.isActiveAndEnabled) continue;
                    var cr = b.GetComponent<CanvasRenderer>();
                    var rend = cr == null ? b.GetComponent<Renderer>() : null;
                    bool visible = cr != null ? cr.GetInheritedAlpha() >= 0.05f && !cr.cull : rend != null && rend.isVisible;
                    if (!visible) continue;
                    string text = Tags.Replace(prop.GetValue(o) as string ?? "", "").Trim();
                    if (text.Length == 0) continue;
                    text = Regex.Replace(text, @"\s+", " ");
                    if (text.Length > 160) text = text.Substring(0, 160) + "...";
                    if (seen.Add(text)) found.Add(text);
                    if (found.Count >= max) return found;
                }
            }

            return found;
        }

        public static string ScreenTextLine()
        {
            string line = string.Join(" | ", ScreenTexts(8).Cast<string>());
            return line.Length > 300 ? line.Substring(0, 300) : line;
        }

        private static void FindTextTypes()
        {
            if (_textTypesFound) return;
            _textTypesFound = true;
            foreach (string name in new[] { "TMPro.TMP_Text", "UnityEngine.UI.Text" })
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = asm.GetType(name, false);
                    PropertyInfo p = t?.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                    if (p == null) continue;
                    TextTypes.Add((t, p));
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ aim

        private static IEnumerator Aim(Dictionary<string, object> c, Dictionary<string, object> r, Action<string> fail)
        {
            Vector3 point;
            if (c.ContainsKey("point")) point = V3(c, "point", Vector3.zero);
            else
            {
                GameObject go = FindObject(S(c, "target", ""), true);
                if (go == null) { fail("aim: no active object named '" + S(c, "target", "") + "' (observe lists what is near)"); yield break; }
                point = Focus(go);
                r["target"] = Path3(go.transform);
            }

            yield return AimAt(point, F(c, "tolerance", 1.5f), F(c, "timeout", 3f), r, fail);
        }

        /// <summary>Turns the view onto a world point with mouse movement, learning how far the view turns per mouse unit.</summary>
        private static bool _calibrated, _calibrating;
        internal static bool Calibrated => _calibrated || _calibrating;

        /// <summary>
        /// Finds how far the view turns per unit of mouse movement on each axis (and which way), once per session: a small
        /// nudge right and back, up and back, measured once the view has stopped turning (a game smooths its look, so a reading a
        /// few frames after the nudge is short of the real turn and every later turn would overshoot). Aiming then steers with it.
        /// </summary>
        public static IEnumerator Calibrate()
        {
            Camera cam = Cam();
            if (cam == null || _calibrated || _calibrating) yield break;
            _calibrating = true;
            for (int axis = 0; axis < 2; axis++)
            {
                if (cam == null) break;
                Transform view = cam.transform;
                float pitch0 = Pitch(view.forward);
                // Pitch near a limit cannot turn the whole nudge: nudge away from it.
                float sign = axis == 1 && pitch0 > 0f ? -1f : 1f;
                float yaw0 = ViewYaw(view);
                var nudge = axis == 0 ? new Vector2(40f, 0f) : new Vector2(0f, 40f * sign);
                AgentPlayInput.MouseDelta(nudge);
                yield return Settle(cam);
                if (cam == null) break;
                float turned = axis == 0 ? Mathf.DeltaAngle(yaw0, ViewYaw(cam.transform)) : (Pitch(cam.transform.forward) - pitch0) * sign;
                if (Mathf.Abs(turned) > 0.05f)
                {
                    float k = Mathf.Clamp(Mathf.Abs(turned / 40f), 0.005f, 2f) * Mathf.Sign(turned);
                    if (axis == 0) _kx = k; else _ky = k;
                }

                AgentPlayInput.MouseDelta(-nudge);
                yield return Settle(cam);
            }

            _calibrated = true;
            _calibrating = false;
        }

        /// <summary>
        /// Turns the view onto a point like a hand on a mouse: a little every frame toward it (at most 220 degrees a second,
        /// easing in near it), slower after an overshoot, and never against the game (a scripted look is waited out). Stops
        /// at once when <paramref name="done"/> says so (the object is under the crosshair already).
        /// </summary>
        public static IEnumerator AimAt(Vector3 point, float tolerance, float timeout, Dictionary<string, object> r, Action<string> fail,
            Func<bool> done = null)
        {
            Camera cam = Cam();
            if (cam == null) { fail("aim: no camera"); yield break; }
            float until = Time.realtimeSinceStartup + timeout, gameHadView = 0f, lastErr = float.MaxValue, damp = 1f, sent = 0f, turned = 0f;
            int frames = 0;
            Vector3 prev = cam.transform.forward;
            while (true)
            {
                // A player does not fight a scripted look (a dialog turning the view to the speaker): wait it out.
                bool gameView = false;
                try { gameView = AgentPlayAttention.GameHasView != null && AgentPlayAttention.GameHasView(); }
                catch (Exception) { }
                if (gameView)
                {
                    gameHadView += Time.unscaledDeltaTime;
                    until += Time.unscaledDeltaTime;
                    if (gameHadView > 8f) { r["aimed"] = false; fail("aim: the game has the view (a scripted look) for more than 8 s"); yield break; }
                    yield return null;
                    prev = cam.transform.forward;
                    continue;
                }

                if (!_calibrated) { yield return Calibrate(); prev = cam.transform.forward; }
                Vector3 f = cam.transform.forward, d = (point - cam.transform.position).normalized;
                turned += Vector3.Angle(prev, f);
                prev = f;
                float err = Vector3.Angle(f, d);
                bool hit = false;
                try { hit = done != null && done(); }
                catch (Exception) { }
                if (err <= tolerance || hit)
                {
                    r["aimed"] = true;
                    r["aimError"] = Round(err);
                    r["aimFrames"] = frames;
                    yield break;
                }

                if (Time.realtimeSinceStartup > until)
                {
                    r["aimed"] = false;
                    r["aimError"] = Round(err);
                    fail("aim: still " + err.ToString("0.#", CultureInfo.InvariantCulture) + " deg off after " + timeout.ToString("0.#", CultureInfo.InvariantCulture) + " s (pitch limit, or the view is driven by the game?)");
                    yield break;
                }

                // Overshoot: the error grew since the last frame: steer more gently; converging: back to normal.
                damp = err > lastErr + 0.3f ? Mathf.Max(0.2f, damp * 0.6f) : Mathf.Min(1f, damp * 1.08f);
                lastErr = err;
                float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0.005f, 0.05f);
                float ease = 1f - Mathf.Exp(-dt * 12f), most = 220f * dt * damp;
                float yawErr = Mathf.DeltaAngle(Yaw(f), Yaw(d)), pitchErr = Pitch(d) - Pitch(f);
                float turnYaw = Mathf.Clamp(yawErr * ease, -most, most), turnPitch = Mathf.Clamp(pitchErr * ease, -most, most);
                if (Mathf.Abs(yawErr) > tolerance * 0.5f && Mathf.Abs(turnYaw) < 0.15f) turnYaw = Mathf.Sign(yawErr) * Mathf.Min(0.15f, Mathf.Abs(yawErr));
                if (Mathf.Abs(pitchErr) > tolerance * 0.5f && Mathf.Abs(turnPitch) < 0.15f) turnPitch = Mathf.Sign(pitchErr) * Mathf.Min(0.15f, Mathf.Abs(pitchErr));
                float dx = Mathf.Clamp(turnYaw / _kx, -200f, 200f), dy = Mathf.Clamp(turnPitch / _ky, -200f, 200f);
                AgentPlayInput.MouseDelta(new Vector2(dx, dy));
                sent += Mathf.Abs(dx) + Mathf.Abs(dy);
                frames++;
                yield return null;
                // Many frames of mouse movement and the view has not turned: look input is off right now.
                if (frames % 20 == 0)
                {
                    if (sent > 60f && turned < 0.3f)
                    {
                        r["aimed"] = false;
                        fail("aim: the view does not follow the mouse (the game has turned the player's look off right now?)");
                        yield break;
                    }

                    sent = turned = 0f;
                }
            }
        }

        /// <summary>Waits until the view stops turning (at least 4 frames, at most about a second).</summary>
        private static IEnumerator Settle(Camera cam)
        {
            if (cam == null) yield break;
            float lastYaw = ViewYaw(cam.transform), lastPitch = Pitch(cam.transform.forward);
            int quiet = 0;
            for (int i = 0; i < 60; i++)
            {
                yield return null;
                if (cam == null) yield break;
                float yaw = ViewYaw(cam.transform), pitch = Pitch(cam.transform.forward);
                float change = Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw)) + Mathf.Abs(pitch - lastPitch);
                lastYaw = yaw;
                lastPitch = pitch;
                quiet = change < 0.02f ? quiet + 1 : 0;
                if (i >= 3 && quiet >= 3) yield break;
            }
        }

        private static bool Learn(ref float k, float turned, float input)
        {
            if (Mathf.Abs(input) < 0.5f || Mathf.Abs(turned) < 0.01f) return false;
            float ratio = turned / input;
            if (Mathf.Abs(ratio) < 1e-4f || Mathf.Abs(ratio) > 50f) return true;
            // The view moved, but not the way this input would move it (the game turned it too): learn nothing from it,
            // unless it keeps saying so (an inverted axis: three readings in a row).
            if (Mathf.Sign(ratio) != Mathf.Sign(k))
            {
                if (++_flipVotes < 3) return true;
                _flipVotes = 0;
                k = ratio;
                return true;
            }

            _flipVotes = 0;
            if (Mathf.Abs(ratio) > Mathf.Abs(k) * 10f || Mathf.Abs(ratio) < Mathf.Abs(k) * 0.1f) return true;
            k = Mathf.Lerp(k, ratio, 0.6f);
            return true;
        }

        private static int _flipVotes;

        // ------------------------------------------------------------------ point: the mouse pointer, for menus and pointer UIs

        /// <summary>
        /// point {target | world:[x,y,z] | x, y (0..1 of the screen from the bottom left), click: n, interval: s, glide: frames}:
        /// glides the pointer over an object as the camera sees it (a UI element where it is drawn), like a hand, so hover
        /// effects fire, then clicks n times (aiming again before each click, for things that move).
        /// </summary>
        private static IEnumerator Point(Dictionary<string, object> c, Dictionary<string, object> r, Action<string> fail)
        {
            if (!AgentPlayInput.Available) { fail(AgentPlayInput.Missing); yield break; }
            GameObject go = null;
            if (c.ContainsKey("target"))
            {
                go = FindObject(S(c, "target", ""), false);
                if (go == null) { fail("point: no active object named '" + S(c, "target", "") + "'"); yield break; }
                r["target"] = Path3(go.transform);
            }

            string error = ScreenPoint(c, go, out Vector2 screen);
            if (error != null) { fail(error); yield break; }
            Vector2 start = AgentPlayInput.Pointer;
            int glide = Mathf.Max(1, I(c, "glide", 10));
            for (int i = 1; i <= glide; i++)
            {
                float t = i / (float)glide;
                AgentPlayInput.SetPointer(Vector2.Lerp(start, screen, t * t * (3f - 2f * t)));
                yield return null;
            }

            yield return null;
            int clicks = Mathf.Max(0, I(c, "click", 0));
            float interval = Mathf.Max(0.05f, F(c, "interval", 0.4f));
            for (int k = 0; k < clicks; k++)
            {
                if (go != null && ScreenPoint(c, go, out Vector2 again) == null) { AgentPlayInput.SetPointer(again); screen = again; yield return null; }
                yield return AgentPlayInput.Click(0, 0.06f);
                yield return new WaitForSecondsRealtime(interval);
            }

            r["screen"] = new List<object> { Round(screen.x), Round(screen.y) };
            r["clicks"] = clicks;
        }

        private static string ScreenPoint(Dictionary<string, object> c, GameObject go, out Vector2 screen)
        {
            screen = default;
            if (go == null && !c.ContainsKey("world"))
            {
                if (!c.ContainsKey("x")) return "point needs a target, world [x,y,z], or x and y (0..1 of the screen)";
                screen = new Vector2(F(c, "x", 0.5f) * Screen.width, F(c, "y", 0.5f) * Screen.height);
                return null;
            }

            if (go != null && go.transform is RectTransform rect)
            {
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                Vector3 middle = (corners[0] + corners[2]) * 0.5f;
                Canvas canvas = go.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay) { screen = middle; return null; }
                Camera ui = canvas != null && canvas.worldCamera != null ? canvas.worldCamera : Cam();
                if (ui == null) return "point: no camera draws " + go.name;
                screen = ui.WorldToScreenPoint(middle);
                return null;
            }

            Camera cam = Cam();
            if (cam == null) return "point: no camera";
            Vector3 world = go != null ? Focus(go) : V3(c, "world", Vector3.zero);
            Vector3 p = cam.WorldToScreenPoint(world);
            if (p.z <= 0f) return "point: " + (go != null ? go.name : "the point") + " is behind the camera";
            screen = p;
            return null;
        }

        // ------------------------------------------------------------------ interact / approach

        private static IEnumerator Interact(Dictionary<string, object> c, Dictionary<string, object> r, Action<string> fail)
        {
            string name = S(c, "target", "");
            GameObject go = FindObject(name, true);
            if (go == null) { fail("interact: no active object named '" + name + "' (observe lists what is near)"); yield break; }
            r["target"] = Path3(go.transform);
            float reach = F(c, "reach", Reach);
            Camera cam = Cam();
            if (cam == null) { fail("interact: no camera"); yield break; }

            if (Vector3.Distance(cam.transform.position, Focus(go)) > reach && B(c, "approach", true))
            {
                var walk = new Dictionary<string, object>();
                yield return Walk(PointNear(go, reach * 0.6f), 0.35f, F(c, "timeout", 25f), walk);
                r["walked"] = walk.TryGetValue("reached", out object reached) ? reached : false;
                if (walk.TryGetValue("why", out object why)) r["walkWhy"] = why;
            }

            r["distance"] = Round(Vector3.Distance(cam.transform.position, Focus(go)));
            bool targeted = IsTargeted(go);
            foreach (Vector3 point in AimPoints(go))
            {
                if (targeted) break;
                string aimError = null;
                var aim = new Dictionary<string, object>();
                yield return AimAt(point, F(c, "tolerance", 1.2f), 2.5f, aim, e => aimError = e, () => IsTargeted(go));
                if (aimError != null && aimError.Contains("does not follow")) { fail("interact: " + aimError); yield break; }
                yield return null; // the interactor casts its ray in its own update
                targeted = IsTargeted(go);
            }

            r["targeted"] = targeted;
            GameObject under = UnderCrosshair();
            r["underCrosshair"] = under != null ? Path3(under.transform) : null;
            if (!targeted && !B(c, "force", false))
            {
                fail("interact: could not get '" + go.name + "' under the crosshair (under it: " + (under != null ? under.name : "nothing") +
                     ", distance " + S(r, "distance", "?") + " m; walk closer, clear the view, or the object is not usable now)");
                yield break;
            }

            float hold = c.ContainsKey("hold") ? F(c, "hold", 0f) : HoldSeconds != null ? HoldSeconds(go) : 0f;
            string key = S(c, "key", InteractKey);
            string pressError = null;
            yield return AgentPlayInput.Press(key, hold > 0f ? hold + 0.25f : 0.12f, e => pressError = e);
            if (pressError != null) { fail(pressError); yield break; }
            r["pressed"] = key;
            r["held"] = Round(hold);
            yield return new WaitForSecondsRealtime(F(c, "settle", 0.35f));
        }

        private static IEnumerator Approach(Dictionary<string, object> c, Dictionary<string, object> r, Action<string> fail)
        {
            Vector3 point;
            GameObject go = null;
            if (c.ContainsKey("pos")) point = V3(c, "pos", Vector3.zero);
            else
            {
                go = FindObject(S(c, "target", ""), true);
                if (go == null) { fail("approach: no active object named '" + S(c, "target", "") + "'"); yield break; }
                point = PointNear(go, F(c, "dist", 1.2f));
                r["target"] = Path3(go.transform);
            }

            yield return Walk(point, F(c, "tolerance", 0.35f), F(c, "timeout", 30f), r);
            if (go != null && B(c, "face", true))
                yield return AimAt(Focus(go), 3f, 1.5f, new Dictionary<string, object>(), _ => { }); // facing it is a courtesy, not a failure

            if (!(r.TryGetValue("reached", out object ok) && ok is bool b && b) && !B(c, "soft", false))
                fail("approach: did not get there (" + S(r, "why", "unknown") + ")");
        }

        private static IEnumerator Walk(Vector3 point, float tolerance, float timeout, Dictionary<string, object> r) =>
            (Walker ?? SteerWalk)(point, tolerance, timeout, r);

        /// <summary>The default walker: <see cref="KeyWalk"/> in a straight line.</summary>
        public static IEnumerator SteerWalk(Vector3 destination, float tolerance, float timeout, Dictionary<string, object> r) =>
            KeyWalk(new[] { destination }, tolerance, timeout, r);

        private static readonly string[] MoveKeys = { "W", "A", "S", "D" };
        private const float PursuitAhead = 0.6f; // metres ahead on the line: keeps within about 0.2 m of it

        /// <summary>
        /// Walks like a player: holds W, A, S and D for the way to the next corner as seen from where the camera looks
        /// (attention turns the view along the way: <see cref="AgentPlayAttention.Heading"/>), so the game's own movement
        /// applies: its speed, its collisions, its locks. While <paramref name="frozen"/> says the game holds the player still
        /// it lets go and waits, as a person would, instead of calling it blocked. <paramref name="run"/> holds Left Shift.
        /// It keeps to the line between corners (pure pursuit: it heads for a point a little ahead on the line), so the 8 key
        /// directions never drift it off a narrow lane, whatever the view looks at.
        /// Fills reached, remaining, why, stoppedAt, frozen (seconds the game held the player).
        /// </summary>
        public static IEnumerator KeyWalk(IList<Vector3> corners, float tolerance, float timeout, Dictionary<string, object> r,
            Func<bool> frozen = null, bool run = false)
        {
            Transform player = PlayerTransform();
            if (player == null || corners == null || corners.Count == 0) { r["reached"] = false; r["why"] = "no player"; yield break; }
            Vector3 destination = corners[corners.Count - 1];
            float until = Time.realtimeSinceStartup + timeout, lastProgress = Time.realtimeSinceStartup, still = 0f, best = float.MaxValue;
            Vector3 last = player.position, from = player.position;
            float closest = float.MaxValue; // progress is getting closer to the corner, not any movement (jitter at a wall)
            var trail = new List<object> { new List<object> { Round(from.x), Round(from.z) } };
            float started = Time.realtimeSinceStartup, frozenFrom = -1f, frozenTo = -1f;
            float nextTrail = Time.realtimeSinceStartup + 0.5f;
            string held = "";
            int i = 0;
            try
            {
                while (player != null)
                {
                    Vector3 toCorner = Horizontal(corners[i] - player.position);
                    bool final = i == corners.Count - 1;
                    if (toCorner.magnitude <= (final ? tolerance : Mathf.Max(tolerance, 0.5f)))
                    {
                        if (final) break;
                        from = corners[i];
                        i++;
                        closest = float.MaxValue;
                        continue;
                    }

                    // Head for a point a little ahead on the line from the last corner to this one.
                    Vector3 line = Horizontal(corners[i] - from);
                    float length = line.magnitude;
                    Vector3 along = length > 0.01f ? line / length : toCorner.normalized;
                    float done = Mathf.Clamp(Vector3.Dot(Horizontal(player.position - from), along), 0f, length);
                    Vector3 to = Horizontal(from + along * Mathf.Min(length, done + PursuitAhead) - player.position);
                    if (to.sqrMagnitude < 1e-4f) to = toCorner;

                    float rest = Horizontal(destination - player.position).magnitude;
                    if (final && best <= tolerance + 0.3f && rest > best + 0.05f) break; // a step past it: close enough
                    best = Mathf.Min(best, rest);
                    float now = Time.realtimeSinceStartup;
                    if (now > until) { r["why"] = "timeout"; break; }
                    bool hold = false;
                    try { hold = frozen != null && frozen(); }
                    catch (Exception) { }
                    AgentPlayAttention.Heading = length > 0.01f ? along : to;
                    AgentPlayAttention.HeadingAt = now;
                    string keys = hold ? "" : KeysToward(to);
                    if (keys != held) { HoldKeys(held, keys, run); held = keys; }
                    yield return null;
                    if (player != null && Time.realtimeSinceStartup >= nextTrail && trail.Count < 80)
                    {
                        nextTrail = Time.realtimeSinceStartup + 0.5f;
                        trail.Add(new List<object> { Round(player.position.x), Round(player.position.z) });
                    }

                    if (player == null) break;
                    bool dying = false;
                    try { dying = Dying != null && Dying(); }
                    catch (Exception) { }
                    if (dying)
                    {
                        r["why"] = "the player died";
                        r["diedAt"] = V(player.position);
                        break;
                    }

                    if ((player.position - last).sqrMagnitude > 9f)
                    {
                        r["why"] = "the player was moved away (a death or a teleport)";
                        r["movedTo"] = V(player.position);
                        break;
                    }

                    last = player.position;
                    float gap = Horizontal(corners[i] - player.position).magnitude;
                    if (gap < closest - 0.1f) { closest = gap; lastProgress = Time.realtimeSinceStartup; }
                    else if (hold) { still += Time.realtimeSinceStartup - now; lastProgress = Time.realtimeSinceStartup; }
                    if (hold)
                    {
                        if (frozenFrom < 0f) frozenFrom = now - started;
                        frozenTo = Time.realtimeSinceStartup - started;
                    }
                    else if (Time.realtimeSinceStartup - lastProgress > 2f)
                    {
                        r["why"] = "blocked: no progress for 2 s (NPC, door or story barrier in the way)";
                        break;
                    }
                }
            }
            finally
            {
                HoldKeys(held, "", run);
                AgentPlayAttention.Heading = null;
                _keySector = int.MinValue;
            }

            if (still > 0.05f) r["frozen"] = Round(still);
            if (frozenFrom >= 0f) r["frozenSpan"] = new List<object> { Round(frozenFrom), Round(frozenTo) }; // seconds into the walk
            var path = new List<object>();
            foreach (Vector3 c in corners) path.Add(new List<object> { Round(c.x), Round(c.z) });
            r["path"] = path;   // the corners walked (x, z)
            r["trail"] = trail; // where the player was every half second (x, z)
            Vector3 left = player != null ? Horizontal(destination - player.position) : Vector3.zero;
            bool reached = player != null && left.magnitude <= tolerance + 0.5f;
            r["reached"] = reached;
            if (reached) { r.Remove("why"); yield break; }
            r["remaining"] = player != null ? Round(left.magnitude) : -1f;
            if (!r.ContainsKey("why")) r["why"] = "the player is gone";
            if (player != null) r["stoppedAt"] = V(player.position);
        }

        /// <summary>
        /// The forward the movement keys move along. Default: the player's body (first-person controllers move relative to
        /// the body; a scripted camera can look elsewhere while the body stays put). A project whose keys follow the camera
        /// sets it to the camera's forward.
        /// </summary>
        public static Func<Vector3> MoveForward;

        /// <summary>True while the player is dying (the project's death sequence): a walk stops and says so.</summary>
        public static Func<bool> Dying;

        private static int _keySector = int.MinValue;

        /// <summary>
        /// The movement keys for a direction, relative to what the keys move along (8 directions). Keeps the keys it holds
        /// until the way is clearly in another direction (12 degrees past the boundary), so it does not flicker between W and
        /// W+D, which makes a strafing camera lean back and forth.
        /// </summary>
        public static string KeysToward(Vector3 to)
        {
            Transform player = PlayerTransform();
            Vector3 forward = MoveForward != null ? MoveForward() : player != null ? player.forward : Cam() != null ? Cam().transform.forward : Vector3.forward;
            float a = Mathf.DeltaAngle(Yaw(forward), Yaw(to));
            int sector = Mathf.RoundToInt(a / 45f);
            if (_keySector != int.MinValue && Mathf.Abs(Mathf.DeltaAngle(a, _keySector * 45f)) <= 22.5f + 12f) sector = _keySector;
            _keySector = sector;
            a = sector * 45f;
            string keys = "";
            if (Mathf.Abs(a) <= 67.5f) keys += "W";
            if (Mathf.Abs(a) >= 112.5f) keys += "S";
            if (a >= 22.5f && a <= 157.5f) keys += "D";
            if (a <= -22.5f && a >= -157.5f) keys += "A";
            return keys;
        }

        private static void HoldKeys(string old, string now, bool run)
        {
            foreach (string k in MoveKeys)
            {
                bool was = old.Contains(k), will = now.Contains(k);
                if (was != will) AgentPlayInput.SetKey(k, will);
            }

            if (run && (old.Length > 0) != (now.Length > 0)) AgentPlayInput.SetKey("LeftShift", now.Length > 0);
        }

        private static Vector3 Horizontal(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        /// <summary>The old straight-line walker (W and mouse steering); kept for projects that call it.</summary>
        public static IEnumerator MouseSteerWalk(Vector3 destination, float tolerance, float timeout, Dictionary<string, object> r)
        {
            Transform player = PlayerTransform();
            Camera cam = Cam();
            if (player == null || cam == null) { r["reached"] = false; r["why"] = "no player or camera"; yield break; }
            float until = Time.realtimeSinceStartup + timeout, lastProgress = Time.realtimeSinceStartup;
            Vector3 last = player.position;
            bool forward = false;
            while (true)
            {
                Vector3 to = destination - player.position;
                to.y = 0f;
                if (to.magnitude <= tolerance) break;
                if (Time.realtimeSinceStartup > until) { r["why"] = "timeout"; break; }
                float yaw0 = Yaw(cam.transform.forward), yawErr = Mathf.DeltaAngle(yaw0, Yaw(to));
                float dx = Mathf.Clamp(yawErr * 0.5f / _kx, -600f, 600f);
                AgentPlayInput.MouseDelta(new Vector2(dx, 0f));
                bool go = Mathf.Abs(yawErr) < 35f;
                if (go != forward) { AgentPlayInput.SetKey("W", go); forward = go; }
                yield return null;
                yield return null;
                Learn(ref _kx, Mathf.DeltaAngle(yaw0, Yaw(cam.transform.forward)), dx);
                if ((player.position - last).sqrMagnitude > 0.0025f) { last = player.position; lastProgress = Time.realtimeSinceStartup; }
                else if (Time.realtimeSinceStartup - lastProgress > 2f && forward) { r["why"] = "blocked: no progress for 2 s"; break; }
            }

            if (forward) AgentPlayInput.SetKey("W", false);
            Vector3 rest = destination - player.position;
            rest.y = 0f;
            r["reached"] = rest.magnitude <= tolerance + 0.5f;
            if (rest.magnitude > tolerance + 0.5f) r["remaining"] = Round(rest.magnitude);
        }

        // ------------------------------------------------------------------ lookups

        /// <summary>The player's eye; without a player (a menu) the main camera, else the camera that draws last.</summary>
        public static Camera Cam()
        {
            Camera cam = ViewCamera != null ? ViewCamera() : null;
            if (cam != null) return cam;
            if (Camera.main != null) return Camera.main;
            Camera best = null;
            foreach (Camera k in Camera.allCameras)
                if (k.enabled && k.targetTexture == null && (best == null || k.depth > best.depth)) best = k;
            return best;
        }

        public static Transform PlayerTransform()
        {
            if (Player != null) return Player();
            GameObject tagged = GameObject.FindWithTag("Player");
            return tagged != null ? tagged.transform : null;
        }

        /// <summary>What an interact press would act on now (the project's interactor, else a ray from the screen centre).</summary>
        public static GameObject UnderCrosshair()
        {
            if (CrosshairTarget != null) return CrosshairTarget();
            Camera cam = Cam();
            if (cam == null) return null;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return Physics.Raycast(ray, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Collide) ? hit.collider.gameObject : null;
        }

        private static bool IsTargeted(GameObject go)
        {
            GameObject under = UnderCrosshair();
            return under != null && (under == go || under.transform.IsChildOf(go.transform) || go.transform.IsChildOf(under.transform));
        }

        /// <summary>
        /// An active object by name: exact name first, then a "Parent/Child" path ending, then a name containing the text;
        /// among the project's interactables first when asked; the one nearest the player wins a tie.
        /// </summary>
        public static GameObject FindObject(string name, bool preferInteractable)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Camera eye = PlayerTransform() == null ? Cam() : null;
            Vector3 origin = PlayerTransform() != null ? PlayerTransform().position : eye != null ? eye.transform.position : Vector3.zero;
            if (preferInteractable && Interactables != null)
            {
                GameObject hit = Best(Interactables().Where(g => g != null && g.activeInHierarchy).Select(g => g.transform), name, origin);
                if (hit != null) return hit;
            }

            return Best(UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None), name, origin);
        }

        private static GameObject Best(IEnumerable<Transform> pool, string name, Vector3 origin)
        {
            Transform best = null;
            int bestRank = 9;
            float bestDist = float.MaxValue;
            bool path = name.Contains("/");
            foreach (Transform t in pool)
            {
                int rank = string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase) ? 0
                    : path && PathOf(t).EndsWith(name, StringComparison.OrdinalIgnoreCase) ? 1
                    : !path && t.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 9;
                if (rank > bestRank || rank == 9) continue;
                float dist = (t.position - origin).sqrMagnitude;
                if (rank < bestRank || dist < bestDist) { best = t; bestRank = rank; bestDist = dist; }
            }

            return best != null ? best.gameObject : null;
        }

        /// <summary>The point to aim at: the middle of the object's collider, else of its renderers, else its position.</summary>
        public static Vector3 Focus(GameObject go)
        {
            Collider col = go.GetComponent<Collider>();
            if (col == null || !col.enabled) col = go.GetComponentInChildren<Collider>();
            if (col != null && col.enabled) return col.bounds.center;
            Renderer rend = go.GetComponentInChildren<Renderer>();
            return rend != null ? rend.bounds.center : go.transform.position;
        }

        private static IEnumerable<Vector3> AimPoints(GameObject go)
        {
            yield return Focus(go);
            foreach (Collider col in go.GetComponentsInChildren<Collider>().Where(k => k.enabled).Take(3))
            {
                Bounds b = col.bounds;
                yield return b.center;
                yield return b.center + Vector3.up * b.extents.y * 0.6f;
                yield return b.center - Vector3.up * b.extents.y * 0.6f;
            }

            yield return go.transform.position;
        }

        /// <summary>A point <paramref name="dist"/> from the object on the player's side, at the player's height.</summary>
        public static Vector3 PointNear(GameObject go, float dist)
        {
            Vector3 target = Focus(go);
            Transform player = PlayerTransform();
            Vector3 from = player != null ? player.position : target + Vector3.back;
            Vector3 away = from - target;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = Vector3.back;
            Vector3 point = target + away.normalized * dist;
            point.y = from.y;
            return point;
        }

        /// <summary>
        /// The serialized fields of a component as JSON-ready values (objects as name, position and active flag; numbers,
        /// flags, enums, short strings, vectors), for hints such as "what is this story step waiting for".
        /// </summary>
        public static Dictionary<string, object> Fields(Component component, int max = 16)
        {
            var d = new Dictionary<string, object>();
            for (Type t = component.GetType(); t != null && t != typeof(MonoBehaviour) && t != typeof(Component); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.IsNotSerialized || !(f.IsPublic || f.IsDefined(typeof(SerializeField), false))) continue;
                    object value;
                    try { value = Json(f.GetValue(component), 0); }
                    catch (Exception) { continue; }
                    if (value == null) continue;
                    string key = f.Name.StartsWith("<") ? f.Name.Substring(1, f.Name.IndexOf('>') - 1) : f.Name;
                    d[key] = value;
                    if (d.Count >= max) return d;
                }
            }

            return d;
        }

        private static object Json(object v, int depth)
        {
            switch (v)
            {
                case null: return null;
                case UnityEngine.Object o when o == null: return null;
                case GameObject go:
                    return new Dictionary<string, object> { ["obj"] = go.name, ["pos"] = V(go.transform.position), ["active"] = go.activeInHierarchy };
                case Component comp:
                    return new Dictionary<string, object> { ["obj"] = comp.name, ["type"] = comp.GetType().Name, ["pos"] = V(comp.transform.position), ["active"] = comp.gameObject.activeInHierarchy };
                case UnityEngine.Object asset: return asset.name;
                case string s: return s.Length == 0 ? null : s.Length > 80 ? s.Substring(0, 80) + "..." : s;
                case bool b: return b;
                case Enum e: return e.ToString();
                case int i: return i;
                case float f: return Round(f);
                case double db: return Round((float)db);
                case Vector3 v3: return V(v3);
                case IList list when depth == 0:
                    var items = new List<object>();
                    foreach (object item in list)
                    {
                        object j = Json(item, 1);
                        if (j != null) items.Add(j);
                        if (items.Count >= 6) break;
                    }

                    return items.Count > 0 ? items : null;
                default: return null;
            }
        }

        // ------------------------------------------------------------------ helpers

        public static float Yaw(Vector3 f) => Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        /// <summary>The yaw of a view from its right vector: well defined even looking straight up or down (no roll assumed).</summary>
        public static float ViewYaw(Transform view) => Mathf.Atan2(-view.right.z, view.right.x) * Mathf.Rad2Deg;
        public static float Pitch(Vector3 f) => Mathf.Asin(Mathf.Clamp(f.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        public static List<object> V(Vector3 v) => new List<object> { Round(v.x), Round(v.y), Round(v.z) };
        private static float Round(float v) => Mathf.Round(v * 100f) / 100f;

        public static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        /// <summary>The last three parts of an object's path ("Kitchen/Counter/Kettle").</summary>
        public static string Path3(Transform t)
        {
            string path = t.name;
            for (int i = 0; i < 2 && t.parent != null; i++)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }

            return path;
        }

        public static string San(string s) => new string((s ?? "").Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_').ToArray());
        public static string S(Dictionary<string, object> c, string k, string d) => c.TryGetValue(k, out object v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : d;
        public static int I(Dictionary<string, object> c, string k, int d) => c.TryGetValue(k, out object v) && v is double n ? (int)n : d;
        public static float F(Dictionary<string, object> c, string k, float d) => c.TryGetValue(k, out object v) && v is double n ? (float)n : d;
        public static bool B(Dictionary<string, object> c, string k, bool d) => c.TryGetValue(k, out object v) && v is bool b ? b : d;

        public static Vector3 V3(Dictionary<string, object> c, string k, Vector3 d)
        {
            if (!c.TryGetValue(k, out object v) || !(v is List<object> a) || a.Count < 3) return d;
            return new Vector3(Convert.ToSingle(a[0], CultureInfo.InvariantCulture), Convert.ToSingle(a[1], CultureInfo.InvariantCulture), Convert.ToSingle(a[2], CultureInfo.InvariantCulture));
        }
    }
}
#endif
