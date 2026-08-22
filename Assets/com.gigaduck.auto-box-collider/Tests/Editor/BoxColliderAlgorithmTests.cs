using NUnit.Framework;
using UnityEngine;

namespace Gigaduck.AutoBoxCollider.Tests
{
    internal sealed class BoxColliderAlgorithmTests
    {
        [Test]
        public void SolidVolumeUsesOneExactBox()
        {
            VoxelGrid grid = CreateGrid(5, 4, 3);
            Fill(grid, 1, 1, 1, 4, 3, 3);

            BoxDecompositionResult result = BoxDecomposer.Decompose(grid, 0f, 16, null);

            Assert.That(result.Boxes, Has.Count.EqualTo(1));
            Assert.That(result.OccupiedCoverage, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(result.EmptyVolumeFraction, Is.EqualTo(0f));
            Assert.That(result.BudgetLimited, Is.False);
        }

        [Test]
        public void ConcaveVolumeSplitsWithoutLosingCoverage()
        {
            VoxelGrid grid = CreateLShapeGrid();

            BoxDecompositionResult result = BoxDecomposer.Decompose(grid, 0f, 8, null);

            Assert.That(result.Boxes, Has.Count.EqualTo(2));
            Assert.That(result.OccupiedCoverage, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(result.EmptyVolumeFraction, Is.EqualTo(0f));
            Assert.That(result.BudgetLimited, Is.False);
        }

        [Test]
        public void ColliderBudgetReportsUnresolvedShapeError()
        {
            VoxelGrid grid = CreateLShapeGrid();

            BoxDecompositionResult result = BoxDecomposer.Decompose(grid, 0f, 1, null);

            Assert.That(result.Boxes, Has.Count.EqualTo(1));
            Assert.That(result.OccupiedCoverage, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(result.EmptyVolumeFraction, Is.GreaterThan(0.5f));
            Assert.That(result.BudgetLimited, Is.True);
        }

        [Test]
        public void RelaxedToleranceMinimizesConcaveVolumeToOneBox()
        {
            VoxelGrid grid = CreateLShapeGrid();

            BoxDecompositionResult result = BoxDecomposer.Decompose(grid, 0.6f, 8, null);

            Assert.That(result.Boxes, Has.Count.EqualTo(1));
            Assert.That(result.OccupiedCoverage, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(result.BudgetLimited, Is.False);
        }

        [Test]
        public void DoorOpeningIsNeverBridgedByTolerance()
        {
            VoxelGrid grid = CreateDoorWallGrid();
            MeshColliderAnalyzer.MarkProtectedVoids(grid);

            BoxDecompositionResult result = BoxDecomposer.Decompose(grid, 0.2f, 16, null);

            Assert.That(result.OccupiedCoverage, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(result.ProtectedVoidVoxelCount, Is.GreaterThan(0));
            Assert.That(result.UnresolvedProtectedVoidCount, Is.EqualTo(0));
            foreach (IntBox box in result.Boxes)
            {
                Assert.That(Covers(box, 4, 0, 0), Is.False);
                Assert.That(Covers(box, 5, 0, 0), Is.False);
            }
        }

        [Test]
        public void OpeningPreservationCanOverrideColliderBudget()
        {
            VoxelGrid grid = CreateDoorWallGrid();
            MeshColliderAnalyzer.MarkProtectedVoids(grid);

            BoxDecompositionResult result = BoxDecomposer.Decompose(grid, 0.5f, 1, null);

            Assert.That(result.ColliderBudgetExceeded, Is.True);
            Assert.That(result.UnresolvedProtectedVoidCount, Is.EqualTo(0));
            Assert.That(result.Boxes.Count, Is.GreaterThan(1));
        }

        [Test]
        public void ThroughTunnelRemainsClearAcrossMeshThickness()
        {
            VoxelGrid grid = CreateGrid(8, 8, 3);
            Fill(grid, 0, 0, 0, 8, 8, 3);
            for (int z = 0; z < 3; z++)
            for (int y = 3; y < 5; y++)
            for (int x = 3; x < 5; x++)
                grid.Occupied[grid.Index(x, y, z)] = false;
            MeshColliderAnalyzer.MarkProtectedVoids(grid);

            BoxDecompositionResult result = BoxDecomposer.Decompose(grid, 0.25f, 32, null);

            Assert.That(result.UnresolvedProtectedVoidCount, Is.EqualTo(0));
            foreach (IntBox box in result.Boxes)
            {
                for (int z = 0; z < 3; z++)
                    Assert.That(Covers(box, 3, 3, z), Is.False);
            }
        }

        [Test]
        public void PrincipalAxesTightenRotatedElongatedGeometry()
        {
            MeshGeometry geometry = CreateBoxGeometry(new Vector3(4f, 1f, 0.5f), 35f);

            AnalysisFrame frame = MeshColliderAnalyzer.CalculatePrincipalFrame(geometry);
            Bounds objectBounds = CalculateBounds(geometry, new AnalysisFrame(Vector3.zero, Quaternion.identity, "Object"));
            Bounds principalBounds = CalculateBounds(geometry, frame);

            Assert.That(frame.Name, Does.StartWith("Principal axes"));
            Assert.That(principalBounds.size.x * principalBounds.size.y * principalBounds.size.z,
                Is.LessThan(objectBounds.size.x * objectBounds.size.y * objectBounds.size.z * 0.75f));
        }

        [Test]
        public void TriangleConnectivityDetectsWatertightComponent()
        {
            MeshGeometry geometry = CreateBoxGeometry(Vector3.one, 0f);

            MeshTopologyAnalyzer.Analyze(geometry);

            Assert.That(geometry.ConnectedComponentCount, Is.EqualTo(1));
            Assert.That(geometry.BoundaryEdgeCount, Is.EqualTo(0));
            Assert.That(geometry.NonManifoldEdgeCount, Is.EqualTo(0));
            Assert.That(geometry.IsWatertight, Is.True);
        }

        [Test]
        public void MissingTriangleIsReportedAsOpenTopology()
        {
            MeshGeometry geometry = CreateBoxGeometry(Vector3.one, 0f);
            geometry.Triangles.RemoveRange(geometry.Triangles.Count - 3, 3);

            MeshTopologyAnalyzer.Analyze(geometry);

            Assert.That(geometry.BoundaryEdgeCount, Is.GreaterThan(0));
            Assert.That(geometry.IsWatertight, Is.False);
        }

        [Test]
        public void AngledPlanarSurfaceUsesOneRotatedCandidate()
        {
            const float angleDegrees = 32f;
            float radians = angleDegrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            var geometry = new MeshGeometry();
            Vector3[] localVertices =
            {
                new Vector3(-2f, -0.5f, 0f),
                new Vector3(2f, -0.5f, 0f),
                new Vector3(2f, 0.5f, 0f),
                new Vector3(-2f, 0.5f, 0f)
            };
            foreach (Vector3 vertex in localVertices)
            {
                geometry.Vertices.Add(new Vector3(
                    vertex.x * cosine - vertex.y * sine,
                    vertex.x * sine + vertex.y * cosine,
                    0f));
            }
            geometry.Triangles.AddRange(new[] { 0, 1, 2, 0, 2, 3 });

            var grid = new VoxelGrid(24, 24, 3, 0.25f, new Vector3(-3f, -3f, -0.375f));
            for (int y = 0; y < grid.SizeY; y++)
            for (int x = 0; x < grid.SizeX; x++)
            {
                Vector3 point = grid.CellCenter(x, y, 1);
                float localX = point.x * cosine + point.y * sine;
                float localY = -point.x * sine + point.y * cosine;
                if (Mathf.Abs(localX) > 2.05f || Mathf.Abs(localY) > 0.55f)
                    continue;
                int index = grid.Index(x, y, 1);
                grid.Occupied[index] = true;
                grid.Surface[index] = true;
            }

            var settings = new ColliderAnalysisSettings
            {
                maximumColliders = 16,
                emptySpaceTolerance = 0.05f,
                paddingInVoxels = 0f
            };
            OrientedBoxFitResult result = OrientedBoxFitter.Fit(
                geometry,
                grid,
                new AnalysisFrame(Vector3.zero, Quaternion.identity, "Object"),
                settings);

            Assert.That(result.Boxes, Is.Not.Empty);
            Assert.That(result.CoveredCount, Is.GreaterThan(20));
            Vector3 fittedDirection = result.Boxes[0].Rotation * Vector3.right;
            Vector3 expectedDirection = new Vector3(cosine, sine, 0f);
            Assert.That(Mathf.Abs(Vector3.Dot(fittedDirection.normalized, expectedDirection)), Is.GreaterThan(0.98f));
        }

        [Test]
        public void SlopedPatchInsideWedgeGetsIndependentRotation()
        {
            var geometry = new MeshGeometry();
            geometry.Vertices.AddRange(new[]
            {
                new Vector3(0f, 0f, -0.5f),
                new Vector3(4f, 0f, -0.5f),
                new Vector3(4f, 2f, -0.5f),
                new Vector3(0f, 0f, 0.5f),
                new Vector3(4f, 0f, 0.5f),
                new Vector3(4f, 2f, 0.5f)
            });
            geometry.Triangles.AddRange(new[]
            {
                0, 1, 2,
                3, 5, 4,
                0, 3, 4, 0, 4, 1,
                1, 4, 5, 1, 5, 2,
                0, 2, 5, 0, 5, 3
            });

            var grid = new VoxelGrid(22, 12, 8, 0.25f, new Vector3(-0.75f, -0.5f, -1f));
            for (int z = 0; z < grid.SizeZ; z++)
            for (int y = 0; y < grid.SizeY; y++)
            for (int x = 0; x < grid.SizeX; x++)
            {
                Vector3 point = grid.CellCenter(x, y, z);
                if (point.x < 0f || point.x > 4f
                    || point.z < -0.5f || point.z > 0.5f
                    || point.y < 0f || point.y > point.x * 0.5f)
                    continue;
                grid.Occupied[grid.Index(x, y, z)] = true;
            }

            var settings = new ColliderAnalysisSettings
            {
                maximumColliders = 24,
                emptySpaceTolerance = 0.05f,
                planarAngleTolerance = 2f
            };
            OrientedBoxFitResult result = OrientedBoxFitter.Fit(
                geometry,
                grid,
                new AnalysisFrame(Vector3.zero, Quaternion.identity, "Object"),
                settings);

            Vector3 expectedSlopeNormal = new Vector3(-0.5f, 1f, 0f).normalized;
            bool foundSlope = false;
            foreach (ColliderBox box in result.Boxes)
            {
                Vector3 normal = box.Rotation * Vector3.forward;
                if (Mathf.Abs(Vector3.Dot(normal.normalized, expectedSlopeNormal)) > 0.97f)
                {
                    foundSlope = true;
                    break;
                }
            }
            Assert.That(foundSlope, Is.True, "No independently rotated collider candidate matched the wedge slope.");
        }

        [Test]
        public void MeshAnalysisAndBakeCreatesConfirmedChildColliders()
        {
            var target = new GameObject("Collider Test Target");
            Mesh mesh = CreateCubeMesh();
            try
            {
                target.AddComponent<MeshFilter>().sharedMesh = mesh;
                target.AddComponent<MeshRenderer>();
                Assert.That(
                    MeshColliderAnalyzer.TryBuildGeometry(target, false, out MeshGeometry geometry, out string error),
                    Is.True,
                    error);

                var settings = new ColliderAnalysisSettings
                {
                    alignment = ColliderAlignment.ObjectAxes,
                    voxelResolution = 12,
                    emptySpaceTolerance = 0.01f,
                    maximumColliders = 16,
                    fillEnclosedVolume = true
                };

                ColliderAnalysisResult preview = MeshColliderAnalyzer.Analyze(target, geometry, settings, null);
                Assert.That(preview.Boxes, Has.Count.EqualTo(1));
                Assert.That(preview.OccupiedCoverage, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(target.GetComponentsInChildren<BoxCollider>(true), Is.Empty);

                GameObject generatedRoot = BoxColliderBaker.Bake(preview, true, false, null);

                Assert.That(generatedRoot.transform.parent, Is.EqualTo(target.transform));
                Assert.That(generatedRoot.transform.childCount, Is.EqualTo(preview.Boxes.Count));
                Assert.That(generatedRoot.GetComponentsInChildren<BoxCollider>(true), Has.Length.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void GeometryHashChangesWhenOnlyTopologyChanges()
        {
            var target = new GameObject("Topology Hash Target");
            var mesh = new Mesh
            {
                vertices = new[]
                {
                    Vector3.zero,
                    Vector3.right,
                    Vector3.up,
                    Vector3.one
                },
                triangles = new[] { 0, 1, 2, 1, 3, 2 }
            };
            try
            {
                target.AddComponent<MeshFilter>().sharedMesh = mesh;
                Assert.That(MeshColliderAnalyzer.TryBuildGeometry(target, false, out MeshGeometry first, out _), Is.True);

                mesh.triangles = new[] { 0, 1, 3, 0, 3, 2 };
                Assert.That(MeshColliderAnalyzer.TryBuildGeometry(target, false, out MeshGeometry second, out _), Is.True);

                Assert.That(second.SourceHash, Is.Not.EqualTo(first.SourceHash));
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(mesh);
            }
        }

        private static VoxelGrid CreateGrid(int sizeX, int sizeY, int sizeZ)
        {
            return new VoxelGrid(sizeX, sizeY, sizeZ, 1f, Vector3.zero);
        }

        private static VoxelGrid CreateLShapeGrid()
        {
            VoxelGrid grid = CreateGrid(4, 4, 1);
            Fill(grid, 0, 0, 0, 4, 1, 1);
            Fill(grid, 0, 0, 0, 1, 4, 1);
            return grid;
        }

        private static VoxelGrid CreateDoorWallGrid()
        {
            VoxelGrid grid = CreateGrid(10, 5, 1);
            Fill(grid, 0, 0, 0, 10, 5, 1);
            for (int y = 0; y < 2; y++)
            for (int x = 4; x < 6; x++)
                grid.Occupied[grid.Index(x, y, 0)] = false;
            return grid;
        }

        private static bool Covers(IntBox box, int x, int y, int z)
        {
            return x >= box.MinX && x < box.MaxX
                && y >= box.MinY && y < box.MaxY
                && z >= box.MinZ && z < box.MaxZ;
        }

        private static Bounds CalculateBounds(MeshGeometry geometry, AnalysisFrame frame)
        {
            var bounds = new Bounds(frame.ToFrame(geometry.Vertices[0]), Vector3.zero);
            for (int i = 1; i < geometry.Vertices.Count; i++)
                bounds.Encapsulate(frame.ToFrame(geometry.Vertices[i]));
            return bounds;
        }

        private static MeshGeometry CreateBoxGeometry(Vector3 size, float zRotationDegrees)
        {
            Vector3[] vertices =
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f)
            };
            int[] triangles =
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                3, 7, 6, 3, 6, 2,
                0, 4, 7, 0, 7, 3,
                1, 2, 6, 1, 6, 5
            };
            var geometry = new MeshGeometry();
            float radians = zRotationDegrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            foreach (Vector3 vertex in vertices)
            {
                Vector3 scaled = Vector3.Scale(vertex, size);
                geometry.Vertices.Add(new Vector3(
                    scaled.x * cosine - scaled.y * sine,
                    scaled.x * sine + scaled.y * cosine,
                    scaled.z));
            }
            geometry.Triangles.AddRange(triangles);
            return geometry;
        }

        private static void Fill(
            VoxelGrid grid,
            int minX,
            int minY,
            int minZ,
            int maxX,
            int maxY,
            int maxZ)
        {
            for (int z = minZ; z < maxZ; z++)
            for (int y = minY; y < maxY; y++)
            for (int x = minX; x < maxX; x++)
                grid.Occupied[grid.Index(x, y, z)] = true;
        }

        private static Mesh CreateCubeMesh()
        {
            var mesh = new Mesh { name = "Unit Cube Test Mesh" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f)
            };
            mesh.triangles = new[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                3, 7, 6, 3, 6, 2,
                0, 4, 7, 0, 7, 3,
                1, 2, 6, 1, 6, 5
            };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
