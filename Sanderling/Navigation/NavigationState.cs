using Sanderling.Settings;
using Rhino.Geometry;

namespace Sanderling.Navigation
{
    /// <summary>Navigation mode. F toggles between them during navigation.</summary>
    public enum NavigationMode
    {
        Fly = 0,
        Walk = 1
    }

    /// <summary>Mouse look mode.</summary>
    public enum LookMode
    {
        /// <summary>Rotate the view while dragging with the left/right mouse button held (Enscape style). Default.</summary>
        Drag = 0,
        /// <summary>Free look: the cursor is hidden and locked to the viewport; moving the mouse rotates the view (game style).</summary>
        Free = 1
    }

    /// <summary>
    /// Runtime navigation state. Updated by NavigationController, read by NavigationHUD.
    /// Position/velocity use Rhino model units; speed levels use meters per second.
    /// </summary>
    internal sealed class NavigationState
    {
        public NavigationMode Mode { get; set; }
        public LookMode LookMode { get; set; }

        /// <summary>Camera position (model units).</summary>
        public Point3d Position { get; set; }

        /// <summary>Current (smoothed) yaw in radians around World Z; 0 = +X direction, counter-clockwise positive.</summary>
        public double Yaw { get; set; }
        /// <summary>Current (smoothed) pitch in radians, clamped to +/-89 degrees.</summary>
        public double Pitch { get; set; }

        /// <summary>Target angles from mouse input; Yaw/Pitch smoothly follow them.</summary>
        public double TargetYaw { get; set; }
        public double TargetPitch { get; set; }

        /// <summary>Current velocity vector (model units / second).</summary>
        public Vector3d Velocity { get; set; }

        /// <summary>Current viewport lens length (35mm-equivalent, mm). Changed with Alt + mouse wheel.</summary>
        public double LensLength { get; set; }

        /// <summary>Mouse wheel speed level index used in Fly mode, see SpeedLadder.</summary>
        public int FlySpeedIndex { get; set; }

        /// <summary>Mouse wheel speed level index used in Walk mode, see SpeedLadder.</summary>
        public int WalkSpeedIndex { get; set; }

        /// <summary>Speed level index of the current mode (each mode remembers its own level).</summary>
        public int SpeedIndex
        {
            get { return Mode == NavigationMode.Walk ? WalkSpeedIndex : FlySpeedIndex; }
            set
            {
                if (Mode == NavigationMode.Walk) WalkSpeedIndex = value;
                else FlySpeedIndex = value;
            }
        }

        /// <summary>Walk mode: whether ground was found below the user (HUD shows NO GROUND otherwise).</summary>
        public bool HasGround { get; set; }

        /// <summary>Walk mode collision. Starts from SanderlingSettings, toggled with C (session-only).</summary>
        public bool CollisionEnabled { get; set; }

        /// <summary>Show physics stats in the HUD (SanderlingSettings).</summary>
        public bool ShowStats { get; set; }

        /// <summary>Smoothed time spent in collision / ground queries per frame (ms).</summary>
        public double PhysicsMilliseconds { get; set; }

        /// <summary>Triangles tested in the last frame.</summary>
        public int PhysicsTriangles { get; set; }

        public bool Boost { get; set; }
        public bool Precision { get; set; }
        public bool IsPaused { get; set; }

        /// <summary>Redraw frame rate (for HUD display only).</summary>
        public double Fps { get; set; }

        /// <summary>Model unit system name (HUD display).</summary>
        public string UnitSystemName { get; set; }

        /// <summary>Whether the HUD panel is shown. Toggled with H (session-only).</summary>
        public bool HudVisible { get; set; }

        /// <summary>Short-lived HUD message (e.g. after saving a view); null when nothing is shown.</summary>
        public string FlashMessage { get; set; }

        /// <summary>HUD content changed and needs a redraw (even if the camera did not move).</summary>
        public bool HudDirty { get; set; }

        /// <summary>Base speed set by the mouse wheel (m/s).</summary>
        public double BaseSpeed
        {
            get { return SpeedLadder.Get(SpeedIndex); }
        }

        /// <summary>Actual speed after Shift / Ctrl (m/s). Ctrl takes precedence over Shift.</summary>
        public double EffectiveSpeed(NavigationSettings settings)
        {
            double v = BaseSpeed;
            if (Precision) return v * settings.PrecisionMultiplier;
            if (Boost) return v * settings.BoostMultiplier;
            return v;
        }
    }
}
