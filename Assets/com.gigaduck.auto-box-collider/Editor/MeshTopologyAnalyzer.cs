using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gigaduck.AutoBoxCollider
{
    internal static class MeshTopologyAnalyzer
    {
        private readonly struct WeldKey : IEquatable<WeldKey>
        {
            private readonly int _x;
            private readonly int _y;
            private readonly int _z;

            public WeldKey(Vector3 vertex, Vector3 origin, float inverseTolerance)
            {
                Vector3 relative = (vertex - origin) * inverseTolerance;
                _x = Mathf.RoundToInt(relative.x);
                _y = Mathf.RoundToInt(relative.y);
                _z = Mathf.RoundToInt(relative.z);
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

        public static void Analyze(MeshGeometry geometry)
        {
            if (geometry == null || geometry.Vertices.Count == 0 || geometry.TriangleCount == 0)
                return;

            Vector3 minimum = geometry.Vertices[0];
            Vector3 maximum = geometry.Vertices[0];
            for (int i = 1; i < geometry.Vertices.Count; i++)
            {
                minimum = Vector3.Min(minimum, geometry.Vertices[i]);
                maximum = Vector3.Max(maximum, geometry.Vertices[i]);
            }

            Vector3 size = maximum - minimum;
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float weldTolerance = Mathf.Max(longest * 0.00001f, 0.0000001f);
            float inverseTolerance = 1f / weldTolerance;
            var weldLookup = new Dictionary<WeldKey, int>(geometry.Vertices.Count);
            var weldedVertices = new int[geometry.Vertices.Count];
            int weldCount = 0;

            for (int i = 0; i < geometry.Vertices.Count; i++)
            {
                var key = new WeldKey(geometry.Vertices[i], minimum, inverseTolerance);
                if (!weldLookup.TryGetValue(key, out int weldIndex))
                {
                    weldIndex = weldCount++;
                    weldLookup.Add(key, weldIndex);
                }
                weldedVertices[i] = weldIndex;
            }

            var unionFind = new UnionFind(geometry.TriangleCount);
            var edgeUseCounts = new Dictionary<EdgeKey, int>(geometry.TriangleCount * 2);
            var firstTriangleAtEdge = new Dictionary<EdgeKey, int>(geometry.TriangleCount * 2);

            for (int triangleIndex = 0; triangleIndex < geometry.TriangleCount; triangleIndex++)
            {
                int index = triangleIndex * 3;
                int a = weldedVertices[geometry.Triangles[index]];
                int b = weldedVertices[geometry.Triangles[index + 1]];
                int c = weldedVertices[geometry.Triangles[index + 2]];
                ConnectTriangleAtEdge(
                    unionFind, firstTriangleAtEdge, edgeUseCounts, new EdgeKey(a, b), triangleIndex);
                ConnectTriangleAtEdge(
                    unionFind, firstTriangleAtEdge, edgeUseCounts, new EdgeKey(b, c), triangleIndex);
                ConnectTriangleAtEdge(
                    unionFind, firstTriangleAtEdge, edgeUseCounts, new EdgeKey(c, a), triangleIndex);
            }

            var componentRoots = new HashSet<int>();
            for (int triangleIndex = 0; triangleIndex < geometry.TriangleCount; triangleIndex++)
                componentRoots.Add(unionFind.Find(triangleIndex));

            int boundaryEdges = 0;
            int nonManifoldEdges = 0;
            foreach (int useCount in edgeUseCounts.Values)
            {
                if (useCount == 1)
                    boundaryEdges++;
                else if (useCount > 2)
                    nonManifoldEdges++;
            }

            geometry.ConnectedComponentCount = componentRoots.Count;
            geometry.BoundaryEdgeCount = boundaryEdges;
            geometry.NonManifoldEdgeCount = nonManifoldEdges;
            geometry.IsWatertight = boundaryEdges == 0 && nonManifoldEdges == 0;
        }

        private static void ConnectTriangleAtEdge(
            UnionFind unionFind,
            Dictionary<EdgeKey, int> firstTriangleAtEdge,
            Dictionary<EdgeKey, int> counts,
            EdgeKey edge,
            int triangle)
        {
            if (edge.IsDegenerate)
                return;

            if (firstTriangleAtEdge.TryGetValue(edge, out int previous))
                unionFind.Union(previous, triangle);
            else
                firstTriangleAtEdge.Add(edge, triangle);
            counts.TryGetValue(edge, out int count);
            counts[edge] = count + 1;
        }
    }
}
