using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

#pragma warning disable CS0618

namespace EditorVisualTweaks
{
    [InitializeOnLoad]
    public static class EditorVisualTweaks
    {
        private const string PREF_HIERARCHY_ZEBRA = "EditorVisuals.HierarchyZebra";
        private const string PREF_PROJECT_ZEBRA = "EditorVisuals.ProjectZebra";
        private const string PREF_HIERARCHY_LINES = "EditorVisuals.HierarchyLines";
        private const string PREF_PROJECT_LINES = "EditorVisuals.ProjectLines";

        private static bool _hierarchyZebra;
        private static bool _projectZebra;
        private static bool _hierarchyLines;
        private static bool _projectLines;

        public static bool HierarchyZebra
        {
            get => _hierarchyZebra;
            set
            {
                if (_hierarchyZebra == value) return;
                _hierarchyZebra = value;
                EditorPrefs.SetBool(PREF_HIERARCHY_ZEBRA, value);
                EditorApplication.RepaintHierarchyWindow();
            }
        }

        public static bool ProjectZebra
        {
            get => _projectZebra;
            set
            {
                if (_projectZebra == value) return;
                _projectZebra = value;
                EditorPrefs.SetBool(PREF_PROJECT_ZEBRA, value);
                EditorApplication.RepaintProjectWindow();
            }
        }

        public static bool HierarchyLines
        {
            get => _hierarchyLines;
            set
            {
                if (_hierarchyLines == value) return;
                _hierarchyLines = value;
                EditorPrefs.SetBool(PREF_HIERARCHY_LINES, value);
                EditorApplication.RepaintHierarchyWindow();
            }
        }

        public static bool ProjectLines
        {
            get => _projectLines;
            set
            {
                if (_projectLines == value) return;
                _projectLines = value;
                EditorPrefs.SetBool(PREF_PROJECT_LINES, value);
                EditorApplication.RepaintProjectWindow();
            }
        }

        private static Color ZebraColor => EditorGUIUtility.isProSkin
            ? new Color(1f, 1f, 1f, 0.04f)
            : new Color(0f, 0f, 0f, 0.06f);

        private static Color LineColor => EditorGUIUtility.isProSkin
            ? new Color(0.4f, 0.4f, 0.4f, 0.6f)
            : new Color(0.5f, 0.5f, 0.5f, 0.6f);

        private const float HIERARCHY_INDENT = 14f;
        private const float HIERARCHY_BASE_OFFSET = 60f;
        private const float LINE_WIDTH = 1f;

        static EditorVisualTweaks()
        {
            _hierarchyZebra = EditorPrefs.GetBool(PREF_HIERARCHY_ZEBRA, true);
            _projectZebra = EditorPrefs.GetBool(PREF_PROJECT_ZEBRA, true);
            _hierarchyLines = EditorPrefs.GetBool(PREF_HIERARCHY_LINES, false);
            _projectLines = EditorPrefs.GetBool(PREF_PROJECT_LINES, false);

#if UNITY_6000_0_OR_NEWER
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI -= OnHierarchyWindowItemGUI;
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyWindowItemGUI;

            EditorApplication.projectWindowItemByEntityIdOnGUI -= OnProjectWindowItemGUI;
            EditorApplication.projectWindowItemByEntityIdOnGUI += OnProjectWindowItemGUI;
#else
            EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyWindowItemGUI;
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyWindowItemGUI;

            EditorApplication.projectWindowItemOnGUI -= OnProjectWindowItemGUI;
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
#endif
        }

#if UNITY_6000_0_OR_NEWER
        private static void OnHierarchyWindowItemGUI(EntityId entityId, Rect selectionRect)
        {
            if (!_hierarchyZebra && !_hierarchyLines)
                return;

            GameObject obj = EditorUtility.EntityIdToObject(entityId) as GameObject;
#else
        private static void OnHierarchyWindowItemGUI(int instanceID, Rect selectionRect)
        {
            if (!_hierarchyZebra && !_hierarchyLines)
                return;

            GameObject obj = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
#endif

            if (_hierarchyZebra)
            {
                DrawZebraStripe(selectionRect);
            }

            if (_hierarchyLines && obj != null)
            {
                int depth = GetHierarchyDepth(obj.transform);
                bool hasChildren = obj.transform.childCount > 0;
                
                DrawHierarchyLines(selectionRect, depth, hasChildren);
            }
        }

        

#if UNITY_6000_0_OR_NEWER
        private static void OnProjectWindowItemGUI(EntityId entityId, Rect selectionRect)
        {
            if (!_projectZebra && !_projectLines)
                return;

            if (selectionRect.height > 20f)
                return;

            if (_projectZebra)
            {
                DrawZebraStripe(selectionRect);
            }

            if (_projectLines)
            {
                string path = AssetDatabase.GetAssetPath(entityId);
#else
        private static void OnProjectWindowItemGUI(string guid, Rect selectionRect)
        {
            if (!_projectZebra && !_projectLines)
                return;

            if (selectionRect.height > 20f)
                return;

            if (_projectZebra)
            {
                DrawZebraStripe(selectionRect);
            }

            if (_projectLines)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
#endif
                if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                {
                    int depth = GetProjectDepth(path);
                    
                    float minTreeX = 32f; // Minimum x for first-level items in tree view
                    if (selectionRect.x < minTreeX)
                        return;
                    
                    bool hasChildren = HasSubfolders(path);
                    DrawProjectLines(selectionRect, depth, hasChildren);
                }
            }
        }

        private static void DrawZebraStripe(Rect selectionRect)
        {
            int rowIndex = Mathf.FloorToInt(selectionRect.y / selectionRect.height);
            if (rowIndex % 2 == 0)
                return;

            Rect stripeRect = new Rect(
                0f,
                selectionRect.y,
                selectionRect.x + selectionRect.width + 100f, // Extra width to cover scrollbar area
                selectionRect.height
            );

            EditorGUI.DrawRect(stripeRect, ZebraColor);
        }

        private static void DrawHierarchyLines(Rect selectionRect, int depth, bool hasChildren)
        {
            Color lineColor = LineColor;

            if (depth == 0)
            {
                float rootVerticalX = 38f; // Fixed position for root vertical line
                               

                Rect verticalRect = new Rect(
                    rootVerticalX,
                    selectionRect.y,
                    LINE_WIDTH,
                    selectionRect.height + LINE_WIDTH
                );
                EditorGUI.DrawRect(verticalRect, lineColor);
                
                float horizontalWidth = hasChildren 
                    ? selectionRect.x - rootVerticalX - 14f  // Leave space for fold-out arrow
                    : selectionRect.x - rootVerticalX;       // Extend to icon
                    
                Rect horizontalRect = new Rect(
                    rootVerticalX,
                    selectionRect.y + selectionRect.height * 0.5f,
                    horizontalWidth,
                    LINE_WIDTH
                );
                EditorGUI.DrawRect(horizontalRect, lineColor);
                return;
            }

            for (int i = 0; i < depth; i++)
            {
                float x = HIERARCHY_BASE_OFFSET + (i * HIERARCHY_INDENT) - 7f;

                Rect lineRect = new Rect(
                    x,
                    selectionRect.y,
                    LINE_WIDTH,
                    selectionRect.height
                );

                EditorGUI.DrawRect(lineRect, lineColor);
            }

            float horizontalX = HIERARCHY_BASE_OFFSET + ((depth - 1) * HIERARCHY_INDENT) - 7f;
            float connectorWidth = hasChildren ? HIERARCHY_INDENT * 0.5f : HIERARCHY_INDENT + 5f;
            
            Rect connectorRect = new Rect(
                horizontalX,
                selectionRect.y + selectionRect.height * 0.5f,
                connectorWidth,
                LINE_WIDTH
            );
            EditorGUI.DrawRect(connectorRect, lineColor);
        }

        private static void DrawProjectLines(Rect selectionRect, int depth, bool hasChildren)
        {
            Color lineColor = LineColor;
            
            float baseOffset = 22f;
            float indent = 14f;
            
            if (depth <= 1)
            {
                float rootVerticalX = 8f; // Fixed position for root vertical line
                
                Rect verticalRect = new Rect(
                    rootVerticalX,
                    selectionRect.y,
                    LINE_WIDTH,
                    selectionRect.height * 0.5f + LINE_WIDTH
                );
                EditorGUI.DrawRect(verticalRect, lineColor);
                
                float horizontalWidth = hasChildren 
                    ? selectionRect.x - rootVerticalX - 14f
                    : selectionRect.x - rootVerticalX;
                    
                Rect rootHorizontalRect = new Rect(
                    rootVerticalX,
                    selectionRect.y + selectionRect.height * 0.5f,
                    horizontalWidth,
                    LINE_WIDTH
                );
                EditorGUI.DrawRect(rootHorizontalRect, lineColor);
                return;
            }

            int visibleDepth = depth - 1;

            for (int i = 0; i < visibleDepth; i++)
            {
                float x = baseOffset + (i * indent);

                Rect lineRect = new Rect(
                    x,
                    selectionRect.y,
                    LINE_WIDTH,
                    selectionRect.height
                );

                EditorGUI.DrawRect(lineRect, lineColor);
            }

            float horizontalX = baseOffset + ((visibleDepth - 1) * indent);
            float connectorWidth = hasChildren ? indent * 0.5f : indent + 9f;
            Rect horizontalRect = new Rect(
                horizontalX,
                selectionRect.y + selectionRect.height * 0.5f,
                connectorWidth,
                LINE_WIDTH
            );
            EditorGUI.DrawRect(horizontalRect, lineColor);
        }

        private static int GetHierarchyDepth(Transform transform)
        {
            int depth = 0;
            Transform parent = transform.parent;
            while (parent != null)
            {
                depth++;
                parent = parent.parent;
            }
            return depth;
        }

        private static int GetProjectDepth(string path)
        {
            if (string.IsNullOrEmpty(path))
                return 0;

            int count = 0;
            foreach (char c in path)
            {
                if (c == '/')
                    count++;
            }
            return count;
        }

        private static bool HasSubfolders(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
                return false;

            string[] subfolders = AssetDatabase.GetSubFolders(folderPath);
            return subfolders != null && subfolders.Length > 0;
        }
    }

    public static class EditorVisualTweaksSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            var provider = new SettingsProvider("Preferences/Editor Visuals", SettingsScope.User)
            {
                label = "Editor Visual Tweaks",
                guiHandler = DrawPreferencesGUI,
                keywords = new HashSet<string>(new[]
                {
                    "Hierarchy", "Project", "Zebra", "Stripe", "Lines", "Visual"
                })
            };

            return provider;
        }

        private static void DrawPreferencesGUI(string searchContext)
        {
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Hierarchy Window", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorVisualTweaks.HierarchyZebra = EditorGUILayout.Toggle(
                new GUIContent("Zebra Stripes", "Alternate row colors for better readability"),
                EditorVisualTweaks.HierarchyZebra
            );

            EditorVisualTweaks.HierarchyLines = EditorGUILayout.Toggle(
                new GUIContent("Hierarchy Lines", "Draw vertical lines showing parent-child relationships"),
                EditorVisualTweaks.HierarchyLines
            );

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Project Window", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorVisualTweaks.ProjectZebra = EditorGUILayout.Toggle(
                new GUIContent("Zebra Stripes", "Alternate row colors for better readability (list view only)"),
                EditorVisualTweaks.ProjectZebra
            );

            EditorVisualTweaks.ProjectLines = EditorGUILayout.Toggle(
                new GUIContent("Hierarchy Lines", "Draw vertical lines showing folder depth (list view only)"),
                EditorVisualTweaks.ProjectLines
            );

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(20);

            EditorGUILayout.HelpBox(
                "Zebra stripes and hierarchy lines only apply to list views, not icon/grid views.",
                MessageType.Info
            );
        }
    }
}
#pragma warning restore CS0618