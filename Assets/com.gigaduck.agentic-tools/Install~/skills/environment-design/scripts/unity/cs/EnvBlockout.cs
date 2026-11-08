using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Scratch tooling (compiled in memory by ucs.py; never added to the Unity project).
namespace ClaudeTmp
{
    [Serializable] public class LItem { public string name; public string parent; public string shape; public string prefab; public Vector3 pos; public Vector3 rot; public Vector3 size; public string mat; public int collider; }
    [Serializable] public class LLayout { public LItem[] items; }
    [Serializable] public class Cmd
    {
        public string cmd; public string json; public string scene; public string png;
        public float px, py, pz, tx, ty, tz, fov, roll; public int w, h; public bool pp; public bool ortho; public float orthoSize;
        public string name; public string hide;
    }

    public static class EnvBlockout
    {
        const string MatDir = "@@MATERIAL_DIR@@/Blockout";

        static readonly Dictionary<string, Color> Palette = new Dictionary<string, Color>
        {
            {"plaster", new Color(0.72f, 0.55f, 0.38f)}, {"stone", new Color(0.46f, 0.41f, 0.37f)}, {"dark", new Color(0.04f, 0.03f, 0.03f)},
            {"iron", new Color(0.16f, 0.16f, 0.17f)}, {"brick", new Color(0.66f, 0.30f, 0.18f)}, {"wood", new Color(0.33f, 0.20f, 0.11f)},
            {"wood_light", new Color(0.58f, 0.40f, 0.21f)}, {"iron_teal", new Color(0.24f, 0.38f, 0.39f)}, {"iron_dark", new Color(0.12f, 0.14f, 0.16f)},
            {"red", new Color(0.80f, 0.08f, 0.06f)}, {"cream", new Color(0.88f, 0.80f, 0.60f)}, {"glass", new Color(0.30f, 0.42f, 0.20f)},
            {"glow", new Color(1.0f, 0.55f, 0.12f)},
        };

        static string Stage = "";

        public static string Run(string arg)
        {
            var c = JsonUtility.FromJson<Cmd>(arg);
            try
            {
                switch (c.cmd)
                {
                    case "build": return Build(c);
                    case "capture": return Capture(c);
                    case "info": return Info();
                    case "dbg": return Dbg(c);
                    case "import": return ImportFile(c);
                    case "model": return ModelInfo(c);
                    default: return "unknown cmd " + c.cmd;
                }
            }
            catch (Exception ex)
            {
                return "EXCEPTION at stage [" + Stage + "]: " + ex;
            }
        }

        // JsonUtility does not reliably fill arrays of custom classes for types from a dynamically loaded
        // assembly, so split the items array by brace depth and parse each element on its own.
        static LLayout ReadLayout(string path)
        {
            var text = File.ReadAllText(path);
            var res = new List<LItem>();
            int idx = text.IndexOf("\"items\"", StringComparison.Ordinal);
            int depth = 1, start = -1;   // we are inside the root object
            for (int i = text.IndexOf('[', idx); i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '{')
                {
                    if (depth == 1 && start < 0) start = i;
                    depth++;
                }
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 1 && start >= 0)
                    {
                        res.Add(JsonUtility.FromJson<LItem>(text.Substring(start, i - start + 1)));
                        start = -1;
                    }
                }
            }
            var l = new LLayout();
            l.items = res.ToArray();
            return l;
        }

        static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            var parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetPath));
        }

        static Material Mat(string key)
        {
            string path = MatDir + "/BO_" + key + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            EnsureFolder(MatDir);
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Color col;
            if (!Palette.TryGetValue(key, out col)) col = Color.magenta;
            m.SetColor("_BaseColor", col);
            m.SetFloat("_Smoothness", 0.1f);
            m.SetFloat("_Metallic", 0f);
            if (key == "glow")
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(2.5f, 1.1f, 0.2f));
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
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

        static string Build(Cmd c)
        {
            Stage = "parse";
            var layout = ReadLayout(c.json);
            if (layout == null || layout.items == null || layout.items.Length == 0) return "layout parse failed";
            var active = SceneManager.GetActiveScene();
            if (active.isDirty && active.path != c.scene) return "ABORT: active scene '" + active.name + "' has unsaved changes";
            EnsureFolder(Path.GetDirectoryName(c.scene).Replace('\\', '/'));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject(string.IsNullOrEmpty(c.name) ? "@@NAME@@_Blockout" : c.name);
            var cache = new Dictionary<string, Transform>();
            int made = 0;
            foreach (var it in layout.items)
            {
                Stage = "item " + it.name;
                GameObject go;
                Vector3 scale = it.size;
                switch (it.shape)
                {
                    case "box": go = GameObject.CreatePrimitive(PrimitiveType.Cube); break;
                    case "cylinder": go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); scale = new Vector3(it.size.x, it.size.y * 0.5f, it.size.z); break;
                    case "sphere": go = GameObject.CreatePrimitive(PrimitiveType.Sphere); break;
                    default: go = new GameObject(); scale = Vector3.one; break;
                }
                go.name = it.name;
                go.transform.SetParent(Group(root.transform, cache, it.parent), false);
                go.transform.localPosition = it.pos;
                go.transform.localEulerAngles = it.rot;
                go.transform.localScale = scale;
                var r = go.GetComponent<MeshRenderer>();
                if (r != null)
                {
                    r.sharedMaterial = Mat(it.mat);
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
                var col = go.GetComponent<Collider>();
                if (col != null)
                {
                    if (it.collider == 0) UnityEngine.Object.DestroyImmediate(col);
                    else if (it.shape == "cylinder")
                    {
                        UnityEngine.Object.DestroyImmediate(col);
                        go.AddComponent<MeshCollider>();
                    }
                }
                made++;
            }
            // neutral blockout lighting (no shadows, so the ceiling does not black out the room)
            var lgo = new GameObject("Blockout_Sun");
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Directional; l.intensity = 1.1f; l.shadows = LightShadows.None; l.color = new Color(1f, 0.96f, 0.9f);
            lgo.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
            var fill = new GameObject("Blockout_Fill");
            var fl = fill.AddComponent<Light>();
            fl.type = LightType.Directional; fl.intensity = 0.45f; fl.shadows = LightShadows.None; fl.color = new Color(0.8f, 0.85f, 1f);
            fill.transform.rotation = Quaternion.Euler(-30f, 140f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.42f, 0.46f);
            RenderSettings.fog = false;
            EditorSceneManager.SaveScene(scene, c.scene);
            return "built " + made + " items -> " + c.scene;
        }

        static string Capture(Cmd c)
        {
            Stage = "capture";
            var go = new GameObject("__CaptureCam");
            var hidden = new List<GameObject>();
            if (!string.IsNullOrEmpty(c.hide))
            {
                foreach (var n in c.hide.Split(','))
                {
                    var tok = n.Trim();
                    if (tok.EndsWith("*"))
                    {
                        var pre = tok.Substring(0, tok.Length - 1);
                        foreach (var tr in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                            if (tr != null && tr.gameObject.activeSelf && tr.name.StartsWith(pre)) { tr.gameObject.SetActive(false); hidden.Add(tr.gameObject); }
                    }
                    else
                    {
                        var h0 = GameObject.Find(tok);
                        if (h0 != null && h0.activeSelf) { h0.SetActive(false); hidden.Add(h0); }
                    }
                }
            }
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.transform.position = new Vector3(c.px, c.py, c.pz);
                cam.transform.LookAt(new Vector3(c.tx, c.ty, c.tz));
                if (Mathf.Abs(c.roll) > 0.001f) cam.transform.Rotate(0, 0, c.roll, Space.Self);
                cam.orthographic = c.ortho;
                if (c.ortho) cam.orthographicSize = c.orthoSize > 0 ? c.orthoSize : 5f;
                cam.fieldOfView = c.fov > 0 ? c.fov : 60f;
                cam.nearClipPlane = 0.05f; cam.farClipPlane = 200f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.05f, 0.05f, 0.06f);
                cam.allowHDR = true;
                var acd = cam.GetUniversalAdditionalCameraData();
                acd.renderPostProcessing = c.pp;
                int w = c.w > 0 ? c.w : 1672, h = c.h > 0 ? c.h : 941;
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                cam.targetTexture = rt;
                var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                for (int i = 0; i < 2; i++)   // the first render after a reload can miss static geometry
                {
                    if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                    else cam.Render();
                }
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                Directory.CreateDirectory(Path.GetDirectoryName(c.png));
                File.WriteAllBytes(c.png, tex.EncodeToPNG());
                cam.targetTexture = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(tex);
                return "captured " + c.png + " " + w + "x" + h;
            }
            finally
            {
                foreach (var hg in hidden) if (hg != null) hg.SetActive(true);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static string ImportFile(Cmd c)
        {
            // c.json = source file on disk, c.name = destination asset path (Assets/...)
            string dst = c.name.Replace("\\", "/");
            EnsureFolder(Path.GetDirectoryName(dst).Replace("\\", "/"));
            File.Copy(c.json, dst, true);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            return "imported " + dst;
        }

        static string ModelInfo(Cmd c)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(c.name);
            if (go == null) return "not found " + c.name;
            var sb = new StringBuilder();
            sb.AppendLine("root " + go.name + " rot=" + go.transform.eulerAngles + " scale=" + go.transform.localScale);
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var b = mf.sharedMesh.bounds;
                var wc = mf.transform.TransformPoint(b.center);
                sb.AppendLine(mf.name + " center=" + wc.ToString("F3") + " size=" + Vector3.Scale(b.size, mf.transform.lossyScale).ToString("F3") + " verts=" + mf.sharedMesh.vertexCount + " tris=" + (mf.sharedMesh.triangles.Length / 3) + " rot=" + mf.transform.eulerAngles);
            }
            return sb.ToString();
        }

        static string Dbg(Cmd c)
        {
            var text = File.ReadAllText(c.json);
            var sb = new StringBuilder();
            sb.AppendLine("len=" + text.Length);
            var l = JsonUtility.FromJson<LLayout>(text);
            sb.AppendLine("layout null? " + (l == null) + " items null? " + (l == null || l.items == null));
            var one = JsonUtility.FromJson<LItem>("{\"name\":\"a\",\"shape\":\"box\",\"pos\":{\"x\":1,\"y\":2,\"z\":3}}");
            sb.AppendLine("one: " + (one == null ? "null" : one.name + " pos=" + one.pos));
            var lay = ReadLayout(c.json);
            sb.AppendLine("ReadLayout items=" + lay.items.Length + " first=" + lay.items[0].name + " size=" + lay.items[0].size);
            return sb.ToString();
        }

        static string Info()
        {
            var sb = new StringBuilder();
            var s = SceneManager.GetActiveScene();
            sb.AppendLine("scene " + s.name + " path=" + s.path + " dirty=" + s.isDirty + " roots=" + s.rootCount);
            int n = 0, tris = 0;
            foreach (var mf in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)) { n++; if (mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3; }
            sb.AppendLine("meshfilters=" + n + " tris=" + tris);
            return sb.ToString();
        }
    }
}
