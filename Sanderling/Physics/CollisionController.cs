using System;
using Sanderling.Scene;
using Rhino.Geometry;

namespace Sanderling.Physics
{
    /// <summary>Logical position of the walking body (the camera height follows it smoothly).</summary>
    internal struct BodyState
    {
        public double X;
        public double Y;
        public bool HasGround;
        public double GroundZ;
    }

    /// <summary>
    /// Per-move pipeline for Walk mode:
    ///
    ///   desired move -> substeps -> ground and step rules -> capsule collision (push out + slide)
    ///                -> re-check ground -> penetration check -> valid position
    ///
    /// Rules
    ///   - Substeps are at most half the body radius long, so fast movement cannot pass through thin walls
    ///     or skip over two stair risers at once.
    ///   - Ground is searched from foot + step height downwards: rises up to the step height are climbed,
    ///     drops are allowed, and no ground at all (edge of the model) blocks the move.
    ///   - Walls push the body out horizontally; the movement and velocity components pointing into the
    ///     wall are removed, which produces sliding along walls at any angle.
    ///   - A move is rejected if it would leave the body deeper inside geometry than before (corners,
    ///     low ceilings). Moves that reduce penetration are always allowed, so the user can never get
    ///     permanently stuck after entering Walk mode inside an object.
    /// </summary>
    internal sealed class CollisionController
    {
        private const int MaxSubsteps = 32;
        private const int MaxPushIterations = 4;

        private readonly NavigationBody _body;
        private readonly GroundDetector _ground;
        private readonly CollisionSolver _solver;
        private readonly double _searchDistance;
        private readonly double _skin;
        private readonly double _penetrationTolerance;
        private readonly double _minMove;

        private double _currentPenetration;

        public CollisionController(SpatialIndex index, NavigationBody body, double searchDistance, double metersToModel)
        {
            _body = body;
            _ground = new GroundDetector(index, body.Radius * 0.5);
            _solver = new CollisionSolver(index, body);
            _searchDistance = searchDistance;
            _skin = 0.001 * metersToModel;                 // keep 1 mm away from surfaces after a push
            _penetrationTolerance = 0.002 * metersToModel; // numerical slack for the "never go deeper" rule
            _minMove = 1e-6 * metersToModel;
        }

        public NavigationBody Body { get { return _body; } }

        /// <summary>Triangles tested during the last Move / FindInitialGround call (for the HUD stats).</summary>
        public int LastTrianglesTested { get; private set; }

        /// <summary>Ground straight below the camera, used when Walk mode is activated.</summary>
        public bool FindInitialGround(double x, double y, double cameraZ, out double groundZ)
        {
            bool found = _ground.FindGround(x, y, cameraZ, cameraZ - _searchDistance, out groundZ);
            LastTrianglesTested = _ground.LastTrianglesTested;
            return found;
        }

        /// <summary>Re-measures the current penetration (on activation or when collision is switched on).</summary>
        public void ResetPenetration(BodyState body, double cameraZ, bool collide)
        {
            _currentPenetration = collide
                ? _solver.Evaluate(body.X, body.Y, Foot(body, cameraZ), body.X, body.Y).MaxPenetration
                : 0.0;
        }

        /// <summary>
        /// Moves the body horizontally by the given displacement.
        /// Velocity is clipped against contacts so the caller keeps sliding instead of pushing into walls.
        /// </summary>
        /// <returns>Whether the body moved.</returns>
        public bool Move(ref BodyState body, Vector3d displacement, ref Vector3d velocity, bool collide, double cameraZ)
        {
            LastTrianglesTested = 0;

            Vector3d total = new Vector3d(displacement.X, displacement.Y, 0.0);
            double length = total.Length;
            if (length < _minMove) return false;

            int count = (int)Math.Ceiling(length / Math.Max(_body.MaxSubstepLength, _minMove));
            if (count < 1) count = 1;
            if (count > MaxSubsteps) count = MaxSubsteps;

            Vector3d step = total / count;
            bool moved = false;

            for (int i = 0; i < count; i++)
            {
                if (step.Length < _minMove) break;

                if (TrySubstep(ref body, ref step, ref velocity, collide, cameraZ))
                {
                    moved = true;
                    continue;
                }

                // Hard block (edge, corner, low ceiling): try the X-only and Y-only parts
                Vector3d stepX = new Vector3d(step.X, 0.0, 0.0);
                if (Math.Abs(step.X) >= _minMove && TrySubstep(ref body, ref stepX, ref velocity, collide, cameraZ))
                {
                    step = stepX;
                    velocity = new Vector3d(velocity.X, 0.0, 0.0);
                    moved = true;
                    continue;
                }

                Vector3d stepY = new Vector3d(0.0, step.Y, 0.0);
                if (Math.Abs(step.Y) >= _minMove && TrySubstep(ref body, ref stepY, ref velocity, collide, cameraZ))
                {
                    step = stepY;
                    velocity = new Vector3d(0.0, velocity.Y, 0.0);
                    moved = true;
                    continue;
                }

                velocity = Vector3d.Zero; // fully blocked
                break;
            }

            return moved;
        }

        private bool TrySubstep(ref BodyState body, ref Vector3d step, ref Vector3d velocity, bool collide, double cameraZ)
        {
            double foot = Foot(body, cameraZ);
            double maxZ = foot + _body.StepHeight;
            double minZ = foot - _searchDistance;

            double x = body.X + step.X;
            double y = body.Y + step.Y;

            // ---- Ground and step rules at the target ----
            double groundZ;
            bool hasGround = _ground.FindGround(x, y, maxZ, minZ, out groundZ);
            LastTrianglesTested += _ground.LastTrianglesTested;
            if (!hasGround && body.HasGround) return false; // edge of the model

            double newFoot = hasGround ? groundZ : foot;
            double penetration = 0.0;

            if (collide)
            {
                Vector3d clippedStep = step;
                Vector3d clippedVelocity = velocity;
                bool pushed = false;

                // ---- Capsule collision: push out of the deepest contact, repeat for corners ----
                ContactInfo info = _solver.Evaluate(x, y, newFoot, body.X, body.Y);
                LastTrianglesTested += info.TrianglesTested;

                for (int iteration = 0; iteration < MaxPushIterations && info.HasPush; iteration++)
                {
                    double push = Math.Min(info.PushDistance + _skin, _body.Radius * 2.0);
                    x += info.PushNormal.X * push;
                    y += info.PushNormal.Y * push;
                    Clip(ref clippedStep, info.PushNormal);
                    Clip(ref clippedVelocity, info.PushNormal);
                    pushed = true;

                    info = _solver.Evaluate(x, y, newFoot, body.X, body.Y);
                    LastTrianglesTested += info.TrianglesTested;
                }

                if (pushed)
                {
                    // Being pushed may move the body onto different ground
                    hasGround = _ground.FindGround(x, y, maxZ, minZ, out groundZ);
                    LastTrianglesTested += _ground.LastTrianglesTested;
                    if (!hasGround && body.HasGround) return false;

                    double pushedFoot = hasGround ? groundZ : foot;
                    if (Math.Abs(pushedFoot - newFoot) > _skin)
                    {
                        newFoot = pushedFoot;
                        info = _solver.Evaluate(x, y, newFoot, body.X, body.Y);
                        LastTrianglesTested += info.TrianglesTested;
                    }
                }

                // ---- Never end up deeper inside geometry than before ----
                penetration = info.MaxPenetration;
                if (penetration > _currentPenetration + _penetrationTolerance) return false;

                step = clippedStep;
                velocity = clippedVelocity;
            }

            double dx = x - body.X;
            double dy = y - body.Y;
            if (dx * dx + dy * dy < _minMove * _minMove) return false;

            // ---- Commit ----
            body.X = x;
            body.Y = y;
            body.HasGround = hasGround;
            if (hasGround) body.GroundZ = groundZ;
            if (collide) _currentPenetration = penetration;
            return true;
        }

        /// <summary>Removes the component of v that points into the surface (normal points away from it).</summary>
        private static void Clip(ref Vector3d v, Vector3d normal)
        {
            double into = v * normal;
            if (into < 0.0) v = v - normal * into;
        }

        private double Foot(BodyState body, double cameraZ)
        {
            return body.HasGround ? body.GroundZ : cameraZ - _body.EyeHeight;
        }
    }
}
