using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
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

        private static readonly Color BgDark = new Color(0.12f, 0.12f, 0.12f);
        private static readonly Color BgInput = new Color(0.17f, 0.17f, 0.17f);
        private static readonly Color BorderColor = new Color(0.28f, 0.28f, 0.28f);
        private static readonly Color TextPrimary = new Color(0.85f, 0.85f, 0.85f);
        private static readonly Color TextSecondary = new Color(0.55f, 0.55f, 0.55f);
        private static readonly Color TextDim = new Color(0.4f, 0.4f, 0.4f);
        private static readonly Color AccentBlue = new Color(0.22f, 0.42f, 0.75f);
        private static readonly Color GreenHighlight = new Color(0.4f, 0.9f, 0.4f);
        private static readonly Color MatchHighlight = new Color(0.9f, 0.7f, 0.1f);
        private static readonly Color PreviewBg = new Color(0.1f, 0.1f, 0.1f);

        private readonly HashSet<AssetCategory> _filterSelectedCategories = new HashSet<AssetCategory>();
        private VisualElement _leftColumn;
        private VisualElement _filterDropdownButton;
        private Label _filterSummaryLabel;
        private VisualElement _filterPopup;
        private bool _filterPopupOpen;

        public static void ShowWindow(Object[] selectedObjects)
        {
            var window = GetWindow<BatchRenamerWindow>(true, "Batch Rename");
            window._processor.CollectFromObjects(selectedObjects);
            if (window._previewContainer != null)
            {
                window.RefreshPreview();
                window._previewDirty = false;
            }
            else
            {
                window._previewDirty = true;
            }
            window.Show();
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

            if (_previewDirty)
            {
                RefreshPreview();
                _previewDirty = false;
            }
        }

        private void OnSelectionChange()
        {
            if (_processor == null || _previewContainer == null) return;

            _processor.CollectFromObjects(Selection.objects);
            MarkPreviewDirty();
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
            _filterSelectedCategories.Clear();
            var categories = (AssetCategory[])Enum.GetValues(typeof(AssetCategory));
            foreach (var cat in categories)
            {
                if (cat != AssetCategory.All)
                    _filterSelectedCategories.Add(cat);
            }

            var section = CreateSection();
            CreateSectionHeader(section, "Filters");

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
            section.Add(row);
            parent.Add(section);
        }

        private void ToggleFilterPopup()
        {
            if (_filterPopupOpen)
                CloseFilterPopup();
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
            foreach (var cat in categories)
            {
                if (cat == AssetCategory.All) continue;

                var toggle = new Toggle(cat.ToString());
                toggle.value = _filterSelectedCategories.Contains(cat);
                toggle.style.flexDirection = FlexDirection.Row;
                toggle.style.alignItems = Align.Center;
                toggle.style.paddingLeft = 8;
                toggle.style.paddingRight = 8;
                toggle.style.paddingTop = 2;
                toggle.style.paddingBottom = 2;
                toggle.style.minHeight = 22;
                toggle.style.unityTextAlign = TextAnchor.MiddleLeft;

                var toggleLabel = toggle.Q<Label>();
                if (toggleLabel != null)
                {
                    toggleLabel.style.fontSize = 12;
                    toggleLabel.style.color = TextPrimary;
                    toggleLabel.style.marginLeft = 4;
                }

                var capturedCat = cat;
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                        _filterSelectedCategories.Add(capturedCat);
                    else
                        _filterSelectedCategories.Remove(capturedCat);
                    UpdateFilterSummary();
                    MarkPreviewDirty();
                });

                popup.Add(toggle);
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
            rootVisualElement.RegisterCallback<MouseDownEvent>(OnRootMouseDown, TrickleDown.TrickleDown);
        }

        private void OnRootMouseDown(MouseDownEvent evt)
        {
            rootVisualElement.UnregisterCallback<MouseDownEvent>(OnRootMouseDown, TrickleDown.TrickleDown);

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
            rootVisualElement.UnregisterCallback<MouseDownEvent>(OnRootMouseDown, TrickleDown.TrickleDown);

            if (_filterPopup != null && _filterPopup.parent != null)
                _filterPopup.parent.Remove(_filterPopup);
            _filterPopup = null;
        }

        private void SelectNoneFilter()
        {
            _filterSelectedCategories.Clear();
            CloseFilterPopup();
            UpdateFilterSummary();
            MarkPreviewDirty();
        }

        private void UpdateFilterSummary()
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

            _processor.SetActiveCategories(_filterSelectedCategories);
            _processor.RefreshPreview();

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

            foreach (var item in _processor.Items)
            {
                if (!item.IsValid && !showAll) continue;
                if (item.IsValid) validCount++;
                if (item.IsValid && item.OriginalName != item.NewName) changedCount++;

                BuildPreviewRow(item);
            }

            if (_previewHeader != null)
                _previewHeader.text = $"Preview ({_processor.Items.Count} items)";

            if (_statusLabel != null)
                _statusLabel.text = $"{validCount} valid, {changedCount} will change";

            if (_renameButton != null)
            {
                bool hasValid = validCount > 0;
                _renameButton.text = hasValid
                    ? $"Rename Selected ({validCount})"
                    : "Rename Selected";
                _renameButton.SetEnabled(hasValid);
            }
        }

        private void BuildPreviewRow(RenameItem item)
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

            var icon = new VisualElement();
            var tex = AssetPreview.GetMiniThumbnail(item.Target);
            icon.style.backgroundImage = tex != null ? new Background(tex) : StyleKeyword.None;
            icon.style.width = 16;
            icon.style.height = 16;
            icon.style.marginRight = 6;
            icon.style.flexShrink = 0;
            row.Add(icon);

            var oldContainer = BuildOldNameHighlight(item.OriginalName, item.MatchedTexts, item.IsValid, _processor.CaseSensitive);
            oldContainer.style.marginRight = 8;
            oldContainer.style.flexShrink = 1;
            row.Add(oldContainer);

            var arrow = new Label("\u2192");
            arrow.style.fontSize = 13;
            arrow.style.color = TextSecondary;
            arrow.style.marginRight = 8;
            arrow.style.flexShrink = 0;
            row.Add(arrow);

            var newNameContainer = BuildDiffDisplay(item.OriginalName, item.NewName);
            newNameContainer.style.flexGrow = 1;
            newNameContainer.style.flexShrink = 1;
            row.Add(newNameContainer);

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

            int pos = 0;
            foreach (var (start, end) in intervals)
            {
                if (start < pos) continue;
                if (start > pos)
                {
                    var before = new Label(name.Substring(pos, start - pos));
                    before.style.fontSize = 12;
                    before.style.color = baseColor;
                    before.style.whiteSpace = WhiteSpace.Pre;
                    before.style.flexShrink = 0;
                    container.Add(before);
                }
                var match = new Label(name.Substring(start, end - start));
                match.style.fontSize = 12;
                match.style.color = MatchHighlight;
                match.style.whiteSpace = WhiteSpace.Pre;
                match.style.unityFontStyleAndWeight = FontStyle.Bold;
                match.style.flexShrink = 0;
                container.Add(match);
                pos = end;
            }

            if (pos < name.Length)
            {
                var remaining = new Label(name.Substring(pos));
                remaining.style.fontSize = 12;
                remaining.style.color = baseColor;
                remaining.style.whiteSpace = WhiteSpace.Pre;
                remaining.style.flexShrink = 0;
                container.Add(remaining);
            }

            return container;
        }

        private static VisualElement BuildDiffDisplay(string oldName, string newName)
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

            var (prefix, middle, suffix) = ComputeDiff(oldName, newName);

            if (prefix.Length > 0)
            {
                var pLabel = new Label(prefix);
                pLabel.style.fontSize = 12;
                pLabel.style.color = TextPrimary;
                pLabel.style.whiteSpace = WhiteSpace.Pre;
                pLabel.style.flexShrink = 0;
                container.Add(pLabel);
            }

            if (middle.Length > 0)
            {
                var mLabel = new Label(middle);
                mLabel.style.fontSize = 12;
                mLabel.style.color = GreenHighlight;
                mLabel.style.whiteSpace = WhiteSpace.Pre;
                mLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                mLabel.style.flexShrink = 0;
                container.Add(mLabel);
            }

            if (suffix.Length > 0)
            {
                var sLabel = new Label(suffix);
                sLabel.style.fontSize = 12;
                sLabel.style.color = TextPrimary;
                sLabel.style.whiteSpace = WhiteSpace.Pre;
                sLabel.style.flexShrink = 0;
                container.Add(sLabel);
            }

            return container;
        }

        private static (string prefix, string middle, string suffix) ComputeDiff(string oldName, string newName)
        {
            int minLen = Math.Min(oldName.Length, newName.Length);

            int prefixLen = 0;
            while (prefixLen < minLen && oldName[prefixLen] == newName[prefixLen])
                prefixLen++;

            int suffixLen = 0;
            int oldMaxSuffix = oldName.Length - prefixLen;
            int newMaxSuffix = newName.Length - prefixLen;
            int maxSuffix = Math.Min(oldMaxSuffix, newMaxSuffix);

            while (suffixLen < maxSuffix &&
                   oldName[oldName.Length - 1 - suffixLen] == newName[newName.Length - 1 - suffixLen])
                suffixLen++;

            string prefix = newName.Substring(0, prefixLen);
            string middle = newName.Substring(prefixLen, newName.Length - prefixLen - suffixLen);
            string suffix = newName.Substring(newName.Length - suffixLen);

            return (prefix, middle, suffix);
        }

        private void OnRenameClicked()
        {
            int validCount = _processor.Items.Count(i => i.IsValid);
            if (validCount == 0) return;

            bool proceed = EditorUtility.DisplayDialog(
                "Confirm Batch Rename",
                $"Are you sure you want to rename {validCount} item(s)?\nThis action can be undone (Ctrl+Z).",
                "Rename", "Cancel");

            if (!proceed) return;

            _processor.ApplyRenames();
            Close();
        }
    }
}
