using System;
using System.Collections.Generic;

namespace mhze.HierarchyContextMenu
{
    class MenuNode
    {
        public string Name;
        public string FullPath;
        public string MenuPath;
        public string ShortcutText;
        public List<MenuNode> Children = new List<MenuNode>();
        public bool IsCategory => Children.Count > 0;
        public bool IsLeaf => MenuPath != null;
    }

    class BackItem { }

    class SeparatorItem { }

    class SpecialActionItem
    {
        public string DisplayName;
        public string ShortcutText;
        public Action Action;
        public bool Enabled = true;
    }

    class SpecialSubmenuItem
    {
        public string DisplayName;
        public List<SpecialActionItem> Children = new List<SpecialActionItem>();
        public bool Enabled = true;
    }
}
