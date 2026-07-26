using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MHZE.FolderStructureGenerator
{
    public class FolderStructureGeneratorWindow : EditorWindow
    {
        [System.NonSerialized] List<FolderStructurePreset> _presets;
        [System.NonSerialized] int _selectedPresetIndex;
        [System.NonSerialized] FolderStructurePreset _workingPreset;
        [System.NonSerialized] Vector2 _scrollPos;
        [System.NonSerialized] bool _foldoutState;
        [System.NonSerialized] string _newPresetName = "";
        [System.NonSerialized] string _statusMessage = "";
        [System.NonSerialized] double _statusMessageTime;
        [System.NonSerialized] DefaultAsset _targetFolder;
        [System.NonSerialized] string _rootFolderName = "_ProjectName";
        [System.NonSerialized] bool _wrapInRoot = true;

        [MenuItem("Tools/Folder Structure Generator")]
        public static void ShowWindow()
        {
            var w = GetWindow<FolderStructureGeneratorWindow>();
            w.titleContent = new GUIContent("Folder Structure");
            w.Show();
        }

        void OnEnable()
        {
            LoadPresets();
            if (_presets.Count > 0)
                SelectPreset(0);
        }

        void LoadPresets()
        {
            _presets = new List<FolderStructurePreset>();
            _presets.AddRange(FolderStructurePreset.GetBuiltInPresets());

            var guids = AssetDatabase.FindAssets("t:FolderStructurePreset");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var preset = AssetDatabase.LoadAssetAtPath<FolderStructurePreset>(path);
                if (preset != null)
                    _presets.Add(preset);
            }
        }

        void SelectPreset(int index)
        {
            if (index < 0 || index >= _presets.Count)
                return;
            _selectedPresetIndex = index;
            _workingPreset = _presets[index].Clone();
            _newPresetName = _presets[index].presetName;
        }

        void OnGUI()
        {
            DrawHeader();
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            DrawFolderTree();
            EditorGUILayout.EndScrollView();
            DrawFooter();
            DrawStatusMessage();
        }

        void DrawHeader()
        {
            EditorGUILayout.Space();
            GUILayout.Label("Folder Structure Generator", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            var presetNames = new string[_presets.Count];
            for (int i = 0; i < _presets.Count; i++)
                presetNames[i] = _presets[i].presetName;
            var selected = EditorGUILayout.Popup("Preset", _selectedPresetIndex, presetNames);
            if (EditorGUI.EndChangeCheck())
                SelectPreset(selected);

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            _targetFolder = (DefaultAsset)EditorGUILayout.ObjectField("Target Folder", _targetFolder, typeof(DefaultAsset), false);
            EditorGUILayout.EndHorizontal();

            if (_targetFolder != null && !AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(_targetFolder)))
                _targetFolder = null;

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            _wrapInRoot = EditorGUILayout.ToggleLeft("Wrap in root folder", _wrapInRoot, GUILayout.Width(140));
            GUI.enabled = _wrapInRoot;
            _rootFolderName = EditorGUILayout.TextField(_rootFolderName, GUILayout.Width(200));
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save as Preset", GUILayout.Width(120)))
                SaveAsPreset();
            if (GUILayout.Button("Reload Presets", GUILayout.Width(120)))
                ReloadPresets();
            if (GUILayout.Button("Reset to Default", GUILayout.Width(120)))
                ResetToDefault();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.Space();
        }

        void DrawFolderTree()
        {
            if (_workingPreset == null)
            {
                GUILayout.Label("Select a preset to begin.", EditorStyles.miniLabel);
                return;
            }

            _foldoutState = EditorGUILayout.Foldout(_foldoutState, "Folder Structure", true);
            if (!_foldoutState)
                return;

            EditorGUI.indentLevel++;
            for (int i = 0; i < _workingPreset.rootFolders.Count; i++)
                DrawFolderNode(_workingPreset.rootFolders, i, 0);
            EditorGUI.indentLevel--;

            EditorGUILayout.Space();
            if (GUILayout.Button("+ Add Root Folder", GUILayout.Width(140)))
            {
                _workingPreset.rootFolders.Add(new FolderNode("NewFolder"));
                GUI.FocusControl(null);
            }
        }

        void DrawFolderNode(List<FolderNode> siblings, int index, int depth)
        {
            var node = siblings[index];

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(depth * 20);

            node.name = EditorGUILayout.TextField(node.name, GUILayout.Width(200));

            GUI.enabled = depth < 5;
            if (GUILayout.Button("+", GUILayout.Width(24)))
            {
                node.children.Add(new FolderNode("SubFolder"));
                GUI.FocusControl(null);
            }
            GUI.enabled = true;

            if (GUILayout.Button("x", GUILayout.Width(24)))
            {
                siblings.RemoveAt(index);
                GUI.FocusControl(null);
                return;
            }

            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < node.children.Count; i++)
                DrawFolderNode(node.children, i, depth + 1);
        }

        void DrawFooter()
        {
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Setup Folders", GUILayout.Height(30)))
                CreateFolders();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
        }

        void DrawStatusMessage()
        {
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                var elapsed = EditorApplication.timeSinceStartup - _statusMessageTime;
                var alpha = Mathf.Clamp01(1f - (float)(elapsed / 3.0));
                if (alpha <= 0f)
                {
                    _statusMessage = "";
                    return;
                }
                var color = GUI.color;
                GUI.color = new Color(1, 1, 1, alpha);
                EditorGUILayout.HelpBox(_statusMessage, _statusMessage.Contains("Error") ? MessageType.Error : MessageType.Info);
                GUI.color = color;
                if (alpha > 0)
                    Repaint();
            }
        }

        void SetStatus(string message)
        {
            _statusMessage = message;
            _statusMessageTime = EditorApplication.timeSinceStartup;
        }

        void SaveAsPreset()
        {
            if (_workingPreset == null || _workingPreset.rootFolders.Count == 0)
            {
                SetStatus("Error: No folders to save.");
                return;
            }

            var path = EditorUtility.SaveFilePanelInProject(
                "Save Folder Structure Preset",
                _newPresetName.Replace(" ", ""),
                "asset",
                "Choose where to save the preset."
            );

            if (string.IsNullOrEmpty(path))
                return;

            var preset = ScriptableObject.CreateInstance<FolderStructurePreset>();
            preset.presetName = _newPresetName;
            foreach (var root in _workingPreset.rootFolders)
                preset.rootFolders.Add(root.Clone());

            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            ReloadPresets();
            SetStatus("Preset saved to " + path);
        }

        void ReloadPresets()
        {
            var currentName = _workingPreset != null ? _workingPreset.presetName : "";
            LoadPresets();
            for (int i = 0; i < _presets.Count; i++)
            {
                if (_presets[i].presetName == currentName)
                {
                    SelectPreset(i);
                    return;
                }
            }
            if (_presets.Count > 0)
                SelectPreset(0);
        }

        void ResetToDefault()
        {
            if (_presets.Count > 0)
                SelectPreset(0);
        }

        void CreateFolders()
        {
            if (_workingPreset == null || _workingPreset.rootFolders.Count == 0)
            {
                SetStatus("Error: No folders defined in the preset.");
                return;
            }

            string basePath = "Assets";
            if (_targetFolder != null)
            {
                var folderPath = AssetDatabase.GetAssetPath(_targetFolder);
                if (AssetDatabase.IsValidFolder(folderPath))
                    basePath = folderPath;
            }

            if (_wrapInRoot && !string.IsNullOrWhiteSpace(_rootFolderName))
            {
                var rootName = SanitizeFolderName(_rootFolderName);
                var rootPath = Path.Combine(basePath, rootName).Replace("\\", "/");
                if (!AssetDatabase.IsValidFolder(rootPath))
                {
                    var guid = AssetDatabase.CreateFolder(basePath, rootName);
                    if (string.IsNullOrEmpty(guid))
                    {
                        SetStatus($"Error: Failed to create root folder '{rootPath}'.");
                        return;
                    }
                }
                basePath = rootPath;
            }

            int created = 0;
            int skipped = 0;
            foreach (var root in _workingPreset.rootFolders)
                CreateFolderRecursive(root, basePath, ref created, ref skipped);

            AssetDatabase.Refresh();
            SetStatus($"Created {created} folders" + (skipped > 0 ? $" ({skipped} already existed)." : "."));
        }

        void CreateFolderRecursive(FolderNode node, string parentPath, ref int created, ref int skipped)
        {
            if (string.IsNullOrWhiteSpace(node.name))
                return;

            var sanitized = SanitizeFolderName(node.name);
            var path = Path.Combine(parentPath, sanitized).Replace("\\", "/");

            if (!AssetDatabase.IsValidFolder(path))
            {
                var guid = AssetDatabase.CreateFolder(parentPath, sanitized);
                if (!string.IsNullOrEmpty(guid))
                    created++;
                else
                    SetStatus($"Error: Failed to create folder '{path}'.");
            }
            else
            {
                skipped++;
            }

            foreach (var child in node.children)
                CreateFolderRecursive(child, path, ref created, ref skipped);
        }

        static string SanitizeFolderName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new System.Text.StringBuilder(name.Length);
            foreach (var c in name)
            {
                if (System.Array.IndexOf(invalid, c) >= 0)
                    sanitized.Append('_');
                else
                    sanitized.Append(c);
            }
            return sanitized.ToString();
        }
    }
}
