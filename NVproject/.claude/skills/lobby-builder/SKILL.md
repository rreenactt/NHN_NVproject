---
name: lobby-builder
description: >
  Build the pre-match lobby for the Backrooms escape FPS in Unity 6 — a PUBG-style
  staging room where 5-6 players' 3D character models stand lined up in a row
  waiting for the match to start. Covers the lobby space (backrooms-toned waiting
  room), numbered stand slots, ready-up + countdown, character/skin customization
  with live preview, and slot swapping between players. Networking is deliberately
  deferred: all logic runs behind a transport interface with an offline
  implementation, so the lobby is playable solo today and gets wired to real
  netcode later. Use this whenever the user wants to create, change, or extend the
  lobby / waiting room / staging area / player lineup / ready screen / character
  select — or mentions "lobby", "로비", "waiting room", "ready up", "countdown",
  "slot", "자리바꾸기", "character select", even without naming the game.
---

# Lobby Builder (Backrooms Escape FPS)

A PUBG-style lobby: players enter, their 3D models stand in a numbered row facing
the camera, they customize their character, mark themselves ready, and a countdown
starts the match. Styled as a backrooms waiting room — the lobby is part of the
game world, not a menu floating in the void.

## Networking status — READ FIRST

**Netcode stack is undecided.** Everything here is built so the server work can be
added later without rewriting the lobby:

- All lobby state lives in `LobbyManager`, which never calls a network API directly.
- It talks to **`ILobbyTransport`** (`scripts/ILobbyTransport.cs`).
- `OfflineLobbyTransport` implements it locally so the lobby runs solo today with
  fake/bot players.
- Every place that will need real networking is marked with a
  **`// NETCODE:`** comment explaining exactly what must change.
- `references/netcode-integration.md` is the full handoff doc: what's authoritative,
  what must be replicated, what must be server-validated, and a per-stack
  (Netcode for GameObjects / Photon Fusion / Mirror) mapping.

When the user is ready to add networking, read `references/netcode-integration.md`
before touching any lobby code, and implement a new `ILobbyTransport` rather than
editing `LobbyManager`'s logic.

## What to build (the contract)

### The space
- A single enclosed **backrooms-toned waiting room**: mono-yellow damp walls, worn
  carpet, dropped ceiling with buzzing fluorescents. Reuse the exact palette from
  the `backrooms-map-generator` skill's `references/aesthetic-spec.md` so the lobby
  and the map read as the same building.
- It should feel like a room *adjacent* to the level — a holding area, slightly too
  quiet. See `references/lobby-style.md` for camera, lighting, and mood specifics.

### The lineup
- **`maxPlayers` stand slots** (default 6, supports 5) in a straight row, evenly
  spaced, all facing the lobby camera. Empty slots show a faint floor decal /
  number so the row reads as "waiting for more."
- Each occupied slot displays the player's **3D character model**, an idle
  animation, a nameplate, and a **READY** stamp when they've readied up.
- Slot order is stable — a player keeps their slot until they leave or swap.

### Ready + countdown
- Each player toggles **Ready**. When **all connected players are ready** (and the
  minimum player count is met), a **countdown** starts.
- Anyone un-readying **cancels** the countdown immediately.
- At zero, fire `OnMatchStarting` — the match/role assignment is owned by the
  `game-rules` skill's MatchManager, not by the lobby.

### Character / skin customization
- Live preview on the player's own model in their slot — changes are visible to
  everyone in the row, not just locally.
- Keep the options data-driven (`LobbyCustomizationCatalog`) so adding skins needs
  no code change.
- Customization is locked once the countdown reaches its lock threshold.

### Slot swapping (자리바꾸기)
Two supported modes, configurable:
- **Free move** — a player clicks any empty slot and moves there.
- **Swap request** — a player clicks an occupied slot to request a swap; the other
  player accepts or declines, then the two models trade places.

Swaps and moves must be **rejected while the countdown is locked**, and later must
be **server-validated** (see the `// NETCODE:` markers) so two players can't claim
the same slot.

## Workflow

1. **Confirm the Unity MCP bridge is Running.** Confirm the character model /
   prefab situation; if there are no character models yet, generate placeholder
   capsule-with-head stand-ins so the lineup works end-to-end today.
2. **Read `references/lobby-style.md`** before building the room or placing the
   camera — the framing is what makes it read as PUBG-style rather than a menu.
3. **Build the room**: enclosed backrooms space, one wall the camera looks toward,
   the lineup along it, fluorescent lighting, ambient hum.
4. **Place the slots**: instantiate `LobbySlot` markers in a row from
   `LobbyConfig` (count, spacing, facing). Keep them as transforms so a designer
   can nudge the row without touching code.
5. **Wire `LobbyManager`** with `OfflineLobbyTransport` and spawn fake players so
   the row can be seen filled immediately.
6. **Wire the lobby UI** (ready button, countdown, player list, customization
   panel). Reuse the `game-ui-generator` skill's USS so the lobby UI matches the
   in-game HUD.
7. **Test all four flows**: join/leave, ready/unready cancelling the countdown,
   customization visible on the model, and both swap modes.
8. **Report** what's built, and explicitly list every `// NETCODE:` marker left for
   the server pass so the next session can pick it up cold.

## Handoff discipline (the user asked for heavy notes)

This lobby will be revisited when the server work happens. So:
- Never delete or "clean up" a `// NETCODE:` marker — update it if the code moves.
- When adding new lobby state, add it to the replication table in
  `references/netcode-integration.md` in the same edit. State that isn't in that
  table will silently desync later.
- Keep authority decisions written down as comments at the decision point, not just
  in the doc — the person reading the code later may not open the doc first.

## Files
- `scripts/LobbyConfig.cs` — tunables: player count, slot spacing, countdown, lock.
- `scripts/ILobbyTransport.cs` — transport seam + `OfflineLobbyTransport`.
- `scripts/LobbyManager.cs` — lobby state machine, ready/countdown, slots, swaps.
- `scripts/LobbySlot.cs` — one stand position: model, nameplate, ready stamp.
- `references/lobby-style.md` — room, camera framing, lighting, mood.
- `references/netcode-integration.md` — the server-work handoff doc.

## Don't
- Don't put match/role logic here — the lobby hands off to MatchManager and stops.
- Don't call a networking API from `LobbyManager`; go through `ILobbyTransport`.
- Don't let customization or swaps mutate state after the countdown lock.
- Don't style the lobby as a clean menu — it's a room in the same building.
