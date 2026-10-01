# Sanderling Development Plan

## Overview

**Sanderling** is a Rhino viewport navigation plugin designed to provide real-time, game-like movement similar to Enscape navigation.

The plugin allows users to navigate directly inside the Rhino viewport using keyboard and mouse controls.

Core controls:

- `W / A / S / D` — Move forward, left, backward, right
- `R / E` — Move camera up / down
- `Shift` — Temporary speed boost
- `Ctrl` — Precision / slow movement
- Mouse — Look around
- Mouse Wheel — Adjust navigation speed
- `Esc` — Exit navigation mode

The plugin should be structured as a reusable navigation engine rather than a collection of individual camera commands.

---

# V1 — Core Fly Navigation

## Goal

Build a reliable and smooth real-time viewport navigation system.

The first version should focus entirely on movement quality, responsiveness, and stability.

## Features

### 1. Navigation Mode

Create a dedicated `Sanderling` command that activates a temporary navigation mode in the active Rhino viewport.

While active:

- Rhino viewport receives navigation input.
- Standard Sanderling keyboard controls are enabled.
- Navigation ends when the user presses `Esc`.
- The viewport returns to normal Rhino interaction after exit.

Suggested command:

```text
Sanderling
```

Optional additional command:

```text
SanderlingFly
```

---

### 2. WASD Movement

Implement continuous keyboard movement.

```text
W — Forward
S — Backward
A — Strafe Left
D — Strafe Right
```

Movement should be based on the current camera orientation.

Forward direction:

```text
CameraDirection
```

Horizontal strafe direction:

```text
CameraX
```

Movement vectors should be normalized when multiple keys are pressed simultaneously so that diagonal movement is not faster than straight movement.

Example:

```text
W + D
```

should have the same total speed as:

```text
W
```

---

### 3. Vertical Movement

Use world-space vertical movement.

```text
R — Move Up
E — Move Down
```

Vertical movement should follow:

```text
World Z
```

rather than the camera's local up direction.

This prevents unexpected vertical movement when the camera is tilted.

---

### 4. Mouse Look

Use mouse movement to control camera orientation.

Horizontal mouse movement:

```text
Yaw
```

Rotate around:

```text
World Z
```

Vertical mouse movement:

```text
Pitch
```

Rotate around:

```text
Camera X
```

Pitch should be clamped to approximately:

```text
-89° to +89°
```

to prevent camera flipping.

---

### 5. Frame-Independent Movement

Movement must be based on elapsed time rather than frame count.

Use:

```text
distance = speed × deltaTime
```

This ensures consistent navigation speed at different viewport frame rates.

For example:

```text
30 FPS
60 FPS
144 FPS
```

should all produce the same real-world movement speed.

---

### 6. Speed System

Navigation speed should be based on physical units rather than raw Rhino model units.

Example default speeds:

```text
Precision Speed   0.5 m/s
Normal Speed      2.0 m/s
Boost Speed       8.0 m/s
```

Controls:

```text
Shift — Boost
Ctrl  — Precision
```

Sanderling should automatically convert physical distances according to:

```text
RhinoDoc.ModelUnitSystem
```

so models using millimeters, meters, inches, or feet behave consistently.

---

### 7. Mouse Wheel Speed Adjustment

Allow the user to dynamically adjust navigation speed using the mouse wheel.

Example behavior:

```text
Mouse Wheel Up   → Increase movement speed
Mouse Wheel Down → Decrease movement speed
```

Suggested speed range:

```text
0.1 m/s → 50 m/s
```

Use exponential or stepped speed increments rather than linear increments.

Example:

```text
0.25
0.5
1
2
4
8
16
32
```

---

### 8. Navigation HUD

Display a lightweight viewport overlay while Sanderling is active.

Example:

```text
SANDERLING
FLY MODE

Speed: 2.0 m/s

WASD      Move
E / R     Down / Up
Shift     Boost
Ctrl      Precision
Wheel     Speed
Esc       Exit
```

Recommended implementation:

```text
DisplayConduit
```

The HUD should make it visually obvious that Rhino is currently in Sanderling Navigation Mode.

---

## V1 Architecture

Recommended structure:

```text
Sanderling
│
├── Commands
│   ├── SanderlingCommand.cs
│   └── SanderlingFlyCommand.cs
│
├── Navigation
│   ├── NavigationController.cs
│   ├── CameraController.cs
│   └── NavigationState.cs
│
├── Input
│   ├── InputController.cs
│   ├── KeyboardState.cs
│   └── MouseLook.cs
│
├── Display
│   └── NavigationHUD.cs
│
└── SanderlingPlugIn.cs
```

Primary data flow:

```text
InputController
      ↓
NavigationController
      ↓
CameraController
      ↓
RhinoViewport
```

---

## V1 Completion Criteria

V1 is complete when:

- WASD movement feels continuous and responsive.
- Mouse look feels smooth.
- Shift and Ctrl speed modifiers work.
- Mouse wheel speed adjustment works.
- Movement speed is independent of frame rate.
- Movement behaves consistently across Rhino unit systems.
- Navigation mode can be entered and exited reliably.
- HUD clearly indicates navigation status.
- Existing Rhino viewport behavior resumes correctly after exit.

---

# V2 — Walk Mode and Ground Following

## Goal

Add an architectural walkthrough mode suitable for navigating interiors and buildings at human eye level.

V2 introduces a distinction between:

```text
Fly Mode
```

and:

```text
Walk Mode
```

---

## Features

### 1. Navigation Mode Switching

Support:

```text
FLY
WALK
```

Possible commands:

```text
SanderlingFly
SanderlingWalk
```

Or allow switching while Sanderling is running.

Possible shortcut:

```text
F — Toggle Fly / Walk
```

---

### 2. Horizontal Walking

In Walk Mode, `WASD` movement should stay parallel to the ground plane.

Even when the user looks upward or downward, forward motion should not gain or lose elevation.

Calculate:

```text
Forward =
CameraDirection projected onto World XY
```

Then normalize the resulting vector.

This creates FPS-style walking behavior.

---

### 3. Eye Height

Maintain a configurable camera eye height above the detected ground.

Default:

```text
1.65 m
```

Settings could include:

```text
Eye Height
1.50 m
1.65 m
1.75 m
Custom
```

Eye height must respect Rhino document units.

---

### 4. Ground Detection

Cast a ray downward from the camera or navigation body.

Conceptually:

```text
Camera Position
      ↓
Downward Ray
      ↓
Scene Geometry
      ↓
Ground Point
```

Use the result to determine the desired camera elevation.

Target camera height:

```text
Ground Z + Eye Height
```

---

### 5. Ground Following

Allow the camera to follow sloped or changing terrain.

The system should smoothly adjust elevation instead of instantly snapping.

Possible interpolation:

```text
currentHeight → targetHeight
```

This will make stairs, ramps, landscape, and sloped floors feel more natural.

---

### 6. Step Handling

Introduce a maximum automatic step height.

Example:

```text
Max Step Height = 0.20 m
```

Small height changes can be climbed automatically.

Large vertical differences should block movement until collision support is implemented in V3.

---

### 7. Walk Settings

Add configurable settings:

```text
Walk Speed
Eye Height
Ground Snap Distance
Max Step Height
Ground Follow Strength
```

Possible command:

```text
SanderlingSettings
```

---

## V2 Architecture Additions

```text
Sanderling
│
├── Navigation
│   ├── FlyNavigationController.cs
│   └── WalkNavigationController.cs
│
├── Physics
│   └── GroundDetector.cs
│
└── Settings
    └── NavigationSettings.cs
```

Walk flow:

```text
Keyboard Input
      ↓
Horizontal Movement
      ↓
Ground Detector
      ↓
Target Eye Height
      ↓
Camera Controller
```

---

## V2 Completion Criteria

V2 is complete when:

- Fly and Walk modes can be switched reliably.
- WASD movement remains horizontal in Walk Mode.
- Camera maintains user-defined eye height.
- Floors and terrain can be detected.
- Sloped ground can be followed smoothly.
- Small steps can be traversed.
- Walk settings can be configured.
- Fly Mode behavior from V1 remains unchanged.

---

# V3 — Collision and Navigation Mesh

## Goal

Turn Sanderling Walk Mode into a practical architectural navigation system where users cannot freely pass through walls, floors, or major objects.

The focus of V3 is spatial collision.

---

## Features

### 1. Collision Body

Represent the user as a simplified navigation volume.

Recommended initial shape:

```text
Capsule
```

or:

```text
Cylinder
```

Example dimensions:

```text
Radius      0.25–0.35 m
Eye Height  1.65 m
Body Height 1.75–1.85 m
```

The body dimensions should be configurable.

---

### 2. Wall Collision

Before moving:

```text
Current Position
      ↓
Requested Movement
      ↓
Collision Test
      ↓
Valid Movement
```

If the desired movement intersects geometry, the camera should stop or slide along the obstacle.

Desired behavior:

```text
Move into wall
      ↓
Forward movement blocked
      ↓
Tangential movement preserved
      ↓
Slide along wall
```

This produces much smoother navigation than simply cancelling all movement.

---

### 3. Floor Collision

Prevent the navigation body from falling through floors.

Walk Mode should use both:

```text
Ground Detection
```

and:

```text
Collision Detection
```

to determine legal camera positions.

---

### 4. Ceiling Collision

Prevent vertical movement or walking into geometry above the user.

This becomes important when:

- using stairs,
- entering low ceilings,
- navigating mechanical spaces,
- moving between floors.

---

### 5. Navigation Geometry Cache

Do not run expensive Brep intersections against the entire Rhino model every frame.

Instead create a simplified navigation representation.

Concept:

```text
Rhino Geometry
      ↓
Collect Relevant Objects
      ↓
Convert / Cache Meshes
      ↓
Spatial Acceleration Structure
      ↓
Real-Time Collision Queries
```

Potential source geometry:

```text
Mesh
Brep
Extrusion
SubD
Block Instances
```

Recommended internal collision representation:

```text
Mesh
```

---

### 6. Scene Cache

Build a collision cache when Sanderling starts.

Example:

```text
NavigationScene
│
├── Mesh Collection
├── Bounding Boxes
├── Spatial Index
└── Ground Geometry
```

Rebuild the cache when necessary.

Possible future command:

```text
SanderlingRefresh
```

---

### 7. Layer / Object Filtering

Allow users to define which geometry participates in navigation collision.

Possible settings:

```text
Collision Geometry
[✓] Visible Objects
[✓] Locked Objects
[ ] Hidden Objects
[ ] Curves
[✓] Meshes
[✓] Breps
[✓] Extrusions
[✓] SubD
```

Potential future option:

```text
Use Selected Layers Only
```

This is important for very large architectural models.

---

### 8. Performance Optimization

Collision should target interactive viewport performance.

Avoid:

```text
Full scene Brep intersection every frame
```

Prefer:

```text
Bounding Box Broad Phase
        ↓
Candidate Geometry
        ↓
Mesh Collision Narrow Phase
```

Potential optimization techniques:

- Bounding-box filtering
- RTree spatial indexing
- Cached render/navigation meshes
- Ray tests
- Capsule-vs-triangle tests
- Limited collision radius
- Dirty-cache rebuilding only when geometry changes

---

## V3 Architecture Additions

```text
Sanderling
│
├── Physics
│   ├── CollisionController.cs
│   ├── GroundDetector.cs
│   ├── NavigationBody.cs
│   └── CollisionSolver.cs
│
├── Scene
│   ├── NavigationScene.cs
│   ├── NavigationMeshCache.cs
│   └── SpatialIndex.cs
│
└── Settings
    └── CollisionSettings.cs
```

Collision flow:

```text
Input
   ↓
Desired Movement
   ↓
Navigation Body
   ↓
Broad Phase Collision
   ↓
Narrow Phase Collision
   ↓
Collision Solver
   ↓
Valid Camera Position
```

---

## V3 Completion Criteria

V3 is complete when:

- User cannot walk through walls.
- User remains above floor geometry.
- Basic ceiling collision works.
- Collision is responsive enough for real-time navigation.
- Wall sliding behaves naturally.
- Collision works with common Rhino architectural geometry.
- Large models remain usable through cached navigation meshes.
- Collision geometry can be filtered.
- Scene cache can be refreshed without restarting Rhino.

---

# Development Sequence

Recommended order:

```text
V1
Real-Time Fly Navigation
        ↓
V2
Walk Mode + Ground Following
        ↓
V3
Collision + Navigation Mesh
```

The priority should always be:

```text
Movement Feel
    ↓
Stability
    ↓
Navigation Features
    ↓
Physics Complexity
```

A polished V1 is more valuable than implementing collision before the underlying navigation controls feel smooth.

---

# Longer-Term Opportunities

These are intentionally outside V1–V3 but should be considered when designing the architecture.

Potential future features:

```text
Camera Bookmarks
Camera Keyframe Recording
Gamepad Support
SpaceMouse Support
Navigation Presets
Teleport Navigation
Gravity
Stair Detection
Door Detection
Viewpoint History
Saved Navigation Paths
Camera Path Recording
Integration with Snowyplover
```

A future workflow could become:

```text
Sanderling Navigation
      ↓
Explore Model
      ↓
Save Camera
      ↓
Record Keyframes
      ↓
Snowyplover Animation
```

This would allow Sanderling to become the interactive navigation component of a broader Rhino camera and animation toolkit.
