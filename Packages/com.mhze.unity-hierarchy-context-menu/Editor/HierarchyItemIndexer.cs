using System.Collections.Generic;
using UnityEditor;

namespace mhze.HierarchyContextMenu
{
    static class HierarchyItemIndexer
    {
        private static readonly HashSet<string> ExcludedDisplayNames = new()
        {
            "Center On Children",
            "Make Parent",
            "Clear Parent",
            "Set as first sibling",
            "Set as last sibling",
            "Move To View",
            "Align With View",
            "Align View to Selected",
            "Toggle Active State",
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

            var allPaths = Unsupported.GetSubmenus("GameObject");
            foreach (var path in allPaths)
            {
                if (!visited.Add(path))
                    continue;

                if (!path.StartsWith("GameObject/"))
                    continue;

                var subItems = Unsupported.GetSubmenus(path);
                if (subItems.Length > 0)
                    continue;

                var displayName = path.Substring("GameObject/".Length);

                if (ExcludedDisplayNames.Contains(displayName))
                    continue;

                _items.Add(new HierarchyMenuItem
                {
                    MenuPath = path,
                    DisplayName = displayName,
                    ShortcutText = ShortcutResolver.GetShortcut(path)
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

    struct HierarchyMenuItem
    {
        public string MenuPath;
        public string DisplayName;
        public string ShortcutText;
    }
}
