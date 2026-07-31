---
name: game-ui-generator
description: >
  Generate the in-game UI for a Backrooms-style asymmetric hide-and-seek / escape
  FPS in Unity 6, styled to match the liminal mono-yellow mood (fluorescent glow,
  CRT scanlines, worn stencil labels). Builds role-specific HUDs (Seeker vs
  Runner), objective/key trackers, device prompts and cooldowns, match timer,
  overlays (full-map, seeker camera view, freeze/x-ray), and win/lose screens
  using UI Toolkit (UXML + USS). Use this whenever the user wants to create,
  design, restyle, or add game UI / HUD / menus / screens for this game — or
  mentions "UI", "HUD", "interface", "menu", "screen", "key counter", "ammo",
  "timer", "objective marker", "win screen", even if they don't name the game.
---

# Game UI Generator (Backrooms Escape FPS)

Build the runtime UI in **UI Toolkit** (Unity 6's recommended runtime UI) and
style it to sit inside the same liminal world as the map. The UI is diegetic-
leaning: it should feel like worn facility signage and a flickering CRT overlay,
not a clean esports HUD. Read `references/ui-style.md` before styling anything —
the mood is what makes the UI belong to this game.

If the project uses uGUI (Canvas) instead of UI Toolkit, say so and offer to
regenerate as a Canvas hierarchy; the screen inventory and style below carry over.

## Screen & HUD inventory (the contract)

The game is asymmetric, so the UI is role-driven. Build every element below and
gate visibility by role. Treat this list as acceptance criteria.

### Shared (both roles)
- **Match timer** — the central clock. Seeker wins on timeout, so this is the
  most important shared readout. Style it as a flickering facility clock.
- **Escaped counter** — `escaped / 2 needed`. Updates live.
- **Role reveal** — full-screen card at match start: `SEEKER` or `RUNNER`, in the
  stencil style, with a short unsettling flavor line.
- **Win / Lose screens** — Runner win (2+ escaped), Seeker win (timeout OR fewer
  than 2 escaped). Distinct copy per outcome.
- **Interaction prompt** — contextual `[E]` prompt for devices, keys, and the door.

### Runner HUD
- **Key tracker** — `keys X / 10`, styled as slots that fill as keys are inserted
  into the door. Show carried keys vs inserted keys distinctly.
- **Door marker** — an off-screen directional indicator / compass arrow pointing
  to the hidden door. **Runners only** — never render this for the Seeker.
- **Health** — two hit-pips. On first hit, show a persistent **BLEEDING** state:
  pulsing red vignette + a "leaving a trail" warning. On second hit → death.
- **Device panel** — active device effects and cooldowns: the shared **teleport**
  12s cooldown (show whose/global lockout), map-view toggle, x-ray/freeze banner,
  stop-bleeding confirmation, seeker-camera feed.

### Seeker HUD
- **Ammo** — three shells. Empty state triggers the **CHAIN / DRAG** penalty UI:
  a 3-second "being dragged — reloading" lock with a countdown.
- **Blood-trail vision** — a subtle indicator that trail-tracking is active (the
  trail itself is world-space VFX; the HUD just confirms the sense is on).
- **Device-destroy reticle** — when aiming at a device, show a 4-hit destroy
  meter. The Seeker cannot see the door or the key/objective UI — omit them.

### Overlays (triggered by device effects)
- **Full-map overlay** — top-down schematic showing who is where (reusable device).
- **Seeker camera view** — a small CRT "security feed" window of the Seeker's
  position (reusable device).
- **Freeze + x-ray banner** — brief "ALL FROZEN" state with walls-transparent cue
  (one-time device). Fade in/out; keep it short and disorienting.

## Workflow

1. **Confirm environment & role source.** Verify the Unity MCP bridge is Running.
   Confirm where role/state comes from (the `game-rules` skill's MatchManager, or
   a placeholder). The HUD binds to that state; if it doesn't exist yet, wire to a
   mock state object and note it for later.
2. **Read the style.** Load `references/ui-style.md`. Pull the palette from the
   map's `aesthetic-spec.md` if the backrooms-map-generator skill is present, so
   UI and world share exact colors.
3. **Build the UXML.** Start from `scripts/GameHUD.uxml` — it already lays out the
   shared bar, both role panels, and overlay containers with names the controller
   binds to. Extend, don't rebuild from scratch.
4. **Style with USS.** Apply `scripts/game-hud.uss` (scanlines, fluorescent glow,
   stencil type, worn edges). Keep it readable — liminal ≠ illegible.
5. **Wire the controller.** `scripts/GameHudController.cs` toggles role panels,
   updates counters, drives cooldowns, and shows/hides overlays. Bind it to the
   real match state (events from MatchManager) as it becomes available.
6. **Verify per role.** Enter play mode as Seeker and as Runner; confirm each role
   sees only its allowed elements (critical: Runners see the door marker, Seeker
   never does; Seeker sees ammo/chain, Runners never do).
7. **Report** which screens are built, what's bound to real vs mock state, and any
   art placeholders to replace.

## Hard visibility rules (get these wrong and the game breaks)
- The **door marker / key objective UI** renders for Runners only.
- **Ammo, chain-drag, and device-destroy reticle** render for the Seeker only.
- The **seeker-camera** and **full-map** overlays are device-gated for Runners.
- Nothing role-exclusive should be present in the other role's UXML branch even
  hidden — build separate panels so there's no accidental information leak.

## Files
- `scripts/GameHUD.uxml` — UI Toolkit layout: shared bar, Runner panel, Seeker
  panel, overlay containers. Element names match the controller.
- `scripts/game-hud.uss` — liminal styling (scanlines, glow, stencil, vignette).
- `scripts/GameHudController.cs` — binds match state to the UI, role gating,
  cooldowns, overlays.
- `references/ui-style.md` — the UI mood spec: palette, type, motion, do/don't.
