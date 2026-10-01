# Sanderling — Real-Time Viewport Navigation for Rhino (V3: Fly + Walk + Collision)

Sanderling lets you move through the Rhino viewport in real time with WASD + mouse, similar to Enscape or a game.

- **V1** — free Fly navigation.
- **V2** — Walk mode for architectural walkthroughs at eye height with ground following.
- **V3** — collision in Walk mode (walls, furniture, low ceilings), a persistent navigation geometry cache and layer / object filtering.

The project structure follows McNeel's official RhinoCommon plug-in template for Rhino 8.

## 1. Requirements

| Item | Version |
|---|---|
| Operating system | Windows 10 / 11 (Sanderling uses Win32 input APIs; macOS is not supported yet) |
| Rhino | Rhino 8 (default location `C:\Program Files\Rhino 8\`) |
| Visual Studio | Visual Studio 2022 (17.8 or later) with the **".NET desktop development"** workload |
| .NET SDK | .NET 8 SDK (installed with Visual Studio; can build the net7.0 target) + .NET Framework 4.8 developer pack |
| Optional | McNeel's *RhinoCommon and Grasshopper templates* Visual Studio extension (useful for new projects; not required to open this one) |

The first build requires an internet connection so NuGet can download the `RhinoCommon` package.

## 2. Folder Structure

```
Sanderling/
├── Sanderling.sln                          ← Open this in Visual Studio
├── README.md
├── docs/Sanderling_Development_Plan.md     ← Development plan
└── Sanderling/
    ├── Sanderling.csproj                   ← SDK-style project, outputs Sanderling.rhp
    ├── SanderlingPlugIn.cs                 ← Plug-in entry point
    ├── Properties/
    │   ├── AssemblyInfo.cs              ← Plug-in GUID and description
    │   └── launchSettings.json          ← F5 starts Rhino 8 and loads the plug-in automatically
    ├── Commands/
    │   ├── SanderlingCommand.cs            ← Sanderling (starts in DefaultMode)
    │   ├── SanderlingFlyCommand.cs         ← SanderlingFly
    │   ├── SanderlingWalkCommand.cs        ← SanderlingWalk
    │   ├── SanderlingSettingsCommand.cs    ← SanderlingSettings (incl. collision page)
    │   ├── SanderlingLayersCommand.cs      ← SanderlingLayers (per-document layer filter)
    │   └── SanderlingRefreshCommand.cs     ← SanderlingRefresh (rebuild navigation geometry)
    ├── Navigation/
    │   ├── NavigationSession.cs         ← Session lifecycle, real-time loop, mode switching, safe exit
    │   ├── NavigationController.cs      ← Base engine: look, speed, lens, modifiers, smoothing
    │   ├── FlyNavigationController.cs   ← Fly movement
    │   ├── WalkNavigationController.cs  ← Walk movement, eye height, collision pipeline hookup
    │   ├── CameraController.cs          ← Yaw/Pitch <-> Rhino camera, projection, lens
    │   └── NavigationState.cs           ← Runtime state, NavigationMode, LookMode
    ├── Physics/
    │   ├── NavigationBody.cs            ← Capsule dimensions
    │   ├── CollisionController.cs       ← Per-move pipeline: substeps, ground rules, collision, penetration check
    │   ├── CollisionSolver.cs           ← Capsule-vs-triangle narrow phase + closest-point math
    │   └── GroundDetector.cs            ← Five-ray footprint ground queries
    ├── Scene/
    │   ├── NavigationScene.cs           ← Persistent per-document cache, dirty tracking, filtering
    │   ├── NavigationMeshCache.cs       ← Rhino objects -> world-space triangle meshes
    │   └── SpatialIndex.cs              ← Two-level RTree (meshes -> triangles)
    ├── Input/
    │   ├── InputController.cs           ← Thread hooks + per-frame InputFrame
    │   ├── KeyboardState.cs             ← Keyboard polling
    │   ├── MouseLook.cs                 ← Cursor capture / relative motion
    │   └── Native/Win32.cs              ← P/Invoke declarations
    ├── Display/
    │   └── NavigationHUD.cs             ← DisplayConduit HUD
    └── Settings/
        ├── NavigationSettings.cs        ← Navigation / Walk settings, speed and lens levels
        └── CollisionSettings.cs         ← Collision body, object type filters, layer filter
```

Data flow: `InputController → NavigationController (Fly / Walk) → CameraController → RhinoViewport`.
Walk mode moves through `CollisionController → GroundDetector / CollisionSolver → SpatialIndex → NavigationScene`.
`NavigationSession` guarantees that hooks are removed, the cursor is restored and the HUD is disabled in every situation (Esc, exceptions, closing the view, closing the document, starting another command).

## 3. Building and Debugging

1. Double-click `Sanderling.sln` to open it in Visual Studio 2022 and wait for the NuGet restore to finish.
2. In the toolbar, select the **`Rhino 8 - netcore`** launch profile (target framework `net7.0-windows`).
   If your Rhino runs in .NET Framework mode, select `Rhino 8 - netfx` with `net48`.
3. Press **F5**. Visual Studio builds the project and starts Rhino 8. Rhino loads `Sanderling.rhp` automatically from `bin\Debug\net7.0-windows\` through the `RHINO_PACKAGE_DIRS` environment variable, so breakpoints work immediately.
4. In Rhino, open a model, activate a perspective viewport and run the `Sanderling` command.

**If Rhino does not load the plug-in automatically:** run `PlugInManager` → `Install...` and select
`Sanderling\bin\Debug\net7.0-windows\Sanderling.rhp`, or drag the `.rhp` file into the Rhino window.

**If Rhino is installed elsewhere:** update `executablePath` in `Properties/launchSettings.json`.


## 4. Commands

| Command | Description |
|---|---|
| `Sanderling` | Starts navigation in the DefaultMode set in SanderlingSettings (Fly unless changed) |
| `SanderlingFly` | Starts navigation in Fly mode |
| `SanderlingWalk` | Starts navigation in Walk mode |
| `SanderlingSettings` | Adjusts navigation parameters; the `Collision` option opens the collision page |
| `SanderlingLayers` | Chooses which layers take part in Walk mode ground and collision (saved with the .3dm) |
| `SanderlingRefresh` | Discards and rebuilds the cached navigation geometry of the active document |

The settings commands are not available while navigating.

## 5. Controls

| Input | Action |
|---|---|
| W / A / S / D | Forward / left / backward / right (diagonal movement is normalized) |
| Q / E | Fly mode: move up / down along World Z. Disabled in Walk mode |
| Shift | Boost (default ×4) |
| Ctrl | Precision (default ×0.25); Ctrl takes precedence when both are held |
| Mouse | Look around (pitch clamped to ±89°); in Drag mode, hold the left or right mouse button |
| Mouse wheel | Adjust base speed in levels from 0.1 to 200 m/s (exponential); works anywhere in the Rhino window while navigating |
| Alt + mouse wheel | Adjust the viewport lens length in steps: 14 / 18 / 20 / 24 / 28 / 35 / 50 / 70 / 85 / 105 / 135 mm |
| F | Switch between Fly and Walk mode (current session only) |
| C | Walk mode: switch collision on / off (current session only), e.g. to get out after starting inside a wall |
| Tab | Toggle look mode: Drag (default; hold left or right mouse button, Enscape style) / Free (cursor hidden and locked, game style) |
| H | Show / hide the HUD (current session only) |
| Esc | Exit navigation |

Fly and Walk each remember their own mouse wheel speed level (defaults: Fly 2.0 m/s, Walk 1.5 m/s).

Lens length comes from the Rhino viewport itself: Sanderling uses the viewport's current lens and never stores its own. Changes made with Alt + mouse wheel stay on the viewport after exit.

After exiting, the viewport stays where navigation ended. Run `UndoView` to return to the view from before navigation.
While navigating, all key presses are swallowed (nothing reaches the command line), and mouse clicks and wheel input inside the viewport do not trigger Rhino's normal behavior.
Switching to another application (Alt+Tab) pauses navigation and releases the cursor; navigation resumes automatically when you return to Rhino.

## 6. Walk Mode Rules

**Movement.** WASD moves horizontally only: forward is the camera direction projected onto World XY, so looking up or down never changes elevation. Height is controlled by the ground, not by Q / E. Fly mode has no collision.

**Eye height.** Camera height = ground Z + EyeHeight (default 1.65 m, converted to model units). Height changes on ramps, terrain and stairs are smoothed (GroundFollow).

**Collision body.** The user is a vertical capsule (BodyRadius 0.25 m, BodyHeight 1.80 m). The capsule starts at the step height above the feet:

- Below the step height, the ground rules apply (stairs, kerbs, ramps).
- Above the step height, collision applies (walls, columns, furniture, railings, low ceilings).

**Ground detection.** Five vertical rays (center plus four at half the body radius) search downwards from foot + MaxStepHeight. The ground is the highest surface any of them hits, so small gaps between meshes and standing on step or slab edges are handled reliably.

**Rules for every move.** Movement is split into substeps no longer than half the body radius, so fast movement cannot pass through thin walls or skip two stair risers at once. Each substep:

| Situation | Result |
|---|---|
| Ground rises by at most MaxStepHeight (default 0.20 m) | Step up automatically |
| Ground drops | Allowed; the camera descends smoothly |
| No ground at the target (edge of the model) | Blocked |
| Wall, column, furniture, railing (contact above the step height) | Pushed out; movement into the surface is removed, so you slide along it at any angle |
| Low ceiling or overhang inside the body height | Blocked |
| Corner or narrow gap that would leave the body deeper inside geometry | Blocked |

Moves that reduce penetration are always allowed, so starting Walk mode inside an object never traps you; press C to switch collision off if you need to walk out through geometry.

**Entering Walk mode.** Sanderling looks for ground straight below the camera within GroundSearchDistance. If found, the camera descends smoothly to eye height above it. If not, you walk at the current height and the HUD shows `NO GROUND` until ground is found.

## 7. Navigation Geometry

**What takes part.** Visible meshes, Breps / surfaces, extrusions, SubDs and block instances on visible layers, including locked objects. Hidden objects, objects on hidden layers, curves and points never take part. Object types can be switched off on the SanderlingSettings collision page; layers are filtered with SanderlingLayers. The same filter is used for ground and collision: an excluded layer can neither block you nor be stood on.

**Layer filter (SanderlingLayers).**

| Mode | Behavior | Typical use |
|---|---|---|
| All (default) | All visible layers take part | Most models |
| Exclude | Selected layers and their sub-layers are ignored | Glass, trees, people, site context |
| Only | Only selected layers and their sub-layers take part | Very large models: keep walls, slabs, stairs |

**Cache.** The first time Walk mode is used, Sanderling converts the geometry to triangle meshes (reusing Rhino's render meshes when available) and builds a two-level spatial index. The cache is kept after navigation ends; document changes (added, changed, undeleted objects, block definition edits) mark only the affected objects for rebuilding, so later sessions start quickly. The command line reports how many objects were reused and rebuilt. Run `SanderlingRefresh` if the cache ever seems out of date.

## 8. Settings (SanderlingSettings Command)

Lengths are in meters and are converted to model units automatically.

**Main page**

| Option | Default | Description |
|---|---|---|
| MouseSensitivity | 0.12 | Degrees per pixel |
| InvertY | No | Invert the mouse Y axis |
| LookMode | Drag | Look mode every session starts in: Drag / Free (Tab switches it only for the current session) |
| LookSmoothing | 30 | Look smoothing; higher is more responsive, 0 = off |
| MoveSmoothing | 10 | Acceleration smoothing; higher is snappier, 0 = off |
| BoostMultiplier | 4 | Shift multiplier |
| PrecisionMultiplier | 0.25 | Ctrl multiplier |
| MaxFps | 144 | Redraw frame rate cap, 0 = unlimited |
| ShowHud | Yes | Whether the HUD is shown when a session starts (H toggles it only for the current session) |
| DefaultMode | Fly | Mode used by the `Sanderling` command (F switches it only for the current session) |
| EyeHeight | 1.65 | Walk: camera height above the ground (m) |
| MaxStepHeight | 0.20 | Walk: largest height difference climbed automatically (m) |
| GroundSearchDistance | 50 | Walk: how far below to look for ground (m) |
| GroundFollow | 12 | Walk: height smoothing; higher stays tighter to the ground, 0 = snap instantly |
| Collision | — | Opens the collision page |
| ResetDefaults | — | Restore all defaults (both pages) |

**Collision page** (Enter or Esc returns to the main page; changes are saved with the main page)

| Option | Default | Description |
|---|---|---|
| Collision | Yes | Whether collision is on when a Walk session starts (C toggles it only for the current session) |
| BodyRadius | 0.25 | Capsule radius (m) |
| BodyHeight | 1.80 | Capsule height (m); always kept at least 5 cm above EyeHeight |
| Meshes / Breps / Extrusions / SubD / Blocks | Yes | Object types used for ground and collision |
| LockedObjects | Yes | Whether locked objects (or objects on locked layers) take part |
| ShowStats | No | Show `Physics: x ms  Tris: n` in the HUD while walking |

These parameters and the speed levels persist across Rhino restarts. Mode (F), look mode (Tab), HUD visibility (H) and collision (C) changed during navigation are not remembered.

## 9. Acceptance Test Checklist

### V1 — Fly

- [ ] **Continuous movement:** holding W moves forward smoothly; releasing it decelerates briefly and stops. W+D moves at the same speed as W alone.
- [ ] **Mouse look:** rotation is smooth without jitter; looking fully up or down never flips the camera.
- [ ] **Shift / Ctrl:** the HUD shows BOOST / PRECISION and the effective speed.
- [ ] **Mouse wheel speed / Alt + wheel lens:** the HUD values change by level; the view no longer zooms.
- [ ] **Frame-rate independence:** at 2 m/s, moving forward for 5 seconds covers about 10 m in both light and heavy models.
- [ ] **Unit consistency:** the same speed level produces the same real-world speed in mm, m, inch and foot models.
- [ ] **Enter / exit:** after repeatedly entering and exiting, Alt+Tab, closing the view and closing the document, Rhino always returns to normal.
- [ ] **Restore:** after exiting, Rhino's view rotation, selection and command input work normally; `UndoView` returns to the pre-navigation view.

### V2 — Walk

- [ ] **Mode switching:** F switches between Fly and Walk reliably; `SanderlingWalk` starts directly in Walk.
- [ ] **Horizontal walking:** looking up or down while holding W does not change elevation.
- [ ] **Eye height:** on a flat floor the camera stays at EyeHeight above it.
- [ ] **Slopes and stairs:** ramps and terrain are followed smoothly; stairs with risers ≤ 0.20 m can be climbed and descended, also with Shift held and at low frame rates.
- [ ] **Edges:** walking off the edge of a site model is blocked; walking off a balcony onto lower ground descends smoothly.
- [ ] **Fly unchanged:** Fly mode behaves as in V1.

### V3 — Collision

- [ ] **Walls:** walking straight into a wall stops; walking diagonally into it slides along it, also for walls that are not aligned with X / Y.
- [ ] **Stair sides:** walking up stairs next to a stringer or railing slides along it instead of getting stuck.
- [ ] **Furniture:** a 0.75 m table blocks movement; you cannot walk under it.
- [ ] **Low ceilings:** a beam or soffit lower than BodyHeight blocks movement; spaces higher than BodyHeight can be entered.
- [ ] **Doors and corridors:** a 0.9 m door opening can be passed with the default radius; a gap narrower than the body cannot.
- [ ] **Thin walls at speed:** holding Shift at a high speed level does not pass through thin walls.
- [ ] **Stuck recovery:** starting Walk inside a wall lets you walk out; C switches collision off and on.
- [ ] **Filters:** excluding a layer with SanderlingLayers removes it from collision and ground in the next session; type toggles on the collision page work.
- [ ] **Cache:** the second Walk session in the same model reports objects as reused; after moving one object, only that object is rebuilt; SanderlingRefresh rebuilds everything.
- [ ] **Performance:** with ShowStats on, physics time stays well below the frame time in your largest model.

## 10. Known Limitations

- Windows only (the input layer uses Win32 thread hooks; a macOS implementation can be added behind `InputController` later).
- Layout views are not supported; use Sanderling in a model view.
- When the model unit system is custom or none, speeds and lengths are treated as meters.
- Parallel projection viewports are switched to a 50mm perspective when navigation starts; two-point perspective viewports keep their lens length. `UndoView` restores the original projection.
- Fly mode has no collision.
- Block instances are cached per instance (geometry is flattened), so models with very many instances of large blocks use more memory.
- Very large single meshes (e.g. scanned terrain) build their triangle index the first time you walk near them, which can cause a short pause once.
- At extreme speeds the number of substeps per frame is capped (32), so very thin walls may occasionally be passed.
- Objects whose render meshes change without the object itself changing (e.g. new mesh settings) keep their old cached mesh until SanderlingRefresh.

## 11. Future Opportunities

- Optional collision in Fly mode.
- Shared block definition meshes for lower memory use.
- Camera bookmarks, keyframe recording, gamepad / SpaceMouse support (`InputFrame` is hardware-independent), Snowyplover integration.
