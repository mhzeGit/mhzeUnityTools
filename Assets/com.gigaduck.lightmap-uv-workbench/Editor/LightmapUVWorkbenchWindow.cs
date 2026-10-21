using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Gigaduck.LightmapWorkbench
{
    /// <summary>Centrally stages and reimports individual or multiple FBX assets' lightmap UV generation settings.</summary>
    public sealed class LightmapUVWorkbenchWindow : EditorWindow
    {
        [Serializable]
        private sealed class Entry
        {
            public string guid;
            public LightmapUVSettings baseline, draft;
            public string Path => AssetDatabase.GUIDToAssetPath(guid);
            public bool Dirty => !draft.Equals(baseline);
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();
        [SerializeField] private List<string> selected = new List<string>();
        [SerializeField] private string search = "";
        private Vector2 listScroll, settingsScroll;
        private bool refreshNeeded = true;

        [MenuItem("Tools/Gigaduck/Lightmap UV Workbench")]
        public static void Open()
        {
            var window = GetWindow<LightmapUVWorkbenchWindow>("Lightmap UV Workbench");
            window.minSize = new Vector2(950, 450);
            window.Show();
        }

        private void OnEnable()
        {
            saveChangesMessage = "Apply and reimport pending FBX lightmap UV changes before closing?";
            refreshNeeded = true;
            EditorApplication.projectChanged += RequestRefresh;
        }
        private void OnDisable() => EditorApplication.projectChanged -= RequestRefresh;
        private void OnFocus() => RequestRefresh();
        private void RequestRefresh() { refreshNeeded = true; Repaint(); }

        private void RefreshAssets()
        {
            var previous = entries.ToDictionary(e => e.guid);
            var found = new List<Entry>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
                if (previous.TryGetValue(guid, out var old) && old.Dirty) found.Add(old);
                else if (AssetImporter.GetAtPath(path) is ModelImporter importer)
                {
                    var settings = LightmapUVSettings.Read(importer);
                    found.Add(new Entry { guid = guid, baseline = settings, draft = settings });
                }
            }
            // Keep missing assets' drafts visible so refresh cannot silently discard pending work.
            found.AddRange(entries.Where(e => e.Dirty && found.All(f => f.guid != e.guid)));
            entries = found.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();
            selected.RemoveAll(guid => entries.All(e => e.guid != guid));
            hasUnsavedChanges = entries.Any(e => e.Dirty);
            refreshNeeded = false;
        }

        public override void SaveChanges()
        {
            ApplyPending();
            if (hasUnsavedChanges) throw new InvalidOperationException("Some FBX changes remain unapplied.");
            base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            foreach (var entry in entries) entry.draft = entry.baseline;
            base.DiscardChanges();
            RequestRefresh();
        }

        private void ApplyPending()
        {
            var pending = entries.Where(e => e.Dirty).ToArray();
            try
            {
                // Validate the whole batch before changing any importer.
                foreach (var entry in pending)
                {
                    entry.draft.Validate();
                    if (!(AssetImporter.GetAtPath(entry.Path) is ModelImporter))
                        throw new InvalidOperationException("FBX is missing: " + entry.guid);
                    if ((File.GetAttributes(entry.Path + ".meta") & FileAttributes.ReadOnly) != 0)
                        throw new InvalidOperationException("Importer metadata is read-only: " + entry.Path);
                }
                for (int i = 0; i < pending.Length; i++)
                {
                    var entry = pending[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Reimport lightmap UVs", entry.Path, (float)i / pending.Length)) break;
                    var importer = (ModelImporter)AssetImporter.GetAtPath(entry.Path);
                    entry.draft.ApplyTo(importer, entry.baseline);
                    importer.SaveAndReimport();
                    entry.baseline = entry.draft = LightmapUVSettings.Read((ModelImporter)AssetImporter.GetAtPath(entry.Path));
                }
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                ShowNotification(new GUIContent(error.Message));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                hasUnsavedChanges = entries.Any(e => e.Dirty);
                Repaint();
            }
        }

        private void OnGUI()
        {
            if (refreshNeeded) RefreshAssets();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("FBX project assets", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton)) RefreshAssets();
            }
            search = EditorGUILayout.TextField("Search name or asset path", search);
            var visible = entries.Where(e => e.Path.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            var targets = entries.Where(e => selected.Contains(e.guid)).ToArray();
            int hidden = targets.Count(e => !visible.Contains(e));
            EditorGUILayout.LabelField($"{visible.Length} / {entries.Count} FBX files   ·   {targets.Length} selected" +
                (hidden > 0 ? $" ({hidden} hidden by search)" : ""), EditorStyles.miniLabel);
            EditorGUILayout.HelpBox("Edit Generate Lightmap UVs on the FBX assets themselves. Changes stay pending until Apply & Reimport; all instances of each model use the result.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(position.width * .52f)))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Select filtered")) selected = visible.Select(e => e.guid).ToList();
                        if (GUILayout.Button("Clear")) selected.Clear();
                        if (GUILayout.Button("Use Project selection"))
                        {
                            var paths = Selection.objects.Select(AssetDatabase.GetAssetPath).Where(p => !string.IsNullOrEmpty(p)).ToArray();
                            selected = entries.Where(e => paths.Any(p => e.Path == p ||
                                (AssetDatabase.IsValidFolder(p) && e.Path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase))))
                                .Select(e => e.guid).ToList();
                        }
                    }
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                    {
                        GUILayout.Label("FBX asset (click to ping; hover for full path)");
                        GUILayout.Label("Generate UVs", GUILayout.Width(90));
                    }
                    listScroll = EditorGUILayout.BeginScrollView(listScroll);
                    foreach (var entry in visible)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            bool was = selected.Contains(entry.guid);
                            bool now = GUILayout.Toggle(was, GUIContent.none, GUILayout.Width(18));
                            if (now != was) { if (now) selected.Add(entry.guid); else selected.Remove(entry.guid); }
                            string path = entry.Path;
                            string label = string.IsNullOrEmpty(path) ? "Missing: " + entry.guid : System.IO.Path.GetFileName(path);
                            if (GUILayout.Button(new GUIContent((entry.Dirty ? "* " : "") + label, path), EditorStyles.linkLabel, GUILayout.ExpandWidth(true)))
                                EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(path));
                            entry.draft.generate = EditorGUILayout.Toggle(entry.draft.generate, GUILayout.Width(90));
                        }
                    }
                    EditorGUILayout.EndScrollView();
                }
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    targets = entries.Where(e => selected.Contains(e.guid)).ToArray();
                    settingsScroll = EditorGUILayout.BeginScrollView(settingsScroll);
                    DrawSettings(targets);
                    EditorGUILayout.EndScrollView();
                }
            }
            hasUnsavedChanges = entries.Any(e => e.Dirty);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"{entries.Count(e => e.Dirty)} FBX files have pending changes", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!hasUnsavedChanges || EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning))
                {
                    if (GUILayout.Button("Revert all pending")) DiscardChanges();
                    if (GUILayout.Button("Apply & Reimport changed FBX files")) ApplyPending();
                }
            }
        }

        private void DrawSettings(Entry[] targets)
        {
            EditorGUILayout.LabelField($"Lightmap UV generation · {targets.Length} selected", EditorStyles.boldLabel);
            if (targets.Length == 0)
            {
                EditorGUILayout.HelpBox("Check FBX assets on the left to edit one or many together. Mixed values show a dash; editing a field changes only that field on the selected assets.", MessageType.Info);
                return;
            }
            float oldWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 195;
            try
            {
                Field(targets, e => e.draft.generate, (e,v) => e.draft.generate = v, v => EditorGUILayout.Toggle("Generate Lightmap UVs", v));
                Field(targets, e => e.draft.hardAngle, (e,v) => e.draft.hardAngle = v, v => Mathf.Round(EditorGUILayout.Slider("Hard Angle", v, 0, 180)));
                Field(targets, e => e.draft.angleError, (e,v) => e.draft.angleError = v, v => Mathf.Round(EditorGUILayout.Slider("Angle Error", v, 1, 75)));
                Field(targets, e => e.draft.areaError, (e,v) => e.draft.areaError = v, v => Mathf.Round(EditorGUILayout.Slider("Area Error", v, 1, 75)));
                Field(targets, e => e.draft.marginMethod, (e,v) => e.draft.marginMethod = v, v => (ModelImporterSecondaryUVMarginMethod)EditorGUILayout.EnumPopup("Margin Method", v));
                if (targets.Any(e => e.draft.marginMethod == ModelImporterSecondaryUVMarginMethod.Manual))
                    Field(targets, e => e.draft.packMargin, (e,v) => e.draft.packMargin = v, v => Mathf.Round(EditorGUILayout.Slider("Pack Margin (pixels)", v, 1, 64)));
                if (targets.Any(e => e.draft.marginMethod == ModelImporterSecondaryUVMarginMethod.Calculate))
                {
                    Field(targets, e => e.draft.minResolution, (e,v) => e.draft.minResolution = v, v => Mathf.Round(EditorGUILayout.DelayedFloatField("Min Lightmap Resolution", v)));
                    Field(targets, e => e.draft.minScale, (e,v) => e.draft.minScale = v, v => EditorGUILayout.DelayedFloatField("Min Object Scale", v));
                }
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("Min Lightmap Resolution controls UV packing, not the scene's bake resolution. Calculate also uses Min Object Scale; set it for the model's actual placement scale to avoid excessive margins.", MessageType.None);
            }
            finally { EditorGUIUtility.labelWidth = oldWidth; }
        }

        private static void Field<T>(Entry[] targets, Func<Entry,T> read, Action<Entry,T> write, Func<T,T> draw)
        {
            T first = read(targets[0]);
            EditorGUI.showMixedValue = targets.Any(e => !EqualityComparer<T>.Default.Equals(first, read(e)));
            EditorGUI.BeginChangeCheck();
            T value = draw(first);
            if (EditorGUI.EndChangeCheck()) foreach (var entry in targets) write(entry, value);
            EditorGUI.showMixedValue = false;
        }
    }
}
