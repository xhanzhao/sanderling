# Sanderling — Real-Time Viewport Navigation for Rhino

Sanderling lets you move through the Rhino viewport in real time with WASD + mouse, similar to Enscape or a game.

- **Fly mode** — free 6-DOF navigation anywhere in the model.
- **Walk mode** — architectural walkthroughs at eye height, following floors, ramps and stairs.
- **Collision** — in Walk mode you are stopped by walls, furniture and low ceilings and slide along them naturally.
- **Layer and object filtering** — choose which geometry you can stand on and bump into.
- **Save views** — capture the current camera as a Rhino named view without leaving navigation.

## Requirements

- Windows 10 / 11 (macOS is not supported yet)
- Rhino 8

## Installation

1. In Rhino 8, run `PackageManager`.
2. Search for `sanderling` and click **Install**, then restart Rhino.
3. Open a model, activate a perspective viewport and click a button on the **Sanderling** toolbar, or run `Sanderling`.

Updates are installed the same way: `PackageManager` shows when a newer version is available.

## Toolbar

The Sanderling toolbar is installed with the package. Each button runs one command:

| Button | Command |
|---|---|
| Sanderling (bird) | `Sanderling` |
| Walk (footprints) | `SanderlingWalk` |
| Fly (bird in flight) | `SanderlingFly` |
| Settings (gear) | `SanderlingSettings` |
| Layers | `SanderlingLayers` |
| Refresh | `SanderlingRefresh` |

If the toolbar is not visible, open it from Rhino's list of toolbars (it is called Sanderling).
Clicking a toolbar button while navigating ends navigation first, then runs the button's command.

## Commands

| Command | Description |
|---|---|
| `Sanderling` | Starts navigation in the default mode (Fly unless changed in settings) |
| `SanderlingFly` | Starts navigation in Fly mode |
| `SanderlingWalk` | Starts navigation in Walk mode |
| `SanderlingSettings` | Adjusts navigation parameters; the `Collision` option opens the collision page |
| `SanderlingLayers` | Chooses which layers take part in Walk mode ground and collision (saved with the .3dm) |
| `SanderlingRefresh` | Rebuilds the cached navigation geometry of the active document |

## Controls

| Input | Action |
|---|---|
| W / A / S / D | Move forward / left / backward / right |
| Q / E | Fly mode: move up / down |
| Shift | Boost (×4 by default) |
| Ctrl | Precision (×0.25 by default) |
| Mouse | Look around (in Drag mode, hold the left or right mouse button) |
| Mouse wheel | Change movement speed (0.1 to 200 m/s) |
| Alt + mouse wheel | Change lens length (14 to 135 mm) |
| F | Switch between Fly and Walk |
| C | Walk mode: toggle collision (e.g. to walk out of a wall) |
| Tab | Toggle look mode: Drag (Enscape style) / Free (cursor locked, game style) |
| V | Save the current view as a named view (`Sanderling View <date time>`) |
| H | Show / hide the HUD |
| Esc | Exit navigation |

Tips:

- After exiting, the viewport stays where you ended. Run `UndoView` to go back to the view from before navigation.
- Clicking anywhere in Rhino outside the navigation viewport (toolbar, panel, menu, command line) ends navigation, and the click then works as usual.
- Alt+Tab pauses navigation and releases the cursor; it resumes when you return to Rhino.

## Walk Mode

- WASD moves horizontally; looking up or down never changes your elevation.
- The camera stays at eye height (1.65 m by default) above the ground and follows ramps, terrain and stairs smoothly.
- Steps up to 0.20 m high are climbed automatically. Walking off the edge of the model is blocked.
- You are a capsule 0.25 m in radius and 1.80 m tall: walls, columns, furniture, railings and low ceilings block you, and you slide along them.
- If no ground is found below the camera, the HUD shows `NO GROUND` and you walk at the current height.

**What takes part in ground and collision:** visible meshes, Breps / surfaces, extrusions, SubDs and block instances on visible layers. Object types can be switched off on the `SanderlingSettings` collision page. `SanderlingLayers` filters by layer:

| Mode | Behavior | Typical use |
|---|---|---|
| All (default) | All visible layers take part | Most models |
| Exclude | Selected layers and their sub-layers are ignored | Glass, trees, people, site context |
| Only | Only selected layers and their sub-layers take part | Very large models |

The first Walk session in a model builds a geometry cache; later sessions reuse it and only rebuild changed objects. On large models the build shows its progress in the HUD and the status bar; press Esc to cancel it (navigation then ends, and the objects built so far are kept for next time). Run `SanderlingRefresh` if the cache ever seems out of date.

## Settings

Run `SanderlingSettings`. Lengths are in meters and converted to model units automatically. Settings persist across Rhino restarts.

| Option | Default | Description |
|---|---|---|
| MouseSensitivity | 0.12 | Degrees per pixel |
| InvertY | No | Invert the mouse Y axis |
| LookMode | Drag | Look mode each session starts in |
| LookSmoothing | 30 | Higher is more responsive, 0 = off |
| MoveSmoothing | 10 | Higher is snappier, 0 = off |
| BoostMultiplier | 4 | Shift multiplier |
| PrecisionMultiplier | 0.25 | Ctrl multiplier |
| MaxFps | 144 | Frame rate cap, 0 = unlimited |
| ShowHud | Yes | Show the HUD when a session starts |
| DefaultMode | Fly | Mode used by the `Sanderling` command |
| EyeHeight | 1.65 | Walk: camera height above the ground (m) |
| MaxStepHeight | 0.20 | Walk: largest step climbed automatically (m) |
| GroundSearchDistance | 50 | Walk: how far below to look for ground (m) |
| GroundFollow | 12 | Walk: height smoothing, 0 = snap instantly |

**Collision page**

| Option | Default | Description |
|---|---|---|
| Collision | Yes | Collision on when a Walk session starts |
| BodyRadius | 0.25 | Capsule radius (m) |
| BodyHeight | 1.80 | Capsule height (m) |
| Meshes / Breps / Extrusions / SubD / Blocks | Yes | Object types used for ground and collision |
| LockedObjects | Yes | Whether locked objects take part |
| ShowStats | No | Show physics timing in the HUD |

## Known Limitations

- Windows only.
- Layout views are not supported; use a model view.
- Parallel viewports are switched to a 50 mm perspective when navigation starts (`UndoView` restores them).
- Fly mode has no collision.

## Building from Source

Build `Sanderling.sln` with Visual Studio 2022 and the .NET 8 SDK. The output folders `bin/Release/net7.0-windows` and `bin/Release/net48` each contain `Sanderling.rhp` and `Sanderling.rui`; drag the `.rhp` into Rhino to test it.
The toolbar file is generated from the SVG icons in `toolbar/icons` with `node toolbar/build_rui.js` (needs Playwright).

## License

[MIT](LICENSE.txt)
