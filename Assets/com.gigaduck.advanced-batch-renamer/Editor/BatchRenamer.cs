using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gigaduck.BatchRenamer
{
    static class BatchRenamer
    {
        [MenuItem("Assets/Create/Advanced Batch Rename Preset", false, 200)]
        static void CreatePreset()
        {
            var preset = ScriptableObject.CreateInstance<BatchRenamePreset>();
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(path))
                path = "Assets";
            else if (!AssetDatabase.IsValidFolder(path))
                path = Path.GetDirectoryName(path);

            string assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(path, "New Advanced Batch Rename Preset.asset"));
            AssetDatabase.CreateAsset(preset, assetPath);
            AssetDatabase.SaveAssets();
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = preset;
        }

        [MenuItem("Assets/Advanced Batch Rename", false, 20)]
        static void OpenFromProject()
        {
            var objects = GetSelectedProjectAssets();
            BatchRenamerWindow.ShowWindow(objects);
        }

        internal static Object[] GetSelectedProjectAssets()
        {
            var guids = Selection.assetGUIDs;
            if (guids != null && guids.Length > 0)
            {
                var objs = new Object[guids.Length];
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    objs[i] = AssetDatabase.LoadMainAssetAtPath(path);
                }
                return objs;
            }

            var objects = Selection.objects;
            if (objects == null || objects.Length == 0)
            {
                var active = Selection.activeObject;
                if (active != null)
                    objects = new Object[] { active };
            }
            return objects;
        }

        [MenuItem("GameObject/Advanced Batch Rename", false, 20)]
        static void OpenFromHierarchy()
        {
            var objects = Selection.objects;
            if (objects == null || objects.Length == 0)
            {
                var active = Selection.activeGameObject;
                if (active != null)
                    objects = new Object[] { active };
            }
            BatchRenamerWindow.ShowWindow(objects);
        }

#if !UNITY_6000_0_OR_NEWER
        [MenuItem("Edit/Rename _F2")]
#endif
        private static void OnF2Rename()
        {
            var objects = GetSelectedProjectAssets();
            if (objects != null && objects.Length > 1)
            {
                BatchRenamerWindow.ShowWindow(objects);
                return;
            }

            var activeObject = Selection.activeObject;
            if (activeObject == null) return;

            var windowType = activeObject is GameObject
                ? System.Type.GetType("UnityEditor.SceneHierarchyWindow,UnityEditor")
                : System.Type.GetType("UnityEditor.ProjectBrowser,UnityEditor");

            if (windowType == null) return;

            var windows = Resources.FindObjectsOfTypeAll(windowType);
            if (windows.Length == 0) return;

            var renameWindow = windows[0] as EditorWindow;
            if (renameWindow == null) return;

            renameWindow.Focus();

            var method = windowType.GetMethod("Rename",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (method != null)
            {
                method.Invoke(renameWindow, null);
                return;
            }

            // Fallback: try alternative method name for ProjectBrowser
            method = windowType.GetMethod("StartRename",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (method != null)
            {
                method.Invoke(renameWindow, null);
                return;
            }

            // Last resort: send F2 key event directly
            renameWindow.SendEvent(new Event
            {
                type = EventType.KeyDown,
                keyCode = KeyCode.F2,
                modifiers = EventModifiers.None,
                character = '\0'
            });
        }

#if !UNITY_6000_0_OR_NEWER
        [MenuItem("Edit/Rename _F2", true)]
#endif
        private static bool ValidateOnF2Rename()
        {
            return Selection.activeObject != null;
        }

        [MenuItem("Edit/Advanced Batch Rename", false, 21)]
        [Shortcut("Edit/Advanced Batch Rename", KeyCode.F2, ShortcutModifiers.Action)]
        private static void OnBatchRenameShortcut()
        {
            var objects = GetSelectedProjectAssets();
            if (objects != null && objects.Length > 0)
                BatchRenamerWindow.ShowWindow(objects);
        }

        [MenuItem("Edit/Advanced Batch Rename", true, 21)]
        private static bool ValidateOnBatchRenameShortcut()
        {
            return Selection.objects != null && Selection.objects.Length > 1;
        }
    }
}
