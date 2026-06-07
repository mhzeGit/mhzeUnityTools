using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace mhze.BatchRenamer
{
    [Serializable]
    public class BatchRenameOperation
    {
        public string name = "Rename Operation";
        public string searchPattern = "";
        public string replaceText = "";
        public string prefix = "";
        public string suffix = "";
        public TextCaseMode textCase = TextCaseMode.None;
        public bool preserveNumbers;
        public NumberFormatPreset numberFormat = NumberFormatPreset.UnderscoreN;
        public bool caseSensitive;
        public int startIndex = 1;
        public List<AssetCategory> enabledCategories = new List<AssetCategory>();
        public List<TextureSubCategory> enabledTextureSubCategories = new List<TextureSubCategory>();
        public List<HierarchyCategory> enabledHierarchyCategories = new List<HierarchyCategory>();
    }

    public class BatchRenamePreset : ScriptableObject
    {
        public List<BatchRenameOperation> operations = new List<BatchRenameOperation>();

        public static List<BatchRenamePreset> FindAll()
        {
            var guids = AssetDatabase.FindAssets("t:BatchRenamePreset");
            var presets = new List<BatchRenamePreset>();
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var preset = AssetDatabase.LoadAssetAtPath<BatchRenamePreset>(path);
                if (preset != null)
                    presets.Add(preset);
            }
            return presets;
        }
    }
}
