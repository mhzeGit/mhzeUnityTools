using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace mhze.EditorCustomCommands
{
    static class EditorCustomCommands
    {
        [Shortcut("Editor Custom Commands/Add Component", KeyCode.C, ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        private static void AddComponentShortcut()
        {
            if (Selection.gameObjects.Length == 0)
            {
                EditorApplication.Beep();
                return;
            }

            AddComponentWindow.Open();
        }
    }
}
