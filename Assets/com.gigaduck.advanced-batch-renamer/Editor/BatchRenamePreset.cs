using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gigaduck.BatchRenamer
{
    [Serializable]
    public class BatchRenameOperation
    {
        public string name = "Rename Operation";
        public string searchPattern = "";
        public string replaceText = "";
        public string prefix = "";
        public string suffix = "";
        public bool skipIfAlreadyExists;
        public TextCaseMode textCase = TextCaseMode.None;
        public bool preserveNumbers;
        public NumberFormatPreset numberFormat = NumberFormatPreset.UnderscoreN;
        public bool caseSensitive;
        public bool useRegex;
        public bool searchMustMatch = true;
        public int startIndex = 1;
        public List<AssetCategory> enabledCategories = new List<AssetCategory>();
        public List<TextureSubCategory> enabledTextureSubCategories = new List<TextureSubCategory>();
        public List<HierarchyCategory> enabledHierarchyCategories = new List<HierarchyCategory>();
    }

    public class BatchRenamePreset : ScriptableObject
    {
        public List<BatchRenameOperation> operations = new List<BatchRenameOperation>();
    }
}
