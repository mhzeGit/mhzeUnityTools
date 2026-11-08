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

// Collision geometry for the environment (data-driven: colliders.json) (compiled together with EnvOps*.cs by ucs.py; never added to the Unity project).
// Colliders live in the ASSET prefabs (@@PREFIX@@<Asset>.prefab) so that every instance of an asset carries its own collision.
namespace ClaudeTmp
{
    public static class EnvOps3
    {
        const string PrefabDir = "@@PREFAB_DIR@@";
        const string ColName = "Collider";

        // colliders.json (<env>/unity/colliders.json), passed as OpCmd.json:
        //   box      assets that get ONE BoxCollider from their local bounds                       ["Wall_Brick_Panel", ...]
        //   cylinder "Asset:MeshChild" -> convex cylinder collider fitted to that mesh child        ["Boiler_Large:Teal__Shell", ...]
        //   stairs   "Asset:MeshChild:treadDepth" -> solid wedge under the nose line of the steps   ["Stair_Brick_9:Brick__Bricks:0.2725"]
        [Serializable] public class ColSpec { public string[] box; public string[] cylinder; public string[] stairs; }

        const string ColMeshDir = "@@COLLISION_DIR@@";

        static Bounds PartBounds(GameObject root, MeshFilter mf)
        {
            var mb = mf.sharedMesh.bounds;
            var c = mb.center; var e = mb.extents;
            bool first = true;
            var b = new Bounds();
            for (int i = 0; i < 8; i++)
            {
                var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                var p = root.transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
            }
            return b;
        }

        static Mesh CylinderMesh()
        {
            string path = ColMeshDir + "/@@PREFIX@@Col_Cylinder24.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m != null) return m;
            if (!AssetDatabase.IsValidFolder(ColMeshDir))
                AssetDatabase.CreateFolder("@@ENV_ASSET_DIR@@", "Collision");
            const int n = 24;
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.PI * 2f * i / n;
                v.Add(new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f));
                v.Add(new Vector3(Mathf.Cos(a) * 0.5f, 1f, Mathf.Sin(a) * 0.5f));
            }
            v.Add(new Vector3(0, 0, 0)); v.Add(new Vector3(0, 1, 0));
            for (int i = 0; i < n; i++)
            {
                int i0 = i * 2, i1 = ((i + 1) % n) * 2;
                t.Add(i0); t.Add(i0 + 1); t.Add(i1 + 1);
                t.Add(i0); t.Add(i1 + 1); t.Add(i1);
                t.Add(2 * n); t.Add(i1); t.Add(i0);
                t.Add(2 * n + 1); t.Add(i0 + 1); t.Add(i1 + 1);
            }
            m = new Mesh { name = "@@PREFIX@@Col_Cylinder24" };
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // stair: solid wedge under the sloped line through the lowest tread top of every step (one mesh asset per stair prefab)
        static Mesh WedgeMesh(string name, float xmin, float xmax, Vector2 p0, Vector2 p1, float zBack, float yBack)
        {
            string path = ColMeshDir + "/" + name + ".asset";
            if (!AssetDatabase.IsValidFolder(ColMeshDir))
                AssetDatabase.CreateFolder("@@ENV_ASSET_DIR@@", "Collision");
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) AssetDatabase.DeleteAsset(path);
            var poly = new List<Vector2> { new Vector2(p0.x, 0f), new Vector2(p0.x, p0.y), new Vector2(p1.x, p1.y), new Vector2(zBack, yBack), new Vector2(zBack, 0f) };
            var v = new List<Vector3>(); var t = new List<int>();
            foreach (var q in poly) v.Add(new Vector3(xmin, q.y, q.x));
            foreach (var q in poly) v.Add(new Vector3(xmax, q.y, q.x));
            int k = poly.Count;
            for (int i = 1; i < k - 1; i++) { t.Add(0); t.Add(i + 1); t.Add(i); t.Add(k); t.Add(k + i); t.Add(k + i + 1); }
            for (int i = 0; i < k; i++) { int j = (i + 1) % k; t.Add(i); t.Add(j); t.Add(k + j); t.Add(i); t.Add(k + j); t.Add(k + i); }
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void ClearOld(GameObject root)
        {
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var ch = root.transform.GetChild(i);
                if (ch.name.StartsWith(ColName)) UnityEngine.Object.DestroyImmediate(ch.gameObject);
            }
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
        }

        static Bounds LocalBounds(GameObject root)
        {
            bool first = true;
            var b = new Bounds();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var c = mb.center; var e = mb.extents;
                for (int i = 0; i < 8; i++)
                {
                    var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                    var p = root.transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        public static string PrefabColliders(Scene unused, OpCmd c)
        {
            if (string.IsNullOrEmpty(c.json) || !File.Exists(c.json)) return "pass json=<path to colliders.json>";
            var spec = JsonUtility.FromJson<ColSpec>(File.ReadAllText(c.json));
            var sb = new StringBuilder();
            int boxes = 0, convex = 0, wedges = 0;
            foreach (var asset in spec.box ?? new string[0])
            {
                string path = PrefabDir + "/@@PREFIX@@" + asset + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ClearOld(root);
                    var b = LocalBounds(root);
                    var go = new GameObject(ColName);
                    go.transform.SetParent(root.transform, false);
                    var bc = go.AddComponent<BoxCollider>();
                    bc.center = b.center; bc.size = b.size;
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    boxes++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            var cyl = (spec.cylinder != null && spec.cylinder.Length > 0) ? CylinderMesh() : null;
            foreach (var entry in spec.cylinder ?? new string[0])
            {
                var kv = entry.Split(':');
                string path = PrefabDir + "/@@PREFIX@@" + kv[0] + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ClearOld(root);
                    MeshFilter mf = null;
                    foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                        if (f.name == kv[1] || (f.sharedMesh != null && f.sharedMesh.name == kv[1])) { mf = f; break; }
                    if (mf == null) { sb.AppendLine("no mesh " + kv[1] + " in " + kv[0]); continue; }
                    var pb = PartBounds(root, mf);
                    var go = new GameObject(ColName);
                    go.transform.SetParent(root.transform, false);
                    float d = Mathf.Max(pb.size.x, pb.size.z);
                    go.transform.localPosition = new Vector3(pb.center.x, pb.min.y, pb.center.z);
                    go.transform.localScale = new Vector3(d, pb.size.y, d);
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = cyl; mc.convex = true;
                    sb.AppendLine(kv[0] + ": cylinder d=" + d.ToString("F2") + " h=" + pb.size.y.ToString("F2"));
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    convex++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            foreach (var entry in spec.stairs ?? new string[0])
            {
                var kv = entry.Split(':');
                float tread = kv.Length > 2 ? float.Parse(kv[2], System.Globalization.CultureInfo.InvariantCulture) : 0.27f;
                string path = PrefabDir + "/@@PREFIX@@" + kv[0] + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ClearOld(root);
                    MeshFilter mf = null;
                    foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                        if (f.name == kv[1] || (f.sharedMesh != null && f.sharedMesh.name == kv[1])) { mf = f; break; }
                    if (mf == null) { sb.AppendLine("no mesh " + kv[1] + " in " + kv[0]); continue; }
                    var pb = PartBounds(root, mf);
                    var go = new GameObject(ColName);
                    go.transform.SetParent(root.transform, false);
                    // the nose line: the lowest tread top of every step (flat triangles facing up), one point per step
                    var treads = new List<Vector3>();
                    foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var m = f.sharedMesh; if (m == null) continue;
                        var v = m.vertices; var tr = m.triangles;
                        for (int i = 0; i < tr.Length; i += 3)
                        {
                            var a0 = root.transform.InverseTransformPoint(f.transform.TransformPoint(v[tr[i]]));
                            var b0 = root.transform.InverseTransformPoint(f.transform.TransformPoint(v[tr[i + 1]]));
                            var c0 = root.transform.InverseTransformPoint(f.transform.TransformPoint(v[tr[i + 2]]));
                            var nn = Vector3.Cross(b0 - a0, c0 - a0).normalized;
                            if (nn.y > 0.98f && Mathf.Abs(a0.y - b0.y) < 1e-4f && Mathf.Abs(a0.y - c0.y) < 1e-4f)
                                treads.Add(new Vector3(Mathf.Min(a0.z, b0.z, c0.z), a0.y, Mathf.Max(a0.z, b0.z, c0.z)));
                        }
                    }
                    var front = new SortedDictionary<int, Vector2>();
                    foreach (var tr in treads)
                    {
                        int k = Mathf.RoundToInt(tr.x / tread);
                        Vector2 cur;
                        if (!front.TryGetValue(k, out cur)) front[k] = new Vector2(tr.x, tr.y); else front[k] = new Vector2(Mathf.Min(cur.x, tr.x), Mathf.Min(cur.y, tr.y));
                    }
                    var line = new List<Vector2>();
                    foreach (var kvp in front) if (kvp.Value.y > 0.15f) line.Add(kvp.Value);
                    if (line.Count < 3) { sb.AppendLine("stair " + kv[0] + ": not enough treads"); continue; }
                    var q0 = line[0]; var q1 = line[line.Count - 1];
                    var wedge = WedgeMesh("@@PREFIX@@" + kv[0] + "_Collider", pb.min.x, pb.max.x, q0, q1, pb.max.z, q1.y + 0.1f);
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = wedge; mc.convex = true;
                    sb.AppendLine("stair wedge " + kv[0] + ": nose line (" + q0.x.ToString("F2") + "," + q0.y.ToString("F2") + ") -> (" + q1.x.ToString("F2") + "," + q1.y.ToString("F2") + "), back z=" + pb.max.z.ToString("F2"));
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    wedges++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            sb.AppendLine("box colliders: " + boxes + ", cylinder colliders: " + convex + ", stair wedges: " + wedges);
            return sb.ToString();
        }
    }
}
