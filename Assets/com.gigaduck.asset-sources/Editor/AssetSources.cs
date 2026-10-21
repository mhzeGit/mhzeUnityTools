using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gigaduck.AssetSources
{
    /// <summary>Public editor automation API and explicit context commands for an asset's external sources.</summary>
    public static class AssetSources
    {
        private static SourceRegistry registry;
        public static SourceRegistry Registry => registry ?? (registry = new SourceRegistry(Directory.GetParent(Application.dataPath).FullName));
        public static readonly string[] ApplicationIds = { "default", "blender", "graphmat", "painter", "designer", "photoshop", "affinity" };

        public static string GuidForAsset(string assetPath)
        {
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid) || AssetDatabase.IsValidFolder(assetPath)) throw new ArgumentException("Choose an existing Unity asset file: " + assetPath);
            return guid;
        }

        public static AssetSourceRecord Get(string assetPath) => Registry.GetRecord(GuidForAsset(assetPath));

        public static void Link(string assetPath, string libraryId, string relativePath, string applicationId = "default", string sourceId = "model", string label = null)
        {
            Registry.Link(GuidForAsset(assetPath), new SourceLink
            { id = sourceId, libraryId = libraryId, relativePath = relativePath, applicationId = applicationId, label = label });
        }

        public static void Unlink(string assetPath, string sourceId) => Registry.Unlink(GuidForAsset(assetPath), sourceId);
        public static string[] ValidateAll() => Registry.ValidateAll(guid => !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid))).ToArray();

        public static string ApplicationForPath(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".blend": return "blender";
                case ".gmat": return "graphmat";
                case ".spp": return "painter";
                case ".sbs": return "designer";
                case ".psd": case ".psb": return "photoshop";
                case ".afphoto": case ".afdesign": return "affinity";
                default: return "default";
            }
        }

        public static void Open(string assetPath, string sourceId)
        {
            var source = Get(assetPath).sources.FirstOrDefault(x => x.id == sourceId)
                ?? throw new InvalidOperationException("No source '" + sourceId + "'. Use Setup / Relink Sources first.");
            Open(source);
        }

        public static void Open(SourceLink source)
        {
            string path = Registry.Resolve(source);
            string executable = Registry.GetLocalSettings().applications.FirstOrDefault(x => x.id == source.applicationId)?.path;
            if (string.IsNullOrEmpty(executable)) { EditorUtility.OpenWithDefaultApp(path); return; }
            if (!File.Exists(executable)) throw new FileNotFoundException("Application moved; locate it again in Asset Sources > Applications.", executable);
            // The executable and source argument are separate. No command shell or editable command template.
            var info = new ProcessStartInfo { FileName = executable, Arguments = "\"" + path.Replace("\"", "\\\"") + "\"", UseShellExecute = false };
            Process.Start(info);
        }

        public static void Reveal(SourceLink source) => EditorUtility.RevealInFinder(Registry.Resolve(source));

        public static string SelectedAssetPath()
        {
            Object selected = Selection.activeObject;
            string path = AssetDatabase.GetAssetPath(selected);
            if (!string.IsNullOrEmpty(path)) return AssetDatabase.IsValidFolder(path) ? null : path;
            var go = selected as GameObject;
            if (go == null && selected is Component component) go = component.gameObject;
            if (go == null) return null;
            var skin = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var mesh = go.GetComponentInChildren<MeshFilter>(true);
            return AssetDatabase.GetAssetPath(skin != null ? skin.sharedMesh : mesh != null ? mesh.sharedMesh : null);
        }

        [MenuItem("Assets/Asset Sources/Setup or Relink Sources...", false, 2000)]
        private static void Setup() => AssetSourcesWindow.ShowWindow();
        [MenuItem("Assets/Asset Sources/Setup or Relink Sources...", true)]
        private static bool CanSetup() => !string.IsNullOrEmpty(SelectedAssetPath());

        [MenuItem("Assets/Asset Sources/Open Blender Source", false, 2010)]
        private static void Blender() => OpenApplication("blender");
        [MenuItem("Assets/Asset Sources/Open Blender Source", true)]
        private static bool CanBlender() => HasApplication("blender");
        [MenuItem("Assets/Asset Sources/Open Graphmat Source", false, 2011)]
        private static void Graphmat() => OpenApplication("graphmat");
        [MenuItem("Assets/Asset Sources/Open Graphmat Source", true)]
        private static bool CanGraphmat() => HasApplication("graphmat");
        [MenuItem("Assets/Asset Sources/Open Painter Source", false, 2012)]
        private static void Painter() => OpenApplication("painter");
        [MenuItem("Assets/Asset Sources/Open Painter Source", true)]
        private static bool CanPainter() => HasApplication("painter");
        [MenuItem("Assets/Asset Sources/Open Source...", false, 2020)]
        private static void OpenAny() => ChooseSource(null);
        [MenuItem("Assets/Asset Sources/Open Source...", true)]
        private static bool CanOpenAny() => HasApplication(null);
        [MenuItem("Assets/Asset Sources/Show Source in Explorer", false, 2021)]
        private static void RevealAny() => ChooseSource(null, true);
        [MenuItem("Assets/Asset Sources/Show Source in Explorer", true)]
        private static bool CanRevealAny() => HasApplication(null);

        private static bool HasApplication(string application)
        {
            try
            {
                string path = SelectedAssetPath();
                return !string.IsNullOrEmpty(path) && Get(path).sources.Any(x => application == null || x.applicationId == application);
            }
            catch { return false; }
        }

        private static void OpenApplication(string application) => ChooseSource(application);

        private static void ChooseSource(string application, bool reveal = false)
        {
            Run(() =>
            {
                var sources = Get(SelectedAssetPath()).sources.Where(x => application == null || x.applicationId == application).ToArray();
                if (sources.Length == 0) throw new InvalidOperationException("Link a source first using Setup or Relink Sources.");
                if (sources.Length == 1) { if (reveal) Reveal(sources[0]); else Open(sources[0]); return; }
                var menu = new GenericMenu();
                foreach (var item in sources)
                {
                    var source = item;
                    menu.AddItem(new GUIContent((string.IsNullOrEmpty(source.label) ? source.id : source.label).Replace('/', ' ') + " (" + Path.GetFileName(source.relativePath) + ")"), false,
                        () => Run(() => { if (reveal) Reveal(source); else Open(source); }));
                }
                menu.ShowAsContext();
            });
        }

        internal static void Run(Action action)
        {
            try { action(); }
            catch (Exception ex) { EditorUtility.DisplayDialog("Asset Sources", ex.Message, "OK"); }
        }
    }
}
