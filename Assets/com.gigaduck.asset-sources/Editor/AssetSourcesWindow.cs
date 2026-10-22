using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Gigaduck.AssetSources
{
    /// <summary>Explicit setup, source relinking and per-machine library/application configuration.</summary>
    public sealed class AssetSourcesWindow : EditorWindow
    {
        private int tab;
        private Vector2 scroll;
        private string libraryId = "project-art", libraryName = "Project Art", environmentVariable = "";
        private string sourceId = "model", sourceLabel = "", customApplication = "", sourcePath = "", message = "";
        private int libraryIndex, applicationIndex;
        private bool error;
        private bool liveSync = true;
        private UnityEngine.Object target;

        [MenuItem("Tools/Gigaduck/Asset Sources")]
        public static void ShowWindow()
        {
            var window = GetWindow<AssetSourcesWindow>("Asset Sources");
            window.minSize = new Vector2(480, 360);
            window.target = Selection.activeObject;
            window.Show();
        }

        private void OnEnable() { Selection.selectionChanged += OnSelectionChanged; }
        private void OnDisable() { Selection.selectionChanged -= OnSelectionChanged; }
        private void OnSelectionChanged() { target = Selection.activeObject; message = ""; Repaint(); }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);
            tab = GUILayout.Toolbar(tab, new[] { "Sources", "Libraries", "Applications" });
            EditorGUILayout.Space(6);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            try
            {
                if (tab == 0) DrawSources();
                else if (tab == 1) DrawLibraries();
                else DrawApplications();
            }
            catch (Exception ex) { EditorGUILayout.HelpBox(ex.Message, MessageType.Error); }
            EditorGUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, error ? MessageType.Error : MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reload Links")) { AssetSources.Registry.Reload(); Repaint(); }
            if (GUILayout.Button("Validate All Links")) Act(() =>
            {
                string[] errors = AssetSources.ValidateAll();
                if (errors.Length > 0) throw new InvalidDataException(string.Join("\n", errors));
                message = "All registered links resolve successfully.";
            });
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);
        }

        private void DrawSources()
        {
            EditorGUILayout.HelpBox("1. Add and locate a library.  2. Select a Unity asset and link its sources.\nThen right-click the asset > Asset Sources > Open Blender Source (or another source).", MessageType.Info);
            target = EditorGUILayout.ObjectField("Unity asset", target, typeof(UnityEngine.Object), false);
            string assetPath = AssetDatabase.GetAssetPath(target);
            if (string.IsNullOrEmpty(assetPath)) assetPath = AssetSources.SelectedAssetPath();
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
            { EditorGUILayout.HelpBox("Select an FBX, texture, prefab or another Unity asset file.", MessageType.None); return; }
            EditorGUILayout.SelectableLabel(assetPath, GUILayout.Height(18));
            var record = AssetSources.Get(assetPath);
            foreach (var source in record.sources)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(string.IsNullOrEmpty(source.label) ? source.id : source.label, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(source.applicationId + " / " + source.libraryId);
                EditorGUILayout.SelectableLabel(source.relativePath, GUILayout.Height(18));
                bool exists = false;
                try { AssetSources.Registry.Resolve(source); exists = true; }
                catch (Exception ex) { EditorGUILayout.HelpBox(ex.Message, MessageType.Warning); }
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(!exists))
                {
                    if (GUILayout.Button("Open + Live Link")) Act(() => AssetSources.OpenWithLiveLink(assetPath, source.id));
                    if (GUILayout.Button("Reveal")) Act(() => AssetSources.Reveal(source));
                    if (GUILayout.Button(source.noSync ? "Enable Live Sync" : "Disable Live Sync")) Act(() =>
                    {
                        AssetSources.Link(assetPath, source.libraryId, source.relativePath, source.applicationId, source.id, source.label, !source.noSync);
                        message = source.noSync ? "Live sync enabled; open the source to connect Blender, or save the master to update Unity." : "Live sync disabled; the source stays linked for navigation.";
                    });
                }
                if (GUILayout.Button("Relink"))
                {
                    var chosen = source;
                    Act(() =>
                    {
                        string file = EditorUtility.OpenFilePanel("Relink " + chosen.id, AssetSources.Registry.LibraryRoot(chosen.libraryId), "");
                        if (string.IsNullOrEmpty(file)) return;
                        AssetSources.Link(assetPath, chosen.libraryId, AssetSources.Registry.RelativePath(chosen.libraryId, file), chosen.applicationId, chosen.id, chosen.label, chosen.noSync);
                        message = "Source relinked.";
                    });
                }
                if (GUILayout.Button("Unlink")) Act(() => { AssetSources.Unlink(assetPath, source.id); message = "Link removed; the source file is unchanged."; });
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField(SourceLiveLink.Status(source), EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Link a Source", EditorStyles.boldLabel);
            var libraries = AssetSources.Registry.GetLibraries().libraries;
            if (libraries.Count == 0)
            {
                if (GUILayout.Button("Set Up a Library")) tab = 1;
                return;
            }
            libraryIndex = EditorGUILayout.Popup("Library", Mathf.Clamp(libraryIndex, 0, libraries.Count - 1), libraries.Select(x => x.displayName).ToArray());
            string library = libraries[libraryIndex].id;
            sourceId = EditorGUILayout.TextField("Source ID", sourceId);
            sourceLabel = EditorGUILayout.TextField("Label (optional)", sourceLabel);
            applicationIndex = EditorGUILayout.Popup("Open with", applicationIndex, AssetSources.ApplicationIds.Concat(new[] { "custom..." }).ToArray());
            if (applicationIndex == AssetSources.ApplicationIds.Length) customApplication = EditorGUILayout.TextField("Application ID", customApplication);
            EditorGUILayout.BeginHorizontal();
            sourcePath = EditorGUILayout.TextField("Relative path", sourcePath);
            if (GUILayout.Button("Browse", GUILayout.Width(70))) Act(() =>
            {
                string file = EditorUtility.OpenFilePanel("Choose editable source", AssetSources.Registry.LibraryRoot(library), "");
                if (string.IsNullOrEmpty(file)) return;
                sourcePath = AssetSources.Registry.RelativePath(library, file);
                string app = AssetSources.ApplicationForPath(file);
                applicationIndex = Array.IndexOf(AssetSources.ApplicationIds, app);
            });
            EditorGUILayout.EndHorizontal();
            liveSync = EditorGUILayout.Toggle("Live sync on save", liveSync);
            if (GUILayout.Button("Save Source Link")) Act(() =>
            {
                string app = applicationIndex == AssetSources.ApplicationIds.Length ? customApplication : AssetSources.ApplicationIds[applicationIndex];
                AssetSources.Link(assetPath, library, sourcePath, app, sourceId, sourceLabel, !liveSync);
                message = "Source linked. Right-click this asset > Asset Sources to open it.";
            });
            EditorGUILayout.LabelField("Reusing a Source ID replaces that link; use different IDs for additional files.", EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawLibraries()
        {
            EditorGUILayout.HelpBox("Library IDs and source paths travel with the project. Locate each library once on each PC. Keep the folder structure inside it unchanged.", MessageType.Info);
            foreach (var library in AssetSources.Registry.GetLibraries().libraries)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(library.displayName + " (" + library.id + ")", EditorStyles.boldLabel);
                try { EditorGUILayout.SelectableLabel(AssetSources.Registry.LibraryRoot(library.id), GUILayout.Height(36)); }
                catch (Exception ex) { EditorGUILayout.HelpBox(ex.Message, MessageType.Warning); }
                if (!string.IsNullOrEmpty(library.environmentVariable)) EditorGUILayout.LabelField("Environment override: " + library.environmentVariable);
                if (GUILayout.Button("Locate Library on This PC")) Act(() =>
                {
                    string root = EditorUtility.OpenFolderPanel("Locate " + library.displayName, "", "");
                    if (!string.IsNullOrEmpty(root)) AssetSources.Registry.SetLibraryRoot(library.id, root);
                });
                if (GUILayout.Button("Edit Definition"))
                { libraryId = library.id; libraryName = library.displayName; environmentVariable = library.environmentVariable; }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Add / Update Library", EditorStyles.boldLabel);
            libraryId = EditorGUILayout.TextField("Stable library ID", libraryId);
            libraryName = EditorGUILayout.TextField("Display name", libraryName);
            environmentVariable = EditorGUILayout.TextField("Environment override", environmentVariable);
            if (GUILayout.Button("Save Library Definition")) Act(() =>
            {
                AssetSources.Registry.DefineLibrary(libraryId, libraryName, environmentVariable);
                message = "Library saved. Locate its folder on this PC above.";
            });
        }

        private void DrawApplications()
        {
            EditorGUILayout.HelpBox("Without an executable override, files open in their OS-associated application. Overrides are local to this PC and project. Each application must accept the source filename as an argument.", MessageType.Info);
            var local = AssetSources.Registry.GetLocalSettings();
            foreach (string app in AssetSources.ApplicationIds.Where(x => x != "default").Concat(local.applications.Select(x => x.id)).Distinct())
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(app, EditorStyles.boldLabel);
                string executable = local.applications.FirstOrDefault(x => x.id == app)?.path;
                EditorGUILayout.LabelField(string.IsNullOrEmpty(executable) ? "Use OS file association" : executable, EditorStyles.wordWrappedLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Locate Application")) Act(() =>
                {
                    string path = EditorUtility.OpenFilePanel("Choose " + app + " executable", "", "");
                    if (!string.IsNullOrEmpty(path)) AssetSources.Registry.SetApplication(app, path);
                });
                if (GUILayout.Button("Use OS Association")) Act(() => AssetSources.Registry.SetApplication(app, null));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
            customApplication = EditorGUILayout.TextField("Custom application ID", customApplication);
            if (GUILayout.Button("Configure Custom Application")) Act(() =>
            {
                SourceRegistry.ValidateId(customApplication);
                string path = EditorUtility.OpenFilePanel("Choose application executable", "", "");
                if (!string.IsNullOrEmpty(path)) AssetSources.Registry.SetApplication(customApplication, path);
            });
        }

        private void Act(Action action)
        {
            try { error = false; message = ""; action(); }
            catch (Exception ex) { error = true; message = ex.Message; }
            Repaint();
        }
    }
}
