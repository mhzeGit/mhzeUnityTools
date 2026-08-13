using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gigaduck.HierarchyContextMenu
{
    class SubmenuWindow : EditorWindow
    {
        private ListView _listView;
        private MenuNode _category;
        private IContextMenuHost _parent;
        private SubmenuWindow _nestedSubmenu;
        private SubmenuWindow _parentSubmenu;
        private IVisualElementScheduledItem _submenuSchedule;
        private int _currentNestedIndex = -1;
        private const float ItemHeight = 22f;
        private const float SubmenuWidth = 240f;
        private const long SubmenuDelayMs = 120;

        public static SubmenuWindow Create(IContextMenuHost parent, MenuNode category, Vector2 screenPos, float height, SubmenuWindow parentSubmenu = null)
        {
            var rect = new Rect(screenPos.x, screenPos.y, 1, 1);
            var instance = CreateInstance<SubmenuWindow>();
            instance._parent = parent;
            instance._category = category;
            instance._parentSubmenu = parentSubmenu;
            instance.ShowAsDropDown(rect, new Vector2(SubmenuWidth, Mathf.Max(height, 22f)));
            return instance;
        }

        private void OnDestroy()
        {
            if (_nestedSubmenu != null)
            {
                _nestedSubmenu.Close();
                _nestedSubmenu = null;
            }
            if (_parentSubmenu != null && _parentSubmenu._nestedSubmenu == this)
            {
                _parentSubmenu._nestedSubmenu = null;
                _parentSubmenu._currentNestedIndex = -1;
            }
        }

        private void OnLostFocus()
        {
            if (_nestedSubmenu == null)
                Close();
        }

        private void CreateGUI()
        {
            rootVisualElement.style.backgroundColor = HierarchyContextMenuSettings.BackgroundColor;

            rootVisualElement.style.paddingLeft = 1;
            rootVisualElement.style.paddingRight = 1;
            rootVisualElement.style.paddingTop = 1;
            rootVisualElement.style.paddingBottom = 1;
            rootVisualElement.style.borderTopWidth = 1;
            rootVisualElement.style.borderLeftWidth = 1;
            rootVisualElement.style.borderRightWidth = 1;
            rootVisualElement.style.borderBottomWidth = 1;
            rootVisualElement.style.borderTopColor = HierarchyContextMenuSettings.BorderColor;
            rootVisualElement.style.borderLeftColor = HierarchyContextMenuSettings.BorderColor;
            rootVisualElement.style.borderRightColor = HierarchyContextMenuSettings.BorderColor;
            rootVisualElement.style.borderBottomColor = HierarchyContextMenuSettings.BorderColor;

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
                _parentSubmenu?.CancelSubmenuSchedule();
                CancelSubmenuSchedule();
            });

            rootVisualElement.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (_parentSubmenu != null)
                    _parentSubmenu.ScheduleHideSubmenu();
                else
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
                            ShowNestedSubmenu(idx);
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
                    container.style.backgroundColor = HierarchyContextMenuSettings.HoverColor;
                    if (idx == _currentNestedIndex)
                    {
                        CancelSubmenuSchedule();
                    }
                    else
                    {
                        CancelSubmenuSchedule();
                        _submenuSchedule = rootVisualElement.schedule.Execute(() =>
                        {
                            ShowNestedSubmenu(idx);
                        }).StartingIn(SubmenuDelayMs);
                    }
                }
                else
                {
                    var bg = HierarchyContextMenuSettings.BackgroundColor;
                    container.style.backgroundColor = new Color(
                        Mathf.Min(bg.r + 0.1f, 1f),
                        Mathf.Min(bg.g + 0.1f, 1f),
                        Mathf.Min(bg.b + 0.1f, 1f)
                    );
                }
            });

            container.RegisterCallback<PointerLeaveEvent>(evt =>
            {
                container.style.backgroundColor = new Color(0, 0, 0, 0);
                var idx = (int)container.userData;
                if (idx == _currentNestedIndex)
                {
                    ScheduleHideSubmenu();
                }
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
            var shortcut = element.Q<Label>("item-shortcut");
            var icon = element.Q<VisualElement>("item-icon");

            if (index >= 0 && index < _category.Children.Count)
            {
                var child = _category.Children[index];
                label.text = child.Name;
                arrow.style.display = child.IsCategory ? DisplayStyle.Flex : DisplayStyle.None;
                shortcut.style.display = DisplayStyle.None;
                if (!child.IsCategory && !string.IsNullOrEmpty(child.ShortcutText))
                {
                    shortcut.text = child.ShortcutText;
                    shortcut.style.display = DisplayStyle.Flex;
                }
                if (!HierarchyContextMenuSettings.ShowIcons)
                {
                    icon.style.display = DisplayStyle.None;
                }
                else
                {
                    var iconName = MenuIcons.ResolveIcon(child.Name, child.IsCategory);
                    var desaturate = iconName.Length > 0 && iconName[0] == '!';
                    if (desaturate)
                        iconName = iconName.Substring(1);
                    var tex = desaturate ? MenuIcons.LoadDesaturated(iconName) : MenuIcons.Load(iconName);
                    icon.style.backgroundImage = tex;
                    icon.style.display = tex != null ? DisplayStyle.Flex : DisplayStyle.None;
                    if (tex != null)
                        icon.style.unityBackgroundImageTintColor = Color.white;
                }
            }
        }

        internal void CancelSubmenuSchedule()
        {
            if (_submenuSchedule != null)
            {
                _submenuSchedule.Pause();
                _submenuSchedule = null;
            }
        }

        internal void ScheduleHideSubmenu()
        {
            CancelSubmenuSchedule();
            _submenuSchedule = rootVisualElement.schedule.Execute(HideNestedSubmenu).StartingIn(SubmenuDelayMs);
        }

        private void ShowNestedSubmenu(int index)
        {
            CancelSubmenuSchedule();
            var children = _category.Children;
            if (index >= 0 && index < children.Count)
            {
                var child = children[index];
                if (child.IsCategory)
                {
                    _currentNestedIndex = index;
                    var screenPos = new Vector2(position.x + SubmenuWidth, position.y + (index * ItemHeight));
                    float itemCount = child.Children.Count;
                    float desiredHeight = Mathf.Max((itemCount * ItemHeight) + 5f, 22f);
                    if (_nestedSubmenu != null)
                    {
                        _nestedSubmenu.Close();
                        _nestedSubmenu = null;
                    }
                    var rect = new Rect(screenPos.x, screenPos.y, 1, 1);
                    var submenu = CreateInstance<SubmenuWindow>();
                    submenu._parent = _parent;
                    submenu._category = child;
                    submenu._parentSubmenu = this;
                    _nestedSubmenu = submenu;
                    submenu.ShowAsDropDown(rect, new Vector2(SubmenuWidth, Mathf.Max(desiredHeight, 22f)));
                }
            }
        }

        private void HideNestedSubmenu()
        {
            CancelSubmenuSchedule();
            _currentNestedIndex = -1;
            if (_nestedSubmenu != null)
            {
                _nestedSubmenu.Close();
                _nestedSubmenu = null;
            }
        }
    }
}
