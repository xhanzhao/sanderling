using System;
using System.Collections.Generic;
using Sanderling.Scene;
using Rhino.Geometry;

namespace Sanderling.Physics
{
    /// <summary>
    /// Ground queries against the navigation spatial index.
    ///
    /// Five vertical rays form the user's footprint: the center plus four points at half the body radius
    /// (+X, -X, +Y, -Y). The ground is the highest surface any of them hits within [minZ, maxZ]. This keeps
    /// the user from slipping through small gaps between meshes and makes standing on step or slab edges stable.
    ///
    /// Each ray is a vertical line, so a hit is found by projecting triangles onto XY and interpolating Z.
    /// Vertical triangles (walls) have no XY area and are ignored automatically.
    /// </summary>
    internal sealed class GroundDetector
    {
        private const int SampleCount = 5;

        private readonly SpatialIndex _index;
        private readonly double _footprint;
        private readonly List<TriangleRef> _triangles = new List<TriangleRef>();
        private readonly double[] _sampleX = new double[SampleCount];
        private readonly double[] _sampleY = new double[SampleCount];

        /// <param name="index">Navigation geometry</param>
        /// <param name="footprintRadius">Distance of the four outer rays from the center (model units)</param>
        public GroundDetector(SpatialIndex index, double footprintRadius)
        {
            _index = index;
            _footprint = Math.Max(0.0, footprintRadius);
        }

        public int LastTrianglesTested { get; private set; }

        /// <summary>
        /// Finds the highest surface below (x, y) with minZ &lt;= z &lt;= maxZ.
        /// Surfaces above maxZ are ignored, which is how ceilings, upper floors and obstacles are skipped.
        /// </summary>
        public bool FindGround(double x, double y, double maxZ, double minZ, out double groundZ)
        {
            groundZ = 0.0;
            LastTrianglesTested = 0;
            if (_index == null || maxZ < minZ) return false;

            double f = _footprint;
            _sampleX[0] = x;     _sampleY[0] = y;
            _sampleX[1] = x + f; _sampleY[1] = y;
            _sampleX[2] = x - f; _sampleY[2] = y;
            _sampleX[3] = x;     _sampleY[3] = y + f;
            _sampleX[4] = x;     _sampleY[4] = y - f;

            double pad = f * 0.01 + 1e-9;
            BoundingBox column = new BoundingBox(x - f - pad, y - f - pad, minZ, x + f + pad, y + f + pad, maxZ);
            _index.QueryTriangles(column, _triangles);
            LastTrianglesTested = _triangles.Count;

            double zTolerance = Math.Max(Math.Abs(maxZ), 1.0) * 1e-9;
            double best = double.MinValue;
            bool found = false;

            for (int i = 0; i < _triangles.Count; i++)
            {
                TriangleRef t = _triangles[i];
                Point3d a = t.A, b = t.B, c = t.C;

                // 2D barycentric setup; skip triangles with no XY area (vertical faces)
                double det = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
                double size = (b - a).SquareLength + (c - a).SquareLength;
                if (Math.Abs(det) <= 1e-9 * size) continue;
                double inv = 1.0 / det;

                for (int s = 0; s < SampleCount; s++)
                {
                    double px = _sampleX[s], py = _sampleY[s];
                    double l1 = ((b.Y - c.Y) * (px - c.X) + (c.X - b.X) * (py - c.Y)) * inv;
                    double l2 = ((c.Y - a.Y) * (px - c.X) + (a.X - c.X) * (py - c.Y)) * inv;
                    double l3 = 1.0 - l1 - l2;

                    // Small tolerance closes cracks along shared triangle edges
                    const double edge = -1e-7;
                    if (l1 < edge || l2 < edge || l3 < edge) continue;

                    double z = l1 * a.Z + l2 * b.Z + l3 * c.Z;
                    if (z > maxZ + zTolerance || z < minZ) continue;
                    if (z > best)
                    {
                        best = z;
                        found = true;
                    }
                }
            }

            if (found) groundZ = best;
            return found;
        }
    }
}
