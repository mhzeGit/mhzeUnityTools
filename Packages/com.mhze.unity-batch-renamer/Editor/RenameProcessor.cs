using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mhze.BatchRenamer
{
    public enum TextCaseMode
    {
        None,
        Lowercase,
        Uppercase,
        TitleCase,
        SentenceCase,
        CamelCase,
        PascalCase
    }

    public enum NumberFormatPreset
    {
        UnderscoreN,
        Parenthesized,
        DashN,
        SpaceN,
        RawN,
        UnderscorePadded,
        DotN
    }

    public class RenameItem
    {
        public Object Target;
        public string OriginalName;
        public string NewName;
        public List<SearchExpression.MatchEntry> MatchedTexts;
        public bool IsValid = true;
    }

    public class RenameProcessor
    {
        public string SearchPattern = "";
        public string ReplaceText = "";
        public string Prefix = "";
        public string Suffix = "";
        public TextCaseMode TextCase = TextCaseMode.None;
        public bool PreserveNumbers;
        public NumberFormatPreset NumberFormat = NumberFormatPreset.UnderscoreN;
        public bool CaseSensitive = false;
        public HashSet<AssetCategory> EnabledCategories = new HashSet<AssetCategory>();

        public List<RenameItem> Items = new List<RenameItem>();
        private SearchExpression _cachedExpression;
        private string _lastSearchPattern;
        private bool _lastCaseSensitive;

        public void CollectFromObjects(Object[] objects)
        {
            Items.Clear();

            if (objects == null || objects.Length == 0)
                return;

            bool hasSceneGameObjects = false;
            bool hasAssets = false;

            foreach (var obj in objects)
            {
                if (obj is GameObject go)
                {
                    string path = AssetDatabase.GetAssetPath(go);
                    if (string.IsNullOrEmpty(path))
                        hasSceneGameObjects = true;
                    else
                        hasAssets = true;
                }
                else
                {
                    hasAssets = true;
                }
            }

            if (hasAssets || (!hasSceneGameObjects))
            {
                CollectFromProjectSelection(objects);
            }
            else
            {
                CollectFromHierarchySelection(objects);
            }
        }

        public void SetActiveCategories(HashSet<AssetCategory> categories)
        {
            EnabledCategories = categories;
        }

        private void CollectFromProjectSelection(Object[] objects)
        {
            var visited = new HashSet<string>();
            foreach (var obj in objects)
            {
                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;

                if (AssetDatabase.IsValidFolder(path))
                {
                    CollectFromFolder(path, visited);
                }
                else
                {
                    if (visited.Add(path))
                    {
                        Items.Add(new RenameItem
                        {
                            Target = obj,
                            OriginalName = obj.name
                        });
                    }
                }
            }
        }

        private void CollectFromFolder(string folderPath, HashSet<string> visited)
        {
            var guids = AssetDatabase.FindAssets("", new[] { folderPath });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (visited.Add(path))
                {
                    var obj = AssetDatabase.LoadAssetAtPath<Object>(path);
                    if (obj != null)
                    {
                        Items.Add(new RenameItem
                        {
                            Target = obj,
                            OriginalName = obj.name
                        });
                    }
                }
            }
        }

        private void CollectFromHierarchySelection(Object[] objects)
        {
            foreach (var obj in objects)
            {
                var go = obj as GameObject;
                if (go != null)
                {
                    Items.Add(new RenameItem
                    {
                        Target = go,
                        OriginalName = go.name
                    });
                }
            }
        }

        public void RefreshPreview()
        {
            if (Items.Count == 0) return;

            bool patternChanged = SearchPattern != _lastSearchPattern || CaseSensitive != _lastCaseSensitive;
            if (patternChanged)
            {
                _cachedExpression = !string.IsNullOrEmpty(SearchPattern)
                    ? SearchExpression.Parse(SearchPattern, CaseSensitive)
                    : null;
                _lastSearchPattern = SearchPattern;
                _lastCaseSensitive = CaseSensitive;
                if (_cachedExpression != null)
                    Debug.Log($"[BatchRenamer] Parsed '{SearchPattern}' (caseSensitive={CaseSensitive}) → {_cachedExpression.Describe()}");
            }

            bool hasActiveFilters = EnabledCategories.Count > 0;

            foreach (var item in Items)
            {
                List<SearchExpression.MatchEntry> matchedEntries = null;
                if (_cachedExpression != null)
                    matchedEntries = _cachedExpression.Match(item.OriginalName);
                bool matchesSearch = _cachedExpression == null || matchedEntries.Count > 0;
                bool matchesType = !hasActiveFilters || EnabledCategories.Contains(ClassifyObject(item.Target));

                item.IsValid = matchesSearch && matchesType;
                item.MatchedTexts = matchesSearch ? matchedEntries : null;

                if (matchesSearch)
                {
                    item.NewName = ComputeNewName(item.OriginalName, matchedEntries);
                }
                else
                {
                    item.NewName = item.OriginalName;
                }

                if (_cachedExpression != null && patternChanged && Items.Count <= 5)
                    Debug.Log($"[BatchRenamer]  item='{item.OriginalName}' match={matchesSearch} valid={item.IsValid} matched='{(matchedEntries != null ? string.Join(",", matchedEntries.ConvertAll(e => e.Text)) : "null")}' new='{item.NewName}'");
            }
        }

        private string EvaluateConditions(string input, string originalName)
        {
            if (string.IsNullOrEmpty(input))
                return input ?? "";

            var comparison = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var sb = new System.Text.StringBuilder();
            int i = 0;

            while (i < input.Length)
            {
                if (input[i] == '?' || input[i] == '!')
                {
                    bool isNot = input[i] == '!';
                    i++;

                    if (!isNot && i < input.Length && input[i] == '!')
                    {
                        isNot = true;
                        i++;
                    }

                    int condStart = i;
                    while (i < input.Length && input[i] != ':')
                        i++;
                    string condition = input.Substring(condStart, i - condStart).Trim();

                    if (i < input.Length) i++;

                    int trueStart = i;
                    while (i < input.Length && input[i] != ':')
                        i++;
                    string trueValue = input.Substring(trueStart, i - trueStart).Trim();

                    if (i < input.Length) i++;

                    int falseStart = i;
                    while (i < input.Length && input[i] != '?' && input[i] != '!')
                        i++;
                    string falseValue = input.Substring(falseStart, i - falseStart).Trim();

                    bool condMet = originalName.IndexOf(condition, comparison) >= 0;
                    sb.Append(isNot ? (condMet ? falseValue : trueValue) : (condMet ? trueValue : falseValue));
                }
                else
                {
                    sb.Append(input[i]);
                    i++;
                }
            }

            return sb.ToString();
        }

        private string ComputeNewName(string originalName, List<SearchExpression.MatchEntry> matchedEntries)
        {
            string result = originalName;
            string preservedNumber = null;
            string resolvedReplaceText = EvaluateConditions(ReplaceText, originalName);
            string resolvedPrefix = EvaluateConditions(Prefix, originalName);
            string resolvedSuffix = EvaluateConditions(Suffix, originalName);

            if (PreserveNumbers)
            {
                var match = Regex.Match(result, @"^(.+?)[\s\-_.]*(\d+)$");
                if (match.Success)
                {
                    result = match.Groups[1].Value;
                    preservedNumber = match.Groups[2].Value;
                }
            }

            var realEntries = matchedEntries?.FindAll(e => e.Index >= 0 && e.Text.Length > 0 && e.Index + e.Text.Length <= result.Length);
            if (realEntries != null && realEntries.Count > 0)
            {
                var sorted = new List<SearchExpression.MatchEntry>(realEntries);
                sorted.Sort((a, b) => a.Index.CompareTo(b.Index));

                bool contiguous = true;
                int next = sorted[0].Index + sorted[0].Text.Length;
                for (int i = 1; i < sorted.Count && contiguous; i++)
                {
                    if (sorted[i].Index != next)
                        contiguous = false;
                    else
                        next = sorted[i].Index + sorted[i].Text.Length;
                }

                bool hasNumberToken = resolvedReplaceText.IndexOf("{Number}", StringComparison.OrdinalIgnoreCase) >= 0;

                if (contiguous && hasNumberToken && sorted.Count > 1)
                {
                    string number = "";
                    foreach (var e in sorted)
                    {
                        var nm = Regex.Match(e.Text, @"\d+");
                        if (nm.Success) { number = nm.Value; break; }
                    }
                    var resolved = Regex.Replace(resolvedReplaceText, @"\{number\}", number, RegexOptions.IgnoreCase);
                    int spanStart = sorted[0].Index;
                    int spanEnd = sorted[sorted.Count - 1].Index + sorted[sorted.Count - 1].Text.Length;
                    result = result.Substring(0, spanStart) + resolved + result.Substring(spanEnd);
                }
                else
                {
                    sorted.Sort((a, b) => b.Index.CompareTo(a.Index));
                    foreach (var entry in sorted)
                    {
                        string replacement = resolvedReplaceText;
                        if (hasNumberToken)
                        {
                            var numberMatch = Regex.Match(entry.Text, @"\d+");
                            string numberVal = numberMatch.Success ? numberMatch.Value : "";
                            replacement = Regex.Replace(replacement, @"\{number\}", numberVal, RegexOptions.IgnoreCase);
                        }
                        result = result.Substring(0, entry.Index) + replacement + result.Substring(entry.Index + entry.Text.Length);
                    }
                }
            }

            if (!string.IsNullOrEmpty(resolvedPrefix))
                result = resolvedPrefix + result;

            if (!string.IsNullOrEmpty(resolvedSuffix))
                result = result + resolvedSuffix;

            result = ApplyTextCase(result);

            if (PreserveNumbers && preservedNumber != null)
            {
                result = ApplyNumberFormat(result, preservedNumber);
            }

            return result;
        }

        private string ApplyTextCase(string input)
        {
            switch (TextCase)
            {
                case TextCaseMode.Lowercase:
                    return input.ToLowerInvariant();
                case TextCaseMode.Uppercase:
                    return input.ToUpperInvariant();
                case TextCaseMode.TitleCase:
                    return CultureTitleCase(input);
                case TextCaseMode.SentenceCase:
                    if (string.IsNullOrEmpty(input)) return input;
                    return char.ToUpperInvariant(input[0]) + input.Substring(1).ToLowerInvariant();
                case TextCaseMode.CamelCase:
                    return ToCamelCase(input);
                case TextCaseMode.PascalCase:
                    return ToPascalCase(input);
                default:
                    return input;
            }
        }

        private static string CultureTitleCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var words = input.Split(new[] { ' ', '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1).ToLowerInvariant();
                }
            }
            return string.Join("_", words);
        }

        private static string ToCamelCase(string input)
        {
            var pascal = ToPascalCase(input);
            if (string.IsNullOrEmpty(pascal)) return pascal;
            return char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
        }

        private static string ToPascalCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var words = input.Split(new[] { ' ', '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
                }
            }
            return string.Join("", words);
        }

        private string ApplyNumberFormat(string baseName, string number)
        {
            string format = GetNumberFormatString(NumberFormat);
            return baseName + string.Format(format, number);
        }

        private static string GetNumberFormatString(NumberFormatPreset preset)
        {
            switch (preset)
            {
                case NumberFormatPreset.UnderscoreN: return "_{0}";
                case NumberFormatPreset.Parenthesized: return " ({0})";
                case NumberFormatPreset.DashN: return "-{0}";
                case NumberFormatPreset.SpaceN: return " {0}";
                case NumberFormatPreset.RawN: return "{0}";
                case NumberFormatPreset.UnderscorePadded: return "_{0}";
                case NumberFormatPreset.DotN: return ".{0}";
                default: return "_{0}";
            }
        }

        public static AssetCategory ClassifyObject(Object obj)
        {
            if (obj == null) return AssetCategory.Other;

            if (obj is GameObject go)
            {
                string path = AssetDatabase.GetAssetPath(go);
                if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                    return AssetCategory.Folder;

                if (PrefabUtility.IsPartOfAnyPrefab(go) || PrefabUtility.GetPrefabAssetType(go) != PrefabAssetType.NotAPrefab)
                    return AssetCategory.Prefab;

                return AssetCategory.GameObject;
            }

            string assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(assetPath) && AssetDatabase.IsValidFolder(assetPath))
                return AssetCategory.Folder;

            if (obj is Material) return AssetCategory.Material;
            if (obj is Texture || obj is Texture2D) return AssetCategory.Texture;
            if (obj is MonoScript || obj is TextAsset) return AssetCategory.Script;
            if (obj is AudioClip) return AssetCategory.Audio;
            if (obj is AnimationClip) return AssetCategory.Animation;
            if (obj is SceneAsset) return AssetCategory.Scene;

            if (!string.IsNullOrEmpty(assetPath))
            {
                string ext = Path.GetExtension(assetPath)?.ToLowerInvariant();
                switch (ext)
                {
                    case ".fbx":
                    case ".obj":
                    case ".blend":
                    case ".ma":
                    case ".mb":
                    case ".max":
                        return AssetCategory.Model;
                    case ".controller":
                    case ".anim":
                        return AssetCategory.Animation;
                    case ".cs":
                    case ".js":
                    case ".shader":
                    case ".hlsl":
                        return AssetCategory.Script;
                    case ".png":
                    case ".jpg":
                    case ".jpeg":
                    case ".tga":
                    case ".psd":
                    case ".exr":
                    case ".hdr":
                        return AssetCategory.Texture;
                    case ".mp3":
                    case ".wav":
                    case ".ogg":
                    case ".aiff":
                    case ".aif":
                        return AssetCategory.Audio;
                    case ".mat":
                        return AssetCategory.Material;
                    case ".prefab":
                        return AssetCategory.Prefab;
                    case ".unity":
                        return AssetCategory.Scene;
                }
            }

            return AssetCategory.Other;
        }

        public void ApplyRenames()
        {
            var (renamedCount, errorCount, hasHierarchyItems) = RenameCore();

            if (hasHierarchyItems)
                EditorApplication.DirtyHierarchyWindowSorting();

            AssetDatabase.Refresh();
            EditorApplication.delayCall += () =>
            {
                EditorUtility.DisplayDialog("Batch Rename Complete",
                    $"Successfully renamed {renamedCount} item(s).\n" +
                    (errorCount > 0 ? $"{errorCount} error(s) occurred.\nCheck Console for details." : ""),
                    "OK");
            };
        }

        private (int renamedCount, int errorCount, bool hasHierarchyItems) RenameCore()
        {
            int renamedCount = 0;
            int errorCount = 0;
            bool hasHierarchyItems = false;

            var sceneObjects = new List<Object>();
            foreach (var item in Items)
            {
                if (item.Target is GameObject go)
                {
                    string path = AssetDatabase.GetAssetPath(go);
                    if (string.IsNullOrEmpty(path))
                        sceneObjects.Add(go);
                }
            }
            if (sceneObjects.Count > 0)
                Undo.RecordObjects(sceneObjects.ToArray(), "Batch Rename");

            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                if (!item.IsValid || item.Target == null) continue;
                if (item.OriginalName == item.NewName) continue;

                try
                {
                    string path = AssetDatabase.GetAssetPath(item.Target);

                    if (string.IsNullOrEmpty(path))
                    {
                        if (item.Target is GameObject go)
                        {
                            hasHierarchyItems = true;
                            go.name = item.NewName;
                            renamedCount++;
                        }
                    }
                    else
                    {
                        string directory = Path.GetDirectoryName(path);
                        string extension = Path.GetExtension(path);
                        string newPath = Path.Combine(directory, item.NewName + extension);

                        if (path == newPath) continue;

                        string error = AssetDatabase.RenameAsset(path, item.NewName);
                        if (string.IsNullOrEmpty(error))
                        {
                            renamedCount++;
                        }
                        else
                        {
                            Debug.LogError($"[Batch Renamer] Failed to rename {path}: {error}");
                            errorCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Batch Renamer] Error renaming {item.OriginalName}: {ex.Message}");
                    errorCount++;
                }
            }

            return (renamedCount, errorCount, hasHierarchyItems);
        }

        public void ApplyOperation(BatchRenameOperation op)
        {
            SearchPattern = op.searchPattern;
            ReplaceText = op.replaceText;
            Prefix = op.prefix;
            Suffix = op.suffix;
            TextCase = op.textCase;
            PreserveNumbers = op.preserveNumbers;
            NumberFormat = op.numberFormat;
            CaseSensitive = op.caseSensitive;
            EnabledCategories = new HashSet<AssetCategory>(op.enabledCategories);
        }

        public void RunOperations(List<BatchRenameOperation> operations)
        {
            if (operations == null || operations.Count == 0)
            {
                ApplyRenames();
                return;
            }

            int totalRenamed = 0;
            int totalErrors = 0;
            bool hasHierarchyItems = false;

            foreach (var op in operations)
            {
                ApplyOperation(op);
                RefreshPreview();

                var (renamed, errors, hierarchy) = RenameCore();
                totalRenamed += renamed;
                totalErrors += errors;
                if (hierarchy) hasHierarchyItems = true;

                foreach (var item in Items)
                {
                    if (item.IsValid && item.NewName != item.OriginalName)
                    {
                        item.OriginalName = item.NewName;
                    }
                }
            }

            if (hasHierarchyItems)
                EditorApplication.DirtyHierarchyWindowSorting();

            AssetDatabase.Refresh();

            var savedTotalRenamed = totalRenamed;
            var savedTotalErrors = totalErrors;
            EditorApplication.delayCall += () =>
            {
                EditorUtility.DisplayDialog("Batch Rename Complete",
                    $"Successfully renamed {savedTotalRenamed} item(s).\n" +
                    (savedTotalErrors > 0 ? $"{savedTotalErrors} error(s) occurred.\nCheck Console for details." : ""),
                    "OK");
            };
        }
    }

    public enum AssetCategory
    {
        All,
        Prefab,
        Material,
        Texture,
        Model,
        Audio,
        Script,
        Animation,
        Folder,
        Scene,
        GameObject,
        Other
    }

    public class SearchExpression
    {
        public struct MatchEntry
        {
            public string Text;
            public int Index;
        }

        public interface INode
        {
            // Returns all matched substrings with their positions if the name matches, empty list otherwise.
            List<MatchEntry> Match(string name);
            string Describe();
        }

        public class Literal : INode
        {
            private readonly string _text;
            private readonly StringComparison _comparison;

            public Literal(string text, StringComparison comparison)
            {
                _text = text;
                _comparison = comparison;
            }

            public List<MatchEntry> Match(string name)
            {
                if (string.IsNullOrEmpty(_text))
                    return new List<MatchEntry>();
                var results = new List<MatchEntry>();
                int searchStart = 0;
                while (searchStart < name.Length)
                {
                    int idx = name.IndexOf(_text, searchStart, _comparison);
                    if (idx < 0) break;
                    results.Add(new MatchEntry { Text = name.Substring(idx, _text.Length), Index = idx });
                    searchStart = idx + _text.Length;
                }
                return results;
            }

            public string Describe() => $"Literal(\"{_text}\")";
        }

        public class NumberNode : INode
        {
            private static readonly Regex NumberRegex = new Regex(@"\d+");

            public List<MatchEntry> Match(string name)
            {
                var results = new List<MatchEntry>();
                var match = NumberRegex.Match(name);
                while (match.Success)
                {
                    results.Add(new MatchEntry { Text = match.Value, Index = match.Index });
                    match = match.NextMatch();
                }
                return results;
            }

            public string Describe() => "Number";
        }

        public class OrNode : INode
        {
            private readonly INode _left;
            private readonly INode _right;

            public OrNode(INode left, INode right)
            {
                _left = left;
                _right = right;
            }

            public List<MatchEntry> Match(string name)
            {
                var results = new List<MatchEntry>();
                results.AddRange(_left.Match(name));
                results.AddRange(_right.Match(name));
                return results;
            }

            public string Describe() => $"Or({_left.Describe()}, {_right.Describe()})";
        }

        public class AndNode : INode
        {
            private readonly INode _left;
            private readonly INode _right;

            public AndNode(INode left, INode right)
            {
                _left = left;
                _right = right;
            }

            public List<MatchEntry> Match(string name)
            {
                var leftResult = _left.Match(name);
                if (leftResult.Count == 0) return new List<MatchEntry>();
                var rightResult = _right.Match(name);
                if (rightResult.Count == 0) return new List<MatchEntry>();
                var results = new List<MatchEntry>();
                results.AddRange(leftResult);
                results.AddRange(rightResult);
                return results;
            }

            public string Describe() => $"And({_left.Describe()}, {_right.Describe()})";
        }

        public class NotNode : INode
        {
            private readonly INode _inner;

            public NotNode(INode inner)
            {
                _inner = inner;
            }

            public List<MatchEntry> Match(string name)
            {
                var innerResult = _inner.Match(name);
                if (innerResult.Count > 0)
                    return new List<MatchEntry>();
                return new List<MatchEntry> { new MatchEntry { Text = "", Index = -1 } };
            }

            public string Describe() => $"Not({_inner.Describe()})";
        }

        private readonly INode _root;

        public SearchExpression(INode root)
        {
            _root = root;
        }

        // Returns all matched entries if name matches, empty list otherwise.
        public List<MatchEntry> Match(string name)
        {
            return _root?.Match(name) ?? new List<MatchEntry>();
        }

        public string Describe()
        {
            return _root?.Describe() ?? "null";
        }

        public static SearchExpression Parse(string input, bool caseSensitive = false)
        {
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            if (string.IsNullOrEmpty(input))
                return new SearchExpression(new Literal("", comparison));

            var tokens = Tokenize(input);
            int pos = 0;
            var result = ParseExpression(tokens, ref pos, comparison);
            return new SearchExpression(result);
        }

        private static List<string> Tokenize(string input)
        {
            var tokens = new List<string>();
            int i = 0;
            while (i < input.Length)
            {
                if (input[i] == '{')
                {
                    int close = i + 1;
                    while (close < input.Length && input[close] != '}')
                        close++;
                    if (close < input.Length)
                    {
                        var word = input.Substring(i + 1, close - i - 1).Trim();
                        if (word.Equals("number", StringComparison.OrdinalIgnoreCase))
                        {
                            tokens.Add("{Number}");
                            i = close + 1;
                            continue;
                        }
                    }
                    // Not {Number} — read as literal from {
                    var fallback = "";
                    while (i < input.Length && input[i] != '|' && input[i] != '&' && input[i] != '[' && input[i] != ']')
                    {
                        fallback += input[i];
                        i++;
                    }
                    if (fallback.Length > 0)
                        tokens.Add(fallback);
                    continue;
                }

                if (input[i] == '[' || input[i] == ']')
                {
                    tokens.Add(input[i].ToString());
                    i++;
                    continue;
                }

                if (input[i] == '|')
                {
                    tokens.Add("||");
                    i++;
                    if (i < input.Length && input[i] == '|') i++;
                    continue;
                }

                if (input[i] == '&')
                {
                    tokens.Add("&&");
                    i++;
                    if (i < input.Length && input[i] == '&') i++;
                    continue;
                }

                var lit = "";
                while (i < input.Length && input[i] != '{' && input[i] != '[' && input[i] != ']' && input[i] != '|' && input[i] != '&')
                {
                    lit += input[i];
                    i++;
                }
                if (lit.Length > 0)
                    tokens.Add(lit);
            }
            return tokens;
        }

        private static INode ParseExpression(List<string> tokens, ref int pos, StringComparison comparison)
        {
            var left = ParseTerm(tokens, ref pos, comparison);

            while (pos < tokens.Count && tokens[pos] == "||")
            {
                pos++;
                var right = ParseTerm(tokens, ref pos, comparison);
                left = new OrNode(left, right);
            }

            return left;
        }

        private static INode ParseTerm(List<string> tokens, ref int pos, StringComparison comparison)
        {
            var left = ParseFactor(tokens, ref pos, comparison);

            while (pos < tokens.Count)
            {
                if (tokens[pos] == "&&")
                {
                    pos++;
                }
                else if (tokens[pos] == "||" || tokens[pos] == "]")
                {
                    break;
                }
                // implicit AND — consecutive factors are AND-connected
                var right = ParseFactor(tokens, ref pos, comparison);
                left = new AndNode(left, right);
            }

            return left;
        }

        private static INode ParseFactor(List<string> tokens, ref int pos, StringComparison comparison)
        {
            if (pos >= tokens.Count)
                return new Literal("", comparison);

            if (tokens[pos] == "[")
            {
                pos++;
                var expr = ParseExpression(tokens, ref pos, comparison);
                if (pos < tokens.Count && tokens[pos] == "]")
                    pos++;
                return expr;
            }

            if (tokens[pos] == "{Number}")
            {
                pos++;
                return new NumberNode();
            }

            var text = tokens[pos];
            pos++;

            if (text.StartsWith("!"))
            {
                var innerText = text.Substring(1);
                if (innerText.Length > 0)
                    return new NotNode(new Literal(innerText, comparison));
                return new Literal(text, comparison);
            }

            return new Literal(text, comparison);
        }

    }
}
