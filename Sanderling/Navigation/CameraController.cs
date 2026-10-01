using System;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace Sanderling.Navigation
{
    /// <summary>
    /// Converts between "position + Yaw/Pitch" and the Rhino viewport camera.
    /// This is the only class that modifies the RhinoViewport directly.
    /// </summary>
    internal sealed class CameraController
    {
        private readonly RhinoViewport _viewport;
        private readonly double _fallbackTargetDistance;
        private double _targetDistance;

        /// <param name="viewport">Target viewport</param>
        /// <param name="metersToModel">Number of model units per meter</param>
        public CameraController(RhinoViewport viewport, double metersToModel)
        {
            if (viewport == null) throw new ArgumentNullException("viewport");
            _viewport = viewport;
            _fallbackTargetDistance = 10.0 * metersToModel;
            _targetDistance = _fallbackTargetDistance;
        }

        public RhinoViewport Viewport { get { return _viewport; } }

        /// <summary>Lens length used when converting a parallel view (matches Rhino's default Perspective viewport).</summary>
        private const double ParallelConversionLens = 50.0;

        /// <summary>
        /// Ensures the viewport uses a regular perspective projection (free flight is not possible in parallel / two-point perspective).
        /// Also pushes the current view onto the view undo stack so UndoView can return to it after exit.
        ///
        /// Lens length:
        ///   Perspective    -> unchanged
        ///   Two-point      -> converted to perspective, keeping its own lens length
        ///   Parallel       -> converted to perspective with 50mm
        /// </summary>
        public void PrepareViewport()
        {
            _viewport.PushViewProjection();

            if (_viewport.IsTwoPointPerspectiveProjection)
            {
                double lens = _viewport.Camera35mmLensLength;
                if (!RhinoMath.IsValidDouble(lens) || lens <= 0.0) lens = ParallelConversionLens;

                _viewport.ChangeToPerspectiveProjection(true, lens);
                RhinoApp.WriteLine("Sanderling: Two-point perspective switched to perspective (lens kept at {0:0.#}mm).", lens);
            }
            else if (_viewport.IsParallelProjection)
            {
                _viewport.ChangeToPerspectiveProjection(true, ParallelConversionLens);
                RhinoApp.WriteLine("Sanderling: Parallel view switched to perspective projection ({0:0}mm).", ParallelConversionLens);
            }
        }

        /// <summary>Current 35mm-equivalent lens length of the viewport.</summary>
        public double LensLength
        {
            get { return _viewport.Camera35mmLensLength; }
        }

        /// <summary>Sets the 35mm-equivalent lens length of the viewport. The camera location is kept.</summary>
        public void SetLensLength(double lens)
        {
            _viewport.Camera35mmLensLength = lens;
        }

        /// <summary>Reads the camera from the current viewport and initializes the navigation state.</summary>
        public void ReadCamera(NavigationState state)
        {
            Point3d location = _viewport.CameraLocation;
            Vector3d dir = _viewport.CameraDirection;
            if (!dir.Unitize()) dir = Vector3d.YAxis;

            double yaw;
            if (Math.Abs(dir.Z) > 0.999)
            {
                // Looking straight down/up the direction has no horizontal component, so infer the heading from CameraUp:
                // when looking down, screen "up" is the forward direction; when looking up, it is the opposite.
                Vector3d up = _viewport.CameraUp;
                Vector3d horizontal = dir.Z < 0 ? up : -up;
                yaw = (Math.Abs(horizontal.X) + Math.Abs(horizontal.Y) > 1e-9)
                    ? Math.Atan2(horizontal.Y, horizontal.X)
                    : Math.PI * 0.5;
            }
            else
            {
                yaw = Math.Atan2(dir.Y, dir.X);
            }

            double pitch = NavigationController.ClampPitch(Math.Asin(Math.Max(-1.0, Math.Min(1.0, dir.Z))));

            state.Position = location;
            state.Yaw = yaw;
            state.TargetYaw = yaw;
            state.Pitch = pitch;
            state.TargetPitch = pitch;
            state.Velocity = Vector3d.Zero;

            double d = location.DistanceTo(_viewport.CameraTarget);
            _targetDistance = (RhinoMath.IsValidDouble(d) && d > RhinoMath.ZeroTolerance) ? d : _fallbackTargetDistance;
        }

        /// <summary>Forward direction (including pitch).</summary>
        public static Vector3d Forward(double yaw, double pitch)
        {
            double cp = Math.Cos(pitch);
            return new Vector3d(cp * Math.Cos(yaw), cp * Math.Sin(yaw), Math.Sin(pitch));
        }

        /// <summary>Horizontal right direction (= CameraX when there is no roll).</summary>
        public static Vector3d Right(double yaw)
        {
            return new Vector3d(Math.Sin(yaw), -Math.Cos(yaw), 0.0);
        }

        /// <summary>Writes the navigation state to the viewport camera.</summary>
        public void Apply(Point3d location, double yaw, double pitch)
        {
            Vector3d forward = Forward(yaw, pitch);
            Vector3d right = Right(yaw);
            Vector3d up = Vector3d.CrossProduct(right, forward);
            if (!up.Unitize()) up = Vector3d.ZAxis;

            // Keep the camera-to-target distance constant so Rhino's orbit center stays reasonable after exit
            Point3d target = location + forward * _targetDistance;

            _viewport.SetCameraLocations(target, location);
            _viewport.CameraUp = up;
        }
    }
}
