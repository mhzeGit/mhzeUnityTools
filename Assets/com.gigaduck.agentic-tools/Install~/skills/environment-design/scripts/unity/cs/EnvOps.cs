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

// Scratch tooling for the environment scene (compiled in memory by ucs.py; never added to the Unity project).
// The editor is SHARED with other agents: every operation loads the environment scene ADDITIVELY next to whatever scene is open,
// makes it active only while the operation runs, then restores the previously active scene and closes the environment scene again.
namespace ClaudeTmp
{
    [Serializable] public class OpCmd { public string op; public string scene; public string list; public string text; public string json; public float f0; public float f1; public float f2; public int i0; public bool b0; }

    public static class EnvOps
    {
        public const string ScenePath = "@@SCENE@@";
        static readonly Vector3 Far = new Vector3(5000f, 0f, 0f);       // captures happen 5 km away from whatever the other scene has at the origin
        static string Stage = "";

        public static string Run(string arg)
        {
            var c = JsonUtility.FromJson<OpCmd>(arg);
            if (EditorApplication.isPlaying && c.op != "status") return "ABORT: the editor is in Play Mode (someone else may be using it)";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return "ABORT: the editor is compiling/importing";
            string path = string.IsNullOrEmpty(c.scene) ? ScenePath : c.scene;
            if (c.op == "status") return Status();
            var prevActive = SceneManager.GetActiveScene();
            Scene mine = default(Scene);
            bool openedByMe = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.path == path && s.isLoaded) { mine = s; break; }
            }
            try
            {
                if (!mine.IsValid() || !mine.isLoaded) { Stage = "open"; mine = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); openedByMe = true; }
                SceneManager.SetActiveScene(mine);
                switch (c.op)
                {
                    case "shots": return Shots(mine, c);
                    case "tree": return Tree(mine, c);
                    case "info": return Info(mine);
                    default:
                        var m = typeof(EnvOps2).GetMethod(c.op, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (m == null) m = typeof(EnvOps3).GetMethod(c.op, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (m == null) m = typeof(EnvOps4).GetMethod(c.op, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (m == null) m = typeof(EnvOps5).GetMethod(c.op, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (m == null) m = typeof(EnvOps6).GetMethod(c.op, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (m == null) return "unknown op " + c.op;
                        return (string)m.Invoke(null, new object[] { mine, c });
                }
            }
            catch (System.Reflection.TargetInvocationException tie) { return "EXCEPTION at stage [" + Stage + "]: " + tie.InnerException; }
            catch (Exception ex) { return "EXCEPTION at stage [" + Stage + "]: " + ex; }
            finally
            {
                try
                {
                    if (prevActive.IsValid() && prevActive.isLoaded && prevActive != mine) SceneManager.SetActiveScene(prevActive);
                    if (openedByMe && mine.IsValid())
                    {
                        if (mine.isDirty) EditorSceneManager.SaveScene(mine);
                        EditorSceneManager.CloseScene(mine, true);
                    }
                }
                catch (Exception ex2) { Debug.LogWarning("EnvOps restore: " + ex2.Message); }
            }
        }

        static string Status()
        {
            var sb = new StringBuilder();
            sb.AppendLine("playing=" + EditorApplication.isPlaying + " compiling=" + EditorApplication.isCompiling + " updating=" + EditorApplication.isUpdating);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                sb.AppendLine(" scene[" + i + "] " + s.name + " loaded=" + s.isLoaded + " dirty=" + s.isDirty + " active=" + (s == SceneManager.GetActiveScene()));
            }
            return sb.ToString();
        }

        static string Info(Scene mine)
        {
            var sb = new StringBuilder();
            foreach (var r in mine.GetRootGameObjects()) sb.AppendLine(r.name + " children=" + r.transform.childCount);
            sb.AppendLine("ambientMode=" + RenderSettings.ambientMode + " ambient=" + RenderSettings.ambientLight + " fog=" + RenderSettings.fog);
            return sb.ToString();
        }

        static string Tree(Scene mine, OpCmd c)
        {
            var sb = new StringBuilder();
            int maxDepth = c.i0 > 0 ? c.i0 : 2;
            System.Action<Transform, int> dump = null;
            dump = (t, d) =>
            {
                var names = new List<string>();
                foreach (var cp in t.GetComponents<Component>()) names.Add(cp == null ? "MISSING" : cp.GetType().Name);
                sb.AppendLine(new string(' ', d * 2) + t.name + " [" + string.Join(",", names.ToArray()) + "] pos=" + t.position.ToString("F2") + " children=" + t.childCount);
                if (d < maxDepth) for (int i = 0; i < t.childCount; i++) dump(t.GetChild(i), d + 1);
            };
            foreach (var r in mine.GetRootGameObjects()) dump(r.transform, 0);
            return sb.ToString();
        }

        // c.list file: one shot per line  png|px|py|pz|tx|ty|tz|fov|w|h|pp(0/1)|hide(comma list, optional)
        public static VolumeProfile GradeOverride;

        public static string Shots(Scene mine, OpCmd c)
        {
            var lines = File.ReadAllLines(c.list);
            var roots = mine.GetRootGameObjects();
            var oldPos = new Vector3[roots.Length];
            bool others = false;
            for (int si = 0; si < SceneManager.sceneCount; si++) if (SceneManager.GetSceneAt(si) != mine && SceneManager.GetSceneAt(si).isLoaded) others = true;
            // other scenes' directional lights would light this room too: switch them off for the duration of the capture
            var offLights = new List<Light>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s == mine || !s.isLoaded) continue;
                foreach (var r in s.GetRootGameObjects())
                    foreach (var l in r.GetComponentsInChildren<Light>(false))
                        if (l.enabled && l.type == LightType.Directional) { l.enabled = false; offLights.Add(l); }
            }
            var sb = new StringBuilder();
            bool wasDirty = mine.isDirty;
            // optional "profile=<asset path>" in OpCmd.text: the scene's PostProcess volume gets that profile for the capture only and is put back afterwards
            // (a hidden or throw-away-scene override volume is not picked up by the capture camera). The scene is saved again only to clear the dirty flag
            // this causes, and only when it was clean before, so the file content is unchanged.
            Volume gradeVol = null; VolumeProfile gradeOrig = null;
            if (GradeOverride != null || (!string.IsNullOrEmpty(c.text) && c.text.StartsWith("profile=")))
            {
                var prof = GradeOverride != null ? GradeOverride : AssetDatabase.LoadAssetAtPath<VolumeProfile>(c.text.Substring(8));
                foreach (var r in mine.GetRootGameObjects()) if (r.name == "PostProcess") gradeVol = r.GetComponent<Volume>();
                if (prof != null && gradeVol != null) { gradeOrig = gradeVol.sharedProfile; gradeVol.sharedProfile = prof; }
                else gradeVol = null;
            }
            try
            {
                if (others) for (int i = 0; i < roots.Length; i++) { oldPos[i] = roots[i].transform.position; roots[i].transform.position = oldPos[i] + Far; }
                foreach (var ln in lines)
                {
                    if (string.IsNullOrWhiteSpace(ln)) continue;
                    var p = ln.Split('|');
                    string png = p[0];
                    var pos = new Vector3(F(p[1]), F(p[2]), F(p[3])) + (others ? Far : Vector3.zero);
                    var tgt = new Vector3(F(p[4]), F(p[5]), F(p[6])) + (others ? Far : Vector3.zero);
                    float fov = F(p[7]);
                    int w = int.Parse(p[8]), h = int.Parse(p[9]);
                    bool pp = p[10] == "1";
                    string hide = p.Length > 11 ? p[11] : "";
                    sb.AppendLine(Capture(mine, png, pos, tgt, fov, w, h, pp, hide) + (gradeVol != null ? " [grade " + gradeVol.sharedProfile.name + "]" : ""));
                }
            }
            finally
            {
                if (others) for (int i = 0; i < roots.Length; i++) if (roots[i] != null) roots[i].transform.position = oldPos[i];
                foreach (var l in offLights) if (l != null) l.enabled = true;
                if (gradeVol != null) gradeVol.sharedProfile = gradeOrig;
                if (!wasDirty && mine.isDirty) EditorSceneManager.SaveScene(mine);
            }
            return sb.ToString();
        }

        static float F(string s) { return float.Parse(s, System.Globalization.CultureInfo.InvariantCulture); }

        // runs body with every root object of the scene shifted 5 km away (so other scenes' colliders/lights at the origin do not interfere), then shifts it back
        public static string Remote(Scene mine, System.Func<Vector3, string> body)
        {
            var roots = mine.GetRootGameObjects();
            var oldPos = new Vector3[roots.Length];
            bool others = false;
            for (int si = 0; si < SceneManager.sceneCount; si++) if (SceneManager.GetSceneAt(si) != mine && SceneManager.GetSceneAt(si).isLoaded) others = true;
            Vector3 shift = others ? Far : Vector3.zero;
            try
            {
                if (others) for (int i = 0; i < roots.Length; i++) { oldPos[i] = roots[i].transform.position; roots[i].transform.position = oldPos[i] + Far; }
                Physics.SyncTransforms();
                return body(shift);
            }
            finally
            {
                if (others) for (int i = 0; i < roots.Length; i++) if (roots[i] != null) roots[i].transform.position = oldPos[i];
                Physics.SyncTransforms();
            }
        }

        static string Capture(Scene mine, string png, Vector3 pos, Vector3 tgt, float fov, int w, int h, bool pp, string hide)
        {
            Stage = "capture " + png;
            // The capture camera must live in the scene: a hidden scene-less camera reuses the volume stack of the last editor repaint (the grade then
            // looks wrong). The scene is saved again afterwards only to clear the dirty flag this causes, and only when it was clean before, so the
            // file content is unchanged.
            var go = new GameObject("__CaptureCam");
            SceneManager.MoveGameObjectToScene(go, mine);
            var hidden = new List<GameObject>();
            if (!string.IsNullOrEmpty(hide))
            {
                foreach (var n in hide.Split(','))
                {
                    var tok = n.Trim();
                    if (tok.Length == 0) continue;
                    bool prefix = tok.EndsWith("*");
                    string pre = prefix ? tok.Substring(0, tok.Length - 1) : tok;
                    foreach (var tr in mine.GetRootGameObjects())
                        foreach (var t in tr.GetComponentsInChildren<Transform>(false))
                            if (t != null && t.gameObject.activeSelf && (prefix ? t.name.StartsWith(pre) : t.name == pre)) { t.gameObject.SetActive(false); hidden.Add(t.gameObject); }
                }
            }
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.transform.position = pos;
                cam.transform.LookAt(tgt);
                cam.fieldOfView = fov > 0 ? fov : 60f;
                cam.nearClipPlane = 0.05f; cam.farClipPlane = 200f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.02f, 0.02f, 0.025f);
                cam.allowHDR = true;
                var acd = cam.GetUniversalAdditionalCameraData();
                acd.renderPostProcessing = pp;
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                cam.targetTexture = rt;
                // render requests do not refresh the volume stack: do it here, otherwise the grade is whatever the last editor repaint left behind
                VolumeManager.instance.Update(cam.transform, acd.volumeLayerMask);
                var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                for (int i = 0; i < 2; i++)
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
                Directory.CreateDirectory(Path.GetDirectoryName(png));
                File.WriteAllBytes(png, tex.EncodeToPNG());
                cam.targetTexture = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(tex);
                return "captured " + png + " " + w + "x" + h;
            }
            finally
            {
                foreach (var hg in hidden) if (hg != null) hg.SetActive(true);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }


}
