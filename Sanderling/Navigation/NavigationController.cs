using System;
using Sanderling.Input;
using Sanderling.Settings;
using Rhino;
using Rhino.Geometry;

namespace Sanderling.Navigation
{
    /// <summary>
    /// Base class of the navigation engine. Handles everything shared by all navigation modes:
    /// pause on focus loss, mouse wheel speed, Alt + wheel lens, Shift / Ctrl modifiers and mouse look.
    /// Derived classes implement UpdateMovement (FlyNavigationController, WalkNavigationController).
    ///
    /// Data flow: InputController -> NavigationController -> CameraController -> RhinoViewport
    ///
    /// All motion is based on deltaTime (distance = speed * dt), and smoothing uses
    /// frame-rate independent exponential decay: blend = 1 - exp(-sharpness * dt).
    /// Movement speed and feel are therefore identical at 30 / 60 / 144 FPS.
    /// </summary>
    internal abstract class NavigationController
    {
        private static readonly double MaxPitch = RhinoMath.ToRadians(89.0);

        private readonly CameraController _camera;
        private readonly NavigationState _state;
        private readonly NavigationSettings _settings;
        private readonly double _metersToModel;
        private readonly double _stopThreshold;

        protected NavigationController(CameraController camera, NavigationState state,
                                       NavigationSettings settings, double metersToModel)
        {
            _camera = camera;
            _state = state;
            _settings = settings;
            _metersToModel = metersToModel;
            _stopThreshold = 0.001 * metersToModel; // Below 1 mm/s is treated as stopped
        }

        protected NavigationState State { get { return _state; } }
        protected NavigationSettings Settings { get { return _settings; } }
        protected double MetersToModel { get { return _metersToModel; } }

        /// <summary>Called when this controller becomes the active one (session start or mode switch).</summary>
        public virtual void OnActivated()
        {
        }

        /// <summary>
        /// Advances one frame.
        /// </summary>
        /// <returns>Whether the camera changed (a redraw is needed).</returns>
        public bool Tick(InputFrame input, double dt)
        {
            NavigationState s = _state;

            // ---- Focus / pause ----
            bool paused = !input.HasFocus;
            if (paused != s.IsPaused)
            {
                s.IsPaused = paused;
                s.HudDirty = true;
            }
            if (paused)
            {
                s.Velocity = Vector3d.Zero;
                s.TargetYaw = s.Yaw;
                s.TargetPitch = s.Pitch;
                if (s.Boost || s.Precision)
                {
                    s.Boost = s.Precision = false;
                    s.HudDirty = true;
                }
                return false;
            }

            // ---- Mouse wheel speed adjustment (each mode has its own speed level) ----
            if (input.SpeedSteps != 0)
            {
                int newIndex = SpeedLadder.Clamp(s.SpeedIndex + input.SpeedSteps);
                if (newIndex != s.SpeedIndex)
                {
                    s.SpeedIndex = newIndex;
                    s.HudDirty = true;
                }
            }

            // ---- Alt + mouse wheel lens adjustment ----
            bool lensChanged = false;
            if (input.LensSteps != 0)
            {
                double newLens = LensLadder.Step(s.LensLength, input.LensSteps);
                if (Math.Abs(newLens - s.LensLength) > 1e-6)
                {
                    s.LensLength = newLens;
                    _camera.SetLensLength(newLens);
                    s.HudDirty = true;
                    lensChanged = true;
                }
            }

            // ---- Shift / Ctrl ----
            bool precision = input.Precision;
            bool boost = input.Boost && !precision;
            if (boost != s.Boost || precision != s.Precision)
            {
                s.Boost = boost;
                s.Precision = precision;
                s.HudDirty = true;
            }

            bool changed = UpdateLook(input, dt);
            changed |= UpdateMovement(input, dt);

            if (changed)
            {
                _camera.Apply(s.Position, s.Yaw, s.Pitch);
            }
            return changed || lensChanged;
        }

        /// <summary>
        /// Mode-specific movement. Updates State.Position / State.Velocity.
        /// </summary>
        /// <returns>Whether the position changed.</returns>
        protected abstract bool UpdateMovement(InputFrame input, double dt);

        // ------------------------------------------------------------------
        // Look
        // ------------------------------------------------------------------

        private bool UpdateLook(InputFrame input, double dt)
        {
            NavigationState s = _state;

            if (input.LookDeltaX != 0.0 || input.LookDeltaY != 0.0)
            {
                double radPerPixel = RhinoMath.ToRadians(_settings.MouseSensitivity);
                double dy = _settings.InvertY ? -input.LookDeltaY : input.LookDeltaY;

                // Mouse right -> turn right (clockwise seen from above, Yaw decreases)
                s.TargetYaw -= input.LookDeltaX * radPerPixel;
                // Mouse up (screen dy < 0) -> look up (Pitch increases)
                s.TargetPitch = ClampPitch(s.TargetPitch - dy * radPerPixel);
            }

            double blend = Blend(_settings.LookSharpness, dt);
            double yaw = s.Yaw + (s.TargetYaw - s.Yaw) * blend;
            double pitch = s.Pitch + (s.TargetPitch - s.Pitch) * blend;

            if (Math.Abs(s.TargetYaw - yaw) < 1e-6) yaw = s.TargetYaw;
            if (Math.Abs(s.TargetPitch - pitch) < 1e-6) pitch = s.TargetPitch;

            bool changed = yaw != s.Yaw || pitch != s.Pitch;
            s.Yaw = yaw;
            s.Pitch = pitch;

            // Keep the value from growing too large after long rotations (shift current and target together; smoothing unaffected)
            if (Math.Abs(s.Yaw) > 100.0 * Math.PI)
            {
                double wrap = Math.Round(s.Yaw / (2.0 * Math.PI)) * 2.0 * Math.PI;
                s.Yaw -= wrap;
                s.TargetYaw -= wrap;
            }

            return changed;
        }

        // ------------------------------------------------------------------
        // Helpers for derived controllers
        // ------------------------------------------------------------------

        /// <summary>
        /// Converts a desired direction (any length) into a target velocity in model units / second,
        /// using the current effective speed. Returns zero when there is no input.
        /// Normalization makes diagonal movement (e.g. W+D) as fast as straight movement.
        /// </summary>
        protected Vector3d TargetVelocity(Vector3d wishDirection)
        {
            if (!wishDirection.Unitize()) return Vector3d.Zero;
            double speed = _state.EffectiveSpeed(_settings) * _metersToModel; // model units / second
            return wishDirection * speed;
        }

        /// <summary>Smoothly moves the current velocity towards the target velocity (acceleration / deceleration).</summary>
        protected Vector3d SmoothVelocity(Vector3d current, Vector3d target, double dt)
        {
            double blend = Blend(_settings.MoveSharpness, dt);
            Vector3d velocity = current + (target - current) * blend;

            if (target.IsZero && velocity.Length < _stopThreshold)
            {
                velocity = Vector3d.Zero;
            }
            return velocity;
        }

        // ------------------------------------------------------------------
        // Utilities
        // ------------------------------------------------------------------

        /// <summary>Frame-rate independent exponential smoothing factor. sharpness <= 0 means no smoothing.</summary>
        public static double Blend(double sharpness, double dt)
        {
            if (sharpness <= 0.0) return 1.0;
            return 1.0 - Math.Exp(-sharpness * dt);
        }

        public static double ClampPitch(double pitch)
        {
            if (pitch > MaxPitch) return MaxPitch;
            if (pitch < -MaxPitch) return -MaxPitch;
            return pitch;
        }

        /// <summary>
        /// Number of model units per meter, e.g. millimeters -> 1000, meters -> 1, feet -> 3.2808.
        /// </summary>
        public static double MetersToModelUnits(RhinoDoc doc)
        {
            UnitSystem units = doc.ModelUnitSystem;

            if (units == UnitSystem.None || units == UnitSystem.CustomUnits)
            {
                RhinoApp.WriteLine("Sanderling: Model unit system is {0}; navigation speeds are treated as meters.", units);
                return 1.0;
            }

            double scale = RhinoMath.UnitScale(UnitSystem.Meters, units);
            if (!RhinoMath.IsValidDouble(scale) || scale <= 0.0) return 1.0;
            return scale;
        }
    }
}
