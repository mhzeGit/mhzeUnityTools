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

        private readonly Dictionary<AssetCategory, Toggle> _filterToggles = new Dictionary<AssetCategory, Toggle>();

        public static void ShowWindow(Object[] selectedObjects)
        {
            var window = GetWindow<BatchRenamerWindow>(true, "Batch Rename");
            window.minSize = new Vector2(500, 680);
            window.maxSize = new Vector2(800, 1400);
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
            BuildSearchReplaceSection();
            BuildFilterSection();
            BuildModifySection();
            BuildNumberSection();
            BuildPreviewSection();
            BuildActionsSection();

            if (_previewDirty)
            {
                RefreshPreview();
                _previewDirty = false;
            }
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

        private void BuildSearchReplaceSection()
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

            rootVisualElement.Add(section);
        }

        private void BuildFilterSection()
        {
            var section = CreateSection();
            CreateSectionHeader(section, "Filters");

            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.marginTop = 4;
            grid.style.marginBottom = 4;

            var categories = (AssetCategory[])Enum.GetValues(typeof(AssetCategory));
            foreach (var cat in categories)
            {
                if (cat == AssetCategory.All) continue;

                var toggle = new Toggle(cat.ToString());
                toggle.value = true;
                toggle.style.width = StyleKeyword.Auto;
                toggle.style.minWidth = 100;
                toggle.style.marginRight = 4;
                toggle.style.marginBottom = 4;
                toggle.style.unityTextAlign = TextAnchor.MiddleLeft;

                var label = toggle.Q<Label>();
                if (label != null)
                {
                    label.style.fontSize = 12;
                    label.style.color = TextPrimary;
                    label.style.marginLeft = 4;
                }

                toggle.RegisterValueChangedCallback(_ => MarkPreviewDirty());
                _filterToggles[cat] = toggle;
                grid.Add(toggle);
            }

            var allToggle = new Toggle("All");
            allToggle.value = true;
            allToggle.style.minWidth = 100;
            allToggle.style.marginRight = 4;
            allToggle.style.marginBottom = 4;
            allToggle.style.unityTextAlign = TextAnchor.MiddleLeft;

            var allLabel = allToggle.Q<Label>();
            if (allLabel != null)
            {
                allLabel.style.fontSize = 12;
                allLabel.style.color = TextPrimary;
                allLabel.style.marginLeft = 4;
            }

            allToggle.RegisterValueChangedCallback(evt =>
            {
                foreach (var kvp in _filterToggles)
                    kvp.Value.SetValueWithoutNotify(evt.newValue);
                MarkPreviewDirty();
            });

            grid.Add(allToggle);
            section.Add(grid);
            rootVisualElement.Add(section);
        }

        private void BuildModifySection()
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

            rootVisualElement.Add(section);
        }

        private void BuildNumberSection()
        {
            var section = CreateSection();
            CreateSectionHeader(section, "Numbers");

            _preserveNumbersToggle = new Toggle("Detect & Preserve Numbers");
            _preserveNumbersToggle.value = true;
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

            rootVisualElement.Add(section);
        }

        private void BuildPreviewSection()
        {
            var section = CreateSection();

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
            _previewScrollView.style.backgroundColor = PreviewBg;
            _previewScrollView.style.borderTopWidth = 1;
            _previewScrollView.style.borderLeftWidth = 1;
            _previewScrollView.style.borderRightWidth = 1;
            _previewScrollView.style.borderBottomWidth = 1;
            _previewScrollView.style.borderTopColor = BorderColor;
            _previewScrollView.style.borderLeftColor = BorderColor;
            _previewScrollView.style.borderRightColor = BorderColor;
            _previewScrollView.style.borderBottomColor = BorderColor;
            _previewScrollView.style.minHeight = 180;
            _previewScrollView.style.maxHeight = 400;
            _previewScrollView.style.marginBottom = 8;
            _previewScrollView.style.paddingTop = 4;
            _previewScrollView.style.paddingBottom = 4;

            var scrollContent = _previewScrollView.contentContainer;
            scrollContent.style.paddingLeft = 8;
            scrollContent.style.paddingRight = 8;

            _previewContainer = new VisualElement();
            _previewScrollView.Add(_previewContainer);
            section.Add(_previewScrollView);

            rootVisualElement.Add(section);
        }

        private void BuildActionsSection()
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.justifyContent = Justify.FlexEnd;
            container.style.alignItems = Align.Center;
            container.style.marginTop = 4;
            container.style.marginBottom = 4;

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

            rootVisualElement.Add(container);
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
            field.style.borderTopWidth = 1;
            field.style.borderLeftWidth = 1;
            field.style.borderRightWidth = 1;
            field.style.borderBottomWidth = 1;
            field.style.borderTopColor = BorderColor;
            field.style.borderLeftColor = BorderColor;
            field.style.borderRightColor = BorderColor;
            field.style.borderBottomColor = BorderColor;
            field.style.backgroundColor = BgInput;
            field.style.paddingLeft = 8;
            field.style.paddingRight = 8;
            field.style.paddingTop = 4;
            field.style.paddingBottom = 4;
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
                input.style.backgroundColor = Color.clear;
                input.style.paddingLeft = 0;
                input.style.paddingRight = 0;
                input.style.marginLeft = 0;
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

            var activeCategories = new HashSet<AssetCategory>();
            foreach (var kvp in _filterToggles)
            {
                if (kvp.Value.value)
                    activeCategories.Add(kvp.Key);
            }
            _processor.SetActiveCategories(activeCategories);
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

            var oldContainer = BuildOldNameHighlight(item.OriginalName, item.MatchedText, item.IsValid, _processor.CaseSensitive);
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

        private static VisualElement BuildOldNameHighlight(string name, string matchedText, bool isValid, bool caseSensitive)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.flexWrap = Wrap.NoWrap;
            container.style.overflow = Overflow.Hidden;

            var baseColor = isValid ? TextSecondary : TextDim;

            if (string.IsNullOrEmpty(matchedText))
            {
                var label = new Label(name);
                label.style.fontSize = 12;
                label.style.color = baseColor;
                label.style.whiteSpace = WhiteSpace.NoWrap;
                container.Add(label);
                return container;
            }

            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int searchStart = 0;
            while (searchStart < name.Length)
            {
                int idx = name.IndexOf(matchedText, searchStart, comparison);
                if (idx < 0)
                {
                    var remaining = new Label(name.Substring(searchStart));
                    remaining.style.fontSize = 12;
                    remaining.style.color = baseColor;
                    remaining.style.whiteSpace = WhiteSpace.NoWrap;
                    remaining.style.flexShrink = 0;
                    container.Add(remaining);
                    break;
                }

                if (idx > searchStart)
                {
                    var before = new Label(name.Substring(searchStart, idx - searchStart));
                    before.style.fontSize = 12;
                    before.style.color = baseColor;
                    before.style.whiteSpace = WhiteSpace.NoWrap;
                    before.style.flexShrink = 0;
                    container.Add(before);
                }

                var match = new Label(name.Substring(idx, matchedText.Length));
                match.style.fontSize = 12;
                match.style.color = MatchHighlight;
                match.style.whiteSpace = WhiteSpace.NoWrap;
                match.style.unityFontStyleAndWeight = FontStyle.Bold;
                match.style.flexShrink = 0;
                container.Add(match);

                searchStart = idx + matchedText.Length;
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
                label.style.whiteSpace = WhiteSpace.NoWrap;
                container.Add(label);
                return container;
            }

            var (prefix, middle, suffix) = ComputeDiff(oldName, newName);

            if (prefix.Length > 0)
            {
                var pLabel = new Label(prefix);
                pLabel.style.fontSize = 12;
                pLabel.style.color = TextPrimary;
                pLabel.style.whiteSpace = WhiteSpace.NoWrap;
                pLabel.style.flexShrink = 0;
                container.Add(pLabel);
            }

            if (middle.Length > 0)
            {
                var mLabel = new Label(middle);
                mLabel.style.fontSize = 12;
                mLabel.style.color = GreenHighlight;
                mLabel.style.whiteSpace = WhiteSpace.NoWrap;
                mLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                mLabel.style.flexShrink = 0;
                container.Add(mLabel);
            }

            if (suffix.Length > 0)
            {
                var sLabel = new Label(suffix);
                sLabel.style.fontSize = 12;
                sLabel.style.color = TextPrimary;
                sLabel.style.whiteSpace = WhiteSpace.NoWrap;
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
