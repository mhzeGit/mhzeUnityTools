using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace mhze.HierarchyContextMenu
{
    internal static class ProjectContextMenuActions
    {
        public static void ExecuteMenuItem(ProjectContextMenuWindow window, string menuPath)
        {
            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusProjectWindow();
                EditorApplication.ExecuteMenuItem(menuPath);
            };
        }

        public static void DeselectAll(ProjectContextMenuWindow window)
        {
            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusProjectWindow();
                Selection.activeObject = null;
            };
        }

        public static void InvertSelection(ProjectContextMenuWindow window)
        {
            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusProjectWindow();

                var guids = AssetDatabase.FindAssets("t:Object");
                var current = new HashSet<string>(Selection.assetGUIDs);
                var inverted = guids.Where(g => !current.Contains(g)).ToArray();
                var paths = inverted.Select(AssetDatabase.GUIDToAssetPath).ToArray();
                Selection.objects = paths
                    .Select(p => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p))
                    .Where(o => o != null)
                    .ToArray();
            };
        }

        public static void ShowInExplorer(ProjectContextMenuWindow window)
        {
            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusProjectWindow();
                var path = AssetDatabase.GetAssetPath(Selection.activeObject);
                if (!string.IsNullOrEmpty(path))
                    EditorUtility.RevealInFinder(path);
            };
        }

        public static void OpenAsset(ProjectContextMenuWindow window)
        {
            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusProjectWindow();
                AssetDatabase.OpenAsset(Selection.activeObject);
            };
        }

        public static void FindReferencesInScene(ProjectContextMenuWindow window)
        {
            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusProjectWindow();

                var target = Selection.activeObject;
                if (target == null)
                    return;

                var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (!activeScene.IsValid())
                    return;

                var roots = activeScene.GetRootGameObjects();
                var referencing = new System.Collections.Generic.List<GameObject>();

                foreach (var root in roots)
                {
                    var allTransforms = root.GetComponentsInChildren<Transform>(true);
                    foreach (var t in allTransforms)
                    {
                        var components = t.GetComponents<Component>();
                        foreach (var comp in components)
                        {
                            if (comp == null)
                                continue;

                            var so = new SerializedObject(comp);
                            var prop = so.GetIterator();
                            while (prop.NextVisible(true))
                            {
                                if (prop.propertyType == SerializedPropertyType.ObjectReference &&
                                    prop.objectReferenceValue == target)
                                {
                                    referencing.Add(t.gameObject);
                                    break;
                                }
                            }

                            if (referencing.Count > 0 && referencing[referencing.Count - 1] == t.gameObject)
                                break;
                        }
                    }
                }

                Selection.objects = referencing.ToArray();
            };
        }

        private static void FocusProjectWindow()
        {
            var projectBrowserType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser");
            if (projectBrowserType != null)
            {
                var projectWindow = EditorWindow.GetWindow(projectBrowserType);
                if (projectWindow != null)
                    projectWindow.Focus();
            }
        }
    }
}
