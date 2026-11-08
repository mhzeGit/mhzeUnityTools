using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Gigaduck.AgenticTools
{
    /// <summary>
    /// Tools ▸ Agentic Tools: choose which agent rules and tools this project gets, see what an install or update would change,
    /// and install them with one button. Rules are injected next to the project's own (never over them); see
    /// <see cref="AgenticInstaller"/> and the package README.
    /// </summary>
    public sealed class AgenticToolsWindow : EditorWindow
    {
        private AgenticInstaller _installer;
        private readonly Dictionary<string, bool> _enabled = new Dictionary<string, bool>();
        private Dictionary<string, string> _vars;
        private List<string> _planLines = new List<string>();
        private string _planTitle = "";
        private Vector2 _scroll, _planScroll;
        private string _error;

        [MenuItem(AgenticToolsPackage.MenuRoot + "Open Window", priority = 0)]
        public static void Open() => GetWindow<AgenticToolsWindow>("Agentic Tools").minSize = new Vector2(460, 420);

        private void OnEnable() => Reload();

        private void Reload()
        {
            _error = null;
            try
            {
                _installer = AgenticToolsPackage.CreateInstaller();
                List<string> current = _installer.CurrentModules();
                _enabled.Clear();
                foreach (JsonObject module in _installer.Modules) _enabled[module.GetString("id")] = current.Contains(module.GetString("id"));
                _vars = _installer.CurrentVariables();
            }
            catch (Exception e)
            {
                _installer = null;
                _error = e.Message;
            }
        }

        private List<string> ChosenModules() => _installer.Normalize(_enabled.Where(p => p.Value).Select(p => p.Key));

        private void OnGUI()
        {
            if (_installer == null)
            {
                EditorGUILayout.HelpBox(_error ?? "Package not found.", MessageType.Error);
                if (GUILayout.Button("Retry")) Reload();
                return;
            }

            EditorGUILayout.LabelField("Agentic Tools " + _installer.Version, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Project", AgenticToolsPackage.ProjectRoot, EditorStyles.miniLabel);
            string installed = _installer.IsInstalled ? _installer.InstalledVersion : "not installed";
            EditorGUILayout.LabelField("Installed", installed + (_installer.IsInstalled && _installer.InstalledVersion != _installer.Version ? "  (update available)" : ""));
            EditorGUILayout.HelpBox("Rules are injected as marked blocks next to the project's own rules, never over them. Files edited by hand are " +
                                    "reported as conflicts and left alone. Change shared rules and tools in the package source (mhzeUnityTools), " +
                                    "not in the installed copies.", MessageType.None);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(false), GUILayout.MaxHeight(position.height * 0.55f));
            EditorGUILayout.LabelField("Modules", EditorStyles.boldLabel);
            foreach (JsonObject module in _installer.Modules)
            {
                string id = module.GetString("id");
                using (new EditorGUI.DisabledScope(module.GetBool("required")))
                    _enabled[id] = EditorGUILayout.ToggleLeft(module.GetString("title") + (module.GetBool("required") ? " (always)" : ""), _enabled[id], EditorStyles.boldLabel);
                EditorGUILayout.LabelField(module.GetString("description"), EditorStyles.wordWrappedMiniLabel);
                GUILayout.Space(2);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Folders", EditorStyles.boldLabel);
            foreach (string name in _installer.VariableNames.ToList())
                _vars[name] = EditorGUILayout.TextField(name == "RULES_DIR" ? "Rule docs" : name == "DOCS_DIR" ? "Docs" : name, _vars[name]).Trim().Replace('\\', '/').Trim('/');
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Check", "Show what an install would change; writes nothing"))) Preview(false);
                if (GUILayout.Button(new GUIContent(_installer.IsInstalled ? "Install / Update" : "Install", "Install the chosen modules; conflicts are left alone"), GUILayout.Height(24)))
                    Install(false);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Force", "Also replace files and rule blocks that were edited by hand")) &&
                    EditorUtility.DisplayDialog("Replace hand edits?", "Every conflicting file and rule block is replaced with the package's version. " +
                                                "Move hand edits into the package source or the project's own part of the file first.", "Replace", "Cancel"))
                    Install(true);
                if (GUILayout.Button(new GUIContent("Skills for my user", "Also copy the skills to ~/.claude/skills, ~/.codex/skills, Kilo and Gemini")))
                {
                    List<string> roots = _installer.InstallPersonalSkills();
                    ShowLines("Skills copied to", roots);
                }

                if (GUILayout.Button(new GUIContent("Uninstall", "Remove every unchanged installed file and rule block")) &&
                    EditorUtility.DisplayDialog("Uninstall agentic tools?", "Removes every installed file and rule block that was not edited by hand. " +
                                                "The project's own rules stay.", "Uninstall", "Cancel"))
                {
                    AgenticInstaller.Plan plan = _installer.MakePlan(new List<string>(), _vars, false);
                    _installer.Apply(plan);
                    ShowPlan("Uninstalled", plan);
                    Reload();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open AGENTS.md")) OpenFile("AGENTS.md");
                if (GUILayout.Button("Open package rules doc")) OpenFile(_vars["RULES_DIR"] + "/AGENTIC_TOOLS.md");
                if (GUILayout.Button("Reload")) Reload();
            }

            if (_planLines.Count > 0 || _planTitle.Length > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(_planTitle, EditorStyles.boldLabel);
                _planScroll = EditorGUILayout.BeginScrollView(_planScroll);
                foreach (string line in _planLines) EditorGUILayout.SelectableLabel(line, EditorStyles.miniLabel, GUILayout.Height(14));
                EditorGUILayout.EndScrollView();
            }
        }

        private void Preview(bool force)
        {
            try { ShowPlan("Check (nothing written)", _installer.MakePlan(ChosenModules(), _vars, force)); }
            catch (Exception e) { ShowLines("Check failed", new List<string> { e.Message }); }
        }

        private void Install(bool force)
        {
            try
            {
                AgenticInstaller.Plan plan = _installer.MakePlan(ChosenModules(), _vars, force);
                _installer.Apply(plan);
                ShowPlan(force ? "Installed (forced)" : "Installed", plan);
                Debug.Log("[Agentic Tools] installed " + _installer.Version + ": " + plan.Changes + " change(s), " + plan.Conflicts.Count() + " conflict(s)");
                Reload();
            }
            catch (Exception e) { ShowLines("Install failed", new List<string> { e.Message }); }
        }

        private void ShowPlan(string title, AgenticInstaller.Plan plan)
        {
            int conflicts = plan.Conflicts.Count();
            var lines = plan.Describe(false);
            if (lines.Count == 0) lines.Add("  nothing to change: everything is up to date");
            if (conflicts > 0) lines.Add("CONFLICTS are left alone (a hand edit, or a file nobody installed). Force replaces them.");
            ShowLines(title + ": " + plan.Changes + " change(s), " + conflicts + " conflict(s)", lines);
        }

        private void ShowLines(string title, List<string> lines)
        {
            _planTitle = title;
            _planLines = lines;
            _planScroll = Vector2.zero;
            Repaint();
        }

        private static void OpenFile(string rel)
        {
            string path = Path.Combine(AgenticToolsPackage.ProjectRoot, rel);
            if (File.Exists(path)) EditorUtility.OpenWithDefaultApp(path);
            else Debug.LogWarning("[Agentic Tools] " + rel + " does not exist yet: install first.");
        }
    }
}
