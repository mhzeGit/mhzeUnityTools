using System.Linq;
using UnityEditor;
using UnityEngine;

namespace mhze.HierarchyContextMenu
{
    [InitializeOnLoad]
    static class ProjectContextMenu
    {
        static ProjectContextMenu()
        {
#if UNITY_6000_0_OR_NEWER
            EditorApplication.projectWindowItemByEntityIdOnGUI += OnProjectItemGUI;
#else
            EditorApplication.projectWindowItemOnGUI += OnProjectItemGUI;
#endif
        }

#if UNITY_6000_0_OR_NEWER
        private static void OnProjectItemGUI(EntityId entityId, Rect selectionRect)
        {
            if (!HierarchyContextMenuSettings.Enabled)
                return;

            if (Event.current.type != EventType.ContextClick)
                return;

            if (ProjectContextMenuWindow.IsOpen)
                return;

            Event.current.Use();

            ProjectContextMenuWindow.ClickedOnItem = selectionRect.Contains(Event.current.mousePosition);

            if (ProjectContextMenuWindow.ClickedOnItem)
            {
                var assetPath = AssetDatabase.GetAssetPath(entityId);
#else
        private static void OnProjectItemGUI(string guid, Rect selectionRect)
        {
            if (!HierarchyContextMenuSettings.Enabled)
                return;

            if (Event.current.type != EventType.ContextClick)
                return;

            if (ProjectContextMenuWindow.IsOpen)
                return;

            Event.current.Use();

            ProjectContextMenuWindow.ClickedOnItem = selectionRect.Contains(Event.current.mousePosition);

            if (ProjectContextMenuWindow.ClickedOnItem)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
#endif
                var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                if (asset != null)
                    Selection.activeObject = asset;
            }

            ProjectItemIndexer.Reset();
            ProjectItemIndexer.EnsureIndexed();

            int rootItemCount = 28 + ProjectItemIndexer.Items
                .Select(i => i.DisplayName.Split('/')[0])
                .Distinct()
                .Count(name => name != "Create");
            float desiredHeight = Mathf.Max(44f + (rootItemCount * 22f), 60f);

            var screenPos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
            ProjectContextMenuWindow.Show(screenPos, desiredHeight);
        }

        internal static void ResetHandlingContextClick()
        {
        }
    }
}
