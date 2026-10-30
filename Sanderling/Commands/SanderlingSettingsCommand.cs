using Sanderling.Navigation;
using Sanderling.Settings;
using Sanderling.UI;
using Rhino;
using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;
using Rhino.UI;

namespace Sanderling.Commands
{
    /// <summary>
    /// SanderlingSettings - opens the Sanderling settings window.
    /// Run as -SanderlingSettings (scripted mode, e.g. from a macro) it uses the command-line options instead:
    /// The main page holds navigation and Walk settings; the "Collision" option opens a second page with
    /// the collision body, object type filters and stats. Enter on the main page saves, Esc discards all changes.
    /// Layer filtering is a separate command (SanderlingLayers) because it is stored per document.
    /// </summary>
    [System.Runtime.InteropServices.Guid("560fe617-c722-4511-b0de-eadb35533e09")]
    public class SanderlingSettingsCommand : Command
    {
        public override string EnglishName
        {
            get { return "SanderlingSettings"; }
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            if (NavigationSession.IsRunning) return Result.Nothing;

            PersistentSettings store = SanderlingPlugIn.Instance != null ? SanderlingPlugIn.Instance.Settings : null;
            NavigationSettings s = NavigationSettings.Load(store);
            CollisionSettings c = CollisionSettings.Load(store);

            if (mode == RunMode.Interactive)
            {
                SettingsDialog dialog = new SettingsDialog(s, c);
                dialog.ShowCentered(RhinoEtoApp.MainWindow);
                if (!dialog.Saved) return Result.Cancel;

                s.Save(store);
                c.Save(store);
                RhinoApp.WriteLine("Sanderling: Settings saved.");
                return Result.Success;
            }

            string[] lookModes = { "Drag", "Free" }; // order matches the LookMode enum values
            string[] modes = { "Fly", "Walk" };      // order matches the NavigationMode enum values

            GetOption go = new GetOption();
            go.SetCommandPrompt("Sanderling settings (lengths in meters, press Enter to save)");
            go.AcceptNothing(true);

            while (true)
            {
                go.ClearCommandOptions();

                OptionDouble sensitivity = new OptionDouble(s.MouseSensitivity, 0.01, 2.0);
                OptionToggle invertY = new OptionToggle(s.InvertY, "No", "Yes");
                OptionDouble lookSmoothing = new OptionDouble(s.LookSharpness, 0.0, 100.0);
                OptionDouble moveSmoothing = new OptionDouble(s.MoveSharpness, 0.0, 100.0);
                OptionDouble boost = new OptionDouble(s.BoostMultiplier, 1.0, 20.0);
                OptionDouble precision = new OptionDouble(s.PrecisionMultiplier, 0.01, 1.0);
                OptionInteger maxFps = new OptionInteger(s.MaxFps, 0, 500);
                OptionToggle showHud = new OptionToggle(s.ShowHud, "No", "Yes");
                OptionDouble eyeHeight = new OptionDouble(s.EyeHeight, 0.3, 3.0);
                OptionDouble maxStep = new OptionDouble(s.MaxStepHeight, 0.0, 1.0);
                OptionDouble searchDistance = new OptionDouble(s.GroundSearchDistance, 1.0, 1000.0);
                OptionDouble groundFollow = new OptionDouble(s.GroundFollow, 0.0, 100.0);

                go.AddOptionDouble("MouseSensitivity", ref sensitivity);
                go.AddOptionToggle("InvertY", ref invertY);
                // Look mode every navigation session starts in (Tab switches it temporarily during navigation)
                int lookModeIndex = go.AddOptionList("LookMode", lookModes, (int)s.LookMode);
                go.AddOptionDouble("LookSmoothing", ref lookSmoothing);
                go.AddOptionDouble("MoveSmoothing", ref moveSmoothing);
                go.AddOptionDouble("BoostMultiplier", ref boost);
                go.AddOptionDouble("PrecisionMultiplier", ref precision);
                go.AddOptionInteger("MaxFps", ref maxFps);
                // Whether the HUD is shown when navigation starts (H toggles it temporarily during navigation)
                go.AddOptionToggle("ShowHud", ref showHud);
                // Mode used by the Sanderling command (F switches modes temporarily during navigation)
                int modeIndex = go.AddOptionList("DefaultMode", modes, (int)s.DefaultMode);
                // Walk mode
                go.AddOptionDouble("EyeHeight", ref eyeHeight);
                go.AddOptionDouble("MaxStepHeight", ref maxStep);
                go.AddOptionDouble("GroundSearchDistance", ref searchDistance);
                go.AddOptionDouble("GroundFollow", ref groundFollow);
                // Opens the collision settings page
                int collisionIndex = go.AddOption("Collision");
                int resetIndex = go.AddOption("ResetDefaults");

                GetResult res = go.Get();

                // Numeric options are updated during Get(), so write them back first
                s.MouseSensitivity = sensitivity.CurrentValue;
                s.InvertY = invertY.CurrentValue;
                s.LookSharpness = lookSmoothing.CurrentValue;
                s.MoveSharpness = moveSmoothing.CurrentValue;
                s.BoostMultiplier = boost.CurrentValue;
                s.PrecisionMultiplier = precision.CurrentValue;
                s.MaxFps = maxFps.CurrentValue;
                s.ShowHud = showHud.CurrentValue;
                s.EyeHeight = eyeHeight.CurrentValue;
                s.MaxStepHeight = maxStep.CurrentValue;
                s.GroundSearchDistance = searchDistance.CurrentValue;
                s.GroundFollow = groundFollow.CurrentValue;

                if (res == GetResult.Option)
                {
                    CommandLineOption opt = go.Option();
                    if (opt != null)
                    {
                        if (opt.Index == lookModeIndex)
                        {
                            s.LookMode = opt.CurrentListOptionIndex == (int)LookMode.Free ? LookMode.Free : LookMode.Drag;
                        }
                        else if (opt.Index == modeIndex)
                        {
                            s.DefaultMode = opt.CurrentListOptionIndex == (int)NavigationMode.Walk ? NavigationMode.Walk : NavigationMode.Fly;
                        }
                        else if (opt.Index == collisionIndex)
                        {
                            EditCollision(c);
                        }
                        else if (opt.Index == resetIndex)
                        {
                            s.ResetToDefaults();
                            c.ResetToDefaults();
                            RhinoApp.WriteLine("Sanderling: Settings reset to defaults.");
                        }
                    }
                    continue;
                }

                if (res == GetResult.Nothing) break;

                return Result.Cancel; // Esc: do not save
            }

            s.Save(store);
            c.Save(store);
            RhinoApp.WriteLine("Sanderling: Settings saved.");
            return Result.Success;
        }

        /// <summary>
        /// Collision settings page. Enter or Esc returns to the main page; changes are only saved
        /// when the main page is confirmed with Enter.
        /// </summary>
        private static void EditCollision(CollisionSettings c)
        {
            GetOption go = new GetOption();
            go.SetCommandPrompt("Sanderling collision settings (lengths in meters, press Enter to go back)");
            go.AcceptNothing(true);

            while (true)
            {
                go.ClearCommandOptions();

                OptionToggle collision = new OptionToggle(c.CollisionEnabled, "No", "Yes");
                OptionDouble radius = new OptionDouble(c.BodyRadius, 0.05, 1.0);
                OptionDouble height = new OptionDouble(c.BodyHeight, 0.5, 3.0);
                OptionToggle meshes = new OptionToggle(c.CollideMeshes, "No", "Yes");
                OptionToggle breps = new OptionToggle(c.CollideBreps, "No", "Yes");
                OptionToggle extrusions = new OptionToggle(c.CollideExtrusions, "No", "Yes");
                OptionToggle subd = new OptionToggle(c.CollideSubD, "No", "Yes");
                OptionToggle blocks = new OptionToggle(c.CollideBlocks, "No", "Yes");
                OptionToggle locked = new OptionToggle(c.CollideLocked, "No", "Yes");
                OptionToggle stats = new OptionToggle(c.ShowStats, "No", "Yes");

                // Whether collision is on when a Walk session starts (C toggles it temporarily during navigation)
                go.AddOptionToggle("Collision", ref collision);
                go.AddOptionDouble("BodyRadius", ref radius);
                go.AddOptionDouble("BodyHeight", ref height);
                // Object types used for both ground detection and collision
                go.AddOptionToggle("Meshes", ref meshes);
                go.AddOptionToggle("Breps", ref breps);
                go.AddOptionToggle("Extrusions", ref extrusions);
                go.AddOptionToggle("SubD", ref subd);
                go.AddOptionToggle("Blocks", ref blocks);
                go.AddOptionToggle("LockedObjects", ref locked);
                go.AddOptionToggle("ShowStats", ref stats);

                GetResult res = go.Get();

                c.CollisionEnabled = collision.CurrentValue;
                c.BodyRadius = radius.CurrentValue;
                c.BodyHeight = height.CurrentValue;
                c.CollideMeshes = meshes.CurrentValue;
                c.CollideBreps = breps.CurrentValue;
                c.CollideExtrusions = extrusions.CurrentValue;
                c.CollideSubD = subd.CurrentValue;
                c.CollideBlocks = blocks.CurrentValue;
                c.CollideLocked = locked.CurrentValue;
                c.ShowStats = stats.CurrentValue;

                if (res == GetResult.Option) continue;
                return; // Enter or Esc: back to the main page
            }
        }
    }
}
