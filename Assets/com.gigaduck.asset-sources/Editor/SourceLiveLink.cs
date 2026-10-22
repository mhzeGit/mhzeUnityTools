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
        }
        private static readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>();
        private static double nextPoll;
        private const string SavedKey = "Gigaduck.AssetSources.LiveLink";

        internal static void Start(SourceLink source)
        {
            string path = AssetSources.Registry.Resolve(source);
            string key = Key(source);
            var registry = new SourceRegistry(AssetSources.Registry.ProjectRoot);
            if (sessions.TryGetValue(key, out var active))
            {
                // Reopening refreshes the target list so links added since the first open are included.
                active.records = Task.Run(() => FindTargets(registry, source));
                return;
            }
            // Only the small link registry is inspected, never the resource library or project assets.
            var session = new Session { source = source, path = path, stamp = Stamp(path) };
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

        public static string Status(SourceLink source) => sessions.TryGetValue(Key(source), out var session)
            ? session.status : "Opens with automatic sync on save";

        [MenuItem("Tools/Gigaduck/Asset Sources/Stop Live Links")]
        public static void StopAll()
        {
            foreach (var session in sessions.Values) Discard(session);
            sessions.Clear();
            EditorApplication.update -= Update;
            SessionState.EraseString(SavedKey);
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
                if (record.sources.Any(x => Key(x) == Key(source))) result.Add(record);
            }
            return result;
        }

        private static string Key(SourceLink source) => source.libraryId + "/" + source.relativePath;
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

        private static void Tick(Session session)
        {
            if (session.records != null)
            {
                if (!session.records.IsCompleted) return;
                var task = session.records;
                session.records = null;
                session.targets = task.GetAwaiter().GetResult();
                session.status = session.targets.Count == 0
                    ? "No Unity assets link to this source yet"
                    : "Live on save (" + session.targets.Count + " linked assets)";
            }
            if (session.targets == null) return;
            string stamp = Stamp(session.path);
            if (session.stamp != stamp) { session.stamp = stamp; session.changed = EditorApplication.timeSinceStartup; }
            if (session.export != null)
            {
                if (!session.export.IsCompleted) return;
                var task = session.export;
                session.export = null;
                var results = task.GetAwaiter().GetResult();
                int updated = 0;
                var errors = new List<string>();
                foreach (var result in results)
                {
                    using (result)
                    {
                        try
                        {
                            if (result.error != null) { errors.Add(result.error); continue; }
                            // A save arriving during export supersedes the older result.
                            if (session.changed > 0) continue;
                            string target = AssetDatabase.GUIDToAssetPath(result.guid);
                            if (string.IsNullOrEmpty(target) || target != result.assetPath ||
                                !AssetSources.Registry.GetRecord(result.guid).sources.Any(x => Key(x) == Key(session.source))) continue;
                            string fullTarget = Path.Combine(AssetSources.Registry.ProjectRoot, target);
                            if (SourceExport.Hash(fullTarget) != result.previousHash)
                            { errors.Add(target + " changed during export; save the source again to retry."); continue; }
                            if (!SourceExport.Publish(result.output, fullTarget)) continue;
                            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate);
                            updated++;
                        }
                        catch (Exception ex) { errors.Add(result.assetPath + ": " + ex.GetBaseException().Message); }
                    }
                }
                session.status = errors.Count == 0
                    ? "Live on save; updated " + updated + " assets" + (session.skipped == 0 ? "" : "; " + session.skipped + " linked assets are not exported from this master")
                    : string.Join("\n", errors.Distinct());
                if (errors.Count > 0) Debug.LogWarning("Asset Sources live link: " + session.status);
            }
            if (session.export != null || session.changed <= 0 || EditorApplication.timeSinceStartup - session.changed < .35 || stamp == "missing") return;
            session.changed = 0;
            var jobs = new List<SourceExport.Job>();
            session.skipped = 0;
            foreach (var record in session.targets)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(record.unityGuid);
                if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                if (!AssetSources.Registry.GetRecord(record.unityGuid).sources.Any(x => Key(x) == Key(session.source))) continue;
                var job = SourceExport.Prepare(session.path, assetPath, record.unityGuid);
                if (!SourceExport.CanExport(job.source, job.target)) { session.skipped++; continue; }
                jobs.Add(job);
            }
            if (jobs.Count == 0)
            {
                session.status = session.skipped == 0 ? "No Unity assets link to this source yet"
                    : session.skipped + " linked assets are opened for navigation but are not exported from this master";
                return;
            }
            session.status = "Exporting in background...";
            session.export = Task.Run(() => SourceExport.ExportMany(jobs));
        }
    }
}
