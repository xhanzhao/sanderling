using System;
using System.Diagnostics;
using System.Globalization;
using Sanderling.Input;
using Sanderling.Physics;
using Sanderling.Scene;
using Sanderling.Settings;
using Rhino;
using Rhino.Geometry;

namespace Sanderling.Navigation
{
    /// <summary>
    /// Walk mode: architectural walkthrough at eye height, following the ground, with collision (V3).
    ///
    /// Movement
    ///   - WASD moves horizontally only: forward is the camera direction projected onto World XY.
    ///   - Q / E are ignored; height is determined by the ground.
    ///   - Camera height = ground Z + eye height, approached smoothly (GroundFollow).
    ///
    /// Ground, steps and edges (see CollisionController for details)
    ///   - Rises up to MaxStepHeight are climbed, drops are allowed, the edge of the model blocks.
    ///   - Ground uses a five-ray footprint for stability on edges and across small gaps.
    ///
    /// Collision (toggled with C, session-only)
    ///   - The body is a vertical capsule starting at the step height; walls push it out and it slides along them.
    ///   - Low ceilings and overhangs inside the body height block movement.
    /// </summary>
    internal sealed class WalkNavigationController : NavigationController
    {
        private readonly RhinoDoc _doc;
        private readonly CollisionSettings _collisionSettings;
        private readonly double _epsilon;

        private CollisionController _collision; // created the first time Walk mode is activated in a session
        private BodyState _body;
        private bool _lastCollisionEnabled;

        public WalkNavigationController(CameraController camera, NavigationState state, NavigationSettings settings,
                                        CollisionSettings collisionSettings, double metersToModel, RhinoDoc doc)
            : base(camera, state, settings, metersToModel)
        {
            _doc = doc;
            _collisionSettings = collisionSettings;
            _epsilon = 0.001 * metersToModel;
        }

        private double EyeHeight { get { return Settings.EyeHeight * MetersToModel; } }

        public override void OnActivated()
        {
            EnsureCollision();

            NavigationState s = State;
            s.Velocity = new Vector3d(s.Velocity.X, s.Velocity.Y, 0.0);

            // Look for ground straight below the camera
            _body.X = s.Position.X;
            _body.Y = s.Position.Y;
            double groundZ;
            _body.HasGround = _collision.FindInitialGround(s.Position.X, s.Position.Y, s.Position.Z, out groundZ);
            _body.GroundZ = groundZ;

            _lastCollisionEnabled = s.CollisionEnabled;
            _collision.ResetPenetration(_body, s.Position.Z, s.CollisionEnabled);

            s.HasGround = _body.HasGround;
            s.HudDirty = true;

            if (!_body.HasGround)
            {
                RhinoApp.WriteLine("Sanderling: No ground found within {0:0.#} m below the camera. Walking at the current height.",
                                   Settings.GroundSearchDistance);
            }
        }

        protected override bool UpdateMovement(InputFrame input, double dt)
        {
            NavigationState s = State;

            // Collision switched with C: re-measure so the "never go deeper" rule starts from the current state
            if (s.CollisionEnabled != _lastCollisionEnabled)
            {
                _lastCollisionEnabled = s.CollisionEnabled;
                _body.X = s.Position.X;
                _body.Y = s.Position.Y;
                _collision.ResetPenetration(_body, s.Position.Z, s.CollisionEnabled);
            }

            // Horizontal forward direction, computed from yaw so it never degenerates when looking straight up/down
            Vector3d forward = new Vector3d(Math.Cos(s.Yaw), Math.Sin(s.Yaw), 0.0);
            Vector3d right = CameraController.Right(s.Yaw);
            Vector3d wish = forward * input.Forward + right * input.Strafe; // Q / E ignored in Walk mode

            Vector3d current = new Vector3d(s.Velocity.X, s.Velocity.Y, 0.0);
            Vector3d velocity = SmoothVelocity(current, TargetVelocity(wish), dt);

            bool moved = false;
            if (!velocity.IsZero && dt > 0.0)
            {
                long start = Stopwatch.GetTimestamp();

                _body.X = s.Position.X;
                _body.Y = s.Position.Y;
                moved = _collision.Move(ref _body, velocity * dt, ref velocity, s.CollisionEnabled, s.Position.Z);

                double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                s.PhysicsMilliseconds = s.PhysicsMilliseconds <= 0.0 ? ms : s.PhysicsMilliseconds * 0.9 + ms * 0.1;
                s.PhysicsTriangles = _collision.LastTrianglesTested;

                if (moved)
                {
                    s.Position = new Point3d(_body.X, _body.Y, s.Position.Z);
                }
                if (s.HasGround != _body.HasGround)
                {
                    s.HasGround = _body.HasGround;
                    s.HudDirty = true;
                }
            }
            s.Velocity = velocity;

            bool heightChanged = FollowGround(dt);
            return moved || heightChanged;
        }

        /// <summary>Smoothly moves the camera height towards ground Z + eye height.</summary>
        private bool FollowGround(double dt)
        {
            if (!_body.HasGround) return false;

            NavigationState s = State;
            double target = _body.GroundZ + EyeHeight;
            double z = s.Position.Z;

            double blend = Blend(Settings.GroundFollow, dt);
            double newZ = z + (target - z) * blend;
            if (Math.Abs(target - newZ) < _epsilon) newZ = target;
            if (newZ == z) return false;

            s.Position = new Point3d(s.Position.X, s.Position.Y, newZ);
            return true;
        }

        private void EnsureCollision()
        {
            if (_collision != null) return;

            RhinoApp.WriteLine("Sanderling: Preparing navigation geometry...");
            RhinoApp.Wait(); // let the command line show the message before a potentially long build

            CollisionLayerFilter filter = CollisionLayerFilter.Load(_doc);
            SceneStats stats;
            SpatialIndex index = NavigationScene.ForDocument(_doc).Prepare(_doc, _collisionSettings, filter, out stats);

            NavigationBody body = NavigationBody.Create(Settings, _collisionSettings, MetersToModel);
            _collision = new CollisionController(index, body, Settings.GroundSearchDistance * MetersToModel, MetersToModel);

            RhinoApp.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "Sanderling: Navigation geometry ready - {0} objects ({1} reused, {2} rebuilt), {3} meshes, {4} triangles ({5:0} ms).",
                stats.Objects, stats.Reused, stats.Rebuilt, stats.Meshes, stats.Triangles, stats.Milliseconds));

            if (filter.Mode != CollisionLayerMode.All)
            {
                RhinoApp.WriteLine("Sanderling: Layer filter - {0}", filter.Describe(_doc));
            }
            if (stats.Meshes == 0)
            {
                RhinoApp.WriteLine("Sanderling: No surfaces or meshes take part; Walk mode has no ground or walls.");
            }
        }
    }
}
