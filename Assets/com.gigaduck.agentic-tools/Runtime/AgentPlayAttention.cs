#if DEVELOPMENT_BUILD || UNITY_EDITOR || AGENT_PLAY
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Gigaduck.AgenticTools.AgentPlay
{
    /// <summary>
    /// Where an agent player looks, like a person would: along the way it walks, and at what matters when something
    /// happens (the project's <see cref="Hotspots"/>: who speaks, a hazard that is live, what a story step that just began
    /// points at). It turns the view with real mouse movement, smoothly, holds a hotspot long enough to read it, glances back
    /// at the way on long walks, and never fights the game: while <see cref="GameHasView"/> is true (a scripted look, a look
    /// lock, look input off), while an op aims or takes a picture itself, or when the view stops answering the mouse, it waits.
    /// On by default in every agent player; the <c>attention</c> op switches it (auto, movement, off) or points it at an
    /// object for a while. Its focus shows in status, observe and recording captions ("looks at ...").
    /// </summary>
    public sealed class AgentPlayAttention : MonoBehaviour
    {
        /// <summary>Something worth looking at.</summary>
        public struct Hotspot
        {
            /// <summary>Shown in captions and observe ("Niko (speaking)").</summary>
            public string Label;
            /// <summary>Followed while it moves; null: <see cref="Point"/> is a world point.</summary>
            public Transform Target;
            /// <summary>World point, or the offset from <see cref="Target"/>.</summary>
            public Vector3 Point;
            /// <summary>About 0.2 (worth a glance) to 1 (look now); 1 and more is looked at even when it is behind the walk.</summary>
            public float Weight;
        }

        public enum Mode { Auto, Movement, Off }

        /// <summary>auto: the way and the hotspots; movement: only the way; off: the agent's ops alone move the view.</summary>
        public static Mode Current = Mode.Auto;

        /// <summary>The project's hotspots now (sampled four times a second).</summary>
        public static Func<List<Hotspot>> Hotspots;

        /// <summary>True while the game drives the view itself (a scripted look, a look lock, look input off).</summary>
        public static Func<bool> GameHasView;

        /// <summary>Where a walker is about to go (set every frame while it walks): looked along before the player moves.</summary>
        public static Vector3? Heading;
        internal static float HeadingAt;

        /// <summary>What it looks at now and why ("where it walks", "Girlfriend (speaking)", "idle", "the game has the view").</summary>
        public static string Focus { get; private set; } = "idle";

        // Ops that move the view or the camera themselves: attention stays out of their way.
        private static readonly HashSet<string> HandsOff = new HashSet<string>
        {
            "aim", "interact", "look", "shot", "shots", "punch", "photo", "album", "npc-look", "sister-intro", "point",
        };

        private static AgentPlayAttention _instance;
        private static Transform _pinned;
        private static Vector3 _pinnedPoint;
        private static string _pinnedLabel;
        private static float _pinnedUntil;

        private readonly List<(Hotspot spot, float score)> _ranked = new List<(Hotspot, float)>();
        private Hotspot? _current;
        private float _currentSince, _nextSample, _backOffUntil, _lastCaption;
        private readonly Dictionary<string, float> _rested = new Dictionary<string, float>(); // label -> time it may be chosen again
        private Vector3 _lastPos, _vel;
        private bool _haveLast;
        private float _lastYaw, _lastPitch, _sentX, _sentY, _turned;
        private int _sentFrames;

        internal static void Create()
        {
            if (_instance != null) return;
            var go = new GameObject("AgentPlayAttention");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AgentPlayAttention>();
        }

        /// <summary>Looks at an object (its head when it has one) for a while, whatever else happens. Returns an error or null.</summary>
        public static string Pin(string name, float seconds)
        {
            GameObject go = AgentPlayOps.FindObject(name, true);
            if (go == null) return "attention: no active object named '" + name + "'";
            _pinned = HeadOf(go.transform);
            _pinnedPoint = _pinned == go.transform ? AgentPlayOps.Focus(go) - go.transform.position : Vector3.zero;
            _pinnedLabel = go.name + " (asked)";
            _pinnedUntil = Time.realtimeSinceStartup + Mathf.Max(0.5f, seconds);
            return null;
        }

        public static void Unpin() => _pinnedUntil = 0f;

        /// <summary>The head of a character (a humanoid head bone, a child named like a head), else the object itself.</summary>
        public static Transform HeadOf(Transform t)
        {
            var animator = t.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null) return head;
            }

            foreach (Transform child in t.GetComponentsInChildren<Transform>())
                if (child.name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0 && child.name.IndexOf("headset", StringComparison.OrdinalIgnoreCase) < 0)
                    return child;
            return t;
        }

        /// <summary>Mode, focus and the best hotspots with their scores (for observe and the attention op).</summary>
        public static void Describe(Dictionary<string, object> r)
        {
            r["mode"] = Current.ToString().ToLowerInvariant();
            r["focus"] = Focus;
            if (_instance == null) return;
            var top = new List<object>();
            foreach (var (spot, score) in _instance._ranked)
            {
                top.Add(spot.Label + " " + score.ToString("0.00", CultureInfo.InvariantCulture));
                if (top.Count >= 6) break;
            }

            r["hotspots"] = top;
        }

        private void Update()
        {
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            Camera cam = AgentPlayOps.Cam();
            Transform player = AgentPlayOps.PlayerTransform();
            if (cam == null || player == null) { _haveLast = false; SetFocus("idle"); return; }

            // Where the player goes: its smoothed horizontal velocity.
            Vector3 pos = player.position;
            if (_haveLast)
            {
                Vector3 v = (pos - _lastPos) / dt;
                if (v.sqrMagnitude > 400f) v = Vector3.zero; // a teleport, not a walk
                _vel = Vector3.Lerp(_vel, v, 1f - Mathf.Exp(-dt * 6f));
            }

            _lastPos = pos;
            _haveLast = true;
            Vector3 flat = new Vector3(_vel.x, 0f, _vel.z);
            bool moving = flat.magnitude > 0.35f;
            if (Heading.HasValue && Time.realtimeSinceStartup - HeadingAt < 0.3f)
            {
                Vector3 way = new Vector3(Heading.Value.x, 0f, Heading.Value.z);
                if (way.sqrMagnitude > 1e-4f) { flat = way.normalized * Mathf.Max(flat.magnitude, 1f); moving = true; }
            }

            Vector3 f = cam.transform.forward;
            float yaw = AgentPlayOps.Yaw(f), pitch = AgentPlayOps.Pitch(f);
            float turnedNow = Mathf.Abs(Mathf.DeltaAngle(_lastYaw, yaw)) + Mathf.Abs(pitch - _lastPitch);
            bool someoneElse = Mathf.Abs(Mathf.DeltaAngle(_lastYaw, yaw)) > Mathf.Abs(_sentX * AgentPlayOps._kx) + 25f;
            _lastYaw = yaw;
            _lastPitch = pitch;
            // Did the view answer? Many frames of mouse movement without any turn: look input is off; wait a moment.
            if (_sentFrames > 0) _turned += turnedNow;
            if (_turned > 0.05f) { _turned = 0f; _sentFrames = 0; }
            else if (_sentFrames > 20) { _backOffUntil = Time.realtimeSinceStartup + 1f; _turned = 0f; _sentFrames = 0; }

            string op = AgentPlayOps.CurrentOp;
            if (Current == Mode.Off) { SetFocus("off"); _sentX = _sentY = 0f; return; }
            if (op != null && HandsOff.Contains(op)) { SetFocus("the agent aims (" + op + ")"); _sentX = _sentY = 0f; return; }
            bool gameView = false;
            try { gameView = GameHasView != null && GameHasView(); }
            catch (Exception) { }
            if (gameView || someoneElse) { _backOffUntil = Time.realtimeSinceStartup + 0.6f; }
            if (Time.realtimeSinceStartup < _backOffUntil) { SetFocus("the game has the view"); _sentX = _sentY = 0f; return; }

            // What to look at.
            Vector3 eye = cam.transform.position;
            Vector3? target = null;
            string why;
            float speed;
            if (_pinned != null && Time.realtimeSinceStartup < _pinnedUntil)
            {
                target = _pinned.position + _pinnedPoint;
                why = _pinnedLabel;
                speed = 260f;
            }
            else
            {
                if (Current == Mode.Auto) ChooseHotspot(eye, flat, moving);
                else _current = null;
                if (_current.HasValue && _current.Value.Target == null && _current.Value.Point == Vector3.zero) _current = null;
                if (_current.HasValue)
                {
                    Hotspot h = _current.Value;
                    target = h.Target != null ? h.Target.position + h.Point : h.Point;
                    why = h.Label;
                    speed = 260f;
                }
                else if (moving)
                {
                    float rise = Mathf.Clamp(_vel.y / Mathf.Max(0.5f, flat.magnitude), -0.6f, 0.6f);
                    Vector3 dir = flat.normalized;
                    target = eye + dir * 4f + Vector3.up * (4f * (rise * 0.6f - 0.08f));
                    why = "where it walks";
                    speed = 170f;
                }
                else
                {
                    why = "idle";
                    speed = 0f;
                }
            }

            SetFocus(why);
            if (!target.HasValue) { _sentX = _sentY = 0f; return; }

            Vector3 d = (target.Value - eye).normalized;
            float yawErr = Mathf.DeltaAngle(yaw, AgentPlayOps.Yaw(d)), pitchErr = AgentPlayOps.Pitch(d) - pitch;
            if (Mathf.Abs(yawErr) < 1.5f) yawErr = 0f;
            if (Mathf.Abs(pitchErr) < 1.5f) pitchErr = 0f;
            float step = speed * dt, k = 1f - Mathf.Exp(-dt * 6f);
            float turnYaw = Mathf.Clamp(yawErr * k, -step, step), turnPitch = Mathf.Clamp(pitchErr * k, -step, step);
            _sentX = Mathf.Abs(AgentPlayOps._kx) > 1e-4f ? turnYaw / AgentPlayOps._kx : 0f;
            _sentY = Mathf.Abs(AgentPlayOps._ky) > 1e-4f ? turnPitch / AgentPlayOps._ky : 0f;
            if (_sentX == 0f && _sentY == 0f) return;
            _sentFrames++;
            AgentPlayInput.MouseDelta(new Vector2(Mathf.Clamp(_sentX, -400f, 400f), Mathf.Clamp(_sentY, -400f, 400f)));
        }

        private void ChooseHotspot(Vector3 eye, Vector3 flat, bool moving)
        {
            float now = Time.realtimeSinceStartup;
            if (now >= _nextSample)
            {
                _nextSample = now + 0.25f;
                _ranked.Clear();
                List<Hotspot> spots = null;
                try { spots = Hotspots?.Invoke(); }
                catch (Exception e) { Focus = "hotspots failed: " + e.Message; }
                if (spots != null)
                    foreach (Hotspot h in spots)
                    {
                        float s = Score(h, eye, flat, moving);
                        if (s > 0f) _ranked.Add((h, s));
                    }

                _ranked.Sort((a, b) => b.score.CompareTo(a.score));
            }

            // Keep the current one while it still matters (a glance must be long enough to see it), switch when another
            // clearly matters more, and on long walks give the way a look between hotspots that are not critical.
            float currentScore = 0f;
            if (_current.HasValue)
                foreach (var (spot, score) in _ranked)
                    if (spot.Label == _current.Value.Label) { currentScore = score; break; }
            bool tired = _current.HasValue && moving && _current.Value.Weight < 1f && now - _currentSince > 3.5f;
            if (tired) { _rested[_current.Value.Label] = now + 3f; _current = null; currentScore = 0f; }
            foreach (var (spot, score) in _ranked)
            {
                if (score < 0.3f) break;
                if (_rested.TryGetValue(spot.Label, out float until) && now < until) continue;
                if (_current.HasValue && spot.Label == _current.Value.Label) return; // already looking at the best one
                bool settled = _current.HasValue && now - _currentSince < 1.2f;
                if (_current.HasValue && (settled || score < currentScore * 1.3f) && !(spot.Weight >= 1f && _current.Value.Weight < 1f)) return;
                _current = spot;
                _currentSince = now;
                return;
            }

            if (currentScore < 0.25f) _current = null;
        }

        private static float Score(Hotspot h, Vector3 eye, Vector3 flat, bool moving)
        {
            Vector3 p = h.Target != null ? h.Target.position + h.Point : h.Point;
            if (h.Target == null && h.Point == Vector3.zero) return 0f;
            Vector3 to = p - eye;
            float dist = to.magnitude;
            if (dist < 0.3f || dist > 25f) return 0f;
            float near = dist <= 2f ? 1f : Mathf.Max(0.2f, 1f - (dist - 2f) / 20f);
            float s = h.Weight * near;
            if (Physics.Raycast(eye, to / dist, out RaycastHit hit, dist - 0.2f, ~0, QueryTriggerInteraction.Ignore))
            {
                Transform player = AgentPlayOps.PlayerTransform();
                bool self = player != null && hit.transform.IsChildOf(player);
                bool itself = h.Target != null && (hit.transform == h.Target || h.Target.IsChildOf(hit.transform) || hit.transform.IsChildOf(h.Target.root));
                if (!self && !itself) s *= 0.45f; // behind a wall: a person would not look at it
            }

            if (moving && h.Weight < 1f && Vector3.Angle(flat, new Vector3(to.x, 0f, to.z)) > 110f) s *= 0.3f; // behind the walk
            return s;
        }

        private void SetFocus(string why)
        {
            if (why == Focus) return;
            Focus = why;
            float now = Time.realtimeSinceStartup;
            if (AgentPlayRecorder.Active == null || now - _lastCaption < 0.8f) return;
            if (why == "idle" || why.StartsWith("the agent aims")) return;
            _lastCaption = now;
            AgentPlayRecorder.Mark("look", why == "where it walks" ? "looks where it walks" : why.StartsWith("the game") || why == "off" ? why : "looks at " + why);
        }
    }
}
#endif
