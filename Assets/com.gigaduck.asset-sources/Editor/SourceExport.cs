using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Gigaduck.AssetSources.Editor.Tests")]

namespace Gigaduck.AssetSources
{
    /// <summary>Runs native exporters off the editor thread and atomically publishes verified outputs without touching metadata.</summary>
    internal static class SourceExport
    {
        private static readonly RuntimePlatform Platform = Application.platform;
        [Serializable] internal sealed class Job
        {
            public string source, assetPath, target, guid, executable, scriptDirectory, output;
            public string[] meshes;
            public string channel;
        }
        internal sealed class Result : IDisposable
        {
            public string guid, assetPath, output, previousHash, error, directory;
            public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
        [Serializable] private sealed class JobList { public List<Job> jobs; }
        [Serializable] private sealed class GraphInfo { public GraphMaterial[] materials; public string normalType; }
        [Serializable] private sealed class GraphMaterial { public string name; }

        internal static Job Prepare(string source, string assetPath, string guid)
        {
            string application = AssetSources.ApplicationForPath(source);
            return new Job
            {
                source = source, assetPath = assetPath, guid = guid,
                target = Path.Combine(AssetSources.Registry.ProjectRoot, assetPath),
                executable = Executable(source, application),
                scriptDirectory = Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AssetSources).Assembly).resolvedPath, "Exporters~"),
                meshes = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>().Select(x => x.name).Distinct().ToArray(),
                channel = Channel(Path.GetFileNameWithoutExtension(assetPath))
            };
        }

        internal static List<Result> ExportMany(List<Job> jobs)
        {
            if (jobs.Count > 1 && Path.GetExtension(jobs[0].source).Equals(".gmat", StringComparison.OrdinalIgnoreCase))
            {
                var copies = jobs.Where(x => Path.GetExtension(x.target).Equals(".gmat", StringComparison.OrdinalIgnoreCase)).Select(Export).ToList();
                var native = jobs.Where(x => !Path.GetExtension(x.target).Equals(".gmat", StringComparison.OrdinalIgnoreCase)).ToList();
                copies.AddRange(native.Count > 1 ? GraphmatBatch(native) : native.Select(Export).ToList());
                return copies;
            }
            if (jobs.Count < 2 || Path.GetExtension(jobs[0].source).ToLowerInvariant() != ".blend")
                return jobs.Select(Export).ToList();
            var results = new List<Result>();
            try
            {
                foreach (var job in jobs) results.Add(Stage(job));
                RequireExecutable(jobs[0].executable, "Blender");
                string config = Path.Combine(results[0].directory, "jobs.json");
                File.WriteAllText(config, JsonUtility.ToJson(new JobList { jobs = jobs }), new UTF8Encoding(false));
                Run(jobs[0].executable, "--background", jobs[0].source, "--python-exit-code", "1", "--python", Path.Combine(jobs[0].scriptDirectory, "blender_export.py"), "--", config);
                foreach (var result in results)
                {
                    if (File.Exists(result.output + ".error")) result.error = result.assetPath + ": " + File.ReadAllText(result.output + ".error");
                    else if (!File.Exists(result.output) || new FileInfo(result.output).Length == 0) result.error = result.assetPath + ": Blender produced no output.";
                }
            }
            catch (Exception ex)
            {
                foreach (var result in results) result.error = result.assetPath + ": " + ex.GetBaseException().Message;
            }
            return results;
        }

        private static List<Result> GraphmatBatch(List<Job> jobs)
        {
            var results = new List<Result>();
            try
            {
                string cli = Beside(jobs[0].executable, "graphmat-cli");
                RequireExecutable(cli, "GraphMat full-edition CLI");
                var info = JsonUtility.FromJson<GraphInfo>(Run(cli, "info", jobs[0].source));
                var commands = new StringBuilder();
                foreach (var job in jobs)
                {
                    var result = Stage(job);
                    results.Add(result);
                    try
                    {
                        if (!Path.GetExtension(job.target).Equals(".png", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("GraphMat automatic texture export currently writes PNG targets.");
                        RequireChannel(job);
                        CheckNormal(job, info);
                        string material = SelectMaterial(info.materials.Select(x => x.name).ToArray(), Path.GetFileNameWithoutExtension(job.target));
                        commands.Append("extract ").Append(job.channel).Append(' ').Append(ShellQuote(job.output))
                            .Append(" --scope visible --material ").Append(ShellQuote(material)).AppendLine();
                    }
                    catch (Exception ex) { result.error = job.assetPath + ": " + ex.Message; }
                }
                commands.AppendLine("exit");
                // The shell only preloads a project when its path is the first argument; "interactive" ignores the rest.
                string diagnostic = RunInput(cli, commands.ToString(), jobs[0].source);
                foreach (var result in results.Where(x => x.error == null))
                    if (!File.Exists(result.output) || new FileInfo(result.output).Length == 0) result.error = result.assetPath + ": GraphMat export failed: " + diagnostic;
            }
            catch (Exception ex)
            {
                foreach (var result in results) result.error = result.assetPath + ": " + ex.GetBaseException().Message;
                if (results.Count == 0) results.Add(new Result { assetPath = jobs[0].assetPath, error = ex.GetBaseException().Message });
            }
            return results;
        }

        private static string ShellQuote(string value)
        {
            if (value.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0) throw new ArgumentException("GraphMat's CLI cannot quote this path or material name safely.");
            return "\"" + value + "\"";
        }
        private static void CheckNormal(Job job, GraphInfo info)
        {
            if (job.channel == "normal" && !string.IsNullOrEmpty(info.normalType) && !info.normalType.Equals("OpenGL", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This GraphMat project uses a different normal convention; automatic conversion needs an explicit mapping.");
        }

        private static Result Stage(Job job)
        {
            var result = new Result { guid = job.guid, assetPath = job.assetPath };
            result.directory = Path.Combine(Path.GetDirectoryName(job.source), "Work", "asset-sources-live", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(result.directory);
                result.previousHash = Hash(job.target);
                job.output = result.output = Path.Combine(result.directory, "output" + Path.GetExtension(job.target));
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        internal static Result Export(Job job)
        {
            var result = new Result { guid = job.guid, assetPath = job.assetPath };
            // Production work stays beside the external master, never in Assets or the Unity project.
            result.directory = Path.Combine(Path.GetDirectoryName(job.source), "Work", "asset-sources-live", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(result.directory);
                result.previousHash = Hash(job.target);
                job.output = result.output = Path.Combine(result.directory, "output" + Path.GetExtension(job.target));
                string sourceExt = Path.GetExtension(job.source).ToLowerInvariant();
                string targetExt = Path.GetExtension(job.target).ToLowerInvariant();
                if (sourceExt == targetExt)
                    File.Copy(job.source, job.output);
                else if (sourceExt == ".blend")
                {
                    RequireExecutable(job.executable, "Blender");
                    string config = Path.Combine(result.directory, "job.json");
                    File.WriteAllText(config, JsonUtility.ToJson(job), new UTF8Encoding(false));
                    Run(job.executable, "--background", job.source, "--python-exit-code", "1", "--python", Path.Combine(job.scriptDirectory, "blender_export.py"), "--", config);
                }
                else if (sourceExt == ".gmat" && targetExt == ".png")
                {
                    RequireChannel(job);
                    string cli = Beside(job.executable, "graphmat-cli");
                    RequireExecutable(cli, "GraphMat full-edition CLI (shipped beside GraphMat)");
                    var info = JsonUtility.FromJson<GraphInfo>(Run(cli, "info", job.source));
                    string material = SelectMaterial(info.materials.Select(x => x.name).ToArray(), Path.GetFileNameWithoutExtension(job.target));
                    CheckNormal(job, info);
                    Run(cli, "extract", job.source, "--material", material, "--channel", job.channel, "--scope", "visible", "--out", job.output);
                }
                else if ((sourceExt == ".psd" || sourceExt == ".psb") && IsImage(targetExt))
                {
                    if (Platform != RuntimePlatform.WindowsEditor) throw new NotSupportedException("Photoshop export currently requires Windows automation. Saving a PSD directly to a linked PSD asset works on every platform.");
                    string config = Path.Combine(result.directory, "job.json");
                    File.WriteAllText(config, JsonUtility.ToJson(job), new UTF8Encoding(false));
                    Run("powershell.exe", "-NoProfile", "-NonInteractive", "-File", Path.Combine(job.scriptDirectory, "photoshop_export.ps1"), "-JobPath", config);
                }
                else if (sourceExt == ".sbs" && (targetExt == ".sbsar" || targetExt == ".png"))
                    Designer(job, result.directory);
                else if (sourceExt == ".spp" && targetExt == ".png")
                    Painter(job);
                else throw new NotSupportedException("No automatic native exporter for " + sourceExt + " → " + targetExt + ". Link the editor's exported image instead; it will sync on save.");
                if (!File.Exists(job.output) || new FileInfo(job.output).Length == 0) throw new IOException("Exporter produced no output.");
            }
            catch (Exception ex) { result.error = job.assetPath + ": " + ex.GetBaseException().Message; }
            return result;
        }

        internal static string Hash(string file)
        {
            using (var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(stream));
        }

        internal static bool Publish(string output, string target)
        {
            if (Hash(output) == Hash(target)) return false;
            // Replace on the destination volume, retaining the existing .meta and importer settings.
            string staging = target + ".asset-sources-" + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.Copy(output, staging); File.Replace(staging, target, null); }
            finally { if (File.Exists(staging)) File.Delete(staging); }
            return true;
        }

        internal static string Channel(string name)
        {
            string lower = name.ToLowerInvariant().Replace("_", " ").Replace("-", " ");
            if (lower.Contains("smoothness") || lower.Contains("maskmap") || lower.Split(' ').Any(x => x == "orm" || x == "rma" || x == "arm")) return null;
            var channels = new System.Collections.Generic.List<string>();
            if (lower.Contains("basecolor") || lower.Contains("base color") || lower.Contains("albedo") || lower.Contains("diffuse")) channels.Add("basecolor");
            if (lower.Contains("normal")) channels.Add("normal");
            if (lower.Contains("roughness")) channels.Add("roughness");
            if (lower.Contains("metallic") || lower.Contains("metalness")) channels.Add("metallic");
            if (lower.Contains("occlusion") || lower.Split(' ').Contains("ao")) channels.Add("ao");
            if (lower.Contains("height") || lower.Contains("displacement")) channels.Add("height");
            if (lower.Contains("emiss")) channels.Add("emission");
            if (lower.Contains("opacity") || lower.Contains("alpha")) channels.Add("alpha");
            return channels.Count == 1 ? channels[0] : null;
        }

        internal static string SelectMaterial(string[] materials, string target)
        {
            if (materials.Length == 1) return materials[0];
            string[] matches = materials.Where(x => target.StartsWith(x + "_", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith(x + "-", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Multiple materials need an unambiguous material-name prefix on the linked texture; no asset was changed.");
            return matches[0];
        }

        private static void RequireChannel(Job job)
        { if (job.channel == null) throw new InvalidOperationException("Cannot identify a single PBR channel from this texture's name. Packed or ambiguous textures are left unchanged."); }
        private static bool IsImage(string extension) => new[] { ".png", ".tga", ".jpg", ".jpeg", ".tif", ".tiff", ".exr" }.Contains(extension);

        // A master can be linked to derived assets (for example a Unity material) that cannot be produced by an exporter.
        internal static bool CanExport(string source, string target)
        {
            string sourceExt = Path.GetExtension(source).ToLowerInvariant();
            string targetExt = Path.GetExtension(target).ToLowerInvariant();
            if (sourceExt == targetExt) return true;
            switch (sourceExt)
            {
                case ".blend": return new[] { ".fbx", ".obj", ".glb" }.Contains(targetExt) || IsImage(targetExt);
                case ".gmat": return targetExt == ".png";
                case ".spp": return targetExt == ".png";
                case ".sbs": return targetExt == ".sbsar" || targetExt == ".png";
                case ".psd":
                case ".psb": return IsImage(targetExt);
                default: return false;
            }
        }
        private static void RequireExecutable(string path, string name)
        { if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new FileNotFoundException(name + " could not be located from the file association, Applications override or PATH."); }

        internal static string Executable(string source, string application)
        {
            string configured = AssetSources.Registry.GetLocalSettings().applications.FirstOrDefault(x => x.id == application)?.path;
            if (!string.IsNullOrEmpty(configured)) return configured;
            if (Platform == RuntimePlatform.WindowsEditor)
            {
                var buffer = new StringBuilder(2048);
                uint length = (uint)buffer.Capacity;
                if (AssocQueryString(0, 2, Path.GetExtension(source), null, buffer, ref length) == 0 && File.Exists(buffer.ToString())) return buffer.ToString();
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + application + ".exe"))
                    if (key?.GetValue(null) is string path && File.Exists(path)) return path;
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    string steam = key?.GetValue("SteamPath") as string;
                    if (!string.IsNullOrEmpty(steam))
                    {
                        string libraries = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                        var roots = new System.Collections.Generic.List<string> { steam };
                        if (File.Exists(libraries)) roots.AddRange(Regex.Matches(File.ReadAllText(libraries), "\"path\"\\s+\"([^\"]+)\"").Cast<Match>().Select(x => x.Groups[1].Value.Replace("\\\\", "\\")));
                        foreach (string root in roots.Distinct())
                        {
                            string candidate = Path.Combine(root, "steamapps", "common", application, application + ".exe");
                            if (File.Exists(candidate)) return candidate;
                        }
                    }
                }
            }
            foreach (var process in Process.GetProcessesByName(application))
                using (process) { try { string path = process.MainModule.FileName; if (File.Exists(path)) return path; } catch (System.ComponentModel.Win32Exception) { } }
            if (Platform == RuntimePlatform.OSXEditor && application == "blender" && File.Exists("/Applications/Blender.app/Contents/MacOS/Blender"))
                return "/Applications/Blender.app/Contents/MacOS/Blender";
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                string candidate = Path.Combine(directory.Trim('"'), application + (Platform == RuntimePlatform.WindowsEditor ? ".exe" : ""));
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        [DllImport("Shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern uint AssocQueryString(uint flags, uint str, string association, string extra, StringBuilder output, ref uint length);

        private static string Beside(string executable, string tool)
        {
            if (string.IsNullOrEmpty(executable)) return null;
            string extension = Path.GetExtension(executable);
            foreach (string directory in new[] { Path.GetDirectoryName(executable), Path.Combine(Path.GetDirectoryName(executable), "tools"), Path.Combine(Path.GetDirectoryName(executable), "plugins", "substance") })
            {
                string path = Path.Combine(directory, tool + extension);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        internal static string Quote(string value)
        {
            // Windows CRT quoting also works with Mono's ProcessStartInfo argument parser.
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }

        private static string Run(string executable, params string[] arguments)
            => RunInput(executable, null, arguments);

        private static string RunInput(string executable, string input, params string[] arguments)
        {
            var info = new ProcessStartInfo(executable, string.Join(" ", arguments.Select(Quote)))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input != null };
            using (var process = Process.Start(info))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (input != null) { process.StandardInput.Write(input); process.StandardInput.Close(); }
                if (!process.WaitForExit(600000)) { process.Kill(); throw new TimeoutException("Exporter exceeded ten minutes."); }
                Task.WaitAll(stdout, stderr);
                if (process.ExitCode != 0) throw new IOException((stderr.Result + "\n" + stdout.Result).Trim());
                return stdout.Result + (input == null ? "" : "\n" + stderr.Result);
            }
        }

        private static void Designer(Job job, string directory)
        {
            string cooker = Beside(job.executable, "sbscooker");
            RequireExecutable(cooker, "Substance sbscooker");
            Run(cooker, "--inputs", job.source, "--output-path", directory, "--output-name", "cooked");
            string archive = Path.Combine(directory, "cooked.sbsar");
            if (Path.GetExtension(job.target).Equals(".sbsar", StringComparison.OrdinalIgnoreCase)) { File.Move(archive, job.output); return; }
            RequireChannel(job);
            string renderer = Beside(job.executable, "sbsrender");
            RequireExecutable(renderer, "Substance sbsrender");
            Run(renderer, "render", archive, "--output-path", directory, "--output-name", "{inputGraphUrl}_{outputNodeName}", "--output-format", "png");
            string[] matches = Directory.GetFiles(directory, "*.png").Where(x => Channel(Path.GetFileNameWithoutExtension(x)) == job.channel).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Designer output is ambiguous or missing for " + job.channel + "; no Unity asset was changed.");
            File.Move(matches[0], job.output);
        }

        private static void Painter(Job job)
        {
            RequireChannel(job);
            string script = File.ReadAllText(Path.Combine(job.scriptDirectory, "painter_export.py"));
            script = "import json, base64\nJOB = json.loads(base64.b64decode('" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonUtility.ToJson(job))) + "'))\n" + script;
            // No listener is installed by this package. Use Painter's documented loopback-only API.
            string body = "{\"python\":\"" + Convert.ToBase64String(Encoding.UTF8.GetBytes(script)) + "\"}";
            var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("http://127.0.0.1:60041/run.json");
            request.Proxy = null;
            request.Method = "POST";
            request.Timeout = 120000;
            request.ContentType = "application/json";
            byte[] data = Encoding.UTF8.GetBytes(body);
            request.ContentLength = data.Length;
            using (var stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);
            using (var response = request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream()))
            {
                string text = reader.ReadToEnd();
                if (!File.Exists(job.output)) throw new IOException("Painter did not export: " + text);
            }
        }
    }
}
