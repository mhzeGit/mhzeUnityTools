using UnityEditor;
using UnityEngine;

namespace GitIntegration
{
    public class GitDiffViewerWindow : EditorWindow
    {
        private string _diffText = "";
        private string _title = "";
        private string _commitHash = "";
        private string _filePath = "";
        private Vector2 _scroll;
        private bool _wordWrap;
        private string _searchTerm = "";
        private float _fontSize = 11f;


        public static void ShowDiff(string commitHash, string filePath, string shortHash)
        {
            var win = GetWindow<GitDiffViewerWindow>(true, "Diff Viewer");
            win.minSize = new Vector2(600, 400);
            win._commitHash = commitHash;
            win._filePath = filePath;
            win._title = $"{shortHash} - {filePath}";
            win._diffText = GitOperations.GetDiff(commitHash, filePath);
            win._scroll = Vector2.zero;
            win.Show();
        }

        public static void ShowWorkingDiff(string filePath)
        {
            var win = GetWindow<GitDiffViewerWindow>(true, "Diff Viewer");
            win.minSize = new Vector2(600, 400);
            win._commitHash = "";
            win._filePath = filePath;
            win._title = $"Working changes - {filePath}";
            win._diffText = GitOperations.GetDiffForWorkingCopy(filePath);
            win._scroll = Vector2.zero;
            win.Show();
        }


        private void OnGUI()
        {
            DrawToolbar();
            GitUIStyles.DrawSeparator(1, 0, 2);

            if (string.IsNullOrEmpty(_diffText))
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("No diff available (file may be binary or identical).", GitUIStyles.CenteredGreyMini);
                GUILayout.FlexibleSpace();
                return;
            }

            DrawStats();
            DrawDiffContent();
        }


        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label(_title, EditorStyles.boldLabel, GUILayout.MaxWidth(position.width * 0.6f));
            GUILayout.FlexibleSpace();

            GUILayout.Label("S", GUILayout.Width(18));
            _searchTerm = EditorGUILayout.TextField(_searchTerm, EditorStyles.toolbarSearchField, GUILayout.Width(140));

            _wordWrap = GUILayout.Toggle(_wordWrap, "Wrap", EditorStyles.toolbarButton, GUILayout.Width(42));

            if (GUILayout.Button("A-", EditorStyles.toolbarButton, GUILayout.Width(24)))
                _fontSize = Mathf.Max(8, _fontSize - 1);
            if (GUILayout.Button("A+", EditorStyles.toolbarButton, GUILayout.Width(24)))
                _fontSize = Mathf.Min(20, _fontSize + 1);

            if (GUILayout.Button("Copy", EditorStyles.toolbarButton, GUILayout.Width(42)))
                EditorGUIUtility.systemCopyBuffer = _diffText;

            EditorGUILayout.EndHorizontal();
        }


        private void DrawStats()
        {
            int adds = 0, removes = 0;
            foreach (var line in _diffText.Split('\n'))
            {
                if (line.StartsWith("+") && !line.StartsWith("+++")) adds++;
                else if (line.StartsWith("-") && !line.StartsWith("---")) removes++;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);

            var prevColor = GUI.color;
            GUI.color = GitUIStyles.AccentGreen;
            GUILayout.Label($"+{adds}", EditorStyles.boldLabel, GUILayout.Width(60));
            GUI.color = GitUIStyles.AccentRed;
            GUILayout.Label($"-{removes}", EditorStyles.boldLabel, GUILayout.Width(60));
            GUI.color = prevColor;

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GitUIStyles.DrawSeparator(1, 2, 2);
        }


        private void DrawDiffContent()
        {

            var monoStyle = new GUIStyle(GitUIStyles.MonoLabel)
            {
                fontSize = (int)_fontSize,
                wordWrap = _wordWrap,
            };

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            string searchLower = string.IsNullOrEmpty(_searchTerm) ? null : _searchTerm.ToLowerInvariant();
            int lineNum = 0;

            foreach (var rawLine in _diffText.Split('\n'))
            {
                lineNum++;
                string line = rawLine;

                bool matchesSearch = searchLower != null && line.ToLowerInvariant().Contains(searchLower);

                Color bg = Color.clear;
                if (line.StartsWith("@@"))
                    bg = GitUIStyles.DiffHunkBg;
                else if (line.StartsWith("+") && !line.StartsWith("+++"))
                    bg = GitUIStyles.DiffAddBg;
                else if (line.StartsWith("-") && !line.StartsWith("---"))
                    bg = GitUIStyles.DiffRemoveBg;

                var content = new GUIContent(line);
                float height = monoStyle.CalcHeight(content, position.width - 60);
                var rect = GUILayoutUtility.GetRect(position.width - 40, Mathf.Max(height, 18));

                if (bg != Color.clear)
                    EditorGUI.DrawRect(rect, bg);

                if (matchesSearch)
                {
                    var highlight = new Color(1f, 1f, 0f, 0.18f);
                    EditorGUI.DrawRect(rect, highlight);
                }

                var gutterRect = new Rect(rect.x, rect.y, 40, rect.height);
                var gutterStyle = new GUIStyle(GitUIStyles.MutedLabel) { alignment = TextAnchor.MiddleRight, fontSize = (int)_fontSize - 1 };
                GUI.Label(gutterRect, lineNum.ToString(), gutterStyle);

                var contentRect = new Rect(rect.x + 44, rect.y, rect.width - 48, rect.height);
                GUI.Label(contentRect, line, monoStyle);
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
