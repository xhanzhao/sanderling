using System;
using Sanderling.Navigation;
using Rhino;

namespace Sanderling.Settings
{
    /// <summary>
    /// Navigation speed levels (m/s). Each mouse wheel notch moves one level; the levels grow exponentially.
    /// </summary>
    internal static class SpeedLadder
    {
        private static readonly double[] Steps =
        {
            0.1, 0.15, 0.25, 0.35, 0.5, 0.75,
            1.0, 1.5, 2.0, 3.0, 4.0, 6.0,
            8.0, 12.0, 16.0, 24.0, 32.0, 50.0,
            75.0, 100.0, 150.0, 200.0   // fast levels for large sites / urban models
        };

        /// <summary>Default Fly level = 2.0 m/s</summary>
        public const int DefaultIndex = 8;

        /// <summary>Default Walk level = 1.5 m/s (normal walking speed)</summary>
        public const int DefaultWalkIndex = 7;

        public static int Count { get { return Steps.Length; } }

        public static int Clamp(int index)
        {
            if (index < 0) return 0;
            if (index >= Steps.Length) return Steps.Length - 1;
            return index;
        }

        public static double Get(int index)
        {
            return Steps[Clamp(index)];
        }
    }

    /// <summary>
    /// Common 35mm-equivalent lens lengths (mm) used by Alt + mouse wheel.
    /// The lens itself is not stored by Sanderling: it belongs to the Rhino viewport and stays there after exit.
    /// </summary>
    internal static class LensLadder
    {
        private static readonly double[] Steps =
        {
            14.0, 18.0, 20.0, 24.0, 28.0, 35.0, 50.0, 70.0, 85.0, 105.0, 135.0
        };

        public const double Min = 14.0;
        public const double Max = 135.0;

        /// <summary>
        /// Moves from the current lens length by the given number of steps.
        /// Works even when the current value is not on the ladder (e.g. 32mm steps up to 35mm, down to 28mm).
        /// </summary>
        public static double Step(double current, int steps)
        {
            const double eps = 0.01;
            double value = current;

            while (steps > 0)
            {
                double next = value;
                for (int i = 0; i < Steps.Length; i++)
                {
                    if (Steps[i] > value + eps) { next = Steps[i]; break; }
                }
                value = next;
                steps--;
            }

            while (steps < 0)
            {
                double next = value;
                for (int i = Steps.Length - 1; i >= 0; i--)
                {
                    if (Steps[i] < value - eps) { next = Steps[i]; break; }
                }
                value = next;
                steps++;
            }

            return value;
        }
    }

    /// <summary>
    /// Navigation parameters. Persisted through PlugIn.Settings and kept across Rhino restarts.
    /// Edit them in Rhino with the SanderlingSettings command.
    /// </summary>
    internal sealed class NavigationSettings
    {
        // ---- Defaults ----
        public const double DefaultMouseSensitivity = 0.12;   // degrees / pixel
        public const double DefaultLookSharpness = 30.0;      // look smoothing (higher = more responsive, 0 = off)
        public const double DefaultMoveSharpness = 10.0;      // acceleration smoothing (higher = snappier, 0 = off)
        public const double DefaultBoostMultiplier = 4.0;     // Shift: 2 m/s -> 8 m/s
        public const double DefaultPrecisionMultiplier = 0.25;// Ctrl:  2 m/s -> 0.5 m/s
        public const int DefaultMaxFps = 144;                 // frame rate cap (0 = unlimited)

        // ---- Walk mode defaults (meters) ----
        public const double DefaultEyeHeight = 1.65;
        public const double DefaultMaxStepHeight = 0.20;
        public const double DefaultGroundSearchDistance = 50.0;
        public const double DefaultGroundFollow = 12.0;       // height smoothing (higher = tighter to the ground, 0 = instant)

        public double MouseSensitivity { get; set; }
        public bool InvertY { get; set; }
        public LookMode LookMode { get; set; }
        public double LookSharpness { get; set; }
        public double MoveSharpness { get; set; }
        public double BoostMultiplier { get; set; }
        public double PrecisionMultiplier { get; set; }
        public int MaxFps { get; set; }
        /// <summary>Fly mode speed level (stored under the original "SpeedIndex" key).</summary>
        public int SpeedIndex { get; set; }
        public int WalkSpeedIndex { get; set; }
        public bool ShowHud { get; set; }

        /// <summary>Mode used by the Sanderling command (SanderlingFly / SanderlingWalk force a mode).</summary>
        public NavigationMode DefaultMode { get; set; }

        /// <summary>Walk mode: camera height above the ground, in meters.</summary>
        public double EyeHeight { get; set; }
        /// <summary>Walk mode: largest height difference climbed automatically, in meters.</summary>
        public double MaxStepHeight { get; set; }
        /// <summary>Walk mode: how far below to look for ground, in meters.</summary>
        public double GroundSearchDistance { get; set; }
        /// <summary>Walk mode: height smoothing sharpness (0 = snap instantly).</summary>
        public double GroundFollow { get; set; }

        public NavigationSettings()
        {
            ResetToDefaults();
        }

        public void ResetToDefaults()
        {
            MouseSensitivity = DefaultMouseSensitivity;
            InvertY = false;
            LookMode = LookMode.Drag;
            LookSharpness = DefaultLookSharpness;
            MoveSharpness = DefaultMoveSharpness;
            BoostMultiplier = DefaultBoostMultiplier;
            PrecisionMultiplier = DefaultPrecisionMultiplier;
            MaxFps = DefaultMaxFps;
            SpeedIndex = SpeedLadder.DefaultIndex;
            WalkSpeedIndex = SpeedLadder.DefaultWalkIndex;
            ShowHud = true;
            DefaultMode = NavigationMode.Fly;
            EyeHeight = DefaultEyeHeight;
            MaxStepHeight = DefaultMaxStepHeight;
            GroundSearchDistance = DefaultGroundSearchDistance;
            GroundFollow = DefaultGroundFollow;
        }

        // ---- Persistence ----
        private const string KeySensitivity = "MouseSensitivity";
        private const string KeyInvertY = "InvertY";
        // The look mode every session starts in. Only changed through SanderlingSettings
        // (Tab during navigation does not modify it). Earlier builds used the keys "LookMode" and
        // "MouseLookMode" to store the last used mode; those values are intentionally ignored.
        private const string KeyLookMode = "DefaultLookMode";
        private const string KeyLookSharpness = "LookSharpness";
        private const string KeyMoveSharpness = "MoveSharpness";
        private const string KeyBoost = "BoostMultiplier";
        private const string KeyPrecision = "PrecisionMultiplier";
        private const string KeyMaxFps = "MaxFps";
        private const string KeySpeedIndex = "SpeedIndex";
        // Whether the HUD is shown when a session starts (H during navigation does not modify it)
        private const string KeyShowHud = "ShowHud";
        private const string KeyWalkSpeedIndex = "WalkSpeedIndex";
        private const string KeyDefaultMode = "DefaultMode";
        private const string KeyEyeHeight = "EyeHeight";
        private const string KeyMaxStepHeight = "MaxStepHeight";
        private const string KeyGroundSearchDistance = "GroundSearchDistance";
        private const string KeyGroundFollow = "GroundFollow";

        public static NavigationSettings Load(PersistentSettings store)
        {
            NavigationSettings s = new NavigationSettings();
            if (store == null) return s;

            try
            {
                s.MouseSensitivity = Clamp(store.GetDouble(KeySensitivity, DefaultMouseSensitivity), 0.01, 2.0);
                s.InvertY = store.GetBool(KeyInvertY, false);
                int look = store.GetInteger(KeyLookMode, (int)LookMode.Drag);
                s.LookMode = look == (int)LookMode.Free ? LookMode.Free : LookMode.Drag;
                s.LookSharpness = Clamp(store.GetDouble(KeyLookSharpness, DefaultLookSharpness), 0.0, 100.0);
                s.MoveSharpness = Clamp(store.GetDouble(KeyMoveSharpness, DefaultMoveSharpness), 0.0, 100.0);
                s.BoostMultiplier = Clamp(store.GetDouble(KeyBoost, DefaultBoostMultiplier), 1.0, 20.0);
                s.PrecisionMultiplier = Clamp(store.GetDouble(KeyPrecision, DefaultPrecisionMultiplier), 0.01, 1.0);
                s.MaxFps = (int)Clamp(store.GetInteger(KeyMaxFps, DefaultMaxFps), 0, 500);
                s.SpeedIndex = SpeedLadder.Clamp(store.GetInteger(KeySpeedIndex, SpeedLadder.DefaultIndex));
                s.ShowHud = store.GetBool(KeyShowHud, true);
                s.WalkSpeedIndex = SpeedLadder.Clamp(store.GetInteger(KeyWalkSpeedIndex, SpeedLadder.DefaultWalkIndex));
                int mode = store.GetInteger(KeyDefaultMode, (int)NavigationMode.Fly);
                s.DefaultMode = mode == (int)NavigationMode.Walk ? NavigationMode.Walk : NavigationMode.Fly;
                s.EyeHeight = Clamp(store.GetDouble(KeyEyeHeight, DefaultEyeHeight), 0.3, 3.0);
                s.MaxStepHeight = Clamp(store.GetDouble(KeyMaxStepHeight, DefaultMaxStepHeight), 0.0, 1.0);
                s.GroundSearchDistance = Clamp(store.GetDouble(KeyGroundSearchDistance, DefaultGroundSearchDistance), 1.0, 1000.0);
                s.GroundFollow = Clamp(store.GetDouble(KeyGroundFollow, DefaultGroundFollow), 0.0, 100.0);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("Sanderling: Failed to read settings; using defaults. ({0})", ex.Message);
                s.ResetToDefaults();
            }
            return s;
        }

        public void Save(PersistentSettings store)
        {
            if (store == null) return;

            store.SetDouble(KeySensitivity, MouseSensitivity);
            store.SetBool(KeyInvertY, InvertY);
            store.SetInteger(KeyLookMode, (int)LookMode);
            store.SetDouble(KeyLookSharpness, LookSharpness);
            store.SetDouble(KeyMoveSharpness, MoveSharpness);
            store.SetDouble(KeyBoost, BoostMultiplier);
            store.SetDouble(KeyPrecision, PrecisionMultiplier);
            store.SetInteger(KeyMaxFps, MaxFps);
            store.SetInteger(KeySpeedIndex, SpeedIndex);
            store.SetBool(KeyShowHud, ShowHud);
            store.SetInteger(KeyWalkSpeedIndex, WalkSpeedIndex);
            store.SetInteger(KeyDefaultMode, (int)DefaultMode);
            store.SetDouble(KeyEyeHeight, EyeHeight);
            store.SetDouble(KeyMaxStepHeight, MaxStepHeight);
            store.SetDouble(KeyGroundSearchDistance, GroundSearchDistance);
            store.SetDouble(KeyGroundFollow, GroundFollow);
        }

        private static double Clamp(double v, double min, double max)
        {
            if (double.IsNaN(v)) return min;
            return v < min ? min : (v > max ? max : v);
        }
    }
}
