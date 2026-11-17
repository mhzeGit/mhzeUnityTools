using NUnit.Framework;
using UnityEngine;

namespace Gigaduck.PlayFromView.Tests
{
    /// <summary>Checks the pose maths and that the generic placer makes the two cameras coincide.</summary>
    public class PlayFromViewTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [TestCase(0f, 0f)]
        [TestCase(37f, 20f)]
        [TestCase(-120f, -60f)]
        [TestCase(180f, 45f)]
        [TestCase(90f, 89f)]
        [TestCase(-170f, -89f)]
        public void PoseSplitsIntoYawAndPitchAndRebuildsTheView(float yaw, float pitch)
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            ViewPose pose = new ViewPose(new Vector3(1f, 2f, 3f), rotation);

            Assert.That(Mathf.DeltaAngle(yaw, pose.Yaw), Is.EqualTo(0f).Within(0.01f));
            Assert.That(pose.Pitch, Is.EqualTo(pitch).Within(0.01f));
            Assert.That(Quaternion.Angle(rotation, pose.LookRotation), Is.LessThan(0.01f));
        }

        [TestCase(60f)]
        [TestCase(-140f)]
        public void LookingStraightDownOrUpKeepsTheHeading(float yaw)
        {
            ViewPose down = new ViewPose(Vector3.zero, Quaternion.Euler(90f, yaw, 0f));
            ViewPose up = new ViewPose(Vector3.zero, Quaternion.Euler(-90f, yaw, 0f));

            Assert.That(Mathf.DeltaAngle(yaw, down.Yaw), Is.EqualTo(0f).Within(0.1f));
            Assert.That(down.Pitch, Is.EqualTo(90f).Within(0.1f));
            Assert.That(Mathf.DeltaAngle(yaw, up.Yaw), Is.EqualTo(0f).Within(0.1f));
            Assert.That(up.Pitch, Is.EqualTo(-90f).Within(0.1f));
        }

        [Test]
        public void PoseSurvivesSerialisation()
        {
            ViewPose pose = new ViewPose(new Vector3(-12.345678f, 0.0001f, 1e5f), Quaternion.Euler(12f, 34f, 0f));

            Assert.That(ViewPose.TryParse(pose.Serialize(), out ViewPose back), Is.True);
            Assert.That(back.Position, Is.EqualTo(pose.Position));
            Assert.That(Quaternion.Angle(back.Rotation, pose.Rotation), Is.LessThan(0.001f));
            Assert.That(ViewPose.TryParse("not a pose", out _), Is.False);
            Assert.That(ViewPose.TryParse("1,2,3", out _), Is.False);
        }

        [Test]
        public void GenericAlignPutsTheEyeOnTheScenePose()
        {
            _root = new GameObject("PlayFromView test player");
            _root.transform.SetPositionAndRotation(new Vector3(5f, 0f, 5f), Quaternion.Euler(0f, 90f, 0f));
            _root.AddComponent<CharacterController>();
            GameObject eye = new GameObject("Eye", typeof(Camera));
            eye.transform.SetParent(_root.transform, false);
            eye.transform.localPosition = new Vector3(0f, 1.6f, 0.1f);

            ViewPose pose = new ViewPose(new Vector3(1f, 3f, 2f), Quaternion.Euler(20f, 200f, 0f));
            GenericPlayerPlacer.Align(_root.transform, eye.transform, pose);

            Assert.That(Vector3.Distance(eye.transform.position, pose.Position), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(eye.transform.rotation, pose.Rotation), Is.LessThan(0.01f));
            Assert.That(Vector3.Dot(_root.transform.up, Vector3.up), Is.EqualTo(1f).Within(1e-5f), "The body must stay upright.");
            Assert.That(Mathf.DeltaAngle(pose.Yaw, _root.transform.eulerAngles.y), Is.EqualTo(0f).Within(0.01f));
            Assert.That(_root.GetComponent<CharacterController>().enabled, Is.True, "The controller is switched back on.");
        }

        [Test]
        public void HighestPriorityPlacerWinsAndSameNameReplaces()
        {
            FakePlacer low = new FakePlacer("pfv-test", 1000, true);
            FakePlacer high = new FakePlacer("pfv-test-high", 2000, true);
            try
            {
                PlayFromView.RegisterPlacer(low);
                PlayFromView.RegisterPlacer(high);

                Assert.That(PlayFromView.TryPlace(new ViewPose(Vector3.zero, Quaternion.identity), out _, out string winner), Is.True);
                Assert.That(winner, Is.EqualTo("pfv-test-high"));
                Assert.That(low.Calls, Is.EqualTo(0));

                FakePlacer replacement = new FakePlacer("pfv-test-high", 2000, true);
                PlayFromView.RegisterPlacer(replacement);
                PlayFromView.TryPlace(new ViewPose(Vector3.zero, Quaternion.identity), out _, out _);
                Assert.That(replacement.Calls, Is.EqualTo(1));
                Assert.That(high.Calls, Is.EqualTo(1));
            }
            finally
            {
                PlayFromView.UnregisterPlacer("pfv-test");
                PlayFromView.UnregisterPlacer("pfv-test-high");
            }
        }

        private sealed class FakePlacer : IPlayFromViewPlacer
        {
            private readonly bool _places;

            public FakePlacer(string name, int priority, bool places)
            {
                Name = name;
                Priority = priority;
                _places = places;
            }

            public string Name { get; }

            public int Priority { get; }

            public int Calls { get; private set; }

            public bool TryPlace(in ViewPose pose, out PlacementReport report)
            {
                Calls++;
                report = new PlacementReport(Name, 0f, 0f);
                return _places;
            }
        }
    }
}
