using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Gigaduck.AssetSources
{
    /// <summary>Exchanges staged exports with the explicitly launched Blender editor through a private external session folder.</summary>
    internal sealed class BlenderLiveConnection
    {
        [Serializable] private sealed class Request { public string source; public List<SourceExport.Job> jobs; }
        [Serializable] private sealed class Response { public List<Frame> results; }
        [Serializable] private sealed class Frame { public string guid, assetPath, output, previousHash, error; }
        private readonly string source, directory;
        private List<SourceExport.Job> jobs = new List<SourceExport.Job>();
        private string requestJson;
        private double nextLease;
        internal string Config => Path.Combine(directory, "request.json");
        internal bool Connected => File.Exists(Path.Combine(directory, "blender.alive")) &&
            (DateTime.UtcNow - File.GetLastWriteTimeUtc(Path.Combine(directory, "blender.alive"))).TotalSeconds < 15;

        internal BlenderLiveConnection(string source, string projectRoot)
        {
            this.source = source;
            string token = SessionState.GetString("Gigaduck.AssetSources.BlenderSession", "");
            if (token.Length == 0) { token = Guid.NewGuid().ToString("N"); SessionState.SetString("Gigaduck.AssetSources.BlenderSession", token); }
            string project;
            using (var hash = SHA256.Create()) project = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(projectRoot + "\n" + source))).Replace("-", "").Substring(0, 16);
            directory = SourceExport.WorkDirectory(source, project + "-" + token);
            Directory.CreateDirectory(directory);
            Pulse();
            if (!File.Exists(Config)) Configure(new List<SourceExport.Job>());
        }

        internal string Arguments => "--python " + SourceExport.Quote(Path.Combine(
            UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AssetSources).Assembly).resolvedPath,
            "Exporters~", "blender_live.py")) + " -- " + SourceExport.Quote(Config);

        internal void Configure(List<SourceExport.Job> targets)
        {
            jobs = targets;
            string json = JsonUtility.ToJson(new Request { source = source, jobs = jobs });
            if (json == requestJson && File.Exists(Config)) return;
            string temp = Config + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(Config)) File.Replace(temp, Config, null); else File.Move(temp, Config);
            requestJson = json;
        }

        internal void Pulse()
        {
            if (EditorApplication.timeSinceStartup < nextLease) return;
            nextLease = EditorApplication.timeSinceStartup + 1;
            if (!Directory.Exists(directory)) return;
            File.WriteAllText(Path.Combine(directory, "unity.alive"), "");
        }

        internal List<SourceExport.Result> Receive()
        {
            string ready = Path.Combine(directory, "ready.json");
            if (!File.Exists(ready)) return null;
            var response = JsonUtility.FromJson<Response>(File.ReadAllText(ready));
            if (response?.results == null) throw new InvalidDataException("Blender returned an invalid live export response.");
            var results = new List<SourceExport.Result>();
            foreach (var frame in response.results)
            {
                var job = jobs.FirstOrDefault(x => x.guid == frame.guid && x.assetPath == frame.assetPath);
                if (job == null) continue;
                string output = Path.GetFullPath(frame.output);
                if (!output.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(Path.GetDirectoryName(output)).StartsWith("frame-", StringComparison.Ordinal) ||
                    Path.GetExtension(output) != Path.GetExtension(job.target))
                    throw new InvalidDataException("Blender returned an output outside its live export folder.");
                results.Add(new SourceExport.Result { guid = frame.guid, assetPath = frame.assetPath, output = output,
                    previousHash = frame.previousHash, error = frame.error, directory = Path.GetDirectoryName(output) });
            }
            File.Delete(ready);
            return results;
        }

        internal void Stop()
        {
            // Blender notices removal on its next timer tick and removes its own session files.
            if (File.Exists(Config)) File.Delete(Config);
        }
    }
}
