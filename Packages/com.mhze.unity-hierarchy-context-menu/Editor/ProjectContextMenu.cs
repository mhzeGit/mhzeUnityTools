using UnityEditor;
using UnityEngine;

namespace mhze.HierarchyContextMenu
{
    [InitializeOnLoad]
    static class ProjectContextMenu
    {
        private static bool _contextClickPending;
        private static bool _mouseOverAnyItem;
        private static Vector2 _contextClickScreenPos;

        static ProjectContextMenu()
        {
#pragma warning disable CS0618
            EditorApplication.projectWindowItemOnGUI += OnProjectItemGUI;
#pragma warning restore CS0618
        }

        private static void OnProjectItemGUI(string guid, Rect selectionRect)
        {
            if (!HierarchyContextMenuSettings.Enabled)
                return;

            if (Event.current.type != EventType.ContextClick)
                return;

            if (ProjectContextMenuWindow.IsOpen)
                return;

            // First callback to see the ContextClick: consume event and schedule menu
            if (!_contextClickPending)
            {
                _contextClickPending = true;
                _mouseOverAnyItem = false;
                _contextClickScreenPos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
                Event.current.Use();

                EditorApplication.delayCall -= OpenContextMenu;
                EditorApplication.delayCall += OpenContextMenu;
            }

            // Every callback checks if mouse is over its rect
            if (selectionRect.Contains(Event.current.mousePosition))
            {
                _mouseOverAnyItem = true;
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                if (asset != null)
                    Selection.activeObject = asset;
            }
        }

        private static void OpenContextMenu()
        {
            EditorApplication.delayCall -= OpenContextMenu;
            if (!_contextClickPending)
                return;
            _contextClickPending = false;

            ProjectContextMenuWindow.ClickedOnItem = _mouseOverAnyItem;

            ProjectItemIndexer.EnsureIndexed();

            int rootItemCount = 22;
            float desiredHeight = Mathf.Max(44f + (rootItemCount * 22f), 60f);

            ProjectContextMenuWindow.Show(_contextClickScreenPos, desiredHeight);
        }
    }
}
