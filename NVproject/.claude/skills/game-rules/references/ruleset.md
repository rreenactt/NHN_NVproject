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
| Runner | **2 or more** Runners have escaped through the opened door — but never more than there *are* Runners (see below). |
| Seeker | Timer reaches 0 below that number of escapes (time attack), or **every** Runner goes down without a single one getting out. |

Evaluate on every escape and every timer tick. Reaching the number ends the match
immediately as a Runner win; timer-zero ends it as a Seeker win.

**The target never exceeds the number of Runners the match started with.** With
one Runner, one escape wins. A target of 2 in a two-player match is unreachable,
and an unreachable win condition is not a hard match — it is a match the Runner
cannot win by playing well, which then runs its clock out with nobody left in it.
So the number needed is `min(2, runners at start)`.

**A wipe is a Seeker win — decided, not implied.** The design doc never listed
one, so it sat open for a while; it is settled now. If every Runner goes down the
match ends there rather than making the Seeker sit out the rest of the clock in an
empty building.

Escaping is not being wiped out. The wipe win requires every Runner to have gone
**down**; a Runner who walked out through the door has not been eliminated, and
counting them as such hands the Seeker a win for losing.

## Objective: keys & door
- **10 keys** scattered at valid walkable locations. Runners pick them up and
  **carry** them, then **insert one at a time** into the door.
- The **door** spawns at a **random valid location each match**. **Visible to
  Runners only** — never rendered/highlighted for the Seeker.
- Inserting the **10th** key opens the door; Runners escape through it.
- Escaping is **standing in the open doorway for a hold time**, not touching it.
  The hold exists so the Seeker has a moment to interrupt the last step.
- **The hold's progress is public.** Everyone sees how far along the escaping
  Runner is, the Seeker included. That is deliberate even though the Seeker
  cannot see the door: the door's *position* stays hidden, but the fact that
  somebody is leaving right now is the one cue that makes the hold interruptible
  at all. A hold nobody can see is a delay, not a rule.
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

## Movement
| Input | Effect |
|-------|--------|
| Ctrl (hold) | **Sneak** — walk slowly, silent footsteps. Stance and body height do not change. |

Sneaking is the only way to move without feeding the Seeker's main sensor, so it
costs speed. **It is not a crouch**: no change of stance, no change of hitbox
height. The cost is the speed alone, which keeps "cross this open room quietly"
a decision about time rather than about a second body shape.

Known gap: the server only slows a player when it is told they are crouching, and
this deliberately never sends that. So in a networked match sneaking is silent but
not slow — the cost above is not being charged. Closing it needs a signal that
means "slow" without also meaning "shorter".

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
| Escapes to win | 2, capped at the number of Runners |
| Runner hits to die | 2 |
| Seeker magazine | 3 |
| Chain-drag wait | 3s |
| Device destroy hits | 4 |
| Teleport shared cooldown | 12s |
| Devices placed | 8-9 |
