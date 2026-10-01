using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Sanderling.Scene
{
    /// <summary>Reference to one triangle of a cached mesh.</summary>
    internal struct TriangleRef
    {
        public TriangleMesh Mesh;
        public int Index;

        public Point3d A { get { return Mesh.A(Index); } }
        public Point3d B { get { return Mesh.B(Index); } }
        public Point3d C { get { return Mesh.C(Index); } }
    }

    /// <summary>
    /// Two-level spatial index used by all real-time queries:
    ///   level 1 - RTree of mesh bounding boxes (which meshes are near?)
    ///   level 2 - per-mesh triangle RTree, built lazily (which triangles are near?)
    /// Built at the start of each navigation session from the persistent object cache; building it is cheap
    /// because it only inserts one box per mesh.
    /// </summary>
    internal sealed class SpatialIndex
    {
        private readonly List<TriangleMesh> _meshes = new List<TriangleMesh>();
        private readonly RTree _tree = new RTree();
        private readonly List<int> _meshHits = new List<int>();
        private readonly List<int> _triangleHits = new List<int>();
        private readonly EventHandler<RTreeEventArgs> _meshHandler;

        public SpatialIndex(IEnumerable<CachedObject> objects)
        {
            _meshHandler = (sender, e) => _meshHits.Add(e.Id);

            foreach (CachedObject obj in objects)
            {
                foreach (TriangleMesh mesh in obj.Meshes)
                {
                    int id = _meshes.Count;
                    _meshes.Add(mesh);
                    _tree.Insert(mesh.Bounds, id);
                    TriangleCount += mesh.TriangleCount;
                }
            }
        }

        public int MeshCount { get { return _meshes.Count; } }
        public int TriangleCount { get; private set; }

        /// <summary>Collects all triangles whose bounding box intersects the given box (results list is cleared first).</summary>
        public void QueryTriangles(BoundingBox box, List<TriangleRef> results)
        {
            results.Clear();
            if (_meshes.Count == 0) return;

            _meshHits.Clear();
            _tree.Search(box, _meshHandler);

            for (int m = 0; m < _meshHits.Count; m++)
            {
                TriangleMesh mesh = _meshes[_meshHits[m]];
                _triangleHits.Clear();
                mesh.Query(box, _triangleHits);

                for (int t = 0; t < _triangleHits.Count; t++)
                {
                    TriangleRef r;
                    r.Mesh = mesh;
                    r.Index = _triangleHits[t];
                    results.Add(r);
                }
            }
        }
    }
}
