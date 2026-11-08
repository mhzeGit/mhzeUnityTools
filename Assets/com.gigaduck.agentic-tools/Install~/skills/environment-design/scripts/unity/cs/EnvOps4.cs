using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
namespace ClaudeTmp
{
    public static class EnvOps4
    {

        public static string PlayerProbe(Scene mine, OpCmd c)
        {
            string housePath = "@@PLAYER_SOURCE_SCENE@@";
            var house = EditorSceneManager.OpenScene(housePath, OpenSceneMode.Additive);
            var sb = new StringBuilder();
            try
            {
                GameObject fpc = null;
                foreach (var r in house.GetRootGameObjects()) if (r.name == "FPC_Character") fpc = r;
                if (fpc == null) { foreach (var r in house.GetRootGameObjects()) sb.AppendLine("root: " + r.name); return sb.ToString() + "no FPC_Character"; }
                sb.AppendLine("prefab=" + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(fpc) + " pos=" + fpc.transform.position + " rot=" + fpc.transform.eulerAngles);
                System.Action<Transform, int> dump = null;
                dump = (t, d) =>
                {
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var cp in t.GetComponents<Component>()) names.Add(cp == null ? "MISSING" : cp.GetType().Name);
                    sb.AppendLine(new string(' ', d * 2) + t.name + " [" + string.Join(",", names.ToArray()) + "] lp=" + t.localPosition.ToString("F2") + " lr=" + t.localEulerAngles.ToString("F0") + " active=" + t.gameObject.activeSelf + " layer=" + t.gameObject.layer + " tag=" + t.gameObject.tag);
                    for (int i = 0; i < t.childCount; i++) dump(t.GetChild(i), d + 1);
                };
                dump(fpc.transform, 0);
                var cc = fpc.GetComponent<CharacterController>();
                if (cc != null) sb.AppendLine("CC height=" + cc.height + " radius=" + cc.radius + " step=" + cc.stepOffset + " slope=" + cc.slopeLimit + " skin=" + cc.skinWidth + " center=" + cc.center);
                foreach (var comp in fpc.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (comp == null) continue;
                    var so = new SerializedObject(comp);
                    var it = so.GetIterator();
                    sb.AppendLine("-- " + comp.GetType().FullName + " on " + comp.name);
                    bool enter = true;
                    while (it.NextVisible(enter))
                    {
                        enter = false;
                        if (it.name == "m_Script") continue;
                        string val = it.propertyType == SerializedPropertyType.ObjectReference ? (it.objectReferenceValue != null ? it.objectReferenceValue.name + " (" + it.objectReferenceValue.GetType().Name + ")" : "null") : it.propertyType.ToString() + (it.propertyType == SerializedPropertyType.Float ? "=" + it.floatValue : (it.propertyType == SerializedPropertyType.Integer ? "=" + it.intValue : (it.propertyType == SerializedPropertyType.Boolean ? "=" + it.boolValue : "")));
                        sb.AppendLine("     " + it.name + ": " + val);
                    }
                }
                var cam = fpc.GetComponentInChildren<Camera>();
                if (cam != null) sb.AppendLine("Camera fov=" + cam.fieldOfView + " near=" + cam.nearClipPlane + " far=" + cam.farClipPlane + " tag=" + cam.tag + " hdr=" + cam.allowHDR);
            }
            finally
            {
                EditorSceneManager.CloseScene(house, true);
            }
            return sb.ToString();
        }


        // copies the house's FPC_Character (not a prefab there) into the environment scene and puts it at the spawn point
        public static string Player(Scene mine, OpCmd c)
        {
            GameObject existing = null;
            foreach (var r in mine.GetRootGameObjects()) if (r.name == "FPC_Character") existing = r;
            var sb = new StringBuilder();
            if (existing == null)
            {
                string housePath = "@@PLAYER_SOURCE_SCENE@@";
                var house = EditorSceneManager.OpenScene(housePath, OpenSceneMode.Additive);
                try
                {
                    GameObject fpc = null;
                    foreach (var r in house.GetRootGameObjects()) if (r.name == "FPC_Character") fpc = r;
                    if (fpc == null) return "no FPC_Character in the house scene";
                    SceneManager.SetActiveScene(mine);
                    existing = UnityEngine.Object.Instantiate(fpc);
                    existing.name = "FPC_Character";
                    SceneManager.MoveGameObjectToScene(existing, mine);
                    sb.AppendLine("player copied");
                }
                finally { EditorSceneManager.CloseScene(house, true); SceneManager.SetActiveScene(mine); }
            }
            existing.transform.position = new Vector3(c.f0, c.f1, c.f2);
            existing.transform.rotation = Quaternion.Euler(0f, c.i0, 0f);
            var cc = existing.GetComponent<CharacterController>();
            sb.AppendLine("player at " + existing.transform.position + " yaw " + c.i0 + " cc=" + (cc != null));
            EditorSceneManager.MarkSceneDirty(mine);
            return sb.ToString();
        }

        // moves the player's CharacterController along scripted legs (edit-mode physics) and reports where it ends up
        // c.text: legs "name:dx,dz;name:dx,dz;..." relative moves in metres from the current position, using 4 cm steps; c.f0,f1,f2 = start position
        public static string WalkTest(Scene mine, OpCmd c)
        {
            return EnvOps.Remote(mine, far => WalkTestBody(mine, c, far));
        }

        static string WalkTestBody(Scene mine, OpCmd c, Vector3 far)
        {
            GameObject pl = null;
            foreach (var r in mine.GetRootGameObjects()) if (r.name == "FPC_Character") pl = r;
            if (pl == null) return "no player";
            var cc = pl.GetComponent<CharacterController>();
            var sb = new StringBuilder();
            pl.transform.position = new Vector3(c.f0, c.f1, c.f2) + far;
            Physics.SyncTransforms();
            // the controller must be enabled and settled on the floor first
            cc.enabled = false; cc.enabled = true;
            for (int i = 0; i < 20; i++) cc.Move(new Vector3(0, -0.05f, 0));
            sb.AppendLine("start " + (pl.transform.position - far).ToString("F2") + " grounded=" + cc.isGrounded);
            foreach (var leg in c.text.Split(';'))
            {
                if (leg.Trim().Length == 0) continue;
                var nameAndVec = leg.Split(':');
                var v = nameAndVec[1].Split(',');
                float dx = float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture), dz = float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture);
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                int steps = Mathf.CeilToInt(dist / 0.04f);
                var step = new Vector3(dx / steps, 0f, dz / steps);
                float maxY = pl.transform.position.y;
                for (int i = 0; i < steps; i++)
                {
                    cc.Move(step + new Vector3(0, -0.08f, 0));
                    maxY = Mathf.Max(maxY, pl.transform.position.y);
                }
                sb.AppendLine(nameAndVec[0] + ": end " + (pl.transform.position - far).ToString("F2") + " maxY=" + maxY.ToString("F2") + " grounded=" + cc.isGrounded);
            }
            return sb.ToString();
        }


        // c.f0,f1,f2 = origin, c.text = "dx,dy,dz;..." directions; sphere casts (radius 0.3) at the origin height; reports the first collider hit
        public static string Cast(Scene mine, OpCmd c)
        {
            return EnvOps.Remote(mine, far => CastBody(c, far));
        }

        static string CastBody(OpCmd c, Vector3 far)
        {
            var sb = new StringBuilder();
            Physics.SyncTransforms();
            foreach (var d in c.text.Split(';'))
            {
                if (d.Trim().Length == 0) continue;
                var v = d.Split(',');
                var dir = new Vector3(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture)).normalized;
                RaycastHit hit;
                var o = new Vector3(c.f0, c.f1, c.f2) + far;
                if (Physics.SphereCast(o, 0.3f, dir, out hit, 30f))
                {
                    var t = hit.collider.transform;
                    string path = t.name; var p = t.parent;
                    while (p != null) { path = p.name + "/" + path; p = p.parent; }
                    sb.AppendLine("dir " + dir.ToString("F1") + " hit " + path + " dist=" + hit.distance.ToString("F2") + " point=" + (hit.point - far).ToString("F2") + " collider=" + hit.collider.GetType().Name + " bounds=" + hit.collider.bounds);
                }
                else sb.AppendLine("dir " + dir.ToString("F1") + " nothing");
            }
            return sb.ToString();
        }


        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = FindDeep(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        // c.text: "Name:x,y,z[,rx,ry,rz];Name2:..." sets world position (and optional euler rotation); "Name:+dx,dy,dz" shifts instead when prefixed with '+'
        public static string Nudge(Scene mine, OpCmd c)
        {
            var sb = new StringBuilder();
            foreach (var item in c.text.Split(';'))
            {
                if (item.Trim().Length == 0) continue;
                int k = item.IndexOf(':');
                string name = item.Substring(0, k).Trim();
                string val = item.Substring(k + 1).Trim();
                bool rel = val.StartsWith("+");
                if (rel) val = val.Substring(1);
                var v = val.Split(',');
                Transform t = null;
                foreach (var r in mine.GetRootGameObjects()) { t = FindDeep(r.transform, name); if (t != null) break; }
                if (t == null) { sb.AppendLine("not found: " + name); continue; }
                var P = new Vector3(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture));
                if (rel) t.position += P; else t.position = P;
                if (v.Length >= 6) t.rotation = Quaternion.Euler(float.Parse(v[3], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[4], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[5], System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine(name + " -> " + t.position.ToString("F2"));
            }
            EditorSceneManager.MarkSceneDirty(mine);
            return sb.ToString();
        }


        // c.text: "Name:range" or "Name:range,intensity" for the first Light found under the named object
        public static string LightOverride(Scene mine, OpCmd c)
        {
            var sb = new StringBuilder();
            foreach (var item in c.text.Split(';'))
            {
                if (item.Trim().Length == 0) continue;
                int k = item.IndexOf(':');
                string name = item.Substring(0, k).Trim();
                var v = item.Substring(k + 1).Split(',');
                Transform t = null;
                foreach (var r in mine.GetRootGameObjects()) { t = FindDeep(r.transform, name); if (t != null) break; }
                if (t == null) { sb.AppendLine("not found: " + name); continue; }
                var l = t.GetComponentInChildren<Light>(true);
                if (l == null) { sb.AppendLine("no light under " + name); continue; }
                l.range = float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture);
                if (v.Length > 1) l.intensity = float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture);
                EditorUtility.SetDirty(l);
                sb.AppendLine(name + ": range=" + l.range + " intensity=" + l.intensity);
            }
            EditorSceneManager.MarkSceneDirty(mine);
            return sb.ToString();
        }

        public static string MatInfo(Scene mine, OpCmd c)
        {
            var sb = new StringBuilder();
            foreach (var n in c.text.Split(','))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>("@@MATERIAL_DIR@@/" + n + ".mat");
                if (m == null) { sb.AppendLine(n + ": missing"); continue; }
                sb.AppendLine(n + ": shader=" + m.shader.name + " base=" + m.GetColor("_BaseColor") + " emission=" + m.GetColor("_EmissionColor") + " kw=" + string.Join(" ", m.shaderKeywords) + " emMap=" + (m.GetTexture("_EmissionMap") != null) + " baseMap=" + (m.GetTexture("_BaseMap") != null) + " smooth=" + m.GetFloat("_Smoothness"));
            }
            var root = PrefabUtility.LoadPrefabContents("@@PREFAB_DIR@@/@@PREFIX@@" + (string.IsNullOrEmpty(c.json) ? "Boiler_Large" : c.json) + ".prefab");
            try
            {
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                    sb.AppendLine("  renderer " + r.name + " -> " + (r.sharedMaterial != null ? r.sharedMaterial.name : "null"));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return sb.ToString();
        }
    }
}
