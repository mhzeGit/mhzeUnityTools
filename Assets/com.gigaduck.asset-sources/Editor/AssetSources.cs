using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
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

        public static void Link(string assetPath, string libraryId, string relativePath, string applicationId = "default", string sourceId = "model", string label = null, bool noSync = false)
        {
            Registry.Link(GuidForAsset(assetPath), new SourceLink
            { id = sourceId, libraryId = libraryId, relativePath = relativePath, applicationId = applicationId, label = label, noSync = noSync });
            SourceLiveLink.Refresh();
        }

        public static void Unlink(string assetPath, string sourceId)
        {
            Registry.Unlink(GuidForAsset(assetPath), sourceId);
            SourceLiveLink.Refresh();
        }
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

        /// <summary>Resolves an application's executable the same way Open does, without launching it.</summary>
        public static string ApplicationExecutable(string applicationId, string sampleSourcePath = null)
            => SourceExport.Executable(sampleSourcePath ?? "." + applicationId, applicationId);

        public static void Open(string assetPath, string sourceId)
        {
            var source = Get(assetPath).sources.FirstOrDefault(x => x.id == sourceId)
                ?? throw new InvalidOperationException("No source '" + sourceId + "'. Use Setup / Relink Sources first.");
            Open(source);
        }

        /// <summary>Explicitly enables the selected source's live link and opens its editor in one action.</summary>
        public static void OpenWithLiveLink(string assetPath, string sourceId)
        {
            var source = Get(assetPath).sources.FirstOrDefault(x => x.id == sourceId)
                ?? throw new InvalidOperationException("No source '" + sourceId + "'. Use Setup / Relink Sources first.");
            if (source.noSync)
            {
                Link(assetPath, source.libraryId, source.relativePath, source.applicationId, source.id, source.label, false);
                source.noSync = false;
            }
            Open(source);
        }

        public static void Open(SourceLink source)
        {
            string path;
            try { path = Registry.Resolve(source); }
            catch (Exception ex)
            {
                Debug.LogError("[Asset Sources] Cannot open '" + source.id + "' (" + source.relativePath + "): " + ex.Message);
                throw;
            }

            Debug.Log("[Asset Sources] Opening " + source.applicationId + " source '" + source.id + "': " + path);
            string executable = Registry.GetLocalSettings().applications.FirstOrDefault(x => x.id == source.applicationId)?.path;
            if (string.IsNullOrEmpty(executable))
                try { executable = SourceExport.Executable(path, source.applicationId); }
                catch (Exception ex) { Debug.LogWarning("[Asset Sources] Could not resolve the " + source.applicationId + " executable: " + ex.Message); }

            if (!string.IsNullOrEmpty(executable))
            {
                if (!File.Exists(executable))
                {
                    Debug.LogError("[Asset Sources] Application executable is missing: " + executable + ". Relink it in Asset Sources > Applications.");
                    throw new FileNotFoundException("Application moved; locate it again in Asset Sources > Applications.", executable);
                }
                SourceLiveLink.Start(source);
                if (SourceLiveLink.BlenderConnected(source))
                {
                    Debug.Log("[Asset Sources] Blender is already connected for " + path + ".");
                    return;
                }
                // The executable and source argument are separate. No command shell or editable command template.
                var info = new ProcessStartInfo { FileName = executable, Arguments = (source.applicationId == "painter" ? "--enable-remote-scripting " : "") + SourceExport.Quote(path) +
                    (Path.GetExtension(path).Equals(".blend", StringComparison.OrdinalIgnoreCase) ? " " + SourceLiveLink.BlenderArguments(source) : ""), UseShellExecute = false, CreateNoWindow = true };
                try
                {
                    var process = Process.Start(info);
                    Debug.Log("[Asset Sources] Launched " + executable + (process == null ? "" : " (PID " + process.Id + ")") + " for " + path + ".");
                }
                catch (Exception ex)
                {
                    Debug.LogError("[Asset Sources] Could not launch " + executable + " for " + path + ": " + ex.GetBaseException().Message);
                    throw;
                }
                return;
            }

            Debug.LogWarning("[Asset Sources] No executable was found for application '" + source.applicationId + "'. Asking the operating system to open " + path + ". If nothing happens, set the application path in Tools > Gigaduck > Asset Sources > Applications.");
            SourceLiveLink.Start(source);
            try
            {
                if (Application.platform == RuntimePlatform.WindowsEditor)
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                else
                    EditorUtility.OpenWithDefaultApp(path);
                Debug.Log("[Asset Sources] Handed " + path + " to the operating system.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Asset Sources] The operating system could not open " + path + ": " + ex.GetBaseException().Message);
                throw;
            }
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
        private static void Blender() => ChooseSource("blender", connect: true);
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
                return !string.IsNullOrEmpty(path) && Get(path).sources.Any(x => (application == null || x.applicationId == application) && Exists(x));
            }
            catch { return false; }
        }

        private static bool Exists(SourceLink source)
        {
            try { return File.Exists(Registry.Resolve(source, false)); }
            catch { return false; }
        }

        private static void OpenApplication(string application) => ChooseSource(application);

        private static void ChooseSource(string application, bool reveal = false, bool connect = false)
            => ChooseSource(SelectedAssetPath(), application, reveal, connect);

        internal static void ChooseSource(string assetPath, string application, bool reveal = false, bool connect = false)
        {
            Run(() =>
            {
                var sources = Get(assetPath).sources.Where(x => application == null || x.applicationId == application).ToArray();
                if (sources.Length == 0) throw new InvalidOperationException("Link a source first using Setup or Relink Sources.");
                if (sources.Length == 1)
                {
                    if (!Exists(sources[0])) throw new FileNotFoundException("Source file is missing; use Setup or Relink Sources to reconnect it.", sources[0].relativePath);
                    if (reveal) Reveal(sources[0]); else if (connect) OpenWithLiveLink(assetPath, sources[0].id); else Open(sources[0]);
                    return;
                }
                var menu = new GenericMenu();
                foreach (var item in sources)
                {
                    var source = item;
                    bool exists = Exists(source);
                    var content = new GUIContent((string.IsNullOrEmpty(source.label) ? source.id : source.label).Replace('/', ' ') + " (" + Path.GetFileName(source.relativePath) + ")" + (exists ? "" : " - missing"));
                    if (exists) menu.AddItem(content, false, () => Run(() => { if (reveal) Reveal(source); else if (connect) OpenWithLiveLink(assetPath, source.id); else Open(source); }));
                    else menu.AddDisabledItem(content);
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
