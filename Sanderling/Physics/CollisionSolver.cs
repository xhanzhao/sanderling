using System;
using System.Collections.Generic;
using Sanderling.Scene;
using Rhino.Geometry;

namespace Sanderling.Physics
{
    /// <summary>Result of testing the body at one position against nearby geometry.</summary>
    internal struct ContactInfo
    {
        /// <summary>A contact that can be resolved by pushing the body horizontally (walls, columns, edges).</summary>
        public bool HasPush;
        /// <summary>Unit horizontal direction pointing away from the geometry.</summary>
        public Vector3d PushNormal;
        /// <summary>Horizontal distance needed to resolve the deepest pushable contact.</summary>
        public double PushDistance;
        /// <summary>Deepest penetration of any contact, including ceiling / floor-like contacts that cannot be pushed out.</summary>
        public double MaxPenetration;
        public int TrianglesTested;
    }

    /// <summary>
    /// Narrow phase: capsule-vs-triangle tests.
    ///
    /// For every nearby triangle the closest points between the capsule segment and the triangle are computed.
    /// If they are closer than the radius, the body penetrates the triangle:
    ///   - mostly horizontal contact direction -> pushable (walls, columns, table edges)
    ///   - mostly vertical contact direction   -> ceiling / overhang inside the body span; cannot be pushed out
    ///     horizontally, so it only counts towards the penetration depth (which blocks moving deeper into it)
    /// </summary>
    internal sealed class CollisionSolver
    {
        /// <summary>Contacts whose direction has |z| above this are treated as ceiling / floor-like.</summary>
        private const double VerticalContactThreshold = 0.7;

        private readonly SpatialIndex _index;
        private readonly NavigationBody _body;
        private readonly List<TriangleRef> _triangles = new List<TriangleRef>();

        public CollisionSolver(SpatialIndex index, NavigationBody body)
        {
            _index = index;
            _body = body;
        }

        /// <param name="x">Body center X</param>
        /// <param name="y">Body center Y</param>
        /// <param name="foot">Foot height (ground Z)</param>
        /// <param name="fromX">Previous body center X, used to orient contacts when the segment pierces a triangle</param>
        /// <param name="fromY">Previous body center Y</param>
        public ContactInfo Evaluate(double x, double y, double foot, double fromX, double fromY)
        {
            ContactInfo info = new ContactInfo();

            double r = _body.Radius;
            Point3d p = new Point3d(x, y, _body.SegmentBottom(foot));
            Point3d q = new Point3d(x, y, _body.SegmentTop(foot));

            BoundingBox box = new BoundingBox(x - r, y - r, foot + _body.StepHeight, x + r, y + r, q.Z + r);
            _index.QueryTriangles(box, _triangles);
            info.TrianglesTested = _triangles.Count;

            double r2 = r * r;
            double zeroDistance = r * 1e-6;

            for (int i = 0; i < _triangles.Count; i++)
            {
                TriangleRef t = _triangles[i];
                Point3d a = t.A, b = t.B, c = t.C;

                Point3d onSegment, onTriangle;
                double d2 = CollisionMath.ClosestSegmentTriangle(p, q, a, b, c, out onSegment, out onTriangle);
                if (d2 >= r2) continue;

                double distance = Math.Sqrt(d2);
                double depth = r - distance;

                Vector3d n;
                if (distance > zeroDistance)
                {
                    n = (onSegment - onTriangle) / distance;
                }
                else
                {
                    // The segment pierces the triangle: use the triangle normal, oriented towards where the body came from
                    n = Vector3d.CrossProduct(b - a, c - a);
                    if (!n.Unitize()) continue;
                    Vector3d toPrevious = new Point3d(fromX, fromY, onSegment.Z) - onTriangle;
                    if (n * toPrevious < 0.0) n = -n;
                }

                if (depth > info.MaxPenetration) info.MaxPenetration = depth;

                double horizontal = Math.Sqrt(n.X * n.X + n.Y * n.Y);
                if (Math.Abs(n.Z) > VerticalContactThreshold || horizontal < 1e-6) continue;

                // Moving horizontally by s increases the distance along n by s * horizontal
                double pushDistance = depth / horizontal;
                if (pushDistance > info.PushDistance)
                {
                    info.HasPush = true;
                    info.PushDistance = pushDistance;
                    info.PushNormal = new Vector3d(n.X / horizontal, n.Y / horizontal, 0.0);
                }
            }

            return info;
        }
    }

    /// <summary>
    /// Closest-point routines (after Ericson, "Real-Time Collision Detection").
    /// </summary>
    internal static class CollisionMath
    {
        private const double Epsilon = 1e-12;

        /// <summary>Squared distance between segment PQ and triangle ABC, with the closest points on each.</summary>
        public static double ClosestSegmentTriangle(Point3d p, Point3d q, Point3d a, Point3d b, Point3d c,
                                                    out Point3d onSegment, out Point3d onTriangle)
        {
            // Segment crosses the triangle: distance 0
            Point3d hit;
            if (SegmentIntersectsTriangle(p, q, a, b, c, out hit))
            {
                onSegment = hit;
                onTriangle = hit;
                return 0.0;
            }

            double best = double.MaxValue;
            onSegment = p;
            onTriangle = a;

            // Segment end points against the triangle
            Point3d t = ClosestPointOnTriangle(p, a, b, c);
            double d = p.DistanceToSquared(t);
            if (d < best) { best = d; onSegment = p; onTriangle = t; }

            t = ClosestPointOnTriangle(q, a, b, c);
            d = q.DistanceToSquared(t);
            if (d < best) { best = d; onSegment = q; onTriangle = t; }

            // Segment against the three triangle edges
            Point3d s1, s2;
            d = ClosestSegmentSegment(p, q, a, b, out s1, out s2);
            if (d < best) { best = d; onSegment = s1; onTriangle = s2; }

            d = ClosestSegmentSegment(p, q, b, c, out s1, out s2);
            if (d < best) { best = d; onSegment = s1; onTriangle = s2; }

            d = ClosestSegmentSegment(p, q, c, a, out s1, out s2);
            if (d < best) { best = d; onSegment = s1; onTriangle = s2; }

            return best;
        }

        /// <summary>Closest point on triangle ABC to point P.</summary>
        public static Point3d ClosestPointOnTriangle(Point3d p, Point3d a, Point3d b, Point3d c)
        {
            Vector3d ab = b - a;
            Vector3d ac = c - a;
            Vector3d ap = p - a;
            double d1 = ab * ap;
            double d2 = ac * ap;
            if (d1 <= 0.0 && d2 <= 0.0) return a;

            Vector3d bp = p - b;
            double d3 = ab * bp;
            double d4 = ac * bp;
            if (d3 >= 0.0 && d4 <= d3) return b;

            double vc = d1 * d4 - d3 * d2;
            if (vc <= 0.0 && d1 >= 0.0 && d3 <= 0.0)
            {
                double v = d1 / (d1 - d3);
                return a + ab * v;
            }

            Vector3d cp = p - c;
            double d5 = ab * cp;
            double d6 = ac * cp;
            if (d6 >= 0.0 && d5 <= d6) return c;

            double vb = d5 * d2 - d1 * d6;
            if (vb <= 0.0 && d2 >= 0.0 && d6 <= 0.0)
            {
                double w = d2 / (d2 - d6);
                return a + ac * w;
            }

            double va = d3 * d6 - d5 * d4;
            if (va <= 0.0 && (d4 - d3) >= 0.0 && (d5 - d6) >= 0.0)
            {
                double w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + (c - b) * w;
            }

            double denom = 1.0 / (va + vb + vc);
            double vv = vb * denom;
            double ww = vc * denom;
            return a + ab * vv + ac * ww;
        }

        /// <summary>Squared distance between segments P1Q1 and P2Q2, with the closest points on each.</summary>
        public static double ClosestSegmentSegment(Point3d p1, Point3d q1, Point3d p2, Point3d q2,
                                                   out Point3d c1, out Point3d c2)
        {
            Vector3d d1 = q1 - p1;
            Vector3d d2 = q2 - p2;
            Vector3d r = p1 - p2;
            double a = d1 * d1;
            double e = d2 * d2;
            double f = d2 * r;
            double s, t;

            if (a <= Epsilon && e <= Epsilon)
            {
                c1 = p1;
                c2 = p2;
                return c1.DistanceToSquared(c2);
            }

            if (a <= Epsilon)
            {
                s = 0.0;
                t = Clamp01(f / e);
            }
            else
            {
                double c = d1 * r;
                if (e <= Epsilon)
                {
                    t = 0.0;
                    s = Clamp01(-c / a);
                }
                else
                {
                    double b = d1 * d2;
                    double denom = a * e - b * b;
                    s = denom > Epsilon ? Clamp01((b * f - c * e) / denom) : 0.0;
                    t = (b * s + f) / e;

                    if (t < 0.0)
                    {
                        t = 0.0;
                        s = Clamp01(-c / a);
                    }
                    else if (t > 1.0)
                    {
                        t = 1.0;
                        s = Clamp01((b - c) / a);
                    }
                }
            }

            c1 = p1 + d1 * s;
            c2 = p2 + d2 * t;
            return c1.DistanceToSquared(c2);
        }

        /// <summary>Segment PQ vs triangle ABC intersection (Moller-Trumbore restricted to the segment).</summary>
        public static bool SegmentIntersectsTriangle(Point3d p, Point3d q, Point3d a, Point3d b, Point3d c, out Point3d hit)
        {
            hit = Point3d.Unset;
            Vector3d dir = q - p;
            Vector3d e1 = b - a;
            Vector3d e2 = c - a;
            Vector3d h = Vector3d.CrossProduct(dir, e2);
            double det = e1 * h;

            double scale = e1.SquareLength * dir.SquareLength;
            if (det * det <= 1e-20 * scale * e2.SquareLength) return false; // parallel or degenerate

            double inv = 1.0 / det;
            Vector3d s = p - a;
            double u = inv * (s * h);
            if (u < 0.0 || u > 1.0) return false;

            Vector3d qv = Vector3d.CrossProduct(s, e1);
            double v = inv * (dir * qv);
            if (v < 0.0 || u + v > 1.0) return false;

            double t = inv * (e2 * qv);
            if (t < 0.0 || t > 1.0) return false;

            hit = p + dir * t;
            return true;
        }

        private static double Clamp01(double v)
        {
            return v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
        }
    }
}
