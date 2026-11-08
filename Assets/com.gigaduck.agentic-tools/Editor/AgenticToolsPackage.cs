using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Gigaduck.AgenticTools
{
    /// <summary>
    /// Finds this package on disk (in <c>Library/PackageCache</c> when imported by Git URL, or under <c>Assets/</c> in its
    /// source project), creates the <see cref="AgenticInstaller"/> for the open project, and keeps an installed project up to
    /// date: when the package version differs from the one recorded in <c>.agentic-tools.json</c> (the pin was moved and the
    /// package reimported), it installs the update once on editor load. That writes only the agent tool and rule files the
    /// project already chose (never a scene, prefab or asset), replaces only untouched copies, and reports hand-edited ones in
    /// the Console as conflicts. A project that never installed the tools is left alone: Tools ▸ Agentic Tools installs them.
    /// </summary>
    [InitializeOnLoad]
    public static class AgenticToolsPackage
    {
        public const string MenuRoot = "Tools/Agentic Tools/";
        private const string HintPref = "Gigaduck.AgenticTools.InstallHintShown.";

        static AgenticToolsPackage()
        {
            if (Application.isBatchMode) return; // the agent build copy and CI never change their own files
            EditorApplication.delayCall += UpdateIfOutdated;
        }

        /// <summary>The open Unity project's root folder.</summary>
        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        /// <summary>The package folder (the one holding package.json and Install~), or null if it cannot be found.</summary>
        public static string PackageRoot
        {
            get
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AgenticToolsPackage).Assembly);
                if (info != null && Directory.Exists(Path.Combine(info.resolvedPath, "Install~"))) return info.resolvedPath;
                // Not imported as a package: the source project keeps it as a folder under Assets/.
                string guid = AssetDatabase.FindAssets("Gigaduck.AgenticTools.Editor t:AssemblyDefinitionAsset").FirstOrDefault();
                if (guid == null) return null;
                string asmdef = Path.GetFullPath(Path.Combine(ProjectRoot, AssetDatabase.GUIDToAssetPath(guid)));
                string root = Path.GetDirectoryName(Path.GetDirectoryName(asmdef));
                return Directory.Exists(Path.Combine(root, "Install~")) ? root : null;
            }
        }

        public static AgenticInstaller CreateInstaller()
        {
            string root = PackageRoot;
            if (root == null) throw new InvalidOperationException("Agentic Tools: cannot find the package folder with Install~.");
            return new AgenticInstaller(root, ProjectRoot);
        }

        private static void UpdateIfOutdated()
        {
            AgenticInstaller installer;
            try { installer = CreateInstaller(); }
            catch (Exception e)
            {
                Debug.LogWarning("[Agentic Tools] " + e.Message);
                return;
            }

            if (!installer.IsInstalled)
            {
                string key = HintPref + ProjectRoot;
                if (!EditorPrefs.GetBool(key, false))
                {
                    EditorPrefs.SetBool(key, true);
                    Debug.Log("[Agentic Tools] Not installed in this project yet: open " + MenuRoot.Replace("/", " ▸ ").TrimEnd(' ', '▸') +
                              " and press Install / Update to add the agent rules and tools.");
                }

                return;
            }

            if (installer.InstalledVersion == installer.Version) return;
            Run(installer, installer.CurrentModules(), false, "updated from " + installer.InstalledVersion + " to " + installer.Version);
        }

        /// <summary>Installs <paramref name="modules"/> with the recorded variables and logs the outcome.</summary>
        public static AgenticInstaller.Plan Run(AgenticInstaller installer, System.Collections.Generic.List<string> modules, bool force, string what)
        {
            try
            {
                AgenticInstaller.Plan plan = installer.MakePlan(modules, installer.CurrentVariables(), force);
                installer.Apply(plan);
                string lines = string.Join("\n", plan.Describe(false));
                int conflicts = plan.Conflicts.Count();
                string text = "[Agentic Tools] " + what + ": " + plan.Changes + " change(s), " + conflicts + " conflict(s)" + (lines.Length > 0 ? "\n" + lines : "");
                if (conflicts > 0)
                    Debug.LogWarning(text + "\nConflicts were left alone (a hand edit, or a file nobody installed). Put the change in the package source " +
                                     "or the project's own part of the file, then update again; Tools ▸ Agentic Tools ▸ Force replaces them.");
                else Debug.Log(text);
                return plan;
            }
            catch (Exception e)
            {
                Debug.LogError("[Agentic Tools] " + what + " failed: " + e.Message);
                return null;
            }
        }

        [MenuItem(MenuRoot + "Install or Update", priority = 1)]
        private static void MenuInstall()
        {
            AgenticInstaller installer = CreateInstaller();
            Run(installer, installer.CurrentModules(), false, installer.IsInstalled ? "update" : "install");
        }
    }
}
