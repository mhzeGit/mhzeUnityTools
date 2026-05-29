using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GitIntegration
{
    public class GitAssetHistoryWindow : EditorWindow
    {
        private string _assetPath;
        private string _absolutePath;
        private List<GitCommitInfo> _commits = new List<GitCommitInfo>();
        private int    _selectedIdx  = -1;
        private string _diffText     = "";
        private string _searchFilter = "";

        private Vector2 _listScroll;
        private Vector2 _diffScroll;

        private float _listW        = 0f;   // initialised on first draw
        private float _detailSplitH = 90f;  // height of the slim meta strip
        private bool  _draggingListW       = false;
        private bool  _draggingDetailSplit = false;

        private GUIStyle _msgStyle;
        private GUIStyle _metaStyle;
        private GUIStyle _hashChipStyle;
        private GUIStyle _monoStyle;
        private GUIStyle _gutterStyle;


        public static void ShowForAsset(string assetPath)
        {
            var win = GetWindow<GitAssetHistoryWindow>(true, "Asset History");
            win.minSize        = new Vector2(720, 440);
            win._assetPath     = assetPath;
            win._absolutePath  = Application.dataPath.Replace("/Assets", "") + "/" + assetPath;
            win._selectedIdx   = -1;
            win._diffText      = "";
            win.wantsMouseMove = true;
            win.LoadHistory();
            win.Show();
        }

        private void LoadHistory()
        {
            _commits     = GitOperations.GetLog(200, _absolutePath);
            _selectedIdx = -1;
            _diffText    = "";
        }


        private void EnsureStyles()
        {
            if (_msgStyle != null) return;

            _msgStyle = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Bold,
                clipping  = TextClipping.Clip,
                wordWrap  = false,
            };

            _metaStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                padding = new RectOffset(0, 0, 0, 0),
            };
            _metaStyle.normal.textColor = GitUIStyles.MutedText;

            _hashChipStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                padding   = new RectOffset(2, 2, 0, 0),
            };
            _hashChipStyle.normal.textColor = GitUIStyles.MutedText;

            _monoStyle = new GUIStyle(GitUIStyles.MonoLabel) { fontSize = 11, wordWrap = false };

            _gutterStyle = new GUIStyle(GitUIStyles.MutedLabel)
            {
                alignment = TextAnchor.MiddleRight,
                fontSize  = 10,
            };
        }


        private void OnGUI()
        {
            EnsureStyles();

            if (Event.current.type == EventType.MouseMove) Repaint();

            if (_listW < 10f) _listW = position.width * 0.36f;

            DrawToolbar();

            float usedH  = 22f;
            float bodyH  = position.height - usedH;
            var bodyRect = new Rect(0, usedH, position.width, bodyH);

            const float splW = 5f;
            var listRect  = new Rect(bodyRect.x, bodyRect.y, _listW, bodyRect.height);
            var splRect   = new Rect(bodyRect.x + _listW, bodyRect.y, splW, bodyRect.height);
            var rightRect = new Rect(bodyRect.x + _listW + splW, bodyRect.y,
                                     bodyRect.width - _listW - splW, bodyRect.height);

            DrawCommitList(listRect);
            DrawVerticalSplitter(splRect, ref _draggingListW, ref _listW,
                140f, position.width - 280f);
            DrawRightPanel(rightRect);
        }


        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            Texture icon = GitFileTreeView.GetAssetIcon(_assetPath);
            if (icon != null)
            {
                var ir = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16));
                ir.y += 3;
                GUI.DrawTexture(ir, icon, ScaleMode.ScaleToFit);
                GUILayout.Space(3);
            }

            string fileName  = System.IO.Path.GetFileName(_assetPath);
            string dirPart   = System.IO.Path.GetDirectoryName(_assetPath)?.Replace("\\", "/") ?? "";
            string pathLabel = string.IsNullOrEmpty(dirPart)
                ? $"<b>{fileName}</b>"
                : $"<b>{fileName}</b>  <color=#{ColorUtility.ToHtmlStringRGB(GitUIStyles.MutedText)}>{dirPart}</color>";

            var richStyle = new GUIStyle(EditorStyles.label) { richText = true };
            GUILayout.Label(pathLabel, richStyle, GUILayout.MaxWidth(400));

            GUILayout.FlexibleSpace();

            GUILayout.Label("Search:", EditorStyles.toolbarButton, GUILayout.Width(50));
            _searchFilter = EditorGUILayout.TextField(_searchFilter,
                EditorStyles.toolbarSearchField, GUILayout.Width(160));
            if (GUILayout.Button("x", EditorStyles.toolbarButton, GUILayout.Width(18)))
                _searchFilter = "";

            GUILayout.Space(6);

            var badgeR = GUILayoutUtility.GetRect(72, 16, GUILayout.Width(72));
            badgeR.y += 3;
            GitUIStyles.DrawRoundedBadge(badgeR,
                $"{FilteredCommits().Count} / {_commits.Count}", GitUIStyles.AccentBlue);

            GUILayout.Space(4);
            if (GUILayout.Button("R", EditorStyles.toolbarButton, GUILayout.Width(22)))
                LoadHistory();

            EditorGUILayout.EndHorizontal();
        }


        private void DrawCommitList(Rect area)
        {
            GUI.BeginGroup(area);

            var filtered   = FilteredCommits();
            const float rowH = 42f;
            float contentH   = filtered.Count * rowH;

            _listScroll = GUI.BeginScrollView(
                new Rect(0, 0, area.width, area.height),
                _listScroll,
                new Rect(0, 0, area.width, contentH));

            for (int i = 0; i < filtered.Count; i++)
            {
                bool selected = _selectedIdx == i;
                var  commit   = filtered[i];
                var  rect     = new Rect(0, i * rowH, area.width, rowH);
                bool hovered  = rect.Contains(Event.current.mousePosition);

                if (selected)
                    EditorGUI.DrawRect(rect, GitUIStyles.SelectedRowBg);
                else if (hovered && Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(rect, GitUIStyles.HoverRowBg);

                if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                {
                    _selectedIdx = i;
                    _diffText    = GitOperations.GetDiff(commit.Hash, _absolutePath);
                    _diffScroll  = Vector2.zero;
                    Repaint();
                }

                if (selected)
                    EditorGUI.DrawRect(new Rect(0, rect.y + 5, 3, rect.height - 10), GitUIStyles.AccentBlue);

                GitUIStyles.DrawAuthorCircle(new Rect(8, rect.y + 10, 22, 22),
                    commit.Author, commit.AuthorEmail);

                float textX = 36f;
                float textW = area.width - textX - 68f;
                GUI.Label(new Rect(textX, rect.y + 5, textW, 18),
                    Truncate(commit.Message, 60), _msgStyle);

                GUI.Label(new Rect(area.width - 66, rect.y + 5, 62, 16),
                    commit.RelativeDate, _hashChipStyle);
                GUI.Label(new Rect(area.width - 66, rect.y + 22, 62, 14),
                    commit.ShortHash, _hashChipStyle);

                if (commit.Refs != null && commit.Refs.Count > 0)
                {
                    string firstRef = commit.Refs[0];
                    if (firstRef.StartsWith("HEAD ->"))
                        firstRef = firstRef.Substring(7).Trim();
                    else if (firstRef.StartsWith("tag:"))
                        firstRef = "tag: " + firstRef.Substring(4).Trim();

                    float refW = Mathf.Min(textW * 0.55f, 120f);
                    GitUIStyles.DrawRefPill(new Rect(textX, rect.y + 23, refW, 13),
                        Truncate(firstRef, 18), GitUIStyles.AccentGreen);
                }

                EditorGUI.DrawRect(new Rect(0, rect.yMax - 1, area.width, 1),
                    new Color(GitUIStyles.SeparatorColor.r,
                              GitUIStyles.SeparatorColor.g,
                              GitUIStyles.SeparatorColor.b, 0.3f));
            }

            if (filtered.Count == 0)
                GUI.Label(new Rect(8, 50, area.width - 16, 30),
                    string.IsNullOrEmpty(_searchFilter)
                        ? "No history found for this asset."
                        : "No commits match the search.",
                    GitUIStyles.CenteredGreyMini);

            GUI.EndScrollView();
            GUI.EndGroup();
        }


        private void DrawRightPanel(Rect area)
        {
            var filtered = FilteredCommits();
            if (_selectedIdx < 0 || _selectedIdx >= filtered.Count)
            {
                GUI.Label(area, "Select a commit to view details", GitUIStyles.CenteredGreyMini);
                return;
            }

            var c = filtered[_selectedIdx];
            _detailSplitH = Mathf.Clamp(_detailSplitH, 72f, area.height - 80f);

            var metaRect = new Rect(area.x, area.y,          area.width, _detailSplitH);
            var splRect  = new Rect(area.x, area.y + _detailSplitH, area.width, 5f);
            var diffRect = new Rect(area.x, area.y + _detailSplitH + 5f,
                                    area.width, area.height - _detailSplitH - 5f);

            DrawMetaStrip(metaRect, c);
            DrawHorizontalSplitter(splRect, ref _draggingDetailSplit, ref _detailSplitH,
                72f, area.height - 80f);
            DrawDiffArea(diffRect, c);
        }


        private void DrawMetaStrip(Rect area, GitCommitInfo c)
        {
            EditorGUI.DrawRect(area, GitUIStyles.HeaderBg);
            GUI.BeginGroup(area);

            float w          = area.width;
            const float pad  = 8f;
            const float avatW = 28f;
            const float hashW = 76f;   // right-anchored hash chip
            const float dateW = 150f;  // right-anchored date (left of hash)
            const float gap  = 6f;


            float row1Y = 8f;
            float row1H = 18f;

            GitUIStyles.DrawAuthorCircle(new Rect(pad, row1Y, avatW, avatW), c.Author, c.AuthorEmail);

            float hashX  = w - pad - hashW;
            var hashRect = new Rect(hashX, row1Y + 1, hashW, row1H - 2);
            EditorGUI.DrawRect(hashRect, GitUIStyles.CardBg);
            if (GUI.Button(hashRect, c.ShortHash, _hashChipStyle))
            {
                EditorGUIUtility.systemCopyBuffer = c.Hash;
                ShowNotification(new GUIContent("Hash copied"));
            }
            EditorGUIUtility.AddCursorRect(hashRect, MouseCursor.Text);

            float dateX  = hashX - gap - dateW;
            GUI.Label(new Rect(dateX, row1Y, dateW, row1H), c.Date, _metaStyle);

            float authorX = pad + avatW + gap;
            float authorW = Mathf.Max(dateX - gap - authorX, 60f);
            string authorLabel = authorW > 160f
                ? $"{c.Author}  <{c.AuthorEmail}>"
                : c.Author;
            GUI.Label(new Rect(authorX, row1Y, authorW, row1H), authorLabel, EditorStyles.boldLabel);


            float msgY = row1Y + Mathf.Max(row1H, avatW) + 2f;
            GUI.Label(new Rect(authorX, msgY, w - authorX - pad, 15f),
                Truncate(c.Message, 140), _metaStyle);


            float btnY = msgY + 18f;
            const float btnH = 20f;
            const float btnW = 148f;
            float bx = authorX;

            if (GUI.Button(new Rect(bx, btnY, btnW, btnH), "Open in Diff Viewer", EditorStyles.miniButton))
                GitDiffViewerWindow.ShowDiff(c.Hash, _assetPath, c.ShortHash);
            bx += btnW + gap;

            if (GUI.Button(new Rect(bx, btnY, btnW, btnH), "Restore to This Version", EditorStyles.miniButton))
            {
                if (EditorUtility.DisplayDialog("Restore Asset",
                    $"Restore '{System.IO.Path.GetFileName(_assetPath)}' to commit {c.ShortHash}?\n\nLocal only - not auto-committed.",
                    "Restore", "Cancel"))
                {
                    if (GitOperations.RestoreFileFromCommit(c.Hash, _absolutePath))
                    {
                        AssetDatabase.Refresh();
                        ShowNotification(new GUIContent($"Restored to {c.ShortHash}"));
                    }
                }
            }

            EditorGUI.DrawRect(new Rect(0, area.height - 1, w, 1), GitUIStyles.SeparatorColor);

            GUI.EndGroup();
        }


        private void DrawDiffArea(Rect area, GitCommitInfo c)
        {
            if (area.height < 10f) return;

            var hdr = new Rect(area.x, area.y, area.width, 22f);
            EditorGUI.DrawRect(hdr, GitUIStyles.HeaderBg);

            string fname = System.IO.Path.GetFileName(_assetPath);
            GUI.Label(new Rect(hdr.x + 8, hdr.y + 3, hdr.width - 160, 16),
                fname, GitUIStyles.SectionLabel);

            if (!string.IsNullOrEmpty(_diffText))
            {
                int adds    = _diffText.Split('\n').Count(l => l.StartsWith("+") && !l.StartsWith("+++"));
                int removes = _diffText.Split('\n').Count(l => l.StartsWith("-") && !l.StartsWith("---"));
                var statStyle = new GUIStyle(EditorStyles.miniLabel);
                statStyle.normal.textColor = GitUIStyles.AccentGreen;
                GUI.Label(new Rect(hdr.x + 8 + hdr.width - 160, hdr.y + 4, 80, 14),
                    $"+{adds}  -{removes}", statStyle);
            }

            if (GUI.Button(new Rect(hdr.xMax - 72, hdr.y + 3, 64, 16),
                           "Reload", EditorStyles.miniButton))
            {
                _diffText   = GitOperations.GetDiff(c.Hash, _absolutePath);
                _diffScroll = Vector2.zero;
                Repaint();
            }

            var scrollArea = new Rect(area.x, area.y + 22f, area.width, area.height - 22f);
            EditorGUI.DrawRect(scrollArea, GitUIStyles.CardBg);

            if (string.IsNullOrEmpty(_diffText))
            {
                GUI.Label(new Rect(scrollArea.x, scrollArea.y + scrollArea.height * 0.4f,
                          scrollArea.width, 20),
                    "No diff available (binary file, or no changes in this commit).",
                    GitUIStyles.CenteredGreyMini);
                return;
            }

            string[] lines    = _diffText.Split('\n');
            float    lineH    = 16f;
            float    contentH = lines.Length * lineH;
            float    contentW = scrollArea.width - 16f;

            _diffScroll = GUI.BeginScrollView(scrollArea, _diffScroll,
                new Rect(0, 0, contentW, contentH));

            int first = Mathf.Max(0, Mathf.FloorToInt(_diffScroll.y / lineH) - 1);
            int last  = Mathf.Min(lines.Length - 1,
                        Mathf.CeilToInt((_diffScroll.y + scrollArea.height) / lineH) + 1);

            for (int i = first; i <= last; i++)
            {
                string line = lines[i];
                var    lr   = new Rect(0, i * lineH, contentW, lineH);

                Color bg = Color.clear;
                if      (line.StartsWith("@@"))                            bg = GitUIStyles.DiffHunkBg;
                else if (line.StartsWith("+") && !line.StartsWith("+++")) bg = GitUIStyles.DiffAddBg;
                else if (line.StartsWith("-") && !line.StartsWith("---")) bg = GitUIStyles.DiffRemoveBg;

                if (bg != Color.clear) EditorGUI.DrawRect(lr, bg);
                GUI.Label(new Rect(0,  i * lineH, 36,            lineH), (i + 1).ToString(), _gutterStyle);
                GUI.Label(new Rect(40, i * lineH, contentW - 44, lineH), line, _monoStyle);
            }

            GUI.EndScrollView();
        }


        private List<GitCommitInfo> FilteredCommits()
        {
            if (string.IsNullOrEmpty(_searchFilter)) return _commits;
            string lower = _searchFilter.ToLowerInvariant();
            return _commits.Where(c =>
                c.Message.ToLowerInvariant().Contains(lower) ||
                c.Author.ToLowerInvariant().Contains(lower)  ||
                c.ShortHash.ToLowerInvariant().Contains(lower)
            ).ToList();
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
                ? new Color(GitUIStyles.SeparatorColor.r + 0.10f,
                             GitUIStyles.SeparatorColor.g + 0.10f,
                             GitUIStyles.SeparatorColor.b + 0.10f)
                : GitUIStyles.SeparatorColor);
            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeHorizontal);
            var e = Event.current;
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition)) { dragging = true;  e.Use(); }
            if (dragging)
            {
                if (e.type == EventType.MouseDrag) { value = Mathf.Clamp(value + e.delta.x, min, max); Repaint(); e.Use(); }
                if (e.type == EventType.MouseUp)   { dragging = false; e.Use(); }
            }
        }

        private void DrawHorizontalSplitter(Rect r, ref bool dragging, ref float value, float min, float max)
        {
            bool hot = dragging || r.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(r, hot
                ? new Color(GitUIStyles.SeparatorColor.r + 0.10f,
                             GitUIStyles.SeparatorColor.g + 0.10f,
                             GitUIStyles.SeparatorColor.b + 0.10f)
                : GitUIStyles.SeparatorColor);
            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeVertical);
            var e = Event.current;
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition)) { dragging = true;  e.Use(); }
            if (dragging)
            {
                if (e.type == EventType.MouseDrag) { value = Mathf.Clamp(value + e.delta.y, min, max); Repaint(); e.Use(); }
                if (e.type == EventType.MouseUp)   { dragging = false; e.Use(); }
            }
        }
    }
}
