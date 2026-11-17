using UnityEngine;

namespace Gigaduck.PlayFromView
{
    /// <summary>
    /// Works with any rig: finds the object with the player tag, turns its body to the Scene view's
    /// heading, points its camera exactly like the Scene view and slides the body so the two cameras
    /// coincide. Rigs that keep their own yaw and pitch values need a dedicated placer.
    /// </summary>
    public sealed class GenericPlayerPlacer : IPlayFromViewPlacer
    {
        public string Name => "Generic player (tag)";

        public int Priority => 0;

        public bool TryPlace(in ViewPose pose, out PlacementReport report)
        {
            report = default;

            GameObject player = FindTagged(PlayFromViewSettings.PlayerTag);
            if (player != null)
            {
                Transform eye = FindEye(player.transform);
                Align(player.transform, eye, pose);
                report = MakeReport(player.name, eye, pose);
                return true;
            }

            if (PlayFromViewSettings.FallbackToMainCamera)
            {
                Camera main = Camera.main;
                if (main != null)
                {
                    Align(main.transform, main.transform, pose);
                    report = MakeReport(main.name, main.transform, pose);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Moves <paramref name="root"/> so that <paramref name="eye"/> (the root itself, or a camera
        /// below it) sits at the pose and looks the same way. The root only turns around Y; the eye
        /// takes the pitch.
        /// </summary>
        public static void Align(Transform root, Transform eye, in ViewPose pose)
        {
            CharacterController character = root.GetComponent<CharacterController>();
            bool characterWasEnabled = character != null && character.enabled;
            if (characterWasEnabled)
            {
                // A live CharacterController would push the move back to where it was.
                character.enabled = false;
            }

            if (eye == root)
            {
                root.rotation = pose.LookRotation;
            }
            else
            {
                root.rotation = pose.YawRotation;
                eye.rotation = pose.LookRotation;
            }

            root.position += pose.Position - eye.position;

            Rigidbody body = root.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = root.position;
                body.rotation = root.rotation;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            Physics.SyncTransforms();

            if (characterWasEnabled)
            {
                character.enabled = true;
            }
        }

        private static GameObject FindTagged(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return null;
            }

            try
            {
                return GameObject.FindWithTag(tag);
            }
            catch (UnityException)
            {
                // The tag is not defined in this project.
                return null;
            }
        }

        private static Transform FindEye(Transform root)
        {
            Camera camera = root.GetComponentInChildren<Camera>(false);
            return camera != null ? camera.transform : root;
        }

        private static PlacementReport MakeReport(string name, Transform eye, in ViewPose pose)
        {
            return new PlacementReport(
                name,
                Vector3.Distance(eye.position, pose.Position),
                Quaternion.Angle(eye.rotation, pose.Rotation));
        }
    }
}
