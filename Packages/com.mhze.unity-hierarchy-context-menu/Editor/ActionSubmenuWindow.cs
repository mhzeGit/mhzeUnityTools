using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace mhze.HierarchyContextMenu
{
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
}
