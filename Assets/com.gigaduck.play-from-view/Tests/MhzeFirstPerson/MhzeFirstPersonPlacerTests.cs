using System.Reflection;
using Gigaduck.PlayFromView.Integrations;
using MHZE.FirstPersonController;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Gigaduck.PlayFromView.Tests
{
    /// <summary>
    /// Builds a real First Person Controller (its Awake is called by hand because edit-mode tests do not
    /// run it) and checks the placer lines its camera up with a Scene view pose. It places that exact
    /// controller, never one found in the open scene.
    /// </summary>
    public class MhzeFirstPersonPlacerTests
    {
        private GameObject _root;
        private FPCSettings _settings;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }

            if (_settings != null)
            {
                Object.DestroyImmediate(_settings);
            }
        }

        [TestCase(0f, 0f)]
        [TestCase(137f, 25f)]
        [TestCase(-60f, -40f)]
        public void CameraEndsUpExactlyOnTheScenePose(float yaw, float pitch)
        {
            FirstPersonController controller = BuildController();
            Vector3 startFeet = controller.transform.position;
            ViewPose pose = new ViewPose(new Vector3(4f, 2.5f, -3f), Quaternion.Euler(pitch, yaw, 0f));

            Assert.That(MhzeFirstPersonPlacer.Place(controller, pose, out PlacementReport report), Is.True);

            Camera camera = controller.PlayerCamera;
            Assert.That(report.PositionError, Is.LessThan(0.001f));
            Assert.That(report.AngleError, Is.LessThan(0.05f));
            Assert.That(Vector3.Distance(camera.transform.position, pose.Position), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(camera.transform.rotation, pose.Rotation), Is.LessThan(0.05f));
            Assert.That(controller.transform.position, Is.Not.EqualTo(startFeet));
            Assert.That(Vector3.Dot(controller.transform.up, Vector3.up), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void PitchIsClampedToTheControllersRangeAndStillReported()
        {
            FirstPersonController controller = BuildController();
            _settings.verticalRange = new Vector2(-60f, 60f);
            ViewPose pose = new ViewPose(new Vector3(0f, 2f, 0f), Quaternion.Euler(85f, 10f, 0f));

            Assert.That(MhzeFirstPersonPlacer.Place(controller, pose, out PlacementReport report), Is.True);

            Assert.That(report.PositionError, Is.LessThan(0.001f));
            Assert.That(report.AngleError, Is.EqualTo(25f).Within(0.5f), "Only the pitch beyond the controller's range is lost.");
        }

        private FirstPersonController BuildController()
        {
            _settings = ScriptableObject.CreateInstance<FPCSettings>();
            _root = new GameObject("PlayFromView test FPC");
            _root.SetActive(false);
            _root.transform.SetPositionAndRotation(new Vector3(10f, 0f, 10f), Quaternion.Euler(0f, 45f, 0f));

            CharacterController character = _root.AddComponent<CharacterController>();
            character.height = 1.8f;
            character.center = new Vector3(0f, 0.9f, 0f);

            GameObject pivot = new GameObject("CameraPivot");
            pivot.transform.SetParent(_root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            GameObject cameraObject = new GameObject("Camera", typeof(Camera));
            cameraObject.transform.SetParent(pivot.transform, false);

            FirstPersonController controller = _root.AddComponent<FirstPersonController>();
            using (SerializedObject serialized = new SerializedObject(controller))
            {
                serialized.FindProperty("characterController").objectReferenceValue = character;
                serialized.FindProperty("playerCamera").objectReferenceValue = cameraObject.GetComponent<Camera>();
                serialized.FindProperty("cameraPivot").objectReferenceValue = pivot.transform;
                serialized.FindProperty("settings").objectReferenceValue = _settings;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            _root.SetActive(true);
            typeof(FirstPersonController)
                .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);
            return controller;
        }
    }
}
