using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace mhze.HierarchyContextMenu
{
    static class ShortcutResolver
    {
        private static Dictionary<string, string> _shortcuts;
        private static bool _initialized;
        private static bool _managerAvailable;
        private static string _modPrefix;
        private static object _shortcutManagerInstance;

        private static Type _smType;
        private static MethodInfo _getBindingMethod;
        private static PropertyInfo _keyComboSeqProp;
        private static PropertyInfo _keyProp;
        private static PropertyInfo _modProp;

        public static string GetShortcut(string menuPath)
        {
            if (!_initialized)
                Initialize();

            if (_shortcuts.TryGetValue(menuPath, out var shortcut))
                return shortcut;

            if (_managerAvailable)
            {
                var fromManager = TryResolveShortcut(menuPath);
                if (fromManager != null)
                    _shortcuts[menuPath] = fromManager;
                return fromManager;
            }

            return null;
        }

        private static void Initialize()
        {
            _shortcuts = new Dictionary<string, string>();
            _modPrefix = Application.platform == RuntimePlatform.OSXEditor ? "Cmd" : "Ctrl";

            var methods = TypeCache.GetMethodsWithAttribute<MenuItem>();
            foreach (var method in methods)
            {
                foreach (var attr in method.GetCustomAttributes<MenuItem>(false))
                {
                    var fullPath = attr.menuItem;
                    var result = ParseShortcut(fullPath);
                    if (result.shortcut != null && !_shortcuts.ContainsKey(result.cleanPath))
                        _shortcuts[result.cleanPath] = result.shortcut;
                }
            }

            InitShortcutManagerReflection();

            _initialized = true;
        }

        private static void InitShortcutManagerReflection()
        {
            try
            {
                _smType = Type.GetType("UnityEditor.ShortcutManagement.ShortcutManager, UnityEditor");
                if (_smType == null) return;

                var instanceProp = _smType.GetProperty("instance",
                    BindingFlags.Public | BindingFlags.Static);
                if (instanceProp == null) return;

                _shortcutManagerInstance = instanceProp.GetValue(null);
                if (_shortcutManagerInstance == null) return;

                _getBindingMethod = _smType.GetMethod("GetShortcutBinding",
                    new[] { typeof(string) });
                if (_getBindingMethod == null) return;

                var bindingType = typeof(Editor).Assembly.GetType(
                    "UnityEditor.ShortcutManagement.ShortcutBinding");
                if (bindingType == null) return;

                _keyComboSeqProp = bindingType.GetProperty("keyCombinationSequence")
                                ?? bindingType.GetProperty("combinations");
                if (_keyComboSeqProp == null) return;

                var comboType = typeof(Editor).Assembly.GetType(
                    "UnityEditor.ShortcutManagement.KeyCombination");
                if (comboType == null) return;

                _keyProp = comboType.GetProperty("keyCode");
                _modProp = comboType.GetProperty("modifiers");

                if (_keyProp == null || _modProp == null) return;

                _managerAvailable = true;

                TryBulkPopulate();
            }
            catch
            {
                _managerAvailable = false;
            }
        }

        private static void TryBulkPopulate()
        {
            if (!_managerAvailable) return;

            try
            {
                var getIdsMethod = _smType.GetMethod("GetAvailableShortcutIds", Type.EmptyTypes);
                if (getIdsMethod == null) return;

                var ids = getIdsMethod.Invoke(_shortcutManagerInstance, null) as IEnumerable<string>;
                if (ids == null) return;

                foreach (var id in ids)
                {
                    if (!id.StartsWith("Main Menu/")) continue;

                    var menuPath = id.Substring("Main Menu/".Length);
                    var bindingText = GetBindingForId(id);
                    if (bindingText != null)
                        _shortcuts[menuPath] = bindingText;
                }
            }
            catch { }
        }

        private static string TryResolveShortcut(string menuPath)
        {
            if (!_managerAvailable) return null;

            string[] prefixes = { "Main Menu/", "" };

            foreach (var prefix in prefixes)
            {
                var result = GetBindingForId(prefix + menuPath);
                if (result != null) return result;
            }

            return null;
        }

        private static string GetBindingForId(string shortcutId)
        {
            try
            {
                var binding = _getBindingMethod.Invoke(_shortcutManagerInstance,
                    new object[] { shortcutId });
                if (binding == null) return null;

                var seq = _keyComboSeqProp.GetValue(binding) as IEnumerable;
                if (seq == null) return null;

                foreach (var combo in seq)
                {
                    var keyCode = (KeyCode)_keyProp.GetValue(combo);
                    var modifiers = (EventModifiers)_modProp.GetValue(combo);
                    if (keyCode == KeyCode.None) continue;

                    return FormatKeyCombo(keyCode, modifiers);
                }
            }
            catch { }

            return null;
        }

        private static string FormatKeyCombo(KeyCode key, EventModifiers modifiers)
        {
            var parts = new List<string>();

            if ((modifiers & EventModifiers.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & EventModifiers.Shift) != 0) parts.Add("Shift");
            if ((modifiers & EventModifiers.Alt) != 0) parts.Add("Alt");
            if ((modifiers & EventModifiers.Command) != 0) parts.Add("Cmd");

            parts.Add(KeyCodeToShortcutString(key));

            return string.Join("+", parts);
        }

        private static string KeyCodeToShortcutString(KeyCode key)
        {
            if (key >= KeyCode.A && key <= KeyCode.Z) return key.ToString();
            if (key >= KeyCode.F1 && key <= KeyCode.F12) return key.ToString();
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return key.ToString().Substring("Alpha".Length);
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) return "Num" + key.ToString().Substring("Keypad".Length);

            switch (key)
            {
                case KeyCode.Return: return "Enter";
                case KeyCode.Escape: return "Esc";
                case KeyCode.Space: return "Space";
                case KeyCode.Tab: return "Tab";
                case KeyCode.Backspace: return "Backspace";
                case KeyCode.Delete: return "Del";
                case KeyCode.Insert: return "Ins";
                case KeyCode.Home: return "Home";
                case KeyCode.End: return "End";
                case KeyCode.PageUp: return "PageUp";
                case KeyCode.PageDown: return "PageDown";
                case KeyCode.LeftArrow: return "Left";
                case KeyCode.RightArrow: return "Right";
                case KeyCode.UpArrow: return "Up";
                case KeyCode.DownArrow: return "Down";
                case KeyCode.LeftShift:
                case KeyCode.RightShift: return "Shift";
                case KeyCode.LeftControl:
                case KeyCode.RightControl: return "Ctrl";
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt: return "Alt";
                case KeyCode.LeftCommand:
                case KeyCode.RightCommand: return "Cmd";
                case KeyCode.Semicolon: return ";";
                case KeyCode.Equals: return "=";
                case KeyCode.Comma: return ",";
                case KeyCode.Minus: return "-";
                case KeyCode.Period: return ".";
                case KeyCode.Slash: return "/";
                case KeyCode.BackQuote: return "`";
                case KeyCode.LeftBracket: return "[";
                case KeyCode.RightBracket: return "]";
                case KeyCode.Backslash: return "\\";
                case KeyCode.Quote: return "'";
                default: return key.ToString();
            }
        }

        internal static (string shortcut, string cleanPath) ParseShortcut(string menuItem)
        {
            var lastSpace = menuItem.LastIndexOf(' ');
            if (lastSpace < 0)
                return (null, menuItem);

            var token = menuItem.Substring(lastSpace + 1);
            if (string.IsNullOrEmpty(token))
                return (null, menuItem);

            var hasModifier = token.IndexOfAny(new char[] { '%', '#', '&' }) >= 0;
            var isSpecialKey = token.Length > 1 && token[0] == '_';

            if (!hasModifier && !isSpecialKey)
                return (null, menuItem);

            var result = "";
            var i = 0;

            while (i < token.Length)
            {
                if (token[i] == '%') { result += _modPrefix + "+"; i++; }
                else if (token[i] == '#') { result += "Shift+"; i++; }
                else if (token[i] == '&') { result += "Alt+"; i++; }
                else break;
            }

            if (i < token.Length)
            {
                if (token[i] == '_')
                {
                    result += token.Substring(i + 1);
                }
                else
                {
                    result += char.ToUpper(token[i]);
                }
            }
            else
            {
                return (null, menuItem);
            }

            return (result, menuItem.Substring(0, lastSpace));
        }

        public static void Reset()
        {
            _initialized = false;
            _shortcuts = null;
            _managerAvailable = false;
            _shortcutManagerInstance = null;
        }
    }
}
