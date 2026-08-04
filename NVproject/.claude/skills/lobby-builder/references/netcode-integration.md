# Netcode Integration — Handoff Doc

**Status: networking NOT implemented.** The lobby currently runs on
`OfflineLobbyTransport` with local/fake players. This document is the complete
plan for the server pass. Read it fully before writing any networking code.

If you are picking this up cold: start at "The seam", then "Replication table",
then "Where the markers are".

---

## The seam

`LobbyManager` contains **all lobby logic and zero networking**. It communicates
through `ILobbyTransport`:

```
LobbyManager  ──►  ILobbyTransport  ──►  OfflineLobbyTransport   (today)
                                    └─►  NgoLobbyTransport       (to write)
                                    └─►  FusionLobbyTransport    (to write)
                                    └─►  MirrorLobbyTransport    (to write)
```

**Rule: implement a new `ILobbyTransport`. Do NOT move networking calls into
`LobbyManager`.** If a transport method's signature doesn't fit your stack, change
the interface and update the offline implementation too — never bypass it.

---

## Authority model

The rest of the game (`game-rules` skill) is **host-authoritative**. The lobby must
match that:

| Concern | Authority | Notes |
|---|---|---|
| Player join / leave | Server | Server assigns the initial slot. |
| Slot assignment & swaps | **Server** | Two clients can request the same slot in the same frame — server decides, clients apply the result. |
| Ready state | Server-validated | Client requests, server sets and broadcasts. |
| Countdown timer | **Server only** | Clients display a replicated value; never run a local countdown as truth. |
| Countdown start/cancel | Server | Server evaluates "all ready" — never trust a client's claim. |
| Customization choice | Client requests, server validates | Validate the id exists in the catalog and isn't locked. |
| Match start | Server | Server transitions everyone; hands off to MatchManager. |

**The single most important rule:** the countdown must be server-owned. A
client-run countdown is the classic source of "match started at different times
for different players".

---

## Replication table

Every piece of lobby state that must reach clients. **If you add lobby state, add
a row here in the same edit** — untabled state is how desyncs get shipped.

| State | Owner | Replicate to | Change frequency | Notes |
|---|---|---|---|---|
| `playerId` list | Server | All | On join/leave | Source of the row order. |
| `slotIndex` per player | Server | All | On join/leave/swap | Drives model placement. |
| `displayName` | Server | All | On join | Sanitize/validate on server. |
| `isReady` | Server | All | On toggle | Drives READY stamp + countdown eval. |
| `customizationIds` | Server | All | On change | Must be visible on everyone's row, not local-only. |
| `countdownRemaining` | Server | All | Per tick (or start time + duration) | Prefer replicating a **start timestamp + duration** over a per-frame float. |
| `lobbyState` (Waiting/CountingDown/Locked/Starting) | Server | All | On transition | Gate input on this. |
| pending swap requests | Server | The two involved | On request/resolve | Don't broadcast to everyone. |
| `isBot` | Server | All | On join | Only marks a filler player. Real clients are never bots; do not let a client claim this. |

**Explicitly not replicated:**

| State | Why |
|---|---|
| `isLocal` | Derived per client from `playerId == LocalPlayerId`. Replicating it would be wrong on every machine but one. |
| `LobbySlot` / `LobbyMannequin` / camera / room | Pure view. Rebuilt from the replicated roster on every `RosterChanged`; nothing about the scene needs to travel. |
| `OfflineLobbyTransport`'s bot timers and swap answers | A solo-testing affordance that exists only in the offline transport. The networked transport must not grow an equivalent. |

**Bandwidth note:** replicate the countdown as `startServerTime + duration` and let
clients compute the remaining value locally. Per-frame float replication of a timer
is wasteful and jitters.

---

## Where the markers are

Every integration point in the code is tagged `// NETCODE:`. Search the lobby
scripts for that string — it is the authoritative checklist. **As built there are 17
of them across `Assets/Scripts/Lobby/`**, and the one that does the most work is in
`LobbyBootstrap.Awake`: swapping the transport there is the entire wiring change, and
nothing else in the lobby moves.

Summary:

1. **`ILobbyTransport.cs`** — the interface itself; each method documents what the
   networked version must guarantee.
2. **`LobbyManager.RequestReady`** — must become a client→server RPC; server sets
   state and broadcasts.
3. **`LobbyManager.EvaluateCountdown`** — server-only. Guard with an `IsServer`
   check in the networked transport.
4. **`LobbyManager.TickCountdown`** — server ticks; clients display.
5. **`LobbyManager.RequestMoveToSlot` / `RequestSwap`** — server must resolve
   conflicts and reject while locked.
6. **`LobbyManager.RequestCustomization`** — server validates the id and lock state.
7. **`LobbyManager.StartMatch`** — server-driven scene/state transition for all.
8. **`OfflineLobbyTransport`** — keep it working; it's how the lobby stays testable
   solo after networking lands.
9. **`LobbyBootstrap.Awake`** — the construction site. One `new OfflineLobbyTransport(...)`
   to replace, and the manager is already written to not care which one it gets.
10. **`LobbyCustomizationCatalog`** — the validation table the server checks a requested
    option id against. Client and server must be reading the *same* catalog, so it has to
    ship with the build rather than being authored per-client.

## What was actually built (state of play)

- `LobbyManager` holds the state machine, ready/countdown, slots, swaps, customisation —
  and contains no networking call of any kind.
- `OfflineLobbyTransport` queues requests for one frame before delivering them, on
  purpose: a networked transport always has latency there, and code that accidentally
  depended on same-frame application would break the moment it was swapped in.
- It also invents the other players (join, ready up on their own clock, answer swap
  requests ~70% of the time) so all four flows are exercisable solo.
- Verified offline: join, move to an empty stand, swap request accepted and both figures
  traded places, customisation visible on the model in the row, countdown starting when
  everyone is ready, un-readying cancelling it mid-count, and the lock refusing
  customisation, un-ready and slot moves after the threshold.

---

## Per-stack mapping

### Netcode for GameObjects (Unity official)
- `LobbyManager` on a `NetworkBehaviour`; state in `NetworkList<T>` /
  `NetworkVariable<T>`.
- Client requests → `[Rpc(SendTo.Server)]`; broadcasts → `NetworkVariable` change
  events (no manual broadcast RPC needed).
- Countdown: `NetworkVariable<double>` holding `NetworkManager.ServerTime.Time` at
  start; clients derive remaining.
- Player join/leave: `NetworkManager.OnClientConnectedCallback` / disconnect.

### Photon Fusion
- Use a `NetworkBehaviour` with `[Networked]` properties and `ChangeDetector` for
  UI updates.
- Client requests → `[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]`.
- Countdown: `TickTimer` — it's built exactly for this.
- Slot data fits well in a `[Networked, Capacity(6)] NetworkArray<...>`.

### Mirror
- `NetworkBehaviour` with `SyncList` / `[SyncVar(hook=...)]`.
- Client requests → `[Command]`; server → clients via SyncVar hooks or `[ClientRpc]`.
- Countdown: sync a start `NetworkTime.time` + duration.

---

## Edge cases to handle in the server pass

- **Player disconnects during countdown** → recompute "all ready"; if the minimum
  player count is no longer met, cancel the countdown and return to Waiting.
- **Player disconnects after the lock** → decide: continue starting the match, or
  abort. Default: continue if the minimum is still met.
- **Two players request the same empty slot in one tick** → server grants the first
  processed, sends an explicit rejection to the second so its UI can revert.
- **Swap request pending when the target leaves** → auto-cancel and notify.
- **Swap request pending when the countdown locks** → auto-cancel; no swaps after
  lock.
- **Late joiner while counting down** → default: reject joins once locked; before
  lock, joining cancels the countdown (since not everyone is ready).
- **Customization change arriving after the lock** → reject server-side; clients
  should also gray out the UI, but never rely on the client for this.
- **Duplicate/invalid customization id** → reject and reply with the current valid
  value so the client can resync.

---

## Testing checklist for the server pass

- [ ] Countdown reaches zero at the same wall-clock moment on all clients
- [ ] Un-readying cancels the countdown for everyone
- [ ] Two simultaneous claims on one slot resolve to exactly one winner
- [ ] Customization changes are visible on every client's view of that model
- [ ] Disconnect mid-countdown behaves per the rules above
- [ ] No lobby input is accepted after the lock state
- [ ] Offline transport still works for solo testing
