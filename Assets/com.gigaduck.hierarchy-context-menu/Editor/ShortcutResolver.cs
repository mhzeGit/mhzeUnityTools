using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Gigaduck.HierarchyContextMenu
{
    static class ShortcutResolver
    {
        private static Dictionary<string, string> _shortcuts;
        private static bool _initialized;
        private static string _modPrefix;

        public static string GetShortcut(string menuPath)
        {
            if (!_initialized)
                Initialize();

            _shortcuts.TryGetValue(menuPath, out var shortcut);
            return shortcut;
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

            _initialized = true;
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
        }
    }
}
