using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Gigaduck.AssetSources
{
    /// <summary>Reads and explicitly edits portable source records and local machine settings without importing art.</summary>
    public sealed class SourceRegistry
    {
        public const int SchemaVersion = 1;
        public string ProjectRoot { get; }
        public string SettingsDirectory => Path.Combine(ProjectRoot, "ProjectSettings/AssetSources");
        public string LibrariesPath => Path.Combine(SettingsDirectory, "Libraries.json");
        public string LocalSettingsPath => Path.Combine(ProjectRoot, "UserSettings/AssetSources.local.json");
        private readonly Dictionary<string, Tuple<long, string>> cache = new Dictionary<string, Tuple<long, string>>();
        private static readonly StringComparison PathComparison = Application.platform == RuntimePlatform.WindowsEditor
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        public SourceRegistry(string projectRoot) { ProjectRoot = Path.GetFullPath(projectRoot); }

        public SourceLibraries GetLibraries()
        {
            var result = Read<SourceLibraries>(LibrariesPath);
            CheckVersion(result.schemaVersion);
            if (result.libraries == null) throw new InvalidDataException("Missing libraries array: " + LibrariesPath);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var library in result.libraries)
            {
                if (library == null) throw new InvalidDataException("Null library entry.");
                ValidateId(library.id);
                if (!ids.Add(library.id)) throw new InvalidDataException("Duplicate library ID: " + library.id);
            }
            return result;
        }

        public LocalSourceSettings GetLocalSettings()
        {
            var result = Read<LocalSourceSettings>(LocalSettingsPath);
            CheckVersion(result.schemaVersion);
            if (result.libraries == null || result.applications == null) throw new InvalidDataException("Invalid local settings arrays.");
            return result;
        }

        public string RecordPath(string unityGuid)
        {
            if (!Regex.IsMatch(unityGuid ?? "", "^[a-fA-F0-9]{32}$")) throw new ArgumentException("Expected a 32-character Unity asset GUID.");
            return Path.Combine(SettingsDirectory, "Links", unityGuid.ToLowerInvariant() + ".json");
        }

        public AssetSourceRecord GetRecord(string unityGuid)
        {
            string path = RecordPath(unityGuid);
            if (!File.Exists(path)) return new AssetSourceRecord { unityGuid = unityGuid.ToLowerInvariant() };
            var record = Read<AssetSourceRecord>(path);
            CheckVersion(record.schemaVersion);
            if (!string.Equals(record.unityGuid, unityGuid, StringComparison.OrdinalIgnoreCase) || record.sources == null)
                throw new InvalidDataException("Record GUID or sources do not match: " + path);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in record.sources)
            {
                ValidateSource(source);
                if (!ids.Add(source.id)) throw new InvalidDataException("Duplicate source ID: " + source.id);
            }
            return record;
        }

        public void DefineLibrary(string id, string displayName = null, string environmentVariable = null)
        {
            ValidateId(id);
            var data = GetLibraries();
            var library = data.libraries.FirstOrDefault(x => x.id == id);
            if (library == null) { library = new SourceLibrary { id = id }; data.libraries.Add(library); }
            library.displayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            library.environmentVariable = environmentVariable ?? "";
            data.libraries = data.libraries.OrderBy(x => x.id, StringComparer.Ordinal).ToList();
            Write(LibrariesPath, data);
        }

        public void SetLibraryRoot(string libraryId, string root)
        {
            RequireLibrary(libraryId);
            root = Path.GetFullPath(root);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            if (IsInside(ProjectRoot, root) || SamePath(ProjectRoot, root))
                throw new ArgumentException("Choose an external source folder outside the Unity project.");
            var local = GetLocalSettings();
            SetLocalPath(local.libraries, libraryId, root);
            Write(LocalSettingsPath, local);
        }

        public void SetApplication(string applicationId, string executable)
        {
            ValidateId(applicationId);
            var local = GetLocalSettings();
            if (string.IsNullOrWhiteSpace(executable)) local.applications.RemoveAll(x => x.id == applicationId);
            else
            {
                executable = Path.GetFullPath(executable);
                if (!File.Exists(executable)) throw new FileNotFoundException("Application executable does not exist.", executable);
                SetLocalPath(local.applications, applicationId, executable);
            }
            Write(LocalSettingsPath, local);
        }

        public string LibraryRoot(string libraryId)
        {
            var library = RequireLibrary(libraryId);
            string root = string.IsNullOrEmpty(library.environmentVariable) ? null : Environment.GetEnvironmentVariable(library.environmentVariable);
            if (string.IsNullOrWhiteSpace(root)) root = GetLocalSettings().libraries.FirstOrDefault(x => x.id == libraryId)?.path;
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("Locate library '" + libraryId + "' on this PC in Asset Sources > Libraries.");
            root = Path.GetFullPath(root);
            if (IsInside(ProjectRoot, root) || SamePath(ProjectRoot, root)) throw new InvalidOperationException("Source libraries must be outside the Unity project.");
            return root;
        }

        public string RelativePath(string libraryId, string absolutePath)
        {
            string root = LibraryRoot(libraryId);
            string full = Path.GetFullPath(absolutePath);
            if (!IsInside(root, full)) throw new ArgumentException("The source must be inside library '" + libraryId + "'.");
            return NormalizeRelative(full.Substring(WithSeparator(root).Length));
        }

        public string Resolve(SourceLink source, bool requireExists = true)
        {
            ValidateSource(source);
            string root = LibraryRoot(source.libraryId);
            string full = Path.GetFullPath(Path.Combine(root, NormalizeRelative(source.relativePath)));
            if (!IsInside(root, full)) throw new ArgumentException("Source path escapes the library.");
            if (requireExists && !File.Exists(full)) throw new FileNotFoundException("Source missing; use Setup / Relink Sources to reconnect it.", full);
            return full;
        }

        public void Link(string unityGuid, SourceLink source, bool requireExists = true)
        {
            ValidateSource(source);
            source.relativePath = NormalizeRelative(source.relativePath);
            Resolve(source, requireExists);
            var record = GetRecord(unityGuid);
            record.sources.RemoveAll(x => x.id == source.id);
            record.sources.Add(source);
            record.sources = record.sources.OrderBy(x => x.id, StringComparer.Ordinal).ToList();
            Write(RecordPath(unityGuid), record);
        }

        public void Unlink(string unityGuid, string sourceId)
        {
            var record = GetRecord(unityGuid);
            record.sources.RemoveAll(x => x.id == sourceId);
            Write(RecordPath(unityGuid), record);
        }

        public List<string> ValidateAll(Func<string, bool> assetExists = null)
        {
            var errors = new List<string>();
            try { GetLibraries(); GetLocalSettings(); } catch (Exception ex) { errors.Add(ex.Message); return errors; }
            string links = Path.Combine(SettingsDirectory, "Links");
            if (!Directory.Exists(links)) return errors;
            foreach (string file in Directory.GetFiles(links, "*.json").OrderBy(x => x, StringComparer.Ordinal))
            {
                try
                {
                    var record = GetRecord(Path.GetFileNameWithoutExtension(file));
                    if (assetExists != null && !assetExists(record.unityGuid)) errors.Add(record.unityGuid + ": Unity asset missing.");
                    foreach (var source in record.sources)
                        try { Resolve(source); } catch (Exception ex) { errors.Add(record.unityGuid + "/" + source.id + ": " + ex.Message); }
                }
                catch (Exception ex) { errors.Add(Path.GetFileName(file) + ": " + ex.Message); }
            }
            return errors;
        }

        public void Reload() { cache.Clear(); }

        public static string NormalizeRelative(string path)
        {
            path = (path ?? "").Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith("/") || path.Contains(":") || path.IndexOf('\0') >= 0)
                throw new ArgumentException("Expected a library-relative file path.");
            var parts = path.Split('/');
            if (parts.Any(x => x == ".." || x == "." || string.IsNullOrEmpty(x))) throw new ArgumentException("Source paths must not contain traversal or empty segments.");
            return path;
        }

        public static void ValidateId(string id)
        {
            if (!Regex.IsMatch(id ?? "", "^[a-zA-Z0-9][a-zA-Z0-9._-]*$")) throw new ArgumentException("IDs use letters, numbers, dots, underscores and hyphens.");
        }

        private SourceLibrary RequireLibrary(string id)
        {
            ValidateId(id);
            return GetLibraries().libraries.FirstOrDefault(x => x.id == id) ?? throw new InvalidOperationException("Define library '" + id + "' first.");
        }

        private static void ValidateSource(SourceLink source)
        {
            if (source == null) throw new InvalidDataException("Null source entry.");
            ValidateId(source.id); ValidateId(source.libraryId); ValidateId(source.applicationId);
            NormalizeRelative(source.relativePath);
        }

        private static void CheckVersion(int version)
        {
            if (version != SchemaVersion) throw new InvalidDataException("Unsupported Asset Sources schema version: " + version);
        }

        private T Read<T>(string path) where T : class, new()
        {
            if (!File.Exists(path)) return new T();
            long stamp = File.GetLastWriteTimeUtc(path).Ticks;
            if (!cache.TryGetValue(path, out var entry) || entry.Item1 != stamp)
            { entry = Tuple.Create(stamp, File.ReadAllText(path)); cache[path] = entry; }
            try { return JsonUtility.FromJson<T>(entry.Item2) ?? throw new InvalidDataException("Empty JSON."); }
            catch (Exception ex) { throw new InvalidDataException("Cannot read " + path + ": " + ex.Message, ex); }
        }

        private void Write<T>(string path, T data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true) + "\n", new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); cache.Remove(path); }
        }

        private static void SetLocalPath(List<LocalSourcePath> entries, string id, string path)
        {
            entries.RemoveAll(x => x.id == id);
            entries.Add(new LocalSourcePath { id = id, path = path.Replace('\\', '/') });
            entries.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        }

        private static string WithSeparator(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        private static bool SamePath(string a, string b) => string.Equals(WithSeparator(a), WithSeparator(b), PathComparison);
        private static bool IsInside(string root, string path) => path.StartsWith(WithSeparator(root), PathComparison);
    }
}
