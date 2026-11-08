using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gigaduck.AgenticTools.AgentPlay
{
    /// <summary>
    /// Builds the player (non-Development, with the AGENT_PLAY define) that agents drive with <c>tools/agentplay/agentplay.py</c> (see
    /// <c>Docs/AGENT_PLAY.md</c>). Runs in batch mode, so no editor window has to be open
    /// (<c>Unity.exe -batchmode -projectPath . -executeMethod Gigaduck.AgenticTools.AgentPlay.AgentPlayBuild.BuildFromCommandLine
    /// -agentOut &lt;dir&gt;</c>), or from an already open editor through <see cref="Build"/>. It only reads the scenes in
    /// Build Settings and never changes a scene, prefab or project setting. Editor only.
    /// </summary>
    public static class AgentPlayBuild
    {
        public const string ExeName = "AgentPlay.exe";

        /// <summary>Batch-mode entry point. Writes <c>build-result.json</c> next to the output folder and exits with 0 or 1.</summary>
        public static void BuildFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-agentOut");
            string outDir = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.GetFullPath("Builds/AgentPlay/_stage");

            // One graphics API only: Windows otherwise compiles every shader variant for both DX12 and DX11, which is
            // hours of work for a player that only agents run. Put back afterwards so no project setting changes.
            const BuildTarget Target = BuildTarget.StandaloneWindows64;
            bool wasAutomatic = PlayerSettings.GetUseDefaultGraphicsAPIs(Target);
            GraphicsDeviceType[] wasApis = PlayerSettings.GetGraphicsAPIs(Target);
            string result;
            try
            {
                PlayerSettings.SetUseDefaultGraphicsAPIs(Target, false);
                PlayerSettings.SetGraphicsAPIs(Target, new[] { GraphicsDeviceType.Direct3D12 });
                result = Build(outDir);
            }
            finally
            {
                PlayerSettings.SetGraphicsAPIs(Target, wasApis);
                PlayerSettings.SetUseDefaultGraphicsAPIs(Target, wasAutomatic);
            }

            EditorApplication.Exit(result.StartsWith("OK") ? 0 : 1);
        }

        /// <summary>Builds into <paramref name="outDir"/> and returns "OK ..." or "FAILED ...". Callable through MCP execute_code.</summary>
        public static string Build(string outDir)
        {
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) return Report(outDir, "FAILED no enabled scenes in Build Settings");
            Directory.CreateDirectory(outDir);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                // Not a Development build: that opens a player-connection port for the profiler, so Windows Firewall
                // asks about every new build, and in Connect mode it attaches to the open editor. AGENT_PLAY compiles
                // the harness in instead.
                options = BuildOptions.None,
                extraScriptingDefines = new[] { "AGENT_PLAY" },
            };

            DateTime start = DateTime.UtcNow;
            BuildReport report = BuildPipeline.BuildPlayer(options);
            double seconds = (DateTime.UtcNow - start).TotalSeconds;
            BuildSummary s = report.summary;
            string text = s.result == BuildResult.Succeeded
                ? "OK " + seconds.ToString("F0") + "s " + (s.totalSize / (1024 * 1024)) + "MB warnings=" + s.totalWarnings
                : "FAILED " + s.result + " errors=" + s.totalErrors + " " + string.Join(" | ",
                    report.steps.SelectMany(step => step.messages).Where(m => m.type == LogType.Error || m.type == LogType.Exception).Select(m => m.content).Take(5));
            return Report(outDir, text);
        }

        private static string Report(string outDir, string text)
        {
            try { File.WriteAllText(Path.Combine(outDir, "build-result.txt"), text); }
            catch (Exception) { /* the log still has it */ }
            Debug.Log("[AgentPlayBuild] " + text);
            return text;
        }

        [MenuItem("Tools/Agentic Tools/Build Agent Play Player", priority = 20)]
        public static void MenuBuild() => Debug.Log(Build(Path.GetFullPath("Builds/AgentPlay/_stage")));
    }
}
