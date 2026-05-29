using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GitIntegration
{
    public class FileTreeNode
    {
        public string Name;
        public string FullPath;
        public bool IsFolder;
        public string Status;
        public bool Expanded = true;
        public List<FileTreeNode> Children = new List<FileTreeNode>();
    }

    public static class GitFileTreeView
    {
        private const float ROW_H  = 24f;
        private const float INDENT = 18f;
        private const float ICON_SZ = 16f;

        private static readonly HashSet<string> _collapsed = new HashSet<string>();

        private static GUIStyle _folderLabel;
        private static GUIStyle FolderLabel
        {
            get
            {
                if (_folderLabel == null)
                {
                    _folderLabel = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 11,
                        padding = new RectOffset(0, 0, 0, 0),
                    };
                }
                return _folderLabel;
            }
        }


        public static FileTreeNode Build(List<GitFileChange> files)
        {
            var root = new FileTreeNode { Name = "", IsFolder = true, FullPath = "" };
            if (files == null) return root;
            for (int i = 0; i < files.Count; i++)
                Insert(root, files[i].FilePath.Replace("\\", "/"), files[i].Status);
            Compact(root);
            Sort(root);
            return root;
        }

        public static FileTreeNode Build(List<GitStatusEntry> entries)
        {
            var root = new FileTreeNode { Name = "", IsFolder = true, FullPath = "" };
            if (entries == null) return root;
            for (int i = 0; i < entries.Count; i++)
                Insert(root, entries[i].FilePath.Replace("\\", "/"), entries[i].Status);
            Compact(root);
            Sort(root);
            return root;
        }

        private static void Insert(FileTreeNode parent, string path, string status)
        {
            var parts = path.Split('/');
            var cur = parent;
            for (int i = 0; i < parts.Length; i++)
            {
                bool last = i == parts.Length - 1;
                string fp = i == 0 ? parts[0] : string.Join("/", parts, 0, i + 1);
                FileTreeNode found = null;
                for (int c = 0; c < cur.Children.Count; c++)
                {
                    if (cur.Children[c].Name == parts[i] && cur.Children[c].IsFolder == !last)
                    { found = cur.Children[c]; break; }
                }
                if (found == null)
                {
                    found = new FileTreeNode
                    {
                        Name = parts[i],
                        FullPath = fp,
                        IsFolder = !last,
                        Status = last ? status : null,
                        Expanded = !_collapsed.Contains(fp),
                    };
                    cur.Children.Add(found);
                }
                cur = found;
            }
        }

        private static void Compact(FileTreeNode node)
        {
            for (int i = 0; i < node.Children.Count; i++)
            {
                var c = node.Children[i];
                if (!c.IsFolder) continue;
                Compact(c);
                while (c.IsFolder && c.Children.Count == 1 && c.Children[0].IsFolder)
                {
                    var gc = c.Children[0];
                    c.Name += " / " + gc.Name;
                    c.FullPath = gc.FullPath;
                    c.Children = gc.Children;
                    c.Expanded = gc.Expanded;
                }
            }
        }

        private static void Sort(FileTreeNode node)
        {
            node.Children.Sort((a, b) =>
            {
                if (a.IsFolder != b.IsFolder) return a.IsFolder ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            for (int i = 0; i < node.Children.Count; i++)
                if (node.Children[i].IsFolder) Sort(node.Children[i]);
        }


        public delegate void FileAction(string path, string status);

        public static void DrawLayout(
            FileTreeNode root, string commitHash,
            FileAction onDiff, FileAction onSecondary, string secondaryLabel,
            FileAction onPing, FileAction onRowClick = null)
        {
            if (root == null || root.Children.Count == 0)
            {
                GUILayout.Space(20);
                GUILayout.Label("No files changed.", GitUIStyles.CenteredGreyMini);
                return;
            }
            for (int i = 0; i < root.Children.Count; i++)
                DrawNode(root.Children[i], 0, commitHash, onDiff, onSecondary, secondaryLabel, onPing, onRowClick);
        }

        private static void DrawNode(
            FileTreeNode node, int depth, string commitHash,
            FileAction onDiff, FileAction onSecondary, string secLabel,
            FileAction onPing, FileAction onRowClick = null)
        {
            float indent = depth * INDENT;
            var rowRect = GUILayoutUtility.GetRect(0, ROW_H, GUILayout.ExpandWidth(true));

            bool hover = rowRect.Contains(Event.current.mousePosition);
            if (hover && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rowRect, GitUIStyles.HoverRowBg);

            float x = rowRect.x + indent + 4;

            if (node.IsFolder)
                DrawFolderNode(node, depth, rowRect, x, commitHash, onDiff, onSecondary, secLabel, onPing, onRowClick);
            else
                DrawFileNode(node, depth, rowRect, x, commitHash, onDiff, onSecondary, secLabel, onPing, onRowClick);
        }


        private static void DrawFolderNode(
            FileTreeNode node, int depth, Rect rowRect, float x,
            string commitHash, FileAction onDiff, FileAction onSecondary,
            string secLabel, FileAction onPing, FileAction onRowClick = null)
        {
            bool wasExpanded = node.Expanded;
            node.Expanded = EditorGUI.Foldout(new Rect(x, rowRect.y + 3, 14, 18), node.Expanded, GUIContent.none);
            if (node.Expanded != wasExpanded)
            {
                if (!node.Expanded) _collapsed.Add(node.FullPath);
                else _collapsed.Remove(node.FullPath);
            }
            x += 16;

            var folderIcon = CachedFolderIcon;
            if (folderIcon != null)
                GUI.DrawTexture(new Rect(x, rowRect.y + 4, ICON_SZ, ICON_SZ), folderIcon, ScaleMode.ScaleToFit);
            x += ICON_SZ + 4;

            GUI.Label(new Rect(x, rowRect.y + 3, rowRect.width - x - 60, 18), node.Name, FolderLabel);

            int fc = CountLeaves(node);
            string countTxt = fc + (fc == 1 ? " file" : " files");
            GUI.Label(new Rect(rowRect.xMax - 56, rowRect.y + 5, 52, 14), countTxt, GitUIStyles.MutedLabel);

            float indent = depth * INDENT;
            EditorGUI.DrawRect(
                new Rect(rowRect.x + indent, rowRect.yMax - 1, rowRect.width - indent, 1),
                new Color(GitUIStyles.SeparatorColor.r, GitUIStyles.SeparatorColor.g,
                          GitUIStyles.SeparatorColor.b, 0.3f));

            if (node.Expanded)
                for (int i = 0; i < node.Children.Count; i++)
                    DrawNode(node.Children[i], depth + 1, commitHash, onDiff, onSecondary, secLabel, onPing, onRowClick);
        }


        private static string _lastClickedPath = null;
        private static double _lastClickTime   = -1.0;
        private const  double DBL_CLICK_SECS   = 0.35;


        private static void DrawFileNode(
            FileTreeNode node, int depth, Rect rowRect, float x,
            string commitHash, FileAction onDiff, FileAction onSecondary,
            string secLabel, FileAction onPing, FileAction onRowClick = null)
        {
            x += 16; // align past foldout space

            if (!string.IsNullOrEmpty(node.Status))
            {
                float sz = 15f;
                GitUIStyles.DrawStatusCircle(new Rect(x, rowRect.y + 4.5f, sz, sz), node.Status);
                x += sz + 4;
            }

            Texture icon = GetAssetIcon(node.FullPath);
            if (icon != null)
                GUI.DrawTexture(new Rect(x, rowRect.y + 4, ICON_SZ, ICON_SZ), icon, ScaleMode.ScaleToFit);
            x += ICON_SZ + 4;

            bool willShowDiff = onDiff != null && !string.IsNullOrEmpty(node.Status) && node.Status != "D";
            bool willShowSec  = onSecondary != null;
            float btnAreaW = (willShowDiff ? 52f : 0f) + (willShowSec ? 66f : 0f);
            if (btnAreaW < 4f) btnAreaW = 4f;

            float nameW = Mathf.Max(rowRect.xMax - x - btnAreaW - 8, 30);
            var nameRect = new Rect(x, rowRect.y + 2, nameW, 20);

            var clickRect = new Rect(rowRect.x, rowRect.y, rowRect.width - btnAreaW - 4, rowRect.height);
            EditorGUIUtility.AddCursorRect(nameRect, MouseCursor.Link);

            if (Event.current.type == EventType.MouseDown
                && Event.current.button == 0
                && clickRect.Contains(Event.current.mousePosition))
            {
                double now = EditorApplication.timeSinceStartup;
                bool isDouble = (_lastClickedPath == node.FullPath)
                                && (now - _lastClickTime) < DBL_CLICK_SECS;

                if (isDouble)
                {
                    _lastClickedPath = null;
                    _lastClickTime   = -1.0;
                    onPing?.Invoke(node.FullPath, node.Status);
                }
                else
                {
                    _lastClickedPath = node.FullPath;
                    _lastClickTime   = now;
                    if (onRowClick != null)
                        onRowClick(node.FullPath, node.Status);
                    else
                        onPing?.Invoke(node.FullPath, node.Status);
                }
                Event.current.Use();
            }

            GUI.Label(nameRect, node.Name, EditorStyles.label);

            float bx = rowRect.xMax - btnAreaW;
            if (willShowDiff)
            {
                if (GUI.Button(new Rect(bx, rowRect.y + 4, 44, 17), "Diff", EditorStyles.miniButton))
                    onDiff(node.FullPath, node.Status);
                bx += 48;
            }
            if (willShowSec)
            {
                if (GUI.Button(new Rect(bx, rowRect.y + 4, 58, 17), secLabel ?? "Action", EditorStyles.miniButton))
                    onSecondary(node.FullPath, node.Status);
            }

            float indent = depth * INDENT + 16;
            EditorGUI.DrawRect(
                new Rect(rowRect.x + indent, rowRect.yMax - 1, rowRect.width - indent, 1),
                new Color(GitUIStyles.SeparatorColor.r, GitUIStyles.SeparatorColor.g,
                          GitUIStyles.SeparatorColor.b, 0.18f));
        }


        private static Texture _cachedFolderIcon;
        private static Texture CachedFolderIcon
        {
            get
            {
                if (_cachedFolderIcon == null)
                {
                    var c = EditorGUIUtility.IconContent("Folder Icon");
                    _cachedFolderIcon = c?.image;
                }
                return _cachedFolderIcon;
            }
        }

        public static Texture GetAssetIcon(string path)
        {
            var icon = AssetDatabase.GetCachedIcon(path);
            if (icon != null) return icon;

            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            string iconName = ExtToIcon(ext);
            if (iconName != null)
            {
                var c = EditorGUIUtility.IconContent(iconName);
                if (c?.image != null) return c.image;
            }

            var def = EditorGUIUtility.IconContent("DefaultAsset Icon");
            return def?.image;
        }

        private static string ExtToIcon(string ext)
        {
            switch (ext)
            {
                case ".cs":         return "cs Script Icon";
                case ".shader":
                case ".compute":
                case ".hlsl":
                case ".cginc":      return "Shader Icon";
                case ".mat":        return "Material Icon";
                case ".prefab":     return "Prefab Icon";
                case ".unity":      return "SceneAsset Icon";
                case ".asset":      return "ScriptableObject Icon";
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".tga":
                case ".psd":
                case ".gif":
                case ".bmp":
                case ".exr":
                case ".hdr":        return "Texture Icon";
                case ".fbx":
                case ".obj":
                case ".blend":
                case ".dae":        return "Mesh Icon";
                case ".anim":       return "AnimationClip Icon";
                case ".controller": return "AnimatorController Icon";
                case ".mp3":
                case ".wav":
                case ".ogg":
                case ".aiff":       return "AudioClip Icon";
                case ".txt":
                case ".json":
                case ".xml":
                case ".csv":
                case ".md":
                case ".yaml":
                case ".yml":        return "TextAsset Icon";
                case ".ttf":
                case ".otf":        return "Font Icon";
                case ".asmdef":
                case ".asmref":     return "AssemblyDefinitionAsset Icon";
                case ".dll":        return "dll Script Icon";
                default:            return null;
            }
        }

        public static void PingAsset(string path)
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (obj != null)
            {
                EditorGUIUtility.PingObject(obj);
                Selection.activeObject = obj;
            }
        }

        private static int CountLeaves(FileTreeNode n)
        {
            if (!n.IsFolder) return 1;
            int c = 0;
            for (int i = 0; i < n.Children.Count; i++)
                c += CountLeaves(n.Children[i]);
            return c;
        }
    }
}
