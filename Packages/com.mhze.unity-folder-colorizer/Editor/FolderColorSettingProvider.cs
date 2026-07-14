using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using ColorUtility = UnityEngine.ColorUtility;
using Object = UnityEngine.Object;

#pragma warning disable CS0618, CS0619

namespace UnityFolderColorSettings.Editor
{
    [Serializable]
    internal class FolderColorEntry
    {
        public string path = string.Empty;
        public string name = string.Empty;
        public string color = "FFFFFFFF";
    }

    [Serializable]
    internal class FolderColorProjectSettingsData
    {
        public bool useCustomFolderColor = true;
        public List<FolderColorEntry> entries = new List<FolderColorEntry>();
    }

    internal static class FolderColorProjectSettingsStorage
    {
        private const string SettingsFileName = "FolderColorSettings.json";
        private const string DefaultSettingsFileName = "FolderColorSettingsDefault.json";

        public static string SettingsFilePath
        {
            get
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                return Path.Combine(projectRoot, "ProjectSettings", SettingsFileName);
            }
        }

        public static string DefaultSettingsFilePath
        {
            get
            {
                var packageRoot = Path.Combine(Application.dataPath, "..", "Packages", "com.mhze.unity-folder-colorizer");
                return Path.Combine(packageRoot, "Resources", DefaultSettingsFileName);
            }
        }

        private static FolderColorProjectSettingsData LoadDefaultSettings()
        {
            try
            {
                if (!File.Exists(DefaultSettingsFilePath))
                {
                    return new FolderColorProjectSettingsData();
                }

                var json = File.ReadAllText(DefaultSettingsFilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new FolderColorProjectSettingsData();
                }

                return JsonUtility.FromJson<FolderColorProjectSettingsData>(json) ??
                       new FolderColorProjectSettingsData();
            }
            catch
            {
                return new FolderColorProjectSettingsData();
            }
        }

        public static FolderColorProjectSettingsData Load()
        {
            try
            {
                var defaultData = LoadDefaultSettings();

                if (!File.Exists(SettingsFilePath))
                {
                    return defaultData;
                }

                var json = File.ReadAllText(SettingsFilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return defaultData;
                }

                return JsonUtility.FromJson<FolderColorProjectSettingsData>(json) ??
                       defaultData;
            }
            catch
            {
                return new FolderColorProjectSettingsData();
            }
        }

        public static void Save(FolderColorProjectSettingsData data)
        {
            var safeData = data ?? new FolderColorProjectSettingsData();
            var settingsDirectory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(settingsDirectory))
            {
                Directory.CreateDirectory(settingsDirectory);
            }

            var json = JsonUtility.ToJson(safeData, true);
            File.WriteAllText(SettingsFilePath, json);
        }
    }

    public class FolderColorSettingProvider : SettingsProvider
    {
        private static string FolderNameToAdd = "Assets";
        private static Color ColorToAdd = Color.white;

        public FolderColorSettingProvider(string path, SettingsScope scope)
            : base(path, scope)
        {
        }

        [SettingsProvider]
        public static SettingsProvider CreateFolderIconSettingProvider()
        {
            var provider = new FolderColorSettingProvider("Project/Folder Color Settings", SettingsScope.Project);

            return provider;
        }

        public override void OnGUI(string searchContext)
        {
            bool newUseFolderIconFeature = EditorGUILayout.Toggle(
                "Use Custom Folder Color",
                FolderIconDrawer.UseCustomFolderColor,
                EditorStyles.toggle);

            if (newUseFolderIconFeature != FolderIconDrawer.UseCustomFolderColor)
            {
                FolderIconDrawer.SetUseCustomFolderColor(newUseFolderIconFeature);
                EditorApplication.RepaintProjectWindow();
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Add or modify folder color", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox(
                "Settings are stored in ProjectSettings/FolderColorSettings.json so they can be committed to Git.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            FolderNameToAdd = EditorGUILayout.TextField("Folder Name", FolderNameToAdd);
            ColorToAdd = EditorGUILayout.ColorField("Color", ColorToAdd);
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Add / Modify"))
            {
                var normalizedName = FolderNameToAdd?.Trim();
                if (!string.IsNullOrEmpty(normalizedName))
                {
                    FolderIconDrawer.NameColorDict[normalizedName] = ColorToAdd;
                    FolderIconDrawer.ResolveNamesToPaths();
                    FolderIconDrawer.SaveColorSettings();
                    EditorApplication.RepaintProjectWindow();
                }
            }

            EditorGUILayout.Space(20);
            EditorGUILayout.LabelField("Current color settings", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox(
                "Default settings are loaded from the package. Project-specific overrides are stored in ProjectSettings/FolderColorSettings.json so they can be committed to Git.",
                MessageType.Info);

            foreach (var kv in FolderIconDrawer.NameColorDict.ToList())
            {
                EditorGUILayout.BeginHorizontal();
                var updatedName = EditorGUILayout.TextField(kv.Key);
                var updatedColor = EditorGUILayout.ColorField(kv.Value);

                if (GUILayout.Button("Apply", GUILayout.Width(60)))
                {
                    var normalizedName = updatedName?.Trim();
                    if (!string.IsNullOrEmpty(normalizedName))
                    {
                        FolderIconDrawer.NameColorDict.Remove(kv.Key);
                        FolderIconDrawer.NameColorDict[normalizedName] = updatedColor;
                        FolderIconDrawer.ResolveNamesToPaths();
                    }

                    FolderIconDrawer.SaveColorSettings();
                    EditorApplication.RepaintProjectWindow();
                    break;
                }

                if (GUILayout.Button("Remove"))
                {
                    FolderIconDrawer.NameColorDict.Remove(kv.Key);
                    var pathsToRemove = FolderIconDrawer.PathColorDict.Keys.Where(p => Path.GetFileName(p).Equals(kv.Key, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var p in pathsToRemove) FolderIconDrawer.PathColorDict.Remove(p);
                    FolderIconDrawer.SaveColorSettings();
                    EditorApplication.RepaintProjectWindow();
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }
        }
    }

    [InitializeOnLoad]
    internal static class FolderIconDrawer
    {
        private static readonly Texture2D DefaultFolderTexture;
        private static readonly Texture2D OpenedFolderTexture;
        private static readonly Texture2D EmptyFolderTexture;

        public static Dictionary<string, Color> NameColorDict = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

        public static Dictionary<string, Color> PathColorDict = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

        public static bool UseCustomFolderColor { get; private set; } = true;

        private static double _lastBrowserUpdateTime = 0;
        private const double BrowserUpdateInterval = 0.5;

        static FolderIconDrawer()
        {
            NameColorDict.Clear();
            PathColorDict.Clear();

            LoadColorSettings();

            DefaultFolderTexture = EditorGUIUtility.FindTexture("d_Folder Icon");
            OpenedFolderTexture = EditorGUIUtility.FindTexture("d_FolderOpened Icon");
            EmptyFolderTexture = EditorGUIUtility.FindTexture("d_FolderEmpty Icon");

#if !UNITY_6000_0_OR_NEWER
            EditorApplication.projectWindowItemByEntityIdOnGUI += DrawFolderIcon;
#else
            EditorApplication.projectWindowItemInstanceOnGUI += DrawFolderIcon;
#endif
            EditorApplication.update += UpdateProjectBrowser;
        }

        public static void SetUseCustomFolderColor(bool value)
        {
            UseCustomFolderColor = value;
            SaveColorSettings();
        }

        private static void UpdateProjectBrowser()
        {
            if (EditorApplication.timeSinceStartup - _lastBrowserUpdateTime < BrowserUpdateInterval)
                return;

            _lastBrowserUpdateTime = EditorApplication.timeSinceStartup;
            ProjectWindowUtil.UpdateBrowserFields();
        }

#if !UNITY_6000_0_OR_NEWER
        public static void DrawFolderIcon(EntityId entityId, Rect rect)
        {
            if (!UseCustomFolderColor) return;

            var path = AssetDatabase.GetAssetPath(entityId);
#else
        public static void DrawFolderIcon(int instanceid, Rect rect)
        {
            if (!UseCustomFolderColor) return;

            var path = AssetDatabase.GetAssetPath(instanceid);
#endif

            if (string.IsNullOrEmpty(path) ||
                Event.current.type != EventType.Repaint ||
                !PathColorDict.ContainsKey(path))
            {
                return;
            }

            bool isOpened = false;
            bool isTreeView = rect.width > rect.height;
            bool isSideView = Math.Abs(rect.x - 14) > float.Epsilon;
            if (isTreeView)
            {
                rect.width = rect.height = 16;

                if (!isSideView)
                    rect.x += 3f;
                else
                    isOpened = ProjectWindowUtil.IsFolderOpened(path);
            }
            else
            {
                rect.height -= 14f;
            }

            var prevColor = GUI.color;
            GUI.color = PathColorDict[path];

            if (!Directory.EnumerateFileSystemEntries(path).Any())
            {
                GUI.DrawTexture(rect, EmptyFolderTexture);
            }
            else if (isOpened)
            {
                GUI.DrawTexture(rect, OpenedFolderTexture);
            }
            else
            {
                GUI.DrawTexture(rect, DefaultFolderTexture);
            }

            GUI.color = prevColor;
        }

        public static void SaveColorSettings()
        {
            var data = new FolderColorProjectSettingsData
            {
                useCustomFolderColor = UseCustomFolderColor,
                entries = NameColorDict
                    .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key))
                    .Select(kvp => new FolderColorEntry
                    {
                        name = kvp.Key,
                        color = ColorUtility.ToHtmlStringRGBA(kvp.Value)
                    })
                    .ToList()
            };

            FolderColorProjectSettingsStorage.Save(data);
        }

        public static void LoadColorSettings()
        {
            NameColorDict.Clear();
            PathColorDict.Clear();

            var data = FolderColorProjectSettingsStorage.Load();
            UseCustomFolderColor = data.useCustomFolderColor;

            foreach (var entry in data.entries)
            {
                if (entry == null)
                    continue;

                var colorString = entry.color;
                if (string.IsNullOrWhiteSpace(colorString))
                    continue;

                if (colorString[0] != '#')
                {
                    colorString = $"#{colorString}";
                }

                if (!ColorUtility.TryParseHtmlString(colorString, out var color))
                    continue;

                if (!string.IsNullOrWhiteSpace(entry.path))
                {
                    var folderName = Path.GetFileName(entry.path.TrimEnd('/', '\\'));
                    if (!string.IsNullOrWhiteSpace(folderName))
                    {
                        NameColorDict[folderName] = color;
                    }

                    var fullLegacyPath = entry.path;
                    try
                    {
                        if (!fullLegacyPath.StartsWith("Assets") && Directory.Exists(fullLegacyPath))
                        {
                            var assetPath = "Assets" + fullLegacyPath.Substring(Application.dataPath.Length).Replace('\\', '/');
                            PathColorDict[assetPath] = color;
                        }
                        else if (fullLegacyPath.StartsWith("Assets") && Directory.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, fullLegacyPath)))
                        {
                            PathColorDict[fullLegacyPath] = color;
                        }
                    }
                    catch
                    {
                    }
                }

                if (!string.IsNullOrWhiteSpace(entry.name))
                    NameColorDict[entry.name] = color;
            }

            ResolveNamesToPaths();
        }

        public static void ResolveNamesToPaths()
        {
            try
            {
                PathColorDict.Clear();

                var dataPath = Application.dataPath;
                var allDirs = Directory.EnumerateDirectories(dataPath, "*", SearchOption.AllDirectories).ToList();

                foreach (var dir in allDirs)
                {
                    var dirName = Path.GetFileName(dir);
                    if (NameColorDict.TryGetValue(dirName, out var color))
                    {
                        var assetPath = "Assets" + dir.Substring(dataPath.Length).Replace('\\', '/');
                        PathColorDict[assetPath] = color;
                    }
                }
            }
            catch
            {
            }
        }
    }

    [InitializeOnLoad]
    internal static class ProjectWindowUtil
    {
        private static Type ProjectBrowserType;
        private static EditorWindow ProjectBrowser;
#if UNITY_6000_0_OR_NEWER
        private static TreeViewState<int> CurrentAssetTreeViewState;
        private static TreeViewState<int> CurrentFolderTreeViewState;
#else
        private static TreeViewState CurrentAssetTreeViewState;
        private static TreeViewState CurrentFolderTreeViewState;
#endif
        private static int CurrentProjectBrowserMode;
        private static FieldInfo AssetTreeStateField;
        private static FieldInfo FolderTreeStateField;
        private static FieldInfo ProjectBroswerMode;

        static ProjectWindowUtil()
        {
            ProjectBrowserType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser");
            AssetTreeStateField =
                ProjectBrowserType.GetField("m_AssetTreeState", BindingFlags.NonPublic | BindingFlags.Instance);
            FolderTreeStateField =
                ProjectBrowserType.GetField("m_FolderTreeState", BindingFlags.NonPublic | BindingFlags.Instance);
            ProjectBroswerMode =
                ProjectBrowserType.GetField("m_ViewMode", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        public static bool IsFolderOpened(string path)
        {
            var state = CurrentProjectBrowserMode == 0 ? CurrentAssetTreeViewState : CurrentFolderTreeViewState;

            if (state != null)
            {
#if !UNITY_6000_0_OR_NEWER
                var entityId = AssetDatabase.LoadAssetAtPath<Object>(path).GetEntityId();
                return state.expandedIDs.Contains(entityId.GetHashCode());
#else
                var instanceID = AssetDatabase.LoadAssetAtPath<Object>(path).GetInstanceID();
                return state.expandedIDs.Contains(instanceID);
#endif
            }

            return false;
        }

        public static void UpdateBrowserFields()
        {
            try
            {
                var projectBrowsers = Resources.FindObjectsOfTypeAll(ProjectBrowserType);

                foreach (var obj in projectBrowsers)
                {
                    var browser = obj as EditorWindow;
                    if (browser.hasFocus)
                    {
                        ProjectBrowser = browser;
                    }
                }

#if UNITY_6000_0_OR_NEWER
                CurrentAssetTreeViewState = AssetTreeStateField.GetValue(ProjectBrowser) as TreeViewState<int>;
                CurrentFolderTreeViewState = FolderTreeStateField.GetValue(ProjectBrowser) as TreeViewState<int>;
#else
                CurrentAssetTreeViewState = AssetTreeStateField.GetValue(ProjectBrowser) as TreeViewState;
                CurrentFolderTreeViewState = FolderTreeStateField.GetValue(ProjectBrowser) as TreeViewState;
#endif
                CurrentProjectBrowserMode = (int)ProjectBroswerMode.GetValue(ProjectBrowser);
            }
            catch
            {
                CurrentFolderTreeViewState = null;
            }
        }
    }
}
#pragma warning restore CS0619, CS0618