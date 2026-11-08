using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Scratch tooling for the environment (compiled in memory by ucs.py; never added to the Unity project).
namespace ClaudeTmp
{
    [Serializable] public class RItem { public string name; public string asset; public Vector3 pos; public Vector3 rot; public Vector3 scale; public string group; public Vector3 ay; public Vector3 az; public int useAx; }
    [Serializable] public class TCmd
    {
        public string cmd; public string json; public string scene; public string models; public string prefabs; public string matDir; public string mode; public string name; public string text;
    }

    public static class EnvTools
    {
        static string Stage = "";

        // clay palette per texture region (prefix before "__" in each mesh part name)
        static readonly Dictionary<string, Color> Palette = new Dictionary<string, Color>
        {
            {"Teal", new Color(0.24f, 0.38f, 0.39f)}, {"Iron", new Color(0.14f, 0.15f, 0.17f)}, {"Red", new Color(0.75f, 0.10f, 0.07f)}, {"Brick", new Color(0.62f, 0.27f, 0.17f)},
            {"Mortar", new Color(0.55f, 0.50f, 0.44f)}, {"Flag", new Color(0.45f, 0.40f, 0.36f)}, {"Oak", new Color(0.30f, 0.18f, 0.10f)}, {"Pine", new Color(0.55f, 0.38f, 0.20f)},
            {"Plaster", new Color(0.70f, 0.53f, 0.36f)}, {"Brass", new Color(0.88f, 0.80f, 0.60f)}, {"Glow", new Color(1.0f, 0.55f, 0.12f)}, {"Glass", new Color(0.30f, 0.42f, 0.20f)},
            {"Stoneware", new Color(0.80f, 0.75f, 0.65f)},
        };

        public static string Run(string arg)
        {
            var c = JsonUtility.FromJson<TCmd>(arg);
            try
            {
                switch (c.cmd)
                {
                    case "importmodels": return ImportModels(c);
                    case "makeprefabs": return MakePrefabs(c);
                    case "assemble": return Assemble(c);
                    case "listassets": return ListAssets(c);
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

        static string ImportModels(TCmd c)
        {
            // c.json: text file, one "sourcePath|assetPath" per line
            var lines = File.ReadAllLines(c.json);
            int n = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var ln in lines)
                {
                    if (string.IsNullOrWhiteSpace(ln)) continue;
                    var parts = ln.Split('|');
                    var dst = parts[1].Replace("\\", "/");
                    EnsureFolder(Path.GetDirectoryName(dst).Replace("\\", "/"));
                    File.Copy(parts[0], dst, true);
                    n++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            return "copied/imported " + n + " files";
        }

        static Material ClayMat(string dir, string region)
        {
            string path = dir + "/Clay_" + region + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            EnsureFolder(dir);
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Color col;
            if (!Palette.TryGetValue(region, out col)) col = new Color(0.6f, 0.6f, 0.6f);
            m.SetColor("_BaseColor", col);
            m.SetFloat("_Smoothness", 0.12f);
            m.SetFloat("_Metallic", 0f);
            if (region == "Glow")
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(3.0f, 1.2f, 0.25f));
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static string MakePrefabs(TCmd c)
        {
            // c.models = folder with FBX files, c.prefabs = output folder, c.matDir = clay materials folder
            EnsureFolder(c.prefabs);
            var guids = AssetDatabase.FindAssets("t:Model", new[] { c.models });
            int n = 0;
            var sb = new StringBuilder();
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) continue;
                string asset = Path.GetFileNameWithoutExtension(path);
                if (asset.EndsWith("_final")) asset = asset.Substring(0, asset.Length - 6);
                Stage = "prefab " + asset;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                inst.name = "@@PREFIX@@" + asset;
                foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                {
                    string nm = r.gameObject.name;
                    int k = nm.IndexOf("__", StringComparison.Ordinal);
                    if (k <= 0)
                    {
                        // single-mesh FBX: Unity puts the renderer on the root, the part name survives as the mesh name
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null) { nm = mf.sharedMesh.name; k = nm.IndexOf("__", StringComparison.Ordinal); }
                    }
                    string region = k > 0 ? nm.Substring(0, k) : "Mortar";
                    r.sharedMaterial = ClayMat(c.matDir, region);
                }
                string prefabPath = c.prefabs + "/@@PREFIX@@" + asset + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
                UnityEngine.Object.DestroyImmediate(inst);
                n++;
                sb.Append(asset).Append(' ');
            }
            AssetDatabase.SaveAssets();
            return "prefabs: " + n + " -> " + c.prefabs;
        }

        static List<RItem> ReadItems(string path)
        {
            var text = File.ReadAllText(path);
            var res = new List<RItem>();
            int idx = text.IndexOf("\"items\"", StringComparison.Ordinal);
            int depth = 1, start = -1;
            for (int i = text.IndexOf('[', idx); i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '{') { if (depth == 1 && start < 0) start = i; depth++; }
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 1 && start >= 0) { res.Add(JsonUtility.FromJson<RItem>(text.Substring(start, i - start + 1))); start = -1; }
                }
            }
            return res;
        }

        static Transform Group(Transform root, Dictionary<string, Transform> cache, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;
            Transform t;
            if (cache.TryGetValue(path, out t)) return t;
            Transform cur = root;
            string acc = "";
            foreach (var p in path.Split('/'))
            {
                acc = acc.Length == 0 ? p : acc + "/" + p;
                Transform nt;
                if (!cache.TryGetValue(acc, out nt))
                {
                    var go = new GameObject(p);
                    go.transform.SetParent(cur, false);
                    nt = go.transform;
                    cache[acc] = nt;
                }
                cur = nt;
            }
            return cur;
        }

        static string Assemble(TCmd c)
        {
            // c.json = layout, c.prefabs = prefab folder, c.scene = output scene path, c.mode = "clean" (flat blockout-style light) | "keep"
            Stage = "read";
            var items = ReadItems(c.json);
            var active = SceneManager.GetActiveScene();
            if (active.isDirty && active.path != c.scene) return "ABORT: active scene '" + active.name + "' has unsaved changes";
            EnsureFolder(Path.GetDirectoryName(c.scene).Replace("\\", "/"));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject(string.IsNullOrEmpty(c.name) ? "@@NAME@@" : c.name);
            var cache = new Dictionary<string, Transform>();
            var missing = new List<string>();
            int made = 0;
            foreach (var it in items)
            {
                Stage = "item " + it.name;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(c.prefabs + "/@@PREFIX@@" + it.asset + ".prefab");
                if (prefab == null) { if (!missing.Contains(it.asset)) missing.Add(it.asset); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.name = it.name;
                go.transform.SetParent(Group(root.transform, cache, it.group), false);
                go.transform.localPosition = it.pos;
                if (it.useAx == 1)
                {
                    var yv = it.ay.normalized;
                    var zv = it.az - yv * Vector3.Dot(it.az, yv);
                    if (zv.sqrMagnitude < 1e-6) zv = Mathf.Abs(yv.y) < 0.9f ? Vector3.up : Vector3.right;
                    zv = (zv - yv * Vector3.Dot(zv, yv)).normalized;
                    go.transform.localRotation = Quaternion.LookRotation(zv, yv);
                }
                else go.transform.localEulerAngles = it.rot;
                go.transform.localScale = it.scale;
                GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)(-1));
                made++;
            }
            Stage = "lights";
            if (c.mode == "clean")
            {
                var lgo = new GameObject("Preview_Sun");
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Directional; l.intensity = 1.1f; l.shadows = LightShadows.None; l.color = new Color(1f, 0.96f, 0.9f);
                lgo.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
                var fill = new GameObject("Preview_Fill");
                var fl = fill.AddComponent<Light>();
                fl.type = LightType.Directional; fl.intensity = 0.45f; fl.shadows = LightShadows.None; fl.color = new Color(0.8f, 0.85f, 1f);
                fill.transform.rotation = Quaternion.Euler(-30f, 140f, 0f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.42f, 0.42f, 0.46f);
                RenderSettings.fog = false;
            }
            Stage = "save";
            EditorSceneManager.SaveScene(scene, c.scene);
            return "assembled " + made + " of " + items.Count + " items -> " + c.scene + (missing.Count > 0 ? " MISSING prefabs: " + string.Join(", ", missing.ToArray()) : "");
        }

        static string ListAssets(TCmd c)
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { c.prefabs });
            var sb = new StringBuilder();
            foreach (var g in guids) sb.AppendLine(AssetDatabase.GUIDToAssetPath(g));
            return sb.ToString();
        }
    }
}
