using System;
using System.Collections.Generic;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Sanderling.Scene
{
    /// <summary>
    /// A mesh converted to a flat world-space triangle array (3 points per triangle, quads split in two).
    /// Per-frame collision and ground queries use plain coordinate math on these arrays instead of the slower Mesh API.
    ///
    /// Large meshes get their own triangle RTree, built lazily the first time the mesh is queried,
    /// so only meshes the user actually walks near pay that cost.
    /// </summary>
    internal sealed class TriangleMesh
    {
        /// <summary>Meshes with more triangles than this get a triangle RTree; smaller ones are scanned directly.</summary>
        private const int TreeThreshold = 64;

        private readonly Point3d[] _vertices;
        private RTree _tree;
        private List<int> _searchTarget;
        private EventHandler<RTreeEventArgs> _searchHandler;

        private TriangleMesh(Point3d[] vertices, BoundingBox bounds)
        {
            _vertices = vertices;
            Bounds = bounds;
        }

        public int TriangleCount { get { return _vertices.Length / 3; } }
        public BoundingBox Bounds { get; private set; }

        public Point3d A(int triangle) { return _vertices[triangle * 3]; }
        public Point3d B(int triangle) { return _vertices[triangle * 3 + 1]; }
        public Point3d C(int triangle) { return _vertices[triangle * 3 + 2]; }

        /// <summary>
        /// Creates a triangle mesh from a Rhino mesh, optionally transformed (block instances).
        /// Degenerate triangles are skipped. Returns null if no valid triangle remains.
        /// </summary>
        public static TriangleMesh FromMesh(Mesh mesh, Transform xform)
        {
            if (mesh == null || mesh.Faces.Count == 0) return null;

            Point3d[] points = mesh.Vertices.ToPoint3dArray();
            if (points == null || points.Length == 0) return null;

            if (!xform.IsIdentity)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    Point3d p = points[i];
                    p.Transform(xform);
                    points[i] = p;
                }
            }

            List<Point3d> triangles = new List<Point3d>(mesh.Faces.Count * 6);
            int count = points.Length;
            for (int f = 0; f < mesh.Faces.Count; f++)
            {
                MeshFace face = mesh.Faces[f];
                if (face.A < 0 || face.B < 0 || face.C < 0 || face.A >= count || face.B >= count || face.C >= count) continue;

                AddTriangle(triangles, points[face.A], points[face.B], points[face.C]);
                if (face.IsQuad && face.D >= 0 && face.D < count)
                {
                    AddTriangle(triangles, points[face.A], points[face.C], points[face.D]);
                }
            }

            if (triangles.Count == 0) return null;

            Point3d[] vertices = triangles.ToArray();
            return new TriangleMesh(vertices, ComputeBounds(vertices));
        }

        /// <summary>Adds the indices of all triangles whose bounding box intersects the given box.</summary>
        public void Query(BoundingBox box, List<int> results)
        {
            if (!Overlaps(Bounds, box)) return;

            if (TriangleCount <= TreeThreshold)
            {
                for (int t = 0; t < TriangleCount; t++)
                {
                    if (TriangleOverlaps(t, box)) results.Add(t);
                }
                return;
            }

            EnsureTree();
            _searchTarget = results;
            _tree.Search(box, _searchHandler);
            _searchTarget = null;
        }

        private void EnsureTree()
        {
            if (_tree != null) return;

            _tree = new RTree();
            for (int t = 0; t < TriangleCount; t++)
            {
                _tree.Insert(TriangleBounds(t), t);
            }
            _searchHandler = (sender, e) => _searchTarget.Add(e.Id);
        }

        private static void AddTriangle(List<Point3d> list, Point3d a, Point3d b, Point3d c)
        {
            Vector3d ab = b - a;
            Vector3d ac = c - a;
            double abLen = ab.SquareLength;
            double acLen = ac.SquareLength;
            if (abLen <= 0.0 || acLen <= 0.0) return;

            // Relative degeneracy test (sin^2 of the corner angle), independent of model units
            double cross = Vector3d.CrossProduct(ab, ac).SquareLength;
            if (cross <= 1e-18 * abLen * acLen) return;

            list.Add(a);
            list.Add(b);
            list.Add(c);
        }

        private BoundingBox TriangleBounds(int t)
        {
            Point3d a = A(t), b = B(t), c = C(t);
            return new BoundingBox(
                Math.Min(a.X, Math.Min(b.X, c.X)), Math.Min(a.Y, Math.Min(b.Y, c.Y)), Math.Min(a.Z, Math.Min(b.Z, c.Z)),
                Math.Max(a.X, Math.Max(b.X, c.X)), Math.Max(a.Y, Math.Max(b.Y, c.Y)), Math.Max(a.Z, Math.Max(b.Z, c.Z)));
        }

        private bool TriangleOverlaps(int t, BoundingBox box)
        {
            Point3d a = A(t), b = B(t), c = C(t);
            if (Math.Max(a.X, Math.Max(b.X, c.X)) < box.Min.X || Math.Min(a.X, Math.Min(b.X, c.X)) > box.Max.X) return false;
            if (Math.Max(a.Y, Math.Max(b.Y, c.Y)) < box.Min.Y || Math.Min(a.Y, Math.Min(b.Y, c.Y)) > box.Max.Y) return false;
            if (Math.Max(a.Z, Math.Max(b.Z, c.Z)) < box.Min.Z || Math.Min(a.Z, Math.Min(b.Z, c.Z)) > box.Max.Z) return false;
            return true;
        }

        internal static bool Overlaps(BoundingBox a, BoundingBox b)
        {
            return a.Max.X >= b.Min.X && a.Min.X <= b.Max.X
                && a.Max.Y >= b.Min.Y && a.Min.Y <= b.Max.Y
                && a.Max.Z >= b.Min.Z && a.Min.Z <= b.Max.Z;
        }

        private static BoundingBox ComputeBounds(Point3d[] points)
        {
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            for (int i = 0; i < points.Length; i++)
            {
                Point3d p = points[i];
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Z < minZ) minZ = p.Z;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
                if (p.Z > maxZ) maxZ = p.Z;
            }
            return new BoundingBox(minX, minY, minZ, maxX, maxY, maxZ);
        }
    }

    /// <summary>Cached navigation geometry of one Rhino object (block instances are flattened).</summary>
    internal sealed class CachedObject
    {
        public CachedObject(Guid id, ObjectType type)
        {
            Id = id;
            Type = type;
            Meshes = new List<TriangleMesh>();
        }

        public Guid Id { get; private set; }
        public ObjectType Type { get; private set; }
        public List<TriangleMesh> Meshes { get; private set; }

        public int TriangleCount
        {
            get
            {
                int n = 0;
                foreach (TriangleMesh m in Meshes) n += m.TriangleCount;
                return n;
            }
        }
    }

    /// <summary>
    /// Converts Rhino objects to cached triangle meshes.
    /// Uses the object's existing render meshes when available (fast, matches what is displayed);
    /// otherwise creates meshes from the geometry.
    /// </summary>
    internal static class NavigationMeshCache
    {
        private const int MaxBlockDepth = 8;
        private const int SubDDisplayDensity = 3;

        public static CachedObject Build(RhinoObject obj)
        {
            CachedObject entry = new CachedObject(obj.Id, obj.ObjectType);
            Collect(obj, Transform.Identity, 0, entry.Meshes);
            return entry;
        }

        private static void Collect(RhinoObject obj, Transform xform, int depth, List<TriangleMesh> output)
        {
            InstanceObject instance = obj as InstanceObject;
            if (instance != null)
            {
                if (depth >= MaxBlockDepth) return;
                InstanceDefinition definition = instance.InstanceDefinition;
                if (definition == null) return;

                Transform childXform = xform * instance.InstanceXform;
                RhinoObject[] children = definition.GetObjects();
                if (children == null) return;

                foreach (RhinoObject child in children)
                {
                    if (child != null) Collect(child, childXform, depth + 1, output);
                }
                return;
            }

            Mesh[] meshes = GetObjectMeshes(obj);
            if (meshes == null) return;

            foreach (Mesh mesh in meshes)
            {
                TriangleMesh tm = TriangleMesh.FromMesh(mesh, xform);
                if (tm != null) output.Add(tm);
            }
        }

        private static Mesh[] GetObjectMeshes(RhinoObject obj)
        {
            GeometryBase geometry = obj.Geometry;
            if (geometry == null) return null;

            Mesh mesh = geometry as Mesh;
            if (mesh != null) return new[] { mesh };

            Mesh[] render = null;
            try
            {
                render = obj.GetMeshes(MeshType.Render);
            }
            catch (Exception)
            {
                render = null;
            }
            if (render != null && render.Length > 0) return render;

            Brep brep = geometry as Brep;
            if (brep == null)
            {
                Extrusion extrusion = geometry as Extrusion;
                if (extrusion != null) brep = extrusion.ToBrep();
            }
            if (brep == null)
            {
                Surface surface = geometry as Surface;
                if (surface != null) brep = surface.ToBrep();
            }
            if (brep != null)
            {
                return Mesh.CreateFromBrep(brep, MeshingParameters.FastRenderMesh);
            }

            SubD subd = geometry as SubD;
            if (subd != null)
            {
                Mesh subdMesh = Mesh.CreateFromSubD(subd, SubDDisplayDensity);
                return subdMesh != null ? new[] { subdMesh } : null;
            }

            return null;
        }
    }
}
