#if DEVELOPMENT_BUILD || UNITY_EDITOR || AGENT_PLAY
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if AGENTIC_INPUT_SYSTEM
using System.Linq;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace Gigaduck.AgenticTools.AgentPlay
{
    /// <summary>
    /// The one virtual keyboard and mouse every agent harness and op drives (a hidden player has no real input and never has
    /// focus). Keys use Input System names (<c>W</c>, <c>E</c>, <c>Space</c>, <c>LeftShift</c>). Without the Input System
    /// package every call fails with <see cref="Missing"/>.
    /// </summary>
    public static class AgentPlayInput
    {
        public const string Missing = "keys and mouse need the Input System package (com.unity.inputsystem)";

#if AGENTIC_INPUT_SYSTEM
        private static Keyboard _keyboard;
        private static Mouse _mouse;
        private static bool _left, _right;
        private static readonly HashSet<Key> Held = new HashSet<Key>();

        public static bool Available => true;

        /// <summary>Adds the virtual devices once (again after a domain reload removed them).</summary>
        public static void Ensure()
        {
            if (_keyboard != null && _keyboard.added && _mouse != null && _mouse.added) return;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _keyboard = InputSystem.AddDevice<Keyboard>("AgentKeyboard");
            _keyboard.MakeCurrent();
            _mouse = InputSystem.AddDevice<Mouse>("AgentMouse");
            _mouse.MakeCurrent();
            Held.Clear();
        }

        public static bool IsKey(string name) => TryKey(name, out _);

        private static bool TryKey(string name, out Key key) => Enum.TryParse(name ?? "", true, out key) && key != Key.None;

        /// <summary>Holds (down) or releases a key until changed again. Returns an error text, or null.</summary>
        public static string SetKey(string name, bool down)
        {
            if (!TryKey(name, out Key key)) return "unknown key '" + name + "' (Input System names: W, E, Space, LeftShift ...)";
            Ensure();
            if (down) Held.Add(key);
            else Held.Remove(key);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Held.ToArray()));
            return null;
        }

        public static bool IsHeld(string name) => TryKey(name, out Key key) && Held.Contains(key);

        /// <summary>Lets go of every key and mouse button (after a route or when a command fails half way).</summary>
        public static void ReleaseAll()
        {
            if (_keyboard == null) return;
            Held.Clear();
            _left = _right = false;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            SendMouse(Vector2.zero);
        }

        /// <summary>Presses a key for <paramref name="seconds"/> of real time, then releases it.</summary>
        public static IEnumerator Press(string name, float seconds, Action<string> fail)
        {
            string error = SetKey(name, true);
            if (error != null) { fail(error); yield break; }
            yield return new WaitForSecondsRealtime(Mathf.Max(0.02f, seconds));
            SetKey(name, false);
            yield return null;
        }

        /// <summary>Mouse movement for one frame (Input System deltas last one frame; send one per frame).</summary>
        public static void MouseDelta(Vector2 delta)
        {
            Ensure();
            SendMouse(delta);
        }

        /// <summary>Where the pointer is, in screen pixels from the bottom left.</summary>
        public static Vector2 Pointer
        {
            get { Ensure(); return _mouse.position.ReadValue(); }
        }

        /// <summary>Puts the pointer at a screen position in pixels (from the bottom left).</summary>
        public static void SetPointer(Vector2 position)
        {
            Ensure();
            var state = new MouseState { position = position };
            InputSystem.QueueStateEvent(_mouse, Buttons(state));
        }

        public static void SetButton(int button, bool down)
        {
            Ensure();
            if (button == 1) _right = down;
            else _left = down;
            SendMouse(Vector2.zero);
        }

        /// <summary>Clicks a mouse button (0 left, 1 right) for <paramref name="seconds"/> of real time.</summary>
        public static IEnumerator Click(int button, float seconds)
        {
            SetButton(button, true);
            yield return new WaitForSecondsRealtime(Mathf.Max(0.02f, seconds));
            SetButton(button, false);
            yield return null;
        }

        /// <summary>Turns the view with mouse movement spread over some frames.</summary>
        public static IEnumerator Look(Vector2 total, int frames)
        {
            frames = Mathf.Max(1, frames);
            Vector2 step = total / frames;
            for (int i = 0; i < frames; i++)
            {
                MouseDelta(step);
                yield return null;
            }
        }

        private static void SendMouse(Vector2 delta)
        {
            var state = new MouseState { position = _mouse.position.ReadValue(), delta = delta };
            InputSystem.QueueStateEvent(_mouse, Buttons(state));
        }

        private static MouseState Buttons(MouseState state) => state
            .WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left, _left)
            .WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Right, _right);
#else
        public static bool Available => false;
        public static void Ensure() { }
        public static bool IsKey(string name) => false;
        public static string SetKey(string name, bool down) => Missing;
        public static bool IsHeld(string name) => false;
        public static void ReleaseAll() { }
        public static IEnumerator Press(string name, float seconds, Action<string> fail) { fail(Missing); yield break; }
        public static void MouseDelta(Vector2 delta) { }
        public static Vector2 Pointer => Vector2.zero;
        public static void SetPointer(Vector2 position) { }
        public static void SetButton(int button, bool down) { }
        public static IEnumerator Click(int button, float seconds) { yield break; }
        public static IEnumerator Look(Vector2 total, int frames) { yield break; }
#endif
    }
}
#endif
