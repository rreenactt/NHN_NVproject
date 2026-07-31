---
name: game-rules
description: >
  Define and implement the ruleset for a Backrooms-style asymmetric hide-and-seek
  / escape FPS in Unity 6: one armed Seeker vs unarmed Runners who collect 10 keys
  and insert them into a hidden, randomly-placed door to escape (2+ escapes = Runner
  win). Covers health/bleeding + blood trails, teleport-on-hit, the Seeker's 3-shot
  magazine and chain-drag reload penalty, 8-9 activatable devices, and win/timeout
  conditions. Use this whenever the user wants to create, change, balance, or
  implement game rules / mechanics / systems / win conditions for this game — or
  mentions "rules", "mechanics", "seeker", "runner", "keys", "door", "devices",
  "bleeding", "escape", "win condition", "gameplay loop", even without naming the game.
---

# Game Rules (Backrooms Escape FPS)

This skill is the authoritative source of truth for how the game plays. The full,
exact ruleset — every number, timing, and edge case — lives in
`references/ruleset.md`. **Read it before implementing or changing anything.** The
C# scaffolds in `scripts/` implement that ruleset; keep code and ruleset in sync.

## Netcode note (read first)

This is a multiplayer game (Seeker vs multiple Runners, "2+ escape to win"). The
scaffolds here implement **authoritative game logic on a single source of truth**
(a server/host `MatchManager`). They do NOT include network transport. Wire them
to a netcode layer — Netcode for GameObjects, Photon Fusion, or Mirror — with the
MatchManager running on the host/server and state replicated to clients. Never
trust a client for hits, key inserts, escapes, or device use. If the user wants
the netcode layer built too, treat that as a separate task and ask which stack.

## Core loop (the contract)

Implement all of this. Treat each rule as an acceptance criterion.

### Roles
- One **Seeker** (armed). All others are **Runners** (unarmed).

### Runner win / escape
- Runners find **10 keys** scattered in the map and insert them **one at a time**
  into a **hidden door** placed at a **random location each match**.
- The door is **visible to Runners only** — the Seeker cannot see it.
- When all 10 keys are inserted, the door **opens**. Runners escape through it.
- **2 or more Runners escaping = Runner victory.**

### Seeker win
- **Time attack**: if the match timer runs out before 2 Runners escape, Seeker wins.
- Equivalently, holding escapes below 2 for the whole match = Seeker wins.

### Combat, bleeding, teleport
- Runners have **2 hits**. **2 hits = death.**
- **1 hit = bleeding**: the Runner must keep moving and **leaves a blood trail**
  the **Seeker can see**. (Bleeding can be cleared once by a device — see below.)
- **Being shot teleports the Runner to a random location** in the map.

### Seeker gun + chain-drag penalty
- Magazine = **3 shots**.
- **Firing all 3** triggers the **chain**: a chain emerges from a point and drags
  the Seeker to that point; the Seeker **waits 3 seconds** there, then **reloads**.
  Emptying the mag is a real cost — design and tune it as a punishment, not a free
  reload.

### Devices (8-9 placed in the map)
Each device, when activated, produces one effect. Uses are either **one-time (1x)**
or **repeatable (∞)** per the table in `references/ruleset.md`:
- Add match time (1x)
- Full map view — who is where (∞)
- Stop bleeding (1x)
- Freeze everyone + walls briefly transparent (1x)
- View camera at the Seeker's location (∞)
- 1-to-1 teleport elsewhere — **shared 12s cooldown** (after one player uses it,
  the next can use it 12s later)
- The **Seeker can destroy any device with 4 shots** (repeatable).

## Implementation workflow

1. **Read `references/ruleset.md`** in full. It has the exact numbers, the device
   table, and edge cases (what happens on disconnect, simultaneous inserts, death
   while carrying keys, etc.).
2. **Confirm the netcode stack** (or that you're building single-player/logic-only
   for now). The MatchManager must be host-authoritative.
3. **Create the config asset.** `scripts/GameConfig.cs` is a ScriptableObject with
   every tunable (key count, hits, mag size, chain wait, cooldowns, timer, device
   counts). Create one asset and let designers tune without code changes.
4. **Wire the MatchManager.** `scripts/MatchManager.cs` runs the match state
   machine (Lobby → RoleReveal → Playing → End), owns the timer, tracks keys/
   escapes, and evaluates win conditions every relevant event. Hook its events to
   the `game-ui-generator` HUD.
5. **Wire the device system.** `scripts/DeviceSystem.cs` defines device types,
   activation, use-limits, the shared teleport cooldown, and the 4-hit destroy.
6. **Wire combat.** On a validated hit: apply bleeding on 1st, death on 2nd,
   teleport the Runner to a random valid cell, and start the blood trail (trail
   VFX is world-space; the Seeker's trail-vision reveals it). On the Seeker
   emptying the mag, start the chain-drag sequence.
7. **Place keys + door.** Scatter 10 keys and place the hidden door at a random
   valid location each match; door mesh/marker renders for Runners only.
8. **Verify against the acceptance list** and report each rule's status. Call out
   anything stubbed pending netcode.

## Balancing knobs

All in `GameConfig` so tuning needs no recompile: match duration, key count,
device counts and per-type use limits, teleport cooldown (12s), chain wait (3s),
mag size (3), destroy hits (4), bleeding movement rules, escapes-needed (2).
Start from the ruleset defaults, then playtest — asymmetric games live or die on
these numbers.

## Files
- `references/ruleset.md` — the authoritative ruleset + device table + edge cases.
- `scripts/GameConfig.cs` — ScriptableObject of every tunable value.
- `scripts/MatchManager.cs` — host-authoritative match state machine + win logic.
- `scripts/DeviceSystem.cs` — device types, activation, cooldowns, destroy.

## Don't
- Don't let the Seeker see the door or key objective state — it's a Runner secret.
- Don't trust clients for hits/inserts/escapes/device use — host authority only.
- Don't make emptying the mag free — the chain-drag is a core balancing lever.
- Don't diverge code from `references/ruleset.md`; update the ruleset first, then code.
