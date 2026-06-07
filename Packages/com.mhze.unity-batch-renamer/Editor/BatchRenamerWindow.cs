using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using Object = UnityEngine.Object;

namespace mhze.BatchRenamer
{
    class BatchRenamerWindow : EditorWindow
    {
        private TextField _searchField;
        private TextField _replaceField;
        private Toggle _caseSensitiveToggle;
        private TextField _prefixField;
        private TextField _suffixField;
        private EnumField _caseField;
        private Toggle _preserveNumbersToggle;
        private EnumField _numberFormatField;
        private VisualElement _previewContainer;
        private ScrollView _previewScrollView;
        private Label _previewHeader;
        private Button _renameButton;
        private Label _statusLabel;

        private readonly RenameProcessor _processor = new RenameProcessor();
        private bool _previewDirty = true;
        private IVisualElementScheduledItem _pendingRefresh;
        private Object[] _pendingObjects;

        private static readonly Color BgDark = new Color(0.12f, 0.12f, 0.12f);
        private static readonly Color BgInput = new Color(0.17f, 0.17f, 0.17f);
        private static readonly Color BorderColor = new Color(0.28f, 0.28f, 0.28f);
        private static readonly Color TextPrimary = new Color(0.85f, 0.85f, 0.85f);
        private static readonly Color TextSecondary = new Color(0.55f, 0.55f, 0.55f);
        private static readonly Color TextDim = new Color(0.4f, 0.4f, 0.4f);
        private static readonly Color AccentBlue = new Color(0.22f, 0.42f, 0.75f);
        private static readonly Color GreenHighlight = new Color(0.4f, 0.9f, 0.4f);
        private static readonly Color RemovedHighlight = new Color(0.85f, 0.45f, 0.1f);
        private static readonly Color MatchHighlight = new Color(0.9f, 0.7f, 0.1f);
        private static readonly Color PreviewBg = new Color(0.1f, 0.1f, 0.1f);

        private readonly HashSet<AssetCategory> _filterSelectedCategories = new HashSet<AssetCategory>();
        private readonly HashSet<TextureSubCategory> _filterTextureSubCategories = new HashSet<TextureSubCategory>();
        private readonly HashSet<HierarchyCategory> _hierarchySelectedCategories = new HashSet<HierarchyCategory>();
        private VisualElement _leftColumn;
        private VisualElement _filterSectionContent;
        private VisualElement _filterDropdownButton;

        private BatchRenamePreset _currentPreset;
        private VisualElement _operationsListSection;
        private ObjectField _presetField;
        private Label _filterSummaryLabel;
        private VisualElement _filterPopup;
        private bool _filterPopupOpen;

        public static void ShowWindow(Object[] selectedObjects)
        {
            Debug.Log($"[BatchRenamer] ShowWindow called with {(selectedObjects != null ? selectedObjects.Length : 0)} objects");
            if (selectedObjects != null)
            {
                for (int i = 0; i < selectedObjects.Length; i++)
                {
                    var obj = selectedObjects[i];
                    var path = obj != null ? AssetDatabase.GetAssetPath(obj) : "NULL_OBJ";
                    Debug.Log($"[BatchRenamer]   ShowWindow obj[{i}] type={obj?.GetType().Name ?? "null"} name='{obj?.name ?? "null"}' path='{path}'");
                }
            }
            var window = GetWindow<BatchRenamerWindow>(true, "Batch Rename");
            window._pendingObjects = selectedObjects;
            window.Show();

            if (window._previewContainer != null)
            {
                window.ShowLoadingState();
                window.rootVisualElement.schedule.Execute(window.ProcessPendingObjects).StartingIn(50);
            }
        }

        private void CreateGUI()
        {
            rootVisualElement.style.backgroundColor = BgDark;
            rootVisualElement.style.paddingLeft = 12;
            rootVisualElement.style.paddingRight = 12;
            rootVisualElement.style.paddingTop = 8;
            rootVisualElement.style.paddingBottom = 8;

            BuildHeader();

            var mainRow = new VisualElement();
            mainRow.style.flexDirection = FlexDirection.Row;
            mainRow.style.flexGrow = 1;
            mainRow.style.minHeight = 0;

            var leftColumn = new VisualElement();
            _leftColumn = leftColumn;
            leftColumn.style.flexDirection = FlexDirection.Column;
            leftColumn.style.flexGrow = 0;
            leftColumn.style.flexShrink = 0;
            leftColumn.style.marginRight = 12;
            leftColumn.style.minWidth = 280;
            leftColumn.style.maxWidth = 380;

            var rightColumn = new VisualElement();
            rightColumn.style.flexDirection = FlexDirection.Column;
            rightColumn.style.flexGrow = 1;
            rightColumn.style.flexShrink = 1;
            rightColumn.style.minWidth = 300;
            rightColumn.style.minHeight = 0;

            BuildSearchReplaceSection(leftColumn);
            BuildFilterSection(leftColumn);
            BuildModifySection(leftColumn);
            BuildPresetSection(leftColumn);
            BuildNumberSection(leftColumn);
            BuildHelpSection(leftColumn);

            BuildPreviewSection(rightColumn);
            BuildActionsSection(rightColumn);

            mainRow.Add(leftColumn);
            mainRow.Add(rightColumn);
            rootVisualElement.Add(mainRow);

            rootVisualElement.schedule.Execute(() =>
            {
                if (_leftColumn != null && _leftColumn.childCount > 0)
                    AdjustWindowSize();
            }).StartingIn(50);

            Debug.Log($"[BatchRenamer] CreateGUI: _previewDirty={_previewDirty}, Items.Count={_processor.Items.Count}");
            if (_pendingObjects != null)
            {
                ShowLoadingState();
                rootVisualElement.schedule.Execute(ProcessPendingObjects).StartingIn(50);
            }
            else if (_previewDirty)
            {
                Debug.Log($"[BatchRenamer] CreateGUI: calling RefreshPreview from _previewDirty, Items.Count={_processor.Items.Count}");
                RefreshPreview();
                _previewDirty = false;
            }
        }

        private void OnSelectionChange()
        {
            if (_processor == null || _previewContainer == null) return;

            var selObjs = BatchRenamer.GetSelectedProjectAssets();
            Debug.Log($"[BatchRenamer] OnSelectionChange fired, assetGUIDs based objects count={selObjs?.Length}, _previewContainer={_previewContainer != null}");

            bool hasFolders = selObjs != null && selObjs.Any(o =>
            {
                var p = AssetDatabase.GetAssetPath(o);
                return !string.IsNullOrEmpty(p) && AssetDatabase.IsValidFolder(p);
            });

            if (hasFolders)
            {
                _pendingObjects = selObjs;
                ShowLoadingState();
                rootVisualElement.schedule.Execute(ProcessPendingObjects).StartingIn(50);
            }
            else
            {
                _processor.CollectFromObjects(selObjs);
                RefreshFilterUI();
                MarkPreviewDirty();
            }
        }

        private void ShowLoadingState()
        {
            if (_previewHeader != null)
                _previewHeader.text = "Preview (loading...)";
            if (_previewContainer != null)
            {
                _previewContainer.Clear();
                var loading = new Label("Loading assets...");
                loading.style.color = TextSecondary;
                loading.style.fontSize = 14;
                loading.style.paddingTop = 30;
                loading.style.unityTextAlign = TextAnchor.MiddleCenter;
                loading.style.alignSelf = Align.Center;
                _previewContainer.Add(loading);
            }
            if (_renameButton != null)
                _renameButton.SetEnabled(false);
            if (_statusLabel != null)
                _statusLabel.text = "Loading...";
        }

        private void ProcessPendingObjects()
        {
            if (_pendingObjects == null) return;
            var objects = _pendingObjects;
            _pendingObjects = null;
            Debug.Log($"[BatchRenamer] ProcessPendingObjects: processing {objects.Length} objects");
            _processor.CollectFromObjects(objects);
            RefreshFilterUI();
            RefreshPreview();
        }

        private void AdjustWindowSize()
        {
            if (_leftColumn == null) return;

            float controlsExtent = 0;
            foreach (var child in _leftColumn.Children())
            {
                float childBottom = child.layout.y + child.layout.height;
                float childMarginBottom = child.resolvedStyle.marginBottom;
                controlsExtent = Mathf.Max(controlsExtent, childBottom + childMarginBottom);
            }

            if (controlsExtent <= 0)
            {
                rootVisualElement.schedule.Execute(AdjustWindowSize).StartingIn(100);
                return;
            }

            float headerHeight = 50;
            float rootPadding = 16;
            float minPreviewHeight = 180;
            float actionsHeight = 36;

            float requiredHeight = Mathf.Ceil(rootPadding + headerHeight + controlsExtent + minPreviewHeight + actionsHeight);

            float leftWidth = _leftColumn.resolvedStyle.width;
            if (leftWidth <= 0) leftWidth = 320;
            float rightMinWidth = 300;
            float gap = 12;
            float hPadding = 24;
            float requiredWidth = Mathf.Ceil(leftWidth + gap + rightMinWidth + hPadding);

            minSize = new Vector2(requiredWidth, requiredHeight);
            maxSize = new Vector2(Mathf.Max(2000, requiredWidth), Mathf.Max(2000, requiredHeight));

            var pos = position;
            pos.width = Mathf.Max(pos.width, requiredWidth);
            pos.height = Mathf.Max(pos.height, requiredHeight);
            position = pos;
        }

        private void BuildHeader()
        {
            var header = new Label("Batch Rename");
            header.style.fontSize = 18;
            header.style.color = TextPrimary;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 12;
            header.style.paddingBottom = 8;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = BorderColor;
            rootVisualElement.Add(header);
        }

        private void BuildSearchReplaceSection(VisualElement parent)
        {
            var section = CreateSection();

            _searchField = CreateLabelledField(section, "Search", "Search for text...");
            _searchField.RegisterValueChangedCallback(_ => MarkPreviewDirty());

            _replaceField = CreateLabelledField(section, "Replace", "Replace with...");
            _replaceField.RegisterValueChangedCallback(_ => MarkPreviewDirty());

            var csRow = new VisualElement();
            csRow.style.flexDirection = FlexDirection.Row;
            csRow.style.alignItems = Align.Center;
            csRow.style.marginBottom = 4;

            var csSpacer = new Label("");
            csSpacer.style.minWidth = 70;
            csRow.Add(csSpacer);

            _caseSensitiveToggle = new Toggle("Case Sensitive");
            _caseSensitiveToggle.value = false;
            var csLabel = _caseSensitiveToggle.Q<Label>();
            if (csLabel != null)
            {
                csLabel.style.fontSize = 12;
                csLabel.style.color = TextPrimary;
                csLabel.style.marginLeft = 4;
            }
            _caseSensitiveToggle.RegisterValueChangedCallback(_ => MarkPreviewDirty());
            csRow.Add(_caseSensitiveToggle);
            section.Add(csRow);



            parent.Add(section);
        }

        private void BuildFilterSection(VisualElement parent)
        {
            var section = CreateSection();
            CreateSectionHeader(section, "Filters");

            _filterSectionContent = new VisualElement();
            section.Add(_filterSectionContent);
            parent.Add(section);

            RefreshFilterUI();
        }

        private void RefreshFilterUI()
        {
            _filterSectionContent.Clear();
            _filterDropdownButton = null;
            _filterSummaryLabel = null;
            CloseFilterPopup();

            if (_processor.IsHierarchyMode)
            {
                BuildHierarchyFilterUI(_filterSectionContent);
            }
            else
            {
                BuildProjectFilterUI(_filterSectionContent);
            }
        }

        private void BuildHierarchyFilterUI(VisualElement container)
        {
            _hierarchySelectedCategories.Clear();
            var categories = (HierarchyCategory[])Enum.GetValues(typeof(HierarchyCategory));
            foreach (var cat in categories)
            {
                if (cat != HierarchyCategory.All)
                    _hierarchySelectedCategories.Add(cat);
            }
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 26;
            row.name = "hierarchy-filter-row";

            var spacer = new Label("");
            spacer.style.minWidth = 70;
            row.Add(spacer);

            _filterDropdownButton = new VisualElement();
            _filterDropdownButton.style.flexGrow = 1;
            _filterDropdownButton.style.borderTopWidth = 1;
            _filterDropdownButton.style.borderLeftWidth = 1;
            _filterDropdownButton.style.borderRightWidth = 1;
            _filterDropdownButton.style.borderBottomWidth = 1;
            _filterDropdownButton.style.borderTopColor = BorderColor;
            _filterDropdownButton.style.borderLeftColor = BorderColor;
            _filterDropdownButton.style.borderRightColor = BorderColor;
            _filterDropdownButton.style.borderBottomColor = BorderColor;
            _filterDropdownButton.style.backgroundColor = BgInput;
            _filterDropdownButton.style.paddingLeft = 8;
            _filterDropdownButton.style.paddingRight = 8;
            _filterDropdownButton.style.paddingTop = 4;
            _filterDropdownButton.style.paddingBottom = 4;
            _filterDropdownButton.style.flexDirection = FlexDirection.Row;
            _filterDropdownButton.style.justifyContent = Justify.SpaceBetween;
            _filterDropdownButton.style.alignItems = Align.Center;
            _filterDropdownButton.RegisterCallback<MouseDownEvent>(_ => ToggleFilterPopup());

            _filterSummaryLabel = new Label("All");
            _filterSummaryLabel.style.fontSize = 12;
            _filterSummaryLabel.style.color = TextPrimary;
            _filterSummaryLabel.style.flexGrow = 1;
            _filterSummaryLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _filterDropdownButton.Add(_filterSummaryLabel);

            var arrow = new Label("\u25BC");
            arrow.style.fontSize = 9;
            arrow.style.color = TextSecondary;
            arrow.style.marginLeft = 6;
            arrow.style.flexShrink = 0;
            _filterDropdownButton.Add(arrow);

            row.Add(_filterDropdownButton);
            container.Add(row);
        }

        private void BuildProjectFilterUI(VisualElement container)
        {
            _filterSelectedCategories.Clear();
            _filterTextureSubCategories.Clear();
            var categories = (AssetCategory[])Enum.GetValues(typeof(AssetCategory));
            foreach (var cat in categories)
            {
                if (cat != AssetCategory.All)
                    _filterSelectedCategories.Add(cat);
            }
            var textureSubTypes = (TextureSubCategory[])Enum.GetValues(typeof(TextureSubCategory));
            foreach (var sub in textureSubTypes)
                _filterTextureSubCategories.Add(sub);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 26;
            row.name = "filter-row";

            var spacer = new Label("");
            spacer.style.minWidth = 70;
            row.Add(spacer);

            _filterDropdownButton = new VisualElement();
            _filterDropdownButton.style.flexGrow = 1;
            _filterDropdownButton.style.borderTopWidth = 1;
            _filterDropdownButton.style.borderLeftWidth = 1;
            _filterDropdownButton.style.borderRightWidth = 1;
            _filterDropdownButton.style.borderBottomWidth = 1;
            _filterDropdownButton.style.borderTopColor = BorderColor;
            _filterDropdownButton.style.borderLeftColor = BorderColor;
            _filterDropdownButton.style.borderRightColor = BorderColor;
            _filterDropdownButton.style.borderBottomColor = BorderColor;
            _filterDropdownButton.style.backgroundColor = BgInput;
            _filterDropdownButton.style.paddingLeft = 8;
            _filterDropdownButton.style.paddingRight = 8;
            _filterDropdownButton.style.paddingTop = 4;
            _filterDropdownButton.style.paddingBottom = 4;
            _filterDropdownButton.style.flexDirection = FlexDirection.Row;
            _filterDropdownButton.style.justifyContent = Justify.SpaceBetween;
            _filterDropdownButton.style.alignItems = Align.Center;
            _filterDropdownButton.RegisterCallback<MouseDownEvent>(_ => ToggleFilterPopup());

            _filterSummaryLabel = new Label("All");
            _filterSummaryLabel.style.fontSize = 12;
            _filterSummaryLabel.style.color = TextPrimary;
            _filterSummaryLabel.style.flexGrow = 1;
            _filterSummaryLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _filterDropdownButton.Add(_filterSummaryLabel);

            var arrow = new Label("\u25BC");
            arrow.style.fontSize = 9;
            arrow.style.color = TextSecondary;
            arrow.style.marginLeft = 6;
            arrow.style.flexShrink = 0;
            _filterDropdownButton.Add(arrow);

            row.Add(_filterDropdownButton);
            container.Add(row);
        }

        private void ToggleFilterPopup()
        {
            if (_filterPopupOpen)
                CloseFilterPopup();
            else if (_processor.IsHierarchyMode)
                OpenHierarchyFilterPopup();
            else
                OpenFilterPopup();
        }

        private void OpenFilterPopup()
        {
            _filterPopupOpen = true;

            var popup = new VisualElement();
            popup.name = "filter-section-popup";
            popup.style.position = Position.Absolute;
            popup.style.left = 0;
            popup.style.top = _filterDropdownButton.layout.height;
            popup.style.width = _filterDropdownButton.layout.width;
            popup.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            popup.style.borderTopWidth = 1;
            popup.style.borderLeftWidth = 1;
            popup.style.borderRightWidth = 1;
            popup.style.borderBottomWidth = 1;
            popup.style.borderTopColor = BorderColor;
            popup.style.borderLeftColor = BorderColor;
            popup.style.borderRightColor = BorderColor;
            popup.style.borderBottomColor = BorderColor;
            popup.style.paddingTop = 4;
            popup.style.paddingBottom = 4;
            popup.style.flexDirection = FlexDirection.Column;

            var noneRow = new VisualElement();
            noneRow.style.flexDirection = FlexDirection.Row;
            noneRow.style.alignItems = Align.Center;
            noneRow.style.paddingLeft = 8;
            noneRow.style.paddingRight = 8;
            noneRow.style.paddingTop = 2;
            noneRow.style.paddingBottom = 2;
            noneRow.style.minHeight = 22;
            noneRow.name = "filter-none-row";
            noneRow.RegisterCallback<MouseDownEvent>(_ => SelectNoneFilter());

            var noneLabel = new Label("None");
            noneLabel.style.fontSize = 12;
            noneLabel.style.color = TextDim;
            noneRow.Add(noneLabel);
            popup.Add(noneRow);

            var sep = new VisualElement();
            sep.style.height = 1;
            sep.style.backgroundColor = BorderColor;
            sep.style.marginLeft = 4;
            sep.style.marginRight = 4;
            sep.style.marginTop = 2;
            sep.style.marginBottom = 2;
            popup.Add(sep);

            var categories = (AssetCategory[])Enum.GetValues(typeof(AssetCategory));
            VisualElement textureSubContainer = null;
            foreach (var cat in categories)
            {
                if (cat == AssetCategory.All) continue;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.paddingLeft = 8;
                row.style.paddingRight = 4;
                row.style.paddingTop = 2;
                row.style.paddingBottom = 2;
                row.style.minHeight = 22;

                var iconImage = new Image();
                iconImage.image = GetAssetCategoryIcon(cat);
                iconImage.style.width = 16;
                iconImage.style.height = 16;
                iconImage.style.marginRight = 4;
                iconImage.style.flexShrink = 0;
                row.Add(iconImage);

                var toggleLabel = new Label(cat.ToString());
                toggleLabel.style.fontSize = 12;
                toggleLabel.style.color = TextPrimary;
                toggleLabel.style.marginLeft = 4;
                toggleLabel.style.flexGrow = 1;
                row.Add(toggleLabel);

                var toggle = new Toggle();
                toggle.value = _filterSelectedCategories.Contains(cat);
                toggle.style.flexShrink = 0;
                toggle.style.paddingLeft = 0;
                toggle.style.paddingRight = 0;
                toggle.style.marginLeft = 0;
                toggle.style.marginRight = 0;
                toggle.style.marginTop = 0;
                toggle.style.marginBottom = 0;
                row.Add(toggle);

                var toggleInput = toggle.Q(classes: "unity-base-field__input");
                if (toggleInput != null)
                {
                    toggleInput.style.paddingLeft = 0;
                    toggleInput.style.paddingRight = 0;
                    toggleInput.style.marginLeft = 0;
                    toggleInput.style.marginRight = 0;
                }

                var capturedCat = cat;
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                        _filterSelectedCategories.Add(capturedCat);
                    else
                        _filterSelectedCategories.Remove(capturedCat);

                    if (capturedCat == AssetCategory.Texture && textureSubContainer != null)
                    {
                        textureSubContainer.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                        if (evt.newValue)
                        {
                            var subTypes = (TextureSubCategory[])Enum.GetValues(typeof(TextureSubCategory));
                            foreach (var sub in subTypes)
                                _filterTextureSubCategories.Add(sub);
                            var subToggles = textureSubContainer.Query<Toggle>().ToList();
                            foreach (var st in subToggles)
                                st.value = true;
                        }
                        else
                        {
                            _filterTextureSubCategories.Clear();
                        }
                    }

                    UpdateFilterSummary();
                    MarkPreviewDirty();
                });

                row.RegisterCallback<MouseDownEvent>(evt =>
                {
                    var target = evt.target as VisualElement;
                    if (target == toggle || toggle.Contains(target))
                        return;
                    toggle.value = !toggle.value;
                });

                popup.Add(row);

                if (cat == AssetCategory.Texture)
                {
                    textureSubContainer = new VisualElement();
                    textureSubContainer.style.paddingLeft = 28;
                    textureSubContainer.style.display = toggle.value ? DisplayStyle.Flex : DisplayStyle.None;

                    var subTypes = (TextureSubCategory[])Enum.GetValues(typeof(TextureSubCategory));
                    foreach (var sub in subTypes)
                    {
                        var subRow = new VisualElement();
                        subRow.style.flexDirection = FlexDirection.Row;
                        subRow.style.alignItems = Align.Center;
                        subRow.style.paddingRight = 4;
                        subRow.style.paddingTop = 2;
                        subRow.style.paddingBottom = 2;
                        subRow.style.minHeight = 22;

                        var subToggle = new Toggle();
                        subToggle.value = _filterTextureSubCategories.Contains(sub);
                        subToggle.style.flexShrink = 0;
                        subToggle.style.paddingLeft = 0;
                        subToggle.style.paddingRight = 0;
                        subToggle.style.marginLeft = 0;
                        subToggle.style.marginRight = 0;
                        subToggle.style.marginTop = 0;
                        subToggle.style.marginBottom = 0;
                        subRow.Add(subToggle);

                        var subToggleInput = subToggle.Q(classes: "unity-base-field__input");
                        if (subToggleInput != null)
                        {
                            subToggleInput.style.paddingLeft = 0;
                            subToggleInput.style.paddingRight = 0;
                            subToggleInput.style.marginLeft = 0;
                            subToggleInput.style.marginRight = 0;
                        }

                        var subLabel = new Label(sub.ToString());
                        subLabel.style.fontSize = 12;
                        subLabel.style.color = TextPrimary;
                        subLabel.style.marginLeft = 4;
                        subLabel.style.flexGrow = 1;
                        subRow.Add(subLabel);

                        var capturedSub = sub;
                        subToggle.RegisterValueChangedCallback(evt =>
                        {
                            if (evt.newValue)
                                _filterTextureSubCategories.Add(capturedSub);
                            else
                                _filterTextureSubCategories.Remove(capturedSub);
                            MarkPreviewDirty();
                        });

                        subRow.RegisterCallback<MouseDownEvent>(evt =>
                        {
                            var target = evt.target as VisualElement;
                            if (target == subToggle || subToggle.Contains(target))
                                return;
                            subToggle.value = !subToggle.value;
                        });

                        textureSubContainer.Add(subRow);
                    }

                    popup.Add(textureSubContainer);
                }
            }

            _filterPopup = popup;

            var btnWorld = _filterDropdownButton.worldBound;
            var rootWorld = rootVisualElement.worldBound;
            popup.style.left = btnWorld.x - rootWorld.x;
            popup.style.top = btnWorld.yMax - rootWorld.y;
            popup.style.width = btnWorld.width;
            rootVisualElement.Add(popup);

            RegisterClickAwayHandler();
        }

        private void OpenHierarchyFilterPopup()
        {
            _filterPopupOpen = true;

            var popup = new VisualElement();
            popup.name = "filter-section-popup-hierarchy";
            popup.style.position = Position.Absolute;
            popup.style.left = 0;
            popup.style.top = _filterDropdownButton.layout.height;
            popup.style.width = _filterDropdownButton.layout.width;
            popup.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            popup.style.borderTopWidth = 1;
            popup.style.borderLeftWidth = 1;
            popup.style.borderRightWidth = 1;
            popup.style.borderBottomWidth = 1;
            popup.style.borderTopColor = BorderColor;
            popup.style.borderLeftColor = BorderColor;
            popup.style.borderRightColor = BorderColor;
            popup.style.borderBottomColor = BorderColor;
            popup.style.paddingTop = 4;
            popup.style.paddingBottom = 4;
            popup.style.flexDirection = FlexDirection.Column;

            var noneRow = new VisualElement();
            noneRow.style.flexDirection = FlexDirection.Row;
            noneRow.style.alignItems = Align.Center;
            noneRow.style.paddingLeft = 8;
            noneRow.style.paddingRight = 8;
            noneRow.style.paddingTop = 2;
            noneRow.style.paddingBottom = 2;
            noneRow.style.minHeight = 22;
            noneRow.name = "hierarchy-filter-none-row";
            noneRow.RegisterCallback<MouseDownEvent>(_ => SelectNoneHierarchyFilter());

            var noneLabel = new Label("None");
            noneLabel.style.fontSize = 12;
            noneLabel.style.color = TextDim;
            noneRow.Add(noneLabel);
            popup.Add(noneRow);

            var sep = new VisualElement();
            sep.style.height = 1;
            sep.style.backgroundColor = BorderColor;
            sep.style.marginLeft = 4;
            sep.style.marginRight = 4;
            sep.style.marginTop = 2;
            sep.style.marginBottom = 2;
            popup.Add(sep);

            var categories = (HierarchyCategory[])Enum.GetValues(typeof(HierarchyCategory));
            foreach (var cat in categories)
            {
                if (cat == HierarchyCategory.All) continue;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.paddingLeft = 8;
                row.style.paddingRight = 4;
                row.style.paddingTop = 2;
                row.style.paddingBottom = 2;
                row.style.minHeight = 22;

                var iconImage = new Image();
                iconImage.image = GetHierarchyCategoryIcon(cat);
                iconImage.style.width = 16;
                iconImage.style.height = 16;
                iconImage.style.marginRight = 4;
                iconImage.style.flexShrink = 0;
                row.Add(iconImage);

                var toggleLabel = new Label(cat.ToString());
                toggleLabel.style.fontSize = 12;
                toggleLabel.style.color = TextPrimary;
                toggleLabel.style.marginLeft = 4;
                toggleLabel.style.flexGrow = 1;
                row.Add(toggleLabel);

                var toggle = new Toggle();
                toggle.value = _hierarchySelectedCategories.Contains(cat);
                toggle.style.flexShrink = 0;
                toggle.style.paddingLeft = 0;
                toggle.style.paddingRight = 0;
                toggle.style.marginLeft = 0;
                toggle.style.marginRight = 0;
                toggle.style.marginTop = 0;
                toggle.style.marginBottom = 0;
                row.Add(toggle);

                var toggleInput = toggle.Q(classes: "unity-base-field__input");
                if (toggleInput != null)
                {
                    toggleInput.style.paddingLeft = 0;
                    toggleInput.style.paddingRight = 0;
                    toggleInput.style.marginLeft = 0;
                    toggleInput.style.marginRight = 0;
                }

                var capturedCat = cat;
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                        _hierarchySelectedCategories.Add(capturedCat);
                    else
                        _hierarchySelectedCategories.Remove(capturedCat);
                    UpdateFilterSummary();
                    MarkPreviewDirty();
                });

                row.RegisterCallback<MouseDownEvent>(evt =>
                {
                    var target = evt.target as VisualElement;
                    if (target == toggle || toggle.Contains(target))
                        return;
                    toggle.value = !toggle.value;
                });

                popup.Add(row);
            }

            _filterPopup = popup;

            var btnWorld = _filterDropdownButton.worldBound;
            var rootWorld = rootVisualElement.worldBound;
            popup.style.left = btnWorld.x - rootWorld.x;
            popup.style.top = btnWorld.yMax - rootWorld.y;
            popup.style.width = btnWorld.width;
            rootVisualElement.Add(popup);

            RegisterClickAwayHandler();
        }

        private void RegisterClickAwayHandler()
        {
            rootVisualElement.RegisterCallback<MouseDownEvent>(OnRootMouseDown);
        }

        private void OnRootMouseDown(MouseDownEvent evt)
        {
            rootVisualElement.UnregisterCallback<MouseDownEvent>(OnRootMouseDown);

            var target = evt.target as VisualElement;
            if (target != null && _filterPopup != null)
            {
                if (_filterDropdownButton != null && (target == _filterDropdownButton || _filterDropdownButton.Contains(target)))
                    return;
                if (_filterPopup.Contains(target) || target == _filterPopup)
                    return;
            }

            CloseFilterPopup();
        }

        private void CloseFilterPopup()
        {
            _filterPopupOpen = false;
            rootVisualElement.UnregisterCallback<MouseDownEvent>(OnRootMouseDown);

            if (_filterPopup != null && _filterPopup.parent != null)
                _filterPopup.parent.Remove(_filterPopup);
            _filterPopup = null;
        }

        private void SelectNoneFilter()
        {
            if (_processor.IsHierarchyMode)
            {
                _hierarchySelectedCategories.Clear();
            }
            else
            {
                _filterSelectedCategories.Clear();
                _filterTextureSubCategories.Clear();
            }
            CloseFilterPopup();
            UpdateFilterSummary();
            MarkPreviewDirty();
        }

        private void SelectNoneHierarchyFilter()
        {
            _hierarchySelectedCategories.Clear();
            CloseFilterPopup();
            UpdateFilterSummary();
            MarkPreviewDirty();
        }

        private void UpdateFilterSummary()
        {
            if (_processor.IsHierarchyMode)
            {
                int count = _hierarchySelectedCategories.Count;
                int total = Enum.GetValues(typeof(HierarchyCategory)).Length - 1;

                if (count == total)
                    _filterSummaryLabel.text = "All";
                else if (count == 0)
                    _filterSummaryLabel.text = "None";
                else
                    _filterSummaryLabel.text = $"{count} selected";
            }
            else
            {
                int count = _filterSelectedCategories.Count;
                int total = Enum.GetValues(typeof(AssetCategory)).Length - 1;

                if (count == total)
                    _filterSummaryLabel.text = "All";
                else if (count == 0)
                    _filterSummaryLabel.text = "None";
                else
                    _filterSummaryLabel.text = $"{count} selected";
            }
        }

        private static Texture2D GetAssetCategoryIcon(AssetCategory cat)
        {
            var name = cat switch
            {
                AssetCategory.Prefab => "Prefab Icon",
                AssetCategory.Material => "d_Material Icon",
                AssetCategory.Texture => "Texture2D Icon",
                AssetCategory.Model => "PrefabModel Icon",
                AssetCategory.Audio => "AudioSource Icon",
                AssetCategory.Script => "cs Script Icon",
                AssetCategory.AnimationClip => "d_AnimationClip Icon",
                AssetCategory.AnimationController => "d_AnimatorController Icon",
                AssetCategory.Folder => "Folder Icon",
                AssetCategory.Scene => "UnityEditor.SceneView",
                AssetCategory.GameObject => "GameObject Icon",
                AssetCategory.Other => "d_DefaultAsset Icon",
                _ => "d_DefaultAsset Icon",
            };
            return EditorGUIUtility.IconContent(name).image as Texture2D;
        }

        private static Texture2D GetHierarchyCategoryIcon(HierarchyCategory cat)
        {
            var name = cat switch
            {
                HierarchyCategory.MeshRenderer => "MeshRenderer Icon",
                HierarchyCategory.MeshFilter => "MeshFilter Icon",
                HierarchyCategory.Collider => "d_BoxCollider Icon",
                HierarchyCategory.Rigidbody => "d_Rigidbody Icon",
                HierarchyCategory.Animator => "d_AnimatorController Icon",
                HierarchyCategory.AudioSource => "AudioSource Icon",
                HierarchyCategory.Light => "Light Icon",
                HierarchyCategory.Camera => "Camera Icon",
                HierarchyCategory.ParticleSystem => "ParticleShapeTool",
                HierarchyCategory.Canvas => "Canvas Icon",
                HierarchyCategory.Script => "cs Script Icon",
                HierarchyCategory.Empty => "GameObject Icon",
                _ => "GameObject Icon",
            };
            return EditorGUIUtility.IconContent(name).image as Texture2D;
        }

        private void BuildModifySection(VisualElement parent)
        {
            var section = CreateSection();
            CreateSectionHeader(section, "Modify");

            _prefixField = CreateLabelledField(section, "Prefix", "Add prefix...");
            _prefixField.RegisterValueChangedCallback(_ => MarkPreviewDirty());

            _suffixField = CreateLabelledField(section, "Suffix", "Add suffix...");
            _suffixField.RegisterValueChangedCallback(_ => MarkPreviewDirty());

            _caseField = new EnumField("Case", TextCaseMode.None);
            _caseField.label = "Case";
            _caseField.style.marginBottom = 4;
            _caseField.RegisterValueChangedCallback(_ => MarkPreviewDirty());
            StyleField(_caseField);
            section.Add(_caseField);

            parent.Add(section);
        }

        private void BuildNumberSection(VisualElement parent)
        {
            var section = CreateSection();
            CreateSectionHeader(section, "Numbers");

            _preserveNumbersToggle = new Toggle("Detect & Preserve Numbers");
            var pnLabel = _preserveNumbersToggle.Q<Label>();
            if (pnLabel != null)
            {
                pnLabel.style.fontSize = 12;
                pnLabel.style.color = TextPrimary;
                pnLabel.style.marginLeft = 4;
            }
            _preserveNumbersToggle.RegisterValueChangedCallback(evt =>
            {
                _numberFormatField.SetEnabled(evt.newValue);
                MarkPreviewDirty();
            });
            section.Add(_preserveNumbersToggle);

            _numberFormatField = new EnumField("Format", NumberFormatPreset.UnderscoreN);
            _numberFormatField.label = "Format";
            _numberFormatField.style.marginTop = 4;
            _numberFormatField.style.marginBottom = 4;
            _numberFormatField.RegisterValueChangedCallback(_ => MarkPreviewDirty());
            StyleField(_numberFormatField);
            section.Add(_numberFormatField);

            parent.Add(section);
        }

        private void BuildHelpSection(VisualElement parent)
        {
            var section = new VisualElement();
            section.style.marginTop = 8;
            section.style.marginBottom = 4;
            section.style.paddingLeft = 4;
            section.style.borderTopWidth = 1;
            section.style.borderTopColor = BorderColor;
            section.style.paddingTop = 6;

            var header = new Label("Search Operators");
            header.style.fontSize = 12;
            header.style.color = TextPrimary;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 4;
            section.Add(header);

            var items = new (string glyph, string desc)[]
            {
                ("||", "OR \u2014 matches either term (e.g. Apple||Carrot)"),
                ("&&", "AND \u2014 matches only if both terms present (e.g. Apple&&Carrot)"),
                ("!", "NOT \u2014 excludes matches containing the term"),
                ("[]", "Group \u2014 precedence grouping for complex expressions"),
                ("{Number}", "Digits \u2014 inserts/preserves digit sequences"),
                ("?", "Condition \u2014 ternary: ?term:ifTrue:ifFalse"),
            };

            foreach (var (glyph, desc) in items)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = 2;
                row.style.alignItems = Align.FlexStart;

                var glyphLabel = new Label(glyph);
                glyphLabel.style.fontSize = 11;
                glyphLabel.style.color = AccentBlue;
                glyphLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                glyphLabel.style.minWidth = 30;
                glyphLabel.style.marginRight = 6;
                glyphLabel.style.marginTop = 0;
                row.Add(glyphLabel);

                var descLabel = new Label(desc);
                descLabel.style.fontSize = 11;
                descLabel.style.color = TextDim;
                descLabel.style.whiteSpace = WhiteSpace.Normal;
                descLabel.style.flexShrink = 1;
                row.Add(descLabel);

                section.Add(row);
            }

            parent.Add(section);
        }

        private void BuildPreviewSection(VisualElement parent)
        {
            var section = new VisualElement();
            section.style.flexGrow = 1;
            section.style.display = DisplayStyle.Flex;
            section.style.flexDirection = FlexDirection.Column;
            section.style.minHeight = 0;

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.justifyContent = Justify.SpaceBetween;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 6;

            _previewHeader = new Label($"Preview ({_processor.Items.Count} items)");
            _previewHeader.style.fontSize = 13;
            _previewHeader.style.color = TextPrimary;
            _previewHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerRow.Add(_previewHeader);

            var showFilteredToggle = new Toggle("Show filtered");
            showFilteredToggle.name = "show-filtered-toggle";
            showFilteredToggle.value = true;
            showFilteredToggle.style.unityTextAlign = TextAnchor.MiddleRight;
            var siLabel = showFilteredToggle.Q<Label>();
            if (siLabel != null)
            {
                siLabel.style.fontSize = 11;
                siLabel.style.color = TextSecondary;
                siLabel.style.marginLeft = 4;
            }
            showFilteredToggle.RegisterValueChangedCallback(_ => RefreshPreview());
            headerRow.Add(showFilteredToggle);

            section.Add(headerRow);

            var columnHeaderRow = new VisualElement();
            columnHeaderRow.style.flexDirection = FlexDirection.Row;
            columnHeaderRow.style.marginBottom = 4;
            columnHeaderRow.style.paddingLeft = 12;
            columnHeaderRow.style.paddingRight = 12;

            var beforeHeader = new Label("Before");
            beforeHeader.style.fontSize = 11;
            beforeHeader.style.color = TextSecondary;
            beforeHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            beforeHeader.style.flexGrow = 1;
            beforeHeader.style.flexBasis = 0;
            columnHeaderRow.Add(beforeHeader);

            var afterHeader = new Label("After");
            afterHeader.style.fontSize = 11;
            afterHeader.style.color = TextSecondary;
            afterHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            afterHeader.style.flexGrow = 1;
            afterHeader.style.flexBasis = 0;
            columnHeaderRow.Add(afterHeader);

            section.Add(columnHeaderRow);

            _previewScrollView = new ScrollView(ScrollViewMode.Vertical);
            _previewScrollView.style.flexGrow = 1;
            _previewScrollView.style.backgroundColor = PreviewBg;
            _previewScrollView.style.borderTopWidth = 1;
            _previewScrollView.style.borderLeftWidth = 1;
            _previewScrollView.style.borderRightWidth = 1;
            _previewScrollView.style.borderBottomWidth = 1;
            _previewScrollView.style.borderTopColor = BorderColor;
            _previewScrollView.style.borderLeftColor = BorderColor;
            _previewScrollView.style.borderRightColor = BorderColor;
            _previewScrollView.style.borderBottomColor = BorderColor;
            _previewScrollView.style.minHeight = 60;
            _previewScrollView.style.marginBottom = 8;
            _previewScrollView.style.paddingTop = 4;
            _previewScrollView.style.paddingBottom = 4;

            var scrollContent = _previewScrollView.contentContainer;
            scrollContent.style.paddingLeft = 8;
            scrollContent.style.paddingRight = 8;

            _previewContainer = new VisualElement();
            _previewScrollView.Add(_previewContainer);
            section.Add(_previewScrollView);

            parent.Add(section);
        }

        private void BuildActionsSection(VisualElement parent)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.justifyContent = Justify.FlexEnd;
            container.style.alignItems = Align.Center;
            container.style.marginTop = 0;
            container.style.marginBottom = 4;
            container.style.flexShrink = 0;

            _statusLabel = new Label("");
            _statusLabel.style.fontSize = 11;
            _statusLabel.style.color = TextSecondary;
            _statusLabel.style.marginRight = 12;
            _statusLabel.style.flexGrow = 1;
            container.Add(_statusLabel);

            _renameButton = new Button(OnRenameClicked);
            _renameButton.text = "Rename Selected";
            _renameButton.style.backgroundColor = AccentBlue;
            _renameButton.style.color = Color.white;
            _renameButton.style.fontSize = 13;
            _renameButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _renameButton.style.paddingLeft = 20;
            _renameButton.style.paddingRight = 20;
            _renameButton.style.paddingTop = 8;
            _renameButton.style.paddingBottom = 8;
            _renameButton.style.borderTopWidth = 1;
            _renameButton.style.borderLeftWidth = 1;
            _renameButton.style.borderRightWidth = 1;
            _renameButton.style.borderBottomWidth = 1;
            _renameButton.style.borderTopColor = BorderColor;
            _renameButton.style.borderLeftColor = BorderColor;
            _renameButton.style.borderRightColor = BorderColor;
            _renameButton.style.borderBottomColor = BorderColor;
            _renameButton.style.unityTextAlign = TextAnchor.MiddleCenter;
            container.Add(_renameButton);

            parent.Add(container);
        }

        private void StyleField(VisualElement field)
        {
            field.style.flexDirection = FlexDirection.Row;
            field.style.alignItems = Align.Center;

            var label = field.Q<Label>();
            if (label != null)
            {
                label.style.minWidth = 80;
                label.style.fontSize = 12;
                label.style.color = TextPrimary;
            }
        }

        private TextField CreateLabelledField(VisualElement parent, string labelText, string placeholder)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;
            row.style.minHeight = 26;

            var label = new Label(labelText);
            label.style.minWidth = 70;
            label.style.fontSize = 12;
            label.style.color = TextPrimary;
            row.Add(label);

            var field = new TextField();
            field.style.flexGrow = 1;
            field.style.borderTopWidth = 0;
            field.style.borderLeftWidth = 0;
            field.style.borderRightWidth = 0;
            field.style.borderBottomWidth = 0;
            field.style.backgroundColor = Color.clear;
            field.style.paddingLeft = 0;
            field.style.paddingRight = 0;
            field.style.paddingTop = 0;
            field.style.paddingBottom = 0;
            field.style.marginTop = 0;
            field.style.marginBottom = 0;
            field.style.marginLeft = 0;
            field.style.marginRight = 0;
            field.style.fontSize = 12;
            field.style.color = TextPrimary;

            var textElement = field.Q<TextElement>();
            if (textElement != null)
            {
                textElement.style.color = TextPrimary;
                textElement.style.marginLeft = 0;
            }

            var input = field.Q(className: TextField.inputUssClassName);
            if (input != null)
            {
                input.style.borderTopWidth = 1;
                input.style.borderLeftWidth = 1;
                input.style.borderRightWidth = 1;
                input.style.borderBottomWidth = 1;
                input.style.borderTopColor = BorderColor;
                input.style.borderLeftColor = BorderColor;
                input.style.borderRightColor = BorderColor;
                input.style.borderBottomColor = BorderColor;
                input.style.backgroundColor = BgInput;
                input.style.paddingLeft = 8;
                input.style.paddingRight = 8;
                input.style.paddingTop = 4;
                input.style.paddingBottom = 4;
                input.style.marginLeft = 0;
                input.style.flexGrow = 1;
            }

            row.Add(field);
            parent.Add(row);
            return field;
        }

        private static VisualElement CreateSection()
        {
            var section = new VisualElement();
            section.style.marginBottom = 10;
            section.style.paddingBottom = 10;
            section.style.borderBottomWidth = 1;
            section.style.borderBottomColor = BorderColor;
            return section;
        }

        private static void CreateSectionHeader(VisualElement section, string text)
        {
            var header = new Label(text);
            header.style.fontSize = 13;
            header.style.color = TextPrimary;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 4;
            section.Add(header);
        }

        private void MarkPreviewDirty()
        {
            _previewDirty = true;

            if (rootVisualElement == null) return;

            if (_pendingRefresh != null)
            {
                _pendingRefresh.Pause();
                _pendingRefresh = null;
            }

            _pendingRefresh = rootVisualElement.schedule.Execute(() =>
            {
                if (_previewDirty)
                {
                    RefreshPreview();
                    _previewDirty = false;
                }
                _pendingRefresh = null;
            }).StartingIn(30);
        }

        private void OnDisable()
        {
            if (_pendingRefresh != null)
            {
                _pendingRefresh.Pause();
                _pendingRefresh = null;
            }

            rootVisualElement.UnregisterCallback<MouseDownEvent>(OnRootMouseDown, TrickleDown.TrickleDown);

            if (_filterPopup != null && _filterPopup.parent != null)
                _filterPopup.parent.Remove(_filterPopup);
            _filterPopup = null;
        }

        private void RefreshPreview()
        {
            _processor.SearchPattern = _searchField?.value ?? "";
            _processor.ReplaceText = _replaceField?.value ?? "";
            _processor.Prefix = _prefixField?.value ?? "";
            _processor.Suffix = _suffixField?.value ?? "";
            _processor.CaseSensitive = _caseSensitiveToggle?.value ?? false;
            _processor.TextCase = _caseField != null ? (TextCaseMode)_caseField.value : TextCaseMode.None;
            _processor.PreserveNumbers = _preserveNumbersToggle?.value ?? false;
            _processor.NumberFormat = _numberFormatField != null ? (NumberFormatPreset)_numberFormatField.value : NumberFormatPreset.UnderscoreN;

            _processor.EnabledHierarchyCategories = _hierarchySelectedCategories;
            _processor.SetActiveCategories(_filterSelectedCategories);
            _processor.EnabledTextureSubCategories = _filterTextureSubCategories;

            Debug.Log($"[BatchRenamer] Window.RefreshPreview BEFORE processor.RefreshPreview: Items.Count={_processor.Items.Count}");
            _processor.RefreshPreview();
            Debug.Log($"[BatchRenamer] Window.RefreshPreview AFTER processor.RefreshPreview: Items.Count={_processor.Items.Count}");

            int matchCount = _processor.Items.Count(i => i.IsValid);
            Debug.Log($"[BatchRenamer] Search='{_processor.SearchPattern}' valid={matchCount}/{_processor.Items.Count}");

            var showAll = true;
            var showToggle = rootVisualElement?.Q<Toggle>("show-filtered-toggle");
            if (showToggle != null) showAll = showToggle.value;

            UpdatePreviewList(showAll);
        }

        private void UpdatePreviewList(bool showAll)
        {
            _previewContainer.Clear();

            int validCount = 0;
            int changedCount = 0;

            var itemByPath = new Dictionary<string, RenameItem>();
            foreach (var item in _processor.Items)
            {
                string p = AssetDatabase.GetAssetPath(item.Target);
                if (!string.IsNullOrEmpty(p))
                    itemByPath[p] = item;
            }

            var groups = new Dictionary<string, List<RenameItem>>();
            var groupNames = new Dictionary<string, string>();
            var rootItems = new List<RenameItem>();

            foreach (var item in _processor.Items)
            {
                if (!item.IsValid && !showAll) continue;
                if (item.IsValid) validCount++;
                if (item.IsValid && item.OriginalName != item.NewName) changedCount++;

                string path = AssetDatabase.GetAssetPath(item.Target);
                if (string.IsNullOrEmpty(path) || !path.Contains('/'))
                {
                    rootItems.Add(item);
                    continue;
                }

                int lastSlash = path.LastIndexOf('/');
                string parentPath = path.Substring(0, lastSlash);
                int dirSlash = parentPath.LastIndexOf('/');
                string parentName = dirSlash >= 0 ? parentPath.Substring(dirSlash + 1) : parentPath;

                if (!groups.ContainsKey(parentPath))
                {
                    groups[parentPath] = new List<RenameItem>();
                    groupNames[parentPath] = parentName;
                }
                groups[parentPath].Add(item);
            }

            bool firstItem = true;

            foreach (var item in rootItems)
            {
                BuildPreviewRow(item, 0);
                firstItem = false;
            }

            foreach (var kvp in groups.OrderBy(kvp => kvp.Key))
            {
                var parentPath = kvp.Key;
                var items = kvp.Value;

                if (!firstItem)
                {
                    var spacer = new VisualElement();
                    spacer.style.height = 4;
                    _previewContainer.Add(spacer);
                }
                firstItem = false;

                if (itemByPath.TryGetValue(parentPath, out var folderItem))
                {
                    BuildPreviewRow(folderItem, 0);
                }
                else
                {
                    var dirRow = new VisualElement();
                    dirRow.style.flexDirection = FlexDirection.Row;
                    dirRow.style.alignItems = Align.Center;
                    dirRow.style.paddingTop = 3;
                    dirRow.style.paddingBottom = 1;
                    dirRow.style.paddingLeft = 4;
                    dirRow.style.paddingRight = 4;
                    dirRow.style.minHeight = 22;
                    dirRow.style.borderBottomWidth = 1;
                    dirRow.style.borderBottomColor = new Color(0.15f, 0.15f, 0.15f);
                    dirRow.style.backgroundColor = new Color(0.14f, 0.14f, 0.14f);

                    var dirLeft = new VisualElement();
                    dirLeft.style.flexDirection = FlexDirection.Row;
                    dirLeft.style.alignItems = Align.Center;
                    dirLeft.style.flexGrow = 1;
                    dirLeft.style.flexBasis = 0;

                    var dirIcon = new VisualElement();
                    Texture2D folderIcon = EditorGUIUtility.IconContent("Folder Icon").image as Texture2D;
                    dirIcon.style.backgroundImage = folderIcon != null ? Background.FromTexture2D(folderIcon) : StyleKeyword.None;
                    dirIcon.style.width = 16;
                    dirIcon.style.height = 16;
                    dirIcon.style.marginRight = 6;
                    dirIcon.style.flexShrink = 0;
                    dirLeft.Add(dirIcon);

                    var dirLabel = new Label(groupNames[parentPath]);
                    dirLabel.style.fontSize = 12;
                    dirLabel.style.color = TextPrimary;
                    dirLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    dirLabel.style.whiteSpace = WhiteSpace.Pre;
                    dirLabel.style.flexShrink = 1;
                    dirLeft.Add(dirLabel);

                    dirRow.Add(dirLeft);
                    _previewContainer.Add(dirRow);
                }

                foreach (var item in items)
                {
                    string itemPath = AssetDatabase.GetAssetPath(item.Target);
                    if (!string.IsNullOrEmpty(itemPath) && groups.ContainsKey(itemPath))
                        continue;
                    BuildPreviewRow(item, 1);
                }
            }

            if (_previewHeader != null)
                _previewHeader.text = $"Preview ({_processor.Items.Count} items)";

            if (_statusLabel != null)
                _statusLabel.text = $"{validCount} valid, {changedCount} will change";

            if (_renameButton != null)
            {
                bool canRename = changedCount > 0;
                _renameButton.text = canRename
                    ? $"Rename Selected ({changedCount})"
                    : "Rename Selected";
                _renameButton.SetEnabled(canRename);
            }
        }

        private void BuildPreviewRow(RenameItem item, int depth = 0)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingTop = 3;
            row.style.paddingBottom = 3;
            row.style.paddingLeft = 4;
            row.style.paddingRight = 4;
            row.style.minHeight = 22;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.15f, 0.15f, 0.15f);

            if (!item.IsValid)
            {
                row.style.opacity = 0.4f;
            }

            var leftColumn = new VisualElement();
            leftColumn.style.flexDirection = FlexDirection.Row;
            leftColumn.style.alignItems = Align.Center;
            leftColumn.style.flexGrow = 1;
            leftColumn.style.flexBasis = 0;
            leftColumn.style.overflow = Overflow.Hidden;

            if (depth > 0)
            {
                var indent = new Label("");
                indent.style.minWidth = depth * 14;
                indent.style.flexShrink = 0;
                leftColumn.Add(indent);

                var treeLine = new Label("|_ ");
                treeLine.style.fontSize = 12;
                treeLine.style.color = TextDim;
                treeLine.style.whiteSpace = WhiteSpace.Pre;
                treeLine.style.flexShrink = 0;
                treeLine.style.marginRight = 2;
                leftColumn.Add(treeLine);
            }

            var icon = new VisualElement();
            Texture2D thumb = AssetPreview.GetMiniThumbnail(item.Target);
            if (thumb == null)
            {
                string assetPath = AssetDatabase.GetAssetPath(item.Target);
                if (!string.IsNullOrEmpty(assetPath) && AssetDatabase.IsValidFolder(assetPath))
                    thumb = EditorGUIUtility.IconContent("Folder Icon").image as Texture2D;
            }
            icon.style.backgroundImage = thumb != null ? Background.FromTexture2D(thumb) : StyleKeyword.None;
            icon.style.width = 16;
            icon.style.height = 16;
            icon.style.marginRight = 6;
            icon.style.flexShrink = 0;
            leftColumn.Add(icon);

            bool hasActivePreset = _processor.PreviewOperations != null && _processor.PreviewOperations.Count > 0;

            VisualElement oldContainer;
            if (hasActivePreset)
            {
                var oldRanges = ComputeSimpleDiffRanges(item.NewName, item.OriginalName);
                oldContainer = BuildOldDiffDisplay(item.OriginalName, oldRanges);
            }
            else
            {
                oldContainer = BuildOldNameHighlight(item.OriginalName, item.MatchedTexts, item.IsValid, _processor.CaseSensitive);
            }
            oldContainer.style.flexShrink = 1;
            leftColumn.Add(oldContainer);

            row.Add(leftColumn);

            var arrow = new Label("\u2192");
            arrow.style.fontSize = 13;
            arrow.style.color = TextSecondary;
            arrow.style.marginLeft = 6;
            arrow.style.marginRight = 6;
            arrow.style.flexShrink = 0;
            row.Add(arrow);

            var rightColumn = new VisualElement();
            rightColumn.style.flexDirection = FlexDirection.Row;
            rightColumn.style.alignItems = Align.Center;
            rightColumn.style.flexGrow = 1;
            rightColumn.style.flexBasis = 0;
            rightColumn.style.overflow = Overflow.Hidden;

            VisualElement newNameContainer;
            if (hasActivePreset)
            {
                var ranges = ComputeSimpleDiffRanges(item.OriginalName, item.NewName);
                newNameContainer = RenderDiffDisplay(item.NewName, ranges);
            }
            else
            {
                string resolvedPrefix = _processor.EvaluateConditions(_processor.Prefix, item.OriginalName);
                string resolvedSuffix = _processor.EvaluateConditions(_processor.Suffix, item.OriginalName);
                newNameContainer = BuildDiffDisplay(item.OriginalName, item.NewName, item.MatchedTexts, resolvedPrefix, resolvedSuffix, _processor.ReplaceText);
            }
            rightColumn.Add(newNameContainer);

            row.Add(rightColumn);

            _previewContainer.Add(row);
        }

        private static VisualElement BuildOldNameHighlight(string name, List<SearchExpression.MatchEntry> matchedTexts, bool isValid, bool caseSensitive)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.flexWrap = Wrap.NoWrap;
            container.style.overflow = Overflow.Hidden;

            var baseColor = isValid ? TextSecondary : TextDim;

            if (matchedTexts == null || matchedTexts.Count == 0)
            {
                var label = new Label(name);
                label.style.fontSize = 12;
                label.style.color = baseColor;
                label.style.whiteSpace = WhiteSpace.Pre;
                container.Add(label);
                return container;
            }

            var intervals = new List<(int start, int end)>();
            foreach (var entry in matchedTexts)
            {
                if (string.IsNullOrEmpty(entry.Text)) continue;
                intervals.Add((entry.Index, entry.Index + entry.Text.Length));
            }

            intervals.Sort((a, b) =>
            {
                int cmp = a.start.CompareTo(b.start);
                return cmp != 0 ? cmp : b.end.CompareTo(a.end);
            });

            var richText = new System.Text.StringBuilder();
            var highlightHex = ColorUtility.ToHtmlStringRGB(MatchHighlight);

            int pos = 0;
            foreach (var (start, end) in intervals)
            {
                if (start < pos) continue;
                if (start > pos)
                    richText.Append(name.Substring(pos, start - pos));
                richText.Append("<color=#").Append(highlightHex).Append("><b>")
                    .Append(name.Substring(start, end - start))
                    .Append("</b></color>");
                pos = end;
            }

            if (pos < name.Length)
                richText.Append(name.Substring(pos));

            var resultLabel = new Label(richText.ToString());
            resultLabel.style.fontSize = 12;
            resultLabel.style.color = baseColor;
            resultLabel.style.whiteSpace = WhiteSpace.Pre;
            resultLabel.style.flexShrink = 0;
            resultLabel.style.paddingLeft = 0;
            resultLabel.style.paddingRight = 0;
            resultLabel.style.marginLeft = 0;
            resultLabel.style.marginRight = 0;
            resultLabel.style.borderLeftWidth = 0;
            resultLabel.style.borderRightWidth = 0;
            resultLabel.enableRichText = true;
            container.Add(resultLabel);

            return container;
        }

        private static VisualElement RenderDiffDisplay(string newName, List<(int start, int end)> changedRanges)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.flexWrap = Wrap.NoWrap;
            container.style.overflow = Overflow.Hidden;

            var sorted = new List<(int start, int end)>(changedRanges);
            sorted.Sort((a, b) => a.start.CompareTo(b.start));

            var merged = new List<(int start, int end)>();
            foreach (var range in sorted)
            {
                if (range.start >= newName.Length) continue;
                if (merged.Count > 0 && range.start <= merged[merged.Count - 1].end)
                {
                    merged[merged.Count - 1] = (merged[merged.Count - 1].start, Math.Max(merged[merged.Count - 1].end, range.end));
                }
                else
                {
                    merged.Add(range);
                }
            }

            var richText = new System.Text.StringBuilder();
            var highlightHex = ColorUtility.ToHtmlStringRGB(GreenHighlight);

            int pos = 0;
            foreach (var (start, end) in merged)
            {
                int clampedStart = Math.Max(start, pos);
                int clampedEnd = Math.Max(clampedStart, Math.Min(end, newName.Length));
                if (clampedEnd <= clampedStart) continue;

                if (clampedStart > pos)
                    richText.Append(newName.Substring(pos, clampedStart - pos));

                richText.Append("<color=#").Append(highlightHex).Append("><b>")
                    .Append(newName.Substring(clampedStart, clampedEnd - clampedStart))
                    .Append("</b></color>");

                pos = clampedEnd;
            }

            if (pos < newName.Length)
                richText.Append(newName.Substring(pos));

            var resultLabel = new Label(richText.ToString());
            resultLabel.style.fontSize = 12;
            resultLabel.style.color = TextPrimary;
            resultLabel.style.whiteSpace = WhiteSpace.Pre;
            resultLabel.style.flexShrink = 0;
            resultLabel.style.paddingLeft = 0;
            resultLabel.style.paddingRight = 0;
            resultLabel.style.marginLeft = 0;
            resultLabel.style.marginRight = 0;
            resultLabel.style.borderLeftWidth = 0;
            resultLabel.style.borderRightWidth = 0;
            resultLabel.enableRichText = true;
            container.Add(resultLabel);

            return container;
        }

        private static VisualElement BuildOldDiffDisplay(string oldName, List<(int start, int end)> changedRanges)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.flexWrap = Wrap.NoWrap;
            container.style.overflow = Overflow.Hidden;

            var sorted = new List<(int start, int end)>(changedRanges);
            sorted.Sort((a, b) => a.start.CompareTo(b.start));

            var merged = new List<(int start, int end)>();
            foreach (var range in sorted)
            {
                if (range.start >= oldName.Length) continue;
                if (merged.Count > 0 && range.start <= merged[merged.Count - 1].end)
                {
                    merged[merged.Count - 1] = (merged[merged.Count - 1].start, Math.Max(merged[merged.Count - 1].end, range.end));
                }
                else
                {
                    merged.Add(range);
                }
            }

            var richText = new System.Text.StringBuilder();
            var removedHex = ColorUtility.ToHtmlStringRGB(RemovedHighlight);

            int pos = 0;
            foreach (var (start, end) in merged)
            {
                int clampedStart = Math.Max(start, pos);
                int clampedEnd = Math.Max(clampedStart, Math.Min(end, oldName.Length));
                if (clampedEnd <= clampedStart) continue;

                if (clampedStart > pos)
                    richText.Append(oldName.Substring(pos, clampedStart - pos));

                richText.Append("<color=#").Append(removedHex).Append("><b>")
                    .Append(oldName.Substring(clampedStart, clampedEnd - clampedStart))
                    .Append("</b></color>");

                pos = clampedEnd;
            }

            if (pos < oldName.Length)
                richText.Append(oldName.Substring(pos));

            var resultLabel = new Label(richText.ToString());
            resultLabel.style.fontSize = 12;
            resultLabel.style.color = TextSecondary;
            resultLabel.style.whiteSpace = WhiteSpace.Pre;
            resultLabel.style.flexShrink = 0;
            resultLabel.style.paddingLeft = 0;
            resultLabel.style.paddingRight = 0;
            resultLabel.style.marginLeft = 0;
            resultLabel.style.marginRight = 0;
            resultLabel.style.borderLeftWidth = 0;
            resultLabel.style.borderRightWidth = 0;
            resultLabel.enableRichText = true;
            container.Add(resultLabel);

            return container;
        }

        private static List<(int start, int end)> ComputeSimpleDiffRanges(string oldName, string newName)
        {
            var ranges = new List<(int start, int end)>();
            ComputeDiffRecursive(oldName, 0, oldName.Length, newName, 0, newName.Length, ranges);
            ranges.Sort((a, b) => a.start.CompareTo(b.start));
            return ranges;
        }

        private static void ComputeDiffRecursive(
            string oldName, int oldStart, int oldEnd,
            string newName, int newStart, int newEnd,
            List<(int start, int end)> ranges)
        {
            int oLen = oldEnd - oldStart;
            int nLen = newEnd - newStart;

            if (oLen == 0 && nLen == 0) return;

            if (oLen == 0)
            {
                ranges.Add((newStart, newEnd));
                return;
            }

            if (nLen == 0) return;

            int prefixLen = 0;
            int maxPrefix = Math.Min(oLen, nLen);
            while (prefixLen < maxPrefix && oldName[oldStart + prefixLen] == newName[newStart + prefixLen])
                prefixLen++;

            int suffixLen = 0;
            int maxSuffix = Math.Min(oLen - prefixLen, nLen - prefixLen);
            while (suffixLen < maxSuffix && oldName[oldEnd - 1 - suffixLen] == newName[newEnd - 1 - suffixLen])
                suffixLen++;

            int oMidStart = oldStart + prefixLen;
            int oMidEnd = oldEnd - suffixLen;
            int nMidStart = newStart + prefixLen;
            int nMidEnd = newEnd - suffixLen;

            if (oMidStart >= oMidEnd && nMidStart >= nMidEnd) return;

            if (oMidStart >= oMidEnd)
            {
                ranges.Add((nMidStart, nMidEnd));
                return;
            }

            if (nMidStart >= nMidEnd) return;

            int bestOldRel = -1, bestNewRel = -1, bestLen = 0;
            for (int i = 0; i < oMidEnd - oMidStart; i++)
            {
                for (int j = 0; j < nMidEnd - nMidStart; j++)
                {
                    int k = 0;
                    while (i + k < oMidEnd - oMidStart && j + k < nMidEnd - nMidStart &&
                           oldName[oMidStart + i + k] == newName[nMidStart + j + k])
                        k++;
                    if (k > bestLen)
                    {
                        bestLen = k;
                        bestOldRel = i;
                        bestNewRel = j;
                    }
                }
            }

            if (bestLen < 2)
            {
                ranges.Add((nMidStart, nMidEnd));
                return;
            }

            ComputeDiffRecursive(oldName, oMidStart, oMidStart + bestOldRel,
                                 newName, nMidStart, nMidStart + bestNewRel,
                                 ranges);

            ComputeDiffRecursive(oldName, oMidStart + bestOldRel + bestLen, oMidEnd,
                                 newName, nMidStart + bestNewRel + bestLen, nMidEnd,
                                 ranges);
        }

        private static VisualElement BuildDiffDisplay(string oldName, string newName, List<SearchExpression.MatchEntry> matchedEntries, string prefixText, string suffixText, string replaceText)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.flexWrap = Wrap.NoWrap;
            container.style.overflow = Overflow.Hidden;

            if (oldName == newName)
            {
                var label = new Label(newName);
                label.style.fontSize = 12;
                label.style.color = TextPrimary;
                label.style.whiteSpace = WhiteSpace.Pre;
                container.Add(label);
                return container;
            }

            var changedRanges = new List<(int start, int end)>();

            if (!string.IsNullOrEmpty(prefixText))
            {
                changedRanges.Add((0, prefixText.Length));
            }

            if (matchedEntries != null && matchedEntries.Count > 0 && !string.IsNullOrEmpty(replaceText))
            {
                var sorted = new List<SearchExpression.MatchEntry>(matchedEntries);
                sorted.Sort((a, b) => a.Index.CompareTo(b.Index));

                bool hasNumberToken = replaceText.IndexOf("{Number}", StringComparison.OrdinalIgnoreCase) >= 0;

                bool contiguous = sorted.Count > 1;
                if (contiguous)
                {
                    int next = sorted[0].Index + sorted[0].Text.Length;
                    for (int i = 1; i < sorted.Count; i++)
                    {
                        if (sorted[i].Index != next) { contiguous = false; break; }
                        next = sorted[i].Index + sorted[i].Text.Length;
                    }
                }

                if (contiguous && hasNumberToken)
                {
                    string number = "";
                    foreach (var e in sorted)
                    {
                        var nm = Regex.Match(e.Text, @"\d+");
                        if (nm.Success) { number = nm.Value; break; }
                    }
                    string resolved = Regex.Replace(replaceText, @"\{number\}", number, RegexOptions.IgnoreCase);
                    int spanStart = sorted[0].Index;
                    int spanEnd = sorted[sorted.Count - 1].Index + sorted[sorted.Count - 1].Text.Length;
                    int start = spanStart + (prefixText?.Length ?? 0);
                    int end = start + resolved.Length;
                    changedRanges.Add((start, end));
                }
                else
                {
                    int shift = 0;
                    foreach (var entry in sorted)
                    {
                        if (entry.Index < 0 || string.IsNullOrEmpty(entry.Text)) continue;
                        int repLen = replaceText.Length;
                        if (hasNumberToken)
                        {
                            var numberMatch = Regex.Match(entry.Text, @"\d+");
                            string numberVal = numberMatch.Success ? numberMatch.Value : "";
                            string resolved = Regex.Replace(replaceText, @"\{number\}", numberVal, RegexOptions.IgnoreCase);
                            repLen = resolved.Length;
                        }
                        int start = entry.Index + (prefixText?.Length ?? 0) + shift;
                        int end = start + repLen;
                        changedRanges.Add((start, end));
                        shift += repLen - entry.Text.Length;
                    }
                }
            }

            if (!string.IsNullOrEmpty(suffixText))
            {
                changedRanges.Add((newName.Length - suffixText.Length, newName.Length));
            }

            return RenderDiffDisplay(newName, changedRanges);
        }

        private void OnRenameClicked()
        {
            int changedCount = _processor.Items.Count(i => i.NewName != i.OriginalName);
            if (changedCount == 0) return;

            if (_currentPreset != null && _currentPreset.operations.Count > 0)
            {
                string opDesc = $"{_currentPreset.operations.Count} operation(s)";
                bool proceed = EditorUtility.DisplayDialog(
                    "Confirm Batch Rename",
                    $"Are you sure you want to rename {changedCount} item(s) using {opDesc}?\nThis action can be undone (Ctrl+Z).",
                    "Rename", "Cancel");

                if (!proceed) return;

                _processor.RunOperations(_currentPreset.operations);
                Close();
                return;
            }

            bool singleProceed = EditorUtility.DisplayDialog(
                "Confirm Batch Rename",
                $"Are you sure you want to rename {changedCount} item(s)?\nThis action can be undone (Ctrl+Z).",
                "Rename", "Cancel");

            if (!singleProceed) return;

            _processor.ApplyRenames();
            Close();
        }

        private void BuildPresetSection(VisualElement parent)
        {
            var section = CreateSection();
            CreateSectionHeader(section, "Preset");

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;

            _presetField = new ObjectField();
            _presetField.objectType = typeof(BatchRenamePreset);
            _presetField.style.flexGrow = 1;
            _presetField.RegisterValueChangedCallback(evt =>
            {
                var preset = evt.newValue as BatchRenamePreset;
                _currentPreset = preset;
                if (_currentPreset != null && _currentPreset.operations.Count > 0)
                {
                    _processor.PreviewOperations = _currentPreset.operations;
                }
                else
                {
                    _processor.PreviewOperations = null;
                }
                RefreshOperationsListUI();
                MarkPreviewDirty();
            });

            var objFieldLabel = _presetField.Q<Label>();
            if (objFieldLabel != null)
            {
                objFieldLabel.style.minWidth = 0;
                objFieldLabel.style.display = DisplayStyle.None;
            }

            var objInput = _presetField.Q(className: ObjectField.inputUssClassName);
            if (objInput != null)
            {
                objInput.style.fontSize = 12;
                objInput.style.backgroundColor = BgInput;
                objInput.style.borderTopWidth = 1;
                objInput.style.borderLeftWidth = 1;
                objInput.style.borderRightWidth = 1;
                objInput.style.borderBottomWidth = 1;
                objInput.style.borderTopColor = BorderColor;
                objInput.style.borderLeftColor = BorderColor;
                objInput.style.borderRightColor = BorderColor;
                objInput.style.borderBottomColor = BorderColor;
                objInput.style.paddingLeft = 8;
                objInput.style.paddingRight = 8;
                objInput.style.paddingTop = 4;
                objInput.style.paddingBottom = 4;
            }
            row.Add(_presetField);

            section.Add(row);

            _operationsListSection = new VisualElement();
            _operationsListSection.style.display = DisplayStyle.None;
            _operationsListSection.style.marginTop = 4;
            section.Add(_operationsListSection);

            parent.Add(section);
        }

        private void ApplyOperationToUI(BatchRenameOperation op)
        {
            if (_searchField != null) _searchField.SetValueWithoutNotify(op.searchPattern);
            if (_replaceField != null) _replaceField.SetValueWithoutNotify(op.replaceText);
            if (_prefixField != null) _prefixField.SetValueWithoutNotify(op.prefix);
            if (_suffixField != null) _suffixField.SetValueWithoutNotify(op.suffix);
            if (_caseField != null) _caseField.SetValueWithoutNotify(op.textCase);
            if (_preserveNumbersToggle != null) _preserveNumbersToggle.SetValueWithoutNotify(op.preserveNumbers);
            if (_numberFormatField != null) _numberFormatField.SetValueWithoutNotify(op.numberFormat);
            if (_caseSensitiveToggle != null) _caseSensitiveToggle.SetValueWithoutNotify(op.caseSensitive);

            _filterSelectedCategories.Clear();
            foreach (var cat in op.enabledCategories)
                _filterSelectedCategories.Add(cat);

            _filterTextureSubCategories.Clear();
            foreach (var sub in op.enabledTextureSubCategories)
                _filterTextureSubCategories.Add(sub);

            _hierarchySelectedCategories.Clear();
            foreach (var cat in op.enabledHierarchyCategories)
                _hierarchySelectedCategories.Add(cat);

            UpdateFilterSummary();
        }

        private void RefreshOperationsListUI()
        {
            _operationsListSection.Clear();

            if (_currentPreset == null || _currentPreset.operations.Count == 0)
            {
                _operationsListSection.style.display = DisplayStyle.None;
                return;
            }

            _operationsListSection.style.display = DisplayStyle.Flex;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2;
            row.style.paddingLeft = 4;
            row.style.paddingRight = 4;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;
            row.style.minHeight = 20;
            row.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);

            var icon = new Label("\u26F0");
            icon.style.fontSize = 11;
            icon.style.color = TextDim;
            icon.style.minWidth = 22;
            icon.style.marginRight = 4;
            icon.style.unityTextAlign = TextAnchor.MiddleCenter;
            row.Add(icon);

            int opCount = _currentPreset.operations.Count;
            var summary = new Label($"{opCount} operation(s)");
            summary.style.fontSize = 11;
            summary.style.color = TextSecondary;
            summary.style.whiteSpace = WhiteSpace.NoWrap;
            summary.style.textOverflow = TextOverflow.Ellipsis;
            summary.style.flexGrow = 1;
            row.Add(summary);

            var removeBtn = new Button(() =>
            {
                _presetField.value = null;
            });
            removeBtn.text = "X";
            removeBtn.style.fontSize = 11;
            removeBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            removeBtn.style.color = new Color(1f, 0.3f, 0.3f);
            removeBtn.style.backgroundColor = new Color(0.3f, 0.05f, 0.05f, 0.5f);
            removeBtn.style.borderTopWidth = 1;
            removeBtn.style.borderLeftWidth = 1;
            removeBtn.style.borderRightWidth = 1;
            removeBtn.style.borderBottomWidth = 1;
            removeBtn.style.borderTopColor = new Color(0.5f, 0.1f, 0.1f);
            removeBtn.style.borderLeftColor = new Color(0.5f, 0.1f, 0.1f);
            removeBtn.style.borderRightColor = new Color(0.5f, 0.1f, 0.1f);
            removeBtn.style.borderBottomColor = new Color(0.5f, 0.1f, 0.1f);
            removeBtn.style.width = 20;
            removeBtn.style.height = 20;
            removeBtn.style.paddingLeft = 0;
            removeBtn.style.paddingRight = 0;
            removeBtn.style.paddingTop = 0;
            removeBtn.style.paddingBottom = 0;
            removeBtn.style.marginLeft = 4;
            removeBtn.style.flexShrink = 0;
            removeBtn.tooltip = "Remove preset";
            row.Add(removeBtn);

            _operationsListSection.Add(row);
        }
    }
}
