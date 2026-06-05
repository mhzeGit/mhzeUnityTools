using System.Collections.Generic;
using UnityEditor;

namespace mhze.HierarchyContextMenu
{
    static class ProjectItemIndexer
    {
        private static readonly HashSet<string> ExcludedDisplayNames = new()
        {
            "Show in Explorer",
            "Open",
            "Open Scene Additive",
            "Delete",
            "Rename",
            "Copy Path",
            "Properties...",
            "Reimport",
            "Reimport All",
            "Import New Asset...",
            "Import Package/Custom Package...",
            "Export Package...",
            "Find References In Scene",
            "Select Dependencies",
            "Select Previous",
            "Refresh",
        };

        private static List<HierarchyMenuItem> _items;
        private static bool _indexed;

        public static IReadOnlyList<HierarchyMenuItem> Items
        {
            get
            {
                EnsureIndexed();
                return _items;
            }
        }

        public static void EnsureIndexed()
        {
            if (_indexed)
                return;

            _items = new List<HierarchyMenuItem>();
            var visited = new HashSet<string>();

            var allPaths = Unsupported.GetSubmenus("Assets");
            foreach (var path in allPaths)
            {
                if (!visited.Add(path))
                    continue;

                if (!path.StartsWith("Assets/"))
                    continue;

                var subItems = Unsupported.GetSubmenus(path);
                if (subItems.Length > 0)
                    continue;

                var displayName = path.Substring("Assets/".Length);

                if (ExcludedDisplayNames.Contains(displayName))
                    continue;

                _items.Add(new HierarchyMenuItem
                {
                    MenuPath = path,
                    DisplayName = displayName
                });
            }

            _indexed = true;
        }

        public static void Reset()
        {
            _indexed = false;
            _items = null;
        }
    }
}
