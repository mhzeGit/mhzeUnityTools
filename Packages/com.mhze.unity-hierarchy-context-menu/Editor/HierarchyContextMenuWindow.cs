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
    }

    class HierarchyContextMenuWindow : EditorWindow
    {
        private ListView _listView;
        private TextField _searchField;
        private IList _currentItems;

        private List<HierarchyMenuItem> _allItems;
        private List<HierarchyMenuItem> _filteredItems;
        private MenuNode _rootNode;

        private bool _isSearching;
        private string _lastSearchText = "";
        private List<SpecialActionItem> _specialActions;
        private bool _ready;

        private static HierarchyContextMenuWindow _instance;
        public static bool IsOpen => _instance != null;

        private const float WindowWidth = 280f;
        private const float ItemHeight = 22f;
        private const long SubmenuDelayMs = 120;

        private VisualElement _submenuElement;
        private VisualElement _submenuContent;
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
                _instance = null;
        }

        private void CreateGUI()
        {
            _allItems = new List<HierarchyMenuItem>(HierarchyItemIndexer.Items);
            _filteredItems = new List<HierarchyMenuItem>();

            _specialActions = new List<SpecialActionItem>
            {
                new SpecialActionItem { DisplayName = "Cut", Action = CutSelection },
                new SpecialActionItem { DisplayName = "Copy", Action = CopySelection },
                new SpecialActionItem { DisplayName = "Paste", Action = PasteAsChildOfClicked },
                new SpecialActionItem { DisplayName = "Rename", Action = RenameSelected },
                new SpecialActionItem { DisplayName = "Duplicate", Action = DuplicateSelection },
            };

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
            BuildSubmenuElement();
            BuildListView();
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
            var items = new List<object>();
            items.AddRange(_specialActions);
            items.Add(new SeparatorItem());
            items.AddRange(_rootNode.Children);
            _currentItems = items;
            _listView.itemsSource = _currentItems;
            _listView.selectedIndex = -1;
            _listView.Rebuild();
            HideSubmenu();
            SetScrollBarVisibility(false);
            ResizeWindowToFit(_currentItems.Count);
            _searchField?.Focus();
        }

        private void ShowCategoryLevel(MenuNode node)
        {
            _isSearching = false;
            var items = new List<object>();
            items.Add(new BackItem());
            items.AddRange(node.Children);
            _currentItems = items;
            _listView.itemsSource = _currentItems;
            _listView.selectedIndex = -1;
            _listView.Rebuild();
            SetScrollBarVisibility(false);
            ResizeWindowToFit(_currentItems.Count);
            _searchField?.Focus();
        }

        private float CalculateContentHeight(int itemCount)
        {
            return 44f + (itemCount * ItemHeight) + 16f;
        }

        private void ResizeWindowToFit(int itemCount)
        {
            if (!_ready)
                return;

            var height = CalculateContentHeight(itemCount);
            height = Mathf.Max(height, 60f);
            ShowAsDropDown(new Rect(position.x, position.y, 1, 1), new Vector2(WindowWidth, height));
        }

        private void SetScrollBarVisibility(bool visible)
        {
            var scrollView = _listView?.Q<ScrollView>();
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
            _searchField.style.paddingTop = 7;
            _searchField.style.paddingBottom = 7;
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
            }

            _searchField.RegisterValueChangedCallback(OnSearchChanged);
            _searchField.RegisterCallback<KeyDownEvent>(OnSearchKeyDown, TrickleDown.TrickleDown);

            rootVisualElement.Add(_searchField);
        }

        private void BuildSubmenuElement()
        {
            _submenuElement = new VisualElement();
            _submenuElement.style.position = Position.Absolute;
            _submenuElement.style.display = DisplayStyle.None;
            _submenuElement.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            _submenuElement.style.borderTopLeftRadius = 8;
            _submenuElement.style.borderTopRightRadius = 8;
            _submenuElement.style.borderBottomLeftRadius = 8;
            _submenuElement.style.borderBottomRightRadius = 8;
            _submenuElement.style.borderTopWidth = 1;
            _submenuElement.style.borderLeftWidth = 1;
            _submenuElement.style.borderRightWidth = 1;
            _submenuElement.style.borderBottomWidth = 1;
            _submenuElement.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f);
            _submenuElement.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f);
            _submenuElement.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f);
            _submenuElement.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f);
            _submenuElement.style.minWidth = 180;
            _submenuElement.style.paddingLeft = 4;
            _submenuElement.style.paddingRight = 4;
            _submenuElement.style.paddingTop = 4;
            _submenuElement.style.paddingBottom = 4;
            _submenuElement.style.overflow = Overflow.Hidden;

            _submenuContent = new VisualElement();
            _submenuElement.Add(_submenuContent);

            _submenuElement.RegisterCallback<MouseEnterEvent>(_ =>
            {
                CancelSubmenuSchedule();
            });

            _submenuElement.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                ScheduleHideSubmenu();
            });

            rootVisualElement.Add(_submenuElement);
        }

        private void BuildListView()
        {
            _listView = new ListView(new List<object>(), ItemHeight, MakeItem, BindItem);
            _listView.style.flexGrow = 1;
            _listView.style.marginLeft = 4;
            _listView.style.marginRight = 4;
            _listView.style.marginBottom = 4;
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
            }

            rootVisualElement.Add(_listView);
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

                if (_currentItems[idx] is SeparatorItem || idx == _listView.selectedIndex)
                    return;

                if (_suppressHoverUntilMouseMove)
                    return;

                var oldIdx = _listView.selectedIndex;
                _listView.selectedIndex = idx;

                if (oldIdx >= 0)
                {
                    var oldElement = FindItemVisualElement(oldIdx);
                    if (oldElement != null)
                        oldElement.style.backgroundColor = new Color(0, 0, 0, 0);
                }

                container.style.backgroundColor = new Color(0.22f, 0.42f, 0.75f);
            });

            container.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    var idx = (int)container.userData;
                    _listView.selectedIndex = idx;
                    evt.StopPropagation();

                    if (idx >= 0 && idx < _currentItems.Count)
                    {
                        var clickedItem = _currentItems[idx];
                        if (clickedItem is MenuNode node)
                        {
                            if (!node.IsCategory)
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
                    }
                }
            });

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
                label.style.color = new Color(0.85f, 0.85f, 0.85f);
                arrow.style.display = DisplayStyle.None;
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
            element.style.backgroundColor = _listView.selectedIndex == index
                ? new Color(0.22f, 0.42f, 0.75f)
                : new Color(0, 0, 0, 0);
        }

        private void RegisterHoverEvents(VisualElement element, MenuNode node)
        {
            element.RegisterCallback<MouseEnterEvent, MenuNode>(OnItemMouseEnter, node);
            element.RegisterCallback<MouseLeaveEvent, MenuNode>(OnItemMouseLeave, node);
        }

        private void UnregisterHoverEvents(VisualElement element)
        {
            element.UnregisterCallback<MouseEnterEvent, MenuNode>(OnItemMouseEnter);
            element.UnregisterCallback<MouseLeaveEvent, MenuNode>(OnItemMouseLeave);
        }

        private void OnItemMouseEnter(MouseEnterEvent evt, MenuNode node)
        {
            if (_isSearching)
                return;

            CancelSubmenuSchedule();

            if (node.IsCategory)
            {
                var element = evt.currentTarget as VisualElement;
                _submenuSchedule = rootVisualElement.schedule.Execute(() =>
                {
                    ShowSubmenu(node, element);
                }).StartingIn(SubmenuDelayMs);
            }
            else
            {
                ScheduleHideSubmenu();
            }
        }

        private void OnItemMouseLeave(MouseLeaveEvent evt, MenuNode node)
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

            _submenuContent.Clear();

            foreach (var child in category.Children)
            {
                var childIndex = _submenuContent.childCount;
                var item = new VisualElement();
                item.style.flexDirection = FlexDirection.Row;
                item.style.alignItems = Align.Center;
                item.style.paddingLeft = 10;
                item.style.paddingRight = 10;
                item.style.paddingTop = 2;
                item.style.paddingBottom = 2;
                item.style.minHeight = ItemHeight;
                item.style.backgroundColor = new Color(0, 0, 0, 0);
                item.userData = childIndex;

                var subLabel = new Label();
                subLabel.name = "sub-label";
                subLabel.text = child.Name;
                subLabel.style.fontSize = 13;
                subLabel.style.color = new Color(0.85f, 0.85f, 0.85f);
                subLabel.style.whiteSpace = WhiteSpace.NoWrap;
                subLabel.style.textOverflow = TextOverflow.Ellipsis;
                subLabel.style.flexShrink = 1;
                subLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                subLabel.style.flexGrow = 1;
                item.Add(subLabel);

                var subArrow = new Label();
                subArrow.name = "sub-arrow";
                subArrow.text = "\u25B8";
                subArrow.style.fontSize = 12;
                subArrow.style.color = new Color(0.55f, 0.55f, 0.55f);
                subArrow.style.marginLeft = 4;
                subArrow.style.display = child.IsCategory ? DisplayStyle.Flex : DisplayStyle.None;
                subArrow.style.unityTextAlign = TextAnchor.MiddleRight;
                item.Add(subArrow);

                var capturedChild = child;
                item.RegisterCallback<MouseDownEvent>(subEvt =>
                {
                    if (subEvt.button == 0)
                    {
                        subEvt.StopPropagation();
                        if (capturedChild.IsLeaf)
                            ExecutePath(capturedChild.MenuPath);
                    }
                });

                item.RegisterCallback<PointerEnterEvent>(subEvt =>
                {
                    item.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
                });

                item.RegisterCallback<PointerLeaveEvent>(subEvt =>
                {
                    item.style.backgroundColor = new Color(0, 0, 0, 0);
                });

                _submenuContent.Add(item);
            }

            var posInRoot = hoveredItem.ChangeCoordinatesTo(rootVisualElement, Vector2.zero);
            var windowWidth = rootVisualElement.resolvedStyle.width;
            var submenuWidth = 200f;
            var gap = 4f;

            float left;
            float leftSideX = -submenuWidth - gap;

            if (position.x + leftSideX >= 0)
            {
                left = leftSideX;
            }
            else
            {
                float rightSideX = windowWidth + gap;
                left = rightSideX;
            }

            float submenuHeight = category.Children.Count * ItemHeight + 8;
            float windowHeight = rootVisualElement.resolvedStyle.height;

            float top = posInRoot.y - rootVisualElement.resolvedStyle.borderTopWidth;
            if (top + submenuHeight > windowHeight)
                top = Mathf.Max(0, windowHeight - submenuHeight);

            _submenuElement.style.left = left;
            _submenuElement.style.top = top;
            _submenuElement.style.display = DisplayStyle.Flex;
        }

        private void HideSubmenu()
        {
            CancelSubmenuSchedule();
            _currentSubmenuCategory = null;
            if (_submenuElement != null)
                _submenuElement.style.display = DisplayStyle.None;
        }

        private void ScheduleHideSubmenu()
        {
            CancelSubmenuSchedule();
            _submenuSchedule = rootVisualElement.schedule.Execute(HideSubmenu).StartingIn(SubmenuDelayMs);
        }

        private void CancelSubmenuSchedule()
        {
            if (_submenuSchedule != null)
            {
                _submenuSchedule.Pause();
                _submenuSchedule = null;
            }
        }

        private void NavigateTo(int index)
        {
            _listView.selectedIndex = index;
            _suppressHoverUntilMouseMove = true;
            _listView.Rebuild();
            _listView.ScrollToItem(index);
            HideSubmenu();
        }

        private VisualElement FindItemVisualElement(int index)
        {
            return _listView.Query<VisualElement>().Where(e =>
                e.userData is int i && i == index).First();
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
            _listView.selectedIndex = _filteredItems.Count > 0 ? 0 : -1;
            _listView.Rebuild();
        }

        private void EnterSearchMode()
        {
            _isSearching = true;
            HideSubmenu();
            _filteredItems.Clear();
            _filteredItems.AddRange(_allItems);
            _currentItems = _filteredItems;
            _listView.itemsSource = _currentItems;
            _listView.selectedIndex = _currentItems.Count > 0 ? 0 : -1;
            _listView.Rebuild();
            SetScrollBarVisibility(true);
            ResizeWindowToFit(_currentItems.Count);
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
                        var next = Mathf.Min(_listView.selectedIndex + 1, _currentItems.Count - 1);
                        NavigateTo(next);
                        evt.StopPropagation();
                    }
                    break;

                case KeyCode.UpArrow:
                    if (_currentItems.Count > 0)
                    {
                        var prev = Mathf.Max(_listView.selectedIndex - 1, 0);
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
                        var next = Mathf.Min(_listView.selectedIndex + 1, _currentItems.Count - 1);
                        NavigateTo(next);
                        evt.StopPropagation();
                    }
                    break;

                case KeyCode.UpArrow:
                    if (_currentItems.Count > 0)
                    {
                        var prev = Mathf.Max(_listView.selectedIndex - 1, 0);
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
            if (_listView.selectedIndex < 0 || _listView.selectedIndex >= _currentItems.Count)
                return;

            var item = _currentItems[_listView.selectedIndex];

            if (item is MenuNode node)
            {
                if (!node.IsCategory)
                    ExecutePath(node.MenuPath);
            }
            else if (item is HierarchyMenuItem menuItem)
            {
                ExecutePath(menuItem.MenuPath);
            }
            else if (item is SpecialActionItem special)
            {
                special.Action?.Invoke();
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
    }
}
