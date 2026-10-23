using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Gigaduck.AssetSources
{
    /// <summary>Draws source-opening and reveal buttons at the right edge of linked assets in Project list rows.</summary>
    [InitializeOnLoad]
    internal static class SourceProjectRows
    {
        internal sealed class Group
        {
            internal string application;
            internal bool available;
            internal GUIContent content;
            internal Color color;
        }
        private sealed class CachedRow { internal double expires; internal Group[] groups; }
        private static readonly Dictionary<string, CachedRow> rows = new Dictionary<string, CachedRow>();
        private static GUIStyle badgeStyle;
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
            if (row.height > 22 || row.width < 100) return;
            Draw(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(entityId)), row);
        }
#endif
        private static void Draw(string guid, Rect row)
        {
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
            if (badgeStyle == null)
                badgeStyle = new GUIStyle(EditorStyles.miniButton) { fontSize = 9, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0) };
            var folder = ButtonRect(row, 0);
            using (new EditorGUI.DisabledScope(!groups.Any(x => x.available)))
                if (GUI.Button(folder, new GUIContent(EditorGUIUtility.IconContent("Folder Icon").image,
                    "Show linked source in Explorer (choose a file when several are linked)"), badgeStyle))
                    AssetSources.ChooseSource(assetPath, null, reveal: true);
            for (int i = 0; i < groups.Length; i++)
            {
                var group = groups[i];
                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = group.color;
                using (new EditorGUI.DisabledScope(!group.available))
                    if (GUI.Button(ButtonRect(row, groups.Length - i), group.content, badgeStyle))
                        AssetSources.ChooseSource(assetPath, group.application, connect: true);
                GUI.backgroundColor = previous;
            }
        }

        internal static Rect ButtonRect(Rect row, int fromRight)
            => new Rect(row.xMax - 2 - (fromRight + 1) * ButtonWidth, row.y + (row.height - 16) / 2, ButtonWidth - 2, 16);

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
                    string label;
                    Color color;
                    switch (group.Key)
                    {
                        case "blender": label = "Bl"; color = new Color(1, .58f, .22f); break;
                        case "graphmat": label = "Gm"; color = new Color(.72f, .48f, 1); break;
                        case "painter": label = "Pt"; color = new Color(.36f, .9f, .68f); break;
                        case "designer": label = "Ds"; color = new Color(.6f, .9f, .35f); break;
                        case "photoshop": label = "Ps"; color = new Color(.3f, .72f, 1); break;
                        case "affinity": label = "Af"; color = new Color(.95f, .48f, .7f); break;
                        default: label = group.Key.Substring(0, Math.Min(2, group.Key.Length)); color = Color.white; break;
                    }
                    string tooltip = "Open " + group.Key + " source + enable live sync\n" +
                        string.Join("\n", group.Select(x => x.relativePath)) + (available ? "" : "\nSource unavailable; use Setup or Relink Sources.");
                    return new Group { application = group.Key, available = available, color = color,
                        content = new GUIContent(label, tooltip) };
                }).ToArray();
        }
    }
}
