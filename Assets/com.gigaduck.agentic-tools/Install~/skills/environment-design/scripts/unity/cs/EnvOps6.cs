using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
namespace ClaudeTmp
{
    public static class EnvOps6
    {

        // renders the hero with the scene's PostProcess volume off, saving nothing; prints mean colour per variant
        public static string GradeProbe(Scene mine, OpCmd c)
        {
            var sb = new StringBuilder();
            GameObject pp = null;
            foreach (var r in mine.GetRootGameObjects()) if (r.name == "PostProcess") pp = r;
            bool was = pp.activeSelf;
            bool wasDirty = mine.isDirty;
            string[] variants = { "volume-off" };
            pp.SetActive(false);
            var cam = EditorUtility.CreateGameObjectWithHideFlags("__GradeCam", HideFlags.HideAndDontSave, typeof(Camera));
            var camera = cam.GetComponent<Camera>();
            cam.transform.position = new Vector3(0, 1.65f, -3.0f); cam.transform.LookAt(new Vector3(0, 1.5f, 3.0f));
            camera.fieldOfView = 80; camera.allowHDR = true;
            var acd = camera.GetUniversalAdditionalCameraData(); acd.renderPostProcessing = true;
            var rt = new RenderTexture(418, 235, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            camera.targetTexture = rt;
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            for (int i = 0; i < 2; i++) { if (RenderPipeline.SupportsRenderRequest(camera, req)) RenderPipeline.SubmitRenderRequest(camera, req); else camera.Render(); }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(418, 235, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 418, 235), 0, 0); tex.Apply(); RenderTexture.active = prev;
            var px = tex.GetPixels32(); double r0 = 0, g0 = 0, b0 = 0; foreach (var p in px) { r0 += p.r; g0 += p.g; b0 += p.b; }
            sb.AppendLine("PostProcess volume OFF: mean rgb " + (r0 / px.Length).ToString("F1") + " " + (g0 / px.Length).ToString("F1") + " " + (b0 / px.Length).ToString("F1"));
            pp.SetActive(was);
            UnityEngine.Object.DestroyImmediate(tex); rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(cam);
            if (!wasDirty) EditorSceneManager.SaveScene(mine);
            sb.AppendLine("restored active=" + pp.activeSelf + " dirty=" + mine.isDirty);
            return sb.ToString();
        }


        static string Render(Camera camera, string label)
        {
            var rt = new RenderTexture(418, 235, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            camera.targetTexture = rt;
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            for (int i = 0; i < 2; i++) { if (RenderPipeline.SupportsRenderRequest(camera, req)) RenderPipeline.SubmitRenderRequest(camera, req); else camera.Render(); }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(418, 235, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 418, 235), 0, 0); tex.Apply(); RenderTexture.active = prev;
            var px = tex.GetPixels32(); double r0 = 0, g0 = 0, b0 = 0; foreach (var p in px) { r0 += p.r; g0 += p.g; b0 += p.b; }
            camera.targetTexture = null; rt.Release();
            UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(rt);
            var ca = VolumeManager.instance.stack.GetComponent<ColorAdjustments>();
            var st = VolumeManager.instance.stack.GetComponent<SplitToning>();
            return label + ": mean rgb " + (r0 / px.Length).ToString("F1") + " " + (g0 / px.Length).ToString("F1") + " " + (b0 / px.Length).ToString("F1") + " | stack sat " + ca.saturation.value + " exp " + ca.postExposure.value + " splitBalance " + st.balance.value + " shadows " + st.shadows.value;
        }

        public static string GradeProbe2(Scene mine, OpCmd c)
        {
            var sb = new StringBuilder();
            bool wasDirty = mine.isDirty;
            GameObject pp = null;
            foreach (var r in mine.GetRootGameObjects()) if (r.name == "PostProcess") pp = r;
            var vol = pp.GetComponent<Volume>();
            var orig = vol.sharedProfile;
            var mineProf = AssetDatabase.LoadAssetAtPath<VolumeProfile>("@@ROOM_PROFILE@@");
            var go = new GameObject("__GradeCam"); SceneManager.MoveGameObjectToScene(go, mine);
            var camera = go.AddComponent<Camera>();
            go.transform.position = new Vector3(0, 1.65f, -3.0f); go.transform.LookAt(new Vector3(0, 1.5f, 3.0f));
            camera.fieldOfView = 80; camera.allowHDR = true;
            var acd = camera.GetUniversalAdditionalCameraData(); acd.renderPostProcessing = true;
            sb.AppendLine("camera volumeLayerMask=" + acd.volumeLayerMask.value + " trigger=" + (acd.volumeTrigger != null ? acd.volumeTrigger.name : "null") + " requiresUpdate=" + acd.requiresVolumeFrameworkUpdate);
            sb.AppendLine(Render(camera, "house profile (scene as is)"));
            vol.sharedProfile = mineProf;
            sb.AppendLine(Render(camera, "mine in volume"));
            pp.SetActive(false);
            sb.AppendLine(Render(camera, "volume object off"));
            pp.SetActive(true);
            vol.sharedProfile = orig;
            UnityEngine.Object.DestroyImmediate(go);
            if (!wasDirty) EditorSceneManager.SaveScene(mine);
            sb.AppendLine("restored: profile=" + vol.sharedProfile.name + " dirty=" + mine.isDirty);
            return sb.ToString();
        }


        static string StackDump()
        {
            var st = VolumeManager.instance.stack;
            var tm = st.GetComponent<Tonemapping>(); var ca = st.GetComponent<ColorAdjustments>(); var sp = st.GetComponent<SplitToning>();
            var bl = st.GetComponent<Bloom>(); var vg = st.GetComponent<Vignette>(); var wb = st.GetComponent<WhiteBalance>();
            return "tonemap " + tm.mode.value + " | exp " + ca.postExposure.value + " con " + ca.contrast.value + " sat " + ca.saturation.value + " hue " + ca.hueShift.value + " filter " + ca.colorFilter.value
                + " | split bal " + sp.balance.value + " sh " + sp.shadows.value + " hi " + sp.highlights.value + " | bloom thr " + bl.threshold.value + " int " + bl.intensity.value + " active " + bl.active
                + " | vig " + vg.intensity.value + " | wb " + wb.temperature.value + "/" + wb.tint.value;
        }

        public static string GradeProbe3(Scene mine, OpCmd c)
        {
            var sb = new StringBuilder();
            bool wasDirty = mine.isDirty;
            var all = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var v in all) sb.AppendLine("volume " + v.gameObject.scene.name + "/" + v.name + " global=" + v.isGlobal + " prio=" + v.priority + " w=" + v.weight + " enabled=" + v.enabled + " activeInH=" + v.gameObject.activeInHierarchy + " profile=" + (v.sharedProfile != null ? v.sharedProfile.name : "null"));
            try
            {
                var dp = VolumeManager.instance.globalDefaultProfile;
                sb.AppendLine("global default profile: " + (dp != null ? dp.name + " comps=" + dp.components.Count : "null"));
                if (dp != null) foreach (var comp in dp.components) sb.AppendLine("   default comp " + comp.GetType().Name + " active=" + comp.active);
            }
            catch (System.Exception ex) { sb.AppendLine("global settings: " + ex.Message); }
            GameObject pp = null;
            foreach (var r in mine.GetRootGameObjects()) if (r.name == "PostProcess") pp = r;
            var vol = pp.GetComponent<Volume>();
            var orig = vol.sharedProfile;
            var profs = new[] { "@@BASE_PROFILE@@", "@@ROOM_PROFILE@@" };
            var go = new GameObject("__GradeCam"); SceneManager.MoveGameObjectToScene(go, mine);
            var camera = go.AddComponent<Camera>();
            go.transform.position = new Vector3(0, 1.65f, -3.0f); go.transform.LookAt(new Vector3(0, 1.5f, 3.0f));
            camera.fieldOfView = 80; camera.allowHDR = true;
            var acd = camera.GetUniversalAdditionalCameraData(); acd.renderPostProcessing = true;
            try
            {
                foreach (var pa in profs)
                {
                    vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(pa);
                    VolumeManager.instance.Update(go.transform, acd.volumeLayerMask);
                    sb.AppendLine(vol.sharedProfile.name + " -> stack " + StackDump());
                    sb.AppendLine("   " + Render2(camera, acd));
                }
                pp.SetActive(false);
                VolumeManager.instance.Update(go.transform, acd.volumeLayerMask);
                sb.AppendLine("volume off -> stack " + StackDump());
                sb.AppendLine("   " + Render2(camera, acd));
                pp.SetActive(true);
            }
            finally
            {
                vol.sharedProfile = orig; pp.SetActive(true);
                UnityEngine.Object.DestroyImmediate(go);
                if (!wasDirty && mine.isDirty) EditorSceneManager.SaveScene(mine);
            }
            sb.AppendLine("restored: profile=" + vol.sharedProfile.name + " dirty=" + mine.isDirty);
            return sb.ToString();
        }

        static string Render2(Camera camera, UniversalAdditionalCameraData acd)
        {
            var rt = new RenderTexture(418, 235, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            camera.targetTexture = rt;
            VolumeManager.instance.Update(camera.transform, acd.volumeLayerMask);
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            for (int i = 0; i < 2; i++) { if (RenderPipeline.SupportsRenderRequest(camera, req)) RenderPipeline.SubmitRenderRequest(camera, req); else camera.Render(); }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(418, 235, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 418, 235), 0, 0); tex.Apply(); RenderTexture.active = prev;
            var px = tex.GetPixels32(); double r0 = 0, g0 = 0, b0 = 0; foreach (var p in px) { r0 += p.r; g0 += p.g; b0 += p.b; }
            camera.targetTexture = null; rt.Release();
            UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(rt);
            return "mean rgb " + (r0 / px.Length).ToString("F1") + " " + (g0 / px.Length).ToString("F1") + " " + (b0 / px.Length).ToString("F1");
        }


        // ---- grade tuning -------------------------------------------------------------------------------------------------------------
        static System.Collections.Generic.Dictionary<string, string> GParse(string t)
        {
            var d = new System.Collections.Generic.Dictionary<string, string>();
            if (string.IsNullOrEmpty(t)) return d;
            foreach (var kv in t.Split(';')) { var i = kv.IndexOf('='); if (i > 0) d[kv.Substring(0, i).Trim()] = kv.Substring(i + 1).Trim(); }
            return d;
        }
        static float GF(string s) { return float.Parse(s, System.Globalization.CultureInfo.InvariantCulture); }
        static Color GC(string s) { var p = s.Split(','); return new Color(GF(p[0]), GF(p[1]), GF(p[2]), p.Length > 3 ? GF(p[3]) : 1f); }
        static string GG(System.Collections.Generic.Dictionary<string, string> d, string k) { string v; return d.TryGetValue(k, out v) ? v : null; }
        static T GComp<T>(VolumeProfile p) where T : VolumeComponent { T t; if (!p.TryGet(out t)) t = p.Add<T>(true); t.active = true; return t; }

        // overrides only the keys present: bloomI bloomT bloomS bloomTint vig vigS exposure contrast sat hue filter temp tint shadowTone highlightTone balance tonemap
        static void ApplyGrade(VolumeProfile profile, string text)
        {
            var p = GParse(text); string v;
            var bloom = GComp<Bloom>(profile); var vig = GComp<Vignette>(profile); var ca = GComp<ColorAdjustments>(profile);
            var wb = GComp<WhiteBalance>(profile); var st = GComp<SplitToning>(profile); var tm = GComp<Tonemapping>(profile);
            if ((v = GG(p, "bloomI")) != null) bloom.intensity.Override(GF(v));
            if ((v = GG(p, "bloomT")) != null) bloom.threshold.Override(GF(v));
            if ((v = GG(p, "bloomS")) != null) bloom.scatter.Override(GF(v));
            if ((v = GG(p, "bloomTint")) != null) bloom.tint.Override(GC(v));
            if ((v = GG(p, "vig")) != null) vig.intensity.Override(GF(v));
            if ((v = GG(p, "vigS")) != null) vig.smoothness.Override(GF(v));
            if ((v = GG(p, "exposure")) != null) ca.postExposure.Override(GF(v));
            if ((v = GG(p, "contrast")) != null) ca.contrast.Override(GF(v));
            if ((v = GG(p, "sat")) != null) ca.saturation.Override(GF(v));
            if ((v = GG(p, "hue")) != null) ca.hueShift.Override(GF(v));
            if ((v = GG(p, "filter")) != null) ca.colorFilter.Override(GC(v));
            if ((v = GG(p, "temp")) != null) wb.temperature.Override(GF(v));
            if ((v = GG(p, "tint")) != null) wb.tint.Override(GF(v));
            if ((v = GG(p, "shadowTone")) != null) st.shadows.Override(GC(v));
            if ((v = GG(p, "highlightTone")) != null) st.highlights.Override(GC(v));
            if ((v = GG(p, "balance")) != null) st.balance.Override(GF(v));
            if ((v = GG(p, "tonemap")) != null) { int m = int.Parse(v); tm.mode.Override(m == 0 ? TonemappingMode.None : (m == 1 ? TonemappingMode.Neutral : TonemappingMode.ACES)); }
        }

        static VolumeProfile CloneProfile(VolumeProfile src)
        {
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            foreach (var comp in src.components) p.components.Add(UnityEngine.Object.Instantiate(comp));
            return p;
        }

        // c.list: one candidate per line "name|key=value;key=value" applied on a copy of the @@NAME@@ profile asset; c.text: "px|py|pz|tx|ty|tz|fov|w|h"; c.json: output folder
        // renders every candidate through the scene's capture path (post processing on) and writes <folder>/<name>.png. Saves nothing.
        public static string GradeSweep(Scene mine, OpCmd c)
        {
            var baseProf = AssetDatabase.LoadAssetAtPath<VolumeProfile>("@@ROOM_PROFILE@@");
            var sb = new StringBuilder();
            foreach (var ln in File.ReadAllLines(c.list))
            {
                if (string.IsNullOrWhiteSpace(ln)) continue;
                var bar = ln.IndexOf('|');
                string name = ln.Substring(0, bar), prm = ln.Substring(bar + 1);
                var clone = CloneProfile(baseProf);
                ApplyGrade(clone, prm);
                string png = (c.json + "/" + name + ".png").Replace("\\", "/");
                string list = Path.GetTempFileName();
                File.WriteAllText(list, png + "|" + c.text + "|1|FPC_Character");
                var tmp = new OpCmd { list = list };
                EnvOps.GradeOverride = clone;
                try { sb.AppendLine(name + ": " + EnvOps.Shots(mine, tmp).Trim()); }
                finally
                {
                    EnvOps.GradeOverride = null;
                    foreach (var comp in clone.components) if (comp != null) UnityEngine.Object.DestroyImmediate(comp);
                    UnityEngine.Object.DestroyImmediate(clone);
                    File.Delete(list);
                }
            }
            return sb.ToString();
        }

        // writes the given keys into the @@NAME@@ profile ASSET (the scene is not touched); c.text = "key=value;..."
        public static string GradeAsset(Scene mine, OpCmd c)
        {
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>("@@ROOM_PROFILE@@");
            ApplyGrade(prof, c.text);
            EditorUtility.SetDirty(prof);
            foreach (var comp in prof.components) if (comp != null) EditorUtility.SetDirty(comp);
            AssetDatabase.SaveAssets();
            return "profile saved: " + StackDumpOf(prof);
        }

        static string StackDumpOf(VolumeProfile p)
        {
            Tonemapping tm; ColorAdjustments ca; SplitToning sp; Bloom bl; Vignette vg;
            p.TryGet(out tm); p.TryGet(out ca); p.TryGet(out sp); p.TryGet(out bl); p.TryGet(out vg);
            return "tonemap " + tm.mode.value + " exp " + ca.postExposure.value + " con " + ca.contrast.value + " sat " + ca.saturation.value + " hue " + ca.hueShift.value
                + " split " + sp.shadows.value + "/" + sp.highlights.value + "/" + sp.balance.value + " bloom " + bl.threshold.value + "/" + bl.intensity.value + "/" + bl.scatter.value + " vig " + vg.intensity.value + "/" + vg.smoothness.value;
        }

        // points the scene's PostProcess volume at the @@NAME@@ profile and saves the scene (used once the grade is final)
        public static string UseRoomProfile(Scene mine, OpCmd c)
        {
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>("@@ROOM_PROFILE@@");
            GameObject pp = null;
            foreach (var r in mine.GetRootGameObjects()) if (r.name == "PostProcess") pp = r;
            var vol = pp.GetComponent<Volume>();
            bool wasDirty = mine.isDirty;
            vol.sharedProfile = prof;
            EditorSceneManager.MarkSceneDirty(mine);
            if (!wasDirty) EditorSceneManager.SaveScene(mine);
            return "scene PostProcess profile = " + vol.sharedProfile.name + " dirty=" + mine.isDirty;
        }

        public static string VolumeTest(Scene mine, OpCmd c)
        {
            var sb = new StringBuilder();
            var all = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            sb.AppendLine("volumes before: " + all.Length);
            foreach (var v in all) sb.AppendLine("  " + v.name + " scene=" + v.gameObject.scene.name + " prio=" + v.priority + " layer=" + v.gameObject.layer + " profile=" + (v.sharedProfile != null ? v.sharedProfile.name : "null") + " enabled=" + v.enabled);
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>("@@ROOM_PROFILE@@");
            var go = EditorUtility.CreateGameObjectWithHideFlags("__ForcedGrade", HideFlags.HideAndDontSave);
            var fv = go.AddComponent<Volume>();
            fv.isGlobal = true; fv.priority = 1000f; fv.weight = 1f; fv.sharedProfile = prof;
            var all2 = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            sb.AppendLine("volumes after: " + all2.Length + " (forced found: " + System.Array.Exists(all2, x => x == fv) + ")");
            var tr = new GameObject("__tmp").transform; tr.hideFlags = HideFlags.HideAndDontSave;
            VolumeManager.instance.Update(tr, 1);
            var ca = VolumeManager.instance.stack.GetComponent<ColorAdjustments>();
            sb.AppendLine("stack colour adjustments saturation=" + ca.saturation.value + " hueShift=" + ca.hueShift.value + " filter=" + ca.colorFilter.value);
            var st = VolumeManager.instance.stack.GetComponent<SplitToning>();
            sb.AppendLine("stack split toning balance=" + st.balance.value + " shadows=" + st.shadows.value);
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(tr.gameObject);
            return sb.ToString();
        }
    }
}
