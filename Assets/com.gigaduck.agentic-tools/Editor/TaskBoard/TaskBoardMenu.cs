using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Gigaduck.AgenticTools
{
    /// <summary>
    /// Tools ▸ Agent Task Board ▸ Open Board: opens the Agent Task Board (the installed <c>tools/taskboard</c> copy) in the
    /// browser. If the board server is not running it is started hidden in the background first (no console window to keep
    /// open), so one click is enough. The board is shared by every project, so a server started from another project, a
    /// terminal or <c>task-board.bat</c> is reused. <b>Stop Board Server</b> stops a server this menu started. Needs the
    /// "Agent Task Board" module installed (Tools ▸ Agentic Tools) and Python 3.9 or newer; nothing else.
    /// </summary>
    public static class TaskBoardMenu
    {
        private const string MenuRoot = "Tools/Agent Task Board/";
        private const string PidPref = "Gigaduck.AgenticTools.TaskBoard.Pid";
        private const int Port = 8765;
        private const int StartTimeoutMs = 8000;

        private static string ScriptPath => Path.Combine(AgenticToolsPackage.ProjectRoot, "tools", "taskboard", "taskboard.py");

        [MenuItem(MenuRoot + "Open Board", priority = 0)]
        public static void OpenBoard()
        {
            if (!File.Exists(ScriptPath))
            {
                if (EditorUtility.DisplayDialog("Agent Task Board not installed",
                        "This project has no tools/taskboard folder. Enable the \"Agent Task Board\" module in Tools > Agentic Tools and press Install / Update.",
                        "Open Agentic Tools", "Cancel"))
                    AgenticToolsWindow.Open();
                return;
            }

            if (!BoardIsUp() && !StartServer()) return;
            Application.OpenURL("http://127.0.0.1:" + Port + "/");
        }

        [MenuItem(MenuRoot + "Stop Board Server", priority = 1)]
        public static void StopServer()
        {
            if (!TryGetOwnProcess(out Process process))
            {
                EditorUtility.DisplayDialog("Agent Task Board", "No board server started from this menu is running. " +
                                                                "A board opened from a terminal or task-board.bat is stopped there (Ctrl+C).", "OK");
                return;
            }

            try { KillTree(process); process.WaitForExit(2000); }
            catch (Exception e) { Debug.LogWarning("[Task Board] Could not stop the board server: " + e.Message); }
            finally { process.Dispose(); EditorPrefs.DeleteKey(PidPref); }
            Debug.Log("[Task Board] Board server stopped.");
        }

        [MenuItem(MenuRoot + "Stop Board Server", validate = true)]
        private static bool ValidateStop()
        {
            if (!TryGetOwnProcess(out Process process)) return false;
            process.Dispose();
            return true;
        }

        [MenuItem(MenuRoot + "Show Task Board Folder", priority = 20)]
        private static void RevealFolder()
        {
            string folder = Path.GetDirectoryName(ScriptPath);
            if (Directory.Exists(folder)) EditorUtility.RevealInFinder(folder);
            else OpenBoard(); // shows the "not installed" help
        }

        /// <summary>True if a task board answers on the board port.</summary>
        private static bool BoardIsUp()
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + Port + "/api/board");
                request.Timeout = 800;
                request.ReadWriteTimeout = 800;
                using (var response = (HttpWebResponse)request.GetResponse())
                    return (int)response.StatusCode == 200;
            }
            catch { return false; }
        }

        private static bool StartServer()
        {
            if (!FindPython(out string exe, out string prefixArgs))
            {
                EditorUtility.DisplayDialog("Python not found",
                    "The task board needs Python 3.9 or newer on the PATH (nothing else to install). Install it from python.org, " +
                    "tick \"Add python.exe to PATH\", then try again.", "OK");
                return false;
            }

            var stderr = new StringBuilder();
            Process process;
            try
            {
                var info = new ProcessStartInfo(exe, prefixArgs + "\"" + ScriptPath + "\" serve --port " + Port)
                {
                    WorkingDirectory = AgenticToolsPackage.ProjectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                info.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
                info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                process = Process.Start(info);
                process.OutputDataReceived += (_, __) => { }; // drain so the server never blocks on a full pipe
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Could not start the task board", e.Message, "OK");
                return false;
            }

            var timer = Stopwatch.StartNew();
            try
            {
                while (timer.ElapsedMilliseconds < StartTimeoutMs)
                {
                    EditorUtility.DisplayProgressBar("Agent Task Board", "Starting the board server...", Mathf.Clamp01(timer.ElapsedMilliseconds / (float)StartTimeoutMs));
                    if (BoardIsUp())
                    {
                        EditorPrefs.SetInt(PidPref, process.Id);
                        Debug.Log("[Task Board] Board server started on http://127.0.0.1:" + Port + "/ (Tools > Agent Task Board > Stop Board Server stops it).");
                        return true;
                    }

                    if (process.HasExited) break;
                    Thread.Sleep(150);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            string detail;
            lock (stderr) detail = stderr.Length > 0 ? stderr.ToString().Trim() : "No output from the server.";
            try { if (!process.HasExited) KillTree(process); } catch { /* already gone */ }
            Debug.LogError("[Task Board] The board server did not start:\n" + detail);
            EditorUtility.DisplayDialog("Could not start the task board", detail.Length > 600 ? detail.Substring(0, 600) + "..." : detail, "OK");
            return false;
        }

        /// <summary>Kills the process and its children (the Windows py launcher starts the real interpreter as a child).</summary>
        private static void KillTree(Process process)
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                using (Process killer = Process.Start(new ProcessStartInfo("taskkill", "/PID " + process.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true }))
                    killer.WaitForExit(4000);
            }
            else process.Kill();
        }

        private static bool TryGetOwnProcess(out Process process)
        {
            process = null;
            int pid = EditorPrefs.GetInt(PidPref, 0);
            if (pid == 0) return false;
            try
            {
                process = Process.GetProcessById(pid);
                // A recycled pid must not be killed: only python (or the py launcher) is ours.
                string name = process.ProcessName;
                if (!process.HasExited && (name.StartsWith("python", StringComparison.OrdinalIgnoreCase) || name.Equals("py", StringComparison.OrdinalIgnoreCase))) return true;
            }
            catch { /* no such process */ }

            process?.Dispose();
            process = null;
            EditorPrefs.DeleteKey(PidPref);
            return false;
        }

        /// <summary>Finds a Python 3 interpreter: python (python3 off Windows), then the Windows py launcher.</summary>
        private static bool FindPython(out string exe, out string prefixArgs)
        {
            (string, string)[] candidates = Application.platform == RuntimePlatform.WindowsEditor
                ? new[] { ("python", ""), ("py", "-3 ") }
                : new[] { ("python3", ""), ("python", "") };
            foreach ((string name, string args) in candidates)
            {
                if (RunsOk(name, args + "--version")) { exe = name; prefixArgs = args; return true; }
            }

            exe = null;
            prefixArgs = "";
            return false;
        }

        private static bool RunsOk(string exe, string args)
        {
            try
            {
                using (Process p = Process.Start(new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }))
                {
                    p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(4000)) { try { p.Kill(); } catch { /* gone */ } return false; }
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }
    }
}
