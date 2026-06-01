using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace mhze.HierarchyContextMenu
{
    class MenuNode
    {
        public string Name;
        public string FullPath;
        public string MenuPath;
        public List<MenuNode> Children = new List<MenuNode>();
        public bool IsCategory => Children.Count > 0;
        public bool IsLeaf => MenuPath != null;
    }

    class BackItem { }

    class SeparatorItem { }

    class SpecialActionItem
    {
        public string DisplayName;
        public Action Action;
        public bool Enabled = true;
    }

    class SpecialSubmenuItem
    {
        public string DisplayName;
        public List<SpecialActionItem> Children = new List<SpecialActionItem>();
        public bool Enabled = true;
    }

    class HierarchyContextMenuWindow : EditorWindow
    {
        private ListView _listView;
        private VisualElement _contentContainer;
        private TextField _searchField;
        private IList _currentItems;

        private List<HierarchyMenuItem> _allItems;
        private List<HierarchyMenuItem> _filteredItems;
        private MenuNode _rootNode;

        private bool _isSearching;
        private string _lastSearchText = "";
        private bool _ready;
        private int _selectedIndex = -1;

        private static HierarchyContextMenuWindow _instance;
        public static bool IsOpen => _instance != null;

        private readonly struct ItemIconInfo
        {
            public readonly string IconName;
            public readonly Color? TintColor;

            public ItemIconInfo(string iconName, Color? tintColor = null)
            {
                IconName = iconName;
                TintColor = tintColor;
            }
        }

        private static readonly Dictionary<string, ItemIconInfo> SpecialItemIcons = new()
        {
            { "Cut", new ItemIconInfo("editicon.sml") },
            { "Copy", new ItemIconInfo("SceneLoadIn") },
            { "Paste", new ItemIconInfo("SceneLoadOut") },
            { "Paste Special", new ItemIconInfo("editicon.sml") },
            { "Paste As Child", new ItemIconInfo("editicon.sml") },
            { "Paste As Sibling", new ItemIconInfo("editicon.sml") },
            { "Rename", new ItemIconInfo("editicon.sml") },
            { "Duplicate", new ItemIconInfo("editicon.sml") },
            { "Delete", new ItemIconInfo("TreeEditor.Trash", new Color(1f, 0.45f, 0.45f)) },
            { "Select All", new ItemIconInfo("UnityEditor.SceneHierarchyWindow") },
            { "Deselect All", new ItemIconInfo("UnityEditor.SceneHierarchyWindow") },
            { "Invert Selection", new ItemIconInfo("UnityEditor.SceneHierarchyWindow") },
            { "Select Children", new ItemIconInfo("GameObject Icon") },
            { "Find References in Scene", new ItemIconInfo("Search Icon") },
            { "Set as Default Parent", new ItemIconInfo("UnityEditor.SceneHierarchyWindow") },
            { "Prefab", new ItemIconInfo("Prefab Icon") },
            { "Open Asset in Context", new ItemIconInfo("Prefab Icon") },
            { "Open Asset in Isolation", new ItemIconInfo("Prefab Icon") },
            { "Select Asset", new ItemIconInfo("Prefab Icon") },
            { "Select Root", new ItemIconInfo("Prefab Icon") },
            { "Replace...", new ItemIconInfo("Prefab Icon") },
            { "Replace and Keep Overrides...", new ItemIconInfo("Prefab Icon") },
            { "Unpack", new ItemIconInfo("Prefab Icon") },
            { "Unpack Completely", new ItemIconInfo("Prefab Icon") },
            { "Remove Unused Overrides...", new ItemIconInfo("Prefab Icon") },
        };

        private const float WindowWidth = 420f;
        private const float ItemHeight = 22f;
        private const float SubmenuWidth = 240f;
        private const long SubmenuDelayMs = 120;

        private MenuNode _currentSubmenuCategory;
        private IVisualElementScheduledItem _submenuSchedule;
        private bool _suppressHoverUntilMouseMove;

        private bool IsPrefabContext
        {
            get
            {
                var go = Selection.activeGameObject;
                if (go == null) return false;
                return PrefabUtility.IsPartOfPrefabInstance(go);
            }
        }

        public static void Show(Vector2 screenPoint, float desiredHeight)
        {
            if (_instance != null)
            {
                _instance.Close();
            }

            var buttonRect = new Rect(screenPoint.x, screenPoint.y, 1, 1);
            _instance = CreateInstance<HierarchyContextMenuWindow>();
            _instance.ShowAsDropDown(buttonRect, new Vector2(WindowWidth, desiredHeight));
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                HideSubmenu();
            }
        }

        private void CreateGUI()
        {
            _allItems = new List<HierarchyMenuItem>(HierarchyItemIndexer.Items);
            _filteredItems = new List<HierarchyMenuItem>();

            BuildTree();

            rootVisualElement.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            rootVisualElement.style.borderTopLeftRadius = 8;
            rootVisualElement.style.borderTopRightRadius = 8;
            rootVisualElement.style.borderBottomLeftRadius = 8;
            rootVisualElement.style.borderBottomRightRadius = 8;
            rootVisualElement.style.paddingLeft = 4;
            rootVisualElement.style.paddingRight = 4;
            rootVisualElement.style.paddingTop = 4;
            rootVisualElement.style.paddingBottom = 4;

            rootVisualElement.style.borderTopWidth = 1;
            rootVisualElement.style.borderLeftWidth = 1;
            rootVisualElement.style.borderRightWidth = 1;
            rootVisualElement.style.borderBottomWidth = 1;
            rootVisualElement.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f);
            rootVisualElement.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f);
            rootVisualElement.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f);
            rootVisualElement.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f);

            rootVisualElement.RegisterCallback<PointerMoveEvent>(evt =>
            {
                _suppressHoverUntilMouseMove = false;
            });

            BuildSearchField();

            _contentContainer = new VisualElement();
            _contentContainer.style.flexGrow = 1;
            _contentContainer.style.marginLeft = 4;
            _contentContainer.style.marginRight = 4;
            _contentContainer.style.marginBottom = 4;
            _contentContainer.style.paddingTop = 0;
            _contentContainer.style.paddingBottom = 0;
            rootVisualElement.Add(_contentContainer);

            ShowRootLevel();
            _ready = true;

            _searchField.Focus();
        }

        private void BuildTree()
        {
            _rootNode = new MenuNode { Name = "GameObject", Children = new List<MenuNode>() };

            foreach (var item in _allItems)
            {
                var segments = item.DisplayName.Split('/');
                var current = _rootNode;

                for (int i = 0; i < segments.Length; i++)
                {
                    var child = current.Children.FirstOrDefault(c => c.Name == segments[i]);
                    if (child == null)
                    {
                        child = new MenuNode
                        {
                            Name = segments[i],
                            FullPath = string.Join("/", segments.Take(i + 1)),
                            Children = new List<MenuNode>()
                        };
                        current.Children.Add(child);
                    }

                    if (i == segments.Length - 1)
                        child.MenuPath = item.MenuPath;

                    current = child;
                }
            }
        }

        private void ShowRootLevel()
        {
            _isSearching = false;
            _lastSearchText = "";
            _selectedIndex = -1;

            if (_listView != null && _listView.parent != null)
                rootVisualElement.Remove(_listView);

            if (_contentContainer.parent == null)
                rootVisualElement.Add(_contentContainer);

            _currentItems = BuildRootLevelItems();
            RebuildContentContainer();

            HideSubmenu();
            ResizeWindowToFit(_currentItems.Count);
            _searchField?.Focus();
        }

        private List<object> BuildRootLevelItems()
        {
            var selectionValid = Selection.gameObjects.Length > 0;
            var activeValid = Selection.activeGameObject != null;

            var items = new List<object>();

            items.Add(new SpecialActionItem { DisplayName = "Cut", Action = CutSelection, Enabled = selectionValid });
            items.Add(new SpecialActionItem { DisplayName = "Copy", Action = CopySelection, Enabled = selectionValid });
            items.Add(new SpecialActionItem { DisplayName = "Paste", Action = PasteAsChildOfClicked });
            items.Add(new SpecialSubmenuItem
            {
                DisplayName = "Paste Special",
                Children = new List<SpecialActionItem>
                {
                    new SpecialActionItem { DisplayName = "Paste As Child", Action = PasteAsChild },
                    new SpecialActionItem { DisplayName = "Paste As Sibling", Action = PasteAsSibling },
                }
            });
            items.Add(new SpecialActionItem { DisplayName = "Rename", Action = RenameSelected, Enabled = selectionValid });
            items.Add(new SpecialActionItem { DisplayName = "Duplicate", Action = DuplicateSelection, Enabled = selectionValid });
            items.Add(new SpecialActionItem { DisplayName = "Delete", Action = DeleteSelection, Enabled = selectionValid });

            items.Add(new SeparatorItem());

            items.Add(new SpecialActionItem { DisplayName = "Select All", Action = SelectAll });
            items.Add(new SpecialActionItem { DisplayName = "Deselect All", Action = DeselectAll, Enabled = activeValid });
            items.Add(new SpecialActionItem { DisplayName = "Invert Selection", Action = InvertSelection, Enabled = selectionValid });
            items.Add(new SpecialActionItem { DisplayName = "Select Children", Action = SelectChildren, Enabled = selectionValid });

            items.Add(new SeparatorItem());

            items.Add(new SpecialActionItem { DisplayName = "Find References in Scene", Action = FindReferencesInScene, Enabled = activeValid });
            items.Add(new SpecialActionItem { DisplayName = "Set as Default Parent", Action = SetAsDefaultParent, Enabled = activeValid });

            items.Add(new SeparatorItem());

            if (IsPrefabContext)
            {
                items.Add(new SpecialSubmenuItem
                {
                    DisplayName = "Prefab",
                    Children = new List<SpecialActionItem>
                    {
                        new SpecialActionItem { DisplayName = "Open Asset in Context", Action = OpenAssetInContext },
                        new SpecialActionItem { DisplayName = "Open Asset in Isolation", Action = OpenAssetInIsolation },
                        new SpecialActionItem { DisplayName = "Select Asset", Action = SelectPrefabAsset },
                        new SpecialActionItem { DisplayName = "Select Root", Action = SelectPrefabRoot },
                        new SpecialActionItem { DisplayName = "Replace...", Action = ReplacePrefab },
                        new SpecialActionItem { DisplayName = "Replace and Keep Overrides...", Action = ReplacePrefabKeepOverrides },
                        new SpecialActionItem { DisplayName = "Unpack", Action = UnpackPrefab },
                        new SpecialActionItem { DisplayName = "Unpack Completely", Action = UnpackPrefabCompletely },
                        new SpecialActionItem { DisplayName = "Remove Unused Overrides...", Action = RemoveUnusedOverrides },
                    }
                });
                items.Add(new SeparatorItem());
            }

            items.AddRange(_rootNode.Children);

            return items;
        }

        private void RebuildContentContainer()
        {
            _contentContainer.Clear();
            for (int i = 0; i < _currentItems.Count; i++)
            {
                var element = MakeItem();
                BindItem(element, i);
                _contentContainer.Add(element);
            }
        }

        private void ShowSpecialSubmenuLevel(SpecialSubmenuItem submenu)
        {
            _isSearching = false;
            _lastSearchText = "";
            _selectedIndex = -1;

            if (_listView != null && _listView.parent != null)
                rootVisualElement.Remove(_listView);

            var items = new List<object>();
            items.Add(new BackItem());
            items.AddRange(submenu.Children);
            _currentItems = items;
            RebuildContentContainer();

            HideSubmenu();
            ResizeWindowToFit(_currentItems.Count);
            _searchField?.Focus();
        }

        private void ShowCategoryLevel(MenuNode node)
        {
            _isSearching = false;
            _selectedIndex = -1;

            if (_listView != null && _listView.parent != null)
                rootVisualElement.Remove(_listView);

            var items = new List<object>();
            items.Add(new BackItem());
            items.AddRange(node.Children);
            _currentItems = items;
            RebuildContentContainer();

            ResizeWindowToFit(_currentItems.Count);
            _searchField?.Focus();
        }

        private float CalculateContentHeight(int itemCount)
        {
            return 36f + (itemCount * ItemHeight) + 16f;
        }

        private void ResizeWindowToFit(int itemCount)
        {
            if (!_ready)
                return;

            var height = CalculateContentHeight(itemCount);
            height = Mathf.Max(height, 60f);
            position = new Rect(position.x, position.y, WindowWidth, height);
        }

        private void SetScrollBarVisibility(bool visible)
        {
            if (_listView == null)
                return;
            var scrollView = _listView.Q<ScrollView>();
            if (scrollView != null)
            {
                scrollView.verticalScrollerVisibility = visible
                    ? ScrollerVisibility.Auto
                    : ScrollerVisibility.Hidden;
            }
        }

        private void BuildSearchField()
        {
            var searchContainer = new VisualElement();
            searchContainer.style.flexDirection = FlexDirection.Row;
            searchContainer.style.alignItems = Align.Center;
            searchContainer.style.marginLeft = 4;
            searchContainer.style.marginRight = 4;
            searchContainer.style.marginTop = 4;
            searchContainer.style.marginBottom = 4;
            searchContainer.style.backgroundColor = new Color(0.17f, 0.17f, 0.17f);
            searchContainer.style.borderTopColor = new Color(0.28f, 0.28f, 0.28f);
            searchContainer.style.borderLeftColor = new Color(0.28f, 0.28f, 0.28f);
            searchContainer.style.borderRightColor = new Color(0.28f, 0.28f, 0.28f);
            searchContainer.style.borderBottomColor = new Color(0.28f, 0.28f, 0.28f);
            searchContainer.style.borderTopWidth = 1;
            searchContainer.style.borderLeftWidth = 1;
            searchContainer.style.borderRightWidth = 1;
            searchContainer.style.borderBottomWidth = 1;
            searchContainer.style.borderTopLeftRadius = 6;
            searchContainer.style.borderTopRightRadius = 6;
            searchContainer.style.borderBottomLeftRadius = 6;
            searchContainer.style.borderBottomRightRadius = 6;
            searchContainer.style.paddingLeft = 8;
            searchContainer.style.paddingRight = 4;
            searchContainer.style.minHeight = 26;

            var searchIcon = new Image();
            var iconTex = MenuIcons.Load("Search Icon");
            searchIcon.image = iconTex;
            searchIcon.style.width = 14;
            searchIcon.style.height = 14;
            searchIcon.style.marginRight = 4;
            searchIcon.style.flexShrink = 0;
            searchIcon.style.unityBackgroundImageTintColor = new Color(0.55f, 0.55f, 0.55f);
            if (iconTex == null)
                searchIcon.style.display = DisplayStyle.None;
            searchContainer.Add(searchIcon);

            _searchField = new TextField();
            _searchField.style.flexGrow = 1;
            _searchField.style.flexShrink = 1;
            _searchField.style.borderTopWidth = 0;
            _searchField.style.borderLeftWidth = 0;
            _searchField.style.borderRightWidth = 0;
            _searchField.style.borderBottomWidth = 0;
            _searchField.style.backgroundColor = Color.clear;
            _searchField.style.paddingLeft = 0;
            _searchField.style.paddingRight = 0;
            _searchField.style.paddingTop = 3;
            _searchField.style.paddingBottom = 3;
            _searchField.style.fontSize = 13;
            _searchField.style.color = new Color(0.85f, 0.85f, 0.85f);
            _searchField.style.unityFontStyleAndWeight = FontStyle.Normal;
            _searchField.selectAllOnFocus = true;

            var textElement = _searchField.Q<TextElement>();
            if (textElement != null)
            {
                textElement.style.backgroundColor = Color.clear;
                textElement.style.color = new Color(0.85f, 0.85f, 0.85f);
            }

            var inputContainer = _searchField.Q(className: TextField.inputUssClassName);
            if (inputContainer != null)
            {
                inputContainer.style.borderTopWidth = 0;
                inputContainer.style.borderLeftWidth = 0;
                inputContainer.style.borderRightWidth = 0;
                inputContainer.style.borderBottomWidth = 0;
                inputContainer.style.backgroundColor = Color.clear;
                inputContainer.style.paddingTop = 0;
                inputContainer.style.paddingBottom = 0;
                inputContainer.style.paddingLeft = 0;
                inputContainer.style.paddingRight = 0;
                inputContainer.style.minHeight = 0;
            }

            _searchField.RegisterValueChangedCallback(OnSearchChanged);
            _searchField.RegisterCallback<KeyDownEvent>(OnSearchKeyDown, TrickleDown.TrickleDown);

            searchContainer.Add(_searchField);
            rootVisualElement.Add(searchContainer);
        }

        private Vector2 GetSubmenuScreenPos(VisualElement hoveredItem)
        {
            var posInRoot = hoveredItem.ChangeCoordinatesTo(rootVisualElement, Vector2.zero);
            float left = position.x + rootVisualElement.resolvedStyle.width + 4f;
            float top = position.y + posInRoot.y;
            return new Vector2(left, top);
        }

        private void BuildListView()
        {
            _listView = new ListView(new List<object>(), ItemHeight, MakeItem, BindItem);
            _listView.style.flexGrow = 1;
            _listView.style.marginLeft = 4;
            _listView.style.marginRight = 4;
            _listView.style.marginBottom = 4;
            _listView.style.paddingTop = 0;
            _listView.style.paddingBottom = 0;
            _listView.style.backgroundColor = new Color(0, 0, 0, 0);
            _listView.selectionType = SelectionType.Single;
            _listView.focusable = true;

            _listView.RegisterCallback<KeyDownEvent>(OnListKeyDown, TrickleDown.TrickleDown);

            var scrollView = _listView.Q<ScrollView>();
            if (scrollView != null)
            {
                scrollView.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                scrollView.mouseWheelScrollSize = ItemHeight;
                scrollView.style.paddingTop = 0;
                scrollView.style.paddingBottom = 0;
                scrollView.style.marginTop = 0;
                scrollView.style.marginBottom = 0;
                var viewport = scrollView.Q<VisualElement>(className: "unity-scroll-view__content-viewport");
                if (viewport != null)
                {
                    viewport.style.paddingTop = 0;
                    viewport.style.paddingBottom = 0;
                    viewport.style.marginTop = 0;
                    viewport.style.marginBottom = 0;
                    viewport.style.overflow = Overflow.Visible;
                }
                scrollView.contentContainer.style.overflow = Overflow.Visible;
            }
        }

        private VisualElement MakeItem()
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.paddingLeft = 10;
            container.style.paddingRight = 10;
            container.style.paddingTop = 2;
            container.style.paddingBottom = 2;
            container.style.minHeight = ItemHeight;
            container.style.backgroundColor = new Color(0, 0, 0, 0);

            var icon = new Image();
            icon.name = "item-icon";
            icon.style.width = 16;
            icon.style.height = 16;
            icon.style.marginRight = 6;
            icon.style.flexShrink = 0;
            icon.scaleMode = ScaleMode.ScaleToFit;
            icon.style.display = DisplayStyle.None;
            container.Add(icon);

            var label = new Label();
            label.name = "item-label";
            label.style.fontSize = 13;
            label.style.color = new Color(0.85f, 0.85f, 0.85f);
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.flexShrink = 1;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.flexGrow = 1;
            container.Add(label);

            var arrow = new Label();
            arrow.name = "item-arrow";
            arrow.text = "\u25B8";
            arrow.style.fontSize = 12;
            arrow.style.color = new Color(0.55f, 0.55f, 0.55f);
            arrow.style.marginLeft = 4;
            arrow.style.display = DisplayStyle.None;
            arrow.style.unityTextAlign = TextAnchor.MiddleRight;
            container.Add(arrow);

            container.RegisterCallback<PointerEnterEvent>(evt =>
            {
                var idx = (int)container.userData;
                if (idx < 0 || idx >= _currentItems.Count)
                    return;

                if (_currentItems[idx] is SeparatorItem || idx == _selectedIndex)
                    return;

                if (IsItemDisabled(idx))
                    return;

                if (_suppressHoverUntilMouseMove)
                    return;

                var oldIdx = _selectedIndex;
                _selectedIndex = idx;

                if (oldIdx >= 0)
                {
                    var oldElement = FindItemVisualElement(oldIdx);
                    if (oldElement != null)
                        oldElement.style.backgroundColor = new Color(0, 0, 0, 0);
                }

                container.style.backgroundColor = new Color(0.22f, 0.42f, 0.75f);
            });

            container.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    evt.StopPropagation();
                    var idx = (int)container.userData;

                    if (idx < 0 || idx >= _currentItems.Count)
                        return;

                    var clickedItem = _currentItems[idx];

                    if (clickedItem is SeparatorItem)
                        return;

                    if (IsItemDisabled(idx))
                        return;

                    _selectedIndex = idx;

                    if (clickedItem is BackItem)
                    {
                        ShowRootLevel();
                    }
                    else if (clickedItem is MenuNode node)
                    {
                        if (node.IsCategory)
                            ShowSubmenu(node, container);
                        else
                            ExecutePath(node.MenuPath);
                    }
                    else if (clickedItem is HierarchyMenuItem menuItem)
                    {
                        ExecutePath(menuItem.MenuPath);
                    }
                    else if (clickedItem is SpecialActionItem special)
                    {
                        special.Action?.Invoke();
                    }
                    else if (clickedItem is SpecialSubmenuItem submenu)
                    {
                        if (submenu.DisplayName == "Prefab")
                            ShowPrefabSubmenu(submenu, container);
                        else
                            ShowSpecialSubmenuLevel(submenu);
                    }
                }
            }, TrickleDown.TrickleDown);

            return container;
        }

        private void BindItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _currentItems.Count)
                return;

            element.userData = index;

            var label = element.Q<Label>("item-label");
            var arrow = element.Q<Label>("item-arrow");
            var icon = element.Q<Image>("item-icon");

            element.style.minHeight = ItemHeight;
            element.style.paddingTop = 2;
            element.style.paddingBottom = 2;
            element.style.paddingLeft = 10;
            element.style.paddingRight = 10;
            element.style.borderTopWidth = 0;
            element.style.marginTop = 0;
            element.style.marginBottom = 0;
            element.style.backgroundColor = new Color(0, 0, 0, 0);

            var item = _currentItems[index];

            if (item is BackItem)
            {
                label.text = "\u2190  Back";
                label.style.color = new Color(0.7f, 0.7f, 0.7f);
                arrow.style.display = DisplayStyle.None;
                icon.style.display = DisplayStyle.None;
                ApplySelectionStyle(element, index);
                UnregisterHoverEvents(element);
                return;
            }

            if (item is SeparatorItem)
            {
                label.text = "";
                arrow.style.display = DisplayStyle.None;
                icon.style.display = DisplayStyle.None;
                element.style.minHeight = 8;
                element.style.paddingTop = 0;
                element.style.paddingBottom = 0;
                element.style.paddingLeft = 0;
                element.style.paddingRight = 0;
                element.style.borderTopWidth = 1;
                element.style.borderTopColor = new Color(0.3f, 0.3f, 0.3f);
                element.style.marginTop = 4;
                element.style.marginBottom = 4;
                UnregisterHoverEvents(element);
                return;
            }

            if (item is SpecialActionItem specialAction)
            {
                label.text = specialAction.DisplayName;
                label.style.color = specialAction.Enabled ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.4f, 0.4f, 0.4f);
                arrow.style.display = DisplayStyle.None;
                ApplyIcon(icon, specialAction.DisplayName, specialAction.Enabled);
                ApplySelectionStyle(element, index);
                UnregisterHoverEvents(element);
                return;
            }

            if (item is SpecialSubmenuItem submenuItem)
            {
                label.text = submenuItem.DisplayName;
                label.style.color = submenuItem.Enabled ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.4f, 0.4f, 0.4f);
                arrow.style.display = DisplayStyle.Flex;
                ApplyIcon(icon, submenuItem.DisplayName, submenuItem.Enabled);
                ApplySelectionStyle(element, index);
                UnregisterHoverEvents(element);
                if (submenuItem.DisplayName == "Prefab")
                    RegisterPrefabHoverEvents(element, submenuItem);
                return;
            }

            if (item is MenuNode node)
            {
                label.text = node.Name;
                label.style.color = new Color(0.85f, 0.85f, 0.85f);
                arrow.style.display = node.IsCategory ? DisplayStyle.Flex : DisplayStyle.None;
                ApplyMenuIcon(icon, node.Name, node.IsCategory);

                ApplySelectionStyle(element, index);

                UnregisterHoverEvents(element);
                RegisterHoverEvents(element, node);
            }
            else if (item is HierarchyMenuItem menuItem)
            {
                var displayText = menuItem.DisplayName.Replace("/", " \u25B8 ");
                label.text = displayText;
                label.style.color = new Color(0.85f, 0.85f, 0.85f);
                arrow.style.display = DisplayStyle.None;
                ApplyMenuIcon(icon, menuItem.DisplayName, false);

                ApplySelectionStyle(element, index);

                UnregisterHoverEvents(element);
            }

        }

        private void ApplySelectionStyle(VisualElement element, int index)
        {
            element.style.backgroundColor = _selectedIndex == index
                ? new Color(0.22f, 0.42f, 0.75f)
                : new Color(0, 0, 0, 0);
        }

        private void ApplyIcon(Image icon, string displayName, bool enabled)
        {
            if (SpecialItemIcons.TryGetValue(displayName, out var info))
            {
                var tex = MenuIcons.Load(info.IconName);
                icon.image = tex;
                icon.style.display = tex != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (tex != null)
                {
                    var tint = info.TintColor ?? Color.white;
                    if (!enabled)
                        tint.a *= 0.4f;
                    icon.style.unityBackgroundImageTintColor = tint;
                }
            }
            else
            {
                icon.style.display = DisplayStyle.None;
            }
        }

        private void ApplyMenuIcon(Image icon, string displayName, bool isCategory)
        {
            var iconName = MenuIcons.ResolveIcon(displayName, isCategory);
            var tex = MenuIcons.Load(iconName);
            icon.image = tex;
            icon.style.display = tex != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (tex != null)
                icon.style.unityBackgroundImageTintColor = Color.white;
        }

        private void RegisterHoverEvents(VisualElement element, MenuNode node)
        {
            element.RegisterCallback<PointerEnterEvent, MenuNode>(OnItemPointerEnter, node);
            element.RegisterCallback<PointerLeaveEvent, MenuNode>(OnItemPointerLeave, node);
        }

        private void UnregisterHoverEvents(VisualElement element)
        {
            element.UnregisterCallback<PointerEnterEvent, MenuNode>(OnItemPointerEnter);
            element.UnregisterCallback<PointerLeaveEvent, MenuNode>(OnItemPointerLeave);
        }

        private void OnItemPointerEnter(PointerEnterEvent evt, MenuNode node)
        {
            if (_isSearching)
                return;

            CancelSubmenuSchedule();

            if (node.IsCategory)
            {
                var element = evt.currentTarget as VisualElement;
                var capturedIndex = (int?)element?.userData ?? -1;
                _submenuSchedule = rootVisualElement.schedule.Execute(() =>
                {
                    if (capturedIndex >= 0)
                    {
                        var targetElement = FindItemVisualElement(capturedIndex);
                        if (targetElement != null)
                            ShowSubmenu(node, targetElement);
                    }
                }).StartingIn(SubmenuDelayMs);
            }
            else
            {
                ScheduleHideSubmenu();
            }
        }

        private void OnItemPointerLeave(PointerLeaveEvent evt, MenuNode node)
        {
            if (_isSearching)
                return;

            if (node.IsCategory)
            {
                if (_currentSubmenuCategory == node)
                {
                    ScheduleHideSubmenu();
                }
            }
        }

        private void ShowSubmenu(MenuNode category, VisualElement hoveredItem)
        {
            if (_isSearching)
                return;

            CancelSubmenuSchedule();
            _currentSubmenuCategory = category;

            var screenPos = GetSubmenuScreenPos(hoveredItem);
            float itemCount = category.Children.Count;
            float desiredHeight = Mathf.Max((itemCount * ItemHeight) + 5f, 22f);

            SubmenuWindow.CloseIfOpen();
            SubmenuWindow.Create(this, category, screenPos, desiredHeight);
        }

        private void ShowPrefabSubmenu(SpecialSubmenuItem submenu, VisualElement hoveredItem)
        {
            if (_isSearching)
                return;

            CancelSubmenuSchedule();
            _currentSubmenuCategory = null;

            var screenPos = GetSubmenuScreenPos(hoveredItem);
            float itemCount = submenu.Children.Count;
            float desiredHeight = Mathf.Max((itemCount * ItemHeight) + 5f, 22f);

            ActionSubmenuWindow.CloseIfOpen();
            ActionSubmenuWindow.Create(this, submenu.Children, screenPos, desiredHeight);
        }

        private void HideSubmenu()
        {
            CancelSubmenuSchedule();
            _currentSubmenuCategory = null;
            SubmenuWindow.CloseIfOpen();
            ActionSubmenuWindow.CloseIfOpen();
        }

        internal void ScheduleHideSubmenu()
        {
            CancelSubmenuSchedule();
            _submenuSchedule = rootVisualElement.schedule.Execute(HideSubmenu).StartingIn(SubmenuDelayMs);
        }

        internal void CancelSubmenuSchedule()
        {
            if (_submenuSchedule != null)
            {
                _submenuSchedule.Pause();
                _submenuSchedule = null;
            }
        }

        private void RegisterPrefabHoverEvents(VisualElement element, SpecialSubmenuItem submenu)
        {
            element.RegisterCallback<PointerEnterEvent, SpecialSubmenuItem>(OnPrefabItemPointerEnter, submenu);
            element.RegisterCallback<PointerLeaveEvent, SpecialSubmenuItem>(OnPrefabItemPointerLeave, submenu);
        }

        private void UnregisterPrefabHoverEvents(VisualElement element)
        {
            element.UnregisterCallback<PointerEnterEvent, SpecialSubmenuItem>(OnPrefabItemPointerEnter);
            element.UnregisterCallback<PointerLeaveEvent, SpecialSubmenuItem>(OnPrefabItemPointerLeave);
        }

        private void OnPrefabItemPointerEnter(PointerEnterEvent evt, SpecialSubmenuItem submenu)
        {
            if (_isSearching)
                return;

            CancelSubmenuSchedule();

            var element = evt.currentTarget as VisualElement;
            var capturedIndex = (int?)element?.userData ?? -1;
            _submenuSchedule = rootVisualElement.schedule.Execute(() =>
            {
                if (capturedIndex >= 0)
                {
                    var targetElement = FindItemVisualElement(capturedIndex);
                    if (targetElement != null)
                        ShowPrefabSubmenu(submenu, targetElement);
                }
            }).StartingIn(SubmenuDelayMs);
        }

        private void OnPrefabItemPointerLeave(PointerLeaveEvent evt, SpecialSubmenuItem submenu)
        {
            if (_isSearching)
                return;

            ScheduleHideSubmenu();
        }

        private void NavigateTo(int index)
        {
            _selectedIndex = index;
            _suppressHoverUntilMouseMove = true;
            if (_isSearching && _listView != null)
            {
                _listView.selectedIndex = index;
                _listView.Rebuild();
                _listView.ScrollToItem(index);
            }
            else
            {
                RebuildContentContainer();
            }
            HideSubmenu();
        }

        private VisualElement FindItemVisualElement(int index)
        {
            return rootVisualElement.Query<VisualElement>().Where(e =>
                e.userData is int i && i == index).First();
        }

        private bool IsItemDisabled(int index)
        {
            if (index < 0 || index >= _currentItems.Count)
                return true;
            var item = _currentItems[index];
            if (item is SeparatorItem)
                return true;
            if (item is BackItem)
                return false;
            if (item is SpecialActionItem sa)
                return !sa.Enabled;
            if (item is SpecialSubmenuItem ss)
                return !ss.Enabled;
            return false;
        }

        private int FindNextEnabledIndex(int currentIndex, int direction)
        {
            var next = currentIndex + direction;
            while (next >= 0 && next < _currentItems.Count)
            {
                if (!IsItemDisabled(next))
                    return next;
                next += direction;
            }
            return -1;
        }

        private void OnSearchChanged(ChangeEvent<string> evt)
        {
            var searchText = evt.newValue?.ToLower() ?? "";

            if (string.IsNullOrEmpty(searchText))
            {
                if (_isSearching)
                    ExitSearchMode();
                return;
            }

            if (!_isSearching)
                EnterSearchMode();

            if (searchText == _lastSearchText)
                return;

            _lastSearchText = searchText;
            _filteredItems.Clear();

            foreach (var item in _allItems)
            {
                if (item.DisplayName.ToLower().Contains(searchText))
                    _filteredItems.Add(item);
            }

            _currentItems = _filteredItems;
            _listView.itemsSource = _currentItems;
            _selectedIndex = _filteredItems.Count > 0 ? 0 : -1;
            _listView.selectedIndex = _selectedIndex;
            _listView.Rebuild();
        }

        private void EnterSearchMode()
        {
            _isSearching = true;
            HideSubmenu();

            if (_listView == null)
                BuildListView();

            if (_contentContainer.parent != null)
                rootVisualElement.Remove(_contentContainer);
            if (_listView.parent == null)
                rootVisualElement.Add(_listView);

            _filteredItems.Clear();
            _filteredItems.AddRange(_allItems);
            _currentItems = _filteredItems;
            _listView.itemsSource = _currentItems;
            _selectedIndex = _currentItems.Count > 0 ? 0 : -1;
            _listView.selectedIndex = _selectedIndex;
            _listView.Rebuild();
            SetScrollBarVisibility(true);
        }

        private void ExitSearchMode()
        {
            _isSearching = false;
            _lastSearchText = "";
            ShowRootLevel();
        }

        private void OnSearchKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                    if (_currentItems.Count > 0)
                    {
                        var start = _selectedIndex < 0 ? -1 : _selectedIndex;
                        var next = FindNextEnabledIndex(start, 1);
                        if (next >= 0)
                            NavigateTo(next);
                        evt.StopPropagation();
                    }
                    break;

                case KeyCode.UpArrow:
                    if (_currentItems.Count > 0)
                    {
                        var start = _selectedIndex < 0 ? _currentItems.Count : _selectedIndex;
                        var prev = FindNextEnabledIndex(start, -1);
                        if (prev >= 0)
                            NavigateTo(prev);
                        evt.StopPropagation();
                    }
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    evt.StopPropagation();
                    ExecuteSelected();
                    break;

                case KeyCode.Escape:
                    evt.StopPropagation();
                    if (!string.IsNullOrEmpty(_searchField.value))
                    {
                        _searchField.value = "";
                    }
                    else if (_isSearching)
                    {
                        ExitSearchMode();
                    }
                    else
                    {
                        Close();
                    }
                    break;
            }
        }

        private void OnListKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                    if (_currentItems.Count > 0)
                    {
                        var start = _selectedIndex < 0 ? -1 : _selectedIndex;
                        var next = FindNextEnabledIndex(start, 1);
                        if (next >= 0)
                            NavigateTo(next);
                        evt.StopPropagation();
                    }
                    break;

                case KeyCode.UpArrow:
                    if (_currentItems.Count > 0)
                    {
                        var start = _selectedIndex < 0 ? _currentItems.Count : _selectedIndex;
                        var prev = FindNextEnabledIndex(start, -1);
                        if (prev >= 0)
                            NavigateTo(prev);
                        evt.StopPropagation();
                    }
                    break;

                case KeyCode.Escape:
                    evt.StopPropagation();
                    if (_isSearching)
                    {
                        if (!string.IsNullOrEmpty(_searchField.value))
                        {
                            _searchField.value = "";
                        }
                        else
                        {
                            ExitSearchMode();
                        }
                    }
                    else
                    {
                        HideSubmenu();
                        Close();
                    }
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    evt.StopPropagation();
                    ExecuteSelected();
                    break;
            }
        }

        private void ExecuteSelected()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _currentItems.Count)
                return;

            if (IsItemDisabled(_selectedIndex))
                return;

            var item = _currentItems[_selectedIndex];

            if (item is MenuNode node)
            {
                if (node.IsCategory)
                {
                    var element = FindItemVisualElement(_selectedIndex);
                    if (element != null)
                        ShowSubmenu(node, element);
                }
                else
                    ExecutePath(node.MenuPath);
            }
            else if (item is HierarchyMenuItem menuItem)
            {
                ExecutePath(menuItem.MenuPath);
            }
            else if (item is SpecialActionItem special)
            {
                if (special.Enabled)
                    special.Action?.Invoke();
            }
            else if (item is SpecialSubmenuItem submenu)
            {
                if (submenu.Enabled)
                {
                    var element = FindItemVisualElement(_selectedIndex);
                    if (element != null)
                        ShowSpecialSubmenuLevel(submenu);
                }
            }
        }

        private void ExecutePath(string menuPath)
        {
            Close();

            EditorApplication.delayCall += () =>
            {
                EditorApplication.ExecuteMenuItem(menuPath);
            };
        }

        private void FocusHierarchyWindow()
        {
            var hierarchyType = typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
            if (hierarchyType != null)
            {
                var hierarchyWindow = EditorWindow.GetWindow(hierarchyType);
                if (hierarchyWindow != null)
                    hierarchyWindow.Focus();
            }
        }

        private void CutSelection()
        {
            if (Selection.gameObjects.Length == 0)
                return;

            Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                EditorApplication.ExecuteMenuItem("Edit/Cut");
            };
        }

        private void CopySelection()
        {
            if (Selection.gameObjects.Length == 0)
                return;

            Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                EditorApplication.ExecuteMenuItem("Edit/Copy");
            };
        }

        private void PasteAsChildOfClicked()
        {
            Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                EditorApplication.ExecuteMenuItem("Edit/Paste");
            };
        }

        private void RenameSelected()
        {
            if (Selection.gameObjects.Length == 0)
                return;

            Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                EditorApplication.ExecuteMenuItem("Edit/Rename");
            };
        }

        private void DuplicateSelection()
        {
            if (Selection.gameObjects.Length == 0)
                return;

            Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                EditorApplication.ExecuteMenuItem("Edit/Duplicate");
            };
        }

        private void PasteAsChild()
        {
            Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                EditorApplication.ExecuteMenuItem("GameObject/Paste As Child");
            };
        }

        private void PasteAsSibling()
        {
            Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                EditorApplication.ExecuteMenuItem("GameObject/Paste As Sibling");
            };
        }

        private void DeleteSelection()
        {
            if (Selection.gameObjects.Length == 0)
                return;

            Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                EditorApplication.ExecuteMenuItem("Edit/Delete");
            };
        }

        private void SelectAll()
        {
            Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                EditorApplication.ExecuteMenuItem("Edit/Select All");
            };
        }

        private void DeselectAll()
        {
            Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                Selection.activeGameObject = null;
            };
        }

        private void InvertSelection()
        {
            Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                var currentSelection = new HashSet<GameObject>(Selection.gameObjects);
                var allObjects = new List<GameObject>();
                var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (activeScene.IsValid())
                {
                    var roots = activeScene.GetRootGameObjects();
                    foreach (var root in roots)
                        CollectAllChildren(root, allObjects);
                }
                var newSelection = new List<GameObject>();
                foreach (var obj in allObjects)
                {
                    if (!currentSelection.Contains(obj))
                        newSelection.Add(obj);
                }
                Selection.objects = newSelection.ToArray();
            };
        }

        private void SelectChildren()
        {
            Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                var parents = Selection.gameObjects;
                var allSelected = new HashSet<GameObject>(parents);
                foreach (var parent in parents)
                    CollectAllChildren(parent, allSelected);
                Selection.objects = allSelected.ToArray();
            };
        }

        private void CollectAllChildren(GameObject parent, List<GameObject> list)
        {
            foreach (Transform child in parent.transform)
            {
                list.Add(child.gameObject);
                CollectAllChildren(child.gameObject, list);
            }
        }

        private void CollectAllChildren(GameObject parent, HashSet<GameObject> set)
        {
            foreach (Transform child in parent.transform)
            {
                set.Add(child.gameObject);
                CollectAllChildren(child.gameObject, set);
            }
        }

        private void FindReferencesInScene()
        {
            Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                if (Selection.activeGameObject == null)
                    return;

                var target = Selection.activeGameObject;
                var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (!activeScene.IsValid())
                    return;

                var roots = activeScene.GetRootGameObjects();
                var referencing = new List<GameObject>();

                foreach (var root in roots)
                {
                    var allTransforms = root.GetComponentsInChildren<Transform>(true);
                    foreach (var t in allTransforms)
                    {
                        if (t.gameObject == target)
                            continue;

                        var components = t.GetComponents<Component>();
                        foreach (var comp in components)
                        {
                            if (comp == null)
                                continue;

                            var so = new SerializedObject(comp);
                            var prop = so.GetIterator();
                            while (prop.NextVisible(true))
                            {
                                if (prop.propertyType == SerializedPropertyType.ObjectReference &&
                                    prop.objectReferenceValue == target)
                                {
                                    referencing.Add(t.gameObject);
                                    break;
                                }
                            }

                            if (referencing.Count > 0 && referencing[referencing.Count - 1] == t.gameObject)
                                break;
                        }
                    }
                }

                Selection.objects = referencing.ToArray();
            };
        }

        private void SetAsDefaultParent()
        {
            Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                if (Selection.activeGameObject == null)
                    return;

                var hierarchyType = typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
                if (hierarchyType == null)
                    return;

                var hierarchyWindow = EditorWindow.GetWindow(hierarchyType);
                if (hierarchyWindow == null)
                    return;

                var prop = hierarchyType.GetProperty("defaultParent",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop != null)
                {
                    prop.SetValue(hierarchyWindow, Selection.activeGameObject.transform, null);
                }
            };
        }

        private void OpenAssetInContext()
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (source == null) return;
            ExecutePrefabAction(() => AssetDatabase.OpenAsset(source));
        }

        private void OpenAssetInIsolation()
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (source == null) return;
            ExecutePrefabAction(() => AssetDatabase.OpenAsset(source));
        }

        private void SelectPrefabAsset()
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (source == null) return;
            ExecutePrefabAction(() => Selection.activeObject = source);
        }

        private void SelectPrefabRoot()
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            if (root == null) return;
            ExecutePrefabAction(() => Selection.activeGameObject = root);
        }

        private void UnpackPrefab()
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            if (root == null) return;
            ExecutePrefabAction(() => PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.OutermostRoot, InteractionMode.UserAction));
        }

        private void UnpackPrefabCompletely()
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            if (root == null) return;
            ExecutePrefabAction(() => PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.UserAction));
        }

        private void ReplacePrefab()
        {
            ReplacePrefabInternal(false);
        }

        private void ReplacePrefabKeepOverrides()
        {
            ReplacePrefabInternal(true);
        }

        private void ReplacePrefabInternal(bool keepOverrides)
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            if (root == null) return;

            Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();

                if (root == null) return;

                var absPath = EditorUtility.OpenFilePanelWithFilters(
                    keepOverrides ? "Replace Prefab (Keep Overrides)" : "Replace Prefab",
                    "Assets",
                    new[] { "Prefab files", "prefab" });

                if (string.IsNullOrEmpty(absPath))
                    return;

                var relPath = FileUtil.GetProjectRelativePath(absPath);
                if (string.IsNullOrEmpty(relPath))
                {
                    EditorUtility.DisplayDialog("Replace Prefab", "Selected file is not in the project.", "OK");
                    return;
                }

                var newPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(relPath);
                if (newPrefab == null) return;

                var currentSource = PrefabUtility.GetCorrespondingObjectFromSource(root);
                if (currentSource == newPrefab)
                    return;

                var overrides = keepOverrides ? PrefabUtility.GetPropertyModifications(root) : null;

                var parent = root.transform.parent;
                var siblingIndex = root.transform.GetSiblingIndex();
                var position = root.transform.position;
                var rotation = root.transform.rotation;
                var localScale = root.transform.localScale;
                var name = root.name;
                var layer = root.layer;
                var tag = root.tag;

                var newInstance = (GameObject)PrefabUtility.InstantiatePrefab(newPrefab, parent);
                if (newInstance == null) return;

                newInstance.transform.SetSiblingIndex(siblingIndex);
                newInstance.transform.position = position;
                newInstance.transform.rotation = rotation;
                newInstance.transform.localScale = localScale;
                newInstance.name = name;
                newInstance.layer = layer;
                newInstance.tag = tag;

                if (overrides != null && overrides.Length > 0)
                    PrefabUtility.SetPropertyModifications(newInstance, overrides);

                Selection.activeGameObject = newInstance;
                UnityEngine.Object.DestroyImmediate(root);
            };
        }

        private void RemoveUnusedOverrides()
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            if (root == null) return;

            Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();

                if (root == null) return;
                var source = PrefabUtility.GetCorrespondingObjectFromSource(root);
                if (source == null) return;

                var overrides = PrefabUtility.GetPropertyModifications(root);
                if (overrides == null || overrides.Length == 0)
                {
                    EditorUtility.DisplayDialog("Remove Unused Overrides", "No unused overrides found.", "OK");
                    return;
                }

                var activeOverrides = new List<PropertyModification>();
                int removedCount = 0;

                foreach (var mod in overrides)
                {
                    if (mod.target == null)
                    {
                        removedCount++;
                        continue;
                    }

                    var comp = mod.target as Component;
                    if (comp == null)
                    {
                        activeOverrides.Add(mod);
                        continue;
                    }

                    var targetGo = comp.gameObject;
                    var path = GetTransformPath(targetGo.transform, root.transform);
                    var sourceTransform = string.IsNullOrEmpty(path) ? source.transform : source.transform.Find(path);

                    if (sourceTransform != null)
                    {
                        var sourceComponent = sourceTransform.GetComponent(comp.GetType());
                        if (sourceComponent != null)
                        {
                            var instanceSo = new SerializedObject(comp);
                            var sourceSo = new SerializedObject(sourceComponent);
                            var instanceProp = instanceSo.FindProperty(mod.propertyPath);
                            var sourceProp = sourceSo.FindProperty(mod.propertyPath);

                            if (instanceProp != null && sourceProp != null &&
                                SerializedProperty.DataEquals(instanceProp, sourceProp))
                            {
                                removedCount++;
                                continue;
                            }
                        }
                    }

                    activeOverrides.Add(mod);
                }

                if (removedCount == 0)
                {
                    EditorUtility.DisplayDialog("Remove Unused Overrides", "No unused overrides found.", "OK");
                    return;
                }

                bool proceed = EditorUtility.DisplayDialog("Remove Unused Overrides",
                    $"Remove {removedCount} unused override(s)?", "Remove", "Cancel");

                if (proceed)
                {
                    PrefabUtility.SetPropertyModifications(root, activeOverrides.ToArray());
                }
            };
        }

        private string GetTransformPath(Transform child, Transform root)
        {
            if (child == root) return "";
            var path = child.name;
            var current = child.parent;
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        private void ExecutePrefabAction(Action action)
        {
            Close();
            EditorApplication.delayCall += () =>
            {
                FocusHierarchyWindow();
                action?.Invoke();
            };
        }
    }

    class SubmenuWindow : EditorWindow
    {
        private static SubmenuWindow _instance;
        private ListView _listView;
        private MenuNode _category;
        private HierarchyContextMenuWindow _parent;
        private static readonly Color BgColor = new Color(0.12f, 0.12f, 0.12f);
        private static readonly Color BorderColor = new Color(0.25f, 0.25f, 0.25f);
        private const float ItemHeight = 22f;
        private const float SubmenuWidth = 240f;

        public static void CloseIfOpen()
        {
            if (_instance != null)
            {
                _instance.Close();
                _instance = null;
            }
        }

        public static SubmenuWindow Create(HierarchyContextMenuWindow parent, MenuNode category, Vector2 screenPos, float height)
        {
            var rect = new Rect(screenPos.x, screenPos.y, 1, 1);
            var instance = CreateInstance<SubmenuWindow>();
            instance._parent = parent;
            instance._category = category;
            instance.ShowAsDropDown(rect, new Vector2(SubmenuWidth, Mathf.Max(height, 22f)));
            instance.Focus();
            _instance = instance;
            return instance;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void OnLostFocus()
        {
            Close();
        }

        private void CreateGUI()
        {
            rootVisualElement.style.backgroundColor = BgColor;
            rootVisualElement.style.borderTopLeftRadius = 8;
            rootVisualElement.style.borderTopRightRadius = 8;
            rootVisualElement.style.borderBottomLeftRadius = 8;
            rootVisualElement.style.borderBottomRightRadius = 8;
            rootVisualElement.style.paddingLeft = 1;
            rootVisualElement.style.paddingRight = 1;
            rootVisualElement.style.paddingTop = 1;
            rootVisualElement.style.paddingBottom = 1;
            rootVisualElement.style.borderTopWidth = 1;
            rootVisualElement.style.borderLeftWidth = 1;
            rootVisualElement.style.borderRightWidth = 1;
            rootVisualElement.style.borderBottomWidth = 1;
            rootVisualElement.style.borderTopColor = BorderColor;
            rootVisualElement.style.borderLeftColor = BorderColor;
            rootVisualElement.style.borderRightColor = BorderColor;
            rootVisualElement.style.borderBottomColor = BorderColor;

            _listView = new ListView(new List<MenuNode>(_category.Children), ItemHeight, MakeItem, BindItem);
            _listView.style.flexGrow = 1;
            _listView.style.marginLeft = 1;
            _listView.style.marginRight = 1;
            _listView.style.marginBottom = 1;
            _listView.style.backgroundColor = new Color(0, 0, 0, 0);
            _listView.selectionType = SelectionType.Single;
            _listView.focusable = false;

            var scrollView = _listView.Q<ScrollView>();
            if (scrollView != null)
            {
                scrollView.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                scrollView.mouseWheelScrollSize = ItemHeight;
                scrollView.style.paddingTop = 0;
                scrollView.style.paddingBottom = 0;
                scrollView.style.marginTop = 0;
                scrollView.style.marginBottom = 0;
                var viewport = scrollView.Q<VisualElement>(className: "unity-scroll-view__content-viewport");
                if (viewport != null)
                {
                    viewport.style.paddingTop = 0;
                    viewport.style.paddingBottom = 0;
                    viewport.style.marginTop = 0;
                    viewport.style.marginBottom = 0;
                    viewport.style.overflow = Overflow.Visible;
                }
                scrollView.contentContainer.style.overflow = Overflow.Visible;
                scrollView.contentContainer.style.paddingTop = 0;
                scrollView.contentContainer.style.paddingBottom = 0;
            }

            rootVisualElement.Add(_listView);
            _listView.Rebuild();

            rootVisualElement.RegisterCallback<PointerEnterEvent>(_ =>
            {
                _parent?.CancelSubmenuSchedule();
            });

            rootVisualElement.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                _parent?.ScheduleHideSubmenu();
            });
        }

        private VisualElement MakeItem()
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.paddingLeft = 10;
            container.style.paddingRight = 10;
            container.style.paddingTop = 0;
            container.style.paddingBottom = 0;
            container.style.minHeight = ItemHeight;
            container.style.backgroundColor = new Color(0, 0, 0, 0);

            var icon = new Image();
            icon.name = "item-icon";
            icon.style.width = 16;
            icon.style.height = 16;
            icon.style.marginRight = 6;
            icon.style.flexShrink = 0;
            icon.scaleMode = ScaleMode.ScaleToFit;
            icon.style.display = DisplayStyle.None;
            container.Add(icon);

            var label = new Label();
            label.name = "item-label";
            label.style.fontSize = 13;
            label.style.color = new Color(0.85f, 0.85f, 0.85f);
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.flexShrink = 1;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.flexGrow = 1;
            container.Add(label);

            var arrow = new Label();
            arrow.name = "item-arrow";
            arrow.text = "\u25B8";
            arrow.style.fontSize = 12;
            arrow.style.color = new Color(0.55f, 0.55f, 0.55f);
            arrow.style.marginLeft = 4;
            arrow.style.display = DisplayStyle.None;
            arrow.style.unityTextAlign = TextAnchor.MiddleRight;
            container.Add(arrow);

            container.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    evt.StopPropagation();
                    var idx = (int)container.userData;
                    var children = _category.Children;
                    if (idx >= 0 && idx < children.Count)
                    {
                        var child = children[idx];
                        if (child.IsLeaf)
                        {
                            _parent?.Close();
                            var path = child.MenuPath;
                            EditorApplication.delayCall += () => EditorApplication.ExecuteMenuItem(path);
                        }
                        else if (child.IsCategory)
                        {
                            var screenPos = new Vector2(position.x + SubmenuWidth + 4f, position.y + (idx * ItemHeight));
                            float itemCount = child.Children.Count;
                            float desiredHeight = Mathf.Max((itemCount * ItemHeight) + 5f, 22f);
                            SubmenuWindow.CloseIfOpen();
                            _instance = SubmenuWindow.Create(_parent, child, screenPos, desiredHeight);
                        }
                    }
                }
            }, TrickleDown.TrickleDown);

            container.RegisterCallback<PointerEnterEvent>(evt =>
            {
                var idx = (int)container.userData;
                var children = _category.Children;
                if (idx >= 0 && idx < children.Count && children[idx].IsCategory)
                {
                    container.style.backgroundColor = new Color(0.22f, 0.42f, 0.75f);
                }
                else
                {
                    container.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
                }
            });

            container.RegisterCallback<PointerLeaveEvent>(evt =>
            {
                container.style.backgroundColor = new Color(0, 0, 0, 0);
            });

            return container;
        }

        private void BindItem(VisualElement element, int index)
        {
            element.userData = index;
            element.style.backgroundColor = new Color(0, 0, 0, 0);
            element.style.paddingTop = 0;
            element.style.paddingBottom = 0;
            element.style.minHeight = ItemHeight;

            var label = element.Q<Label>("item-label");
            var arrow = element.Q<Label>("item-arrow");
            var icon = element.Q<Image>("item-icon");

            if (index >= 0 && index < _category.Children.Count)
            {
                var child = _category.Children[index];
                label.text = child.Name;
                arrow.style.display = child.IsCategory ? DisplayStyle.Flex : DisplayStyle.None;
                var iconName = MenuIcons.ResolveIcon(child.Name, child.IsCategory);
                var tex = MenuIcons.Load(iconName);
                icon.image = tex;
                icon.style.display = tex != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (tex != null)
                    icon.style.unityBackgroundImageTintColor = Color.white;
            }
        }
    }

    class ActionSubmenuWindow : EditorWindow
    {
        private static ActionSubmenuWindow _instance;
        private ListView _listView;
        private List<SpecialActionItem> _items;
        private HierarchyContextMenuWindow _parent;
        private static readonly Color BgColor = new Color(0.12f, 0.12f, 0.12f);
        private static readonly Color BorderColor = new Color(0.25f, 0.25f, 0.25f);
        private const float ItemHeight = 22f;
        private const float SubmenuWidth = 240f;

        public static void CloseIfOpen()
        {
            if (_instance != null)
            {
                _instance.Close();
                _instance = null;
            }
        }

        public static ActionSubmenuWindow Create(HierarchyContextMenuWindow parent, List<SpecialActionItem> items, Vector2 screenPos, float height)
        {
            var rect = new Rect(screenPos.x, screenPos.y, 1, 1);
            var instance = CreateInstance<ActionSubmenuWindow>();
            instance._parent = parent;
            instance._items = items;
            instance.ShowAsDropDown(rect, new Vector2(SubmenuWidth, Mathf.Max(height, 22f)));
            instance.Focus();
            _instance = instance;
            return instance;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void OnLostFocus()
        {
            Close();
        }

        private void CreateGUI()
        {
            rootVisualElement.style.backgroundColor = BgColor;
            rootVisualElement.style.borderTopLeftRadius = 8;
            rootVisualElement.style.borderTopRightRadius = 8;
            rootVisualElement.style.borderBottomLeftRadius = 8;
            rootVisualElement.style.borderBottomRightRadius = 8;
            rootVisualElement.style.paddingLeft = 1;
            rootVisualElement.style.paddingRight = 1;
            rootVisualElement.style.paddingTop = 1;
            rootVisualElement.style.paddingBottom = 1;
            rootVisualElement.style.borderTopWidth = 1;
            rootVisualElement.style.borderLeftWidth = 1;
            rootVisualElement.style.borderRightWidth = 1;
            rootVisualElement.style.borderBottomWidth = 1;
            rootVisualElement.style.borderTopColor = BorderColor;
            rootVisualElement.style.borderLeftColor = BorderColor;
            rootVisualElement.style.borderRightColor = BorderColor;
            rootVisualElement.style.borderBottomColor = BorderColor;

            _listView = new ListView(new List<SpecialActionItem>(_items), ItemHeight, MakeItem, BindItem);
            _listView.style.flexGrow = 1;
            _listView.style.marginLeft = 1;
            _listView.style.marginRight = 1;
            _listView.style.marginBottom = 1;
            _listView.style.backgroundColor = new Color(0, 0, 0, 0);
            _listView.selectionType = SelectionType.Single;
            _listView.focusable = false;

            var scrollView = _listView.Q<ScrollView>();
            if (scrollView != null)
            {
                scrollView.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                scrollView.mouseWheelScrollSize = ItemHeight;
                scrollView.style.paddingTop = 0;
                scrollView.style.paddingBottom = 0;
                scrollView.style.marginTop = 0;
                scrollView.style.marginBottom = 0;
                var viewport = scrollView.Q<VisualElement>(className: "unity-scroll-view__content-viewport");
                if (viewport != null)
                {
                    viewport.style.paddingTop = 0;
                    viewport.style.paddingBottom = 0;
                    viewport.style.marginTop = 0;
                    viewport.style.marginBottom = 0;
                    viewport.style.overflow = Overflow.Visible;
                }
                scrollView.contentContainer.style.overflow = Overflow.Visible;
                scrollView.contentContainer.style.paddingTop = 0;
                scrollView.contentContainer.style.paddingBottom = 0;
            }

            rootVisualElement.Add(_listView);
            _listView.Rebuild();

            rootVisualElement.RegisterCallback<PointerEnterEvent>(_ =>
            {
                _parent?.CancelSubmenuSchedule();
            });

            rootVisualElement.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                _parent?.ScheduleHideSubmenu();
            });
        }

        private VisualElement MakeItem()
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.paddingLeft = 10;
            container.style.paddingRight = 10;
            container.style.paddingTop = 0;
            container.style.paddingBottom = 0;
            container.style.minHeight = ItemHeight;
            container.style.backgroundColor = new Color(0, 0, 0, 0);

            var label = new Label();
            label.name = "item-label";
            label.style.fontSize = 13;
            label.style.color = new Color(0.85f, 0.85f, 0.85f);
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.flexShrink = 1;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.flexGrow = 1;
            container.Add(label);

            container.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    evt.StopPropagation();
                    var idx = (int)container.userData;
                    if (idx >= 0 && idx < _items.Count)
                    {
                        var item = _items[idx];
                        if (item.Enabled)
                        {
                            _parent?.Close();
                            var action = item.Action;
                            EditorApplication.delayCall += () => action?.Invoke();
                        }
                    }
                }
            }, TrickleDown.TrickleDown);

            container.RegisterCallback<PointerEnterEvent>(evt =>
            {
                var idx = (int)container.userData;
                if (idx >= 0 && idx < _items.Count && _items[idx].Enabled)
                    container.style.backgroundColor = new Color(0.22f, 0.42f, 0.75f);
                else
                    container.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
            });

            container.RegisterCallback<PointerLeaveEvent>(evt =>
            {
                container.style.backgroundColor = new Color(0, 0, 0, 0);
            });

            return container;
        }

        private void BindItem(VisualElement element, int index)
        {
            element.userData = index;
            element.style.backgroundColor = new Color(0, 0, 0, 0);
            element.style.paddingTop = 0;
            element.style.paddingBottom = 0;
            element.style.minHeight = ItemHeight;

            var label = element.Q<Label>("item-label");
            if (index >= 0 && index < _items.Count)
            {
                var item = _items[index];
                label.text = item.DisplayName;
                label.style.color = item.Enabled ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.4f, 0.4f, 0.4f);
            }
        }
    }

    static class MenuIcons
    {
        private static readonly Dictionary<string, Texture2D> _cache = new();

        public static Texture2D Load(string name)
        {
            if (!_cache.TryGetValue(name, out var tex))
            {
                var content = EditorGUIUtility.IconContent(name);
                tex = content?.image as Texture2D;
                _cache[name] = tex;
            }
            return tex;
        }

        public static string ResolveIcon(string displayName, bool isCategory)
        {
            if (isCategory)
                return ResolveCategoryIcon(displayName);

            var lower = displayName.ToLowerInvariant();

            switch (lower)
            {
                // Create
                case "create empty":
                case "create empty child":
                case "create empty parent":
                    return "d_Transform Icon";

                // 3D primitives
                case "cube":
                    return "PreMatCube";
                case "sphere":
                    return "PreMatSphere";
                case "capsule":
                case "plane":
                case "quad":
                    return "GameObject Icon";
                case "cylinder":
                    return "PreMatCylinder";

                // 3D complex
                case "ragdoll":
                    return "Avatar Icon";
                case "terrain":
                    return "Terrain Icon";
                case "tree":
                    return "Terrain Icon";
                case "wind zone":
                    return "Terrain Icon";

                // 2D / Sprite
                case "sprite":
                case "square":
                case "circle":
                case "isometric diamond":
                case "hexagonal tile":
                case "capsule collider 2d":
                case "circle collider 2d":
                case "box collider 2d":
                case "polygon collider 2d":
                case "edge collider 2d":
                case "sprite shape":
                case "sprite shape profile":
                case "sprite atlas":
                    return "Sprite Icon";

                // Lights
                case "directional light":
                    return "DirectionalLight Icon";
                case "point light":
                    return "Light Icon";
                case "area light":
                    return "d_AreaLight Icon";
                case "light probe proxy volume":
                    return "d_LightProbeProxyVolume Icon";
                case "spot light":
                    return "d_Spotlight Icon";
                case "light probe group":
                    return "LightProbeGroup Gizmo";
                case "reflection probe":
                    return "ReflectionProbeSelector";

                // Audio
                case "audio source":
                case "audio reverb zone":
                case "audio listener":
                case "audio low pass filter":
                case "audio high pass filter":
                case "audio echo filter":
                case "audio distortion filter":
                case "audio reverb filter":
                case "audio chorus filter":
                    return "AudioSource Icon";

                // Video
                case "video player":
                    return "UnityEditor.GameView";

                // UI
                case "canvas":
                case "button":
                case "image":
                case "raw image":
                case "text":
                case "input field":
                case "slider":
                case "scrollbar":
                case "toggle":
                case "dropdown":
                case "panel":
                case "scroll view":
                case "event system":
                case "mask":
                case "rect mask 2d":
                case "selectable":
                case "toggle group":
                case "layout element":
                case "horizontal layout group":
                case "vertical layout group":
                case "grid layout group":
                    return "Canvas Icon";

                // Camera
                case "camera":
                case "cinemachine virtual camera":
                case "cinemachine freelook":
                case "cinemachine clear shot":
                case "cinemachine blend list":
                case "cinemachine state-driven":
                case "cinemachine target group":
                case "cinemachine collider":
                case "cinemachine confiner":
                    return "Camera Icon";

                // Effects
                case "particle system":
                    return "ParticleShapeTool";
                case "particle system force field":
                case "visual effect":
                    return "Particle Effect";
                case "trail":
                    return "d_TrailRenderer Icon";
                case "line":
                    return "d_LineRenderer Icon";

                // Timeline
                case "timeline":
                case "playable director":
                    return "UnityEditor.AnimationWindow";

                // Post Processing
                case "post process volume":
                case "post process layer":
                    return "d_Settings";

                // UI Toolkit
                case "ui document":
                case "panel settings":
                    return "Canvas Icon";

                // Navigation
                case "nav mesh surface":
                    return "d_NavMeshData Icon";
                case "nav mesh agent":
                    return "d_NavMeshAgent Icon";
                case "nav mesh obstacle":
                    return "d_NavMeshObstacle Icon";
                case "nav mesh link":
                    return "d_NavMeshAgent Icon";
                case "nav mesh modifier":
                case "nav mesh modifier volume":
                    return "d_NavMeshObstacle Icon";

                // Physics
                case "rigidbody":
                case "box collider":
                case "sphere collider":
                case "capsule collider":
                case "mesh collider":
                case "wheel collider":
                case "terrain collider":
                case "hinge joint":
                case "fixed joint":
                case "spring joint":
                case "character joint":
                case "configurable joint":
                case "constant force":
                    return "d_editicon.sml";

                // Mesh & Model
                case "textmeshpro":
                    return "Font Icon";

                // Audio Mixer (top-level name)
                case "audio mixer":
                    return "AudioSource Icon";

                default:
                    return "GameObject Icon";
            }
        }

        private static string ResolveCategoryIcon(string categoryName)
        {
            var lower = categoryName.ToLowerInvariant();
            switch (lower)
            {
                case "3d object":
                case "create":
                case "gameobject":
                    return "GameObject Icon";
                case "2d object":
                case "sprite shape":
                case "physics 2d":
                    return "Sprite Icon";
                case "light":
                case "lights":
                    return "Light Icon";
                case "audio":
                    return "AudioSource Icon";
                case "ui":
                case "ui toolkit":
                    return "Canvas Icon";
                case "video":
                    return "UnityEditor.GameView";
                case "effects":
                case "particle systems":
                case "visual effects":
                    return "Particle Effect";
                case "timeline":
                    return "UnityEditor.AnimationWindow";
                case "cinemachine":
                    return "Camera Icon";
                case "post processing":
                case "rendering":
                    return "d_Settings";
                case "textmeshpro":
                    return "Font Icon";
                case "navigation":
                case "navmesh":
                    return "d_NavMeshData Icon";
                case "physics":
                    return "d_editicon.sml";
                case "camera":
                    return "Camera Icon";
                case "terrain":
                    return "Terrain Icon";
                default:
                    return "Folder Icon";
            }
        }
    }
}
