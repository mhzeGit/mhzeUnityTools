using UnityEditor;
using UnityEngine;

namespace mhze.HierarchyContextMenu
{
    [InitializeOnLoad]
    static class ProjectContextMenu
    {
        private static bool _handlingContextClick;
        private static bool _pendingOpen;
        private static bool _mouseOverItem;
        private static Vector2 _pendingScreenPos;

        static ProjectContextMenu()
        {
#pragma warning disable CS0618
            EditorApplication.projectWindowItemOnGUI += OnProjectItemGUI;
#pragma warning restore CS0618
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnProjectItemGUI(string guid, Rect selectionRect)
        {
            if (!HierarchyContextMenuSettings.Enabled)
                return;

            if (Event.current.type != EventType.ContextClick)
                return;

            if (ProjectContextMenuWindow.IsOpen)
                return;

            if (!_handlingContextClick)
            {
                _handlingContextClick = true;
                _pendingOpen = true;
                _mouseOverItem = false;
                _pendingScreenPos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
                Event.current.Use();
            }

            if (selectionRect.Contains(Event.current.mousePosition))
            {
                _mouseOverItem = true;
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                if (asset != null)
                    Selection.activeObject = asset;
            }
        }

        private static void OnEditorUpdate()
        {
            if (!_pendingOpen)
                return;
            _pendingOpen = false;

            ProjectContextMenuWindow.ClickedOnItem = _mouseOverItem;

            ProjectItemIndexer.Reset();
            ProjectItemIndexer.EnsureIndexed();

            int rootItemCount = 22;
            float desiredHeight = Mathf.Max(44f + (rootItemCount * 22f), 60f);

            ProjectContextMenuWindow.Show(_pendingScreenPos, desiredHeight);
        }

        internal static void ResetHandlingContextClick()
        {
            _handlingContextClick = false;
            _pendingOpen = false;
        }
    }
}
