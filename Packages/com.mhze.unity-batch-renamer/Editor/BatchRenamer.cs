using UnityEditor;
using UnityEngine;

namespace mhze.BatchRenamer
{
    static class BatchRenamer
    {
        [MenuItem("Assets/Batch Rename", false, 20)]
        static void OpenFromProject()
        {
            var objects = Selection.objects;
            BatchRenamerWindow.ShowWindow(objects);
        }

        [MenuItem("GameObject/Batch Rename", false, 20)]
        static void OpenFromHierarchy()
        {
            var objects = Selection.objects;
            BatchRenamerWindow.ShowWindow(objects);
        }

        [MenuItem("Assets/Batch Rename", true)]
        static bool ValidateOpenFromProject()
        {
            return Selection.objects != null && Selection.objects.Length > 0;
        }

        [MenuItem("GameObject/Batch Rename", true)]
        static bool ValidateOpenFromHierarchy()
        {
            return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
        }
    }
}
