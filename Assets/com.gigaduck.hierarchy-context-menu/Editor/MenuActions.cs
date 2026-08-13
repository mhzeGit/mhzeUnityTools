using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gigaduck.HierarchyContextMenu
{
    internal static class MenuActions
    {
        public static void FocusHierarchyWindow()
        {
            var hierarchyType = typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
            if (hierarchyType != null)
            {
                var hierarchyWindow = EditorWindow.GetWindow(hierarchyType);
                if (hierarchyWindow != null)
                    hierarchyWindow.Focus();
            }
        }

        public static void CutSelection(HierarchyContextMenuWindow window)
        {
            if (Selection.gameObjects.Length == 0)
                return;

            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                EditorApplication.ExecuteMenuItem("Edit/Cut");
            };
        }

        public static void CopySelection(HierarchyContextMenuWindow window)
        {
            if (Selection.gameObjects.Length == 0)
                return;

            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                EditorApplication.ExecuteMenuItem("Edit/Copy");
            };
        }

        public static void PasteAsChildOfClicked(HierarchyContextMenuWindow window)
        {
            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                EditorApplication.ExecuteMenuItem("Edit/Paste");
            };
        }

        public static void RenameSelected(HierarchyContextMenuWindow window)
        {
            if (Selection.gameObjects.Length == 0)
                return;

            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                EditorApplication.ExecuteMenuItem("Edit/Rename");
            };
        }

        public static void DuplicateSelection(HierarchyContextMenuWindow window)
        {
            if (Selection.gameObjects.Length == 0)
                return;

            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                EditorApplication.ExecuteMenuItem("Edit/Duplicate");
            };
        }

        public static void PasteAsChild(HierarchyContextMenuWindow window)
        {
            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                EditorApplication.ExecuteMenuItem("GameObject/Paste As Child");
            };
        }

        public static void PasteAsSibling(HierarchyContextMenuWindow window)
        {
            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                EditorApplication.ExecuteMenuItem("GameObject/Paste As Sibling");
            };
        }

        public static void DeleteSelection(HierarchyContextMenuWindow window)
        {
            if (Selection.gameObjects.Length == 0)
                return;

            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                EditorApplication.ExecuteMenuItem("Edit/Delete");
            };
        }

        public static void SelectAll(HierarchyContextMenuWindow window)
        {
            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                EditorApplication.ExecuteMenuItem("Edit/Select All");
            };
        }

        public static void DeselectAll(HierarchyContextMenuWindow window)
        {
            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                Selection.activeGameObject = null;
            };
        }

        public static void InvertSelection(HierarchyContextMenuWindow window)
        {
            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                var currentSelection = new HashSet<GameObject>(Selection.gameObjects);
                var allObjects = new List<GameObject>();
                var activeScene = SceneManager.GetActiveScene();
                if (activeScene.IsValid())
                {
                    var roots = activeScene.GetRootGameObjects();
                    foreach (var root in roots)
                        CollectAllChildren(root, allObjects);
                }
                var newSelection = new List<GameObject>();
                foreach (var obj in allObjects)
                {
                    if (!currentSelection.Contains(obj))
                        newSelection.Add(obj);
                }
                Selection.objects = newSelection.ToArray();
            };
        }

        public static void SelectChildren(HierarchyContextMenuWindow window)
        {
            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                var parents = Selection.gameObjects;
                var allSelected = new HashSet<GameObject>(parents);
                foreach (var parent in parents)
                    CollectAllChildren(parent, allSelected);
                Selection.objects = allSelected.ToArray();
            };
        }

        public static void CollectAllChildren(GameObject parent, List<GameObject> list)
        {
            foreach (Transform child in parent.transform)
            {
                list.Add(child.gameObject);
                CollectAllChildren(child.gameObject, list);
            }
        }

        public static void CollectAllChildren(GameObject parent, HashSet<GameObject> set)
        {
            foreach (Transform child in parent.transform)
            {
                set.Add(child.gameObject);
                CollectAllChildren(child.gameObject, set);
            }
        }

        public static void FindReferencesInScene(HierarchyContextMenuWindow window)
        {
            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                if (Selection.activeGameObject == null)
                    return;

                var target = Selection.activeGameObject;
                var activeScene = SceneManager.GetActiveScene();
                if (!activeScene.IsValid())
                    return;

                var roots = activeScene.GetRootGameObjects();
                var referencing = new List<GameObject>();

                foreach (var root in roots)
                {
                    var allTransforms = root.GetComponentsInChildren<Transform>(true);
                    foreach (var t in allTransforms)
                    {
                        if (t.gameObject == target)
                            continue;

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

        public static void SetAsDefaultParent(HierarchyContextMenuWindow window)
        {
            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                if (Selection.activeGameObject == null)
                    return;

                var hierarchyType = typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
                if (hierarchyType == null)
                    return;

                var hierarchyWindow = EditorWindow.GetWindow(hierarchyType);
                if (hierarchyWindow == null)
                    return;

                var prop = hierarchyType.GetProperty("defaultParent",
                    BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                {
                    prop.SetValue(hierarchyWindow, Selection.activeGameObject.transform, null);
                }
            };
        }

        public static void OpenAssetInContext(HierarchyContextMenuWindow window)
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (source == null) return;
            ExecutePrefabAction(window, () => AssetDatabase.OpenAsset(source));
        }

        public static void OpenAssetInIsolation(HierarchyContextMenuWindow window)
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (source == null) return;
            ExecutePrefabAction(window, () => AssetDatabase.OpenAsset(source));
        }

        public static void SelectPrefabAsset(HierarchyContextMenuWindow window)
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (source == null) return;
            ExecutePrefabAction(window, () => Selection.activeObject = source);
        }

        public static void SelectPrefabRoot(HierarchyContextMenuWindow window)
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            if (root == null) return;
            ExecutePrefabAction(window, () => Selection.activeGameObject = root);
        }

        public static void UnpackPrefab(HierarchyContextMenuWindow window)
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            if (root == null) return;
            ExecutePrefabAction(window, () => PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.OutermostRoot, InteractionMode.UserAction));
        }

        public static void UnpackPrefabCompletely(HierarchyContextMenuWindow window)
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            if (root == null) return;
            ExecutePrefabAction(window, () => PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.UserAction));
        }

        public static void ReplacePrefab(HierarchyContextMenuWindow window)
        {
            ReplacePrefabInternal(window, false);
        }

        public static void ReplacePrefabKeepOverrides(HierarchyContextMenuWindow window)
        {
            ReplacePrefabInternal(window, true);
        }

        private static void ReplacePrefabInternal(HierarchyContextMenuWindow window, bool keepOverrides)
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            if (root == null) return;

            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();

                if (root == null) return;

                var absPath = EditorUtility.OpenFilePanelWithFilters(
                    keepOverrides ? "Replace Prefab (Keep Overrides)" : "Replace Prefab",
                    "Assets",
                    new[] { "Prefab files", "prefab" });

                if (string.IsNullOrEmpty(absPath))
                    return;

                var relPath = FileUtil.GetProjectRelativePath(absPath);
                if (string.IsNullOrEmpty(relPath))
                {
                    EditorUtility.DisplayDialog("Replace Prefab", "Selected file is not in the project.", "OK");
                    return;
                }

                var newPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(relPath);
                if (newPrefab == null) return;

                var currentSource = PrefabUtility.GetCorrespondingObjectFromSource(root);
                if (currentSource == newPrefab)
                    return;

                var overrides = keepOverrides ? PrefabUtility.GetPropertyModifications(root) : null;

                var parent = root.transform.parent;
                var siblingIndex = root.transform.GetSiblingIndex();
                var position = root.transform.position;
                var rotation = root.transform.rotation;
                var localScale = root.transform.localScale;
                var name = root.name;
                var layer = root.layer;
                var tag = root.tag;

                var newInstance = (GameObject)PrefabUtility.InstantiatePrefab(newPrefab, parent);
                if (newInstance == null) return;

                newInstance.transform.SetSiblingIndex(siblingIndex);
                newInstance.transform.position = position;
                newInstance.transform.rotation = rotation;
                newInstance.transform.localScale = localScale;
                newInstance.name = name;
                newInstance.layer = layer;
                newInstance.tag = tag;

                if (overrides != null && overrides.Length > 0)
                    PrefabUtility.SetPropertyModifications(newInstance, overrides);

                Selection.activeGameObject = newInstance;
                UnityEngine.Object.DestroyImmediate(root);
            };
        }

        public static void RemoveUnusedOverrides(HierarchyContextMenuWindow window)
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            if (root == null) return;

            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();

                if (root == null) return;
                var source = PrefabUtility.GetCorrespondingObjectFromSource(root);
                if (source == null) return;

                var overrides = PrefabUtility.GetPropertyModifications(root);
                if (overrides == null || overrides.Length == 0)
                {
                    EditorUtility.DisplayDialog("Remove Unused Overrides", "No unused overrides found.", "OK");
                    return;
                }

                var activeOverrides = new List<PropertyModification>();
                int removedCount = 0;

                foreach (var mod in overrides)
                {
                    if (mod.target == null)
                    {
                        removedCount++;
                        continue;
                    }

                    var comp = mod.target as Component;
                    if (comp == null)
                    {
                        activeOverrides.Add(mod);
                        continue;
                    }

                    var targetGo = comp.gameObject;
                    var path = GetTransformPath(targetGo.transform, root.transform);
                    var sourceTransform = string.IsNullOrEmpty(path) ? source.transform : source.transform.Find(path);

                    if (sourceTransform != null)
                    {
                        var sourceComponent = sourceTransform.GetComponent(comp.GetType());
                        if (sourceComponent != null)
                        {
                            var instanceSo = new SerializedObject(comp);
                            var sourceSo = new SerializedObject(sourceComponent);
                            var instanceProp = instanceSo.FindProperty(mod.propertyPath);
                            var sourceProp = sourceSo.FindProperty(mod.propertyPath);

                            if (instanceProp != null && sourceProp != null &&
                                SerializedProperty.DataEquals(instanceProp, sourceProp))
                            {
                                removedCount++;
                                continue;
                            }
                        }
                    }

                    activeOverrides.Add(mod);
                }

                if (removedCount == 0)
                {
                    EditorUtility.DisplayDialog("Remove Unused Overrides", "No unused overrides found.", "OK");
                    return;
                }

                bool proceed = EditorUtility.DisplayDialog("Remove Unused Overrides",
                    $"Remove {removedCount} unused override(s)?", "Remove", "Cancel");

                if (proceed)
                {
                    PrefabUtility.SetPropertyModifications(root, activeOverrides.ToArray());
                }
            };
        }

        public static string GetTransformPath(Transform child, Transform root)
        {
            if (child == root) return "";
            var path = child.name;
            var current = child.parent;
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        public static void ExecutePrefabAction(HierarchyContextMenuWindow window, Action action)
        {
            window.Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                action?.Invoke();
            };
        }
    }
}
