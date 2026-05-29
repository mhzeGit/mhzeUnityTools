using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

namespace HierarchyNavigator
{
    [InitializeOnLoad]
    public static class ProjectNavigatorTool
    {
        private static bool         _inMoveMode;
        private static string[]     _assetPaths;        // selected assets to move
        private static string       _originalFolder;    // folder the assets started in
        private static string       _targetFolder;      // currently highlighted folder

        private static string       _browseParent;
        private static List<string> _browseSiblings = new List<string>();
        private static int          _browseIndex;

        private static HashSet<string> _excludeRoots = new HashSet<string>();

        private static readonly Color HighlightFill   = new Color(0.1f, 0.75f, 0.25f, 0.22f);
        private static readonly Color HighlightBorder = new Color(0.1f, 0.85f, 0.3f, 0.7f);
        private static GUIStyle _targetLabelStyle;

        static ProjectNavigatorTool()
        {
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
            Selection.selectionChanged += OnSelectionChanged;
        }

        private static void OnProjectWindowItemGUI(string guid, Rect rect)
        {
            if (_inMoveMode && !string.IsNullOrEmpty(_targetFolder))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == _targetFolder)
                {
                    EditorGUI.DrawRect(rect, HighlightFill);
                    DrawRectOutline(rect, HighlightBorder);

                    if (_targetLabelStyle == null)
                    {
                        _targetLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                        {
                            normal    = { textColor = new Color(0.1f, 0.9f, 0.3f) },
                            fontStyle = FontStyle.Bold,
                            alignment = TextAnchor.MiddleRight
                        };
                    }
                    GUI.Label(rect, "<- TARGET  ", _targetLabelStyle);
                }
            }

            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;

            if (_inMoveMode)
                HandleMoveModeInput(e);
            else
                HandleIdleInput(e);
        }

        private static void HandleIdleInput(Event e)
        {
            if (!e.control || !e.shift) return;
            if (e.keyCode != KeyCode.UpArrow && e.keyCode != KeyCode.DownArrow) return;

            string[] guids = Selection.assetGUIDs;
            if (guids == null || guids.Length == 0) return;

            List<string> paths = new List<string>();
            foreach (string g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (!string.IsNullOrEmpty(p) && p != "Assets")
                    paths.Add(p);
            }
            if (paths.Count == 0) return;

            EnterMoveMode(paths.ToArray(), e.keyCode == KeyCode.UpArrow ? -1 : 1);
            e.Use();
        }

        private static void HandleMoveModeInput(Event e)
        {
            if (e.control && e.shift)
            {
                switch (e.keyCode)
                {
                    case KeyCode.UpArrow:
                        NavigateSibling(-1);
                        e.Use();
                        return;
                    case KeyCode.DownArrow:
                        NavigateSibling(1);
                        e.Use();
                        return;
                    case KeyCode.RightArrow:
                        NavigateInto();
                        e.Use();
                        return;
                    case KeyCode.LeftArrow:
                        NavigateOut();
                        e.Use();
                        return;
                }
            }

            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                ConfirmMove();
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.Escape)
            {
                CancelMoveMode("Move cancelled.");
                e.Use();
            }
        }


        private static void EnterMoveMode(string[] assetPaths, int initialDirection)
        {
            _assetPaths     = assetPaths;
            _originalFolder = GetParentFolder(assetPaths[0]);

            _excludeRoots.Clear();
            foreach (string p in assetPaths)
                if (AssetDatabase.IsValidFolder(p))
                    _excludeRoots.Add(p);

            string containingFolder = _originalFolder ?? "Assets";

            if (!SetBrowseLevel(containingFolder, null))
            {
                string parentOfContaining = GetParentFolder(containingFolder);
                if (parentOfContaining == null || !SetBrowseLevel(parentOfContaining, containingFolder))
                {
                    if (!SetBrowseLevel(null, "Assets"))
                    {
                        Debug.LogWarning("[Project Navigator] No valid target folders found.");
                        return;
                    }
                }
            }

            _inMoveMode = true;

            NavigateSibling(initialDirection);

            UpdateTargetAndNotify("Move Mode (Up/Down siblings, Right into, Left out, Enter confirm, Esc cancel)");
        }

        private static bool SetBrowseLevel(string parent, string selectFolder)
        {
            _browseParent = parent;
            _browseSiblings = GetChildFolders(parent);

            if (_browseSiblings.Count == 0) return false;

            _browseIndex = _browseSiblings.IndexOf(selectFolder);
            if (_browseIndex < 0) _browseIndex = 0;

            _targetFolder = _browseSiblings[_browseIndex];
            return true;
        }


        private static void NavigateSibling(int direction)
        {
            if (_browseSiblings.Count == 0) return;

            int newIndex = _browseIndex + direction;
            newIndex = Mathf.Clamp(newIndex, 0, _browseSiblings.Count - 1);

            if (newIndex != _browseIndex)
            {
                _browseIndex  = newIndex;
                _targetFolder = _browseSiblings[_browseIndex];
            }

            UpdateTargetAndNotify(null);
        }

        private static void NavigateInto()
        {
            if (string.IsNullOrEmpty(_targetFolder)) return;

            List<string> children = GetChildFolders(_targetFolder);
            if (children.Count == 0)
            {
                ShowNotification($"'{Path.GetFileName(_targetFolder)}' has no sub-folders");
                return;
            }

            _browseParent   = _targetFolder;
            _browseSiblings = children;
            _browseIndex    = 0;
            _targetFolder   = _browseSiblings[0];

            UpdateTargetAndNotify(null);
        }

        private static void NavigateOut()
        {
            if (string.IsNullOrEmpty(_browseParent)) return; // already at root

            string currentLevel = _browseParent;
            string grandParent  = GetParentFolder(_browseParent);

            List<string> parentSiblings = GetChildFolders(grandParent);
            if (parentSiblings.Count == 0) return;

            _browseParent   = grandParent;
            _browseSiblings = parentSiblings;
            _browseIndex    = parentSiblings.IndexOf(currentLevel);
            if (_browseIndex < 0) _browseIndex = 0;
            _targetFolder   = _browseSiblings[_browseIndex];

            UpdateTargetAndNotify(null);
        }


        private static void ConfirmMove()
        {
            if (!_inMoveMode || _assetPaths == null || string.IsNullOrEmpty(_targetFolder))
            {
                CancelMoveMode("Nothing to move.");
                return;
            }

            if (_targetFolder == _originalFolder)
            {
                CancelMoveMode("Target is the same as origin - cancelled.");
                return;
            }

            List<string> movedPaths = new List<string>();

            foreach (string assetPath in _assetPaths)
            {
                string fileName = Path.GetFileName(assetPath);
                string newPath  = _targetFolder + "/" + fileName;

                if (newPath == assetPath) continue;

                if (AssetDatabase.IsValidFolder(assetPath) &&
                    (_targetFolder == assetPath || _targetFolder.StartsWith(assetPath + "/")))
                {
                    Debug.LogWarning($"[Project Navigator] Cannot move '{assetPath}' into itself.");
                    continue;
                }

                newPath = AssetDatabase.GenerateUniqueAssetPath(newPath);

                string error = AssetDatabase.MoveAsset(assetPath, newPath);
                if (string.IsNullOrEmpty(error))
                {
                    movedPaths.Add(newPath);
                }
                else
                {
                    Debug.LogWarning($"[Project Navigator] Failed to move '{assetPath}' -> '{newPath}': {error}");
                }
            }

            if (movedPaths.Count > 0)
            {
                AssetDatabase.Refresh();
                ReselectAssets(movedPaths);
                ShowNotification($"Moved {movedPaths.Count} asset(s) -> {_targetFolder}");
            }
            else
            {
                ShowNotification("No assets were moved.");
            }

            ExitMoveMode();
        }

        private static void CancelMoveMode(string message)
        {
            ShowNotification(message);
            ExitMoveMode();
        }

        private static void ExitMoveMode()
        {
            _inMoveMode = false;
            _targetFolder = null;
            _assetPaths = null;
            _browseParent = null;
            _browseSiblings.Clear();
            _browseIndex = -1;
            _excludeRoots.Clear();
            EditorApplication.RepaintProjectWindow();
        }

        private static void OnSelectionChanged()
        {
            if (_inMoveMode)
                ExitMoveMode();
        }


        private static List<string> GetChildFolders(string parent)
        {
            List<string> result = new List<string>();

            if (parent == null)
            {
                if (!_excludeRoots.Contains("Assets"))
                    result.Add("Assets");
                return result;
            }

            string[] subs = AssetDatabase.GetSubFolders(parent);
            if (subs == null) return result;

            foreach (string s in subs)
            {
                bool excluded = false;
                foreach (string ex in _excludeRoots)
                {
                    if (s == ex || s.StartsWith(ex + "/"))
                    {
                        excluded = true;
                        break;
                    }
                }
                if (!excluded)
                    result.Add(s);
            }

            return result;
        }


        private static string GetParentFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(parent)) return null;
            return parent;
        }

        private static void UpdateTargetAndNotify(string extraMessage)
        {
            PingTargetFolder();
            string folderName = Path.GetFileName(_targetFolder);
            string msg = $"Target: {_targetFolder}";
            if (!string.IsNullOrEmpty(extraMessage))
                msg = extraMessage + "\n" + msg;
            ShowNotification(msg);
            EditorApplication.RepaintProjectWindow();
        }

        private static void ReselectAssets(List<string> paths)
        {
            if (paths.Count == 0) return;
            List<Object> objects = new List<Object>();
            foreach (string p in paths)
            {
                Object obj = AssetDatabase.LoadAssetAtPath<Object>(p);
                if (obj != null)
                    objects.Add(obj);
            }
            if (objects.Count > 0)
                Selection.objects = objects.ToArray();
        }

        private static void PingTargetFolder()
        {
            if (string.IsNullOrEmpty(_targetFolder)) return;
            Object folderObj = AssetDatabase.LoadAssetAtPath<Object>(_targetFolder);
            if (folderObj != null)
                EditorGUIUtility.PingObject(folderObj);
        }

        private static void ShowNotification(string message)
        {
            try
            {
                var projectBrowserType = typeof(Editor).Assembly.GetType("UnityEditor.ProjectBrowser");
                if (projectBrowserType != null)
                {
                    var window = EditorWindow.GetWindow(projectBrowserType, false, null, false);
                    if (window != null)
                        window.ShowNotification(new GUIContent(message), 2.5f);
                }
            }
            catch
            {
                Debug.Log($"[Project Navigator] {message}");
            }
        }

        private static void DrawRectOutline(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
        }
    }
}
