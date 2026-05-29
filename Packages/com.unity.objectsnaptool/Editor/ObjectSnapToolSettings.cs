using UnityEngine;
using UnityEditor;
using System.IO;

namespace Unity.ObjectSnapTool
{
    /// <summary>
    /// Project-level settings for the Object Snap Tool.
    /// The asset is auto-created at Assets/Editor/ObjectSnapTool/Settings.asset on first use.
    /// </summary>
    public class ObjectSnapToolSettings : ScriptableObject
    {
        public enum DetectionMethod
        {
            /// <summary>Uses Physics.Raycast — objects must have colliders.</summary>
            Collider,
            /// <summary>Uses ray–mesh intersection — works without colliders.</summary>
            Mesh
        }

        private const string AssetDirectory = "Assets/Editor/ObjectSnapTool";
        private const string AssetPath      = AssetDirectory + "/Settings.asset";

        [Tooltip("Collider: requires colliders on target objects.\nMesh: ray-mesh intersection, no colliders needed.")]
        public DetectionMethod detectionMethod = DetectionMethod.Collider;

        [Tooltip("Only objects on these layers will be considered as snap targets.")]
        public LayerMask snapLayers = ~0; // all layers by default

        // ── Singleton ────────────────────────────────────────────────────────

        private static ObjectSnapToolSettings _instance;

        /// <summary>Loads the settings asset, creating it if it does not yet exist.</summary>
        public static ObjectSnapToolSettings GetOrCreate()
        {
            if (_instance != null)
                return _instance;

            _instance = AssetDatabase.LoadAssetAtPath<ObjectSnapToolSettings>(AssetPath);
            if (_instance != null)
                return _instance;

            if (!Directory.Exists(AssetDirectory))
                Directory.CreateDirectory(AssetDirectory);

            _instance = CreateInstance<ObjectSnapToolSettings>();
            AssetDatabase.CreateAsset(_instance, AssetPath);
            AssetDatabase.SaveAssets();
            return _instance;
        }
    }
}
