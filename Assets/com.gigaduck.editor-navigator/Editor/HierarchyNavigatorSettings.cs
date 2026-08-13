using UnityEngine;
using UnityEditor;

namespace HierarchyNavigator
{
    public class HierarchyNavigatorSettings : EditorWindow
    {
        private Vector2 scrollPosition;
        
        [MenuItem("Tools/Hierarchy Navigator/Settings && Help", false, 300)]
        public static void ShowWindow()
        {
            HierarchyNavigatorSettings window = GetWindow<HierarchyNavigatorSettings>();
            window.titleContent = new GUIContent("Hierarchy Navigator");
            window.minSize = new Vector2(350, 250);
            window.Show();
        }
        
        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.Space(10);
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("Hierarchy Navigator", headerStyle);
            EditorGUILayout.Space(5);

            EditorGUILayout.HelpBox(
                "A tool for quickly reorganizing GameObjects in the hierarchy " +
                "and moving assets in the Project window using keyboard shortcuts.",
                MessageType.Info
            );
            
            EditorGUILayout.Space(15);

            EditorGUILayout.LabelField("Hierarchy Shortcuts", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            DrawShortcutRow("Ctrl + Shift + Up", "Move selected object(s) UP in hierarchy");
            DrawShortcutRow("Ctrl + Shift + Down", "Move selected object(s) DOWN in hierarchy");
            DrawShortcutRow("Ctrl + Shift + Left", "Unparent (move OUT of parent)");
            DrawShortcutRow("Ctrl + Shift + Right", "Parent to sibling above (move IN)");
            
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(
                "- Works with multiple selected objects\n" +
                "- All operations support Undo (Ctrl+Z)\n" +
                "- Move In: Parents to the sibling directly above\n" +
                "- Move Out: Moves object to same level as current parent",
                MessageType.None
            );
            
            EditorGUILayout.Space(15);

            EditorGUILayout.LabelField("Project Move Mode", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            EditorGUILayout.HelpBox(
                "Move Mode is a two-step workflow for moving assets between folders:\n\n" +
                "1. Select asset(s) in the Project window\n" +
                "2. Press Ctrl+Shift+Up or Down to enter Move Mode\n" +
                "3. A green-highlighted target folder appears\n" +
                "4. Up/Down browse sibling folders at the same level\n" +
                "5. Right goes INTO the highlighted folder (browse children)\n" +
                "6. Left goes OUT one level (browse parent's siblings)\n" +
                "7. Press Enter to move, or Escape to cancel",
                MessageType.Info
            );
            
            EditorGUILayout.Space(5);
            
            DrawShortcutRow("Ctrl + Shift + Up", "Previous sibling folder");
            DrawShortcutRow("Ctrl + Shift + Down", "Next sibling folder");
            DrawShortcutRow("Ctrl + Shift + Right", "Go into folder (browse children)");
            DrawShortcutRow("Ctrl + Shift + Left", "Go out one level (browse parent)");
            DrawShortcutRow("Enter", "Confirm move to highlighted folder");
            DrawShortcutRow("Escape", "Cancel move mode");
            
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(
                "- Works with files, scripts, folders, and any asset\n" +
                "- Multi-selection supported\n" +
                "- Name conflicts are handled automatically\n" +
                "- File moves do NOT support Undo\n" +
                "- Clicking elsewhere also cancels move mode",
                MessageType.None
            );
            
            EditorGUILayout.Space(15);

            EditorGUILayout.LabelField("Tips", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            EditorGUILayout.HelpBox(
                "- Shortcuts are context-aware: they apply to the Hierarchy " +
                "window or Project window depending on which is focused.\n" +
                "- All commands are also available via: Tools -> Hierarchy Navigator",
                MessageType.None
            );
            
            EditorGUILayout.EndScrollView();
        }
        
        private void DrawShortcutRow(string shortcut, string description)
        {
            EditorGUILayout.BeginHorizontal();
            
            GUIStyle shortcutStyle = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Bold,
                fixedWidth = 140
            };
            
            EditorGUILayout.LabelField(shortcut, shortcutStyle, GUILayout.Width(140));
            EditorGUILayout.LabelField(description);
            
            EditorGUILayout.EndHorizontal();
        }
    }
}
