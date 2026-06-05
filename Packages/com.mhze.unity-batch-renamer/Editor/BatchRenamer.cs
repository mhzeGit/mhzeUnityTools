using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace mhze.BatchRenamer
{
    static class BatchRenamer
    {
        [MenuItem("Assets/Create/Batch Rename Preset", false, 200)]
        static void CreatePreset()
        {
            var preset = ScriptableObject.CreateInstance<BatchRenamePreset>();
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(path))
                path = "Assets";
            else if (!AssetDatabase.IsValidFolder(path))
                path = Path.GetDirectoryName(path);

            string assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(path, "New Batch Rename Preset.asset"));
            AssetDatabase.CreateAsset(preset, assetPath);
            AssetDatabase.SaveAssets();
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = preset;
        }

        [MenuItem("Assets/Batch Rename", false, 20)]
        static void OpenFromProject()
        {
            var objects = Selection.objects;
            BatchRenamerWindow.ShowWindow(objects);
        }

        [MenuItem("GameObject/Batch Rename", false, 20)]
        static void OpenFromHierarchy()
        {
            var objects = Selection.objects;
            BatchRenamerWindow.ShowWindow(objects);
        }

        [MenuItem("Assets/Batch Rename", true)]
        static bool ValidateOpenFromProject()
        {
            return Selection.objects != null && Selection.objects.Length > 0;
        }

        [MenuItem("GameObject/Batch Rename", true)]
        static bool ValidateOpenFromHierarchy()
        {
            return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
        }

        [MenuItem("Edit/Rename _F2")]
        private static void OnF2Rename()
        {
            var objects = Selection.objects;
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

        [MenuItem("Edit/Rename _F2", true)]
        private static bool ValidateOnF2Rename()
        {
            return Selection.objects != null && Selection.objects.Length > 0;
        }
    }
}
