using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gigaduck.AutoBoxCollider
{
    internal sealed class OrientedBoxFitResult
    {
        public readonly List<ColliderBox> Boxes = new List<ColliderBox>();
        public bool[] CoveredOccupied;
        public int CoveredCount;
        public int EmptyCellCount;
    }

    internal static class OrientedBoxFitter
    {
        private const int MaximumPatchFramesPerComponent = 6;

        private sealed class Candidate
        {
            public ColliderBox Box;
            public List<int> CoveredCells;
            public int EmptyCells;
            public bool IsComponent;
            public float Score;
        }

        private readonly struct WeldKey : IEquatable<WeldKey>
        {
            private readonly int _x;
            private readonly int _y;
            private readonly int _z;

            public WeldKey(Vector3 point, Vector3 origin, float inverseTolerance)
            {
                Vector3 value = (point - origin) * inverseTolerance;
                _x = Mathf.RoundToInt(value.x);
                _y = Mathf.RoundToInt(value.y);
                _z = Mathf.RoundToInt(value.z);
            }

            public bool Equals(WeldKey other)
            {
                return _x == other._x && _y == other._y && _z == other._z;
            }

            public override bool Equals(object obj)
            {
                return obj is WeldKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _x;
                    hash = hash * 397 ^ _y;
                    hash = hash * 397 ^ _z;
                    return hash;
                }
            }
        }

        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            private readonly int _first;
            private readonly int _second;

            public EdgeKey(int first, int second)
            {
                if (first < second)
                {
                    _first = first;
                    _second = second;
                }
                else
                {
                    _first = second;
                    _second = first;
                }
            }

            public bool IsDegenerate => _first == _second;

            public bool Equals(EdgeKey other)
            {
                return _first == other._first && _second == other._second;
            }

            public override bool Equals(object obj)
            {
                return obj is EdgeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return _first * 397 ^ _second;
                }
            }
        }

        private sealed class UnionFind
        {
            private readonly int[] _parent;
            private readonly byte[] _rank;

            public UnionFind(int count)
            {
                _parent = new int[count];
                _rank = new byte[count];
                for (int i = 0; i < count; i++)
                    _parent[i] = i;
            }

            public int Find(int value)
            {
                int root = value;
                while (_parent[root] != root)
                    root = _parent[root];
                while (_parent[value] != value)
                {
                    int next = _parent[value];
                    _parent[value] = root;
                    value = next;
                }
                return root;
            }

            public void Union(int left, int right)
            {
                int leftRoot = Find(left);
                int rightRoot = Find(right);
                if (leftRoot == rightRoot)
                    return;
                if (_rank[leftRoot] < _rank[rightRoot])
                    _parent[leftRoot] = rightRoot;
                else if (_rank[leftRoot] > _rank[rightRoot])
                    _parent[rightRoot] = leftRoot;
                else
                {
                    _parent[rightRoot] = leftRoot;
                    _rank[leftRoot]++;
                }
            }
        }

        private readonly struct PatchFrame
        {
            public readonly AnalysisFrame Frame;
            public readonly float Area;
            public readonly int ComponentRoot;
            public readonly List<int> Triangles;

            public PatchFrame(
                AnalysisFrame frame,
                float area,
                int componentRoot,
                List<int> triangles)
            {
                Frame = frame;
                Area = area;
                ComponentRoot = componentRoot;
                Triangles = triangles;
            }
        }

        public static OrientedBoxFitResult Fit(
            MeshGeometry geometry,
            VoxelGrid grid,
            AnalysisFrame analysisFrame,
            ColliderAnalysisSettings settings)
        {
            var result = new OrientedBoxFitResult
            {
                CoveredOccupied = new bool[grid.Occupied.Length]
            };
            if (geometry.TriangleCount == 0)
                return result;

            BuildTriangleGraph(
                geometry,
                out UnionFind components,
                out UnionFind patches,
                out Vector3[] triangleNormals,
                out float[] triangleAreas,
                settings.planarAngleTolerance);
            Dictionary<int, List<int>> componentGroups = BuildGroups(components, geometry.TriangleCount);
            Dictionary<int, List<int>> patchGroups = BuildGroups(patches, geometry.TriangleCount);
            var patchFrames = new List<PatchFrame>();

            foreach (List<int> patchTriangles in patchGroups.Values)
            {
                float area = 0f;
                foreach (int triangle in patchTriangles)
                    area += triangleAreas[triangle];
                if (patchTriangles.Count < 2 || area < grid.VoxelSize * grid.VoxelSize)
                    continue;

                AnalysisFrame frame = FitPatchFrame(
                    geometry, patchTriangles, triangleNormals, triangleAreas);
                patchFrames.Add(new PatchFrame(
                    frame,
                    area,
                    components.Find(patchTriangles[0]),
                    patchTriangles));
            }
            patchFrames.Sort((left, right) => right.Area.CompareTo(left.Area));

            var candidates = new List<Candidate>();
            foreach (KeyValuePair<int, List<int>> component in componentGroups)
            {
                HashSet<int> componentVertices = CollectVertices(geometry, component.Value);
                var frames = new List<AnalysisFrame>
                {
                    FitPrincipalFrame(geometry, component.Value)
                };
                int patchFrameCount = 0;
                foreach (PatchFrame patch in patchFrames)
                {
                    if (patch.ComponentRoot != component.Key)
                        continue;
                    frames.Add(patch.Frame);
                    if (++patchFrameCount >= MaximumPatchFramesPerComponent)
                        break;
                }

                foreach (AnalysisFrame frame in frames)
                {
                    ColliderBox box = FitBounds(
                        geometry,
                        componentVertices,
                        frame,
                        grid.VoxelSize,
                        settings.paddingInVoxels,
                        false);
                    Candidate candidate = EvaluateCandidate(
                        box, true, grid, analysisFrame, settings.emptySpaceTolerance);
                    if (candidate != null)
                        candidates.Add(candidate);
                }
            }

            foreach (PatchFrame patch in patchFrames)
            {
                HashSet<int> patchVertices = CollectVertices(geometry, patch.Triangles);
                ColliderBox box = FitBounds(
                    geometry,
                    patchVertices,
                    patch.Frame,
                    grid.VoxelSize,
                    settings.paddingInVoxels,
                    true);
                Candidate candidate = EvaluateCandidate(
                    box, false, grid, analysisFrame, settings.emptySpaceTolerance);
                if (candidate != null)
                    candidates.Add(candidate);
            }

            candidates.Sort((left, right) => right.Score.CompareTo(left.Score));
            int orientedLimit = Mathf.Clamp(settings.maximumColliders - 1, 1, 128);
            foreach (Candidate candidate in candidates)
            {
                if (result.Boxes.Count >= orientedLimit)
                    break;

                int uncovered = 0;
                foreach (int cell in candidate.CoveredCells)
                {
                    if (!result.CoveredOccupied[cell])
                        uncovered++;
                }

                int minimumUsefulCoverage = candidate.IsComponent ? 8 : 6;
                float requiredUncoveredFraction = candidate.IsComponent ? 0.9f : 0.45f;
                if (uncovered < minimumUsefulCoverage
                    || uncovered < candidate.CoveredCells.Count * requiredUncoveredFraction)
                    continue;

                result.Boxes.Add(candidate.Box);
                result.EmptyCellCount += candidate.EmptyCells;
                foreach (int cell in candidate.CoveredCells)
                {
                    if (result.CoveredOccupied[cell])
                        continue;
                    result.CoveredOccupied[cell] = true;
                    result.CoveredCount++;
                }
            }

            return result;
        }

        private static Candidate EvaluateCandidate(
            ColliderBox box,
            bool isComponent,
            VoxelGrid grid,
            AnalysisFrame analysisFrame,
            float requestedTolerance)
        {
            GetGridRange(box, grid, analysisFrame, out IntBox range);
            if (!range.IsValid)
                return null;

            Quaternion inverseRotation = Quaternion.Inverse(box.Rotation);
            Vector3 halfSize = box.Size * 0.5f + Vector3.one * 0.000001f;
            Matrix4x4 gridToBox = Matrix4x4.Rotate(
                inverseRotation * analysisFrame.Rotation);
            float halfVoxel = grid.VoxelSize * 0.45f;
            Vector3 projectedVoxelHalfSize = new Vector3(
                halfVoxel * (Mathf.Abs(gridToBox.m00) + Mathf.Abs(gridToBox.m01) + Mathf.Abs(gridToBox.m02)),
                halfVoxel * (Mathf.Abs(gridToBox.m10) + Mathf.Abs(gridToBox.m11) + Mathf.Abs(gridToBox.m12)),
                halfVoxel * (Mathf.Abs(gridToBox.m20) + Mathf.Abs(gridToBox.m21) + Mathf.Abs(gridToBox.m22)));
            var covered = new List<int>();
            int totalCells = 0;
            int protectedCells = 0;

            for (int z = range.MinZ; z < range.MaxZ; z++)
            for (int y = range.MinY; y < range.MaxY; y++)
            for (int x = range.MinX; x < range.MaxX; x++)
            {
                Vector3 framePoint = grid.CellCenter(x, y, z);
                Vector3 targetPoint = analysisFrame.Origin + analysisFrame.Rotation * framePoint;
                Vector3 localPoint = inverseRotation * (targetPoint - box.Center);
                int index = grid.Index(x, y, z);
                bool intersectsBox = Mathf.Abs(localPoint.x) <= halfSize.x + projectedVoxelHalfSize.x
                    && Mathf.Abs(localPoint.y) <= halfSize.y + projectedVoxelHalfSize.y
                    && Mathf.Abs(localPoint.z) <= halfSize.z + projectedVoxelHalfSize.z;
                if (grid.ProtectedVoid[index] && intersectsBox)
                    protectedCells++;
                if (Mathf.Abs(localPoint.x) > halfSize.x
                    || Mathf.Abs(localPoint.y) > halfSize.y
                    || Mathf.Abs(localPoint.z) > halfSize.z)
                    continue;

                totalCells++;
                if (grid.Occupied[index])
                    covered.Add(index);
            }

            if (protectedCells > 0 || totalCells == 0)
                return null;

            int emptyCells = totalCells - covered.Count;
            float density = covered.Count / (float)totalCells;
            float maximumComponentError = Mathf.Min(requestedTolerance, 0.02f);
            float minimumDensity = isComponent ? 1f - maximumComponentError : 0.4f;
            int minimumCoverage = isComponent ? 8 : 6;
            if (covered.Count < minimumCoverage || density < minimumDensity)
                return null;

            return new Candidate
            {
                Box = box,
                CoveredCells = covered,
                EmptyCells = emptyCells,
                IsComponent = isComponent,
                Score = covered.Count * 100f
                    - emptyCells * (isComponent ? 100f : 20f)
                    + (isComponent ? covered.Count * 0.1f : 0f)
            };
        }

        private static void GetGridRange(
            ColliderBox box,
            VoxelGrid grid,
            AnalysisFrame analysisFrame,
            out IntBox range)
        {
            Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            Vector3 half = box.Size * 0.5f;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 targetCorner = box.Center + box.Rotation
                    * new Vector3(half.x * x, half.y * y, half.z * z);
                Vector3 frameCorner = analysisFrame.ToFrame(targetCorner);
                minimum = Vector3.Min(minimum, frameCorner);
                maximum = Vector3.Max(maximum, frameCorner);
            }

            int minX = Mathf.Clamp(
                Mathf.FloorToInt((minimum.x - grid.MinCorner.x) / grid.VoxelSize) - 1, 0, grid.SizeX);
            int minY = Mathf.Clamp(
                Mathf.FloorToInt((minimum.y - grid.MinCorner.y) / grid.VoxelSize) - 1, 0, grid.SizeY);
            int minZ = Mathf.Clamp(
                Mathf.FloorToInt((minimum.z - grid.MinCorner.z) / grid.VoxelSize) - 1, 0, grid.SizeZ);
            int maxX = Mathf.Clamp(
                Mathf.CeilToInt((maximum.x - grid.MinCorner.x) / grid.VoxelSize) + 1, 0, grid.SizeX);
            int maxY = Mathf.Clamp(
                Mathf.CeilToInt((maximum.y - grid.MinCorner.y) / grid.VoxelSize) + 1, 0, grid.SizeY);
            int maxZ = Mathf.Clamp(
                Mathf.CeilToInt((maximum.z - grid.MinCorner.z) / grid.VoxelSize) + 1, 0, grid.SizeZ);
            range = new IntBox(minX, minY, minZ, maxX, maxY, maxZ);
        }

        private static ColliderBox FitBounds(
            MeshGeometry geometry,
            HashSet<int> vertexIndices,
            AnalysisFrame frame,
            float voxelSize,
            float paddingInVoxels,
            bool planar)
        {
            Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            foreach (int vertexIndex in vertexIndices)
            {
                Vector3 point = frame.ToFrame(geometry.Vertices[vertexIndex]);
                minimum = Vector3.Min(minimum, point);
                maximum = Vector3.Max(maximum, point);
            }

            float basePadding = voxelSize * (planar ? 0.65f : 0.55f);
            float userPadding = paddingInVoxels * voxelSize;
            minimum -= Vector3.one * (basePadding + userPadding);
            maximum += Vector3.one * (basePadding + userPadding);
            if (planar && maximum.z - minimum.z < voxelSize * 1.25f)
            {
                float center = (minimum.z + maximum.z) * 0.5f;
                minimum.z = center - voxelSize * 0.625f;
                maximum.z = center + voxelSize * 0.625f;
            }

            Vector3 localCenter = (minimum + maximum) * 0.5f;
            return new ColliderBox(
                frame.Origin + frame.Rotation * localCenter,
                maximum - minimum,
                frame.Rotation,
                true);
        }

        private static AnalysisFrame FitPrincipalFrame(
            MeshGeometry geometry,
            List<int> triangles)
        {
            var component = new MeshGeometry();
            var remap = new Dictionary<int, int>();
            foreach (int triangle in triangles)
            {
                int index = triangle * 3;
                AddVertex(geometry.Triangles[index]);
                AddVertex(geometry.Triangles[index + 1]);
                AddVertex(geometry.Triangles[index + 2]);
            }
            return MeshColliderAnalyzer.CalculatePrincipalFrame(component);

            void AddVertex(int sourceIndex)
            {
                if (!remap.TryGetValue(sourceIndex, out int componentIndex))
                {
                    componentIndex = component.Vertices.Count;
                    component.Vertices.Add(geometry.Vertices[sourceIndex]);
                    remap.Add(sourceIndex, componentIndex);
                }
                component.Triangles.Add(componentIndex);
            }
        }

        private static AnalysisFrame FitPatchFrame(
            MeshGeometry geometry,
            List<int> triangles,
            Vector3[] triangleNormals,
            float[] triangleAreas)
        {
            Vector3 normal = Vector3.zero;
            Vector3 origin = Vector3.zero;
            float totalArea = 0f;
            foreach (int triangle in triangles)
            {
                float area = triangleAreas[triangle];
                int index = triangle * 3;
                Vector3 centroid = (
                    geometry.Vertices[geometry.Triangles[index]]
                    + geometry.Vertices[geometry.Triangles[index + 1]]
                    + geometry.Vertices[geometry.Triangles[index + 2]]) / 3f;
                normal += triangleNormals[triangle] * area;
                origin += centroid * area;
                totalArea += area;
            }
            normal = MakeDirectionDeterministic(normal.normalized);
            origin /= Mathf.Max(totalArea, 0.000001f);

            Vector3 basisX = MostStablePerpendicularAxis(normal);
            Vector3 basisY = Vector3.Cross(normal, basisX).normalized;
            HashSet<int> vertices = CollectVertices(geometry, triangles);
            float meanX = 0f;
            float meanY = 0f;
            foreach (int vertex in vertices)
            {
                Vector3 offset = geometry.Vertices[vertex] - origin;
                meanX += Vector3.Dot(offset, basisX);
                meanY += Vector3.Dot(offset, basisY);
            }
            meanX /= Mathf.Max(1, vertices.Count);
            meanY /= Mathf.Max(1, vertices.Count);

            float covarianceXX = 0f;
            float covarianceXY = 0f;
            float covarianceYY = 0f;
            foreach (int vertex in vertices)
            {
                Vector3 offset = geometry.Vertices[vertex] - origin;
                float x = Vector3.Dot(offset, basisX) - meanX;
                float y = Vector3.Dot(offset, basisY) - meanY;
                covarianceXX += x * x;
                covarianceXY += x * y;
                covarianceYY += y * y;
            }

            float angle = 0.5f * Mathf.Atan2(
                2f * covarianceXY,
                covarianceXX - covarianceYY);
            Vector3 tangent = (Mathf.Cos(angle) * basisX + Mathf.Sin(angle) * basisY).normalized;
            tangent = MakeDirectionDeterministic(tangent);
            Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;
            Quaternion rotation = Quaternion.LookRotation(normal, bitangent);
            return new AnalysisFrame(origin, rotation, "Planar surface frame");
        }

        private static void BuildTriangleGraph(
            MeshGeometry geometry,
            out UnionFind components,
            out UnionFind patches,
            out Vector3[] triangleNormals,
            out float[] triangleAreas,
            float planarAngleTolerance)
        {
            int triangleCount = geometry.TriangleCount;
            var componentUnion = new UnionFind(triangleCount);
            var patchUnion = new UnionFind(triangleCount);
            var normals = new Vector3[triangleCount];
            var areas = new float[triangleCount];

            Vector3 minimum = geometry.Vertices[0];
            Vector3 maximum = geometry.Vertices[0];
            foreach (Vector3 vertex in geometry.Vertices)
            {
                minimum = Vector3.Min(minimum, vertex);
                maximum = Vector3.Max(maximum, vertex);
            }
            Vector3 size = maximum - minimum;
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float inverseTolerance = 1f / Mathf.Max(longest * 0.00001f, 0.0000001f);
            var weldLookup = new Dictionary<WeldKey, int>(geometry.Vertices.Count);
            var welded = new int[geometry.Vertices.Count];
            int weldCount = 0;
            for (int i = 0; i < geometry.Vertices.Count; i++)
            {
                var key = new WeldKey(geometry.Vertices[i], minimum, inverseTolerance);
                if (!weldLookup.TryGetValue(key, out int weldIndex))
                {
                    weldIndex = weldCount++;
                    weldLookup.Add(key, weldIndex);
                }
                welded[i] = weldIndex;
            }

            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                int index = triangle * 3;
                Vector3 a = geometry.Vertices[geometry.Triangles[index]];
                Vector3 b = geometry.Vertices[geometry.Triangles[index + 1]];
                Vector3 c = geometry.Vertices[geometry.Triangles[index + 2]];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                areas[triangle] = cross.magnitude * 0.5f;
                normals[triangle] = cross.sqrMagnitude > 1e-12f
                    ? cross.normalized
                    : Vector3.up;
            }

            var edgeOwner = new Dictionary<EdgeKey, int>(triangleCount * 2);
            float normalThreshold = Mathf.Cos(planarAngleTolerance * Mathf.Deg2Rad);
            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                int index = triangle * 3;
                int a = welded[geometry.Triangles[index]];
                int b = welded[geometry.Triangles[index + 1]];
                int c = welded[geometry.Triangles[index + 2]];
                ConnectEdge(new EdgeKey(a, b), triangle);
                ConnectEdge(new EdgeKey(b, c), triangle);
                ConnectEdge(new EdgeKey(c, a), triangle);
            }

            void ConnectEdge(EdgeKey edge, int triangle)
            {
                if (edge.IsDegenerate)
                    return;
                if (!edgeOwner.TryGetValue(edge, out int previous))
                {
                    edgeOwner.Add(edge, triangle);
                    return;
                }
                componentUnion.Union(previous, triangle);
                if (Vector3.Dot(normals[previous], normals[triangle]) >= normalThreshold)
                    patchUnion.Union(previous, triangle);
            }

            components = componentUnion;
            patches = patchUnion;
            triangleNormals = normals;
            triangleAreas = areas;
        }

        private static Dictionary<int, List<int>> BuildGroups(UnionFind unionFind, int count)
        {
            var groups = new Dictionary<int, List<int>>();
            for (int index = 0; index < count; index++)
            {
                int root = unionFind.Find(index);
                if (!groups.TryGetValue(root, out List<int> group))
                {
                    group = new List<int>();
                    groups.Add(root, group);
                }
                group.Add(index);
            }
            return groups;
        }

        private static HashSet<int> CollectVertices(MeshGeometry geometry, List<int> triangles)
        {
            var vertices = new HashSet<int>();
            foreach (int triangle in triangles)
            {
                int index = triangle * 3;
                vertices.Add(geometry.Triangles[index]);
                vertices.Add(geometry.Triangles[index + 1]);
                vertices.Add(geometry.Triangles[index + 2]);
            }
            return vertices;
        }

        private static Vector3 MostStablePerpendicularAxis(Vector3 normal)
        {
            Vector3[] candidates = { Vector3.right, Vector3.up, Vector3.forward };
            Vector3 best = Vector3.zero;
            float magnitude = -1f;
            foreach (Vector3 candidate in candidates)
            {
                Vector3 projected = candidate - Vector3.Dot(candidate, normal) * normal;
                if (projected.sqrMagnitude <= magnitude)
                    continue;
                best = projected;
                magnitude = projected.sqrMagnitude;
            }
            return best.normalized;
        }

        private static Vector3 MakeDirectionDeterministic(Vector3 direction)
        {
            Vector3 absolute = new Vector3(
                Mathf.Abs(direction.x),
                Mathf.Abs(direction.y),
                Mathf.Abs(direction.z));
            float dominant = absolute.x >= absolute.y && absolute.x >= absolute.z
                ? direction.x
                : absolute.y >= absolute.z ? direction.y : direction.z;
            return dominant < 0f ? -direction : direction;
        }
    }
}
