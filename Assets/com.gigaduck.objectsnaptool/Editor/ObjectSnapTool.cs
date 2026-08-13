using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Gigaduck.ObjectSnapTool
{
    /// <summary>
    /// Unity Editor tool for snapping selected objects to surfaces.
    ///
    /// Keyboard shortcuts (SceneView must have focus):
    ///   End                  — Snap to ground (downward)
    ///   Ctrl  + Arrow Keys   — Snap along X/Z axes
    ///   Shift + Arrow Keys   — Snap along Y axis
    ///   Alt   + Arrow Keys   — Snap diagonally
    ///
    /// Mouse shortcut:
    ///   Ctrl + Shift + Left Click — Snap selected object(s) to the surface under the cursor.
    ///   Configure layers and detection method via Tools > Object Snap Tool > Settings.
    /// </summary>
    public static class ObjectSnapTool
    {
        private const float MaxSnapDistance = 100f;
        private const int   AllLayers       = -1;

        // Cached reflection handle for HandleUtility.IntersectRayMesh (internal Unity API).
        private static MethodInfo s_IntersectRayMesh;

        // ── Initialisation ───────────────────────────────────────────────────

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            SceneView.duringSceneGui -= OnSceneViewGUI;
            SceneView.duringSceneGui += OnSceneViewGUI;
        }

        // ── SceneView handler ────────────────────────────────────────────────

        private static void OnSceneViewGUI(SceneView sceneView)
        {
            Event e = Event.current;

            // Ctrl + Shift + Left Click → snap to surface under cursor
            if (e.type == EventType.MouseDown && e.button == 0 && e.control && e.shift)
            {
                HandleMouseClickSnap(e);
                e.Use();
                return;
            }

            // Keyboard shortcuts
            if (e.type == EventType.KeyDown && HandleKeyInput(e))
            {
                e.Use();
                SceneView.RepaintAll();
            }
        }

        // ── Mouse click snap ─────────────────────────────────────────────────

        private static void HandleMouseClickSnap(Event e)
        {
            if (Selection.gameObjects == null || Selection.gameObjects.Length == 0)
            {
                Debug.LogWarning("ObjectSnapTool: No objects selected.");
                return;
            }

            var settings  = ObjectSnapToolSettings.GetOrCreate();
            var ray       = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            var excluded  = new HashSet<GameObject>(Selection.gameObjects);

            bool       hit;
            RaycastHit hitInfo;

            if (settings.detectionMethod == ObjectSnapToolSettings.DetectionMethod.Collider)
                hit = RaycastCollider(ray, settings.snapLayers, excluded, out hitInfo);
            else
                hit = RaycastMesh(ray, settings.snapLayers, excluded, out hitInfo);

            if (!hit)
            {
                Debug.LogWarning("ObjectSnapTool: No surface found under cursor.");
                return;
            }

            SnapSelectedObjectsToPoint(hitInfo.point, hitInfo.normal);
        }

        /// <summary>Physics (collider-based) raycast that skips excluded objects.</summary>
        private static bool RaycastCollider(Ray ray, LayerMask layerMask,
            HashSet<GameObject> excluded, out RaycastHit closestHit)
        {
            RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, layerMask);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (RaycastHit hit in hits)
            {
                if (!excluded.Contains(hit.collider.gameObject))
                {
                    closestHit = hit;
                    return true;
                }
            }

            closestHit = default;
            return false;
        }

        /// <summary>
        /// Mesh-based raycast using HandleUtility.IntersectRayMesh (internal Unity API via reflection).
        /// Falls back to collider raycast if the method cannot be found.
        /// </summary>
        private static bool RaycastMesh(Ray ray, LayerMask layerMask,
            HashSet<GameObject> excluded, out RaycastHit closestHit)
        {
            if (s_IntersectRayMesh == null)
            {
                s_IntersectRayMesh = typeof(HandleUtility).GetMethod(
                    "IntersectRayMesh",
                    BindingFlags.Static | BindingFlags.NonPublic);
            }

            if (s_IntersectRayMesh == null)
            {
                Debug.LogWarning("ObjectSnapTool: IntersectRayMesh not available; falling back to collider raycast.");
                return RaycastCollider(ray, layerMask, excluded, out closestHit);
            }

            closestHit = default;
            float closestDistance = float.MaxValue;
            bool  anyHit          = false;

#pragma warning disable CS0618 // FindObjectsOfType is deprecated in Unity 2023+, kept for 2020.3 compatibility
            MeshFilter[] meshFilters = Object.FindObjectsOfType<MeshFilter>();
#pragma warning restore CS0618

            var invokeArgs = new object[4];

            foreach (MeshFilter mf in meshFilters)
            {
                if (mf == null || mf.sharedMesh == null)          continue;
                if (excluded.Contains(mf.gameObject))             continue;
                if ((layerMask & (1 << mf.gameObject.layer)) == 0) continue;

                invokeArgs[0] = ray;
                invokeArgs[1] = mf.sharedMesh;
                invokeArgs[2] = mf.transform.localToWorldMatrix;
                invokeArgs[3] = default(RaycastHit);

                bool result = (bool)s_IntersectRayMesh.Invoke(null, invokeArgs);
                if (!result) continue;

                var   hit  = (RaycastHit)invokeArgs[3];
                float dist = Vector3.Distance(ray.origin, hit.point);
                if (dist < closestDistance)
                {
                    closestDistance = dist;
                    closestHit      = hit;
                    anyHit          = true;
                }
            }

            return anyHit;
        }

        /// <summary>Moves selected objects so they rest on the given surface point.</summary>
        private static void SnapSelectedObjectsToPoint(Vector3 hitPoint, Vector3 hitNormal)
        {
            GameObject[] selectedObjects = Selection.gameObjects;
            Undo.RecordObjects(selectedObjects.Select(go => go.transform).ToArray(), "Snap to Surface");

            int snappedCount = 0;
            foreach (GameObject obj in selectedObjects)
            {
                if (obj == null) continue;

                Renderer renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Bounds bounds        = renderer.bounds;
                    Vector3 pivotOffset  = obj.transform.position - bounds.center;

                    // Half-extent of the AABB projected onto the surface normal.
                    Vector3 extents     = bounds.extents;
                    float normalExtent  = extents.x * Mathf.Abs(hitNormal.x)
                                       + extents.y * Mathf.Abs(hitNormal.y)
                                       + extents.z * Mathf.Abs(hitNormal.z);

                    obj.transform.position = hitPoint + hitNormal * normalExtent + pivotOffset;
                }
                else
                {
                    obj.transform.position = hitPoint;
                }

                snappedCount++;
            }

            if (snappedCount > 0)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                Debug.Log($"ObjectSnapTool: Snapped {snappedCount} object(s) to surface at {hitPoint}.");
            }
        }

        // ── Keyboard snap ────────────────────────────────────────────────────

        private static bool HandleKeyInput(Event e)
        {
            Vector3 snapDirection = GetSnapDirection(e);
            if (snapDirection != Vector3.zero)
            {
                SnapSelectedObjects(snapDirection);
                return true;
            }
            return false;
        }

        private static Vector3 GetSnapDirection(Event e)
        {
            bool ctrl  = e.control;
            bool shift = e.shift;
            bool alt   = e.alt;

            switch (e.keyCode)
            {
                case KeyCode.End:       return Vector3.down;

                case KeyCode.UpArrow:
                    if (shift) return Vector3.up;
                    if (ctrl)  return Vector3.forward;
                    if (alt)   return (Vector3.forward + Vector3.up).normalized;
                    break;

                case KeyCode.DownArrow:
                    if (shift) return Vector3.down;
                    if (ctrl)  return Vector3.back;
                    if (alt)   return (Vector3.back + Vector3.down).normalized;
                    break;

                case KeyCode.LeftArrow:
                    if (ctrl) return Vector3.left;
                    if (alt)  return (Vector3.left + Vector3.down).normalized;
                    break;

                case KeyCode.RightArrow:
                    if (ctrl) return Vector3.right;
                    if (alt)  return (Vector3.right + Vector3.down).normalized;
                    break;
            }
            return Vector3.zero;
        }

        private static void SnapSelectedObjects(Vector3 direction)
        {
            GameObject[] selectedObjects = Selection.gameObjects;
            if (selectedObjects == null || selectedObjects.Length == 0)
            {
                Debug.LogWarning("ObjectSnapTool: No objects selected for snapping.");
                return;
            }

            Undo.RecordObjects(selectedObjects.Select(go => go.transform).ToArray(), "Snap Objects");

            int snappedCount = 0;
            foreach (GameObject obj in selectedObjects)
            {
                if (SnapObject(obj, direction))
                    snappedCount++;
            }

            if (snappedCount > 0)
            {
                Debug.Log($"ObjectSnapTool: Snapped {snappedCount} objects {direction}.");
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }
        }

        private static bool SnapObject(GameObject obj, Vector3 direction)
        {
            if (obj == null) return false;

            Vector3 startPosition = GetStartPosition(obj);
            Ray ray = new Ray(startPosition, direction);

            if (Physics.Raycast(ray, out RaycastHit hit, MaxSnapDistance, AllLayers))
            {
                Vector3 newPosition = hit.point;

                Renderer renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Bounds bounds  = renderer.bounds;
                    Vector3 offset = obj.transform.position - bounds.center;

                    if (direction == Vector3.down)
                        newPosition.y += bounds.extents.y;

                    newPosition += offset;
                }

                obj.transform.position = newPosition;
                return true;
            }

            Debug.LogWarning($"ObjectSnapTool: No surface found for {obj.name}");
            return false;
        }

        private static Vector3 GetStartPosition(GameObject obj)
        {
            Renderer renderer = obj.GetComponent<Renderer>();
            return renderer != null ? renderer.bounds.center : obj.transform.position;
        }

        // ── Menu items ───────────────────────────────────────────────────────

        [MenuItem("Tools/Object Snap Tool/Snap to Ground", priority = 100)]
        public static void SnapToGround()
        {
            SnapSelectedObjects(Vector3.down);
        }

        [MenuItem("Tools/Object Snap Tool/Snap to Ground", true)]
        public static bool ValidateSnapToGround()
        {
            return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
        }
    }
}