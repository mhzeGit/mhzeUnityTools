using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace mhze.HierarchyContextMenu
{
    class HierarchyContextMenuWindow : EditorWindow, IContextMenuHost
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
        private SubmenuWindow _currentSubmenu;
        private IVisualElementScheduledItem _submenuSchedule;
        private bool _suppressHoverUntilMouseMove;

        private bool IsPrefabContext
        {
            get
            {
                var go = Selection.activeGameObject;
                if (go == null) return false;
                if (PrefabUtility.IsPartOfPrefabInstance(go)) return true;
                var prefabType = PrefabUtility.GetPrefabAssetType(go);
                return prefabType == PrefabAssetType.Model;
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
            EditorApplication.delayCall -= OnDelayedResize;
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

            rootVisualElement.style.backgroundColor = HierarchyContextMenuSettings.BackgroundColor;

            rootVisualElement.style.paddingLeft = 4;
            rootVisualElement.style.paddingRight = 4;
            rootVisualElement.style.paddingTop = 4;
            rootVisualElement.style.paddingBottom = 2;

            rootVisualElement.style.borderTopWidth = 1;
            rootVisualElement.style.borderLeftWidth = 1;
            rootVisualElement.style.borderRightWidth = 1;
            rootVisualElement.style.borderBottomWidth = 1;
            rootVisualElement.style.borderTopColor = HierarchyContextMenuSettings.BorderColor;
            rootVisualElement.style.borderLeftColor = HierarchyContextMenuSettings.BorderColor;
            rootVisualElement.style.borderRightColor = HierarchyContextMenuSettings.BorderColor;
            rootVisualElement.style.borderBottomColor = HierarchyContextMenuSettings.BorderColor;

            rootVisualElement.RegisterCallback<PointerMoveEvent>(evt =>
            {
                _suppressHoverUntilMouseMove = false;
            });

            BuildSearchField();

            _contentContainer = new VisualElement();
            _contentContainer.style.flexGrow = 1;
            _contentContainer.style.marginLeft = 4;
            _contentContainer.style.marginRight = 4;
            _contentContainer.style.marginBottom = 2;
            _contentContainer.style.paddingTop = 0;
            _contentContainer.style.paddingBottom = 0;
            rootVisualElement.Add(_contentContainer);

            ShowRootLevel();
            _ready = true;

            EditorApplication.delayCall += OnDelayedResize;

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
                    {
                        child.MenuPath = item.MenuPath;
                        child.ShortcutText = item.ShortcutText;
                    }

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

            var ctrl = Application.platform == RuntimePlatform.OSXEditor ? "Cmd" : "Ctrl";

            items.Add(new SpecialActionItem { DisplayName = "Cut", ShortcutText = ctrl + "+X", Action = () => MenuActions.CutSelection(this), Enabled = selectionValid });
            items.Add(new SpecialActionItem { DisplayName = "Copy", ShortcutText = ctrl + "+C", Action = () => MenuActions.CopySelection(this), Enabled = selectionValid });
            items.Add(new SpecialActionItem { DisplayName = "Paste", ShortcutText = ctrl + "+V", Action = () => MenuActions.PasteAsChildOfClicked(this) });
            items.Add(new SpecialSubmenuItem
            {
                DisplayName = "Paste Special",
                Children = new List<SpecialActionItem>
                {
                    new SpecialActionItem { DisplayName = "Paste As Child", Action = () => MenuActions.PasteAsChild(this) },
                    new SpecialActionItem { DisplayName = "Paste As Sibling", Action = () => MenuActions.PasteAsSibling(this) },
                }
            });
            items.Add(new SpecialActionItem { DisplayName = "Rename", ShortcutText = "F2", Action = () => MenuActions.RenameSelected(this), Enabled = selectionValid });
            items.Add(new SpecialActionItem { DisplayName = "Duplicate", ShortcutText = ctrl + "+D", Action = () => MenuActions.DuplicateSelection(this), Enabled = selectionValid });
            items.Add(new SpecialActionItem { DisplayName = "Delete", ShortcutText = "Del", Action = () => MenuActions.DeleteSelection(this), Enabled = selectionValid });

            items.Add(new SeparatorItem());

            items.Add(new SpecialActionItem { DisplayName = "Select All", ShortcutText = ctrl + "+A", Action = () => MenuActions.SelectAll(this) });
            items.Add(new SpecialActionItem { DisplayName = "Deselect All", Action = () => MenuActions.DeselectAll(this), Enabled = activeValid });
            items.Add(new SpecialActionItem { DisplayName = "Invert Selection", Action = () => MenuActions.InvertSelection(this), Enabled = selectionValid });
            items.Add(new SpecialActionItem { DisplayName = "Select Children", Action = () => MenuActions.SelectChildren(this), Enabled = selectionValid });

            items.Add(new SeparatorItem());

            items.Add(new SpecialActionItem { DisplayName = "Find References in Scene", Action = () => MenuActions.FindReferencesInScene(this), Enabled = activeValid });
            items.Add(new SpecialActionItem { DisplayName = "Set as Default Parent", Action = () => MenuActions.SetAsDefaultParent(this), Enabled = activeValid });

            items.Add(new SeparatorItem());

            if (IsPrefabContext)
            {
                items.Add(new SpecialSubmenuItem
                {
                    DisplayName = "Prefab",
                    Children = new List<SpecialActionItem>
                    {
                        new SpecialActionItem { DisplayName = "Open Asset in Context", Action = () => MenuActions.OpenAssetInContext(this) },
                        new SpecialActionItem { DisplayName = "Open Asset in Isolation", Action = () => MenuActions.OpenAssetInIsolation(this) },
                        new SpecialActionItem { DisplayName = "Select Asset", Action = () => MenuActions.SelectPrefabAsset(this) },
                        new SpecialActionItem { DisplayName = "Select Root", Action = () => MenuActions.SelectPrefabRoot(this) },
                        new SpecialActionItem { DisplayName = "Replace...", Action = () => MenuActions.ReplacePrefab(this) },
                        new SpecialActionItem { DisplayName = "Replace and Keep Overrides...", Action = () => MenuActions.ReplacePrefabKeepOverrides(this) },
                        new SpecialActionItem { DisplayName = "Unpack", Action = () => MenuActions.UnpackPrefab(this) },
                        new SpecialActionItem { DisplayName = "Unpack Completely", Action = () => MenuActions.UnpackPrefabCompletely(this) },
                        new SpecialActionItem { DisplayName = "Remove Unused Overrides...", Action = () => MenuActions.RemoveUnusedOverrides(this) },
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
            return 44f + (itemCount * ItemHeight);
        }

        private void ResizeWindowToFit(int itemCount)
        {
            if (!_ready)
                return;

            var height = CalculateContentHeight(itemCount);
            height = Mathf.Max(height, 60f);
            position = new Rect(position.x, position.y, WindowWidth, height);
        }

        private void OnDelayedResize()
        {
            if (_instance != this)
                return;
            if (_currentItems == null)
                return;
            maxSize = new Vector2(10000, 10000);
            minSize = new Vector2(WindowWidth, 0);
            ResizeWindowToFit(_currentItems.Count);
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
            searchContainer.style.backgroundColor = HierarchyContextMenuSettings.SearchBackgroundColor;
            searchContainer.style.borderTopColor = HierarchyContextMenuSettings.BorderColor;
            searchContainer.style.borderLeftColor = HierarchyContextMenuSettings.BorderColor;
            searchContainer.style.borderRightColor = HierarchyContextMenuSettings.BorderColor;
            searchContainer.style.borderBottomColor = HierarchyContextMenuSettings.BorderColor;
            searchContainer.style.borderTopWidth = 1;
            searchContainer.style.borderLeftWidth = 1;
            searchContainer.style.borderRightWidth = 1;
            searchContainer.style.borderBottomWidth = 1;

            searchContainer.style.paddingLeft = 8;
            searchContainer.style.paddingRight = 4;
            searchContainer.style.minHeight = 26;

            var searchIcon = new VisualElement();
            var iconTex = MenuIcons.Load("Search Icon");
            searchIcon.style.backgroundImage = iconTex != null ? Background.FromTexture2D(iconTex) : StyleKeyword.None;
            searchIcon.style.width = 14;
            searchIcon.style.height = 14;
            searchIcon.style.marginRight = 4;
            searchIcon.style.flexShrink = 0;
            searchIcon.style.unityBackgroundImageTintColor = HierarchyContextMenuSettings.DimColor;
            if (!HierarchyContextMenuSettings.ShowIcons || iconTex == null)
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
            _searchField.style.color = HierarchyContextMenuSettings.TextColor;
            _searchField.style.unityFontStyleAndWeight = FontStyle.Normal;
            _searchField.selectAllOnFocus = true;

            var textElement = _searchField.Q<TextElement>();
            if (textElement != null)
            {
                textElement.style.backgroundColor = Color.clear;
                textElement.style.color = HierarchyContextMenuSettings.TextColor;
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
            float left = position.x + rootVisualElement.resolvedStyle.width;
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

            var icon = new VisualElement();
            icon.name = "item-icon";
            icon.style.width = 16;
            icon.style.height = 16;
            icon.style.marginRight = 6;
            icon.style.flexShrink = 0;
            icon.style.display = DisplayStyle.None;
            container.Add(icon);

            var label = new Label();
            label.name = "item-label";
            label.style.fontSize = 13;
            label.style.color = HierarchyContextMenuSettings.TextColor;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.flexShrink = 1;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.flexGrow = 1;
            container.Add(label);

            var shortcut = new Label();
            shortcut.name = "item-shortcut";
            shortcut.style.fontSize = 12;
            shortcut.style.color = HierarchyContextMenuSettings.DimColor;
            shortcut.style.whiteSpace = WhiteSpace.NoWrap;
            shortcut.style.flexShrink = 0;
            shortcut.style.marginLeft = 12;
            shortcut.style.unityTextAlign = TextAnchor.MiddleRight;
            shortcut.style.display = DisplayStyle.None;
            container.Add(shortcut);

            var arrow = new Label();
            arrow.name = "item-arrow";
            arrow.text = "\u25B8";
            arrow.style.fontSize = 12;
            arrow.style.color = HierarchyContextMenuSettings.DimColor;
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

                if (_suppressHoverUntilMouseMove)
                    return;

                if (IsItemDisabled(idx))
                {
                    if (_selectedIndex >= 0)
                    {
                        var oldElement = FindItemVisualElement(_selectedIndex);
                        if (oldElement != null)
                            oldElement.style.backgroundColor = new Color(0, 0, 0, 0);
                    }

                    var bg = HierarchyContextMenuSettings.BackgroundColor;
                    container.style.backgroundColor = new Color(
                        Mathf.Min(bg.r + 0.1f, 1f),
                        Mathf.Min(bg.g + 0.1f, 1f),
                        Mathf.Min(bg.b + 0.1f, 1f)
                    );
                    return;
                }

                var oldIdx = _selectedIndex;
                _selectedIndex = idx;

                if (oldIdx >= 0)
                {
                    var oldElement = FindItemVisualElement(oldIdx);
                    if (oldElement != null)
                        oldElement.style.backgroundColor = new Color(0, 0, 0, 0);
                }

                container.style.backgroundColor = HierarchyContextMenuSettings.HoverColor;
            });

            container.RegisterCallback<PointerLeaveEvent>(evt =>
            {
                var idx = (int)container.userData;
                container.style.backgroundColor = idx == _selectedIndex
                    ? HierarchyContextMenuSettings.HoverColor
                    : new Color(0, 0, 0, 0);
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
            var shortcut = element.Q<Label>("item-shortcut");
            var icon = element.Q<VisualElement>("item-icon");

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

            shortcut.style.display = DisplayStyle.None;

            if (item is BackItem)
            {
                label.text = "\u2190  Back";
                label.style.color = HierarchyContextMenuSettings.DimColor;
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
                element.style.borderTopColor = HierarchyContextMenuSettings.BorderColor;
                element.style.marginTop = 4;
                element.style.marginBottom = 4;
                UnregisterHoverEvents(element);
                return;
            }

            if (item is SpecialActionItem specialAction)
            {
                label.text = specialAction.DisplayName;
                label.style.color = specialAction.Enabled ? HierarchyContextMenuSettings.TextColor : HierarchyContextMenuSettings.DisabledTextColor;
                arrow.style.display = DisplayStyle.None;
                if (!string.IsNullOrEmpty(specialAction.ShortcutText))
                {
                    shortcut.text = specialAction.ShortcutText;
                    shortcut.style.display = DisplayStyle.Flex;
                }
                ApplyIcon(icon, specialAction.DisplayName, specialAction.Enabled);
                ApplySelectionStyle(element, index);
                UnregisterHoverEvents(element);
                return;
            }

            if (item is SpecialSubmenuItem submenuItem)
            {
                label.text = submenuItem.DisplayName;
                label.style.color = submenuItem.Enabled ? HierarchyContextMenuSettings.TextColor : HierarchyContextMenuSettings.DisabledTextColor;
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
                label.style.color = HierarchyContextMenuSettings.TextColor;
                arrow.style.display = node.IsCategory ? DisplayStyle.Flex : DisplayStyle.None;
                if (!node.IsCategory && !string.IsNullOrEmpty(node.ShortcutText))
                {
                    shortcut.text = node.ShortcutText;
                    shortcut.style.display = DisplayStyle.Flex;
                }
                ApplyMenuIcon(icon, node.Name, node.IsCategory);

                ApplySelectionStyle(element, index);

                UnregisterHoverEvents(element);
                RegisterHoverEvents(element, node);
            }
            else if (item is HierarchyMenuItem menuItem)
            {
                arrow.style.display = DisplayStyle.None;
                if (!string.IsNullOrEmpty(menuItem.ShortcutText))
                {
                    shortcut.text = menuItem.ShortcutText;
                    shortcut.style.display = DisplayStyle.Flex;
                }
                ApplyMenuIcon(icon, menuItem.DisplayName, false);

                if (_isSearching && !string.IsNullOrEmpty(_lastSearchText))
                {
                    var displayName = menuItem.DisplayName;
                    var lastSlash = displayName.LastIndexOf('/');
                    var dirPath = lastSlash >= 0 ? displayName.Substring(0, lastSlash) : "";
                    var itemName = lastSlash >= 0 ? displayName.Substring(lastSlash + 1) : displayName;

                    var dimHex = ColorUtility.ToHtmlStringRGB(HierarchyContextMenuSettings.DimColor);
                    var dirText = string.IsNullOrEmpty(dirPath)
                        ? ""
                        : $"<color=#{dimHex}>{dirPath.Replace("/", " \u25B8 ")} \u25B8 </color>";

                    var searchLower = _lastSearchText.ToLower();
                    var nameLower = itemName.ToLower();
                    var searchIdx = nameLower.IndexOf(searchLower, System.StringComparison.Ordinal);

                    string itemText;
                    if (searchIdx >= 0)
                    {
                        var before = itemName.Substring(0, searchIdx);
                        var match = itemName.Substring(searchIdx, _lastSearchText.Length);
                        var after = itemName.Substring(searchIdx + _lastSearchText.Length);
                        itemText = $"{before}<b>{match}</b>{after}";
                    }
                    else
                    {
                        itemText = itemName;
                    }

                    label.text = dirText + itemText;
                    label.style.color = HierarchyContextMenuSettings.TextColor;
                }
                else
                {
                    var displayText = menuItem.DisplayName.Replace("/", " \u25B8 ");
                    label.text = displayText;
                    label.style.color = HierarchyContextMenuSettings.TextColor;
                }

                ApplySelectionStyle(element, index);

                UnregisterHoverEvents(element);
            }

        }

        private void ApplySelectionStyle(VisualElement element, int index)
        {
            element.style.backgroundColor = _selectedIndex == index
                ? HierarchyContextMenuSettings.HoverColor
                : new Color(0, 0, 0, 0);
        }

        private void ApplyIcon(VisualElement icon, string displayName, bool enabled)
        {
            if (!HierarchyContextMenuSettings.ShowIcons)
            {
                icon.style.display = DisplayStyle.None;
                return;
            }

            if (MenuIcons.SpecialItemIcons.TryGetValue(displayName, out var info))
            {
                var tex = MenuIcons.Load(info.IconName);
                icon.style.backgroundImage = tex;
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

        private void ApplyMenuIcon(VisualElement icon, string displayName, bool isCategory)
        {
            if (!HierarchyContextMenuSettings.ShowIcons)
            {
                icon.style.display = DisplayStyle.None;
                return;
            }

            var iconName = MenuIcons.ResolveIcon(displayName, isCategory);
            var desaturate = iconName.Length > 0 && iconName[0] == '!';
            if (desaturate)
                iconName = iconName.Substring(1);
            var tex = desaturate ? MenuIcons.LoadDesaturated(iconName) : MenuIcons.Load(iconName);
            icon.style.backgroundImage = tex;
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

            if (_currentSubmenu != null)
                _currentSubmenu.Close();
            _currentSubmenu = SubmenuWindow.Create(this, category, screenPos, desiredHeight);
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
            if (_currentSubmenu != null)
            {
                _currentSubmenu.Close();
                _currentSubmenu = null;
            }
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

        void IContextMenuHost.CancelSubmenuSchedule() => CancelSubmenuSchedule();
        void IContextMenuHost.ScheduleHideSubmenu() => ScheduleHideSubmenu();
    }
}
