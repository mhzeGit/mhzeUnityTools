using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Gigaduck.AssetSources
{
    /// <summary>Automatically exports explicitly opened sources after saves and publishes only their confirmed Unity targets.</summary>
    public static class SourceLiveLink
    {
        [Serializable] private sealed class SavedLinks { public List<SourceLink> sources = new List<SourceLink>(); }
        private sealed class Session
        {
            public SourceLink source;
            public string path, status = "Finding linked outputs...", stamp;
            public double changed;
            public int skipped;
            public Task<List<AssetSourceRecord>> records;
            public List<AssetSourceRecord> targets;
            public Task<List<SourceExport.Result>> export;
            public BlenderLiveConnection blender;
            public bool connected;
        }
        private static readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>();
        private static double nextPoll;
        private const string SavedKey = "Gigaduck.AssetSources.LiveLink";

        /// <summary>A source file changed on disk. Handlers run on the editor thread; a throwing handler does not stop syncing.</summary>
        public static event Action<SourceLink, string> SourceSaved;

        /// <summary>Lets a project pipeline describe how it syncs a source. The first non-empty answer wins.</summary>
        public static event Func<SourceLink, string> StatusProvider;

        internal static void Start(SourceLink source)
        {
            if (source.noSync)
            {
                Debug.Log("[Asset Sources] Live sync is off for '" + source.id + "'; the source is opened for editing only.");
                return;
            }
            string path = AssetSources.Registry.Resolve(source);
            string key = Key(source);
            var registry = new SourceRegistry(AssetSources.Registry.ProjectRoot);
            if (sessions.TryGetValue(key, out var active))
            {
                if (active.blender != null && !File.Exists(active.blender.Config))
                    active.blender = new BlenderLiveConnection(path, registry.ProjectRoot);
                // Reopening refreshes the target list so links added since the first open are included.
                active.records = Task.Run(() => FindTargets(registry, source));
                return;
            }
            // Only the small link registry is inspected, never the resource library or project assets.
            var session = new Session { source = source, path = path, stamp = Stamp(path) };
            if (Path.GetExtension(path).Equals(".blend", StringComparison.OrdinalIgnoreCase))
                session.blender = new BlenderLiveConnection(path, registry.ProjectRoot);
            session.records = Task.Run(() => FindTargets(registry, source));
            sessions.Add(key, session);
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            EditorApplication.quitting -= StopAll;
            EditorApplication.quitting += StopAll;
            Save();
        }

        public static string Status(SourceLink source)
        {
            if (source.noSync) return "Linked for navigation only (live sync off)";
            if (StatusProvider != null)
                foreach (var handler in StatusProvider.GetInvocationList())
                {
                    try
                    {
                        string text = ((Func<SourceLink, string>)handler)(source);
                        if (!string.IsNullOrEmpty(text)) return text;
                    }
                    catch (Exception ex) { Debug.LogWarning("Asset Sources status handler: " + ex.GetBaseException().Message); }
                }
            return sessions.TryGetValue(Key(source), out var session) ? session.status : "Opens with automatic sync on save";
        }

        [MenuItem("Tools/Gigaduck/Asset Sources/Stop Live Links")]
        public static void StopAll()
        {
            foreach (var session in sessions.Values) { session.blender?.Stop(); Discard(session); }
            sessions.Clear();
            EditorApplication.update -= Update;
            SessionState.EraseString(SavedKey);
            SessionState.EraseString("Gigaduck.AssetSources.BlenderSession");
        }

        private static void BeforeReload()
        {
            Save();
            foreach (var session in sessions.Values) Discard(session);
            EditorApplication.update -= Update;
        }

        // Restore only sessions the user explicitly opened in this editor session.
        [DidReloadScripts] private static void Resume()
        {
            string json = SessionState.GetString(SavedKey, "");
            if (string.IsNullOrEmpty(json)) return;
            foreach (var source in JsonUtility.FromJson<SavedLinks>(json).sources)
                try { Start(source); }
                catch (Exception ex) { Debug.LogWarning("Asset Sources live link: " + ex.Message); }
        }

        private static void Save() => SessionState.SetString(SavedKey,
            JsonUtility.ToJson(new SavedLinks { sources = sessions.Values.Select(x => x.source).ToList() }));

        private static void Discard(Session session)
        {
            if (session.export != null)
                session.export.ContinueWith(task => { if (task.Status == TaskStatus.RanToCompletion) foreach (var result in task.Result) result.Dispose(); });
        }

        internal static List<AssetSourceRecord> FindTargets(SourceRegistry registry, SourceLink source)
        {
            string folder = Path.Combine(registry.SettingsDirectory, "Links");
            var result = new List<AssetSourceRecord>();
            if (!Directory.Exists(folder)) return result;
            foreach (string file in Directory.EnumerateFiles(folder, "*.json"))
            {
                var record = registry.GetRecord(Path.GetFileNameWithoutExtension(file));
                if (record.sources.Any(x => Matches(x, source))) result.Add(record);
            }
            return result;
        }

        private static string Key(SourceLink source) => source.libraryId + "/" + source.relativePath;
        internal static bool Matches(SourceLink candidate, SourceLink source) => !candidate.noSync && Key(candidate) == Key(source);
        internal static string BlenderArguments(SourceLink source) => !source.noSync && sessions.TryGetValue(Key(source), out var session)
            ? session.blender?.Arguments ?? "" : "";
        internal static bool BlenderConnected(SourceLink source) => !source.noSync && sessions.TryGetValue(Key(source), out var session)
            && session.blender != null && session.blender.Connected;
        internal static void Refresh()
        {
            foreach (var session in sessions.Values)
            {
                var registry = new SourceRegistry(AssetSources.Registry.ProjectRoot);
                session.records = Task.Run(() => FindTargets(registry, session.source));
            }
        }
        private static string Stamp(string path)
        {
            var file = new FileInfo(path);
            return file.Exists ? file.LastWriteTimeUtc.Ticks + ":" + file.Length : "missing";
        }

        private static void Update()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + .2;
            foreach (var session in sessions.Values)
            {
                try { Tick(session); }
                catch (Exception ex) { session.status = "Sync failed: " + ex.GetBaseException().Message; Debug.LogWarning("Asset Sources: " + session.status); }
            }
        }

        private static void RefreshTargets(Session session)
        {
            int exportable = 0, navigation = 0;
            foreach (var record in session.targets)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(record.unityGuid);
                if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                if (!AssetSources.Registry.GetRecord(record.unityGuid).sources.Any(x => Matches(x, session.source))) continue;
                if (SourceExport.CanExport(session.path, Path.Combine(AssetSources.Registry.ProjectRoot, assetPath))) exportable++;
                else navigation++;
            }
            session.skipped = navigation;
            session.status = exportable > 0
                ? "Live on save: " + exportable + " linked assets will update" + (navigation == 0 ? "" : " (" + navigation + " navigation only)")
                : navigation > 0
                    ? "Linked for navigation only: this master produces no replacement Unity asset"
                    : "No Unity assets link to this source yet";
            if (session.blender != null) session.blender.Configure(Jobs(session).Where(IsLiveMesh).ToList());
        }

        private static bool IsLiveMesh(SourceExport.Job job) => new[] { ".fbx", ".obj", ".glb" }.Contains(Path.GetExtension(job.target).ToLowerInvariant());

        private static List<SourceExport.Job> Jobs(Session session)
        {
            var jobs = new List<SourceExport.Job>();
            foreach (var record in session.targets)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(record.unityGuid);
                if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                    !AssetSources.Registry.GetRecord(record.unityGuid).sources.Any(x => Matches(x, session.source))) continue;
                if (SourceExport.CanExport(session.path, assetPath)) jobs.Add(SourceExport.Prepare(session.path, assetPath, record.unityGuid));
            }
            return jobs;
        }

        private static void Publish(Session session, List<SourceExport.Result> results, bool live)
        {
            int updated = 0;
            var errors = new List<string>();
            foreach (var result in results)
                using (result)
                {
                    try
                    {
                        if (result.error != null) { errors.Add(result.assetPath + ": " + result.error); continue; }
                        if (!live && session.changed > 0) continue;
                        string target = AssetDatabase.GUIDToAssetPath(result.guid);
                        if (string.IsNullOrEmpty(target) || target != result.assetPath ||
                            !AssetSources.Registry.GetRecord(result.guid).sources.Any(x => Matches(x, session.source))) continue;
                        string fullTarget = Path.Combine(AssetSources.Registry.ProjectRoot, target);
                        if (SourceExport.Hash(fullTarget) != result.previousHash)
                        { errors.Add(target + " changed during export; edit the source again to retry."); continue; }
                        if (!SourceExport.Publish(result.output, fullTarget)) continue;
                        AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate);
                        updated++;
                    }
                    catch (Exception ex) { errors.Add(result.assetPath + ": " + ex.GetBaseException().Message); }
                }
            session.status = errors.Count == 0
                ? (live ? "Blender connected (no save needed)" : "Live on save") + "; updated " + updated + " assets"
                : string.Join("\n", errors.Distinct());
            if (errors.Count > 0) Debug.LogWarning("Asset Sources live link: " + session.status);
        }

        private static void Tick(Session session)
        {
            session.blender?.Pulse();
            if (session.records != null)
            {
                if (!session.records.IsCompleted) return;
                var task = session.records;
                session.records = null;
                session.targets = task.GetAwaiter().GetResult();
                RefreshTargets(session);
            }
            if (session.targets == null) return;
            if (session.blender != null)
            {
                var frames = session.blender.Receive();
                if (frames != null) Publish(session, frames, true);
                else if (session.blender.Connected && !session.connected)
                    session.status = "Blender connected (no save needed); waiting for edits";
                else if (!session.blender.Connected && session.connected)
                    session.status = "Blender disconnected; reopen the source to reconnect. Saved changes still sync.";
                session.connected = session.blender.Connected;
            }
            string stamp = Stamp(session.path);
            if (session.stamp != stamp)
            {
                session.stamp = stamp;
                session.changed = EditorApplication.timeSinceStartup;
                if (stamp != "missing" && SourceSaved != null)
                    try { SourceSaved(session.source, session.path); }
                    catch (Exception ex) { Debug.LogWarning("Asset Sources save handler: " + ex.GetBaseException().Message); }
            }
            if (session.export != null)
            {
                if (!session.export.IsCompleted) return;
                var task = session.export;
                session.export = null;
                var results = task.GetAwaiter().GetResult();
                if (session.connected)
                {
                    foreach (var result in results.Where(x => new[] { ".fbx", ".obj", ".glb" }.Contains(Path.GetExtension(x.assetPath).ToLowerInvariant()))) result.Dispose();
                    results.RemoveAll(x => new[] { ".fbx", ".obj", ".glb" }.Contains(Path.GetExtension(x.assetPath).ToLowerInvariant()));
                }
                if (results.Count > 0) Publish(session, results, false);
            }
            if (session.export != null || session.changed <= 0 || EditorApplication.timeSinceStartup - session.changed < .35 || stamp == "missing") return;
            session.changed = 0;
            var jobs = Jobs(session);
            if (session.blender != null && session.blender.Connected) jobs.RemoveAll(IsLiveMesh);
            if (jobs.Count == 0)
            {
                if (session.connected) return;
                session.status = session.skipped == 0 ? "No Unity assets link to this source yet"
                    : session.skipped + " linked assets are opened for navigation but are not exported from this master";
                return;
            }
            session.status = "Exporting in background...";
            session.export = Task.Run(() => SourceExport.ExportMany(jobs));
        }
    }
}
