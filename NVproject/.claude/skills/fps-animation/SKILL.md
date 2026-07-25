---
name: fps-animation
description: >
  Set up and adjust animation for a 3D first-person shooter in Unity via MCP — Animator
  Controllers, state machines, blend trees for locomotion, first-person arms/viewmodel animation,
  and procedural motion (weapon sway, bob, recoil kick, ADS). Use whenever the user works on
  animation or movement feel: "애니메이션 조정", "애니메이터", "블렌드 트리", "총 흔들림/반동
  모션", "걷기/뛰기 애니메이션", "손 모델 움직임", viewmodel, animation blending, procedural sway,
  "애니메이션이 뚝뚝 끊겨". Assumes unity-mcp-ops for tool calls.
---

# FPS Animation

Read **unity-mcp-ops** first. In an FPS, animation splits into two very different jobs — treat
them separately or you'll fight yourself:

1. **Authored clips** driven by an Animator Controller (reload, draw, inspect, footstep-synced
   arms). Good for discrete, designed motions.
2. **Procedural motion** computed in code (sway, bob, recoil kick, ADS lerp). Good for anything
   that must respond continuously to input/velocity. Most of the "juice" is here, and it needs
   NO clips.

## Animator Controller structure (keep it flat)

For first-person arms/weapon, a small controller beats a deep one:

```
Base Layer
├── Idle  ──(Speed > 0.1)──►  Locomotion (BlendTree)
│                              ├─ Walk
│                              └─ Sprint     (blended by Speed param)
├── Any State ──(trigger "Reload")──► Reload ──(exit)──► back
├── Any State ──(trigger "Fire")────► Fire (additive layer, see below)
└── Any State ──(trigger "Draw")────► Draw
```

Parameters to expose: `Speed` (float), `IsGrounded` (bool), triggers `Fire` / `Reload` / `Draw`.
Drive these from the character/weapon scripts via `animator.SetFloat/SetBool/SetTrigger` — cache
the `Animator` and the parameter **hashes** (`Animator.StringToHash`) in `Awake`, never look up
by string every frame.

## Blend trees for locomotion

Use a 1D blend tree on `Speed` for walk↔sprint, or 2D (`Speed X`, `Speed Z`) for strafing arms.
Set transition durations to ~0.1–0.15s; longer feels mushy, zero feels robotic. Smooth the
`Speed` parameter you feed in (damp it) rather than snapping, so blends don't pop.

## Building it via MCP

The Animator Controller and its states are assets. Create/wire them with:

- `add_package` if you need the animation rigging package for IK.
- `execute_menu_item` → `Assets/Create/Animator Controller` to make the asset, then
  `select_gameobject` the player and `update_component` to add an `Animator` and assign the
  controller.
- States, parameters, and transitions are fiddly to build blind. The reliable path: generate a
  small **editor script** (`Assets/Editor/FpsAnimatorBuilder.cs`) using
  `UnityEditor.Animations.AnimatorController` APIs to construct states/blend trees/transitions in
  code, expose it as a menu item, then run it with `execute_menu_item`. This is deterministic and
  reviewable, versus clicking through the graph by proxy.

## Procedural motion (no clips needed) — the real feel

### Weapon sway (mouse-driven)
Offset the weapon holder opposite to mouse movement, then lerp back. Runs in `Update` but is
trivial math (a lerp), which is fine — the convention is to avoid *expensive* per-frame work.

```csharp
Vector2 look = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
Quaternion target = Quaternion.Euler(-look.y * swayAmount, look.x * swayAmount, 0f);
weaponHolder.localRotation = Quaternion.Slerp(
    weaponHolder.localRotation, target, swaySmooth * Time.deltaTime);
```

### View bob (velocity-driven)
Sample a sine wave scaled by move speed, applied to the camera/holder local position. Kill the
amplitude to zero when grounded speed ≈ 0 so it settles cleanly.

### Recoil kick
On fire, push a `recoilRotation` up/back, then Slerp it toward zero every frame. Additive on top
of sway so both read at once. (This is the same recoil covered in fps-weapon-system — keep the
*visual* kick here and the *aim* kick with the weapon, or unify them, but don't double-apply.)

### ADS (aim down sights)
Lerp the weapon holder between a hip `Transform` and an ads `Transform`, and lerp camera FOV down
(e.g. 60→45) on the same `t`. One `aimProgress` float drives position, rotation, FOV, and reduced
sway — keep them in sync off a single value.

## Root motion: usually OFF for FPS

First-person locomotion is code-driven (CharacterController). Leave `Animator.applyRootMotion`
**false** so the animation doesn't fight your movement code. Turn it on only for specific
full-body third-person setups.

## IK (optional polish)

For the off-hand gripping the foregrip, use a Two-Bone IK constraint (Animation Rigging package)
targeting a `Transform` on the weapon. This keeps the left hand glued to the gun across different
weapons without re-animating.

## "Animation looks wrong" — diagnosing

- **Snapping between states** → transition duration is 0, or you're setting a trigger every frame.
  Fire triggers on the input edge (`GetKeyDown`), not while held.
- **Blend never reaches sprint** → the `Speed` you feed doesn't reach the blend tree's max
  threshold; log the value.
- **Sway/bob stutters** → you're driving it in `FixedUpdate`; move continuous visual motion to
  `Update`.
- **Reload plays but arms teleport** → mixing root motion with code movement; disable root motion.
