---
name: lobby-builder
description: >
  Work on the two lobby screens of the Backrooms escape FPS in Unity 6 — the main
  lobby (server browser: room list, create room, join by code, quick join) and the
  game lobby / waiting room (roster, ready-up, character select, host powers, start).
  Both live in the MainLobby scene as two pages of one UI Toolkit tree, and both are
  driven by the authoritative .NET server: the client sends Control requests and
  draws the RoomState bulletin. Use this whenever the user wants to create, change,
  or extend either lobby screen — or mentions "lobby", "로비", "대기방", "waiting
  room", "ready up", "준비", "roster", "명단", "character select", "캐릭터 선택",
  "kick", "강제 퇴장", "방장", "host", "room list", "방 목록", even without naming
  the game.
---

# Lobby Builder (Backrooms Escape FPS)

Two screens, one scene, one authority.

| Screen | Element | Owns |
|---|---|---|
| **Main lobby** | `#page-browser` | Everything *outside* a room — profile, connection state, room list (`GET /rooms`), create room, join by code, quick join, quit |
| **Game lobby** | `#page-room` | Everything *inside* a room — invite code, map, roster, ready, character select, kick / host transfer, start, leave, match result |

They never both show. `LobbyUIController.ShowRoomPage` is the only thing that
switches, and it switches on `NetSession.State` — not on a button click, because
there are four ways into a room (create, code, list, quick join) and a screen that
each of them has to remember to open is a screen one of them forgets.

## The authority — READ FIRST

**There is no client-side lobby authority.** There used to be (`LobbyManager` +
`ILobbyTransport` + `OfflineLobbyTransport`, with 17 `// NETCODE:` markers); the
server pass replaced all of it and those files are gone. Do not reintroduce them:
a second thing that decides who is ready is a second thing that can disagree with
`Room.cs`.

The seam is the wire, and it is already there:

```
client                                     server (NVserver/Modules/Realtime)
──────                                     ─────────────────────────────────
NetSession.SetReady / SetCharacter    ──►  Control(0x02) ──► RoomCommand ──► Room.*
NetSession.KickPlayer / TransferHost                        (tick boundary judges)
NetSession.RequestStart / ReturnToLobby
                                     ◄──  Event(0x82) RoomState, 2Hz, forever
GameLobbyView draws RoomState                              (phase · host · seeker
                                                            · roster with flags)
```

- **Requests, not commands.** The server re-checks who is host, what phase the room
  is in, and whether the transition is legal. A client cannot change room state.
- **Bulletins, not notifications.** `RoomState` repeats in full at 2Hz and is
  idempotent. Nothing in the lobby is a one-shot message — the session's outbound
  channel is `Bounded(32, DropOldest)`, so a one-shot "you were rejected" is a frame
  that can vanish. Read ADR 0003 before adding any notification.
- **No local copies.** `IsLocalReady` reads the roster; the character picker reads
  the roster. A local copy means a request the server refused stays true on screen,
  and that difference is invisible.

The living contract — every `Control` kind, every roster field, what the server
re-checks, and the checklist for widening the roster entry — is
`references/server-contract.md`. Why it is shaped that way, and what was
deliberately left out, is `NVserver/docs/game-lobby-plan.md`.

## What the server decides

| Rule | Where | Note |
|---|---|---|
| Ready | `Room.SetReady` | Waiting phase only. Cleared for everyone by `ResetToWaiting` |
| Start | `Room.Start` | Host + min players + **everyone else ready**. Host does not ready — pressing start is their ready. Bots are not counted. Static rooms skip the ready condition |
| Character | `Room.SetCharacter` | Range (`ProtocolInfo.LobbyCharacterCount`) + **unique in the room**. First processed wins |
| Kick | `Room.Kick` | Host session only — *not* `IsAuthorized`, which grants everyone in a static room. Removes from the roster **and** closes the socket with code 4003 |
| Host transfer | `Room.TransferHost` | Host session only. Never to a bot |
| Host succession | `Room.LowestRemainingSessionId` | When the host leaves. Server-chosen, humans only |

**Kick has a limit and it is not a bug:** there are no accounts, so kicking ends a
socket. Someone with the invite code can come back. The private-room code is the
real gate.

## The wire (protocol 4)

`RoomPlayerEntry` is 4 bytes + name: `playerId · flags · characterId · nameLength`.

- `flags` — `Ready` (bit 0), `Bot` (bit 1). **Bits 2-7 are free; teams and
  spectators go there.**
- `characterId` — index into `LobbyCharacterCatalog`. `0xFF` = unassigned. The
  server knows the *count* and nothing else; names, colours and headgear are the
  client's table.

Adding a field to that entry means adding it to `NetworkClient.Differs` in the same
edit. That comparison is the only thing that produces a redraw signal, so a missing
field is a value the server sends and the screen never shows.

## Files

| File | Role |
|---|---|
| `Assets/Resources/UI/MainLobby/MainLobby.uxml` | The shell: header, two pages, status line, popup/toast roots |
| `templates/GameLobbyPage.uxml` | The waiting room. Cloned into `#page-room` |
| `templates/*.uxml` | Popups and repeating rows. **A `VisualTreeAsset` is this project's prefab** — there are no UI prefabs |
| `Scripts/Lobby/UI/GameLobbyView.cs` | Draws the waiting room from `NetSession` + `NetworkClient`. Holds no state |
| `Scripts/Lobby/UI/CharacterPickerView.cs` | The character column |
| `Scripts/Lobby/CharacterPreview.cs` | One `LobbyMannequin` + camera → `RenderTexture` |
| `Scripts/Lobby/Controllers/GameLobbyController.cs` | Which page is up; what the buttons do |
| `Scripts/Lobby/Controllers/LobbyController.cs` | Main-lobby flow |
| `Scripts/Lobby/LobbyCharacterCatalog.cs` | The eight characters. **List order is the wire value** |
| `Scripts/Lobby/LobbyMannequin.cs` | The block figure, procedural idle |

## Rules that cost something to learn

- **Views hold no state.** Everything is read from `NetSession`/`NetworkClient` at
  draw time. A view with its own roster copy disagrees with the server invisibly.
- **Views do not call the session.** They expose `Action`s; the controller fills
  them. Otherwise "what the start button does" ends up in four files.
- **`display`, not `RemoveFromHierarchy`.** The game HUD tears role-exclusive panels
  out of the tree because a hidden element there is one style rule away from leaking
  the objective. There is nothing to hide in the lobby, and tearing out means
  rebuilding on the way back.
- **The tree does not survive a domain reload; components do.** `MainLobbyController`
  therefore asks `TreeIsLive` instead of keeping a `built` flag, and rebuilds whole.
  Anything holding a scene object (the character preview's camera and render
  texture) must be disposed in that rebuild or it accumulates one per reload.
- **Reasons, not just disabled buttons.** A start button that is off without a line
  saying why reads as broken. `GameLobbyView.StartNote` is that line.
- **Two-column reading order.** Ready state and full capacity are said by colour and
  row count first, numbers second.

## Don't

- Don't reintroduce a client-side lobby authority or a transport interface.
- Don't add a one-shot lobby message. Extend the bulletin. (ADR 0003: does missing
  it leave a wrong state behind? Then it must be a bulletin.)
- Don't put match or role logic here — the room hands off at `RoomPhase.Playing` and
  `SessionSceneRouter` opens the scene for the room's map.
- Don't copy a server value (capacity, min players) into a client constant. They
  arrive from `GET /rooms/{code}`.
- Don't reorder `LobbyCharacterCatalog` — the index is on the wire.
- Don't style the lobby as a clean menu. It is a room in the same building.
