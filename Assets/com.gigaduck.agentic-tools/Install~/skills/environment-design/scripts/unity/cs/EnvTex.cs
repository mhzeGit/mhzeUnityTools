using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Scratch tooling: textured environment assets (compiled in memory by ucs.py; never added to the Unity project).
namespace ClaudeTmp
{
    [Serializable] public class TexCmd { public string cmd; public string list; public string texDir; public string matDir; public string prefabs; public string models; public float smoothness; public float glow; }

    public static class EnvTex
    {
        static string Stage = "";

        public static string Run(string arg)
        {
            var c = JsonUtility.FromJson<TexCmd>(arg);
            try
            {
                switch (c.cmd)
                {
                    case "tex": return ImportTextures(c);
                    case "mats": return MakeMaterials(c);
                    case "models": return ImportModels(c);
                    case "apply": return ApplyMaterials(c);
                    case "prefabs": return RemakePrefabs(c);
                    default: return "unknown cmd " + c.cmd;
                }
            }
            catch (Exception ex)
            {
                return "EXCEPTION at stage [" + Stage + "]: " + ex;
            }
        }

        static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            var parent = Path.GetDirectoryName(assetPath).Replace("\\", "/");
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetPath));
        }

        static string[] Lines(string file)
        {
            var res = new List<string>();
            foreach (var l in File.ReadAllLines(file)) if (!string.IsNullOrWhiteSpace(l)) res.Add(l.Trim());
            return res.ToArray();
        }

        // list: "srcPng|assetPath"
        static string ImportTextures(TexCmd c)
        {
            var sb = new StringBuilder();
            int n = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var ln in Lines(c.list))
                {
                    var p = ln.Split('|');
                    var dst = p[1].Replace("\\", "/");
                    EnsureFolder(Path.GetDirectoryName(dst).Replace("\\", "/"));
                    File.Copy(p[0], dst, true);
                    n++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            foreach (var ln in Lines(c.list))
            {
                var dst = ln.Split('|')[1].Replace("\\", "/");
                var ti = AssetImporter.GetAtPath(dst) as TextureImporter;
                if (ti == null) { sb.AppendLine("no importer: " + dst); continue; }
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.mipmapEnabled = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Trilinear;
                ti.anisoLevel = 8;
                ti.maxTextureSize = 2048;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.alphaSource = TextureImporterAlphaSource.None;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.SaveAndReimport();
            }
            return "textures imported: " + n + (sb.Length > 0 ? "\n" + sb : "");
        }

        // list: "group|texAssetPath|glow(0/1)"
        static string MakeMaterials(TexCmd c)
        {
            EnsureFolder(c.matDir);
            var sb = new StringBuilder();
            foreach (var ln in Lines(c.list))
            {
                var p = ln.Split('|');
                string group = p[0];
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p[1]);
                if (tex == null) { sb.AppendLine("missing texture " + p[1]); continue; }
                Color tint = Color.white;
                if (p.Length > 3 && p[3].Length > 0)
                {
                    var t = p[3].Split(',');
                    tint = new Color(float.Parse(t[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(t[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(t[2], System.Globalization.CultureInfo.InvariantCulture), 1f);
                }
                float smooth = p.Length > 4 && p[4].Length > 0 ? float.Parse(p[4], System.Globalization.CultureInfo.InvariantCulture) : 0.12f;
                float glowI = p.Length > 5 && p[5].Length > 0 ? float.Parse(p[5], System.Globalization.CultureInfo.InvariantCulture) : 4f;
                Make(c, "@@PREFIX@@" + group, tex, false, tint, smooth, glowI);
                if (p.Length > 2 && p[2] == "1") Make(c, "@@PREFIX@@" + group + "_Glow", tex, true, tint, smooth, glowI);
            }
            AssetDatabase.SaveAssets();
            return "materials done\n" + sb;
        }

        static void Make(TexCmd c, string name, Texture2D tex, bool glow, Color tint, float smoothness, float glowI)
        {
            string path = c.matDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew) m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", glow ? Color.black : tint);   // the glow parts are pure emission: no diffuse light on them (the lantern light sits inside its glass)
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            if (glow)
            {
                m.EnableKeyword("_EMISSION");
                m.SetTexture("_EmissionMap", tex);
                float g = glowI;
                m.SetColor("_EmissionColor", new Color(g, g * 0.44f, g * 0.18f, 1f));   // orange, so ACES does not wash the glow out to yellow-white
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            if (glow) m.EnableKeyword("_EMISSION"); else m.DisableKeyword("_EMISSION");
            if (isNew) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
        }

        // list: "srcFbx|assetFbxPath"  (copies over the existing model so its GUID and mesh ids stay)
        static string ImportModels(TexCmd c)
        {
            int n = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var ln in Lines(c.list))
                {
                    var p = ln.Split('|');
                    var dst = p[1].Replace("\\", "/");
                    EnsureFolder(Path.GetDirectoryName(dst).Replace("\\", "/"));
                    File.Copy(p[0], dst, true);
                    n++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            return "models copied: " + n;
        }


        // list: one asset name per line. Rebuilds @@PREFIX@@<asset>.prefab from the (re-imported) model; materials come from the apply stage afterwards.
        static string RemakePrefabs(TexCmd c)
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (var asset in Lines(c.list))
            {
                Stage = "remake " + asset;
                string modelPath = c.models + "/" + asset + ".fbx";
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (model == null) { sb.AppendLine("no model " + modelPath); continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                inst.name = "@@PREFIX@@" + asset;
                string prefabPath = c.prefabs + "/@@PREFIX@@" + asset + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
                UnityEngine.Object.DestroyImmediate(inst);
                n++;
            }
            AssetDatabase.SaveAssets();
            return "prefabs remade: " + n + (sb.Length > 0 ? "\n" + sb : "");
        }

        static string Region(MeshRenderer r)
        {
            string nm = r.gameObject.name;
            int k = nm.IndexOf("__", StringComparison.Ordinal);
            if (k <= 0)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) { nm = mf.sharedMesh.name; k = nm.IndexOf("__", StringComparison.Ordinal); }
            }
            return k > 0 ? nm.Substring(0, k) : "";
        }

        // list: "asset|group"
        static string ApplyMaterials(TexCmd c)
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (var ln in Lines(c.list))
            {
                var p = ln.Split('|');
                string asset = p[0], group = p[1];
                string prefabPath = c.prefabs + "/@@PREFIX@@" + asset + ".prefab";
                Stage = "prefab " + asset;
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    var plain = AssetDatabase.LoadAssetAtPath<Material>(c.matDir + "/@@PREFIX@@" + group + ".mat");
                    var glow = AssetDatabase.LoadAssetAtPath<Material>(c.matDir + "/@@PREFIX@@" + group + "_Glow.mat");
                    if (plain == null) { sb.AppendLine("missing material for group " + group); continue; }
                    foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        string region = Region(r);
                        Material m = (region == "Glow" && glow != null) ? glow : plain;
                        r.sharedMaterial = m;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    n++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            return "prefabs updated: " + n + (sb.Length > 0 ? "\n" + sb : "");
        }
    }
}
