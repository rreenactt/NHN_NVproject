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

Two screens, and they are different *kinds* of thing — which is why they are
different scenes.

| Screen | Scene | Owns |
|---|---|---|
| **Main lobby** | `MainLobby.unity` | Everything *outside* a room — profile, connection state, room list (`GET /rooms`), create room, join by code, quick join, quit. Pure UI Toolkit |
| **Game lobby** | `GameLobby.unity` | Everything *inside* a room — the 3D staging space with a figure standing on each stand, plus invite code, roster, ready, character select, kick / host transfer, start, leave, result |
| *(prototype)* | `Lobby.unity` | The **offline** lobby it was built from — `LobbyManager`, fake players, countdown, slot swapping. Still present on purpose; delete it once the wired room is confirmed end to end |

**The waiting room is a room, not a page.** An earlier pass rebuilt it as a
full-screen UI page inside `MainLobby` and that was wrong: the 3D lineup *is* the
screen, and the job was to keep that composition and improve the UI on top of it.

`SessionSceneRouter` owns every transition — `InLobby`/`Ended` → `GameLobby`,
`Playing` → the room's map scene, `Idle`/`Failed` → `MainLobby`. **No button loads a
scene**, because there are four ways into a room and a transition each of them has to
remember is one that one of them forgets.

Both product scenes are **generated** (**Tools ▸ NV ▸ Scene ▸ Create Main Lobby
Scene** / **Create Game Lobby Scene**) and register themselves in Build Settings. The
router finds scenes by name, and an unregistered scene fails to load with no log line.

## The authority — READ FIRST

**The wired room has no client-side authority.** `LobbyManager` +
`ILobbyTransport` + `OfflineLobbyTransport` still exist, but only inside the offline
prototype scene — the server-driven room does not touch them and must not: a second
thing that decides who is ready is a second thing that can disagree with `Room.cs`.
When the prototype is deleted, those three go with it.

**What the two share is the view, and only the view.** `LobbyRoom`, `LobbySlot` and
`LobbyMannequin` build the space, the stands and the figures for both scenes. They are
presentation, so the server changed nothing about them — what changed is who decides
who stands where.

The seam is the wire, and it is already there:

```
client                                     server (NVserver/Modules/Realtime)
──────                                     ─────────────────────────────────
NetSession.SetReady / SetCharacter    ──►  Control(0x02) ──► RoomCommand ──► Room.*
NetSession.KickPlayer / TransferHost                        (tick boundary judges)
NetSession.RequestStart / ReturnToLobby
                                     ◄──  Event(0x82) RoomState, 2Hz, forever
GameLobbyHud + the row of stands                           (phase · host · seeker
  both draw the same RoomState                              · roster with flags)
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
| `Scripts/Lobby/GameLobby/GameLobbyBootstrap.cs` | The waiting-room scene's only object. Builds the room and the row from the server's **capacity**, binds stands to the roster |
| `Scripts/Lobby/GameLobby/GameLobbyHud.cs` | Draws the HUD from `NetSession` + `NetworkClient`. Holds no state |
| `Scripts/Lobby/GameLobby/GameLobbyPicker.cs` | Clicking a stand — a host gesture, not a slot move |
| `Scripts/Lobby/Models/RoomMember.cs` | One roster line projected (entry + host byte + own id). Built fresh, never stored |
| `Resources/UI/GameLobbyHUD.uxml` + `game-lobby.uss` | The HUD, on top of the prototype's `lobby.uss` |
| `Scripts/Lobby/LobbyRoom.cs` · `LobbySlot.cs` · `LobbyMannequin.cs` | **Shared with the prototype.** Space, stands, figures |
| `Scripts/Lobby/LobbyCharacterCatalog.cs` | The eight characters. **List order is the wire value** |
| `Resources/UI/MainLobby/MainLobby.uxml` + `templates/*.uxml` | The menu. **A `VisualTreeAsset` is this project's prefab** — there are no UI prefabs |
| `Scripts/Lobby/Controllers/LobbyController.cs` | Main-lobby flow |
| `Assets/Editor/Scene/GameLobbySetup.cs` | Generates the waiting-room scene and registers it |

## Rules that cost something to learn

- **Views hold no state.** Everything is read from `NetSession`/`NetworkClient` at
  draw time. A view with its own roster copy disagrees with the server invisibly.
- **Views do not call the session.** They expose `Action`s; the controller fills
  them. Otherwise "what the start button does" ends up in four files.
- **The tree does not survive a domain reload; components do.** `MainLobbyController`
  and `GameLobbyBootstrap` therefore ask whether the tree is *live* instead of keeping
  a `built` flag — a bool survives the reload and describes elements that are all null,
  so the screen stays blank and throws once a frame. Rebuild whole.
- **The stand number is the `PlayerId`.** The server reserves slots by it and picks the
  spawn from it. That is what makes the row and the match agree on who is who, and it
  is why there is no slot swapping — moving a figure would move a spawn.
- **Reasons, not just disabled buttons.** A start button that is off without a line
  saying why reads as broken. `GameLobbyHud.Hint` is that line.
- **Buttons eat their own clicks.** In a screen where clicking the 3D room is a
  gesture, a click that landed on a button must not also hit the figure behind it —
  `GameLobbyHud.PointerOverUi` is that guard, carried over from the prototype.
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
