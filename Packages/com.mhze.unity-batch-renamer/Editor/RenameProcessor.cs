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
        public string MatchedText;
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
        public HashSet<AssetCategory> EnabledCategories = new HashSet<AssetCategory>();

        public List<RenameItem> Items = new List<RenameItem>();
        private SearchExpression _cachedExpression;
        private string _lastSearchPattern;

        public void CollectFromObjects(Object[] objects)
        {
            Items.Clear();

            if (objects == null || objects.Length == 0)
                return;

            bool hasGameObjects = false;
            bool hasAssets = false;

            foreach (var obj in objects)
            {
                if (obj is GameObject)
                    hasGameObjects = true;
                else
                    hasAssets = true;
            }

            if (hasAssets || (!hasGameObjects))
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

            bool patternChanged = SearchPattern != _lastSearchPattern;
            if (patternChanged)
            {
                _cachedExpression = !string.IsNullOrWhiteSpace(SearchPattern)
                    ? SearchExpression.Parse(SearchPattern)
                    : null;
                _lastSearchPattern = SearchPattern;
                if (_cachedExpression != null)
                    Debug.Log($"[BatchRenamer] Parsed '{SearchPattern}' → {_cachedExpression.Describe()}");
            }

            bool hasActiveFilters = EnabledCategories.Count > 0;

            foreach (var item in Items)
            {
                string matchedText = null;
                bool matchesSearch = _cachedExpression == null ||
                    ((matchedText = _cachedExpression.Match(item.OriginalName)) != null);
                bool matchesType = !hasActiveFilters || EnabledCategories.Contains(ClassifyObject(item.Target));

                item.IsValid = matchesSearch && matchesType;
                item.MatchedText = matchesSearch ? matchedText : null;

                if (matchesSearch)
                {
                    item.NewName = ComputeNewName(item.OriginalName, matchedText);
                }
                else
                {
                    item.NewName = item.OriginalName;
                }

                if (_cachedExpression != null && patternChanged && Items.Count <= 5)
                    Debug.Log($"[BatchRenamer]  item='{item.OriginalName}' match={matchesSearch} valid={item.IsValid} matched='{matchedText}' new='{item.NewName}'");
            }
        }

        private string ComputeNewName(string originalName, string matchedText)
        {
            string result = originalName;
            string preservedNumber = null;

            if (PreserveNumbers)
            {
                var match = Regex.Match(result, @"^(.+?)[\s\-_.]*(\d+)$");
                if (match.Success)
                {
                    result = match.Groups[1].Value;
                    preservedNumber = match.Groups[2].Value;
                }
            }

            if (matchedText != null)
            {
                result = result.Replace(matchedText, ReplaceText);
            }

            if (!string.IsNullOrEmpty(Prefix))
                result = Prefix + result;

            if (!string.IsNullOrEmpty(Suffix))
                result = result + Suffix;

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

                return AssetCategory.Other;
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
            bool hasHierarchyItems = false;
            int renamedCount = 0;
            int errorCount = 0;

            Undo.RecordObjects(Items.FindAll(i => i.Target is GameObject).ConvertAll(i => i.Target).ToArray(), "Batch Rename");

            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                if (!item.IsValid || item.Target == null) continue;
                if (item.OriginalName == item.NewName) continue;

                try
                {
                    if (item.Target is GameObject go)
                    {
                        hasHierarchyItems = true;
                        go.name = item.NewName;
                        renamedCount++;
                    }
                    else
                    {
                        string path = AssetDatabase.GetAssetPath(item.Target);
                        if (string.IsNullOrEmpty(path)) continue;

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

            if (hasHierarchyItems)
            {
                EditorApplication.DirtyHierarchyWindowSorting();
            }

            AssetDatabase.Refresh();
            EditorApplication.delayCall += () =>
            {
                EditorUtility.DisplayDialog("Batch Rename Complete",
                    $"Successfully renamed {renamedCount} item(s).\n" +
                    (errorCount > 0 ? $"{errorCount} error(s) occurred.\nCheck Console for details." : ""),
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
        Other
    }

    public class SearchExpression
    {
        public interface INode
        {
            // Returns the matched text if name matches, null otherwise.
            string Match(string name);
            string Describe();
        }

        public class Literal : INode
        {
            private readonly string _text;

            public Literal(string text)
            {
                _text = text;
            }

            public string Match(string name)
            {
                if (string.IsNullOrEmpty(_text)) return name;
                int idx = name.IndexOf(_text, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) return name.Substring(idx, _text.Length);
                return null;
            }

            public string Describe() => $"Literal(\"{_text}\")";
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

            public string Match(string name)
            {
                var leftResult = _left.Match(name);
                if (leftResult != null) return leftResult;
                return _right.Match(name);
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

            public string Match(string name)
            {
                var leftResult = _left.Match(name);
                if (leftResult == null) return null;
                var rightResult = _right.Match(name);
                if (rightResult == null) return null;
                return leftResult;
            }

            public string Describe() => $"And({_left.Describe()}, {_right.Describe()})";
        }

        private readonly INode _root;

        public SearchExpression(INode root)
        {
            _root = root;
        }

        // Returns the matched text if name matches, null otherwise.
        public string Match(string name)
        {
            return _root?.Match(name);
        }

        public string Describe()
        {
            return _root?.Describe() ?? "null";
        }

        public static SearchExpression Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new SearchExpression(new Literal(""));

            var tokens = Tokenize(input);
            int pos = 0;
            var result = ParseExpression(tokens, ref pos);
            return new SearchExpression(result);
        }

        private static List<string> Tokenize(string input)
        {
            var tokens = new List<string>();
            int i = 0;
            while (i < input.Length)
            {
                if (char.IsWhiteSpace(input[i]))
                {
                    i++;
                    continue;
                }

                if (input[i] == '(' || input[i] == ')')
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
                while (i < input.Length && input[i] != '|' && input[i] != '&' && input[i] != '(' && input[i] != ')')
                {
                    lit += input[i];
                    i++;
                }
                var trimmedLit = lit.Trim();
                if (trimmedLit.Length > 0)
                    tokens.Add(trimmedLit);
            }
            return tokens;
        }

        private static INode ParseExpression(List<string> tokens, ref int pos)
        {
            var left = ParseTerm(tokens, ref pos);

            while (pos < tokens.Count && tokens[pos] == "||")
            {
                pos++;
                var right = ParseTerm(tokens, ref pos);
                left = new OrNode(left, right);
            }

            return left;
        }

        private static INode ParseTerm(List<string> tokens, ref int pos)
        {
            var left = ParseFactor(tokens, ref pos);

            while (pos < tokens.Count && tokens[pos] == "&&")
            {
                pos++;
                var right = ParseFactor(tokens, ref pos);
                left = new AndNode(left, right);
            }

            return left;
        }

        private static INode ParseFactor(List<string> tokens, ref int pos)
        {
            if (pos >= tokens.Count)
                return new Literal("");

            if (tokens[pos] == "(")
            {
                pos++;
                var expr = ParseExpression(tokens, ref pos);
                if (pos < tokens.Count && tokens[pos] == ")")
                    pos++;
                return expr;
            }

            var text = tokens[pos];
            pos++;
            return new Literal(text);
        }

    }
}
