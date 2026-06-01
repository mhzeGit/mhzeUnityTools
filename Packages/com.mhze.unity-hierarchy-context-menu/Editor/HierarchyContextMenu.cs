using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace mhze.HierarchyContextMenu
{
    [InitializeOnLoad]
    static class HierarchyContextMenu
    {
        static HierarchyContextMenu()
        {
#pragma warning disable CS0618
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
#pragma warning restore CS0618
        }

        private static void OnHierarchyItemGUI(int instanceID, Rect selectionRect)
        {
            if (Event.current.type != EventType.ContextClick)
                return;

            Event.current.Use();

            if (HierarchyContextMenuWindow.IsOpen)
                return;

#pragma warning disable CS0618
            var clickedObject = EditorUtility.InstanceIDToObject(instanceID);
#pragma warning restore CS0618
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
            bool isPrefab = clickedGo != null && PrefabUtility.IsPartOfPrefabInstance(clickedGo);
            int additionalItems = isPrefab ? 2 : 0;
            int rootItemCount = 16 + topLevelNames.Count + additionalItems;
            float desiredHeight = Mathf.Max(36f + (rootItemCount * 22f) + 16f, 60f);

            var screenPos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            HierarchyContextMenuWindow.Show(screenPos, desiredHeight);
        }
    }
}
