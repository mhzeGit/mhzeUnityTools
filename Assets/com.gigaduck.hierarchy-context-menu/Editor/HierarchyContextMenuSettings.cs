using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Gigaduck.HierarchyContextMenu
{
    static class HierarchyContextMenuSettings
    {
        private const string PREFIX = "HierarchyContextMenu.";

        private const string PREF_ENABLED = PREFIX + "Enabled";
        private const string PREF_SHOW_ICONS = PREFIX + "ShowIcons";
        private const string PREF_BG_COLOR = PREFIX + "BackgroundColor";
        private const string PREF_HOVER_COLOR = PREFIX + "HoverColor";
        private const string PREF_BORDER_COLOR = PREFIX + "BorderColor";
        private const string PREF_TEXT_COLOR = PREFIX + "TextColor";
        private const string PREF_DISABLED_TEXT_COLOR = PREFIX + "DisabledTextColor";
        private const string PREF_SEARCH_BG_COLOR = PREFIX + "SearchBackgroundColor";
        private const string PREF_DIM_COLOR = PREFIX + "DimColor";

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PREF_ENABLED, true);
            set => EditorPrefs.SetBool(PREF_ENABLED, value);
        }

        public static bool ShowIcons
        {
            get => EditorPrefs.GetBool(PREF_SHOW_ICONS, true);
            set => EditorPrefs.SetBool(PREF_SHOW_ICONS, value);
        }

        public static Color BackgroundColor
        {
            get => GetColor(PREF_BG_COLOR, new Color(0.12f, 0.12f, 0.12f));
            set => SetColor(PREF_BG_COLOR, value);
        }

        public static Color HoverColor
        {
            get => GetColor(PREF_HOVER_COLOR, new Color(0.22f, 0.42f, 0.75f));
            set => SetColor(PREF_HOVER_COLOR, value);
        }

        public static Color BorderColor
        {
            get => GetColor(PREF_BORDER_COLOR, new Color(0.25f, 0.25f, 0.25f));
            set => SetColor(PREF_BORDER_COLOR, value);
        }

        public static Color TextColor
        {
            get => GetColor(PREF_TEXT_COLOR, new Color(0.85f, 0.85f, 0.85f));
            set => SetColor(PREF_TEXT_COLOR, value);
        }

        public static Color DisabledTextColor
        {
            get => GetColor(PREF_DISABLED_TEXT_COLOR, new Color(0.4f, 0.4f, 0.4f));
            set => SetColor(PREF_DISABLED_TEXT_COLOR, value);
        }

        public static Color SearchBackgroundColor
        {
            get => GetColor(PREF_SEARCH_BG_COLOR, new Color(0.17f, 0.17f, 0.17f));
            set => SetColor(PREF_SEARCH_BG_COLOR, value);
        }

        public static Color DimColor
        {
            get => GetColor(PREF_DIM_COLOR, new Color(0.55f, 0.55f, 0.55f));
            set => SetColor(PREF_DIM_COLOR, value);
        }

        public static void ResetAll()
        {
            EditorPrefs.DeleteKey(PREF_ENABLED);
            EditorPrefs.DeleteKey(PREF_SHOW_ICONS);
            EditorPrefs.DeleteKey(PREF_BG_COLOR);
            EditorPrefs.DeleteKey(PREF_HOVER_COLOR);
            EditorPrefs.DeleteKey(PREF_BORDER_COLOR);
            EditorPrefs.DeleteKey(PREF_TEXT_COLOR);
            EditorPrefs.DeleteKey(PREF_DISABLED_TEXT_COLOR);
            EditorPrefs.DeleteKey(PREF_SEARCH_BG_COLOR);
            EditorPrefs.DeleteKey(PREF_DIM_COLOR);
        }

        private static Color GetColor(string key, Color defaultValue)
        {
            var str = EditorPrefs.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(str))
                return defaultValue;
            if (ColorUtility.TryParseHtmlString("#" + str, out var color))
                return color;
            return defaultValue;
        }

        private static void SetColor(string key, Color color)
        {
            EditorPrefs.SetString(key, ColorUtility.ToHtmlStringRGBA(color));
        }
    }

    static class HierarchyContextMenuSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            var provider = new SettingsProvider("Preferences/Hierarchy Context Menu", SettingsScope.User)
            {
                label = "Hierarchy Context Menu",
                guiHandler = DrawPreferencesGUI,
                keywords = new HashSet<string>(new[]
                {
                    "Hierarchy", "Context", "Menu", "Right-click", "Icons", "Color", "Disable"
                })
            };
            return provider;
        }

        private static void DrawPreferencesGUI(string searchContext)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("General", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            var enabled = EditorGUILayout.Toggle(
                new GUIContent("Enabled", "Enable or disable the custom hierarchy context menu"),
                HierarchyContextMenuSettings.Enabled
            );
            if (enabled != HierarchyContextMenuSettings.Enabled)
                HierarchyContextMenuSettings.Enabled = enabled;

            var showIcons = EditorGUILayout.Toggle(
                new GUIContent("Show Icons", "Show icons next to menu items"),
                HierarchyContextMenuSettings.ShowIcons
            );
            if (showIcons != HierarchyContextMenuSettings.ShowIcons)
                HierarchyContextMenuSettings.ShowIcons = showIcons;

            EditorGUI.indentLevel--;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Colors", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            DrawColorField("Background", "Background color of the context menu", HierarchyContextMenuSettings.BackgroundColor, v => HierarchyContextMenuSettings.BackgroundColor = v);
            DrawColorField("Search Background", "Background color of the search field", HierarchyContextMenuSettings.SearchBackgroundColor, v => HierarchyContextMenuSettings.SearchBackgroundColor = v);
            DrawColorField("Border", "Border color of the context menu", HierarchyContextMenuSettings.BorderColor, v => HierarchyContextMenuSettings.BorderColor = v);
            DrawColorField("Text", "Text color of menu items", HierarchyContextMenuSettings.TextColor, v => HierarchyContextMenuSettings.TextColor = v);
            DrawColorField("Disabled Text", "Text color for disabled menu items", HierarchyContextMenuSettings.DisabledTextColor, v => HierarchyContextMenuSettings.DisabledTextColor = v);
            DrawColorField("Hover", "Hover/selection highlight color", HierarchyContextMenuSettings.HoverColor, v => HierarchyContextMenuSettings.HoverColor = v);
            DrawColorField("Dim", "Color for arrows and secondary elements", HierarchyContextMenuSettings.DimColor, v => HierarchyContextMenuSettings.DimColor = v);

            EditorGUI.indentLevel--;

            EditorGUILayout.Space(15);
            if (GUILayout.Button("Reset to Defaults", GUILayout.Width(140)))
            {
                HierarchyContextMenuSettings.ResetAll();
            }
        }

        private static void DrawColorField(string label, string tooltip, Color currentValue, System.Action<Color> setter)
        {
            var color = EditorGUILayout.ColorField(
                new GUIContent(label, tooltip),
                currentValue
            );
            if (color != currentValue)
                setter(color);
        }
    }
}
