using System;
using System.Diagnostics;
using System.Threading;
using Sanderling.Display;
using Sanderling.Input;
using Sanderling.Settings;
using Rhino;
using Rhino.Commands;
using Rhino.Display;

namespace Sanderling.Navigation
{
    /// <summary>
    /// Full lifecycle of a navigation session:
    ///   prepare viewport -> install input -> real-time loop -> always restore Rhino state.
    ///
    /// The real-time loop runs inside the command (the command stays active until Esc):
    ///   while (!exit) { RhinoApp.Wait(); read input; update camera; redraw; }
    /// RhinoApp.Wait() pumps Windows messages so the Rhino UI stays responsive.
    /// </summary>
    internal sealed class NavigationSession
    {
        private const double MaxDeltaTime = 0.1; // Compensate at most 0.1 s after a stall to avoid "teleporting"

        private static NavigationSession _current;

        public static bool IsRunning { get { return _current != null; } }

        private readonly RhinoDoc _doc;
        private readonly RhinoView _view;
        private readonly NavigationMode? _requestedMode; // null = use SanderlingSettings DefaultMode

        private FlyNavigationController _fly;
        private WalkNavigationController _walk;

        private bool _exitRequested;
        private bool _viewDestroyed;
        private string _exitReason;

        private NavigationSession(RhinoDoc doc, RhinoView view, NavigationMode? mode)
        {
            _doc = doc;
            _view = view;
            _requestedMode = mode;
        }

        /// <summary>Command entry point.</summary>
        /// <param name="doc">Active document</param>
        /// <param name="mode">Mode to start in, or null to use the DefaultMode from SanderlingSettings</param>
        public static Result Run(RhinoDoc doc, NavigationMode? mode)
        {
            if (_current != null)
            {
                RhinoApp.WriteLine("Sanderling: Navigation is already running.");
                return Result.Nothing;
            }
            if (doc == null) return Result.Failure;

            RhinoView view = doc.Views.ActiveView;
            if (view == null)
            {
                RhinoApp.WriteLine("Sanderling: No active view.");
                return Result.Failure;
            }
            if (view is RhinoPageView)
            {
                RhinoApp.WriteLine("Sanderling: Layout views are not supported yet. Switch to a model view and try again.");
                return Result.Failure;
            }
            NavigationSession session = new NavigationSession(doc, view, mode);
            _current = session;
            try
            {
                return session.Execute();
            }
            finally
            {
                _current = null;
            }
        }

        private Result Execute()
        {
            SanderlingPlugIn plugin = SanderlingPlugIn.Instance;
            PersistentSettings store = plugin != null ? plugin.Settings : null;
            NavigationSettings settings = NavigationSettings.Load(store);
            CollisionSettings collisionSettings = CollisionSettings.Load(store);

            RhinoViewport viewport = _view.ActiveViewport;
            double metersToModel = NavigationController.MetersToModelUnits(_doc);

            CameraController camera = new CameraController(viewport, metersToModel);
            camera.PrepareViewport();

            NavigationState state = new NavigationState();
            state.Mode = _requestedMode.HasValue ? _requestedMode.Value : settings.DefaultMode;
            state.LookMode = settings.LookMode;
            state.HudVisible = settings.ShowHud;
            state.CollisionEnabled = collisionSettings.CollisionEnabled;
            state.ShowStats = collisionSettings.ShowStats;
            state.FlySpeedIndex = SpeedLadder.Clamp(settings.SpeedIndex);
            state.WalkSpeedIndex = SpeedLadder.Clamp(settings.WalkSpeedIndex);
            state.UnitSystemName = _doc.ModelUnitSystem.ToString();
            camera.ReadCamera(state);
            state.LensLength = camera.LensLength;
            camera.Apply(state.Position, state.Yaw, state.Pitch); // Apply corrections such as the pitch clamp

            _fly = new FlyNavigationController(camera, state, settings, metersToModel);
            _walk = new WalkNavigationController(camera, state, settings, collisionSettings, metersToModel, _doc);
            NavigationHUD hud = new NavigationHUD(state, settings, viewport.Id);
            InputController input = new InputController(_view.Handle);

            Result result = Result.Success;
            SubscribeEvents();
            try
            {
                if (!input.Start())
                {
                    RhinoApp.WriteLine("Sanderling: Failed to install input hooks. Navigation cancelled.");
                    return Result.Failure;
                }

                hud.Enabled = true;
                state.HudDirty = true;
                PrintHelp(settings, state.Mode);

                NavigationController controller = ControllerFor(state.Mode);
                controller.OnActivated();

                RunLoop(input, controller, state, settings);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("Sanderling: An error occurred and navigation was stopped. {0}", ex.Message);
                result = Result.Failure;
            }
            finally
            {
                // ---- Cleanup that must always run ----
                input.Dispose();          // Remove hooks, release and show the cursor
                hud.Enabled = false;
                UnsubscribeEvents();

                // Only the speed levels (one per mode) are remembered between sessions.
                // Mode (F), look mode (Tab), HUD visibility (H) and collision (C) are session-only: every session starts
                // with the values configured in SanderlingSettings.
                settings.SpeedIndex = state.FlySpeedIndex;
                settings.WalkSpeedIndex = state.WalkSpeedIndex;
                settings.Save(store);

                if (!_viewDestroyed)
                {
                    try { _view.Redraw(); } catch { }
                }

                if (!string.IsNullOrEmpty(_exitReason))
                {
                    RhinoApp.WriteLine("Sanderling: {0}", _exitReason);
                }
                RhinoApp.WriteLine("Sanderling: Navigation ended. Run UndoView to return to the view before navigation.");
            }
            return result;
        }

        private void RunLoop(InputController input, NavigationController controller,
                             NavigationState state, NavigationSettings settings)
        {
            Stopwatch clock = Stopwatch.StartNew();
            double lastTime = clock.Elapsed.TotalSeconds;
            double fpsWindow = 0.0;
            int fpsFrames = 0;

            while (!_exitRequested)
            {
                // Pump Windows messages (hook callbacks, window repaints and Rhino events all happen here)
                RhinoApp.Wait();
                if (_exitRequested || _viewDestroyed) break;

                double frameStart = clock.Elapsed.TotalSeconds;
                double dt = frameStart - lastTime;
                lastTime = frameStart;
                if (dt < 0.0) dt = 0.0;
                if (dt > MaxDeltaTime) dt = MaxDeltaTime;

                InputFrame frame = input.Update(state.LookMode);
                if (frame.ExitRequested) break;

                if (frame.ToggleHud)
                {
                    state.HudVisible = !state.HudVisible;
                    state.HudDirty = true;
                    RhinoApp.WriteLine(state.HudVisible ? "Sanderling: HUD shown." : "Sanderling: HUD hidden - press H to show it again.");
                }

                if (frame.ToggleMode)
                {
                    state.Mode = state.Mode == NavigationMode.Fly ? NavigationMode.Walk : NavigationMode.Fly;
                    controller = ControllerFor(state.Mode);
                    controller.OnActivated();
                    state.HudDirty = true;
                    RhinoApp.WriteLine(state.Mode == NavigationMode.Walk
                        ? "Sanderling: Walk mode - WASD walks on the ground, Q/E disabled. Press F for Fly mode."
                        : "Sanderling: Fly mode. Press F for Walk mode.");

                    // Building the ground cache may take a while; do not count that time as movement
                    lastTime = clock.Elapsed.TotalSeconds;
                }

                if (frame.ToggleCollision)
                {
                    state.CollisionEnabled = !state.CollisionEnabled;
                    state.HudDirty = true;
                    string suffix = state.Mode == NavigationMode.Walk ? "." : " (applies in Walk mode).";
                    RhinoApp.WriteLine((state.CollisionEnabled ? "Sanderling: Collision on" : "Sanderling: Collision off") + suffix);
                }

                if (frame.ToggleLookMode)
                {
                    state.LookMode = state.LookMode == LookMode.Drag ? LookMode.Free : LookMode.Drag;
                    state.HudDirty = true;
                }

                bool cameraChanged = controller.Tick(frame, dt);
                bool redraw = cameraChanged || state.HudDirty;

                if (redraw)
                {
                    state.HudDirty = false;
                    _view.Redraw();
                    fpsFrames++;
                }

                fpsWindow += dt;
                if (fpsWindow >= 0.5)
                {
                    if (fpsFrames > 0) state.Fps = fpsFrames / fpsWindow;
                    fpsWindow = 0.0;
                    fpsFrames = 0;
                }

                if (redraw)
                {
                    Throttle(clock, frameStart, settings.MaxFps);
                }
                else
                {
                    Thread.Sleep(2); // Reduce CPU usage while idle
                }
            }
        }

        /// <summary>Frame rate cap, avoids spinning the GPU/CPU on light models.</summary>
        private static void Throttle(Stopwatch clock, double frameStart, int maxFps)
        {
            if (maxFps <= 0) return;
            double minFrame = 1.0 / maxFps;
            double spent = clock.Elapsed.TotalSeconds - frameStart;
            int sleepMs = (int)((minFrame - spent) * 1000.0);
            if (sleepMs > 0) Thread.Sleep(sleepMs);
        }

        private NavigationController ControllerFor(NavigationMode mode)
        {
            return mode == NavigationMode.Walk ? (NavigationController)_walk : _fly;
        }

        private void PrintHelp(NavigationSettings settings, NavigationMode mode)
        {
            RhinoApp.WriteLine("Sanderling navigation started ({0} mode) - WASD Move | Q/E Up/Down (Fly) | Shift Boost | Ctrl Precision | Wheel Speed | Alt+Wheel Lens | F Fly/Walk | C Collision (Walk) | Tab Look mode | H HUD | Esc Exit",
                               mode == NavigationMode.Walk ? "Walk" : "Fly");
            if (settings.LookMode == LookMode.Drag)
            {
                RhinoApp.WriteLine("Sanderling: Drag look mode - hold the left or right mouse button and drag to look around. Press Tab for Free look.");
            }
        }

        // ------------------------------------------------------------------
        // Rhino events: ensure a safe exit in every abnormal situation
        // ------------------------------------------------------------------

        private void SubscribeEvents()
        {
            RhinoApp.EscapeKeyPressed += OnEscapeKeyPressed;
            RhinoView.Destroy += OnViewDestroy;
            RhinoDoc.CloseDocument += OnCloseDocument;
            Command.BeginCommand += OnBeginCommand;
        }

        private void UnsubscribeEvents()
        {
            RhinoApp.EscapeKeyPressed -= OnEscapeKeyPressed;
            RhinoView.Destroy -= OnViewDestroy;
            RhinoDoc.CloseDocument -= OnCloseDocument;
            Command.BeginCommand -= OnBeginCommand;
        }

        private void RequestExit(string reason)
        {
            if (!_exitRequested && !string.IsNullOrEmpty(reason)) _exitReason = reason;
            _exitRequested = true;
        }

        private void OnEscapeKeyPressed(object sender, EventArgs e)
        {
            RequestExit(null);
        }

        private void OnViewDestroy(object sender, ViewEventArgs e)
        {
            if (e.View != null && e.View.RuntimeSerialNumber == _view.RuntimeSerialNumber)
            {
                _viewDestroyed = true;
                RequestExit("The navigation view was closed.");
            }
        }

        private void OnCloseDocument(object sender, DocumentEventArgs e)
        {
            if (e.DocumentSerialNumber == _doc.RuntimeSerialNumber)
            {
                _viewDestroyed = true;
                RequestExit("The document was closed.");
            }
        }

        private void OnBeginCommand(object sender, CommandEventArgs e)
        {
            string name = e.CommandEnglishName ?? string.Empty;
            if (!name.StartsWith("Sanderling", StringComparison.OrdinalIgnoreCase))
            {
                RequestExit("Another command was started (" + name + ").");
            }
        }
    }
}
