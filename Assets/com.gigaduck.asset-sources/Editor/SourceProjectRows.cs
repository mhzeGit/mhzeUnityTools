using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Gigaduck.AssetSources
{
    /// <summary>Draws source-opening and reveal buttons at the right edge of selected linked assets in Project list rows.</summary>
    [InitializeOnLoad]
    internal static class SourceProjectRows
    {
        internal sealed class Group
        {
            internal string application;
            internal bool available;
            internal GUIContent content;
        }
        private sealed class CachedRow { internal double expires; internal Group[] groups; }
        private static readonly Dictionary<string, CachedRow> rows = new Dictionary<string, CachedRow>();
        private static GUIStyle iconButtonStyle;
        private static readonly Dictionary<string, Texture> icons = new Dictionary<string, Texture>();
        private static GUIContent folderContent;
        internal static GUIContent FolderContent => folderContent ?? (folderContent = new GUIContent(
            EditorGUIUtility.IconContent("Folder Icon").image,
            "Show linked source in Explorer (choose a file when several are linked)"));
        private const float ButtonWidth = 22;

        static SourceProjectRows()
        {
#if UNITY_6000_0_OR_NEWER
            EditorApplication.projectWindowItemByEntityIdOnGUI += Draw;
#else
            EditorApplication.projectWindowItemOnGUI += Draw;
#endif
            EditorApplication.projectChanged += rows.Clear;
        }

#if UNITY_6000_0_OR_NEWER
        private static void Draw(EntityId entityId, Rect row)
        {
            if (!Selection.Contains(entityId) || row.height > 22 || row.width < 100) return;
            Draw(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(entityId)), row);
        }
#endif
        private static void Draw(string guid, Rect row)
        {
#if !UNITY_6000_0_OR_NEWER
            if (!Selection.assetGUIDs.Contains(guid)) return;
#endif
            if (string.IsNullOrEmpty(guid) || row.height > 22 || row.width < 100) return;
            if (!rows.TryGetValue(guid, out var cached) || cached.expires < EditorApplication.timeSinceStartup)
            {
                cached = new CachedRow { expires = EditorApplication.timeSinceStartup + 2 };
                // Invalid records must not break the Project browser; setup/validation reports their details.
                try { cached.groups = ReadGroups(AssetSources.Registry, guid); }
                catch { cached.groups = new Group[0]; }
                rows[guid] = cached;
            }
            var groups = cached.groups;
            if (groups.Length == 0 || row.width < (groups.Length + 1) * ButtonWidth + 60) return;
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            var folder = ButtonRect(row, 0);
            using (new EditorGUI.DisabledScope(!groups.Any(x => x.available)))
                if (IconButton(folder, FolderContent))
                    AssetSources.ChooseSource(assetPath, null, reveal: true);
            for (int i = 0; i < groups.Length; i++)
            {
                var group = groups[i];
                using (new EditorGUI.DisabledScope(!group.available))
                    if (IconButton(ButtonRect(row, groups.Length - i), group.content))
                        AssetSources.ChooseSource(assetPath, group.application, connect: true);
            }
        }

        internal static Rect ButtonRect(Rect row, int fromRight)
            => new Rect(row.xMax - 2 - (fromRight + 1) * ButtonWidth, row.y + (row.height - 16) / 2, ButtonWidth - 2, 16);

        internal static Rect IconRect(Rect button) => new Rect(button.center.x - 8, button.center.y - 8, 16, 16);

        private static bool IconButton(Rect rect, GUIContent content)
        {
            if (iconButtonStyle == null) iconButtonStyle = new GUIStyle(GUIStyle.none) { imagePosition = ImagePosition.TextOnly };
            bool clicked = GUI.Button(rect, content, iconButtonStyle);
            if (Event.current.type == EventType.Repaint)
            {
                if (GUI.enabled && rect.Contains(Event.current.mousePosition))
                    EditorGUI.DrawRect(rect, new Color(1, 1, 1, .12f));
                Color previous = GUI.color;
                if (!GUI.enabled) GUI.color = new Color(previous.r, previous.g, previous.b, previous.a * .35f);
                GUI.DrawTexture(IconRect(rect), content.image, ScaleMode.ScaleToFit, true);
                GUI.color = previous;
            }
            return clicked;
        }

        internal static Texture ApplicationIcon(string application)
        {
            if (!icons.TryGetValue(application, out var icon))
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AssetSources).Assembly);
                string root = package != null ? package.assetPath : "Assets/com.gigaduck.asset-sources";
                icon = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "/Editor/Icons/" + application + ".png");
                if (icon == null) icon = EditorGUIUtility.IconContent("DefaultAsset Icon").image;
                icons[application] = icon;
            }
            return icon;
        }

        internal static Group[] ReadGroups(SourceRegistry registry, string guid)
        {
            return registry.GetRecord(guid).sources.GroupBy(x => x.applicationId).OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(group =>
                {
                    bool available = group.Any(source =>
                    {
                        try { return File.Exists(registry.Resolve(source, false)); }
                        catch { return false; }
                    });
                    string tooltip = "Open " + group.Key + " source + enable live sync\n" +
                        string.Join("\n", group.Select(x => x.relativePath)) + (available ? "" : "\nSource unavailable; use Setup or Relink Sources.");
                    return new Group { application = group.Key, available = available,
                        content = new GUIContent(ApplicationIcon(group.Key), tooltip) };
                }).ToArray();
        }
    }
}
