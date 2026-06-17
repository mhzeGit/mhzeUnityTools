using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace mhze.ShortcutCommander
{
    static class ShortcutActions
    {
        [Shortcut("Shortcut Commander/Add Component", KeyCode.C, ShortcutModifiers.Action | ShortcutModifiers.Shift)]
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
