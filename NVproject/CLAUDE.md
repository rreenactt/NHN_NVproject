# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Unity **6000.3.20f1** (Unity 6.3), **URP 17.3**, **new Input System** (`activeInputHandler: 1` — legacy `Input.GetAxis`/`Input.GetKey` will throw). A single-scene 3D first-person shooter prototype. Main scene: `Assets/Scenes/SampleScene.unity`.

The player is a **Minecraft-style figure built from white cubes in code, animated entirely procedurally**. There is no character model, no skinned mesh, no Animator, no AnimationClip and no blend tree anywhere in the project — all of that was removed. `Assets/Shady_3d` and `Assets/Animations` are leftover assets that nothing references; the humanoid scripts that drove them (`AimPitch`, `RootMotionRelay`, `ViewmodelRig`, `FirstPersonSetup`) are deleted. **Do not reintroduce Mecanim to solve an animation problem here** — pose the joints in code instead.

## Working in this project (no CLI build)

There is no build script or test suite. All work happens through the **Unity Editor via MCP** (`mcp__unity-mcp__*`, Unity's official `com.unity.ai.assistant` package).

- **Read the `unity-mcp-ops` skill before any Unity MCP task** — the `fps-*` skills say what to build, `unity-mcp-ops` says how to drive the server.
- Keep `com.unity.ai.assistant` pinned at **2.6.0-pre.1**. 2.7.x requires a paid Unity AI seat and every MCP call fails with "Connection revoked" / "Capacity Limit". Enable via Project Settings ▸ AI ▸ Assistant MCP Extensions ▸ "Enable MCP Tools".
- Editing any `Assets/**/*.cs` triggers a domain reload that briefly drops the bridge ("Unity not detected") — retry the next call. **Always re-enter play mode after a script edit before trusting runtime reads**; a stale play session silently runs the old code. A domain reload does *not* reliably exit play mode, so guard any edit-mode-only command with `if (Application.isPlaying) return;` — writes made in play mode are silently discarded.
- **`Undo.AddComponent` / `Undo.RegisterCreatedObjectUndo` inside a command get rolled back if that command later errors**, even after an `ExecuteMenuItem` has already succeeded — you get a half-applied scene (objects deleted, components missing) with nothing in the console. Use plain `AddComponent` from commands and keep `Undo.*` for genuine editor menu items.
- Newly added components serialize the field defaults **as of that moment**. Changing a default in the `.cs` afterwards does not update the scene — set both, or the scene silently keeps the old value.
- `result.Log` only substitutes `{0}`-style object references; format specifiers like `{0:F3}` are printed literally. Pre-format with `ToString("F3")` and concatenate.
- `Mesh` collides with a namespace inside the generated command wrapper — write `UnityEngine.Mesh`.
- **Never rewrite this file with PowerShell `Get-Content`/`Set-Content`** — the round trip mangles every em-dash and `▸` into `??`. Use the Edit/Write tools.
- In `Unity_RunCommand`: `AssetDatabase.DeleteAsset` fails ("User interactions are not supported for MCP tool calls") and can abort mid-command — delete the asset + its `.meta` from the filesystem, then `AssetDatabase.Refresh()`. Write to fresh asset paths rather than overwriting.
- `System.Reflection` inside `Unity_RunCommand` fails with a bare `UNEXPECTED_ERROR: Object reference not set…` (even just `GetField`). Add a public API to the script instead — `FirstPersonController.SetPitch` exists partly for this. Injected input (`InputSystem.QueueDeltaStateEvent`) also does not reach the player loop from a command, and `Time.deltaTime` does not advance within one command, so **per-frame motion cannot be stepped from a command at all**. Verify the *formula* against the live serialized parameters instead, then ask the user to play it.
- `Unity_Camera_Capture` with a specific camera fails under URP; only the no-arg scene-view capture works. **Animation smoothness cannot be self-verified visually** — verify numerically and ask the user for visual judgement. For "where is it on screen" questions, rasterize: project every triangle of the baked/mesh geometry to viewport space onto a coarse occupancy grid and print an ASCII map. Bounding boxes are useless here — geometry that straddles the near plane produces garbage extents, so skip triangles with any vertex at `z <= nearClipPlane`.
- Editor entry point: menu **Tools ▸ Block Player ▸ Build Block Player** (`Assets/Editor/BlockPlayerSetup.cs`) strips whatever character is under `Player` and rewires the block player from scratch.

## Architecture

Runtime scripts live in `Assets/Scripts/`. The scene holds almost nothing — `BlockRig` builds every block in `Awake`, so the body exists only at runtime and there is no prefab to keep in sync:

```
Player          CharacterController + FirstPersonController + BlockRig
                + BlockCharacterAnimator + ProceduralReload + WeaponController + WeaponSwitcher
 └─ FP Camera   local (0, 1.62, 0); nearClip 0.02; cullingMask excludes PlayerBody
     └─ Viewmodel Arms   ← built at runtime, framing = BlockRig.viewmodelOffset
Backrooms       BackroomsMap — the whole level, built at runtime from a seed
Mirror / Mirror Frame        repositioned into the spawn room by BackroomsMap
Global Volume, Directional Light (disabled)

built at runtime under Player:
 Hips ─ Torso ─┬─ Neck ─ Head        Arm R ─ Hand R ─ Pistol ─ Muzzle
               ├─ Arm R / Arm L      Leg R / Leg L (children of Hips)

built at runtime under Backrooms:
 Floor, Ceiling (one slab each) ─ Walls/* (merged runs) ─ Ceiling Lights/*
```

**Proportions are Minecraft's, on a 16-per-block pixel grid** (`head 8³`, `torso 8×12×4`, `limbs 4×12×4`; legs 12 + torso 12 + head 8 = 32 px). At `totalHeight` 1.8 m the eyes land at 1.62 m, which is where the camera already sat. Every limb's pivot is at its **joint**, with the cube offset half its length below it — get that wrong and limbs orbit their own centre, which is the usual reason a blocky walk looks broken.

**Movement is controller-driven again, and the animation follows it.** `FirstPersonController` moves the `CharacterController` directly and publishes `PlanarSpeed`, `MoveInput`, `VerticalVelocity`, `IsGrounded`. `PlanarSpeed` is **measured from actual displacement**, not from input, so walking into a wall stops the legs instead of leaving them running on the spot.

**The one load-bearing equation.** A rotating leg of length `L` swinging by `A` radians at rate `w` moves its foot through mid-stance at `L*A*w`. Setting that equal to real speed `v` gives

```
A = v / (L * w)
```

so the stride is *derived from measured speed* and the planted foot tracks the ground. Cadence is chosen first (a natural 0.95–1.55 strides/sec), the swing angle follows, and if that angle would exceed `maxLegSwing` (52°) the **cadence rises instead of the stride widening**. Verified: planted-foot speed matches body speed to 0.000000 m/s at 1–7 m/s. This is what replaced baked root motion — do not "fix" foot sliding by reintroducing clips.

**Everything else in `BlockCharacterAnimator` is the naturalness budget**, composed fresh into each joint's `localRotation` every `LateUpdate`:
- **Swing axis is derived from the move direction** (`Cross(up, localMoveDir)`), so strafing abducts the legs sideways and walking backwards reverses the cycle — no extra states, no separate clips.
- Arms counter-swing and **lag the legs** by `armPhaseLag` radians; zero lag reads as clockwork.
- Hips drop as the legs split. The true geometric drop is `L*(1-cos A)` ≈ 0.23 m, which pogo-sticks without knees — `bobAmount` applies a quarter of it. The camera takes `cameraBobRatio` of that plus a small lateral figure-of-eight.
- Torso leans into speed, banks into strafes; **the head counter-rotates `headStabilise` of the body's own pitch and roll** so the gaze stays level. Idle breathes, and only shows through when the body is still.
- Jump tucks the legs, falling spreads them, landing squashes with a `Sin(t*PI)` envelope scaled by impact speed.

**`ProceduralReload` no longer touches any transform** — it only produces a 0..1 `Weight`, and the animator blends the arms toward `loweredDirection` by that amount. One place composes the pose, so a reload cannot fight the walk cycle or the aim. It runs in `Update` (the animator is `[DefaultExecutionOrder(100)]`) so the weight is current when read.

**Arms-only first person, unchanged in principle.** Layers `FirstPersonArms` (8) and `PlayerBody` (9) split what you see from what everyone else sees: the FP camera's `cullingMask` excludes `PlayerBody`, so your own body — head included — is invisible to you but present for the mirror and for shadows. The camera sits *inside* the head block, which is fine precisely because of that mask. The viewmodel arms are a second pair of blocks parented to the camera with `shadowCastingMode = Off`.

**Viewmodel framing.** Anatomically the shoulders sit 0.27 m below and behind the lens, so a faithful copy hangs entirely out of frame; `BlockRig.viewmodelOffset` lifts the pair into shot. Currently **(−0.1692, −0.0028, 0.1027)** with `armedDirectionR` (−0.12, −0.155, 1.0) — arm 8.7° below level — putting the pistol at viewport ≈(0.55, 0.10) and the hand at (0.56, 0.075), covering ~12% of the screen. `armedDirectionL` must point **hard inward** (0.80, −0.170, 1.0): the left shoulder is a third of a metre off-centre, and anything gentler strands that arm against the left edge of the screen (it measured x=0.073 before that fix, 0.423 after).

**The two framing knobs are not independent.** `viewmodelOffset` slides the arms; `armedDirection*.y` changes their *angle*, and raising the angle also pushes the hand **further from the lens**, which shrinks the arms as well as lifting them. Going from 16.6° to 8.7° below level halved screen coverage (26% → 12%) on its own. Always re-measure with the rasteriser and re-solve the offset after touching the angle — a 0.045 m offset shift alone moves coverage by ~16 points.

**A held weapon must not inherit the arm's rotation.** Two traps here, both already paid for:
- `AimLimb` points a limb's local −Y along a direction with `FromToRotation`, which pins the length axis and leaves the **roll arbitrary**. That is fine for limbs — every block has a *square* cross-section, so rolling one is invisible — but the pistol is a child of the hand and inherited that arbitrary roll, which left its barrel 74° off, very nearly straight **up**, and every shot leaving the muzzle sideways.
- Pinning the roll with `LookRotation(forward, -direction)` is **worse**: `LookRotation` prioritises its forward argument and orthogonalises the up axis, so the length axis gets thrown away and the arm hangs straight down (the hand measured viewport y = −5.7).
- The fix is neither: `BlockCharacterAnimator.PointBarrelAt` sets the gun's **world** rotation directly, `LookRotation(aimDirection, Vector3.up)`, ignoring the arm. There is also a physical reason it can never inherit the hand — the barrel is perpendicular to a rigid arm block, so an arm reaching forward *forces* the barrel to point up or sideways.

**The shot leaves the muzzle in a straight line.** `WeaponController.UpdateAim` publishes `AimPoint`/`AimDistance` from the crosshair every frame; the animator turns both pistols onto it (clamped to `barrelAlignClamp` 28° from the look direction, and the aim *distance* is what gets smoothed — glancing from a near wall to the sky moves the point 100 m but should only swing the gun a few degrees). The tracer is then drawn along `muzzle.forward`, so it is collinear with the barrel by construction. Measured: barrel off-axis from the shot line **0.9°**, tracer endpoint at viewport (0.496, 0.531) against a crosshair at (0.5, 0.5). Hit detection still comes off the screen centre, so what you hit matches the reticle rather than the barrel's offset — the ~6.5° the gun turns at 3.5 m is pure parallax and correct.

**Recoil** is a `SmoothStep` rise-then-settle envelope in the animator (`recoilRise` 0.035 s to a `recoilKick` of 3.2°, `recoilFall` 0.17 s back), driven by `WeaponController.Fire` calling `BlockCharacterAnimator.AddRecoil()`. It raises both arm pairs (body at `recoilBodyShare` 0.7), kicks the barrel up, and pushes `ViewRoot` back by `recoilPush` 14 mm. `AddRecoil` **restarts** the envelope rather than accumulating, so holding the trigger chatters evenly instead of the hands climbing away. The kick is applied in `LateUpdate`, i.e. *after* `Fire` has already traced the shot from the previous frame's muzzle orientation — so recoil can never pull a bullet off target. Traced live: 0 → 3.18° over 0.036 s, eased decay, hand viewport y 0.075 → 0.112 and back.

**The body arms move too**, since that is what the mirror and other players read. `armedBodySway` (0.55) keeps over half the walk's arm swing alive inside the held-weapon pose — welding the arms to the gun makes the body read as a mannequin being slid around. `armAimFollow` (0.5) adds a share of the look pitch on top of the torso's own, so an 80° look sweep swings the arms through 56° of elevation.

**Feet-on-floor shim.** A `CharacterController` rests its own `skinWidth` (0.08 by default) above the ground, so a body hung straight off the transform floats by exactly that much — plainly visible. `BlockRig.GroundOffset` reads `skinWidth` and the animator subtracts it from the hip height. Residual gap is 0.0067 m, which is just the `seam` shrink.

**The level is a Backrooms maze, also generated in code.** `BackroomsMap` (on the `Backrooms` root) builds it in `Awake` from a seed — 56×56 cells at 3.2 m, ~179 m square, 3 m ceiling. The scene itself holds no level geometry. `Ground`, `Plane` and the test `Cube` are gone; the `Directional Light` is **disabled, not deleted** (there is no sun indoors, but it is easy to put back). Note `GameObject.Find` will not return it while it is inactive.

Layout is three passes and all three earn their place:
- **Recursive backtracker maze** over the cells. This is what guarantees connectivity — verified by flood-filling the *actual colliders*, not the grid: 3136/3136 cells reachable from spawn. Later passes only ever *remove* walls, so connectivity cannot regress.
- **Rectangular rooms** for contrast; a uniform maze reads as corridor soup.
- **Random extra doorways** (`loopChance`). A perfect maze has exactly one path between any two points, which reads as a puzzle to solve rather than as being lost.

**Watch the openness metric, not the wall count.** A spanning-tree maze is already **~51% open** by neighbour-link count, so that is the floor, not zero. The first attempt (34 rooms, `loopChance` 0.16, 4 m cells) measured **70.7% open** and felt like a warehouse. Now 18 rooms / 0.08 / 3.2 m cells gives **60.2%**, and the metric that actually predicts claustrophobia is the **mean straight sightline: 6.3 m** (longest hallway 52.7 m — those long runs are worth keeping).

**Cost control.** Each straight run of wall is merged into **one box with one collider**, which is why 56×56 costs 1365 wall pieces rather than ~3000. All walls share the built-in cube mesh and one instanced material. Ceiling light panels are emissive boxes with **no collider** (they must not block shots); real point lights are far sparser (`lightSpacing` 5 → 121 lights) and have **shadows off** — a hundred shadow-casting lights would be ruinous and flat diffuse light is what the reference looks like anyway. Levers if it runs badly: `lightSpacing`, `panelSpacing`, then `gridWidth`/`gridHeight`.

Atmosphere is set at runtime in `ApplyAtmosphere`: exponential-squared fog (density 0.028, sickly yellow), flat ambient, skybox nulled. The fog does most of the dread — not being able to see how far the room goes.

**Mirror:** `PlanarMirror` (on the `Mirror` quad, material `Assets/Shaders/MirrorSurface.mat`) reflects the viewer camera through the surface and renders into a RenderTexture that the `NV/Mirror Surface` shader samples **by screen position** — mesh UVs would not line up with the reflection camera's frustum. It moves a real camera rather than applying a mirror matrix, so face culling stays correct and URP renders it in the normal loop; URP does not support calling `camera.Render()` manually. Unity's Quad has normals along −Z, hence `flipNormal = true`. It **assigns** `reflectLayers` straight to the reflection camera's `cullingMask` (it does not AND with the viewer's), so `reflectLayers` must include `PlayerBody` and exclude `FirstPersonArms`.

**Weapon switching:** `WeaponSwitcher` (keys 1 = empty hands, 2 = pistol) reuses `ProceduralReload` as the swap animation and does the show/hide via its `onBottom` callback — at the bottom of the motion, where the hands are out of frame. Armed state drives three things together: both pistol copies' active state, `BlockCharacterAnimator.Armed` (the held-weapon arm pose), and `WeaponController.Armed`.

**Weapon:** 8-round mag, R or empty-click reloads, damage via `SendMessageUpwards("OnHit", damage)`. The pistol is built from blocks too. **No block on the character carries a collider** — the `CharacterController` handles collision, and `hitMask` also excludes layers 8/9, so a shot can never hit the shooter (verified). `muzzle` is re-pointed in `Start` to the **viewmodel** pistol's muzzle, so tracers leave the barrel the player can actually see — it must be `Start`, since the rig builds during `Awake`.
