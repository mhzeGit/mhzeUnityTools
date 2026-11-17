using MHZE.FirstPersonController;
using UnityEditor;
using UnityEngine;

namespace Gigaduck.PlayFromView.Integrations
{
    /// <summary>
    /// Places the MHZE First Person Controller. It keeps its own yaw and pitch and reads the pitch
    /// back from the camera pivot when it is teleported, so the pivot is tilted first and the
    /// controller's own teleport then adopts both angles without a snap on the first mouse move.
    /// </summary>
    public sealed class MhzeFirstPersonPlacer : IPlayFromViewPlacer
    {
        public string Name => "MHZE First Person Controller";

        public int Priority => 100;

        public bool TryPlace(in ViewPose pose, out PlacementReport report)
        {
            report = default;

            FirstPersonController controller = Object.FindAnyObjectByType<FirstPersonController>();
            if (controller == null || !controller.isActiveAndEnabled)
            {
                return false;
            }

            return Place(controller, pose, out report);
        }

        /// <summary>Places one specific controller. Returns false if it has no camera yet.</summary>
        public static bool Place(FirstPersonController controller, in ViewPose pose, out PlacementReport report)
        {
            report = default;

            Camera camera = controller.PlayerCamera;
            if (camera == null)
            {
                return false;
            }

            controller.StopForceMove();
            controller.StopForceLook(true);

            Transform pivot = ResolvePivot(controller, camera);
            pivot.localRotation = Quaternion.Euler(ClampPitch(controller, pose.Pitch), 0f, 0f);

            // Rotation first: the controller's teleport syncs its look state from the pivot.
            controller.SetPositionAndRotation(controller.transform.position, Quaternion.Euler(0f, pose.Yaw, 0f));

            // Then slide the body until the eyes are at the Scene view position. A second pass
            // absorbs any offset the first one could not (crouch height, camera smoothing).
            for (int pass = 0; pass < 2; pass++)
            {
                Vector3 delta = pose.Position - camera.transform.position;
                if (delta.sqrMagnitude < 1e-8f)
                {
                    break;
                }

                controller.SetPosition(controller.transform.position + delta);
            }

            report = new PlacementReport(
                controller.name,
                Vector3.Distance(camera.transform.position, pose.Position),
                Quaternion.Angle(camera.transform.rotation, pose.Rotation));
            return true;
        }

        /// <summary>The transform that carries the pitch: the controller's own rule, then its usual name.</summary>
        private static Transform ResolvePivot(FirstPersonController controller, Camera camera)
        {
            using (SerializedObject serialized = new SerializedObject(controller))
            {
                SerializedProperty property = serialized.FindProperty("cameraPivot");
                if (property != null && property.objectReferenceValue is Transform assigned)
                {
                    return assigned;
                }
            }

            Transform parent = camera.transform.parent;
            if (parent != null && parent != controller.transform && parent.IsChildOf(controller.transform))
            {
                return parent;
            }

            // After the controller's Start the camera hangs below an unparented smoother instead.
            Transform named = controller.transform.Find("CameraPivot");
            return named != null ? named : camera.transform;
        }

        private static float ClampPitch(FirstPersonController controller, float pitch)
        {
            using (SerializedObject serialized = new SerializedObject(controller))
            {
                SerializedProperty property = serialized.FindProperty("settings");
                if (property != null && property.objectReferenceValue is FPCSettings settings)
                {
                    return Mathf.Clamp(pitch, settings.verticalRange.x, settings.verticalRange.y);
                }
            }

            return pitch;
        }
    }

    [InitializeOnLoad]
    internal static class MhzeFirstPersonPlacerRegistration
    {
        static MhzeFirstPersonPlacerRegistration()
        {
            PlayFromView.RegisterPlacer(new MhzeFirstPersonPlacer());
        }
    }
}
