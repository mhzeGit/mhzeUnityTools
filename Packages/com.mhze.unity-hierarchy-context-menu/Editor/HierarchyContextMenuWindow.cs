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

        private const float WindowWidth = 420f;
        private const float ItemHeight = 22f;
        private const float SubmenuWidth = 240f;
        private const long SubmenuDelayMs = 120;

        private MenuNode _currentSubmenuCategory;
        private IVisualElementScheduledItem _submenuSchedule;
        private bool _suppressHoverUntilMouseMove;

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
            _searchField = new TextField();
            _searchField.style.flexShrink = 0;
            _searchField.style.marginLeft = 4;
            _searchField.style.marginRight = 4;
            _searchField.style.marginTop = 4;
            _searchField.style.marginBottom = 4;
            _searchField.style.paddingLeft = 10;
            _searchField.style.paddingRight = 10;
            _searchField.style.paddingTop = 3;
            _searchField.style.paddingBottom = 3;
            _searchField.style.backgroundColor = new Color(0.17f, 0.17f, 0.17f);
            _searchField.style.borderTopColor = new Color(0.28f, 0.28f, 0.28f);
            _searchField.style.borderLeftColor = new Color(0.28f, 0.28f, 0.28f);
            _searchField.style.borderRightColor = new Color(0.28f, 0.28f, 0.28f);
            _searchField.style.borderBottomColor = new Color(0.28f, 0.28f, 0.28f);
            _searchField.style.borderTopWidth = 1;
            _searchField.style.borderLeftWidth = 1;
            _searchField.style.borderRightWidth = 1;
            _searchField.style.borderBottomWidth = 1;
            _searchField.style.borderTopLeftRadius = 6;
            _searchField.style.borderTopRightRadius = 6;
            _searchField.style.borderBottomLeftRadius = 6;
            _searchField.style.borderBottomRightRadius = 6;
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
                inputContainer.style.minHeight = 0;
            }

            _searchField.RegisterValueChangedCallback(OnSearchChanged);
            _searchField.RegisterCallback<KeyDownEvent>(OnSearchKeyDown, TrickleDown.TrickleDown);

            rootVisualElement.Add(_searchField);
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
                ApplySelectionStyle(element, index);
                UnregisterHoverEvents(element);
                return;
            }

            if (item is SeparatorItem)
            {
                label.text = "";
                arrow.style.display = DisplayStyle.None;
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
                ApplySelectionStyle(element, index);
                UnregisterHoverEvents(element);
                return;
            }

            if (item is SpecialSubmenuItem submenuItem)
            {
                label.text = submenuItem.DisplayName;
                label.style.color = submenuItem.Enabled ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.4f, 0.4f, 0.4f);
                arrow.style.display = DisplayStyle.Flex;
                ApplySelectionStyle(element, index);
                UnregisterHoverEvents(element);
                return;
            }

            if (item is MenuNode node)
            {
                label.text = node.Name;
                label.style.color = new Color(0.85f, 0.85f, 0.85f);
                arrow.style.display = node.IsCategory ? DisplayStyle.Flex : DisplayStyle.None;

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

        private void HideSubmenu()
        {
            CancelSubmenuSchedule();
            _currentSubmenuCategory = null;
            SubmenuWindow.CloseIfOpen();
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

            if (index >= 0 && index < _category.Children.Count)
            {
                var child = _category.Children[index];
                label.text = child.Name;
                arrow.style.display = child.IsCategory ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
