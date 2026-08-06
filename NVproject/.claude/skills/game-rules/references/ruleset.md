# Ruleset — Backrooms Escape FPS (authoritative)

This is the source of truth. Code implements these numbers; if a rule changes,
change it here first, then update `GameConfig` and the systems.

## Roles
- **Seeker** — exactly 1, armed with a gun. Cannot see the door or Runner key UI.
- **Runner** — everyone else, unarmed. Objective: escape.

## Match flow
1. **Lobby** — players join; roles unassigned.
2. **Role reveal** — one Seeker chosen; brief reveal screen per player.
3. **Playing** — timer counts down; keys/door/devices active.
4. **End** — win condition met or timer hits 0.

## Win conditions
| Side   | Wins when |
|--------|-----------|
| Runner | **2 or more** Runners have escaped through the opened door. |
| Seeker | Timer reaches 0 with **fewer than 2** escapes (time attack), i.e. the Seeker keeps escapes below 2 for the whole match. |

Evaluate on every escape and every timer tick. The 2nd escape ends the match
immediately as a Runner win; timer-zero ends it as a Seeker win.

## Objective: keys & door
- **10 keys** scattered at valid walkable locations. Runners pick them up and
  **carry** them, then **insert one at a time** into the door.
- The **door** spawns at a **random valid location each match**. **Visible to
  Runners only** — never rendered/highlighted for the Seeker.
- Inserting the **10th** key opens the door; Runners escape through it.
- Distinguish **carried keys** (on a Runner) from **inserted keys** (in the door).
  Total inserted is global progress toward 10.

## Combat
| Rule | Value / behavior |
|------|------------------|
| Hits to kill a Runner | **2** |
| First hit | Runner enters **Bleeding**; leaves a **blood trail**; must keep moving. |
| Blood trail | Visible to the **Seeker** (via trail-vision), **and to the Runner leaking it** — not to any other Runner. World-space VFX. |
| Any hit | Runner is **teleported to a random valid location**. |
| Bleeding cleared | Only by the **Stop Bleeding** device (1x). |

Bleeding "must keep moving": define a rule such as escalating penalty if the
Runner stays still while bleeding (e.g. faster/heavier trail, or a slow tick).
Tune in playtest; keep it in `GameConfig`.

Seeing your own blood is part of that rule, not a leak of information: the
penalty for standing still *is* the pool on the floor, and a Runner who cannot
see it has no way to learn the rule or to judge how much they are giving away.
The trail stays hidden from **other** Runners — it is the Seeker's evidence and
the bleeder's own problem.

## Seeker gun & chain-drag
| Rule | Value |
|------|-------|
| Magazine | **3 shots** |
| Emptying the mag | Triggers **chain-drag** |
| Chain-drag | A chain emerges from a point, drags the Seeker to it, Seeker **waits 3s**, then **reloads** |
| Destroying a device | **4 shots** on the device |

The chain-drag is the Seeker's main risk/cost — spending all 3 shots strands them
for 3s at the drag point. It is intentionally punishing; do not shorten it without
playtesting the whole balance.

## Devices (8-9 in the map)
Each placed device provides ONE effect. Uses: **1x** = single use per match,
**∞** = repeatable.

| # | Effect | Uses | Notes |
|---|--------|------|-------|
| 1 | Add match time | 1x | Extends the timer — helps Runners vs time attack. |
| 2 | Full map view (who is where) | ∞ | Shows all player positions briefly. |
| 3 | Stop bleeding | 1x | Clears a Runner's bleeding + trail. |
| 4 | Freeze all + walls transparent | 1x | Everyone frozen briefly; walls go see-through. |
| 5 | Seeker camera view | ∞ | Shows a feed from the Seeker's position. |
| 6 | 1-to-1 teleport elsewhere | ∞, shared 12s CD | After anyone uses it, next use is locked for **12s** globally. |

- Place **8-9** device instances; the mix of effects is a level-design choice.
  A device instance has a type from the table; multiple instances can share a type.
- **Seeker counter-play**: the Seeker destroys any device with **4 shots**
  (repeatable), removing its effect for the rest of the match.
- The teleport cooldown is **shared across all Runners** (global 12s lockout), not
  per-player.

## Edge cases (decide and implement consistently)
- **Runner dies while carrying keys** → drop carried keys at death location (or
  return them to the pool). Pick one; default: **drop at death location** so keys
  stay findable.
- **Simultaneous insert** of the 10th key → host resolves order; only one "door
  opens" event fires.
- **Shot while already bleeding** → that's the 2nd hit → death (per "2 hits = die").
- **Teleport-on-hit lands in an invalid/occupied cell** → reroll to nearest valid
  walkable cell.
- **Seeker destroys the only device of a type** → that effect is simply gone; no
  respawn (unless a designer enables respawn in config).
- **Player disconnects** → host removes them from counts; recompute win conditions
  (e.g. if too few Runners remain, decide a default — configurable).
- **Timer extended past a hit event** → device time-add stacks additively onto the
  remaining timer.

## Default tunables (starting point — playtest to adjust)
| Key | Default |
|-----|---------|
| Match duration | 8:00 (tune) |
| Keys required | 10 |
| Escapes to win | 2 |
| Runner hits to die | 2 |
| Seeker magazine | 3 |
| Chain-drag wait | 3s |
| Device destroy hits | 4 |
| Teleport shared cooldown | 12s |
| Devices placed | 8-9 |
