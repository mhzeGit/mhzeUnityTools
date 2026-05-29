using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GitIntegration
{
    public class GitSetupWizard : EditorWindow
    {
        private GitUserConfig _userConfig;
        private List<GitRemoteInfo> _remotes = new List<GitRemoteInfo>();
        private string _repoRoot;
        private string _currentBranch;
        private string _gitVersion;
        private bool _isRepo;
        private bool _gitInstalled;
        private bool _hasGitIgnore;
        private bool _lfsInstalled;
        private Vector2 _scroll;

        private string _editName = "";
        private string _editEmail = "";
        private string _editRemoteUrl = "";
        private bool _editMode;

        public static void ShowWizard()
        {
            var win = GetWindow<GitSetupWizard>(true, "Git Configuration");
            win.minSize = new Vector2(480, 380);
            win.maxSize = new Vector2(600, 520);
            win.DetectConfig();
            win.Show();
        }

        private void DetectConfig()
        {
            _gitInstalled = GitOperations.IsGitInstalled();
            if (!_gitInstalled) return;

            _gitVersion = GitOperations.GetGitVersion();
            _isRepo = GitOperations.IsInsideRepo();
            if (!_isRepo) return;

            _repoRoot = GitOperations.RepoRoot;
            _currentBranch = GitOperations.GetCurrentBranch();
            _userConfig = GitOperations.GetUserConfig();
            _remotes = GitOperations.GetRemotes();
            _hasGitIgnore = GitOperations.HasGitIgnore();
            _lfsInstalled = GitOperations.IsLfsInstalled();

            _editName = _userConfig?.UserName ?? "";
            _editEmail = _userConfig?.UserEmail ?? "";
            _editRemoteUrl = _remotes.Count > 0 ? _remotes[0].FetchUrl : "";
            _editMode = false;
        }


        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            GUILayout.Space(10);

            if (!_gitInstalled)
            {
                DrawNotInstalled();
                EditorGUILayout.EndScrollView();
                return;
            }

            if (!_isRepo)
            {
                DrawNotARepo();
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawDetectedConfig();

            EditorGUILayout.EndScrollView();
        }


        private void DrawNotInstalled()
        {
            EditorGUILayout.HelpBox("Git is not installed or not found in PATH.\nPlease install Git and restart Unity.", MessageType.Error);
            GUILayout.Space(8);
            if (GUILayout.Button("Retry Detection", GUILayout.Height(28)))
                DetectConfig();
        }


        private void DrawNotARepo()
        {
            EditorGUILayout.HelpBox("No Git repository detected in the project directory.\nIf you already have one, make sure the .git folder is at the project root.", MessageType.Warning);
            GUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Initialize Git Repo", GUILayout.Height(28)))
            {
                if (EditorUtility.DisplayDialog("Initialize Git",
                    "This will run 'git init' in the project root and create a Unity .gitignore.\n\nContinue?",
                    "Initialize", "Cancel"))
                {
                    GitOperations.InitRepo();
                    GitOperations.CreateUnityGitIgnore();
                    DetectConfig();
                }
            }
            if (GUILayout.Button("Retry Detection", GUILayout.Height(28)))
            {
                GitOperations.InvalidateRepoRoot();
                DetectConfig();
            }
            EditorGUILayout.EndHorizontal();
        }


        private void DrawDetectedConfig()
        {
            EditorGUILayout.HelpBox("Git repository detected.", MessageType.Info);
            GUILayout.Space(8);

            GitUIStyles.BeginCard();
            GUILayout.Label("Repository", GitUIStyles.SubHeader);
            GUILayout.Space(2);
            DrawReadOnlyField("Root", _repoRoot);
            DrawReadOnlyField("Branch", _currentBranch);
            DrawReadOnlyField("Git Version", _gitVersion);
            GitUIStyles.EndCard();

            GUILayout.Space(6);

            GitUIStyles.BeginCard();
            GUILayout.Label("Identity", GitUIStyles.SubHeader);
            GUILayout.Space(2);

            if (!_editMode)
            {
                if (!string.IsNullOrEmpty(_userConfig?.UserName))
                    DrawReadOnlyField("Name", _userConfig.UserName);
                else
                    DrawWarningField("Name", "(not set)");

                if (!string.IsNullOrEmpty(_userConfig?.UserEmail))
                    DrawReadOnlyField("Email", _userConfig.UserEmail);
                else
                    DrawWarningField("Email", "(not set)");
            }
            else
            {
                _editName = EditorGUILayout.TextField("Name", _editName);
                _editEmail = EditorGUILayout.TextField("Email", _editEmail);
            }
            GitUIStyles.EndCard();

            GUILayout.Space(6);

            GitUIStyles.BeginCard();
            GUILayout.Label("Remotes", GitUIStyles.SubHeader);
            GUILayout.Space(2);

            if (_remotes.Count == 0)
            {
                if (!_editMode)
                    GUILayout.Label("  No remotes configured.", GitUIStyles.MutedLabel);
                else
                    _editRemoteUrl = EditorGUILayout.TextField("Origin URL", _editRemoteUrl);
            }
            else
            {
                foreach (var remote in _remotes)
                    DrawReadOnlyField(remote.Name, remote.FetchUrl);

                if (_editMode)
                {
                    GUILayout.Space(4);
                    _editRemoteUrl = EditorGUILayout.TextField("Update origin", _editRemoteUrl);
                }
            }
            GitUIStyles.EndCard();

            GUILayout.Space(6);

            GitUIStyles.BeginCard();
            GUILayout.Label("Extras", GitUIStyles.SubHeader);
            GUILayout.Space(2);
            DrawReadOnlyField(".gitignore", _hasGitIgnore ? "Present" : "Missing");
            DrawReadOnlyField("Git LFS", _lfsInstalled ? "Installed" : "Not installed");

            if (!_hasGitIgnore)
            {
                GUILayout.Space(4);
                if (GUILayout.Button("Create Unity .gitignore", GUILayout.Height(24)))
                {
                    GitOperations.CreateUnityGitIgnore();
                    _hasGitIgnore = true;
                }
            }
            if (!_lfsInstalled)
            {
                GUILayout.Space(4);
                if (GUILayout.Button("Initialize LFS", GUILayout.Height(24)))
                {
                    GitOperations.InitLfs();
                    _lfsInstalled = GitOperations.IsLfsInstalled();
                }
            }
            GitUIStyles.EndCard();

            GUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();

            if (!_editMode)
            {
                if (GUILayout.Button("Edit Configuration", GUILayout.Height(28)))
                    _editMode = true;
            }
            else
            {
                if (GUILayout.Button("Save Changes", GUILayout.Height(28)))
                {
                    ApplyEdits();
                    _editMode = false;
                }
                if (GUILayout.Button("Cancel", GUILayout.Height(28)))
                {
                    _editMode = false;
                    DetectConfig();
                }
            }

            if (GUILayout.Button("Refresh", GUILayout.Height(28), GUILayout.Width(80)))
                DetectConfig();

            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);
        }


        private void ApplyEdits()
        {
            if (!string.IsNullOrWhiteSpace(_editName) && !string.IsNullOrWhiteSpace(_editEmail))
                GitOperations.SetUserConfig(_editName, _editEmail);

            if (!string.IsNullOrEmpty(_editRemoteUrl))
            {
                if (_remotes.Exists(r => r.Name == "origin"))
                    GitOperations.SetRemoteUrl("origin", _editRemoteUrl);
                else
                    GitOperations.AddRemote("origin", _editRemoteUrl);
            }

            DetectConfig();
        }


        private void DrawReadOnlyField(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", GitUIStyles.MutedLabel, GUILayout.Width(90));
            EditorGUILayout.SelectableLabel(value, EditorStyles.label, GUILayout.Height(18));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawWarningField(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", GitUIStyles.MutedLabel, GUILayout.Width(90));
            var style = new GUIStyle(EditorStyles.label);
            style.normal.textColor = GitUIStyles.AccentYellow;
            GUILayout.Label(value, style);
            EditorGUILayout.EndHorizontal();
        }
    }
}
