---
name: fps-graphics-vfx
description: >
  Set up rendering, lighting, post-processing, materials, and combat VFX for a 3D first-person
  shooter in Unity via MCP — URP/HDRP pipeline setup, Volume-based post FX (bloom, color grading,
  vignette, motion blur), lighting and reflection probes, PBR materials, and effects like muzzle
  flash, impact/hit particles, tracers, and decals. Use whenever the user works on visuals or
  effects: "그래픽 요소", "머티리얼/재질", "라이팅/조명", "포스트 프로세싱", "총구 화염",
  "피격 이펙트", "탄흔/데칼", "화면 효과", graphics, look, VFX, muzzle flash, "게임이 밋밋해".
  Assumes unity-mcp-ops for tool calls.
---

# FPS Graphics & VFX

Read **unity-mcp-ops** first. Split the work into three layers: **pipeline** (once), **scene look**
(lighting + post), and **combat VFX** (per-effect). Do them in that order — post-processing on top
of an unlit, un-tonemapped scene just looks noisy.

## 1. Pipeline — decide and commit early

- **URP** (Universal RP): the right default for most FPS projects — great performance, mobile→PC,
  full post stack. Install `com.unity.render-pipelines.universal` via `add_package`, create a URP
  asset (`Assets/Create/Rendering/URP Asset`), and assign it in Project Settings → Graphics /
  Quality (`execute_menu_item` → `Edit/Project Settings...`).
- **HDRP**: only if you specifically need high-end lighting on PC/console and accept the cost.
- **Don't switch pipelines mid-project casually** — materials must be upgraded
  (`Edit/Rendering/Materials/Convert...`). Ask before converting an existing project.

Confirm the choice with the user if the project isn't already on one.

## 2. Scene look

### Lighting
- One **Directional Light** as sun/key; set intensity and a slightly warm/cool color for mood.
- Mark static geometry **Contribute GI** and bake lightmaps for stable, cheap indirect light;
  keep dynamic lights (muzzle flashes) realtime.
- Add a **Reflection Probe** per room/area so metals read correctly.
- Set the **Skybox / ambient** (Environment tab) — flat ambient is the #1 reason a scene looks
  "gamey and cheap".

### Post-processing (URP Volume)
Create a **global Volume** (`execute_menu_item` → `GameObject/Volume/Global Volume`) with a
profile, then add overrides. A tasteful FPS baseline:

| Override | Purpose | Restraint |
|---|---|---|
| **Tonemapping** (ACES) | filmic contrast; makes everything else look right | always on |
| **Bloom** | glow on bright emissives / muzzle flash | threshold high, intensity low |
| **Color Adjustments** | contrast/saturation/exposure grade | subtle |
| **Vignette** | focuses the eye | ~0.2–0.3, never heavy |
| **Motion Blur** | speed feel | low; some players disable it |
| **Chromatic Aberration / Film Grain** | texture | very sparingly |

Rule of thumb: if the player *notices* the post FX, it's too strong. Grade for cohesion, not
spectacle.

## 3. Materials (PBR)

Edit with `update_material` / inspect with `get_material_info`. For URP/Lit:
- **Metallic** and **Smoothness** carry the look — a gun is high-metallic, mid-smoothness; concrete
  is zero-metallic, low-smoothness.
- Use **Emission** for anything that should glow (weapon LEDs, sci-fi panels) so Bloom picks it up.
- Keep a small set of shared materials; per-object material instances balloon draw calls.

## 4. Combat VFX

### Muzzle flash
A short-lived `ParticleSystem` (or a flipbook quad) parented to `MuzzlePoint`, plus a brief
**realtime point light** flicker for the flash-lights-the-room effect. `.Play()` it from the
weapon's `Fire()` (already wired in fps-weapon-system). Keep lifetime ~0.05s — a lingering flash
looks wrong.

### Impact effects
Spawn an impact prefab at the raycast `hit.point`, oriented to `hit.normal`
(`Quaternion.LookRotation(hit.normal)`). Ideally swap the effect by surface type (metal sparks vs
concrete dust vs flesh) using a tag/material lookup on what was hit. **Pool** these — instantiating
on every bullet hit causes GC hitches; a simple object pool removes the stutter.

### Bullet tracers
A thin stretched quad or `LineRenderer` from `MuzzlePoint` to `hit.point`, alive a few frames.
Purely cosmetic — fire it alongside the hitscan ray, don't let it affect hit detection.

### Decals (bullet holes)
Use URP **Decal Projectors** at hit points for bullet holes/scorch. Cap the count (ring buffer /
pool) and fade the oldest, or they pile up and tank performance.

## Performance guardrails (an FPS is fill-rate and draw-call bound)

- **Pool** all repeating VFX (impacts, tracers, decals). No `Instantiate`/`Destroy` per shot.
- Cap particle counts and decal lifetimes.
- Bake static lighting; keep realtime lights few and short-lived.
- Watch draw calls via the Profiler (`run_tests` isn't for this — use the Profiler window / Stats).
- Emissive + Bloom is cheaper than lots of realtime lights for "glow".

## "It looks flat / cheap" — quick wins in order

1. Turn on **ACES tonemapping** (biggest single improvement).
2. Fix **ambient/skybox** so shadows aren't pure black and lit areas aren't blown out.
3. Add **subtle bloom + vignette + slight contrast**.
4. Give surfaces correct **metallic/smoothness** instead of default gray Lit.
5. Add one realtime **muzzle flash light** so combat pops.
