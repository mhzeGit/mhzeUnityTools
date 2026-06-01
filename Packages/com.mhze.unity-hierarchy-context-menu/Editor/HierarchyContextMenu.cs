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

            var clickedObject = EditorUtility.InstanceIDToObject(instanceID);
            if (clickedObject != null)
                Selection.activeObject = clickedObject;

            HierarchyItemIndexer.EnsureIndexed();

            var topLevelNames = new HashSet<string>();
            foreach (var item in HierarchyItemIndexer.Items)
            {
                var parts = item.DisplayName.Split('/');
                topLevelNames.Add(parts[0]);
            }
            int rootItemCount = 6 + topLevelNames.Count;
            float desiredHeight = Mathf.Max(36f + (rootItemCount * 22f) + 16f, 60f);

            var screenPos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            HierarchyContextMenuWindow.Show(screenPos, desiredHeight);
        }
    }
}
