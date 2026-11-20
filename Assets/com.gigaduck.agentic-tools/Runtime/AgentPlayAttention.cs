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
    /// points at). It never fights the game: while <see cref="GameHasView"/> is true (a scripted look, a look lock, look input
    /// off), while an op aims or takes a picture itself, or when the view stops answering the mouse, it waits.
    /// <para>
    /// Steady by design. The view is steered open loop: a virtual head eases toward the goal (a critically damped spring)
    /// and only its movement is sent as mouse movement, so the game's own look smoothing and head bob can never feed back
    /// into an oscillation. Errors (a wrong mouse gain) are corrected only once the head is still and the game's view has
    /// settled. Moving targets (an animated head bone) are low-passed and a small dead zone ignores sway.
    /// </para>
    /// <para>
    /// Smart choice. Every hotspot is tracked by what it points at (several labels on one character are one hotspot) with a
    /// smoothed score, so a spot that blinks out of one sample (a door frame, a view cone) is not dropped. Looking at a hotspot
    /// bores it; boredom recovers slowly, so the view does not ping-pong between two points; a hotspot that becomes more
    /// important again (a new line, a new story step) is fresh at once. The way it walks is a candidate like the others.
    /// Switching needs a minimum hold and a clear margin, and the point just left is not taken again for a while unless it
    /// became fresh. <c>observe</c> shows a <c>jitter</c> count (view reversals in the last 10 s) to catch any regression.
    /// </para>
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

        /// <summary>
        /// The numbers behind attention's choices and turns. Not a game motion (agent test tooling), so plain fields a test
        /// can change at run time instead of a curve profile.
        /// </summary>
        public static class Tuning
        {
            /// <summary>Seconds a hotspot's score takes to rise toward a higher value, and to fall toward a lower one.</summary>
            public static float ScoreRise = 0.25f, ScoreFall = 0.6f;
            /// <summary>Seconds a hotspot missing from the project's list keeps its last score (blinks are not departures).</summary>
            public static float Grace = 1f;
            /// <summary>A hotspot is taken at this effective score or more, and let go after its score stays under DropScore for DropAfter s.</summary>
            public static float StartScore = 0.3f, DropScore = 0.18f, DropAfter = 0.6f;
            /// <summary>Seconds a focus is held before anything else may take over (UrgentDwell: a critical hotspot taking over a normal one).</summary>
            public static float MinDwell = 1.6f, UrgentDwell = 0.35f;
            /// <summary>A challenger needs at least current * SwitchRatio + SwitchMargin.</summary>
            public static float SwitchRatio = 1.35f, SwitchMargin = 0.08f;
            /// <summary>Seconds the point just left is not taken again (unless it became fresh).</summary>
            public static float ReturnBlock = 4f;
            /// <summary>Seconds of looking that fully bore a normal hotspot, a critical one (weight 1+), and that a bored one takes to recover.</summary>
            public static float BoredAfter = 4f, CriticalBoredAfter = 12f, BoredRecover = 12f;
            /// <summary>How much full boredom lowers a hotspot's score (0 none, 1 to zero).</summary>
            public static float BoredWeight = 0.75f;
            /// <summary>Score of the way it walks while it walks.</summary>
            public static float WalkScore = 0.4f;
            /// <summary>Seconds the virtual head takes to ease onto a hotspot, onto the way; top turn speeds in degrees a second.</summary>
            public static float HotspotSmooth = 0.25f, WalkSmooth = 0.35f, HotspotSpeed = 260f, WalkSpeed = 170f;
            /// <summary>Seconds a moving target point is low-passed; degrees of target sway ignored (soft dead zone).</summary>
            public static float TargetSmooth = 0.15f, DeadZone = 1.2f;
            /// <summary>Pitch limit of the virtual head, degrees.</summary>
            public static float MaxPitch = 60f;
            /// <summary>
            /// The head's remaining error is measured once the head is still and the view has stopped turning (QuietRate degrees a
            /// second for QuietTime s; then errors over ResyncQuiet degrees are corrected at once), or, when the view never stops
            /// (head bob while walking), after Settle s (errors over ResyncAngle degrees).
            /// </summary>
            public static float Settle = 0.45f, ResyncAngle = 2f, QuietRate = 1.5f, QuietTime = 0.1f, ResyncQuiet = 0.6f;
            /// <summary>Degrees a move must turn to teach the mouse gain, and how much of the measured correction is taken (0..1).</summary>
            public static float LearnFrom = 3f, LearnRate = 0.85f;
        }

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

        private const string WalkKey = "walk", PinKey = "pin";

        private static AgentPlayAttention _instance;
        private static Transform _pinned;
        private static Vector3 _pinnedPoint;
        private static string _pinnedLabel;
        private static float _pinnedUntil;

        /// <summary>A hotspot tracked over time, by what it points at.</summary>
        private sealed class Spot
        {
            public string Key;
            public Hotspot H;
            public float Raw, Score, Bored, LastSeen, FreshAt, PrevWeight;
        }

        private readonly Dictionary<string, Spot> _spots = new Dictionary<string, Spot>();
        private readonly Dictionary<string, (Hotspot h, float raw)> _merge = new Dictionary<string, (Hotspot, float)>();
        private readonly List<string> _drop = new List<string>();
        private readonly Spot _walk = new Spot { Key = WalkKey, H = new Hotspot { Label = "where it walks", Weight = 0.5f } };
        private Spot _current;
        private float _since, _fadingFor, _nextSample, _leftAt, _backOffUntil, _lastCaption;
        private string _leftKey;
        private readonly Queue<float> _switches = new Queue<float>();

        // The player's movement.
        private Vector3 _lastPos, _vel;
        private bool _haveLast;

        // The view as measured, and the virtual head that steers it.
        private bool _haveView, _driving;
        private float _lastYaw, _lastPitch, _measYaw, _measPitch;
        private float _viewYaw, _viewPitch, _yawVel, _pitchVel, _stillFor, _camRate, _camQuiet;
        private string _goalKey;
        private float _goalYaw, _goalPitch;
        private Vector3 _goalPoint;
        private float _sentYaw, _sentPitch, _sentDeg, _seenDeg;
        private int _checkFrames;
        private float _pitchUp = 90f, _pitchDown = 90f; // the game's own pitch limits, learned when the view stops short
        private float _gainX = 1f, _gainY = 1f, _segYaw, _segPitch, _segStartYaw, _segStartPitch;

        // Jitter: reversals of the view's turn while attention drives it.
        private float _yawRate, _pitchRate;
        private int _yawSign, _pitchSign;
        private readonly Queue<float> _reversals = new Queue<float>();

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

        /// <summary>
        /// Mode, focus, how long it is held, the best hotspots with their effective scores (boredom included), focus switches
        /// in the last 30 s and jitter (reversals of the view's turn in the last 10 s; a steady view has a few at most).
        /// </summary>
        public static void Describe(Dictionary<string, object> r)
        {
            r["mode"] = Current.ToString().ToLowerInvariant();
            r["focus"] = Focus;
            if (_instance == null) return;
            AgentPlayAttention a = _instance;
            float now = Time.realtimeSinceStartup;
            var ranked = new List<Spot>(a._spots.Values);
            ranked.Sort((x, y) => a.Effective(y).CompareTo(a.Effective(x)));
            var top = new List<object>();
            foreach (Spot s in ranked)
            {
                if (s.Score < 0.01f) continue;
                string text = s.H.Label + " " + a.Effective(s).ToString("0.00", CultureInfo.InvariantCulture);
                if (s.Bored > 0.05f) text += " (bored " + s.Bored.ToString("0.0", CultureInfo.InvariantCulture) + ")";
                top.Add(text);
                if (top.Count >= 6) break;
            }

            r["hotspots"] = top;
            if (a._current != null) r["held"] = Math.Round(now - a._since, 1);
            Prune(a._switches, now - 30f);
            Prune(a._reversals, now - 10f);
            r["switches"] = a._switches.Count;
            // Degrees of view per mouse unit attention steers with (calibration times what it learned since), x and y.
            r["gain"] = new List<object> { Math.Round(AgentPlayOps._kx * a._gainX, 4), Math.Round(AgentPlayOps._ky * a._gainY, 4) };
            r["jitter"] = a._reversals.Count;
        }

        private static void Prune(Queue<float> q, float before)
        {
            while (q.Count > 0 && q.Peek() < before) q.Dequeue();
        }

        private void Update()
        {
            float now = Time.realtimeSinceStartup;
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 1e-4f, 0.1f);
            Camera cam = AgentPlayOps.Cam();
            Transform player = AgentPlayOps.PlayerTransform();
            if (cam == null || player == null) { _haveLast = _haveView = false; Release(); SetFocus("idle"); return; }

            // Where the player goes: its smoothed velocity, or the walker's heading.
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
            bool walking = Heading.HasValue && now - HeadingAt < 0.3f;
            if (walking)
            {
                Vector3 way = new Vector3(Heading.Value.x, 0f, Heading.Value.z);
                if (way.sqrMagnitude > 1e-4f) { flat = way.normalized * Mathf.Max(flat.magnitude, 1f); moving = true; }
            }

            // The view as it is.
            Vector3 f = cam.transform.forward;
            float yaw = AgentPlayOps.ViewYaw(cam.transform), pitch = AgentPlayOps.Pitch(f); // yaw from the right vector: defined at any pitch
            float dYaw = 0f, dPitch = 0f;
            if (_haveView)
            {
                dYaw = Mathf.DeltaAngle(_lastYaw, yaw);
                dPitch = pitch - _lastPitch;
            }
            else
            {
                _measYaw = yaw;
                _measPitch = pitch;
            }

            // Something else turned the view (a cut, a scripted snap): far more than this frame's own turn.
            bool someoneElse = _haveView && Mathf.Abs(dYaw) > Mathf.Abs(_sentYaw) + 25f;
            _lastYaw = yaw;
            _lastPitch = pitch;
            _haveView = true;
            float m = 1f - Mathf.Exp(-dt / 0.1f); // the measured view, low-passed: head bob is not an error
            _measYaw += Mathf.DeltaAngle(_measYaw, yaw) * m;
            _measPitch += (pitch - _measPitch) * m;
            _camRate = (Mathf.Abs(dYaw) + Mathf.Abs(dPitch)) / dt;
            _camQuiet = _camRate < Tuning.QuietRate ? _camQuiet + dt : 0f;
            if (_driving) TrackJitter(dYaw, dPitch, dt, now);

            // Did the view answer? Many frames of turning sent without any turn seen: look input is off; wait a moment.
            if (_driving)
            {
                _sentDeg += Mathf.Abs(_sentYaw) + Mathf.Abs(_sentPitch);
                _seenDeg += Mathf.Abs(dYaw) + Mathf.Abs(dPitch);
                if (++_checkFrames >= 20)
                {
                    if (_sentDeg > 8f && _seenDeg < 0.3f) _backOffUntil = now + 1f;
                    _sentDeg = _seenDeg = 0f;
                    _checkFrames = 0;
                }
            }

            _sentYaw = _sentPitch = 0f;

            string op = AgentPlayOps.CurrentOp;
            if (Current == Mode.Off) { Release(); SetFocus("off"); return; }
            // Ops that aim leave the view alone, except while they walk (an interact's approach): then look along the way.
            if (op != null && HandsOff.Contains(op) && !walking) { Release(); SetFocus("the agent aims (" + op + ")"); return; }
            bool gameView = false;
            try { gameView = GameHasView != null && GameHasView(); }
            catch (Exception) { }
            if (gameView || someoneElse) _backOffUntil = Mathf.Max(_backOffUntil, now + 0.6f);
            if (now < _backOffUntil) { Release(); SetFocus("the game has the view"); return; }
            if (!AgentPlayOps.Calibrated) { Release(); StartCoroutine(AgentPlayOps.Calibrate()); return; } // the mouse's real gain, once

            // What to look at.
            Vector3 eye = cam.transform.position;
            Sample(now, eye, flat, moving);
            UpdateScores(now, dt, moving);
            string key = null, why;
            float smooth = Tuning.HotspotSmooth, speed = Tuning.HotspotSpeed, goalYaw = 0f, goalPitch = 0f;
            if (_pinned != null && now < _pinnedUntil)
            {
                key = PinKey;
                why = _pinnedLabel;
                AimAt(key, _pinned.position + _pinnedPoint, eye, dt, out goalYaw, out goalPitch);
            }
            else
            {
                if (_pinnedUntil > 0f) { _pinnedUntil = 0f; _pinned = null; }
                Choose(now, dt);
                why = "idle";
                if (_current == _walk)
                {
                    key = WalkKey;
                    why = _walk.H.Label;
                    float rise = Mathf.Clamp(_vel.y / Mathf.Max(0.5f, new Vector3(_vel.x, 0f, _vel.z).magnitude), -0.6f, 0.6f);
                    goalYaw = AgentPlayOps.Yaw(flat);
                    goalPitch = Mathf.Atan(rise * 0.6f - 0.08f) * Mathf.Rad2Deg;
                    smooth = Tuning.WalkSmooth;
                    speed = Tuning.WalkSpeed;
                    if (!moving) key = null;
                }
                else if (_current != null)
                {
                    Hotspot h = _current.H;
                    if (!ReferenceEquals(h.Target, null) && h.Target == null) { Leave(now); } // destroyed
                    else
                    {
                        key = _current.Key;
                        why = h.Label;
                        AimAt(key, h.Target != null ? h.Target.position + h.Point : h.Point, eye, dt, out goalYaw, out goalPitch);
                    }
                }
            }

            SetFocus(why);
            if (key == null) { Release(); return; }
            Steer(key, yaw, pitch, goalYaw, goalPitch, smooth, speed, dt);
        }

        // ------------------------------------------------------------------ choosing

        private static string KeyOf(Hotspot h)
        {
            Vector3 p = h.Point;
            string at = Mathf.RoundToInt(p.x * 4f) + "," + Mathf.RoundToInt(p.y * 4f) + "," + Mathf.RoundToInt(p.z * 4f);
            // Object identity without instance ids (obsolete in Unity 6.6): the reference's hash plus its name.
            return h.Target != null ? "t" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(h.Target) + h.Target.name + ":" + at : "p" + at;
        }

        /// <summary>Asks the project for hotspots (four times a second) and folds them into the tracked ones.</summary>
        private void Sample(float now, Vector3 eye, Vector3 flat, bool moving)
        {
            if (Current != Mode.Auto || now < _nextSample) return;
            _nextSample = now + 0.25f;
            List<Hotspot> list = null;
            try { list = Hotspots?.Invoke(); }
            catch (Exception e) { Focus = "hotspots failed: " + e.Message; }

            // Several labels can name one thing ("Niko (speaking)", "Niko (SomeBeat)", "Niko"): one hotspot, its best entry.
            _merge.Clear();
            if (list != null)
                foreach (Hotspot h in list)
                {
                    if (h.Target == null && h.Point == Vector3.zero) continue;
                    float raw = Score(h, eye, flat, moving);
                    string key = KeyOf(h);
                    if (!_merge.TryGetValue(key, out var have) || raw > have.raw || (raw == have.raw && h.Weight > have.h.Weight))
                        _merge[key] = (h, raw);
                }

            foreach (var pair in _merge)
            {
                if (!_spots.TryGetValue(pair.Key, out Spot s))
                {
                    if (_spots.Count >= 64) continue;
                    s = new Spot { Key = pair.Key, Score = pair.Value.raw, FreshAt = now, PrevWeight = pair.Value.h.Weight };
                    _spots[pair.Key] = s;
                }
                else if (pair.Value.h.Weight >= s.PrevWeight + 0.3f && pair.Value.h.Weight >= 0.5f)
                {
                    s.Bored = 0f; // clearly more important than before: something new happened there
                    s.FreshAt = now;
                }

                s.H = pair.Value.h;
                s.Raw = pair.Value.raw;
                s.PrevWeight = pair.Value.h.Weight;
                s.LastSeen = now;
            }

            _drop.Clear();
            foreach (Spot s in _spots.Values)
            {
                if (_merge.ContainsKey(s.Key)) continue;
                if (now - s.LastSeen > Tuning.Grace) { s.Raw = 0f; s.PrevWeight = 0f; }
                if (s != _current && s.Score < 0.01f && s.Bored < 0.01f && now - s.LastSeen > 3f) _drop.Add(s.Key);
            }

            foreach (string key in _drop) _spots.Remove(key);
        }

        /// <summary>Smooths each score toward its raw value, bores what is looked at, rests what is not.</summary>
        private void UpdateScores(float now, float dt, bool moving)
        {
            float rise = 1f - Mathf.Exp(-dt / Tuning.ScoreRise), fall = 1f - Mathf.Exp(-dt / Tuning.ScoreFall);
            foreach (Spot s in _spots.Values)
            {
                if (Current != Mode.Auto) s.Raw = 0f;
                s.Score += (s.Raw - s.Score) * (s.Raw > s.Score ? rise : fall);
                if (s == _current) s.Bored = Mathf.Min(1f, s.Bored + dt / (s.H.Weight >= 1f ? Tuning.CriticalBoredAfter : Tuning.BoredAfter));
                else s.Bored = Mathf.Max(0f, s.Bored - dt / Tuning.BoredRecover);
            }

            _walk.Raw = moving && Current != Mode.Off ? Tuning.WalkScore : 0f;
            _walk.Score += (_walk.Raw - _walk.Score) * (_walk.Raw > _walk.Score ? rise : fall);
        }

        private float Effective(Spot s) => s == _walk ? s.Score : s.Score * (1f - Tuning.BoredWeight * s.Bored);

        /// <summary>Keeps the focus while it matters; takes another only after a hold, by a clear margin, never straight back.</summary>
        private void Choose(float now, float dt)
        {
            if (_current != null)
            {
                bool gone = _current != _walk && !_spots.ContainsKey(_current.Key);
                bool wrongMode = Current == Mode.Movement && _current != _walk;
                _fadingFor = _current.Score < Tuning.DropScore ? _fadingFor + dt : 0f;
                if (gone || wrongMode || _fadingFor >= Tuning.DropAfter) Leave(now);
            }

            Spot best = null;
            float bestEff = 0f;
            Consider(_walk, now, ref best, ref bestEff);
            if (Current == Mode.Auto)
                foreach (Spot s in _spots.Values)
                    Consider(s, now, ref best, ref bestEff);
            if (best == null || bestEff < Tuning.StartScore) return;
            if (_current == null) { Enter(best, now); return; }

            bool urgent = best.H.Weight >= 1f && _current.H.Weight < 1f;
            if (now - _since < (urgent ? Tuning.UrgentDwell : Tuning.MinDwell)) return;
            float currentEff = Effective(_current);
            if (bestEff < (urgent ? currentEff : currentEff * Tuning.SwitchRatio + Tuning.SwitchMargin)) return;
            Enter(best, now);
        }

        private void Consider(Spot s, float now, ref Spot best, ref float bestEff)
        {
            if (s == _current) return;
            // The point just left waits ReturnBlock s; only a critical one that became fresh since (a new line, a live hazard) may not.
            if (s != _walk && s.Key == _leftKey && now - _leftAt < Tuning.ReturnBlock && !(s.FreshAt > _leftAt && s.H.Weight >= 1f)) return;
            float e = Effective(s);
            if (e > bestEff) { best = s; bestEff = e; }
        }

        private void Enter(Spot s, float now)
        {
            if (_current != null) Leave(now);
            _current = s;
            _since = now;
            _fadingFor = 0f;
            _switches.Enqueue(now);
        }

        private void Leave(float now)
        {
            if (_current == null) return;
            _leftKey = _current.Key;
            _leftAt = now;
            _current = null;
            _fadingFor = 0f;
        }

        private static float Score(Hotspot h, Vector3 eye, Vector3 flat, bool moving)
        {
            if (!ReferenceEquals(h.Target, null) && h.Target == null) return 0f; // destroyed
            Vector3 p = h.Target != null ? h.Target.position + h.Point : h.Point;
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

        // ------------------------------------------------------------------ turning

        /// <summary>The goal direction toward a (possibly moving) point: low-passed, so a swaying head bone does not shake the view.</summary>
        private void AimAt(string key, Vector3 point, Vector3 eye, float dt, out float yaw, out float pitch)
        {
            if (key != _goalKey) _goalPoint = point;
            else _goalPoint = Vector3.Lerp(_goalPoint, point, 1f - Mathf.Exp(-dt / Tuning.TargetSmooth));
            Vector3 d = _goalPoint - eye;
            if (d.sqrMagnitude < 1e-6f) d = Vector3.forward;
            yaw = AgentPlayOps.Yaw(d);
            pitch = AgentPlayOps.Pitch(d);
        }

        /// <summary>
        /// Eases the virtual head toward the goal and sends only its movement as mouse movement (open loop: nothing the game
        /// adds, its smoothing or head bob, is fed back). Once the head is still and the view has settled, the remaining error
        /// is measured: it teaches the mouse gain and, when larger than <see cref="Tuning.ResyncAngle"/>, is turned away.
        /// </summary>
        private void Steer(string key, float yaw, float pitch, float goalYaw, float goalPitch, float smooth, float speed, float dt)
        {
            if (!_driving)
            {
                _driving = true;
                _viewYaw = _measYaw = yaw;
                _viewPitch = _measPitch = pitch;
                _yawVel = _pitchVel = 0f;
                _stillFor = 0f;
                _goalKey = null;
                StartSegment();
            }

            if (key != _goalKey)
            {
                _goalKey = key;
                _goalYaw = goalYaw;
                _goalPitch = goalPitch;
            }
            else
            {
                // Soft dead zone: the goal follows the target only by what exceeds the zone (sway is ignored, a walk is followed).
                float ey = Mathf.DeltaAngle(_goalYaw, goalYaw), ep = goalPitch - _goalPitch;
                if (Mathf.Abs(ey) > Tuning.DeadZone) _goalYaw += ey - Mathf.Sign(ey) * Tuning.DeadZone;
                if (Mathf.Abs(ep) > Tuning.DeadZone) _goalPitch += ep - Mathf.Sign(ep) * Tuning.DeadZone;
            }

            float wantPitch = Mathf.Clamp(_goalPitch, -Mathf.Min(Tuning.MaxPitch, _pitchDown), Mathf.Min(Tuning.MaxPitch, _pitchUp));
            float ny = Mathf.SmoothDampAngle(_viewYaw, _goalYaw, ref _yawVel, smooth, speed, dt);
            float np = Mathf.SmoothDamp(_viewPitch, wantPitch, ref _pitchVel, smooth, speed, dt);
            float turnYaw = Mathf.DeltaAngle(_viewYaw, ny), turnPitch = np - _viewPitch;
            _viewYaw = Mathf.Repeat(ny + 180f, 360f) - 180f;
            _viewPitch = np;

            // Still: measure what the view really did, learn from it, and correct a real error (not head bob).
            bool still = Mathf.Abs(_yawVel) < 3f && Mathf.Abs(_pitchVel) < 3f;
            _stillFor = still ? _stillFor + dt : 0f;
            // The view stopped short (or past) of where the head went: correct the rest at once and learn the gain from it, so the
            // next turn lands in one move. Waiting a fixed time here made every short turn a visible stop-and-go (step by step).
            bool quiet = _camQuiet >= Tuning.QuietTime;
            if ((still && quiet && _stillFor >= 0.05f) || _stillFor >= Tuning.Settle)
            {
                float limit = quiet ? Tuning.ResyncQuiet : Tuning.ResyncAngle;
                Learn(ref _gainX, Mathf.DeltaAngle(_segStartYaw, _measYaw), _segYaw, false);
                Learn(ref _gainY, _measPitch - _segStartPitch, _segPitch, Mathf.Abs(wantPitch) > 40f);
                float ey = Mathf.DeltaAngle(_viewYaw, _measYaw), ep = _measPitch - _viewPitch;
                // Stuck short of a steep pitch it was driven to: the game's limit. Never push past it again (no fight, no back-off loop).
                if (Mathf.Abs(ep) > limit && Mathf.Abs(_measPitch) > 30f && Mathf.Sign(_viewPitch - _measPitch) == Mathf.Sign(_measPitch))
                {
                    if (_measPitch > 0f) _pitchUp = Mathf.Min(_pitchUp, _measPitch);
                    else _pitchDown = Mathf.Min(_pitchDown, -_measPitch);
                }

                if (Mathf.Abs(ey) > limit) _viewYaw += ey;
                if (Mathf.Abs(ep) > limit) _viewPitch += ep;
                _stillFor = 0f;
                StartSegment();
            }

            // Overshoot guard: the game's view lags what was sent, so with the right gain it never turns further than that.
            // Turning clearly further means the gain is too low (every turn would overshoot): correct it now and restart from here.
            float seenYaw = Mathf.DeltaAngle(_segStartYaw, _measYaw), seenPitch = _measPitch - _segStartPitch;
            if (Overshot(seenYaw, _segYaw))
            {
                _gainX = Mathf.Clamp(_gainX * Mathf.Abs(seenYaw / _segYaw), 0.2f, 5f);
                _viewYaw = _measYaw;
                _yawVel = 0f;
                _segYaw = 0f;
                _segStartYaw = _measYaw;
                turnYaw = 0f;
            }

            if (Overshot(seenPitch, _segPitch))
            {
                _gainY = Mathf.Clamp(_gainY * Mathf.Abs(seenPitch / _segPitch), 0.2f, 5f);
                _viewPitch = _measPitch;
                _pitchVel = 0f;
                _segPitch = 0f;
                _segStartPitch = _measPitch;
                turnPitch = 0f;
            }

            if (turnYaw == 0f && turnPitch == 0f) return;
            _segYaw += turnYaw;
            _segPitch += turnPitch;
            _sentYaw = turnYaw;
            _sentPitch = turnPitch;
            float kx = AgentPlayOps._kx * _gainX, ky = AgentPlayOps._ky * _gainY;
            float dx = Mathf.Abs(kx) > 1e-4f ? turnYaw / kx : 0f, dy = Mathf.Abs(ky) > 1e-4f ? turnPitch / ky : 0f;
            AgentPlayInput.MouseDelta(new Vector2(Mathf.Clamp(dx, -400f, 400f), Mathf.Clamp(dy, -400f, 400f)));
        }

        private static bool Overshot(float seen, float sent) =>
            Mathf.Abs(sent) > 3f && Mathf.Sign(seen) == Mathf.Sign(sent) && Mathf.Abs(seen) > Mathf.Abs(sent) * 1.25f + 2f;

        private void StartSegment()
        {
            _segYaw = _segPitch = 0f;
            _segStartYaw = _measYaw;
            _segStartPitch = _measPitch;
        }

        /// <summary>The view turned <paramref name="seen"/> for <paramref name="sent"/> degrees sent: move the gain part way toward the truth.</summary>
        private static void Learn(ref float gain, float seen, float sent, bool clamped)
        {
            if (clamped || Mathf.Abs(sent) < Tuning.LearnFrom) return;
            float ratio = seen / sent;
            if (ratio < 0.3f || ratio > 3f) return; // the game turned it too, or nothing answered: learn nothing
            gain = Mathf.Clamp(gain * Mathf.Lerp(1f, ratio, Tuning.LearnRate), 0.2f, 5f);
        }

        /// <summary>Stops steering; the next steer starts from the view as it is.</summary>
        private void Release()
        {
            _driving = false;
            _goalKey = null;
            _yawVel = _pitchVel = 0f;
            _sentDeg = _seenDeg = 0f;
            _checkFrames = 0;
        }

        private void TrackJitter(float dYaw, float dPitch, float dt, float now)
        {
            float k = 1f - Mathf.Exp(-dt * 15f);
            _yawRate += (dYaw / dt - _yawRate) * k;
            _pitchRate += (dPitch / dt - _pitchRate) * k;
            Reversal(_yawRate, ref _yawSign, now);
            Reversal(_pitchRate, ref _pitchSign, now);
        }

        private void Reversal(float rate, ref int sign, float now)
        {
            if (Mathf.Abs(rate) < 6f) return;
            int s = rate > 0f ? 1 : -1;
            if (sign != 0 && s != sign) _reversals.Enqueue(now);
            sign = s;
            if (_reversals.Count > 200) _reversals.Dequeue();
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
