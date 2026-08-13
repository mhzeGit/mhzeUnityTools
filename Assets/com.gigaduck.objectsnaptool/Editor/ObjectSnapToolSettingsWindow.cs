using UnityEngine;
using UnityEditor;

namespace Gigaduck.ObjectSnapTool
{
    /// <summary>
    /// Simple settings window for the Object Snap Tool.
    /// Open via Tools > Object Snap Tool > Settings.
    /// </summary>
    public class ObjectSnapToolSettingsWindow : EditorWindow
    {
        private SerializedObject   _serializedSettings;
        private SerializedProperty _detectionMethodProp;
        private SerializedProperty _snapLayersProp;

        [MenuItem("Tools/Object Snap Tool/Settings", priority = 200)]
        public static void OpenWindow()
        {
            var window = GetWindow<ObjectSnapToolSettingsWindow>(false, "Snap Tool Settings", true);
            window.minSize = new Vector2(340, 110);
        }

        private void OnEnable()
        {
            LoadSettings();
        }

        private void LoadSettings()
        {
            var settings = ObjectSnapToolSettings.GetOrCreate();
            if (settings == null) return;

            _serializedSettings  = new SerializedObject(settings);
            _detectionMethodProp = _serializedSettings.FindProperty(nameof(ObjectSnapToolSettings.detectionMethod));
            _snapLayersProp      = _serializedSettings.FindProperty(nameof(ObjectSnapToolSettings.snapLayers));
        }

        private void OnGUI()
        {
            if (_serializedSettings == null)
            {
                LoadSettings();
                if (_serializedSettings == null)
                {
                    EditorGUILayout.HelpBox("Could not load settings asset.", MessageType.Error);
                    return;
                }
            }

            _serializedSettings.Update();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Mouse Click Snap  (Ctrl + Shift + Left Click)", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.PropertyField(
                _detectionMethodProp,
                new GUIContent("Detection Method",
                    "Collider: uses Physics.Raycast (objects need colliders).\n" +
                    "Mesh: uses ray–mesh intersection (no colliders required)."));

            EditorGUILayout.PropertyField(
                _snapLayersProp,
                new GUIContent("Snap Layers",
                    "Only surfaces on these layers will be considered snap targets."));

            if (_serializedSettings.ApplyModifiedProperties())
                AssetDatabase.SaveAssets();
        }
    }
}
