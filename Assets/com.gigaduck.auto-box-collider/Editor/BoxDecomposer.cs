using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gigaduck.AutoBoxCollider
{
    internal sealed class BoxDecompositionResult
    {
        public List<IntBox> Boxes;
        public float OccupiedCoverage;
        public float EmptyVolumeFraction;
        public float WorstBoxError;
        public bool BudgetLimited;
        public bool ColliderBudgetExceeded;
        public int ProtectedVoidVoxelCount;
        public int UnresolvedProtectedVoidCount;
    }

    internal static class BoxDecomposer
    {
        private sealed class VolumeNode
        {
            public IntBox Bounds;
            public int Occupied;
            public int Empty;
            public float Error;
            public int ProtectedVoid;
            public bool SplitEvaluated;
            public bool HasSplit;
            public SplitCandidate BestSplit;

            public VolumeNode(IntBox bounds, int occupied, int protectedVoid)
            {
                Bounds = bounds;
                Occupied = occupied;
                Empty = bounds.Volume - occupied;
                Error = bounds.Volume > 0 ? Empty / (float)bounds.Volume : 0f;
                ProtectedVoid = protectedVoid;
            }
        }

        private readonly struct SplitCandidate
        {
            public readonly IntBox Left;
            public readonly IntBox Right;
            public readonly int LeftOccupied;
            public readonly int RightOccupied;
            public readonly int LeftProtectedVoid;
            public readonly int RightProtectedVoid;
            public readonly float Score;

            public SplitCandidate(
                IntBox left,
                IntBox right,
                int leftOccupied,
                int rightOccupied,
                int leftProtectedVoid,
                int rightProtectedVoid,
                float score)
            {
                Left = left;
                Right = right;
                LeftOccupied = leftOccupied;
                RightOccupied = rightOccupied;
                LeftProtectedVoid = leftProtectedVoid;
                RightProtectedVoid = rightProtectedVoid;
                Score = score;
            }
        }

        public static BoxDecompositionResult Decompose(
            VoxelGrid grid,
            float emptySpaceTolerance,
            int maximumColliders,
            AnalysisProgress progress)
        {
            var integral = new VolumeIntegral(grid, grid.Occupied);
            var protectedIntegral = new VolumeIntegral(grid, grid.ProtectedVoid);
            var completeBounds = new IntBox(0, 0, 0, grid.SizeX, grid.SizeY, grid.SizeZ);
            int totalOccupied = integral.Count(completeBounds);
            int totalProtectedVoid = protectedIntegral.Count(completeBounds);
            if (totalOccupied == 0)
                throw new InvalidOperationException("Voxelization produced no occupied cells.");

            IntBox rootBounds = integral.Trim(completeBounds);
            var nodes = new List<VolumeNode>
            {
                new VolumeNode(rootBounds, totalOccupied, protectedIntegral.Count(rootBounds))
            };

            bool mergedInPreviousPass;
            int optimizationPass = 0;
            do
            {
                SplitToTolerance(
                    nodes,
                    integral,
                    protectedIntegral,
                    emptySpaceTolerance,
                    maximumColliders,
                    progress);
                mergedInPreviousPass = MergeCompliant(
                    nodes, integral, protectedIntegral, emptySpaceTolerance);
                optimizationPass++;
            }
            while (mergedInPreviousPass
                && nodes.Count < maximumColliders
                && HasOverToleranceNode(nodes, emptySpaceTolerance)
                && optimizationPass < 8);

            if (progress != null && progress(0.97f, "Measuring collider coverage"))
                throw new OperationCanceledException();

            return BuildResult(
                grid,
                nodes,
                totalOccupied,
                totalProtectedVoid,
                emptySpaceTolerance,
                maximumColliders);
        }

        private static void SplitToTolerance(
            List<VolumeNode> nodes,
            VolumeIntegral integral,
            VolumeIntegral protectedIntegral,
            float tolerance,
            int maximumColliders,
            AnalysisProgress progress)
        {
            const int openingSafetyLimit = 512;
            while (nodes.Count < maximumColliders || HasProtectedVoidNode(nodes))
            {
                if (nodes.Count >= Mathf.Max(maximumColliders, openingSafetyLimit))
                    break;

                SplitCandidate best = default;
                int bestNodeIndex = -1;
                bool found = false;

                for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
                {
                    VolumeNode node = nodes[nodeIndex];
                    bool openingRequired = node.ProtectedVoid > 0;
                    bool detailRequired = nodes.Count < maximumColliders
                        && node.Error > tolerance + 1e-6f;
                    if (!openingRequired && !detailRequired)
                        continue;

                    if (!node.SplitEvaluated)
                    {
                        node.HasSplit = TryFindBestSplit(
                            node,
                            integral,
                            protectedIntegral,
                            out SplitCandidate candidate);
                        node.BestSplit = candidate;
                        node.SplitEvaluated = true;
                    }

                    if (!node.HasSplit)
                        continue;

                    if (!found || node.BestSplit.Score > best.Score)
                    {
                        best = node.BestSplit;
                        bestNodeIndex = nodeIndex;
                        found = true;
                    }
                }

                if (!found)
                    break;

                nodes.RemoveAt(bestNodeIndex);
                nodes.Add(new VolumeNode(
                    best.Left, best.LeftOccupied, best.LeftProtectedVoid));
                nodes.Add(new VolumeNode(
                    best.Right, best.RightOccupied, best.RightProtectedVoid));

                if ((nodes.Count & 7) == 0 && progress != null)
                {
                    float value = 0.76f + 0.19f * Mathf.Clamp01(
                        nodes.Count / (float)Mathf.Max(1, maximumColliders));
                    if (progress(value, "Topology-constrained box cover optimization"))
                        throw new OperationCanceledException();
                }
            }
        }

        private static bool TryFindBestSplit(
            VolumeNode node,
            VolumeIntegral integral,
            VolumeIntegral protectedIntegral,
            out SplitCandidate best)
        {
            best = default;
            bool found = false;

            for (int axis = 0; axis < 3; axis++)
            {
                int minimum = node.Bounds.GetMin(axis);
                int maximum = node.Bounds.GetMax(axis);
                for (int split = minimum + 1; split < maximum; split++)
                {
                    IntBox leftRaw = node.Bounds.WithAxisMax(axis, split);
                    IntBox rightRaw = node.Bounds.WithAxisMin(axis, split);
                    int leftOccupied = integral.Count(leftRaw);
                    int rightOccupied = node.Occupied - leftOccupied;
                    if (leftOccupied == 0 || rightOccupied == 0)
                        continue;

                    IntBox left = integral.Trim(leftRaw);
                    IntBox right = integral.Trim(rightRaw);
                    int leftProtected = protectedIntegral.Count(left);
                    int rightProtected = protectedIntegral.Count(right);
                    int emptyAfterSplit = left.Volume - leftOccupied
                        + right.Volume - rightOccupied;
                    int improvement = node.Empty - emptyAfterSplit;
                    int protectedImprovement = node.ProtectedVoid
                        - leftProtected - rightProtected;
                    if (improvement <= 0 && protectedImprovement <= 0 && node.ProtectedVoid == 0)
                        continue;

                    float balance = Mathf.Min(leftOccupied, rightOccupied)
                        / (float)Mathf.Max(leftOccupied, rightOccupied);
                    float isolatedCleanMaterial = 0f;
                    if (leftProtected == 0)
                        isolatedCleanMaterial += leftOccupied / (float)node.Occupied;
                    if (rightProtected == 0)
                        isolatedCleanMaterial += rightOccupied / (float)node.Occupied;
                    float score = protectedImprovement * 1000000f
                        + improvement * 1000f
                        + isolatedCleanMaterial * 10f
                        + balance * 0.01f;
                    if (!found || score > best.Score)
                    {
                        best = new SplitCandidate(
                            left,
                            right,
                            leftOccupied,
                            rightOccupied,
                            leftProtected,
                            rightProtected,
                            score);
                        found = true;
                    }
                }
            }

            return found;
        }

        private static bool MergeCompliant(
            List<VolumeNode> nodes,
            VolumeIntegral integral,
            VolumeIntegral protectedIntegral,
            float tolerance)
        {
            bool changed = RemoveContainedNodes(nodes);

            while (nodes.Count > 1)
            {
                int bestLeft = -1;
                int bestRight = -1;
                IntBox bestUnion = default;
                int bestOccupied = 0;
                float bestCost = float.PositiveInfinity;

                for (int leftIndex = 0; leftIndex < nodes.Count - 1; leftIndex++)
                for (int rightIndex = leftIndex + 1; rightIndex < nodes.Count; rightIndex++)
                {
                    IntBox union = IntBox.Union(nodes[leftIndex].Bounds, nodes[rightIndex].Bounds);
                    if (protectedIntegral.Count(union) > 0)
                        continue;
                    int occupied = integral.Count(union);
                    int empty = union.Volume - occupied;
                    float error = empty / (float)union.Volume;
                    if (error > tolerance + 1e-6f)
                        continue;

                    int oldEmpty = nodes[leftIndex].Empty + nodes[rightIndex].Empty;
                    float cost = empty - oldEmpty + union.Volume * 0.000001f;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestLeft = leftIndex;
                        bestRight = rightIndex;
                        bestUnion = union;
                        bestOccupied = occupied;
                    }
                }

                if (bestLeft < 0)
                    break;

                nodes.RemoveAt(bestRight);
                nodes.RemoveAt(bestLeft);
                nodes.Add(new VolumeNode(bestUnion, bestOccupied, 0));
                RemoveContainedNodes(nodes);
                changed = true;
            }

            return changed;
        }

        private static bool RemoveContainedNodes(List<VolumeNode> nodes)
        {
            bool changed = false;
            for (int outer = nodes.Count - 1; outer >= 0; outer--)
            {
                for (int inner = 0; inner < nodes.Count; inner++)
                {
                    if (outer == inner)
                        continue;
                    if (!nodes[inner].Bounds.Contains(nodes[outer].Bounds))
                        continue;

                    nodes.RemoveAt(outer);
                    changed = true;
                    break;
                }
            }
            return changed;
        }

        private static bool HasOverToleranceNode(List<VolumeNode> nodes, float tolerance)
        {
            foreach (VolumeNode node in nodes)
            {
                if (node.Error > tolerance + 1e-6f)
                    return true;
            }
            return false;
        }

        private static bool HasProtectedVoidNode(List<VolumeNode> nodes)
        {
            foreach (VolumeNode node in nodes)
            {
                if (node.ProtectedVoid > 0)
                    return true;
            }
            return false;
        }

        private static BoxDecompositionResult BuildResult(
            VoxelGrid grid,
            List<VolumeNode> nodes,
            int totalOccupied,
            int totalProtectedVoid,
            float tolerance,
            int maximumColliders)
        {
            var boxes = new List<IntBox>(nodes.Count);
            var covered = new bool[grid.Occupied.Length];
            float worstError = 0f;
            bool budgetLimited = false;

            foreach (VolumeNode node in nodes)
            {
                boxes.Add(node.Bounds);
                worstError = Mathf.Max(worstError, node.Error);
                if (node.Error > tolerance + 1e-6f)
                    budgetLimited = true;

                IntBox box = node.Bounds;
                for (int z = box.MinZ; z < box.MaxZ; z++)
                for (int y = box.MinY; y < box.MaxY; y++)
                for (int x = box.MinX; x < box.MaxX; x++)
                    covered[grid.Index(x, y, z)] = true;
            }

            int coveredCells = 0;
            int coveredOccupied = 0;
            int coveredProtectedVoid = 0;
            for (int i = 0; i < covered.Length; i++)
            {
                if (!covered[i])
                    continue;
                coveredCells++;
                if (grid.Occupied[i])
                    coveredOccupied++;
                if (grid.ProtectedVoid[i])
                    coveredProtectedVoid++;
            }

            int emptyCells = coveredCells - coveredOccupied;
            return new BoxDecompositionResult
            {
                Boxes = boxes,
                OccupiedCoverage = coveredOccupied / (float)totalOccupied,
                EmptyVolumeFraction = coveredCells > 0 ? emptyCells / (float)coveredCells : 0f,
                WorstBoxError = worstError,
                BudgetLimited = budgetLimited,
                ColliderBudgetExceeded = boxes.Count > maximumColliders,
                ProtectedVoidVoxelCount = totalProtectedVoid,
                UnresolvedProtectedVoidCount = coveredProtectedVoid
            };
        }

        private sealed class VolumeIntegral
        {
            private readonly int _sizeX;
            private readonly int _sizeY;
            private readonly int _sizeZ;
            private readonly int[] _prefix;

            public VolumeIntegral(VoxelGrid grid, bool[] cells)
            {
                _sizeX = grid.SizeX + 1;
                _sizeY = grid.SizeY + 1;
                _sizeZ = grid.SizeZ + 1;
                _prefix = new int[_sizeX * _sizeY * _sizeZ];

                for (int z = 1; z < _sizeZ; z++)
                for (int y = 1; y < _sizeY; y++)
                for (int x = 1; x < _sizeX; x++)
                {
                    int value = cells[grid.Index(x - 1, y - 1, z - 1)] ? 1 : 0;
                    _prefix[Index(x, y, z)] = value
                        + Get(x - 1, y, z) + Get(x, y - 1, z) + Get(x, y, z - 1)
                        - Get(x - 1, y - 1, z) - Get(x - 1, y, z - 1) - Get(x, y - 1, z - 1)
                        + Get(x - 1, y - 1, z - 1);
                }
            }

            public int Count(IntBox box)
            {
                if (!box.IsValid)
                    return 0;

                return Get(box.MaxX, box.MaxY, box.MaxZ)
                    - Get(box.MinX, box.MaxY, box.MaxZ)
                    - Get(box.MaxX, box.MinY, box.MaxZ)
                    - Get(box.MaxX, box.MaxY, box.MinZ)
                    + Get(box.MinX, box.MinY, box.MaxZ)
                    + Get(box.MinX, box.MaxY, box.MinZ)
                    + Get(box.MaxX, box.MinY, box.MinZ)
                    - Get(box.MinX, box.MinY, box.MinZ);
            }

            public IntBox Trim(IntBox box)
            {
                if (Count(box) == 0)
                    return default;

                int minX = box.MinX;
                int minY = box.MinY;
                int minZ = box.MinZ;
                int maxX = box.MaxX;
                int maxY = box.MaxY;
                int maxZ = box.MaxZ;

                while (minX + 1 < maxX && Count(new IntBox(minX, minY, minZ, minX + 1, maxY, maxZ)) == 0)
                    minX++;
                while (maxX - 1 > minX && Count(new IntBox(maxX - 1, minY, minZ, maxX, maxY, maxZ)) == 0)
                    maxX--;
                while (minY + 1 < maxY && Count(new IntBox(minX, minY, minZ, maxX, minY + 1, maxZ)) == 0)
                    minY++;
                while (maxY - 1 > minY && Count(new IntBox(minX, maxY - 1, minZ, maxX, maxY, maxZ)) == 0)
                    maxY--;
                while (minZ + 1 < maxZ && Count(new IntBox(minX, minY, minZ, maxX, maxY, minZ + 1)) == 0)
                    minZ++;
                while (maxZ - 1 > minZ && Count(new IntBox(minX, minY, maxZ - 1, maxX, maxY, maxZ)) == 0)
                    maxZ--;

                return new IntBox(minX, minY, minZ, maxX, maxY, maxZ);
            }

            private int Get(int x, int y, int z)
            {
                return _prefix[Index(x, y, z)];
            }

            private int Index(int x, int y, int z)
            {
                return x + _sizeX * (y + _sizeY * z);
            }
        }
    }
}
