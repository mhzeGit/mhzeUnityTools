using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Gigaduck.LightmapWorkbench
{
    /// <summary>Checks staged FBX lightmap settings, validation, targeted changes and persistence using a disposable imported copy.</summary>
    public static class LightmapUVWorkbenchChecks
    {
        [MenuItem("Tools/Gigaduck/Validate Lightmap UV Workbench")]
        public static void Run()
        {
            string source = AssetDatabase.FindAssets("t:Model", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => new FileInfo(p).Length).FirstOrDefault();
            if (source == null) throw new InvalidOperationException("Checks need one FBX under Assets to make a disposable copy.");
            string folder = "Assets/__LightmapUVWorkbenchCheck_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            string path = folder + "/Fixture.fbx";
            try
            {
                Check(AssetDatabase.CopyAsset(source, path), "Could not create isolated FBX fixture.");
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                var original = LightmapUVSettings.Read(importer);
                var draft = original;
                draft.generate = !original.generate;
                draft.hardAngle = 72;
                draft.angleError = 9;
                draft.areaError = 17;
                draft.marginMethod = ModelImporterSecondaryUVMarginMethod.Manual;
                draft.packMargin = 7;
                draft.minResolution = 32;
                draft.minScale = .75f;
                Check(LightmapUVSettings.Read(importer).Equals(original), "Staging altered the actual importer.");
                float originalGlobalScale = importer.globalScale;
                bool originalAnimation = importer.importAnimation;
                draft.ApplyTo(importer, original);
                importer.SaveAndReimport();
                importer = (ModelImporter)AssetImporter.GetAtPath(path);
                Check(LightmapUVSettings.Read(importer).Equals(draft), "The eight settings did not survive reimport.");
                Check(importer.globalScale == originalGlobalScale && importer.importAnimation == originalAnimation, "Unrelated import settings changed.");

                var patch = draft;
                patch.packMargin = 3;
                importer.secondaryUVMinObjectScale = 4;
                patch.ApplyTo(importer, draft);
                Check(importer.secondaryUVPackMargin == 3 && importer.secondaryUVMinObjectScale == 4, "A single-field edit overwrote an untouched field.");
                var beforeInvalid = LightmapUVSettings.Read(importer);
                foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
                {
                    var bad = patch;
                    bad.minScale = invalid;
                    bool rejected = false;
                    try { bad.ApplyTo(importer, draft); }
                    catch (ArgumentOutOfRangeException) { rejected = true; }
                    Check(rejected && LightmapUVSettings.Read(importer).Equals(beforeInvalid), "Invalid input changed the importer.");
                }
                Debug.Log("Lightmap UV Workbench checks passed: staging, eight-field reimport persistence, preservation of unrelated settings and untouched UV fields, invalid input rejection.");
            }
            finally { AssetDatabase.DeleteAsset(folder); }
        }

        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
