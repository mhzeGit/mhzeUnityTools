using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gigaduck.AutoBoxCollider
{
    internal enum ColliderAlignment
    {
        Auto,
        ObjectAxes,
        PrincipalAxes
    }

    [Serializable]
    internal sealed class ColliderAnalysisSettings
    {
        [Range(12, 160)] public int voxelResolution = 64;
        [Range(0f, 0.5f)] public float emptySpaceTolerance = 0.12f;
        [Range(1, 256)] public int maximumColliders = 64;
        [Range(-0.25f, 0.5f)] public float paddingInVoxels;
        public ColliderAlignment alignment = ColliderAlignment.Auto;
        public bool fillEnclosedVolume = true;
        public bool preserveHolesAndOpenings = true;
        public bool detectAngledSurfaces = true;
        [Range(0.5f, 15f)] public float planarAngleTolerance = 3f;
        public bool includeChildMeshes;

        public void Validate()
        {
            voxelResolution = Mathf.Clamp(voxelResolution, 12, 160);
            emptySpaceTolerance = Mathf.Clamp(emptySpaceTolerance, 0f, 0.5f);
            maximumColliders = Mathf.Clamp(maximumColliders, 1, 256);
            paddingInVoxels = Mathf.Clamp(paddingInVoxels, -0.25f, 0.5f);
            planarAngleTolerance = Mathf.Clamp(planarAngleTolerance, 0.5f, 15f);
        }

        public int GetStableHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + voxelResolution;
                hash = hash * 31 + emptySpaceTolerance.GetHashCode();
                hash = hash * 31 + maximumColliders;
                hash = hash * 31 + paddingInVoxels.GetHashCode();
                hash = hash * 31 + (int)alignment;
                hash = hash * 31 + fillEnclosedVolume.GetHashCode();
                hash = hash * 31 + preserveHolesAndOpenings.GetHashCode();
                hash = hash * 31 + detectAngledSurfaces.GetHashCode();
                hash = hash * 31 + planarAngleTolerance.GetHashCode();
                hash = hash * 31 + includeChildMeshes.GetHashCode();
                return hash;
            }
        }
    }

    internal readonly struct AnalysisFrame
    {
        public readonly Vector3 Origin;
        public readonly Quaternion Rotation;
        public readonly string Name;

        public AnalysisFrame(Vector3 origin, Quaternion rotation, string name)
        {
            Origin = origin;
            Rotation = rotation;
            Name = name;
        }

        public Vector3 ToFrame(Vector3 targetLocalPoint)
        {
            return Quaternion.Inverse(Rotation) * (targetLocalPoint - Origin);
        }
    }

    internal readonly struct ColliderBox
    {
        public readonly Vector3 Center;
        public readonly Vector3 Size;
        public readonly Quaternion Rotation;
        public readonly bool IsOrientedFit;

        public ColliderBox(
            Vector3 center,
            Vector3 size,
            Quaternion rotation,
            bool isOrientedFit = false)
        {
            Center = center;
            Size = size;
            Rotation = rotation;
            IsOrientedFit = isOrientedFit;
        }
    }

    internal sealed class MeshGeometry
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<int> Triangles = new List<int>();
        public int SourceCount;
        public int SourceHash = 17;
        public string Description;
        public int ConnectedComponentCount;
        public int BoundaryEdgeCount;
        public int NonManifoldEdgeCount;
        public bool IsWatertight;

        public int TriangleCount => Triangles.Count / 3;
    }

    internal sealed class ColliderAnalysisResult
    {
        public GameObject Target;
        public AnalysisFrame Frame;
        public List<ColliderBox> Boxes;
        public int SettingsHash;
        public int SourceHash;
        public int VertexCount;
        public int TriangleCount;
        public int GridX;
        public int GridY;
        public int GridZ;
        public int SurfaceVoxelCount;
        public int OccupiedVoxelCount;
        public float OccupiedCoverage;
        public float EmptyVolumeFraction;
        public float WorstBoxError;
        public float VoxelSize;
        public bool BudgetLimited;
        public bool ColliderBudgetExceeded;
        public int ProtectedVoidVoxelCount;
        public int UnresolvedProtectedVoidCount;
        public int OrientedBoxCount;
        public int ConnectedComponentCount;
        public int BoundaryEdgeCount;
        public int NonManifoldEdgeCount;
        public bool IsWatertight;
        public string SourceDescription;
    }

    internal readonly struct IntBox : IEquatable<IntBox>
    {
        public readonly int MinX;
        public readonly int MinY;
        public readonly int MinZ;
        public readonly int MaxX;
        public readonly int MaxY;
        public readonly int MaxZ;

        public IntBox(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
        {
            MinX = minX;
            MinY = minY;
            MinZ = minZ;
            MaxX = maxX;
            MaxY = maxY;
            MaxZ = maxZ;
        }

        public bool IsValid => MaxX > MinX && MaxY > MinY && MaxZ > MinZ;
        public int Volume => (MaxX - MinX) * (MaxY - MinY) * (MaxZ - MinZ);

        public IntBox WithAxisMax(int axis, int value)
        {
            switch (axis)
            {
                case 0: return new IntBox(MinX, MinY, MinZ, value, MaxY, MaxZ);
                case 1: return new IntBox(MinX, MinY, MinZ, MaxX, value, MaxZ);
                default: return new IntBox(MinX, MinY, MinZ, MaxX, MaxY, value);
            }
        }

        public IntBox WithAxisMin(int axis, int value)
        {
            switch (axis)
            {
                case 0: return new IntBox(value, MinY, MinZ, MaxX, MaxY, MaxZ);
                case 1: return new IntBox(MinX, value, MinZ, MaxX, MaxY, MaxZ);
                default: return new IntBox(MinX, MinY, value, MaxX, MaxY, MaxZ);
            }
        }

        public int GetMin(int axis)
        {
            return axis == 0 ? MinX : axis == 1 ? MinY : MinZ;
        }

        public int GetMax(int axis)
        {
            return axis == 0 ? MaxX : axis == 1 ? MaxY : MaxZ;
        }

        public bool Contains(IntBox other)
        {
            return MinX <= other.MinX && MinY <= other.MinY && MinZ <= other.MinZ
                && MaxX >= other.MaxX && MaxY >= other.MaxY && MaxZ >= other.MaxZ;
        }

        public static IntBox Union(IntBox a, IntBox b)
        {
            return new IntBox(
                Mathf.Min(a.MinX, b.MinX),
                Mathf.Min(a.MinY, b.MinY),
                Mathf.Min(a.MinZ, b.MinZ),
                Mathf.Max(a.MaxX, b.MaxX),
                Mathf.Max(a.MaxY, b.MaxY),
                Mathf.Max(a.MaxZ, b.MaxZ));
        }

        public bool Equals(IntBox other)
        {
            return MinX == other.MinX && MinY == other.MinY && MinZ == other.MinZ
                && MaxX == other.MaxX && MaxY == other.MaxY && MaxZ == other.MaxZ;
        }

        public override bool Equals(object obj)
        {
            return obj is IntBox other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = MinX;
                hash = hash * 397 ^ MinY;
                hash = hash * 397 ^ MinZ;
                hash = hash * 397 ^ MaxX;
                hash = hash * 397 ^ MaxY;
                hash = hash * 397 ^ MaxZ;
                return hash;
            }
        }
    }

    internal sealed class VoxelGrid
    {
        public readonly int SizeX;
        public readonly int SizeY;
        public readonly int SizeZ;
        public readonly float VoxelSize;
        public readonly Vector3 MinCorner;
        public readonly bool[] Surface;
        public readonly bool[] Occupied;
        public readonly bool[] ProtectedVoid;

        public VoxelGrid(int sizeX, int sizeY, int sizeZ, float voxelSize, Vector3 minCorner)
        {
            SizeX = sizeX;
            SizeY = sizeY;
            SizeZ = sizeZ;
            VoxelSize = voxelSize;
            MinCorner = minCorner;
            Surface = new bool[sizeX * sizeY * sizeZ];
            Occupied = new bool[Surface.Length];
            ProtectedVoid = new bool[Surface.Length];
        }

        public int Index(int x, int y, int z)
        {
            return x + SizeX * (y + SizeY * z);
        }

        public Vector3 CellCenter(int x, int y, int z)
        {
            return MinCorner + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * VoxelSize;
        }
    }
}
