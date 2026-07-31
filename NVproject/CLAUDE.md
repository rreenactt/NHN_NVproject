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
- `Mesh` and `Image` collide with namespaces inside the generated command wrapper — write `UnityEngine.Mesh` / `UnityEngine.UI.Image`.
- **Never hold runtime-built references in a `Dictionary` on a MonoBehaviour.** A script edit triggers a domain reload that does not exit play mode; `UnityEngine.Object` fields survive it, a `Dictionary` does not. The half-restored component then throws every frame. Use plain fields, and guard `Update` with a rebuild when a built reference has gone null.
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
Match           MatchBootstrap — the rules layer; builds everything below at runtime
 ├─ Match Manager / Device System / Match HUD
 ├─ __Objectives   Escape Door, Key ×10, Device ×9
 └─ __PracticeRunners  wandering Runner dummies (offline testing only)
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
- The fix is neither: `BlockCharacterAnimator.PointBarrelAt` sets the gun's **world** rotation directly with `LookRotation`, ignoring the arm. There is also a physical reason it can never inherit the hand — the barrel is perpendicular to a rigid arm block, so an arm reaching forward *forces* the barrel to point up or sideways.
- **That `LookRotation`'s up argument must be the camera's up, never `Vector3.up`.** Look far enough up and the barrel approaches world up itself; the two arguments become parallel, the roll is undefined, and the pistol visibly spins. Measured with world up: roll was −0.96° at 20° of pitch but **−131.7° at 82°**. With `cameraTransform.up` it is 0.000° across the entire ±85° range, because the barrel is clamped to within `barrelAlignClamp` of the look direction and so can never align with the camera's own up.

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

**Crosshair:** `Crosshair` builds a `ScreenSpaceOverlay` Canvas of four bars plus a centre dot at Awake — no prefab, no sprite asset, no scene-authored UI. Each bar is drawn twice, a black rect 2 px proud behind a white one, because a plain white reticle vanishes against the Backrooms' pale yellow walls. A `CanvasScaler` against 1920×1080 keeps it the same apparent size at any resolution.

**Hit marker:** four diagonal ticks that flash and drift outward as they fade, raised by `Bullet`'s `onImpact` callback → `WeaponController.OnBulletImpact` → `Crosshair.ShowHitMarker`. It fires when the round *lands*, not when the trigger is pulled — with projectiles those are different moments, which is the whole reason it earns its place. Falloff is squared so it is sharp on impact rather than a slow smear. It currently marks **any** impact, including walls; once there are enemies, gate it on the target actually receiving `OnHit`.

The gap is **dynamic**: `restGap` + `moveSpread` × (speed / sprintSpeed) + `shotSpread` × `BlockCharacterAnimator.RecoilWeight`. With projectile rounds that take ~40 ms to land, a reticle that visibly kicks is the only immediate feedback a shot happened. **The open/close smoothing must be asymmetric** — a single 0.09 s SmoothDamp only reached ~50% of the intended spread before the 0.2 s recoil envelope had already decayed, so the kick never registered. `openSmoothing` 0.02 s against `closeSmoothing` 0.13 s reaches 96% and reads as a snap-and-settle.

**Weapon switching:** `WeaponSwitcher` (keys 1 = empty hands, 2 = pistol) reuses `ProceduralReload` as the swap animation and does the show/hide via its `onBottom` callback — at the bottom of the motion, where the hands are out of frame. Armed state drives three things together: both pistol copies' active state, `BlockCharacterAnimator.Armed` (the held-weapon arm pose), and `WeaponController.Armed`.

**Weapon:** 8-round mag, R or empty-click reloads, damage via `SendMessageUpwards("OnHit", damage)`. The pistol is built from blocks too. **No block on the character carries a collider** — the `CharacterController` handles collision, and `hitMask` also excludes layers 8/9, so a shot can never hit the shooter (verified). `muzzle` is re-pointed in `Start` to the **viewmodel** pistol's muzzle, so rounds leave the barrel the player can actually see — it must be `Start`, since the rig builds during `Awake`.

**Shots are real projectiles, not hitscan.** `WeaponController.Fire` launches a `Bullet` and resolves nothing itself; the round works out its own impact as it flies (120 m/s default, ~40 ms over 5 m). Trajectory is integrated by hand, not by a Rigidbody — the same rule this project applies to thrown objects.

- **The swept test is the whole thing.** Each step raycasts along the segment the bullet *travelled*, never testing just the new position. At 120 m/s a round covers ~0.7 m per frame against 0.25 m walls, so a position test would tunnel instantly. Verified by firing at **100,000 m/s** — 602 m in one step at a wall 20 m away — and it still stops at the wall.
- **Fire along `AimPoint - origin`, never along `muzzle.forward`.** The muzzle is a viewmodel bone: it bobs with the walk, sways, takes the recoil push, and is re-aimed in `LateUpdate` — so reading its rotation from `Fire` in `Update` uses *last frame's* orientation with the bob added on top. Rounds fired while moving or turning then left at visibly wrong angles (this is what read as bullets "curving"; the trajectory itself is straight to 0.00000 m of lateral drift, verified while shoving the player sideways mid-flight). `AimPoint` is refreshed at the top of the same `Update`, so it is both current and on the reticle: impacts now land at viewport (0.5000, 0.5000) exactly, against 1.31% of screen off with `muzzle.forward` even at rest.
- The bullet has **no collider**; it only queries the world. Nothing can hit it and it cannot shove the shooter.
- `Fire` linecasts eye→muzzle and falls back to the eye position if blocked, or standing flush against a wall would let you shoot through it.
- **One raycast survives, in `UpdateAim`, and it is not hit detection** — it is what converges the barrel on the crosshair. Delete it and the gun fires parallel to the camera, so every close shot lands beside the reticle by the muzzle offset.
- `bulletGravity` defaults to **0** so the round stays on the crosshair at any range; turning it on makes the reticle lie at distance.
- Watch out when debugging: `RaycastHit.distance` here is only the portion of *one frame's step* before impact (0.0–0.7 m), not the flight distance. `Bullet` tracks `_travelled` for that reason.

## The match layer (`Assets/Scripts/Game/`)

The game itself — one Seeker against Runners who insert **10 keys** into a hidden door and escape. The ruleset is `.claude/skills/game-rules/references/ruleset.md` and it is **the source of truth**: change it first, then `GameConfig`, then the systems. Tunables live in `Assets/Settings/GameConfig.asset` (menu **Tools ▸ Backrooms ▸ Create Game Config Asset**); the scene object comes from **Tools ▸ Backrooms ▸ Set Up Match**.

**Every rule is decided in `MatchManager` and nowhere else.** Keys, door, devices and agents hold state and raise intentions; the manager resolves them. That is not tidiness — the game is asymmetric, so every rule is also an *information* rule, and a client that decides its own hits decides it was never hit. Wiring the existing `NVserver` in later means running this class on the host and replicating its events; the event list on it (`PhaseChanged`, `KeysChanged`, `EscapesChanged`, `AgentHit`, `MatchEnded`, `RolesAssigned`, `Notified`) is exactly what a replication layer has to carry. Nothing in it assumes the single local player it currently has.

**Asymmetric visibility is two culling layers, not two copies of the world.** `RunnerVision` (10) carries the door, `SeekerVision` (11) carries the blood trail, and `MatchLayers.ApplyRoleVisibility` points each camera at the half its owner may see. Toggling renderers per role leaks the moment a new object forgets the rule; a culling mask cannot leak because the camera never renders the layer. The Seeker's HUD is also told nothing about key progress — only the escape count.

**The door has no collider**, and neither do the keys. A door the Seeker cannot see but *can* walk into would be found by bumping into thin air.

**Combat counts hits, not health.** `Bullet` still calls `SendMessageUpwards("OnHit", damage)`; `PlayerAgent.OnHit` throws the number away. First hit → bleeding + teleport to a random standable cell; second → death, dropping carried keys where they fell. `hitImmunity` (0.75 s) exists because three rounds can be in the air at once — without it one burst kills through the teleport by hitting the place the victim just left.

**Bleeding is enforced by the trail, not by a timer.** Running lays a thin dotted line; standing still past `bleedStillGrace` pools blood on the spot, bigger and lasting `bleedPoolLifetimeScale`× longer. Hiding while wounded paints a sign over the hiding place, which is what "must keep moving" means here.

**The chain-drag is the Seeker's whole cost.** Emptying the 3-round magazine hands the empty to `ChainDrag` via `WeaponController.onMagazineEmpty`, which *replaces* the reload: a chain anchors on the nearest wall, drags the Seeker there, holds them `chainWait` 3 s, and only then reloads. `MovementLocked` (new on `FirstPersonController`) blocks movement while leaving the look free — `InputEnabled` would release the cursor, which mid-match reads as the game losing focus.

**Agents are never deactivated when they die or escape** — `SetPresent(false)` hides them instead. `SetActive(false)` fires `OnDisable`, which unregisters the agent in the middle of the manager's own loop over the roster, and the win conditions still need to count them.

**Two Unity traps this layer paid for, both the domain-reload family** (a script edit during play wipes managed state without re-running `Awake`):
- `MatchManager.Instance` / `DeviceSystem.Instance` re-find themselves lazily, `BackroomsMapGenerator.EnsureGrid()` re-solves the wiped grid from the same seed without rebuilding geometry, and every `System.Random` is behind a lazy `Rng` property. Each of these was a live NullReference every frame first.
- **Win conditions are evaluated in `Update`, never on unregister.** Leaving play mode disables agents one at a time, which read as the Seeker having wiped them out and wrote a fictional result into the log on every exit.

`MapDevice` registers *itself* in `OnEnable` and `DeviceSystem.Start` sweeps once for stragglers: the first match is started from `MatchBootstrap.Start`, too early to assume the system it wants is awake — measured as nine devices in the level and none of them registered.

**The HUD is UI Toolkit** (`Assets/Resources/UI/GameHUD.uxml` + `game-hud.uss` + `GameHudPanelSettings`, driven by `GameHudController`), and it is the one part of this project that is *not* built in code — a stylesheet is the only sane way to get flickering signage. It lives under `Resources/` so the controller can load it without anything wired in the scene. The **crosshair stays uGUI**: its canvas sits at `sortingOrder` 100 and the panel at 0, so the reticle draws over the HUD.

- **Role gating is structural, not cosmetic.** On `RolesAssigned` the tree is rebuilt from the UXML and the other side's panel is `RemoveFromHierarchy()`'d. A Runner's HUD contains no ammo counter; a Seeker's contains no key slots, no carried count and no door marker — verified by querying the live tree, not by reading the code. Hiding them instead would be one `display: flex` away from handing the Seeker the objective.
- Everything role-exclusive must therefore live *inside* `#runner-panel` or `#seeker-panel`. An element parked outside them is an element both sides get.
- The **door compass** is drawn on a ring around screen centre from the yaw between the camera and the door (verified to 0.09° against the true bearing, with a ↑/↓ when the door is on another storey). It makes a hidden door easy to find — `GameConfig.showDoorCompass` turns it off if locating the door should be most of the job.
- Scanlines and the wound vignette are **generated textures**, not art. The scanline texture is rebuilt at the screen's pixel height so the lines land one pixel apart; stretched, it degrades into a grey wash and the effect is lost.
- The full-map overlay is drawn from the level grid into a `Texture2D` (358×175 for the 35×35 two-floor map), not rendered by a second camera — a top-down camera here photographs ceiling tiles. The Seeker camera feed *is* a real camera, and it renders with the Seeker's own culling mask so the Runners' device cannot leak the door back to them.

**Offline testing.** `practiceRunners` (3) spawns NavMeshAgent dummies so the Seeker's half of the ruleset can be exercised solo; they deliberately **do not collect keys** (`PlayerAgent.collectsKeys`) or the objective is swept clean in a minute. Debug keys: **F1** swap side and restart, **F2** restart, **F5** take a hit. Set `practiceRunners` to 0 and `debugKeys` off for a real match.
