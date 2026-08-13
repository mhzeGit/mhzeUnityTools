using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GitIntegration
{
    public class GitIntegrationWindow : EditorWindow
    {
        const float TOOLBAR_H  = 22f;
        const float SEARCH_H   = 24f;
        const float COL_HDR_H  = 22f;
        const float ROW_H      = 24f;
        const float LANE_W     = 14f;
        const float DOT_R      = 4.5f;
        const float LINE_W     = 2.0f;

        private float _sidebarW      = 180f;
        private float _detailH       = 250f;
        private float _detailTreeW   = 240f;
        private float _changesTreeW  = 260f;

        private bool  _draggingSidebar     = false;
        private bool  _draggingDetail      = false;
        private bool  _draggingDetailTree  = false;
        private bool  _draggingChangesTree = false;

        private List<GraphRow>        _graphRows     = new List<GraphRow>();
        private List<GitBranchInfo>   _branches      = new List<GitBranchInfo>();
        private List<GitStatusEntry>  _statusEntries = new List<GitStatusEntry>();
        private List<GitFileChange>   _selectedFiles = new List<GitFileChange>();
        private List<GraphRow>        _filteredCache;
        private string                _lastFilterStr  = "";
        private FileTreeNode          _detailTree;
        private FileTreeNode          _changesTree;

        private int     _selectedRowIdx = -1;
        private int     _maxLaneCount   = 1;
        private string  _searchFilter   = "";
        private string  _currentBranch  = "";
        private bool    _gitAvailable, _isRepo;

        private bool _showChanges = false;
        private bool _showConfiguration = false;

        private string  _selectedChangePath   = null;
        private string  _selectedChangeStatus = null;
        private string  _inlineDiffText       = null;
        private Vector2 _inlineDiffScroll;

        private string  _detailDiffPath   = null;
        private string  _detailDiffText   = null;
        private Vector2 _detailDiffScroll;

        private Vector2 _sidebarScroll;
        private Vector2 _graphScroll;
        private Vector2 _detailScroll;
        private Vector2 _changesScroll;
        private Vector2 _configScroll;

        private GitUserConfig _configUserConfig;
        private List<GitRemoteInfo> _configRemotes = new List<GitRemoteInfo>();
        private string _configRepoRoot;
        private string _configCurrentBranch;
        private string _configGitVersion;
        private bool _configIsRepo;
        private bool _configGitInstalled;
        private bool _configHasGitIgnore;
        private bool _configLfsInstalled;

        private string _configEditName = "";
        private string _configEditEmail = "";
        private string _configEditRemoteUrl = "";
        private bool _configEditMode;


        [MenuItem("Tools/Git Integration %&g", false, 200)]
        public static void ShowWindow()
        {
            var win = GetWindow<GitIntegrationWindow>();
            win.titleContent = new GUIContent("Git Integration");
            win.minSize = new Vector2(620, 440);
            win.Show();
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            CheckGitState();
            if (_gitAvailable && _isRepo) RefreshAll();
        }


        private void OnGUI()
        {
            if (Event.current.type == EventType.MouseMove)
                Repaint();

            if (!_gitAvailable) { DrawNoGit();  return; }
            if (!_isRepo)       { DrawNoRepo(); return; }

            DrawToolbar();

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));

            EditorGUILayout.BeginVertical(GUILayout.Width(_sidebarW), GUILayout.ExpandHeight(true));
            DrawSidebar();
            EditorGUILayout.EndVertical();

            var vSep = GUILayoutUtility.GetRect(5, 5, GUILayout.Width(5), GUILayout.ExpandHeight(true));
            DrawVerticalSplitter(vSep, ref _draggingSidebar, ref _sidebarW, 120f, 320f);

            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (_showConfiguration) DrawConfigurationPanel();
            else if (_showChanges) DrawChangesPanel();
            else              DrawGraphPanel();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }


        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label(_currentBranch, EditorStyles.boldLabel, GUILayout.MaxWidth(200));
            GUILayout.FlexibleSpace();

            if (_statusEntries.Count > 0)
            {
                var prev = GUI.color;
                GUI.color = GitUIStyles.AccentYellow;
                if (GUILayout.Button($" {_statusEntries.Count} uncommitted ", EditorStyles.toolbarButton))
                    _showChanges = true;
                GUI.color = prev;
            }

            if (GUILayout.Button("Fetch",  EditorStyles.toolbarButton, GUILayout.Width(44)))
            { GitOperations.Fetch(); RefreshAll(); }

            if (GUILayout.Button("Config", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                _showConfiguration = !_showConfiguration;
                if (_showConfiguration)
                {
                    _showChanges = false;
                    RefreshConfigState();
                }
            }

            if (GUILayout.Button("R", EditorStyles.toolbarButton, GUILayout.Width(22)))
                RefreshAll();

            EditorGUILayout.EndHorizontal();
        }


        private void DrawSidebar()
        {
            _sidebarScroll = EditorGUILayout.BeginScrollView(_sidebarScroll, GUILayout.ExpandHeight(true));

            GUILayout.Space(4);
            GUILayout.Label("VIEWS", GitUIStyles.SectionLabel);

            DrawSidebarRow("History", "UnityEditor.SceneHierarchyWindow", !_showChanges && !_showConfiguration, 
                () => { _showChanges = false; _showConfiguration = false; });

            bool hasChanges = _statusEntries.Count > 0;
            DrawSidebarRow("Working Tree", "UnityEditor.ProjectBrowser", _showChanges && !_showConfiguration, 
                () => { _showChanges = true; _showConfiguration = false; },
                hasChanges ? _statusEntries.Count.ToString() : null);

            DrawSidebarRow("Configuration", "UnityEditor.SettingsWindow", _showConfiguration,
                () => { _showConfiguration = true; _showChanges = false; RefreshConfigState(); });

            EditorGUILayout.EndScrollView();
        }

        private void DrawSidebarRow(string label, string iconName, bool selected, System.Action onClick, string badge = null)
        {
            var rowRect = GUILayoutUtility.GetRect(_sidebarW - 8, 26);

            if (selected)
                EditorGUI.DrawRect(rowRect, GitUIStyles.SelectedRowBg);
            else if (rowRect.Contains(Event.current.mousePosition) && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rowRect, GitUIStyles.HoverRowBg);

            if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none))
                onClick?.Invoke();

            if (selected)
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y + 3, 3, rowRect.height - 6), GitUIStyles.AccentBlue);

            float textX = rowRect.x + 12;
            float textW = rowRect.width - 20;
            if (badge != null) textW -= 34;

            var labelStyle = selected ? EditorStyles.boldLabel : EditorStyles.label;
            GUI.Label(new Rect(textX, rowRect.y + 4, textW, 18), label, labelStyle);

            if (badge != null)
            {
                var badgeRect = new Rect(rowRect.xMax - 36, rowRect.y + 6, 30, 14);
                GitUIStyles.DrawRoundedBadge(badgeRect, badge, GitUIStyles.AccentYellow);
            }
        }


        private void DrawGraphPanel()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Search:", EditorStyles.toolbarButton, GUILayout.Width(50));
            var prevFilter = _searchFilter;
            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField);
            if (_searchFilter != prevFilter) { _selectedRowIdx = -1; _detailDiffPath = null; _detailDiffText = null; _filteredCache = null; }
            if (GUILayout.Button("X", EditorStyles.toolbarButton, GUILayout.Width(20)))
            { _searchFilter = ""; _selectedRowIdx = -1; _detailDiffPath = null; _detailDiffText = null; _filteredCache = null; }
            EditorGUILayout.EndHorizontal();

            DrawColumnHeaders();

            bool hasDetail = _selectedRowIdx >= 0 && _graphRows.Count > 0;

            Rect graphRect = GUILayoutUtility.GetRect(10, 60,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawGraphArea(graphRect, graphRect.width);

            if (hasDetail)
            {
                var sepR = GUILayoutUtility.GetRect(0, 5, GUILayout.ExpandWidth(true), GUILayout.Height(5));
                DrawHorizontalSplitter(sepR, ref _draggingDetail, ref _detailH, 80f, 550f, invert: true);
                DrawDetailPanel();
            }
        }


        private void DrawColumnHeaders()
        {
            float rightW   = position.width - _sidebarW - 5;
            float graphColW = _maxLaneCount * LANE_W + 8f;
            var rect = GUILayoutUtility.GetRect(rightW, COL_HDR_H, GUILayout.Width(rightW), GUILayout.Height(COL_HDR_H));
            EditorGUI.DrawRect(rect, GitUIStyles.HeaderBg);

            float x = rect.x + graphColW + 4;
            GUI.Label(new Rect(x,              rect.y, rect.width * 0.5f, COL_HDR_H), "Summary", GitUIStyles.ColumnHeader);
            GUI.Label(new Rect(rect.xMax - 188, rect.y, 100, COL_HDR_H),              "Author",  GitUIStyles.ColumnHeader);
            GUI.Label(new Rect(rect.xMax - 88,  rect.y, 80,  COL_HDR_H),              "Date",    GitUIStyles.ColumnHeader);

            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), GitUIStyles.SeparatorColor);
        }


        private void DrawGraphArea(Rect area, float rightW)
        {
            if (area.height < 1) return;

            var filtered   = GetFilteredRows();
            float graphColW = _maxLaneCount * LANE_W + 8f;
            float contentW  = Mathf.Max(rightW, graphColW + 400);
            float contentH  = filtered.Count * ROW_H + 4;

            _graphScroll = GUI.BeginScrollView(area, _graphScroll, new Rect(0, 0, contentW, contentH));

            int first = Mathf.Max(0, Mathf.FloorToInt(_graphScroll.y / ROW_H) - 1);
            int last  = Mathf.Min(filtered.Count - 1, Mathf.CeilToInt((_graphScroll.y + area.height) / ROW_H) + 1);

            for (int i = first; i <= last; i++)
                DrawRow(filtered, i, contentW, graphColW);

            if (filtered.Count == 0)
                GUI.Label(new Rect(0, 40, contentW, 30), "No commits found.", GitUIStyles.CenteredGreyMini);

            GUI.EndScrollView();
        }


        private void DrawRow(List<GraphRow> rows, int idx, float contentW, float graphColW)
        {
            var row      = rows[idx];
            float y      = idx * ROW_H;
            var rowRect  = new Rect(0, y, contentW, ROW_H);
            bool selected = _selectedRowIdx == idx;

            if (!selected && rowRect.Contains(Event.current.mousePosition) && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rowRect, GitUIStyles.HoverRowBg);

            if (selected)
                EditorGUI.DrawRect(rowRect, GitUIStyles.SelectedRowBg);

            if (selected)
                EditorGUI.DrawRect(new Rect(0, y, 2, ROW_H), GitUIStyles.AccentBlue);

            DrawGraphForRow(row, new Rect(0, y, graphColW, ROW_H));

            float refX    = graphColW + 2;
            float refUsed = DrawRefLabels(row.Commit, refX, y);

            float infoX   = refX + refUsed;
            float dateW   = 80f;
            float authW   = 100f;
            float circleW = 20f;
            float msgW    = Mathf.Max(contentW - infoX - dateW - authW - circleW - 16, 60);

            var msgStyle = selected ? EditorStyles.boldLabel : EditorStyles.label;
            GUI.Label(new Rect(infoX, y + 3, msgW, ROW_H - 6),
                      Truncate(row.Commit.Message, 90), msgStyle);

            GitUIStyles.DrawAuthorCircle(
                new Rect(infoX + msgW + 4, y + 4, 16, 16),
                row.Commit.Author, row.Commit.AuthorEmail);

            GUI.Label(new Rect(infoX + msgW + 22, y + 3, authW - 22, ROW_H - 6),
                      Truncate(row.Commit.Author, 14), GitUIStyles.MutedLabel);

            GUI.Label(new Rect(contentW - dateW - 4, y + 3, dateW, ROW_H - 6),
                      row.Commit.RelativeDate, GitUIStyles.MutedLabel);

            EditorGUI.DrawRect(new Rect(0, y + ROW_H - 1, contentW, 1),
                new Color(GitUIStyles.SeparatorColor.r, GitUIStyles.SeparatorColor.g,
                          GitUIStyles.SeparatorColor.b, 0.4f));

            if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none))
            {
                if (_selectedRowIdx == idx)
                    _selectedRowIdx = -1;
                else
                {
                    _selectedRowIdx = idx;
                    _selectedFiles  = GitOperations.GetCommitFiles(row.Commit.Hash);
                    _detailTree     = GitFileTreeView.Build(_selectedFiles);
                    _detailScroll   = Vector2.zero;
                    _detailDiffPath = null;
                    _detailDiffText = null;
                }
                Repaint();
            }
        }


        private void DrawGraphForRow(GraphRow row, Rect r)
        {
            float cx  = r.x + row.Lane * LANE_W + LANE_W * 0.5f;
            float top = r.y;
            float bot = r.yMax;
            float mid = r.y + ROW_H * 0.5f;

            if (Event.current.type == EventType.Repaint)
            {
                Handles.BeginGUI();

                for (int i = 0; i < row.Passthrough.Count; i++)
                {
                    var (lane, color) = row.Passthrough[i];
                    float lx = r.x + lane * LANE_W + LANE_W * 0.5f;
                    DrawVLineAA(lx, top, bot, color);
                }

                for (int i = 0; i < row.Converging.Count; i++)
                {
                    var (fromLane, color) = row.Converging[i];
                    float fx = r.x + fromLane * LANE_W + LANE_W * 0.5f;
                    DrawBezierLine(fx, top, cx, mid, color);
                }

                for (int i = 0; i < row.Diverging.Count; i++)
                {
                    var (toLane, color) = row.Diverging[i];
                    float tx = r.x + toLane * LANE_W + LANE_W * 0.5f;
                    DrawBezierLine(cx, mid, tx, bot, color);
                }

                Handles.EndGUI();

                var prevColor = GUI.color;
                GUI.color = row.DotColor;
                GUI.DrawTexture(
                    new Rect(cx - DOT_R, mid - DOT_R, DOT_R * 2f, DOT_R * 2f),
                    GitUIStyles.CircleTexture, ScaleMode.ScaleToFit);
                GUI.color = prevColor;
            }
        }


        private float DrawRefLabels(GitCommitInfo commit, float startX, float y)
        {
            if (commit.Refs == null || commit.Refs.Count == 0) return 0f;

            var pills = new System.Collections.Generic.List<(string label, Color color)>();
            var localNames = new System.Collections.Generic.HashSet<string>();

            foreach (var refStr in commit.Refs)
            {
                if (refStr.StartsWith("HEAD ->", StringComparison.Ordinal))
                {
                    string name = refStr.Substring(7).Trim();
                    localNames.Add(name);
                    pills.Add((name, GitUIStyles.AccentGreen));
                }
                else if (refStr.StartsWith("tag:", StringComparison.Ordinal))
                {
                    string tag = refStr.Substring(4).Trim();
                    pills.Add(("tag " + tag, GitUIStyles.AccentYellow));
                }
                else if (!refStr.Contains("/"))
                {
                    localNames.Add(refStr);
                    pills.Add((refStr, GitUIStyles.AccentGreen));
                }
            }

            foreach (var refStr in commit.Refs)
            {
                if (!refStr.Contains("/") || refStr.StartsWith("HEAD ->") || refStr.StartsWith("tag:"))
                    continue;

                string stripped = refStr.StartsWith("origin/", StringComparison.Ordinal)
                    ? refStr.Substring(7) : refStr;

                if (stripped.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (localNames.Contains(stripped))
                    continue;

                pills.Add(("up " + stripped, GitUIStyles.AccentBlue));
            }

            float x = startX;
            foreach (var (label, bg) in pills)
            {
                float w = Mathf.Max(EditorStyles.miniLabel.CalcSize(new GUIContent(label)).x + 14, 30);
                var pillRect = new Rect(x, y + 5, w, ROW_H - 10);
                GitUIStyles.DrawRefPill(pillRect, label, bg);
                x += w + 3;
            }

            return x - startX + 2;
        }


        private void DrawDetailPanel()
        {
            var filtered = GetFilteredRows();
            if (_selectedRowIdx < 0 || _selectedRowIdx >= filtered.Count) return;
            var commit = filtered[_selectedRowIdx].Commit;

            var groupRect = EditorGUILayout.BeginVertical(GUILayout.Height(_detailH));
            EditorGUI.DrawRect(groupRect, GitUIStyles.CardBg);

            var headerRect = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, GitUIStyles.HeaderBg);

            GitUIStyles.DrawAuthorCircle(
                new Rect(headerRect.x + 8, headerRect.y + 6, 28, 28),
                commit.Author, commit.AuthorEmail);

            GUI.Label(new Rect(headerRect.x + 42, headerRect.y + 2, headerRect.width - 240, 20),
                      Truncate(commit.Message, 100), EditorStyles.boldLabel);

            GUI.Label(new Rect(headerRect.x + 42, headerRect.y + 21, headerRect.width - 100, 16),
                $"{commit.ShortHash} - {commit.Author} - {commit.Date}", GitUIStyles.MutedLabel);

            if (GUI.Button(new Rect(headerRect.xMax - 24, headerRect.y + 4, 20, 20), "X", EditorStyles.miniLabel))
            {
                _selectedRowIdx = -1;
                _detailDiffPath = null;
                _detailDiffText = null;
                EditorGUILayout.EndVertical();
                return;
            }

            int fileCount = _selectedFiles != null ? _selectedFiles.Count : 0;
            GitUIStyles.DrawRoundedBadge(
                new Rect(headerRect.xMax - 90, headerRect.y + 11, 58, 18),
                $"{fileCount} files", GitUIStyles.AccentBlue);

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));

            EditorGUILayout.BeginVertical(
                _detailDiffPath != null
                    ? (GUILayoutOption[])new[] { GUILayout.Width(_detailTreeW), GUILayout.ExpandHeight(true) }
                    : (GUILayoutOption[])new[] { GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true) });

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll, GUILayout.ExpandHeight(true));

            if (_detailTree != null)
            {
                GitFileTreeView.DrawLayout(
                    _detailTree,
                    commit.Hash,
                    onDiff: null,
                    (path, status) =>
                    {
                        if (EditorUtility.DisplayDialog("Restore File",
                            $"Restore '{path}' -> commit {commit.ShortHash}?\n\nThis is LOCAL only.",
                            "Restore", "Cancel"))
                        {
                            GitOperations.RestoreFileFromCommit(commit.Hash, path);
                            AssetDatabase.Refresh();
                            _statusEntries = GitOperations.GetStatus();
                            _changesTree = GitFileTreeView.Build(_statusEntries);
                        }
                    },
                    "Restore",
                    (path, _) => GitFileTreeView.PingAsset(path),
                    onRowClick: (path, status) =>
                    {
                        _detailDiffPath   = path;
                        _detailDiffText   = GitOperations.GetDiff(commit.Hash, path);
                        _detailDiffScroll = Vector2.zero;
                        Repaint();
                    }
                );
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            if (_detailDiffPath != null)
            {
                var sep = GUILayoutUtility.GetRect(5, 5, GUILayout.Width(5), GUILayout.ExpandHeight(true));
                DrawVerticalSplitter(sep, ref _draggingDetailTree, ref _detailTreeW, 120f, 520f);

                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                DrawDetailInlineDiff();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }


        private void DrawDetailInlineDiff()
        {
            var headerRect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, GitUIStyles.HeaderBg);

            string displayName = System.IO.Path.GetFileName(_detailDiffPath);
            GUI.Label(new Rect(headerRect.x + 8, headerRect.y + 6, headerRect.width - 80, 18),
                      displayName, EditorStyles.boldLabel);

            if (GUI.Button(new Rect(headerRect.xMax - 70, headerRect.y + 5, 40, 18),
                           "Ping", EditorStyles.miniButton))
                GitFileTreeView.PingAsset(_detailDiffPath);

            if (GUI.Button(new Rect(headerRect.xMax - 26, headerRect.y + 5, 20, 18),
                           "X", EditorStyles.miniLabel))
            {
                _detailDiffPath = null;
                _detailDiffText = null;
                Repaint();
                return;
            }

            var scrollRect = GUILayoutUtility.GetRect(0, 0, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(scrollRect, GitUIStyles.CardBg);

            if (string.IsNullOrEmpty(_detailDiffText))
            {
                GUI.Label(new Rect(scrollRect.x, scrollRect.y + scrollRect.height * 0.4f, scrollRect.width, 20),
                          "No diff available (file may be binary or identical).", GitUIStyles.CenteredGreyMini);
                return;
            }

            var monoStyle   = new GUIStyle(GitUIStyles.MonoLabel)   { fontSize = 11, wordWrap = false };
            var gutterStyle = new GUIStyle(GitUIStyles.MutedLabel)   { alignment = TextAnchor.MiddleRight, fontSize = 10 };

            string[] lines   = _detailDiffText.Split('\n');
            float    lineH   = 16f;
            float    contentH = lines.Length * lineH;
            float    contentW = scrollRect.width - 16;

            _detailDiffScroll = GUI.BeginScrollView(scrollRect, _detailDiffScroll,
                new Rect(0, 0, contentW, contentH));

            int firstLine = Mathf.Max(0, Mathf.FloorToInt(_detailDiffScroll.y / lineH) - 1);
            int lastLine  = Mathf.Min(lines.Length - 1,
                            Mathf.CeilToInt((_detailDiffScroll.y + scrollRect.height) / lineH) + 1);

            for (int i = firstLine; i <= lastLine; i++)
            {
                string line     = lines[i];
                var    lineRect = new Rect(0, i * lineH, contentW, lineH);

                Color bg = Color.clear;
                if      (line.StartsWith("@@"))                              bg = GitUIStyles.DiffHunkBg;
                else if (line.StartsWith("+") && !line.StartsWith("+++"))   bg = GitUIStyles.DiffAddBg;
                else if (line.StartsWith("-") && !line.StartsWith("---"))   bg = GitUIStyles.DiffRemoveBg;

                if (bg != Color.clear) EditorGUI.DrawRect(lineRect, bg);

                GUI.Label(new Rect(0,  i * lineH, 36, lineH), (i + 1).ToString(), gutterStyle);
                GUI.Label(new Rect(40, i * lineH, contentW - 44, lineH), line, monoStyle);
            }

            GUI.EndScrollView();
        }


        private void DrawChangesPanel()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Working Tree", EditorStyles.boldLabel, GUILayout.Height(18));

            if (_statusEntries.Count > 0)
            {
                var countRect = GUILayoutUtility.GetRect(46, 16, GUILayout.Width(46));
                countRect.y += 2;
                GitUIStyles.DrawRoundedBadge(countRect, $"{_statusEntries.Count}", GitUIStyles.AccentYellow);
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("R", EditorStyles.toolbarButton, GUILayout.Width(22)))
            {
                _statusEntries        = GitOperations.GetStatus();
                _changesTree          = GitFileTreeView.Build(_statusEntries);
                _selectedChangePath   = null;
                _inlineDiffText       = null;
            }
            if (GUILayout.Button("Back", EditorStyles.toolbarButton, GUILayout.Width(56)))
                _showChanges = false;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));

            EditorGUILayout.BeginVertical(
                _selectedChangePath != null
                    ? (GUILayoutOption[])new[] { GUILayout.Width(_changesTreeW), GUILayout.ExpandHeight(true) }
                    : (GUILayoutOption[])new[] { GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true) });

            _changesScroll = EditorGUILayout.BeginScrollView(_changesScroll, GUILayout.ExpandHeight(true));

            if (_statusEntries.Count == 0)
            {
                GUILayout.Space(60);
                GUILayout.Label("Working tree clean - no uncommitted changes.", GitUIStyles.CenteredGreyMini);
            }
            else if (_changesTree != null)
            {
                GitFileTreeView.DrawLayout(
                    _changesTree,
                    null,
                    onDiff: null,
                    onSecondary: (path, status) =>
                    {
                        if (status != "??")
                        {
                            if (EditorUtility.DisplayDialog("Discard",
                                $"Discard local changes to '{path}'?\nThis cannot be undone.",
                                "Discard", "Cancel"))
                            {
                                GitOperations.DiscardLocalChanges(path);
                                AssetDatabase.Refresh();
                                _statusEntries      = GitOperations.GetStatus();
                                _changesTree        = GitFileTreeView.Build(_statusEntries);
                                _selectedChangePath = null;
                                _inlineDiffText     = null;
                            }
                        }
                    },
                    secondaryLabel: "Discard",
                    onPing: (path, _) => GitFileTreeView.PingAsset(path),
                    onRowClick: (path, status) =>
                    {
                        _selectedChangePath   = path;
                        _selectedChangeStatus = status;
                        _inlineDiffText       = GitOperations.GetDiffForWorkingCopy(path);
                        _inlineDiffScroll     = Vector2.zero;
                        Repaint();
                    }
                );
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            if (_selectedChangePath != null)
            {
                var hSep = GUILayoutUtility.GetRect(5, 5, GUILayout.Width(5), GUILayout.ExpandHeight(true));
                DrawVerticalSplitter(hSep, ref _draggingChangesTree, ref _changesTreeW, 120f, 560f);

                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                DrawInlineDiffPanel();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndHorizontal();
        }


        private void DrawInlineDiffPanel()
        {
            var headerRect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, GitUIStyles.HeaderBg);
            string displayName = System.IO.Path.GetFileName(_selectedChangePath);
            GUI.Label(new Rect(headerRect.x + 8, headerRect.y + 6, headerRect.width - 80, 18),
                      displayName, EditorStyles.boldLabel);

            if (GUI.Button(new Rect(headerRect.xMax - 70, headerRect.y + 5, 40, 18),
                           "Ping", EditorStyles.miniButton))
                GitFileTreeView.PingAsset(_selectedChangePath);

            if (GUI.Button(new Rect(headerRect.xMax - 26, headerRect.y + 5, 20, 18),
                           "X", EditorStyles.miniLabel))
            {
                _selectedChangePath = null;
                _inlineDiffText     = null;
                Repaint();
                return;
            }

            var scrollRect = GUILayoutUtility.GetRect(0, 0, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(scrollRect, GitUIStyles.CardBg);

            if (string.IsNullOrEmpty(_inlineDiffText))
            {
                var noMsg = new GUIStyle(GitUIStyles.CenteredGreyMini);
                GUI.Label(new Rect(scrollRect.x, scrollRect.y + scrollRect.height * 0.4f, scrollRect.width, 20),
                          "No diff available (file may be binary, untracked, or identical).", noMsg);
                return;
            }

            var monoStyle = new GUIStyle(GitUIStyles.MonoLabel) { fontSize = 11, wordWrap = false };
            var gutterStyle = new GUIStyle(GitUIStyles.MutedLabel)
                { alignment = TextAnchor.MiddleRight, fontSize = 10 };

            string[] lines = _inlineDiffText.Split('\n');
            float lineH = 16f;
            float contentH = lines.Length * lineH;
            float contentW = scrollRect.width - 16;

            _inlineDiffScroll = GUI.BeginScrollView(scrollRect, _inlineDiffScroll,
                new Rect(0, 0, contentW, contentH));

            int firstLine = Mathf.Max(0, Mathf.FloorToInt(_inlineDiffScroll.y / lineH) - 1);
            int lastLine  = Mathf.Min(lines.Length - 1,
                            Mathf.CeilToInt((_inlineDiffScroll.y + scrollRect.height) / lineH) + 1);

            for (int i = firstLine; i <= lastLine; i++)
            {
                string line = lines[i];
                var lineRect = new Rect(0, i * lineH, contentW, lineH);

                Color bg = Color.clear;
                if (line.StartsWith("@@"))          bg = GitUIStyles.DiffHunkBg;
                else if (line.StartsWith("+") && !line.StartsWith("+++")) bg = GitUIStyles.DiffAddBg;
                else if (line.StartsWith("-") && !line.StartsWith("---")) bg = GitUIStyles.DiffRemoveBg;

                if (bg != Color.clear)
                    EditorGUI.DrawRect(lineRect, bg);

                GUI.Label(new Rect(0, i * lineH, 36, lineH), (i + 1).ToString(), gutterStyle);

                GUI.Label(new Rect(40, i * lineH, contentW - 44, lineH), line, monoStyle);
            }

            GUI.EndScrollView();
        }


        private void DrawNoGit()
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label("Git is not installed or not on PATH.", GitUIStyles.CenteredGreyMini);
            GUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Retry", GUILayout.Width(100), GUILayout.Height(28))) CheckGitState();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
        }

        private void DrawNoRepo()
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label("No Git repository detected.", GitUIStyles.CenteredGreyMini);
            GUILayout.Space(6);
            GUILayout.Label("If the project already has Git, click Retry.", GitUIStyles.CenteredGreyMini);
            GUILayout.Space(14);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Retry Detection", GUILayout.Width(160), GUILayout.Height(28)))
            {
                GitOperations.InvalidateRepoRoot();
                CheckGitState();
                if (_isRepo) RefreshAll();
            }
            GUILayout.Space(8);
            if (GUILayout.Button("Open Configuration", GUILayout.Width(160), GUILayout.Height(28)))
            {
                _showConfiguration = true;
                _showChanges = false;
                RefreshConfigState();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
        }


        private void CheckGitState()
        {
            _gitAvailable = GitOperations.IsGitInstalled();
            _isRepo       = _gitAvailable && GitOperations.IsInsideRepo();
        }

        private void RefreshAll()
        {
            _currentBranch  = GitOperations.GetCurrentBranch();
            _branches       = GitOperations.GetBranches(true);
            _statusEntries  = GitOperations.GetStatus();

            var commits    = GitOperations.GetLogAll(400);
            _graphRows     = GitGraphBuilder.Build(commits);
            _maxLaneCount  = _graphRows.Count > 0 ? _graphRows.Max(r => r.MaxLane) : 1;

            _changesTree = GitFileTreeView.Build(_statusEntries);

            _selectedRowIdx = -1;
            _detailDiffPath = null;
            _detailDiffText = null;
            _selectedFiles.Clear();
            _detailTree     = null;
            _filteredCache  = null;
            _lastFilterStr  = "";

            Repaint();
        }


        private List<GraphRow> GetFilteredRows()
        {
            if (string.IsNullOrEmpty(_searchFilter))
                return _graphRows;

            if (_filteredCache != null && _searchFilter == _lastFilterStr)
                return _filteredCache;

            _lastFilterStr = _searchFilter;
            string lower = _searchFilter.ToLowerInvariant();
            _filteredCache = _graphRows.Where(r =>
                r.Commit.Message.ToLowerInvariant().Contains(lower)   ||
                r.Commit.Author.ToLowerInvariant().Contains(lower)    ||
                r.Commit.ShortHash.ToLowerInvariant().Contains(lower) ||
                r.Commit.Refs.Any(rf => rf.ToLowerInvariant().Contains(lower))
            ).ToList();

            return _filteredCache;
        }


        private static void DrawVLineAA(float x, float y0, float y1, Color color)
        {
            Handles.color = color;
            Handles.DrawAAPolyLine(LINE_W,
                new Vector3(x, y0, 0f),
                new Vector3(x, y1, 0f));
        }

        private static void DrawBezierLine(float x0, float y0, float x1, float y1, Color color)
        {
            if (Mathf.Abs(x0 - x1) < 0.5f)
            {
                DrawVLineAA(x0, y0, y1, color);
                return;
            }
            float midY = (y0 + y1) * 0.5f;
            Vector3 p0 = new Vector3(x0, y0, 0f);
            Vector3 p1 = new Vector3(x0, midY, 0f);
            Vector3 p2 = new Vector3(x1, midY, 0f);
            Vector3 p3 = new Vector3(x1, y1, 0f);
            Handles.DrawBezier(p0, p3, p1, p2, color, null, LINE_W);
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "...";
        }


        private void DrawVerticalSplitter(Rect r, ref bool dragging, ref float value, float min, float max)
        {
            bool hot = dragging || r.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(r, hot
                ? new Color(GitUIStyles.SeparatorColor.r + 0.08f,
                             GitUIStyles.SeparatorColor.g + 0.08f,
                             GitUIStyles.SeparatorColor.b + 0.08f, 1f)
                : GitUIStyles.SeparatorColor);

            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeHorizontal);

            var e = Event.current;
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
            { dragging = true; e.Use(); }

            if (dragging)
            {
                if (e.type == EventType.MouseDrag)
                { value = Mathf.Clamp(value + e.delta.x, min, max); Repaint(); e.Use(); }
                if (e.type == EventType.MouseUp)
                { dragging = false; e.Use(); }
            }
        }

        private void DrawHorizontalSplitter(Rect r, ref bool dragging, ref float value,
                                             float min, float max, bool invert = false)
        {
            bool hot = dragging || r.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(r, hot
                ? new Color(GitUIStyles.SeparatorColor.r + 0.08f,
                             GitUIStyles.SeparatorColor.g + 0.08f,
                             GitUIStyles.SeparatorColor.b + 0.08f, 1f)
                : GitUIStyles.SeparatorColor);

            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeVertical);

            var e = Event.current;
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
            { dragging = true; e.Use(); }

            if (dragging)
            {
                if (e.type == EventType.MouseDrag)
                {
                    float delta = invert ? -e.delta.y : e.delta.y;
                    value = Mathf.Clamp(value + delta, min, max);
                    Repaint();
                    e.Use();
                }
                if (e.type == EventType.MouseUp)
                { dragging = false; e.Use(); }
            }
        }


        private void RefreshConfigState()
        {
            _configGitInstalled = GitOperations.IsGitInstalled();
            if (!_configGitInstalled) return;

            _configGitVersion = GitOperations.GetGitVersion();
            _configIsRepo = GitOperations.IsInsideRepo();
            if (!_configIsRepo) return;

            _configRepoRoot = GitOperations.RepoRoot;
            _configCurrentBranch = GitOperations.GetCurrentBranch();
            _configUserConfig = GitOperations.GetUserConfig();
            _configRemotes = GitOperations.GetRemotes();
            _configHasGitIgnore = GitOperations.HasGitIgnore();
            _configLfsInstalled = GitOperations.IsLfsInstalled();

            _configEditName = _configUserConfig?.UserName ?? "";
            _configEditEmail = _configUserConfig?.UserEmail ?? "";
            _configEditRemoteUrl = _configRemotes.Count > 0 ? _configRemotes[0].FetchUrl : "";
            _configEditMode = false;
        }

        private void DrawConfigurationPanel()
        {
            _configScroll = EditorGUILayout.BeginScrollView(_configScroll, GUILayout.ExpandHeight(true));
            GUILayout.Space(10);

            if (!_configGitInstalled)
            {
                EditorGUILayout.HelpBox("Git is not installed or not found in PATH.\nPlease install Git and restart Unity.", MessageType.Error);
                GUILayout.Space(8);
                if (GUILayout.Button("Retry Detection", GUILayout.Height(28)))
                    RefreshConfigState();
                EditorGUILayout.EndScrollView();
                return;
            }

            if (!_configIsRepo)
            {
                EditorGUILayout.HelpBox("Current directory is not a Git repository.\nInit a new repo or open a Git project.", MessageType.Warning);
                GUILayout.Space(8);
                if (GUILayout.Button("Initialize Git Repo", GUILayout.Height(28)))
                {
                    GitOperations.InitRepo();
                    RefreshConfigState();
                }
                EditorGUILayout.EndScrollView();
                return;
            }

            GUILayout.Space(10);
            GitUIStyles.BeginCard();
            GUILayout.Label("Repository", GitUIStyles.SectionLabel);
            DrawConfigReadOnlyField("Root", _configRepoRoot);
            DrawConfigReadOnlyField("Current Branch", _configCurrentBranch);
            DrawConfigReadOnlyField("Git Version", _configGitVersion);
            GitUIStyles.EndCard();

            GUILayout.Space(10);
            GitUIStyles.BeginCard();
            GUILayout.Label("Git Identity", GitUIStyles.SectionLabel);
            if (!_configEditMode)
            {
                DrawConfigReadOnlyField("Name", _configUserConfig?.UserName ?? "(not set)");
                DrawConfigReadOnlyField("Email", _configUserConfig?.UserEmail ?? "(not set)");
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Name:", GitUIStyles.MutedLabel, GUILayout.Width(90));
                _configEditName = EditorGUILayout.TextField(_configEditName, GUILayout.Height(18));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Email:", GitUIStyles.MutedLabel, GUILayout.Width(90));
                _configEditEmail = EditorGUILayout.TextField(_configEditEmail, GUILayout.Height(18));
                EditorGUILayout.EndHorizontal();
            }
            GitUIStyles.EndCard();

            GUILayout.Space(10);
            GitUIStyles.BeginCard();
            GUILayout.Label("Remotes", GitUIStyles.SectionLabel);
            if (_configRemotes.Count == 0)
            {
                GUILayout.Label("No remote(s) configured.", GitUIStyles.MutedLabel);
            }
            else
            {
                foreach (var remote in _configRemotes)
                {
                    DrawConfigReadOnlyField(remote.Name, remote.FetchUrl);
                }
            }

            if (!_configEditMode && _configRemotes.Exists(r => r.Name == "origin"))
            {
            }
            else if (_configEditMode && _configRemotes.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Origin URL:", GitUIStyles.MutedLabel, GUILayout.Width(90));
                _configEditRemoteUrl = EditorGUILayout.TextField(_configEditRemoteUrl, GUILayout.Height(18));
                EditorGUILayout.EndHorizontal();
            }
            GitUIStyles.EndCard();

            GUILayout.Space(10);
            GitUIStyles.BeginCard();
            GUILayout.Label("Project Settings", GitUIStyles.SectionLabel);
            GUILayout.Space(2);
            DrawConfigReadOnlyField(".gitignore", _configHasGitIgnore ? "Present" : "Missing");
            DrawConfigReadOnlyField("Git LFS", _configLfsInstalled ? "Installed" : "Not installed");

            if (!_configHasGitIgnore)
            {
                GUILayout.Space(4);
                if (GUILayout.Button("Create Unity .gitignore", GUILayout.Height(24)))
                {
                    GitOperations.CreateUnityGitIgnore();
                    _configHasGitIgnore = true;
                }
            }
            if (!_configLfsInstalled)
            {
                GUILayout.Space(4);
                if (GUILayout.Button("Initialize LFS", GUILayout.Height(24)))
                {
                    GitOperations.InitLfs();
                    _configLfsInstalled = GitOperations.IsLfsInstalled();
                }
            }
            GitUIStyles.EndCard();

            GUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();

            if (!_configEditMode)
            {
                if (GUILayout.Button("Edit Configuration", GUILayout.Height(28)))
                    _configEditMode = true;
            }
            else
            {
                if (GUILayout.Button("Save Changes", GUILayout.Height(28)))
                {
                    if (!string.IsNullOrWhiteSpace(_configEditName) && !string.IsNullOrWhiteSpace(_configEditEmail))
                        GitOperations.SetUserConfig(_configEditName, _configEditEmail);

                    if (!string.IsNullOrEmpty(_configEditRemoteUrl))
                    {
                        if (_configRemotes.Exists(r => r.Name == "origin"))
                            GitOperations.SetRemoteUrl("origin", _configEditRemoteUrl);
                        else
                            GitOperations.AddRemote("origin", _configEditRemoteUrl);
                    }

                    RefreshConfigState();
                }
                if (GUILayout.Button("Cancel", GUILayout.Height(28)))
                {
                    _configEditMode = false;
                    RefreshConfigState();
                }
            }

            if (GUILayout.Button("Refresh", GUILayout.Height(28), GUILayout.Width(80)))
                RefreshConfigState();

            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);

            EditorGUILayout.EndScrollView();
        }

        private void DrawConfigReadOnlyField(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", GitUIStyles.MutedLabel, GUILayout.Width(90));
            EditorGUILayout.SelectableLabel(value, EditorStyles.label, GUILayout.Height(18));
            EditorGUILayout.EndHorizontal();
        }
    }
}
