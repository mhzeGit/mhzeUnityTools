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

// Scene operations for the environment scene (compiled together with EnvOps.cs by ucs.py; never added to the Unity project).
namespace ClaudeTmp
{
    public static class EnvOps2
    {
        const string PrefabDir = "@@PREFAB_DIR@@";
        const string HouseProfile = "@@BASE_PROFILE@@";
        const string RoomProfile = "@@ROOM_PROFILE@@";

        static Dictionary<string, string> Parse(string s)
        {
            var d = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(s)) return d;
            foreach (var kv in s.Split(';'))
            {
                int k = kv.IndexOf('=');
                if (k <= 0) continue;
                d[kv.Substring(0, k).Trim()] = kv.Substring(k + 1).Trim();
            }
            return d;
        }
        static float Fl(string s) { return float.Parse(s, System.Globalization.CultureInfo.InvariantCulture); }
        static float[] Vec(string s) { var p = s.Split(','); var r = new float[p.Length]; for (int i = 0; i < p.Length; i++) r[i] = Fl(p[i]); return r; }
        static Color Col(string s) { var v = Vec(s); return new Color(v[0], v[1], v[2], v.Length > 3 ? v[3] : 1f); }
        static string Get(Dictionary<string, string> d, string k, string def) { string v; return d.TryGetValue(k, out v) ? v : def; }

        public static string DumpVolume(Scene mine, OpCmd c)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(string.IsNullOrEmpty(c.json) ? HouseProfile : c.json);
            if (profile == null) return "no profile";
            var sb = new StringBuilder();
            foreach (var comp in profile.components)
            {
                sb.AppendLine(comp.GetType().Name + " active=" + comp.active);
                foreach (var f in comp.GetType().GetFields())
                {
                    var prm = f.GetValue(comp) as VolumeParameter;
                    if (prm == null) continue;
                    var valProp = prm.GetType().GetProperty("value");
                    sb.AppendLine("   " + f.Name + " override=" + prm.overrideState + " value=" + (valProp != null ? valProp.GetValue(prm, null) : null));
                }
            }
            return sb.ToString();
        }

        static void EnsureLight(string prefabName, string childName, Vector3 localPos, Color color, float intensity, float range, LightShadows shadows, float shadowStrength, LightType type = LightType.Point, Vector3 euler = default(Vector3), float spotAngle = 90f, float innerAngle = 40f)
        {
            string path = PrefabDir + "/" + prefabName + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var t = root.transform.Find(childName);
                GameObject go;
                if (t == null) { go = new GameObject(childName); go.transform.SetParent(root.transform, false); }
                else go = t.gameObject;
                go.transform.localPosition = localPos;
                var l = go.GetComponent<Light>();
                if (l == null) l = go.AddComponent<Light>();
                l.type = type;
                go.transform.localRotation = Quaternion.Euler(euler);
                if (type == LightType.Spot) { l.spotAngle = spotAngle; l.innerSpotAngle = innerAngle; }
                l.color = color; l.intensity = intensity; l.range = range;
                l.shadows = shadows; l.shadowStrength = shadowStrength;
                l.shadowBias = 0.02f; l.shadowNormalBias = 0.3f;
                l.lightmapBakeType = LightmapBakeType.Realtime;
                l.renderMode = LightRenderMode.ForcePixel;
                l.GetUniversalAdditionalLightData();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // c.text: key=value;...
        public static string Lighting(Scene mine, OpCmd c)
        {
            var p = Parse(c.text);
            foreach (var r in mine.GetRootGameObjects())
                if (r.name == "Preview_Sun" || r.name == "Preview_Fill") UnityEngine.Object.DestroyImmediate(r);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Col(Get(p, "sky", "0.10,0.065,0.05"));
            RenderSettings.ambientEquatorColor = Col(Get(p, "equator", "0.07,0.045,0.035"));
            RenderSettings.ambientGroundColor = Col(Get(p, "ground", "0.03,0.02,0.015"));
            RenderSettings.fog = Get(p, "fog", "1") == "1";
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Col(Get(p, "fogColor", "0.05,0.03,0.02"));
            RenderSettings.fogDensity = Fl(Get(p, "fogDensity", "0.012"));
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0f;
            Color lantern = Col(Get(p, "lanternColor", "1,0.55,0.2"));
            Color furnace = Col(Get(p, "furnaceColor", "1,0.42,0.1"));
            // ADAPT: lanternPrefab/lanternPos = the prefab that carries the wall-lantern light and where the glass centre is in its local frame; furnacePrefab/furnacePos = the hero light source's prefab (a hearth, a window, a lamp...)
            var lp = Vec(Get(p, "lanternPos", "0,-0.20,-0.30"));
            EnsureLight("@@PREFIX@@" + Get(p, "lanternPrefab", "Lantern_Wall"), "LanternLight", new Vector3(lp[0], lp[1], lp[2]), lantern, Fl(Get(p, "lantern", "5")), Fl(Get(p, "lanternRange", "8")),
                Get(p, "lanternShadows", "0") == "1" ? LightShadows.Soft : LightShadows.None, 0.85f);
            // furnace window: 0.6 m in front of the door, window height
            // furnace glow spilling out of the window onto the hearth bricks and the floor: a spot pointing down and forward (-Z), so the door and its frame are only grazed
            var fp = Vec(Get(p, "furnacePos", "0,0.75,-1.45"));
            EnsureLight("@@PREFIX@@" + Get(p, "furnacePrefab", "Boiler_Large"), "FurnaceLight", new Vector3(fp[0], fp[1], fp[2]), furnace, Fl(Get(p, "furnace", "12")), Fl(Get(p, "furnaceRange", "7")),
                Get(p, "furnaceShadows", "1") == "1" ? LightShadows.Soft : LightShadows.None, 0.9f, LightType.Spot, new Vector3(Fl(Get(p, "furnacePitch", "50")), 180f, 0f), Fl(Get(p, "furnaceAngle", "130")), Fl(Get(p, "furnaceInner", "60")));
            // soft warm bounce from the south (no shadows): fakes the light the lit floor and walls throw back onto the hero props
            GameObject lroot = null;
            foreach (var r in mine.GetRootGameObjects()) if (r.name == "Lighting") lroot = r;
            if (lroot == null) { lroot = new GameObject("Lighting"); SceneManager.MoveGameObjectToScene(lroot, mine); }
            var ft = lroot.transform.Find("Fill_Bounce");
            GameObject fgo;
            if (ft == null) { fgo = new GameObject("Fill_Bounce"); fgo.transform.SetParent(lroot.transform, false); } else fgo = ft.gameObject;
            var fl = fgo.GetComponent<Light>(); if (fl == null) fl = fgo.AddComponent<Light>();
            var fe = Vec(Get(p, "fillEuler", "35,15,0"));
            fgo.transform.rotation = Quaternion.Euler(fe[0], fe[1], fe[2]);
            fl.type = LightType.Directional; fl.shadows = LightShadows.None;
            fl.color = Col(Get(p, "fillColor", "1,0.78,0.58")); fl.intensity = Fl(Get(p, "fill", "0.3"));
            fl.lightmapBakeType = LightmapBakeType.Realtime;
            fl.GetUniversalAdditionalLightData();
            EditorSceneManager.MarkSceneDirty(mine);
            AssetDatabase.SaveAssets();
            return "lighting set";
        }

        // c.text keys: bloomI, bloomT, bloomS, bloomTint, vig, vigS, exposure, contrast, sat, filter, temp, tint, shadowTone, highlightTone, balance, tonemap (0 none, 1 neutral, 2 aces)
        public static string Volume(Scene mine, OpCmd c)
        {
            var p = Parse(c.text);
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(RoomProfile) == null)
            {
                if (!AssetDatabase.CopyAsset(HouseProfile, RoomProfile)) return "could not copy the house profile";
                AssetDatabase.Refresh();
            }
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(RoomProfile);
            Bloom bloom; Vignette vig; ColorAdjustments ca; WhiteBalance wb; SplitToning st; Tonemapping tm; MotionBlur mb; FilmGrain fg;
            if (profile.TryGet(out bloom)) { bloom.active = true; bloom.intensity.Override(Fl(Get(p, "bloomI", "0.9"))); bloom.threshold.Override(Fl(Get(p, "bloomT", "0.9"))); bloom.scatter.Override(Fl(Get(p, "bloomS", "0.7"))); bloom.tint.Override(Col(Get(p, "bloomTint", "1,0.85,0.7"))); bloom.highQualityFiltering.Override(true); }
            if (profile.TryGet(out vig)) { vig.active = true; vig.intensity.Override(Fl(Get(p, "vig", "0.32"))); vig.smoothness.Override(Fl(Get(p, "vigS", "0.45"))); vig.color.Override(Color.black); }
            if (profile.TryGet(out ca)) { ca.active = true; ca.postExposure.Override(Fl(Get(p, "exposure", "0"))); ca.contrast.Override(Fl(Get(p, "contrast", "15"))); ca.saturation.Override(Fl(Get(p, "sat", "10"))); ca.colorFilter.Override(Col(Get(p, "filter", "1,1,1"))); }
            if (profile.TryGet(out wb)) { wb.active = true; wb.temperature.Override(Fl(Get(p, "temp", "8"))); wb.tint.Override(Fl(Get(p, "tint", "4"))); }
            if (profile.TryGet(out st)) { st.active = true; st.shadows.Override(Col(Get(p, "shadowTone", "0.12,0.2,0.25"))); st.highlights.Override(Col(Get(p, "highlightTone", "1,0.62,0.3"))); st.balance.Override(Fl(Get(p, "balance", "20"))); }
            if (profile.TryGet(out tm)) { tm.active = true; int m = int.Parse(Get(p, "tonemap", "2")); tm.mode.Override(m == 0 ? TonemappingMode.None : (m == 1 ? TonemappingMode.Neutral : TonemappingMode.ACES)); }
            if (profile.TryGet(out mb)) mb.active = false;
            if (profile.TryGet(out fg)) fg.active = false;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            GameObject go = null;
            foreach (var r in mine.GetRootGameObjects()) if (r.name == "PostProcess") go = r;
            if (go == null) { go = new GameObject("PostProcess"); SceneManager.MoveGameObjectToScene(go, mine); }
            var vol = go.GetComponent<Volume>();
            if (vol == null) vol = go.AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 10; vol.weight = 1f;
            vol.sharedProfile = profile;
            EditorSceneManager.MarkSceneDirty(mine);
            return "volume ready: " + RoomProfile;
        }
    }
}
