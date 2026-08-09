# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repository is

One repo, two projects that ship together: a browser FPS whose **server holds authority over movement and combat**.

| Folder | What it is |
|---|---|
| `NVproject/` | Unity 6.3 (`6000.3.20f1`) WebGL client — URP, new Input System, IL2CPP. **Has its own `CLAUDE.md`** |
| `NVserver/` | .NET 10 server — Kestrel, Minimal API, raw `System.Net.WebSockets`, 30Hz tick loop, modular monolith |

`NVserver/docs/` is the server's specification and is **written in Korean**, as are most server code comments. `NVproject/CLAUDE.md` is in English. Match the language of the file you are editing.

| Question | Document |
|---|---|
| Scope, fixed parameters, how to run client+server together | `NVserver/docs/readme.md` |
| Reference rules, what libraries are banned, module design, wire protocol | `NVserver/docs/architecture.md` |
| Where a new file goes, naming, folder layout | `NVserver/docs/structure.md` |
| Rules discovered while implementing — the trap list | `NVserver/docs/conventions.md` |
| Everything Unity-side (block character, procedural animation, Backrooms level, match layer) | `NVproject/CLAUDE.md` |

## Commands

### Server — from `NVserver/`

```bash
dotnet run --project Api        # http://localhost:5202 (ws://localhost:5202/ws)
dotnet build                    # must finish with 0 warnings — TreatWarningsAsErrors is on
dotnet test                     # 394 tests: Architecture.Tests + Modules.Tests
dotnet test --filter "FullyQualifiedName~MovementTests"
dotnet test tests/Modules.Tests/Modules.Tests.csproj
```

`Api` is the only entry point; `Shared`, `Infrastructure`, `Modules/*` are class libraries. Build output goes to `artifacts/`, not `bin/obj` — that redirect in `Directory.Build.props` is **mandatory**, because a `Shared/obj/` directory makes Unity read the generated `AssemblyInfo.cs` and fail with duplicate definitions.

Package versions live only in `Directory.Packages.props`. A `Version=` attribute on a `PackageReference` fails restore with NU1008 and the error message does not say why.

### Client — from `NVproject/`

**There is no CLI build and no test suite.** All client work happens in the Unity Editor, driven via Unity MCP (`mcp__unity-mcp__*`). Read `NVproject/CLAUDE.md` and the `unity-mcp-ops` skill first — the MCP bridge has a long list of failure modes that look like code bugs.

Editor menus are the entry points:

| Menu | Does |
|---|---|
| **Tools ▸ NV ▸ Build and Launch 2 Clients** | Windows standalone ×2, the fast path to two players. Builds the **Build Settings scene list as-is**, so the players open on `MainLobby` exactly like the real product. Always Windows and always two, whatever the Build Manager's current selection says |
| **Tools ▸ NV ▸ Build (current selection)** / **Launch Clients (no build)** | Builds whatever platform + environment is currently selected; or just relaunches the last build |
| **Tools ▸ NV ▸ Environment ▸ Switch / Show Current** | Which server this build (and editor Play) points at. Cycles the assets in `Assets/Settings/Environments/`; `Show` logs the active one |
| **Tools ▸ NV ▸ Scene ▸ Create Main Lobby Scene** | Generates `Assets/Scenes/MainLobby.unity` — the product entry menu — and puts it at **Build Settings index 0**. Regenerating re-asserts that slot, so the entry scene cannot quietly drift back to `SampleScene` |
| **Tools ▸ NV ▸ Scene ▸ Create Game Lobby Scene** | Generates `Assets/Scenes/GameLobby.unity` — the 3D waiting room the server drives — and registers it. The room, the row of stands and the figures are built at runtime, so the scene holds one object. Without the registration `SessionSceneRouter` cannot load it, and a missing scene fails **silently** |
| **Tools ▸ NV ▸ Scene ▸ Create Multiplayer Test Scene** | Regenerates `Assets/Scenes/MultiplayerTest.unity` |
| **Tools ▸ NV ▸ Map ▸ Export Map Collision** | Writes the current scene's level to `NVserver/MapData/{map}.json` — the **server's** half |
| **Tools ▸ NV ▸ Map ▸ Bake Current Scene To Catalog** | Freezes the same level into `Assets/Settings/Maps/{map}.asset` and gives it a `MapCatalog` row — the **client's** half. Both are needed or the lobby refuses the map (see below) |
| **Tools ▸ Block Player ▸ Build Block Player** | Rebuilds the player rig from scratch |
| **Tools ▸ Backrooms ▸ Set Up Match** / **Create Game Config Asset** | Match layer scene object and `Assets/Settings/GameConfig.asset` |

**Which server the client talks to is an asset, not a constant.** `Assets/Settings/Environments/*.asset` (`NVEnvironment`) owns the host, the `wss`/`https` flag, whether the lobby's settings popup may override the address, and whether the match debug keys are allowed. A build bakes the selected one into `Assets/Resources/NVEnvironment.asset` (gitignored — it is a build output); the editor's Play mode reads the same selection out of `EditorPrefs` instead. `PlayerPrefs` keys are namespaced per environment (`nv.{id}.lobby.host`), because they survive a reinstall and a single key means a machine that once talked to `localhost` keeps pointing there after you install a build for a real server — a failure that shows up only as "server unreachable". A build whose environment names a remote host with `secure` off is **refused**: an HTTPS page cannot open `ws://`, and that failure never reproduces locally.

The client's skills (`fps-*`, `unity-mcp-ops`, `game-rules`, …) live in `NVproject/.claude/skills/`, so **a session started at the repo root does not load them** — work Unity tasks from `NVproject/` if you want them.

### Running both

1. `dotnet run --project Api` — no config edit needed, both maps are registered
2. Either path:
   - **Main lobby (product flow)** — generate both lobby scenes first (**Tools ▸ NV ▸ Scene ▸ Create Main Lobby Scene** and **Create Game Lobby Scene**), open `MainLobby.unity`, Play, **방 만들기** — which loads `GameLobby.unity`, the 3D waiting room where everyone stands in a row. Hand the code to the second client, the guest presses **READY**, then the host presses **START**. Ready is the gate and the host never presses it — the start button *is* the host's ready, so requiring both would make two controls mean one thing. **No room skips the ready gate**, including the static dev room `test` — that room is public, so quick join and the room list drop ordinary players into it, and an exemption there was an exemption in a real match (the symptom was "nobody pressed READY and it started"). `Room.IsAuthorized` still accepts anyone's Start in a static room, which is a different question — who may press it, not whether the room is ready. `SessionSceneRouter` loads the scene that matches the room's map. The active-room list, quick join and the online headcount all ride on `GET /rooms`, which returns **only rooms created as public** — the create-room popup defaults to private, so an untouched room is reachable by invite code alone. Visibility is fixed at creation and `POST /rooms` treats a missing `isPublic` as private, so exposure is always a choice.
   - **Dev dashboard (fast)** — open `Assets/Scenes/MultiplayerTest.unity`, Play, join room `test` (the pre-opened static room) in the panel.
3. Second client via **Build and Launch 2 Clients**

Scene and map are a pair: `MultiplayerTest` ↔ `test-room`, `SampleScene` ↔ `backrooms`. Coming through the lobby the router picks it for you; opening a scene by hand and joining the wrong room surfaces only as a map-hash warning. A match needs **two players** — one Seeker, one Runner. Full walkthrough with the session-state table is in `NVserver/docs/readme.md`.

## The seam between the two projects

Three things cross the folder boundary. Everything else is independent.

**1. `NVserver/Shared/` is a Unity local package.** `NVproject/Packages/manifest.json` has `"com.nv.shared": "file:../../NVserver/Shared"`, so Unity (IL2CPP, netstandard2.1) and the server (net10.0) compile *the same `.cs` files*. Client prediction only works if movement computes bit-identically on both sides, so this assembly is the strictest place in the repo:

- C# 9 (Unity's ceiling), no NuGet references, no `UnityEngine` references, `ImplicitUsings` off
- `System.Numerics.Vector3` — but only as a container. `Vector3.Normalize`/`Length`/`Dot`/`Distance` and `MathF.Sin`/`Cos`/`Tan` are banned; use `DeterministicMath`. SIMD/FMA paths and libm differences round differently, and the only symptom is character jitter after reconciliation.
- No `deltaTime` parameter on movement functions — `SimConstants.TickDelta` only. A parameter lets a caller pass real elapsed time, and re-application then diverges.
- Values that go into `Shared` are values the client knows: the WebGL build is decompilable. Sharing a number and delegating the *judgement* are different — the module re-checks.

After changing `Shared`, `dotnet build` passing is half the check; confirm the Unity Editor compiles it too. `Shared/*.meta` files are committed on purpose.

**2. The map's source of truth is the client.** The level is generated in code from a seed, so the server only knows the terrain as an exported box list in `NVserver/MapData/*.json`. Change the seed, grid, or wall thickness and re-run **Export Map Collision**, or you get a map-hash mismatch on connect — that hash is the only guard on this coupling.

The export also carries a **walkability grid** (`grid` block, cells base64), and the server needs it: objective placement and the hit-teleport both draw from it, so a map exported without one gets a match with no keys and no door. `MapCellFlags.FreeFloor` is not authored — `MapGridBuilder.MarkFreeFloor` computes it by asking the *server's* collision whether a player box fits, which is why the flag means "the server can stand a player here" rather than "the grid says walkable". The hash includes the grid **only when present**, so adding one to a map changes that map's hash and nothing else. Maps also carry a `meta` block (display name, description, recommended players) from schema 2 on; like `source` it is **outside the hash**, so adding it forces no re-export, and a file without one is normal — the server synthesizes the name from the map id.

**Exporting a map registers it with the server and with nothing else.** The lobby's create-room screen judges a map by merging *two* answers (`MapChoices.Merge`) — the server's `GET /maps` says what exists, and `Assets/Resources/MapCatalog.asset` says what this build can draw — so a map that is only exported comes back `MissingLocally` ("이 빌드에는 이 맵이 없다") and the room cannot be made. The catalog row is written by the bake, never by hand or by the export. A map with a generator (`backrooms-v2`) gets its row from **Map Generator**; a map that is still generated at runtime inside its own scene — `backrooms` in `SampleScene` — has no blueprint to bake and gets it from **Bake Current Scene To Catalog**, which freezes what is standing in the scene rather than regenerating it. Both bakes go through `MapExportPipeline.Plan()`, the same judgement the export uses, and the scene bake refuses any map `MapSceneTable` has no scene for: it writes no prefab, so such a row would be in the catalog and still unplayable.

**Registration is the file.** The server scans `Game:MapDirectory` (`../MapData`) and registers every `*.json` under **that file's own name**, so exporting a map is all it takes; it refuses to start if a filename and the `name` inside it disagree, which is what keeps map id, map name and export filename one string instead of three. `Game:Maps` still exists but is now an **alias table** (`default` → `backrooms`) — `default` cannot be deleted because unspecified-map requests and `Game:StaticRooms` speak through it. An unregistered map id is rejected rather than quietly opened as `default`. `GET /maps` serves that list (built once at startup, ETagged) and the lobby's create-room screen merges it with the client's own `MapCatalog` — what maps exist is the server's answer, whether this build can draw one is local, and a WebGL build ships its assets baked in. `Game:StaticRooms` pre-opens fixed rooms (`test` → `test-room`) so the two-client dev loop still has a room id to connect to — but it is **development-only**: it lives in `appsettings.Development.json`, never the base file (a base-file entry is inherited by every environment, and an override file cannot delete a key), and the server refuses to start with static rooms configured outside Development, the same rule as bots.

**3. The wire protocol lives in `Shared/Contracts/Messages`.** Binary frames, opcode first, little-endian: `0x01` Input (C→S, last 3 ticks resent), `0x02` Control (C→S: start, report match end, return to lobby, set ready, set character, kick, transfer host), `0x81` Snapshot (full, every tick, only while the room is `Playing`), `0x82` Event, `0x83` Welcome. `ProtocolInfo.Version` is **4** (4 is where the waiting room became server-authoritative); it is checked *before* the WebSocket upgrade and mismatch is rejected with 426 — the version, room code, host token and display name travel in the query string because browsers cannot set handshake headers. Client sends input, never position.

`Event` carries an `EventKind` byte after the opcode and there are three: `RoomState` (2Hz — phase, host, seeker, and a roster whose every row carries ready/bot flags and a character index), `MatchState` (2Hz — match phase, clock, keys inserted, escapes, per-player ammo, hits and carried keys), `ObjectiveState` (on change + every 5s — altar, door, keys, devices). **`MatchState` and `ObjectiveState` are encoded per session, not per room**, and the filter runs **both ways**: a Seeker's copy has key progress blanked and the door block omitted entirely, and a Runner's copy has the ammo count blanked — the gunshot is how this game tells a Runner a round was spent, and a number would hand that over for free. The filter lives in the codec so a caller cannot bypass it. **`RoomState` is not one of them — it is identical for everyone in the room.** It was per-recipient in static rooms for a while, each copy naming its own reader as host, because that room has no host-token path and the client offers the Start button by comparing `RoomState.HostPlayerId` to its own id. The catch is that static rooms are public, so the lobby list and quick join drop ordinary players into one — and two of them then sat in a room where each was the host, with kick, host transfer and the ready gate all meaning nothing. A static room now simply appoints the first human to join, which succession already handles; `Room.IsAuthorized` still accepts everyone's Start there, because that was the dev convenience and it is a different question from who the roster draws a crown on. Adding a kind means adding a `case` to the client's `DispatchEvent` — parsing every `Event` as one kind throws on the others.

A fourth kind, `FireEvent`, is **the one notification** — 17 bytes sent on the tick a shot is fired and never repeated, because a shot is an event rather than a state. Missing one costs a tracer and nothing else (the hit, the death and the ammo all arrive as bulletins). The test for adding another is in ADR 0003: **does missing it leave a wrong state behind?** If yes, it has to be a bulletin.

The other three are **bulletins, not notifications**: they go out in full, forever, and are idempotent. A one-shot "the match started" would eventually be the frame that the session's `Bounded(32, DropOldest)` channel drops, and that client would sit in the lobby screen for good. `Control` is a *request* — the room re-checks who is host and whether the transition is legal at the tick boundary.

**That is also why nothing the waiting room does is a notification.** Ready, character choice, kick and host transfer all go out as `Control` and come back inside the `RoomState` bulletin; a refused request is simply a request the next bulletin does not reflect. The one thing that cannot ride the bulletin is a kick, because the point is that the roster no longer contains that client — so it travels as a WebSocket close code (**4003**), and that code is the only thing separating "the host ejected you" from "your connection dropped". Without it the client's auto-retry walks the ejected player straight back into the room. There are no accounts, so a kick ends a socket and nothing more.

**What is per-tick and what is per-bulletin is a design decision, not an accident.** `EntityFlags` (8 bits, all used) carries what a remote body's *appearance* must follow immediately — bleeding, escaped, downed, frozen, seeker. Counts that a HUD can lag half a second on — keys inserted, escapes, hits — ride the 2Hz bulletin. Putting bleeding on the bulletin makes blood trails start late; putting key counts in the flags wastes bits there is no room for.

**Rooms are made, not stumbled into.** `POST /rooms` returns an invite code and a host token; connecting with an unknown code is a 404. `GET /rooms/{code}?v=2` is the pre-flight that separates the failure cases (400/404/409/426/429/503) *before* the upgrade, because a browser turns every handshake rejection into close code `1006` and nothing else.

**There is no cap on concurrent rooms.** The cap was the guard from when any query string could conjure a room; now that rooms are made explicitly, two things replace it — a room is reclaimed the moment its last participant leaves (a room that has never been joined gets 30s first, because `POST /rooms` and the WebSocket connect are two steps and the room has zero participants between them), and the request *rate* is limited (`RateLimit:CreatePerMinute` 10, `RateLimit:CodeAttemptsPerMinute` 60). The code-attempt budget covers `GET /rooms/{code}` **and** `/ws` from one bucket: limiting only the lookup leaves guessing via the upgrade, which answers differently for a code that exists. Rate limits partition by remote IP, so behind a reverse proxy everyone shares one bucket unless forwarded headers are configured with a trusted-proxy list.

Codes are 6 characters from a 31-symbol alphabet (`i l o 0 1` removed — a code is read aloud, and one wrong character is indistinguishable from a dead room). **The length is not fixed:** it grows with the live room count so the load factor stays under 1e-5 (6 chars up to ~8,900 rooms, 7 up to ~275,000). Clients validate the length as a *range*; pinning it to 6 would make a client reject a code the server legitimately grew. Bytes are folded into the alphabet by rejection sampling, since 256 is not a multiple of 31 and the naive modulo would favour the first 8 symbols.

## Server architecture in one screen

Modular monolith. Only two rules are structural, and both are compiler- or test-enforced: **modules never reference each other**, and **everything outside a module's `Contracts/` is `internal`**. `tests/Architecture.Tests` verifies this by reading `ProjectReference` declarations (reflection under-reports, so it would pass vacuously).

```
Shared          → nothing
Infrastructure  → Shared
Modules/*       → Shared, Infrastructure
Api             → everything
```

`Api` has **no controllers** — each module registers its own endpoints via `{Module}Module.MapXxx()`. `Modules/` and `tests/` are grouping folders, not projects.

Of the four modules the docs plan, **only `Realtime` exists today**; Identity, Matchmaking and Leaderboard are unbuilt, and there is no database yet — all state is in memory. `Realtime` deliberately has no `DbContext`: if EF Core starts appearing there, the design has drifted.

Threading model: Kestrel threads drain into `InboundQueue`/`CommandQueue`, `GameLoopService` ticks at 30Hz and never touches a socket or DB outside those queues, and outbound snapshots go through a bounded (32, DropOldest) channel. HTTP threads must not mutate room state directly — the tick loop owns it, so mutations go through `IRoomCommand` and reads return immutable snapshots.

Constants have exactly two homes: `Shared/Simulation/SimConstants.cs` when the client must compute with the same number, `Modules/{Module}/{Module}Constants.cs` when the server decides alone. Never re-type a value that can be derived from another.

## Before writing server code

`NVserver/docs/architecture.md` has a **기본값 대체표** (defaults-replacement table) listing the ordinary .NET/Unity choices that are wrong here — repository interfaces, layered folders, `EnsureCreated()`, delta compression, `await` inside the tick loop, cross-module JOINs, and a dozen more. Check it before reaching for a familiar pattern. `structure.md` has the 8-question table that decides where a new file goes; question 1 ("does the client run this same computation?") wins over everything else.

**Ask instead of implementing** when you would need to break a documented prohibition, add a NuGet package, add a module, add an interface with only one implementation, add a synchronous call between modules, or change a fixed parameter (30Hz, **5 players per room** — it was 8 until the lobby work, and the capacity *is* the Runner count, so it is a balance number as much as a buffer size — 1/64m quantization, 100ms interpolation, 200ms lag-compensation cap).

Record any rule you settle or problem that cost more than 30 minutes in `NVserver/docs/conventions.md`, symptom → cause → fix. That file is the accumulated trap list and is worth reading in full before non-trivial network or simulation work.

## Git

Branches are `feature/{area}/{topic}` (`feature/client-main`, `feature/server/init`); `main` is the base for PRs. Commit subjects are conventional-commit prefixed (`feat(realtime):`, `docs:`, `refactor:`) with Korean or English bodies.
