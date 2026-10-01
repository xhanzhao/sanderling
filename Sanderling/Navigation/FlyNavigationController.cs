using Sanderling.Input;
using Sanderling.Settings;
using Rhino.Geometry;

namespace Sanderling.Navigation
{
    /// <summary>
    /// Fly mode (V1 behavior, unchanged):
    ///   forward/back = CameraDirection (including pitch)
    ///   left/right   = CameraX (horizontal)
    ///   up/down      = World Z (Q / E, independent of camera tilt)
    /// </summary>
    internal sealed class FlyNavigationController : NavigationController
    {
        public FlyNavigationController(CameraController camera, NavigationState state,
                                       NavigationSettings settings, double metersToModel)
            : base(camera, state, settings, metersToModel)
        {
        }

        protected override bool UpdateMovement(InputFrame input, double dt)
        {
            NavigationState s = State;

            Vector3d forward = CameraController.Forward(s.Yaw, s.Pitch);
            Vector3d right = CameraController.Right(s.Yaw);
            Vector3d wish = forward * input.Forward
                          + right * input.Strafe
                          + Vector3d.ZAxis * input.Vertical;

            Vector3d velocity = SmoothVelocity(s.Velocity, TargetVelocity(wish), dt);
            s.Velocity = velocity;

            if (velocity.IsZero || dt <= 0.0) return false;

            // distance = speed * deltaTime
            s.Position = s.Position + velocity * dt;
            return true;
        }
    }
}
