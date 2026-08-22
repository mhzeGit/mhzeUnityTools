using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gigaduck.AutoBoxCollider
{
    internal delegate bool AnalysisProgress(float progress, string stage);

    internal static class MeshColliderAnalyzer
    {
        private readonly struct TriangleBounds
        {
            public readonly Vector3 Minimum;
            public readonly Vector3 Maximum;

            public TriangleBounds(Vector3 minimum, Vector3 maximum)
            {
                Minimum = minimum;
                Maximum = maximum;
            }
        }

        public static bool HasMeshSource(GameObject target, bool includeChildren)
        {
            if (target == null)
                return false;

            if (includeChildren)
            {
                return target.GetComponentInChildren<MeshFilter>(true) != null
                    || target.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
            }

            return target.GetComponent<MeshFilter>() != null
                || target.GetComponent<SkinnedMeshRenderer>() != null;
        }

        public static bool TryBuildGeometry(
            GameObject target,
            bool includeChildren,
            out MeshGeometry geometry,
            out string error)
        {
            geometry = new MeshGeometry();
            error = null;

            if (target == null)
            {
                error = "Choose a target GameObject first.";
                return false;
            }

            try
            {
                MeshFilter[] filters = includeChildren
                    ? target.GetComponentsInChildren<MeshFilter>(true)
                    : target.GetComponents<MeshFilter>();

                foreach (MeshFilter filter in filters)
                {
                    if (filter == null || filter.sharedMesh == null)
                        continue;

                    Matrix4x4 sourceToTarget = target.transform.worldToLocalMatrix
                        * filter.transform.localToWorldMatrix;
                    AppendMesh(filter.sharedMesh, sourceToTarget, geometry);
                    AddSourceHash(geometry, filter.sharedMesh, sourceToTarget);
                    geometry.SourceCount++;
                }

                SkinnedMeshRenderer[] skinnedRenderers = includeChildren
                    ? target.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    : target.GetComponents<SkinnedMeshRenderer>();

                foreach (SkinnedMeshRenderer renderer in skinnedRenderers)
                {
                    if (renderer == null || renderer.sharedMesh == null)
                        continue;

                    var bakedMesh = new Mesh { name = renderer.sharedMesh.name + " Collider Bake" };
                    try
                    {
                        renderer.BakeMesh(bakedMesh);
                        Matrix4x4 sourceToTarget = target.transform.worldToLocalMatrix
                            * renderer.transform.localToWorldMatrix;
                        AppendMesh(bakedMesh, sourceToTarget, geometry);
                        AddSourceHash(geometry, renderer.sharedMesh, sourceToTarget);
                        geometry.SourceHash = CombineHash(geometry.SourceHash, bakedMesh.vertexCount);
                        geometry.SourceCount++;
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(bakedMesh);
                    }
                }
            }
            catch (Exception exception)
            {
                error = "Could not read the mesh data: " + exception.Message;
                return false;
            }

            if (geometry.SourceCount == 0)
            {
                error = includeChildren
                    ? "The target or its children do not contain a usable MeshFilter or SkinnedMeshRenderer."
                    : "The target does not contain a usable MeshFilter or SkinnedMeshRenderer.";
                return false;
            }

            if (geometry.Vertices.Count == 0 || geometry.Triangles.Count < 3)
            {
                error = "The selected mesh sources contain no triangle geometry.";
                return false;
            }

            MeshTopologyAnalyzer.Analyze(geometry);
            geometry.Description = geometry.SourceCount == 1
                ? "1 mesh source"
                : geometry.SourceCount + " mesh sources";
            return true;
        }

        public static ColliderAnalysisResult Analyze(
            GameObject target,
            MeshGeometry geometry,
            ColliderAnalysisSettings settings,
            AnalysisProgress progress)
        {
            if (geometry == null || geometry.TriangleCount == 0)
                throw new ArgumentException("Geometry must contain triangles.", nameof(geometry));

            settings.Validate();
            AnalysisFrame objectFrame = new AnalysisFrame(Vector3.zero, Quaternion.identity, "Object axes");
            AnalysisFrame principalFrame = CalculatePrincipalFrame(geometry);

            ColliderAnalysisResult result;
            switch (settings.alignment)
            {
                case ColliderAlignment.ObjectAxes:
                    result = AnalyzeFrame(geometry, settings, objectFrame, progress, 0f, 1f);
                    break;

                case ColliderAlignment.PrincipalAxes:
                    result = AnalyzeFrame(geometry, settings, principalFrame, progress, 0f, 1f);
                    break;

                default:
                    ColliderAnalysisResult objectResult = AnalyzeFrame(
                        geometry, settings, objectFrame, progress, 0f, 0.5f);
                    ColliderAnalysisResult principalResult = AnalyzeFrame(
                        geometry, settings, principalFrame, progress, 0.5f, 0.5f);
                    result = IsBetter(principalResult, objectResult, settings)
                        ? principalResult
                        : objectResult;
                    break;
            }

            result.Target = target;
            result.SettingsHash = settings.GetStableHash();
            result.SourceHash = geometry.SourceHash;
            result.VertexCount = geometry.Vertices.Count;
            result.TriangleCount = geometry.TriangleCount;
            result.SourceDescription = geometry.Description;
            result.ConnectedComponentCount = geometry.ConnectedComponentCount;
            result.BoundaryEdgeCount = geometry.BoundaryEdgeCount;
            result.NonManifoldEdgeCount = geometry.NonManifoldEdgeCount;
            result.IsWatertight = geometry.IsWatertight;
            return result;
        }

        private static ColliderAnalysisResult AnalyzeFrame(
            MeshGeometry geometry,
            ColliderAnalysisSettings settings,
            AnalysisFrame frame,
            AnalysisProgress progress,
            float progressOffset,
            float progressScale)
        {
            bool RemappedProgress(float value, string stage)
            {
                return progress != null && progress(progressOffset + value * progressScale, stage);
            }

            VoxelGrid grid = Voxelize(geometry, settings, frame, RemappedProgress);
            OrientedBoxFitResult orientedFit = settings.detectAngledSurfaces
                ? OrientedBoxFitter.Fit(geometry, grid, frame, settings)
                : new OrientedBoxFitResult
                {
                    CoveredOccupied = new bool[grid.Occupied.Length]
                };
            VoxelGrid residualGrid = CreateResidualGrid(grid, orientedFit.CoveredOccupied);
            int residualOccupied = CountTrue(residualGrid.Occupied);
            int residualBudget = Mathf.Max(1, settings.maximumColliders - orientedFit.Boxes.Count);
            BoxDecompositionResult decomposition = residualOccupied > 0
                ? BoxDecomposer.Decompose(
                    residualGrid,
                    settings.emptySpaceTolerance,
                    residualBudget,
                    RemappedProgress)
                : new BoxDecompositionResult
                {
                    Boxes = new List<IntBox>(),
                    OccupiedCoverage = 1f,
                    ProtectedVoidVoxelCount = CountTrue(grid.ProtectedVoid)
                };

            float padding = settings.paddingInVoxels * grid.VoxelSize;
            var colliderBoxes = new List<ColliderBox>(
                orientedFit.Boxes.Count + decomposition.Boxes.Count);
            colliderBoxes.AddRange(orientedFit.Boxes);
            var frameVertices = new Vector3[geometry.Vertices.Count];
            Quaternion inverseRotation = Quaternion.Inverse(frame.Rotation);
            for (int i = 0; i < frameVertices.Length; i++)
                frameVertices[i] = inverseRotation * (geometry.Vertices[i] - frame.Origin);
            var triangleBounds = new TriangleBounds[geometry.TriangleCount];
            for (int triangleIndex = 0; triangleIndex < geometry.TriangleCount; triangleIndex++)
            {
                int index = triangleIndex * 3;
                Vector3 a = frameVertices[geometry.Triangles[index]];
                Vector3 b = frameVertices[geometry.Triangles[index + 1]];
                Vector3 c = frameVertices[geometry.Triangles[index + 2]];
                triangleBounds[triangleIndex] = new TriangleBounds(
                    Vector3.Min(a, Vector3.Min(b, c)),
                    Vector3.Max(a, Vector3.Max(b, c)));
            }
            foreach (IntBox box in decomposition.Boxes)
            {
                ColliderBox frameBox = CreateRefinedColliderBox(
                    box, grid, triangleBounds, padding);
                colliderBoxes.Add(new ColliderBox(
                    frame.Origin + frame.Rotation * frameBox.Center,
                    frameBox.Size,
                    frame.Rotation));
            }

            int surfaceCount = 0;
            int occupiedCount = 0;
            for (int i = 0; i < grid.Occupied.Length; i++)
            {
                if (grid.Surface[i])
                    surfaceCount++;
                if (grid.Occupied[i])
                    occupiedCount++;
            }

            float residualCoveredCells = residualOccupied > 0
                ? residualOccupied / Mathf.Max(0.0001f, 1f - decomposition.EmptyVolumeFraction)
                : 0f;
            float approximateCoveredCells = orientedFit.CoveredCount
                + orientedFit.EmptyCellCount
                + residualCoveredCells;
            float approximateEmptyCells = orientedFit.EmptyCellCount
                + Mathf.Max(0f, residualCoveredCells - residualOccupied);
            AnalysisFrame resultFrame = orientedFit.Boxes.Count > 0
                ? new AnalysisFrame(
                    frame.Origin,
                    frame.Rotation,
                    frame.Name + " + local planar fits")
                : frame;

            return new ColliderAnalysisResult
            {
                Frame = resultFrame,
                Boxes = colliderBoxes,
                GridX = grid.SizeX,
                GridY = grid.SizeY,
                GridZ = grid.SizeZ,
                SurfaceVoxelCount = surfaceCount,
                OccupiedVoxelCount = occupiedCount,
                OccupiedCoverage = 1f,
                EmptyVolumeFraction = approximateCoveredCells > 0f
                    ? approximateEmptyCells / approximateCoveredCells
                    : 0f,
                WorstBoxError = decomposition.WorstBoxError,
                VoxelSize = grid.VoxelSize,
                BudgetLimited = decomposition.BudgetLimited,
                ColliderBudgetExceeded = decomposition.ColliderBudgetExceeded
                    || colliderBoxes.Count > settings.maximumColliders,
                ProtectedVoidVoxelCount = decomposition.ProtectedVoidVoxelCount,
                UnresolvedProtectedVoidCount = decomposition.UnresolvedProtectedVoidCount,
                OrientedBoxCount = orientedFit.Boxes.Count
            };
        }

        private static VoxelGrid CreateResidualGrid(VoxelGrid source, bool[] coveredOccupied)
        {
            var residual = new VoxelGrid(
                source.SizeX,
                source.SizeY,
                source.SizeZ,
                source.VoxelSize,
                source.MinCorner);
            Array.Copy(source.Surface, residual.Surface, source.Surface.Length);
            Array.Copy(source.ProtectedVoid, residual.ProtectedVoid, source.ProtectedVoid.Length);
            for (int i = 0; i < source.Occupied.Length; i++)
                residual.Occupied[i] = source.Occupied[i] && !coveredOccupied[i];
            return residual;
        }

        private static int CountTrue(bool[] values)
        {
            int count = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i])
                    count++;
            }
            return count;
        }

        private static ColliderBox CreateRefinedColliderBox(
            IntBox box,
            VoxelGrid grid,
            TriangleBounds[] triangles,
            float padding)
        {
            Vector3 minimum = grid.MinCorner + new Vector3(
                box.MinX, box.MinY, box.MinZ) * grid.VoxelSize;
            Vector3 maximum = grid.MinCorner + new Vector3(
                box.MaxX, box.MaxY, box.MaxZ) * grid.VoxelSize;

            for (int axis = 0; axis < 3; axis++)
            {
                if (FaceBordersEmptySpace(grid, box, axis, false)
                    && TryFindExactFace(
                        triangles,
                        minimum,
                        maximum,
                        axis,
                        false,
                        grid.VoxelSize,
                        out float exactMinimum))
                {
                    minimum[axis] = Mathf.Clamp(
                        exactMinimum,
                        minimum[axis],
                        minimum[axis] + grid.VoxelSize);
                }

                if (FaceBordersEmptySpace(grid, box, axis, true)
                    && TryFindExactFace(
                        triangles,
                        minimum,
                        maximum,
                        axis,
                        true,
                        grid.VoxelSize,
                        out float exactMaximum))
                {
                    maximum[axis] = Mathf.Clamp(
                        exactMaximum,
                        maximum[axis] - grid.VoxelSize,
                        maximum[axis]);
                }
            }

            minimum -= Vector3.one * padding;
            maximum += Vector3.one * padding;
            Vector3 size = maximum - minimum;
            size.x = Mathf.Max(size.x, grid.VoxelSize * 0.1f);
            size.y = Mathf.Max(size.y, grid.VoxelSize * 0.1f);
            size.z = Mathf.Max(size.z, grid.VoxelSize * 0.1f);
            return new ColliderBox((minimum + maximum) * 0.5f, size, Quaternion.identity);
        }

        private static bool FaceBordersEmptySpace(
            VoxelGrid grid,
            IntBox box,
            int axis,
            bool positive)
        {
            int faceCoordinate = positive ? box.GetMax(axis) - 1 : box.GetMin(axis);
            int neighborCoordinate = positive ? faceCoordinate + 1 : faceCoordinate - 1;
            bool foundOccupiedFaceCell = false;

            for (int z = box.MinZ; z < box.MaxZ; z++)
            for (int y = box.MinY; y < box.MaxY; y++)
            for (int x = box.MinX; x < box.MaxX; x++)
            {
                int coordinate = axis == 0 ? x : axis == 1 ? y : z;
                if (coordinate != faceCoordinate || !grid.Occupied[grid.Index(x, y, z)])
                    continue;

                foundOccupiedFaceCell = true;
                int neighborX = axis == 0 ? neighborCoordinate : x;
                int neighborY = axis == 1 ? neighborCoordinate : y;
                int neighborZ = axis == 2 ? neighborCoordinate : z;
                if (neighborX >= 0 && neighborY >= 0 && neighborZ >= 0
                    && neighborX < grid.SizeX && neighborY < grid.SizeY && neighborZ < grid.SizeZ
                    && grid.Occupied[grid.Index(neighborX, neighborY, neighborZ)])
                    return false;
            }

            return foundOccupiedFaceCell;
        }

        private static bool TryFindExactFace(
            TriangleBounds[] triangles,
            Vector3 minimum,
            Vector3 maximum,
            int axis,
            bool positive,
            float voxelSize,
            out float coordinate)
        {
            float approximateFace = positive ? maximum[axis] : minimum[axis];
            float searchDistance = voxelSize * 1.25f;
            float best = positive ? float.NegativeInfinity : float.PositiveInfinity;
            bool found = false;

            int firstOtherAxis = axis == 0 ? 1 : 0;
            int secondOtherAxis = axis == 2 ? 1 : 2;
            for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex++)
            {
                Vector3 triangleMinimum = triangles[triangleIndex].Minimum;
                Vector3 triangleMaximum = triangles[triangleIndex].Maximum;
                float triangleAxisMinimum = triangleMinimum[axis];
                float triangleAxisMaximum = triangleMaximum[axis];
                if (triangleAxisMaximum < approximateFace - searchDistance
                    || triangleAxisMinimum > approximateFace + searchDistance)
                    continue;

                float firstMinimum = triangleMinimum[firstOtherAxis];
                float firstMaximum = triangleMaximum[firstOtherAxis];
                float secondMinimum = triangleMinimum[secondOtherAxis];
                float secondMaximum = triangleMaximum[secondOtherAxis];
                if (firstMaximum < minimum[firstOtherAxis] - voxelSize
                    || firstMinimum > maximum[firstOtherAxis] + voxelSize
                    || secondMaximum < minimum[secondOtherAxis] - voxelSize
                    || secondMinimum > maximum[secondOtherAxis] + voxelSize)
                    continue;

                float triangleCoordinate = positive ? triangleAxisMaximum : triangleAxisMinimum;
                best = positive ? Mathf.Max(best, triangleCoordinate) : Mathf.Min(best, triangleCoordinate);
                found = true;
            }

            coordinate = best;
            return found;
        }

        private static bool IsBetter(
            ColliderAnalysisResult candidate,
            ColliderAnalysisResult current,
            ColliderAnalysisSettings settings)
        {
            if (candidate.UnresolvedProtectedVoidCount != current.UnresolvedProtectedVoidCount)
                return candidate.UnresolvedProtectedVoidCount < current.UnresolvedProtectedVoidCount;

            if (candidate.ColliderBudgetExceeded != current.ColliderBudgetExceeded)
                return !candidate.ColliderBudgetExceeded;

            if (candidate.BudgetLimited != current.BudgetLimited)
                return !candidate.BudgetLimited;

            if (candidate.BudgetLimited)
            {
                float candidateViolation = Mathf.Max(
                    0f, candidate.WorstBoxError - settings.emptySpaceTolerance);
                float currentViolation = Mathf.Max(
                    0f, current.WorstBoxError - settings.emptySpaceTolerance);
                if (!Mathf.Approximately(candidateViolation, currentViolation))
                    return candidateViolation < currentViolation;
            }

            float candidateScore = candidate.Boxes.Count
                + candidate.EmptyVolumeFraction * 100f
                + candidate.WorstBoxError * 20f;
            float currentScore = current.Boxes.Count
                + current.EmptyVolumeFraction * 100f
                + current.WorstBoxError * 20f;
            return candidateScore < currentScore;
        }

        private static void AppendMesh(Mesh mesh, Matrix4x4 sourceToTarget, MeshGeometry geometry)
        {
            int vertexOffset = geometry.Vertices.Count;
            int triangleOffset = geometry.Triangles.Count;
            using (Mesh.MeshDataArray meshDataArray = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                Mesh.MeshData meshData = meshDataArray[0];
                using (var vertices = new NativeArray<Vector3>(
                    meshData.vertexCount,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory))
                {
                    meshData.GetVertices(vertices);
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 vertex = sourceToTarget.MultiplyPoint3x4(vertices[i]);
                        geometry.Vertices.Add(vertex);
                        geometry.SourceHash = CombineHash(geometry.SourceHash, vertex.x.GetHashCode());
                        geometry.SourceHash = CombineHash(geometry.SourceHash, vertex.y.GetHashCode());
                        geometry.SourceHash = CombineHash(geometry.SourceHash, vertex.z.GetHashCode());
                    }
                }

                if (meshData.indexFormat == IndexFormat.UInt16)
                {
                    NativeArray<ushort> indices = meshData.GetIndexData<ushort>();
                    AppendSubMeshes(meshData, indices, vertexOffset, geometry.Triangles);
                }
                else
                {
                    NativeArray<uint> indices = meshData.GetIndexData<uint>();
                    AppendSubMeshes(meshData, indices, vertexOffset, geometry.Triangles);
                }
            }

            for (int i = triangleOffset; i < geometry.Triangles.Count; i++)
                geometry.SourceHash = CombineHash(geometry.SourceHash, geometry.Triangles[i] - vertexOffset);
        }

        private static void AppendSubMeshes(
            Mesh.MeshData meshData,
            NativeArray<ushort> indices,
            int vertexOffset,
            List<int> destination)
        {
            for (int subMeshIndex = 0; subMeshIndex < meshData.subMeshCount; subMeshIndex++)
            {
                SubMeshDescriptor subMesh = meshData.GetSubMesh(subMeshIndex);
                if (subMesh.topology != MeshTopology.Triangles)
                    continue;

                int end = subMesh.indexStart + subMesh.indexCount;
                for (int i = subMesh.indexStart; i + 2 < end; i += 3)
                {
                    destination.Add(vertexOffset + subMesh.baseVertex + indices[i]);
                    destination.Add(vertexOffset + subMesh.baseVertex + indices[i + 1]);
                    destination.Add(vertexOffset + subMesh.baseVertex + indices[i + 2]);
                }
            }
        }

        private static void AppendSubMeshes(
            Mesh.MeshData meshData,
            NativeArray<uint> indices,
            int vertexOffset,
            List<int> destination)
        {
            for (int subMeshIndex = 0; subMeshIndex < meshData.subMeshCount; subMeshIndex++)
            {
                SubMeshDescriptor subMesh = meshData.GetSubMesh(subMeshIndex);
                if (subMesh.topology != MeshTopology.Triangles)
                    continue;

                int end = subMesh.indexStart + subMesh.indexCount;
                for (int i = subMesh.indexStart; i + 2 < end; i += 3)
                {
                    destination.Add(vertexOffset + subMesh.baseVertex + (int)indices[i]);
                    destination.Add(vertexOffset + subMesh.baseVertex + (int)indices[i + 1]);
                    destination.Add(vertexOffset + subMesh.baseVertex + (int)indices[i + 2]);
                }
            }
        }

        private static VoxelGrid Voxelize(
            MeshGeometry geometry,
            ColliderAnalysisSettings settings,
            AnalysisFrame frame,
            AnalysisProgress progress)
        {
            if (progress != null && progress(0.01f, "Transforming mesh into analysis space"))
                throw new OperationCanceledException();

            Quaternion inverseRotation = Quaternion.Inverse(frame.Rotation);
            var vertices = new Vector3[geometry.Vertices.Count];
            Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertex = inverseRotation * (geometry.Vertices[i] - frame.Origin);
                vertices[i] = vertex;
                min = Vector3.Min(min, vertex);
                max = Vector3.Max(max, vertex);
            }

            Vector3 dimensions = max - min;
            float longestDimension = Mathf.Max(dimensions.x, Mathf.Max(dimensions.y, dimensions.z));
            if (longestDimension <= 1e-6f)
                throw new InvalidOperationException("The mesh bounds have no measurable volume.");

            float voxelSize = longestDimension / settings.voxelResolution;
            int sizeX = Mathf.Max(1, Mathf.CeilToInt(dimensions.x / voxelSize)) + 3;
            int sizeY = Mathf.Max(1, Mathf.CeilToInt(dimensions.y / voxelSize)) + 3;
            int sizeZ = Mathf.Max(1, Mathf.CeilToInt(dimensions.z / voxelSize)) + 3;
            Vector3 minCorner = min - Vector3.one * (voxelSize * 1.5f);
            var grid = new VoxelGrid(sizeX, sizeY, sizeZ, voxelSize, minCorner);
            Vector3 halfVoxel = Vector3.one * (voxelSize * 0.5f);
            float rangeEpsilon = voxelSize * 0.0001f;

            int triangleCount = geometry.TriangleCount;
            for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                if ((triangleIndex & 255) == 0 && progress != null)
                {
                    float value = 0.05f + 0.62f * triangleIndex / Mathf.Max(1f, triangleCount);
                    if (progress(value, "Conservative triangle voxelization"))
                        throw new OperationCanceledException();
                }

                int baseIndex = triangleIndex * 3;
                Vector3 a = vertices[geometry.Triangles[baseIndex]];
                Vector3 b = vertices[geometry.Triangles[baseIndex + 1]];
                Vector3 c = vertices[geometry.Triangles[baseIndex + 2]];
                Vector3 triangleMin = Vector3.Min(a, Vector3.Min(b, c)) - Vector3.one * rangeEpsilon;
                Vector3 triangleMax = Vector3.Max(a, Vector3.Max(b, c)) + Vector3.one * rangeEpsilon;

                int minX = Mathf.Clamp(Mathf.FloorToInt((triangleMin.x - minCorner.x) / voxelSize), 0, sizeX - 1);
                int minY = Mathf.Clamp(Mathf.FloorToInt((triangleMin.y - minCorner.y) / voxelSize), 0, sizeY - 1);
                int minZ = Mathf.Clamp(Mathf.FloorToInt((triangleMin.z - minCorner.z) / voxelSize), 0, sizeZ - 1);
                int maxX = Mathf.Clamp(Mathf.FloorToInt((triangleMax.x - minCorner.x) / voxelSize), 0, sizeX - 1);
                int maxY = Mathf.Clamp(Mathf.FloorToInt((triangleMax.y - minCorner.y) / voxelSize), 0, sizeY - 1);
                int maxZ = Mathf.Clamp(Mathf.FloorToInt((triangleMax.z - minCorner.z) / voxelSize), 0, sizeZ - 1);

                for (int z = minZ; z <= maxZ; z++)
                for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    int index = grid.Index(x, y, z);
                    if (grid.Surface[index])
                        continue;

                    if (TriangleIntersectsBox(a, b, c, grid.CellCenter(x, y, z), halfVoxel))
                        grid.Surface[index] = true;
                }
            }

            if (progress != null && progress(0.7f, "Flood filling enclosed mesh volume"))
                throw new OperationCanceledException();

            FillOccupancy(grid, settings.fillEnclosedVolume && geometry.IsWatertight);
            if (settings.preserveHolesAndOpenings)
                MarkProtectedVoids(grid);
            if (progress != null && progress(0.75f, "Classifying topology and protected negative space"))
                throw new OperationCanceledException();

            return grid;
        }

        private static void FillOccupancy(VoxelGrid grid, bool fillEnclosedVolume)
        {
            if (!fillEnclosedVolume)
            {
                Array.Copy(grid.Surface, grid.Occupied, grid.Surface.Length);
                return;
            }

            var exterior = new bool[grid.Surface.Length];
            var queue = new Queue<int>();

            for (int z = 0; z < grid.SizeZ; z++)
            for (int y = 0; y < grid.SizeY; y++)
            {
                EnqueueExterior(grid, exterior, queue, 0, y, z);
                EnqueueExterior(grid, exterior, queue, grid.SizeX - 1, y, z);
            }

            for (int z = 0; z < grid.SizeZ; z++)
            for (int x = 0; x < grid.SizeX; x++)
            {
                EnqueueExterior(grid, exterior, queue, x, 0, z);
                EnqueueExterior(grid, exterior, queue, x, grid.SizeY - 1, z);
            }

            for (int y = 0; y < grid.SizeY; y++)
            for (int x = 0; x < grid.SizeX; x++)
            {
                EnqueueExterior(grid, exterior, queue, x, y, 0);
                EnqueueExterior(grid, exterior, queue, x, y, grid.SizeZ - 1);
            }

            int slice = grid.SizeX * grid.SizeY;
            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int z = index / slice;
                int remainder = index - z * slice;
                int y = remainder / grid.SizeX;
                int x = remainder - y * grid.SizeX;

                EnqueueExterior(grid, exterior, queue, x - 1, y, z);
                EnqueueExterior(grid, exterior, queue, x + 1, y, z);
                EnqueueExterior(grid, exterior, queue, x, y - 1, z);
                EnqueueExterior(grid, exterior, queue, x, y + 1, z);
                EnqueueExterior(grid, exterior, queue, x, y, z - 1);
                EnqueueExterior(grid, exterior, queue, x, y, z + 1);
            }

            for (int i = 0; i < grid.Occupied.Length; i++)
                grid.Occupied[i] = grid.Surface[i] || !exterior[i];
        }

        internal static void MarkProtectedVoids(VoxelGrid grid)
        {
            for (int z = 0; z < grid.SizeZ; z++)
            for (int y = 0; y < grid.SizeY; y++)
            {
                int first = -1;
                int last = -1;
                for (int x = 0; x < grid.SizeX; x++)
                {
                    if (!grid.Occupied[grid.Index(x, y, z)])
                        continue;
                    if (first < 0)
                        first = x;
                    last = x;
                }
                for (int x = first + 1; x < last; x++)
                {
                    int index = grid.Index(x, y, z);
                    if (!grid.Occupied[index])
                        grid.ProtectedVoid[index] = true;
                }
            }

            for (int z = 0; z < grid.SizeZ; z++)
            for (int x = 0; x < grid.SizeX; x++)
            {
                int first = -1;
                int last = -1;
                for (int y = 0; y < grid.SizeY; y++)
                {
                    if (!grid.Occupied[grid.Index(x, y, z)])
                        continue;
                    if (first < 0)
                        first = y;
                    last = y;
                }
                for (int y = first + 1; y < last; y++)
                {
                    int index = grid.Index(x, y, z);
                    if (!grid.Occupied[index])
                        grid.ProtectedVoid[index] = true;
                }
            }

            for (int y = 0; y < grid.SizeY; y++)
            for (int x = 0; x < grid.SizeX; x++)
            {
                int first = -1;
                int last = -1;
                for (int z = 0; z < grid.SizeZ; z++)
                {
                    if (!grid.Occupied[grid.Index(x, y, z)])
                        continue;
                    if (first < 0)
                        first = z;
                    last = z;
                }
                for (int z = first + 1; z < last; z++)
                {
                    int index = grid.Index(x, y, z);
                    if (!grid.Occupied[index])
                        grid.ProtectedVoid[index] = true;
                }
            }
        }

        private static void EnqueueExterior(
            VoxelGrid grid,
            bool[] exterior,
            Queue<int> queue,
            int x,
            int y,
            int z)
        {
            if (x < 0 || y < 0 || z < 0
                || x >= grid.SizeX || y >= grid.SizeY || z >= grid.SizeZ)
                return;

            int index = grid.Index(x, y, z);
            if (exterior[index] || grid.Surface[index])
                return;

            exterior[index] = true;
            queue.Enqueue(index);
        }

        private static bool TriangleIntersectsBox(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 boxCenter,
            Vector3 boxHalfSize)
        {
            Vector3 v0 = a - boxCenter;
            Vector3 v1 = b - boxCenter;
            Vector3 v2 = c - boxCenter;
            Vector3 e0 = v1 - v0;
            Vector3 e1 = v2 - v1;
            Vector3 e2 = v0 - v2;

            if (!OverlapsOnAxis(v0, v1, v2, Vector3.Cross(e0, Vector3.right), boxHalfSize)
                || !OverlapsOnAxis(v0, v1, v2, Vector3.Cross(e0, Vector3.up), boxHalfSize)
                || !OverlapsOnAxis(v0, v1, v2, Vector3.Cross(e0, Vector3.forward), boxHalfSize)
                || !OverlapsOnAxis(v0, v1, v2, Vector3.Cross(e1, Vector3.right), boxHalfSize)
                || !OverlapsOnAxis(v0, v1, v2, Vector3.Cross(e1, Vector3.up), boxHalfSize)
                || !OverlapsOnAxis(v0, v1, v2, Vector3.Cross(e1, Vector3.forward), boxHalfSize)
                || !OverlapsOnAxis(v0, v1, v2, Vector3.Cross(e2, Vector3.right), boxHalfSize)
                || !OverlapsOnAxis(v0, v1, v2, Vector3.Cross(e2, Vector3.up), boxHalfSize)
                || !OverlapsOnAxis(v0, v1, v2, Vector3.Cross(e2, Vector3.forward), boxHalfSize))
                return false;

            if (Mathf.Min(v0.x, Mathf.Min(v1.x, v2.x)) > boxHalfSize.x
                || Mathf.Max(v0.x, Mathf.Max(v1.x, v2.x)) < -boxHalfSize.x
                || Mathf.Min(v0.y, Mathf.Min(v1.y, v2.y)) > boxHalfSize.y
                || Mathf.Max(v0.y, Mathf.Max(v1.y, v2.y)) < -boxHalfSize.y
                || Mathf.Min(v0.z, Mathf.Min(v1.z, v2.z)) > boxHalfSize.z
                || Mathf.Max(v0.z, Mathf.Max(v1.z, v2.z)) < -boxHalfSize.z)
                return false;

            return OverlapsOnAxis(v0, v1, v2, Vector3.Cross(e0, e1), boxHalfSize);
        }

        private static bool OverlapsOnAxis(
            Vector3 v0,
            Vector3 v1,
            Vector3 v2,
            Vector3 axis,
            Vector3 halfSize)
        {
            if (axis.sqrMagnitude < 1e-12f)
                return true;

            float p0 = Vector3.Dot(v0, axis);
            float p1 = Vector3.Dot(v1, axis);
            float p2 = Vector3.Dot(v2, axis);
            float radius = halfSize.x * Mathf.Abs(axis.x)
                + halfSize.y * Mathf.Abs(axis.y)
                + halfSize.z * Mathf.Abs(axis.z);
            float min = Mathf.Min(p0, Mathf.Min(p1, p2));
            float max = Mathf.Max(p0, Mathf.Max(p1, p2));
            return min <= radius && max >= -radius;
        }

        internal static AnalysisFrame CalculatePrincipalFrame(MeshGeometry geometry)
        {
            double totalArea = 0d;
            var firstMoment = new double[3];
            var secondMoment = new double[3, 3];

            for (int triangleIndex = 0; triangleIndex < geometry.TriangleCount; triangleIndex++)
            {
                int index = triangleIndex * 3;
                Vector3 a = geometry.Vertices[geometry.Triangles[index]];
                Vector3 b = geometry.Vertices[geometry.Triangles[index + 1]];
                Vector3 c = geometry.Vertices[geometry.Triangles[index + 2]];
                double area = Vector3.Cross(b - a, c - a).magnitude * 0.5d;
                if (area <= 1e-12d)
                    continue;

                totalArea += area;
                firstMoment[0] += area * (a.x + b.x + c.x) / 3d;
                firstMoment[1] += area * (a.y + b.y + c.y) / 3d;
                firstMoment[2] += area * (a.z + b.z + c.z) / 3d;

                double[] av = { a.x, a.y, a.z };
                double[] bv = { b.x, b.y, b.z };
                double[] cv = { c.x, c.y, c.z };
                for (int row = 0; row < 3; row++)
                for (int column = row; column < 3; column++)
                {
                    double diagonal = av[row] * av[column]
                        + bv[row] * bv[column]
                        + cv[row] * cv[column];
                    double cross = av[row] * bv[column] + bv[row] * av[column]
                        + av[row] * cv[column] + cv[row] * av[column]
                        + bv[row] * cv[column] + cv[row] * bv[column];
                    double moment = area * (diagonal / 6d + cross / 12d);
                    secondMoment[row, column] += moment;
                    if (row != column)
                        secondMoment[column, row] += moment;
                }
            }

            if (totalArea <= 1e-12d)
                return new AnalysisFrame(Vector3.zero, Quaternion.identity, "Principal axes (fallback)");

            var mean = new double[3];
            for (int axis = 0; axis < 3; axis++)
                mean[axis] = firstMoment[axis] / totalArea;

            var covariance = new double[3, 3];
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
                covariance[row, column] = secondMoment[row, column] / totalArea - mean[row] * mean[column];

            Vector3[] eigenvectors = JacobiEigenvectors(covariance, out double[] eigenvalues);
            int[] order = { 0, 1, 2 };
            Array.Sort(order, (left, right) => eigenvalues[right].CompareTo(eigenvalues[left]));

            double largestEigenvalue = Math.Max(Math.Abs(eigenvalues[order[0]]), 1e-12d);
            bool largestPairDegenerate = Math.Abs(eigenvalues[order[0]] - eigenvalues[order[1]])
                / largestEigenvalue < 0.0001d;
            bool smallestPairDegenerate = Math.Abs(eigenvalues[order[1]] - eigenvalues[order[2]])
                / largestEigenvalue < 0.0001d;
            if (largestPairDegenerate && smallestPairDegenerate)
                return new AnalysisFrame(Vector3.zero, Quaternion.identity, "Object axes (symmetric PCA)");

            Vector3 axisX;
            Vector3 axisY;
            Vector3 axisZ;
            string frameName = "Principal axes";

            if (largestPairDegenerate)
            {
                axisZ = MakeDirectionDeterministic(eigenvectors[order[2]].normalized);
                axisX = MostStablePerpendicularAxis(axisZ);
                axisY = Vector3.Cross(axisZ, axisX).normalized;
                frameName = "Principal axes (stabilized plane)";
            }
            else if (smallestPairDegenerate)
            {
                axisX = MakeDirectionDeterministic(eigenvectors[order[0]].normalized);
                axisY = MostStablePerpendicularAxis(axisX);
                axisZ = Vector3.Cross(axisX, axisY).normalized;
                axisY = Vector3.Cross(axisZ, axisX).normalized;
                frameName = "Principal axes (stabilized axis)";
            }
            else
            {
                axisX = MakeDirectionDeterministic(eigenvectors[order[0]].normalized);
                axisY = eigenvectors[order[1]] - Vector3.Dot(eigenvectors[order[1]], axisX) * axisX;
                if (axisY.sqrMagnitude < 1e-8f)
                    axisY = MostStablePerpendicularAxis(axisX);
                axisY.Normalize();
                axisZ = Vector3.Cross(axisX, axisY).normalized;
                axisY = Vector3.Cross(axisZ, axisX).normalized;
            }

            Quaternion rotation = Quaternion.LookRotation(axisZ, axisY);
            Vector3 origin = new Vector3((float)mean[0], (float)mean[1], (float)mean[2]);
            return new AnalysisFrame(origin, rotation, frameName);
        }

        private static Vector3 MostStablePerpendicularAxis(Vector3 normal)
        {
            Vector3[] candidates = { Vector3.right, Vector3.up, Vector3.forward };
            Vector3 best = Vector3.zero;
            float bestMagnitude = -1f;
            foreach (Vector3 candidate in candidates)
            {
                Vector3 projected = candidate - Vector3.Dot(candidate, normal) * normal;
                if (projected.sqrMagnitude <= bestMagnitude)
                    continue;
                best = projected;
                bestMagnitude = projected.sqrMagnitude;
            }
            return MakeDirectionDeterministic(best.normalized);
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

        private static Vector3[] JacobiEigenvectors(double[,] matrix, out double[] eigenvalues)
        {
            var a = (double[,])matrix.Clone();
            var vectors = new double[3, 3]
            {
                { 1d, 0d, 0d },
                { 0d, 1d, 0d },
                { 0d, 0d, 1d }
            };

            for (int iteration = 0; iteration < 32; iteration++)
            {
                int p = 0;
                int q = 1;
                double largest = Math.Abs(a[0, 1]);
                if (Math.Abs(a[0, 2]) > largest)
                {
                    p = 0;
                    q = 2;
                    largest = Math.Abs(a[0, 2]);
                }
                if (Math.Abs(a[1, 2]) > largest)
                {
                    p = 1;
                    q = 2;
                    largest = Math.Abs(a[1, 2]);
                }
                if (largest < 1e-12d)
                    break;

                double angle = 0.5d * Math.Atan2(2d * a[p, q], a[q, q] - a[p, p]);
                double cosine = Math.Cos(angle);
                double sine = Math.Sin(angle);
                double app = cosine * cosine * a[p, p] - 2d * sine * cosine * a[p, q]
                    + sine * sine * a[q, q];
                double aqq = sine * sine * a[p, p] + 2d * sine * cosine * a[p, q]
                    + cosine * cosine * a[q, q];

                for (int k = 0; k < 3; k++)
                {
                    if (k == p || k == q)
                        continue;
                    double akp = cosine * a[k, p] - sine * a[k, q];
                    double akq = sine * a[k, p] + cosine * a[k, q];
                    a[k, p] = a[p, k] = akp;
                    a[k, q] = a[q, k] = akq;
                }

                a[p, p] = app;
                a[q, q] = aqq;
                a[p, q] = a[q, p] = 0d;

                for (int k = 0; k < 3; k++)
                {
                    double vkp = cosine * vectors[k, p] - sine * vectors[k, q];
                    double vkq = sine * vectors[k, p] + cosine * vectors[k, q];
                    vectors[k, p] = vkp;
                    vectors[k, q] = vkq;
                }
            }

            eigenvalues = new[] { a[0, 0], a[1, 1], a[2, 2] };
            return new[]
            {
                new Vector3((float)vectors[0, 0], (float)vectors[1, 0], (float)vectors[2, 0]),
                new Vector3((float)vectors[0, 1], (float)vectors[1, 1], (float)vectors[2, 1]),
                new Vector3((float)vectors[0, 2], (float)vectors[1, 2], (float)vectors[2, 2])
            };
        }

        private static void AddSourceHash(MeshGeometry geometry, Mesh mesh, Matrix4x4 matrix)
        {
#if UNITY_6000_0_OR_NEWER
            geometry.SourceHash = CombineHash(geometry.SourceHash, mesh.GetEntityId().GetHashCode());
#else
            geometry.SourceHash = CombineHash(geometry.SourceHash, mesh.GetInstanceID());
#endif
            geometry.SourceHash = CombineHash(geometry.SourceHash, mesh.vertexCount);
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                geometry.SourceHash = CombineHash(geometry.SourceHash, matrix[row, column].GetHashCode());
        }

        private static int CombineHash(int hash, int value)
        {
            unchecked
            {
                return hash * 31 + value;
            }
        }
    }
}
