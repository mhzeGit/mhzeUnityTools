using UnityEngine;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using System.Collections.Generic;

#pragma warning disable CS0618, CS0619

namespace HierarchyNavigator
{
    [InitializeOnLoad]
    public static class HierarchyNavigatorTool
    {
        private const string MENU_PATH = "Tools/Hierarchy Navigator/";
        
        static HierarchyNavigatorTool()
        {
#if !UNITY_6000_0_OR_NEWER
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyWindowItemGUI;
#else
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyWindowItemGUI;
#endif
        }
        
#if !UNITY_6000_0_OR_NEWER
        private static void OnHierarchyWindowItemGUI(EntityId entityId, Rect selectionRect)
#else
        private static void OnHierarchyWindowItemGUI(int instanceID, Rect selectionRect)
#endif
        {
            Event e = Event.current;

            if (e.type != EventType.KeyDown || Selection.gameObjects.Length == 0)
                return;

            if (!e.control || !e.shift)
                return;
            
            bool handled = false;
            
            switch (e.keyCode)
            {
                case KeyCode.UpArrow:
                    MoveUp();
                    handled = true;
                    break;
                case KeyCode.DownArrow:
                    MoveDown();
                    handled = true;
                    break;
                case KeyCode.LeftArrow:
                    if (CanMoveOut())
                    {
                        MoveOut();
                        handled = true;
                    }
                    break;
                case KeyCode.RightArrow:
                    if (CanMoveIn())
                    {
                        MoveIn();
                        handled = true;
                    }
                    break;
            }
            
            if (handled)
                e.Use();
        }
        
        #region Move Up/Down
        
        [MenuItem(MENU_PATH + "Move Up", false, 100)]
        private static void MoveUp()
        {
            MoveSelectedObjects(-1);
        }
        
        [MenuItem(MENU_PATH + "Move Up", true)]
        private static bool MoveUpValidate()
        {
            return Selection.gameObjects.Length > 0;
        }
        
        [MenuItem(MENU_PATH + "Move Down", false, 101)]
        private static void MoveDown()
        {
            MoveSelectedObjects(1);
        }
        
        [MenuItem(MENU_PATH + "Move Down", true)]
        private static bool MoveDownValidate()
        {
            return Selection.gameObjects.Length > 0;
        }
        
        [Shortcut("Hierarchy Navigator/Move Up", KeyCode.UpArrow, ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        private static void MoveUpShortcut()
        {
            if (!IsHierarchyFocused()) return;
            if (Selection.gameObjects.Length > 0)
                MoveSelectedObjects(-1);
        }
        
        [Shortcut("Hierarchy Navigator/Move Down", KeyCode.DownArrow, ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        private static void MoveDownShortcut()
        {
            if (!IsHierarchyFocused()) return;
            if (Selection.gameObjects.Length > 0)
                MoveSelectedObjects(1);
        }
        
        [Shortcut("Hierarchy Navigator/Move Out", KeyCode.LeftArrow, ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        private static void MoveOutShortcut()
        {
            if (!IsHierarchyFocused()) return;
            if (CanMoveOut())
                MoveOut();
        }
        
        [Shortcut("Hierarchy Navigator/Move In", KeyCode.RightArrow, ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        private static void MoveInShortcut()
        {
            if (!IsHierarchyFocused()) return;
            if (CanMoveIn())
                MoveIn();
        }
        
        private static bool IsHierarchyFocused()
        {
            var focused = EditorWindow.focusedWindow;
            return focused != null && focused.GetType().FullName.Contains("Hierarchy");
        }
        
        private static void MoveSelectedObjects(int direction)
        {
            GameObject[] selectedObjects = Selection.gameObjects;
            if (selectedObjects.Length == 0) return;
            
            List<GameObject> sortedObjects = new List<GameObject>(selectedObjects);
            sortedObjects.Sort((a, b) =>
            {
                if (a.transform.parent != b.transform.parent)
                    return 0;
                int comparison = a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex());
                return direction > 0 ? -comparison : comparison;
            });
            
            Undo.RecordObjects(GetTransforms(sortedObjects.ToArray()), "Move Objects in Hierarchy");
            
            foreach (GameObject obj in sortedObjects)
            {
                Transform transform = obj.transform;
                int currentIndex = transform.GetSiblingIndex();
                int newIndex = currentIndex + direction;
                int siblingCount = transform.parent != null
                    ? transform.parent.childCount
                    : obj.scene.rootCount;

                newIndex = Mathf.Clamp(newIndex, 0, siblingCount - 1);
                
                if (newIndex != currentIndex)
                {
                    transform.SetSiblingIndex(newIndex);
                }
            }
            
            EditorApplication.RepaintHierarchyWindow();
        }
        
        #endregion
        
        #region Parent/Unparent (Move In/Out)
        
        [MenuItem(MENU_PATH + "Move Out (Unparent)", false, 200)]
        private static void MoveOut()
        {
            UnparentSelectedObjects();
        }
        
        [MenuItem(MENU_PATH + "Move Out (Unparent)", true)]
        private static bool MoveOutValidate()
        {
            return CanMoveOut();
        }
        
        private static bool CanMoveOut()
        {
            if (Selection.gameObjects.Length == 0) return false;
            
            foreach (GameObject obj in Selection.gameObjects)
                if (obj.transform.parent != null)
                    return true;
            return false;
        }
        
        [MenuItem(MENU_PATH + "Move In (Parent to Above)", false, 201)]
        private static void MoveIn()
        {
            ParentSelectedObjects();
        }
        
        [MenuItem(MENU_PATH + "Move In (Parent to Above)", true)]
        private static bool MoveInValidate()
        {
            return CanMoveIn();
        }
        
        private static bool CanMoveIn()
        {
            if (Selection.gameObjects.Length == 0) return false;

            List<GameObject> sortedSelection = GetSortedSelection();
            if (sortedSelection.Count > 0)
                return GetSiblingAbove(sortedSelection[0].transform) != null;

            return false;
        }
        
        private static void UnparentSelectedObjects()
        {
            GameObject[] selectedObjects = Selection.gameObjects;
            if (selectedObjects.Length == 0) return;
            
            Undo.RecordObjects(GetTransforms(selectedObjects), "Unparent Objects");
            
            foreach (GameObject obj in selectedObjects)
            {
                Transform transform = obj.transform;
                Transform parent = transform.parent;
                
                if (parent != null)
                {
                    Transform grandParent = parent.parent;
                    int parentSiblingIndex = parent.GetSiblingIndex();
                    
                    Undo.SetTransformParent(transform, grandParent, "Unparent Object");
                    transform.SetSiblingIndex(parentSiblingIndex + 1);
                }
            }
            
            EditorApplication.RepaintHierarchyWindow();
        }
        
        private static void ParentSelectedObjects()
        {
            GameObject[] selectedObjects = Selection.gameObjects;
            if (selectedObjects.Length == 0) return;

            List<GameObject> sortedObjects = GetSortedSelection();
            if (sortedObjects.Count == 0) return;

            Transform newParent = GetSiblingAbove(sortedObjects[0].transform);
            if (newParent == null) return;

            Undo.RecordObjects(GetTransforms(sortedObjects.ToArray()), "Parent Objects");

            foreach (GameObject obj in sortedObjects)
                Undo.SetTransformParent(obj.transform, newParent, "Parent Object");

            EditorApplication.RepaintHierarchyWindow();
        }
        
        private static Transform GetSiblingAbove(Transform transform)
        {
            int siblingIndex = transform.GetSiblingIndex();
            
            if (siblingIndex <= 0) return null;

            if (transform.parent != null)
                return transform.parent.GetChild(siblingIndex - 1);

            GameObject[] rootObjects = transform.gameObject.scene.GetRootGameObjects();
            if (siblingIndex < rootObjects.Length)
                return rootObjects[siblingIndex - 1].transform;

            return null;
        }
        
        #endregion
        
        #region Utility Methods
        
        private static Transform[] GetTransforms(GameObject[] gameObjects)
        {
            Transform[] transforms = new Transform[gameObjects.Length];
            for (int i = 0; i < gameObjects.Length; i++)
            {
                transforms[i] = gameObjects[i].transform;
            }
            return transforms;
        }
        
        private static List<GameObject> GetSortedSelection()
        {
            List<GameObject> sortedSelection = new List<GameObject>(Selection.gameObjects);
            sortedSelection.Sort((a, b) =>
            {
                if (a.transform.parent != b.transform.parent)
                    return 0;
                return a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex());
            });
            return sortedSelection;
        }
        
        #endregion
    }
}
#pragma warning restore CS0619, CS0618