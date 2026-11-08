using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
namespace ClaudeTmp
{
    public static class EnvOps5
    {

        const string OutlineAsset = "@@OUTLINE_ASSET@@";
        const string OutlineBase = "@@OUTLINE_BASE@@";
        const string RendererPath = "@@RENDERER_ASSET@@";

        static float Fv(string s) { return float.Parse(s, System.Globalization.CultureInfo.InvariantCulture); }

        // c.text: color=r,g,b;depth=thr,thick;normal=thr,thick   -> @@PREFIX@@Outline.asset (copy of the house outline settings) + a "@@NAME@@" scene entry on the PC renderer's outline feature
        public static string Outline(Scene mine, OpCmd c)
        {
            var sb = new StringBuilder();
            var kv = new Dictionary<string, string>();
            foreach (var part in (c.text ?? "").Split(';')) { int k = part.IndexOf('='); if (k > 0) kv[part.Substring(0, k).Trim()] = part.Substring(k + 1).Trim(); }
            if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(OutlineAsset) == null)
            {
                if (!AssetDatabase.CopyAsset(OutlineBase, OutlineAsset)) return "could not copy " + OutlineBase;
                AssetDatabase.Refresh();
            }
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(OutlineAsset);
            var so = new SerializedObject(asset);
            string v;
            if (kv.TryGetValue("color", out v)) { var f = v.Split(','); so.FindProperty("outlineColor").colorValue = new Color(Fv(f[0]), Fv(f[1]), Fv(f[2]), 1f); }
            if (kv.TryGetValue("depth", out v)) { var f = v.Split(','); so.FindProperty("thresholdDepth").floatValue = Fv(f[0]); so.FindProperty("thicknessDepth").floatValue = Fv(f[1]); so.FindProperty("minThicknessDepth").floatValue = Fv(f[1]); so.FindProperty("maxThicknessDepth").floatValue = Fv(f[1]); }
            if (kv.TryGetValue("normal", out v)) { var f = v.Split(','); so.FindProperty("thresholdNormal").floatValue = Fv(f[0]); so.FindProperty("thicknessNormal").floatValue = Fv(f[1]); so.FindProperty("minThicknessNormal").floatValue = Fv(f[1]); so.FindProperty("maxThicknessNormal").floatValue = Fv(f[1]); }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            // wire into the renderer feature (by active scene name)
            var all = AssetDatabase.LoadAllAssetsAtPath(RendererPath);
            ScriptableRendererFeature feature = null;
            foreach (var o in all) { var rf = o as ScriptableRendererFeature; if (rf != null && rf.GetType().Name == "ScreenSpaceOutlineFeature") feature = rf; }
            if (feature == null) return sb.ToString() + "outline feature not found";
            var fso = new SerializedObject(feature);
            var list = fso.FindProperty("m_SceneSettings");
            int found = -1;
            for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).FindPropertyRelative("sceneName").stringValue == mine.name) found = i;
            if (found < 0) { list.InsertArrayElementAtIndex(list.arraySize); found = list.arraySize - 1; }
            var el = list.GetArrayElementAtIndex(found);
            el.FindPropertyRelative("sceneName").stringValue = mine.name;
            el.FindPropertyRelative("settings").objectReferenceValue = asset;
            fso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(feature);
            var rendererData = AssetDatabase.LoadMainAssetAtPath(RendererPath);
            if (rendererData != null) { EditorUtility.SetDirty(rendererData); AssetDatabase.SaveAssetIfDirty(rendererData); }
            AssetDatabase.SaveAssets();
            sb.AppendLine("outline asset " + OutlineAsset + "; renderer entries now:");
            for (int i = 0; i < list.arraySize; i++) sb.AppendLine("   " + list.GetArrayElementAtIndex(i).FindPropertyRelative("sceneName").stringValue + " -> " + (list.GetArrayElementAtIndex(i).FindPropertyRelative("settings").objectReferenceValue != null ? list.GetArrayElementAtIndex(i).FindPropertyRelative("settings").objectReferenceValue.name : "null"));
            return sb.ToString();
        }


        // c.text: optional comma list of asset names (default: every @@PREFIX@@ prefab). For every mesh: triangles, "large flat" triangles (area >= 0.01 m2)
        // and how many of those have a vertex normal more than 5 degrees off the triangle's own normal (a hard-edged flat face that is shaded smooth).
        public static string NormalAudit(Scene mine, OpCmd c)
        {
            var sb = new StringBuilder();
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "@@PREFAB_DIR@@" });
            var only = string.IsNullOrEmpty(c.text) ? null : new HashSet<string>(c.text.Split(','));
            int totalLarge = 0, totalBad = 0;
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                string asset = System.IO.Path.GetFileNameWithoutExtension(path).Substring(3);
                if (only != null && !only.Contains(asset)) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int tris = 0, large = 0, bad = 0;
                    foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var m = mf.sharedMesh; if (m == null) continue;
                        var v = m.vertices; var n = m.normals; var t = m.triangles;
                        for (int i = 0; i < t.Length; i += 3)
                        {
                            tris++;
                            var p0 = v[t[i]]; var p1 = v[t[i + 1]]; var p2 = v[t[i + 2]];
                            var cr = Vector3.Cross(p1 - p0, p2 - p0);
                            float area = cr.magnitude * 0.5f;
                            if (area < 0.01f) continue;
                            large++;
                            var fn = cr.normalized;
                            float dev = 0f;
                            for (int k = 0; k < 3; k++) dev = Mathf.Max(dev, Vector3.Angle(fn, n.Length > 0 ? n[t[i + k]] : fn));
                            if (dev > 5f) bad++;
                        }
                    }
                    totalLarge += large; totalBad += bad;
                    sb.AppendLine(asset.PadRight(22) + " tris " + tris.ToString().PadLeft(6) + "  large " + large.ToString().PadLeft(5) + "  smoothed-looking " + bad.ToString().PadLeft(5));
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            sb.AppendLine("TOTAL large " + totalLarge + " with normals >5 deg off: " + totalBad);
            return sb.ToString();
        }


        // c.text = asset name: lists (up to 25) large triangles whose vertex normals deviate from the face normal: mesh, centre, face normal, max deviation
        public static string NormalDetail(Scene mine, OpCmd c)
        {
            var sb = new StringBuilder();
            var root = PrefabUtility.LoadPrefabContents("@@PREFAB_DIR@@/@@PREFIX@@" + c.text + ".prefab");
            try
            {
                int shown = 0;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = mf.sharedMesh; if (m == null) continue;
                    var v = m.vertices; var n = m.normals; var t = m.triangles;
                    for (int i = 0; i < t.Length && shown < 25; i += 3)
                    {
                        var p0 = v[t[i]]; var p1 = v[t[i + 1]]; var p2 = v[t[i + 2]];
                        var cr = Vector3.Cross(p1 - p0, p2 - p0);
                        float area = cr.magnitude * 0.5f;
                        if (area < 0.01f) continue;
                        var fn = cr.normalized;
                        float dev = 0f;
                        for (int k = 0; k < 3; k++) dev = Mathf.Max(dev, Vector3.Angle(fn, n[t[i + k]]));
                        if (dev <= 5f) continue;
                        shown++;
                        var cen = (p0 + p1 + p2) / 3f;
                        sb.AppendLine(mf.name + " centre " + cen.ToString("F2") + " faceN " + fn.ToString("F2") + " area " + area.ToString("F3") + " dev " + dev.ToString("F1") + " n0 " + n[t[i]].ToString("F2"));
                    }
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return sb.ToString();
        }

        public static string Audit(Scene mine, OpCmd c)
        {
            var sb = new StringBuilder();
            int lights = 0, renderers = 0, colliders = 0, tris = 0, missing = 0;
            foreach (var r in mine.GetRootGameObjects())
            {
                foreach (var l in r.GetComponentsInChildren<Light>(true)) { lights++; sb.AppendLine("light " + l.name + " type=" + l.type + " int=" + l.intensity + " range=" + l.range + " shadows=" + l.shadows + " pos=" + l.transform.position.ToString("F2")); }
                foreach (var mr in r.GetComponentsInChildren<MeshRenderer>(true))
                {
                    renderers++;
                    var mf = mr.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                    if (mr.sharedMaterial == null) missing++;
                    else if (mr.sharedMaterial.name.StartsWith("Clay_")) sb.AppendLine("CLAY material still on " + mr.name);
                }
                colliders += r.GetComponentsInChildren<Collider>(true).Length;
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                    foreach (var comp in t.GetComponents<Component>()) if (comp == null) { missing++; sb.AppendLine("missing script on " + t.name); }
            }
            sb.AppendLine("lights=" + lights + " renderers=" + renderers + " colliders=" + colliders + " scene triangles=" + tris + " missing=" + missing);
            sb.AppendLine("ambient=" + RenderSettings.ambientMode + " sky=" + RenderSettings.ambientSkyColor + " fog=" + RenderSettings.fog + " fogDensity=" + RenderSettings.fogDensity);
            var vol = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None);
            foreach (var v in vol) if (v.gameObject.scene == mine) sb.AppendLine("volume " + v.name + " global=" + v.isGlobal + " profile=" + (v.sharedProfile != null ? v.sharedProfile.name : "null"));
            return sb.ToString();
        }
    }
}
