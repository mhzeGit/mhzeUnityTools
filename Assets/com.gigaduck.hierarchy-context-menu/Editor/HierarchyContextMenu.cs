using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Gigaduck.HierarchyContextMenu
{
    [InitializeOnLoad]
    static class HierarchyContextMenu
    {
        static HierarchyContextMenu()
        {
#if UNITY_6000_0_OR_NEWER
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyItemGUI;
#else
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
#endif
        }

#if UNITY_6000_0_OR_NEWER
        private static void OnHierarchyItemGUI(EntityId entityId, Rect selectionRect)
        {
            if (!HierarchyContextMenuSettings.Enabled)
                return;

            if (Event.current.type != EventType.ContextClick)
                return;

            Event.current.Use();

            if (HierarchyContextMenuWindow.IsOpen)
                return;

            var clickedObject = EditorUtility.EntityIdToObject(entityId);
#else
        private static void OnHierarchyItemGUI(int instanceID, Rect selectionRect)
        {
            if (!HierarchyContextMenuSettings.Enabled)
                return;

            if (Event.current.type != EventType.ContextClick)
                return;

            Event.current.Use();

            if (HierarchyContextMenuWindow.IsOpen)
                return;

            var clickedObject = EditorUtility.InstanceIDToObject(instanceID);
#endif

            GameObject clickedGo = clickedObject as GameObject;
            if (clickedGo != null)
                Selection.activeObject = clickedGo;

            HierarchyItemIndexer.EnsureIndexed();

            var topLevelNames = new HashSet<string>();
            foreach (var item in HierarchyItemIndexer.Items)
            {
                var parts = item.DisplayName.Split('/');
                topLevelNames.Add(parts[0]);
            }
            bool isPrefab = clickedGo != null && (PrefabUtility.IsPartOfPrefabInstance(clickedGo) || PrefabUtility.GetPrefabAssetType(clickedGo) == PrefabAssetType.Model);
            int additionalItems = isPrefab ? 2 : 0;
            int rootItemCount = 16 + topLevelNames.Count + additionalItems;
            float desiredHeight = Mathf.Max(44f + (rootItemCount * 22f), 60f);

            var screenPos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            HierarchyContextMenuWindow.Show(screenPos, desiredHeight);
        }
    }
}
