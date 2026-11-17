using System.Globalization;
using UnityEngine;

namespace Gigaduck.PlayFromView
{
    /// <summary>
    /// A camera pose in world space, split into the yaw (turn around Y) and pitch (look up or down)
    /// that first-person rigs keep separately. The Scene view never rolls, so yaw and pitch rebuild
    /// the exact same view.
    /// </summary>
    public readonly struct ViewPose
    {
        /// <summary>World position of the camera.</summary>
        public Vector3 Position { get; }

        /// <summary>World rotation of the camera, as captured.</summary>
        public Quaternion Rotation { get; }

        /// <summary>Turn around the world Y axis in degrees; the heading a player body should face.</summary>
        public float Yaw { get; }

        /// <summary>Look angle in degrees, the same sign as Unity's euler X: positive looks down.</summary>
        public float Pitch { get; }

        public ViewPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation.normalized;

            Vector3 forward = Rotation * Vector3.forward;
            float horizontal = new Vector2(forward.x, forward.z).magnitude;
            Pitch = Mathf.Atan2(-forward.y, horizontal) * Mathf.Rad2Deg;

            if (horizontal > 1e-4f)
            {
                Yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            }
            else
            {
                // Looking straight up or down: the heading is where the top of the view points.
                Vector3 up = Rotation * Vector3.up;
                float sign = forward.y < 0f ? 1f : -1f;
                Yaw = Mathf.Atan2(sign * up.x, sign * up.z) * Mathf.Rad2Deg;
            }
        }

        /// <summary>Rotation for a body that only turns around Y.</summary>
        public Quaternion YawRotation => Quaternion.Euler(0f, Yaw, 0f);

        /// <summary>The captured view with the horizon forced level (no roll).</summary>
        public Quaternion LookRotation => Quaternion.Euler(Pitch, Yaw, 0f);

        /// <summary>Text form that survives a domain reload (SessionState).</summary>
        public string Serialize()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R},{3:R},{4:R},{5:R},{6:R}",
                Position.x, Position.y, Position.z, Rotation.x, Rotation.y, Rotation.z, Rotation.w);
        }

        public static bool TryParse(string text, out ViewPose pose)
        {
            pose = default;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string[] parts = text.Split(',');
            if (parts.Length != 7)
            {
                return false;
            }

            float[] values = new float[7];
            for (int i = 0; i < 7; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                {
                    return false;
                }
            }

            pose = new ViewPose(
                new Vector3(values[0], values[1], values[2]),
                new Quaternion(values[3], values[4], values[5], values[6]));
            return true;
        }
    }
}
