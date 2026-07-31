---
name: backrooms-map-generator
description: >
  Generate a procedural Backrooms-style level for a Unity 6 3D FPS. Builds a
  35x35 grid map made of rooms and corridors across TWO floors connected by a
  fixed stairwell, with a mono-yellow liminal-space mood (damp wallpaper, worn
  carpet, buzzing fluorescent lights). The layout reshuffles on every generation
  while designated anchor cells (spawn, exit, stairwell) stay fixed and aligned
  between floors. Use this whenever the user wants to build, generate,
  regenerate, or lay out a Backrooms / liminal / yellow-maze map or level in
  Unity — or mentions "backrooms", "liminal", "procedural map/level", "2-floor
  map", "map generator", or describes a maze of rooms and hallways with stairs,
  even if they don't say "backrooms" explicitly.
---

# Backrooms Map Generator

Build a Backrooms-style level inside a Unity 6 project and drive Unity through
the **Unity MCP bridge** (Edit > Project Settings > AI > Unity MCP). The skill
produces a runtime/editor generator that stamps modular prefabs onto a grid, so
a designer can regenerate the level with one click and always get a playable,
connected map with a consistent liminal mood.

## The contract (what "done" looks like)

A generated map MUST satisfy all of these — treat them as acceptance criteria:

1. **Footprint**: a square `gridSize x gridSize` grid, default `35 x 35` cells.
   Each cell is `cellSize` units (default `3.0`), so the default level is a
   ~105 m square. `gridSize` and `cellSize` are parameters.
2. **Two floors**: `floors = 2`. Floor 1 sits at `y = 0`, floor 2 at
   `y = floorHeight` (default `3.2`). Both floors share the same grid footprint.
3. **Rooms + corridors**: the walkable space is a mix of open rooms connected by
   narrower corridors. No isolated pockets — every walkable cell must be
   reachable from the spawn (enforced by a flood-fill pass).
4. **Fixed anchors that never change**: the spawn room, the exit room, and the
   **stairwell** are stamped at fixed grid coordinates on every generation. The
   stairwell occupies the SAME cells on floor 1 and floor 2 so the stairs line
   up vertically. Everything else reshuffles per seed.
5. **Vertical traversal**: a stair module connects floor 1 to floor 2 through
   the stairwell. The ceiling of floor 1 and the floor of floor 2 are cut open
   over the stairwell footprint so the player can walk up.
6. **Backrooms mood**: mono-yellow damp walls, worn carpet, a dropped ceiling
   with recessed fluorescent lights, a constant low hum, mild fog, and slightly
   desaturated warm lighting. Full values live in
   `references/aesthetic-spec.md` — read it before touching materials/lighting.
7. **Playable**: player spawn placed, colliders on walls/floors/stairs, and a
   baked NavMesh (for entities/AI) covering both floors including the stairs.

## Determinism rule

Same `seed` -> identical map. Different `seed` -> different rooms and corridors,
but the fixed anchors (#4) are byte-for-byte identical regardless of seed. Use a
single seeded `System.Random` for ALL procedural choices — never `UnityEngine.Random`
mixed in, or reproducibility breaks. Expose `randomizeSeed` for designers who
want a fresh layout each run.

## Workflow

Work in this order. Do not start placing geometry before the grid is solved and
validated in memory — placing first and fixing later floods the scene with
orphaned objects.

### 1. Confirm the environment
- Verify the Unity MCP bridge shows **Running** (green). If not, tell the user to
  start it in Edit > Project Settings > AI > Unity MCP.
- Confirm a modular **prefab kit** exists (floor tile, ceiling tile, wall
  segment, pillar, doorway opening, fluorescent light, stair module, spawn
  marker, exit marker). If any are missing, list exactly what's missing and
  offer to generate placeholder prefabs (primitive cubes/quads with the correct
  dimensions and the aesthetic materials) so the generator runs end-to-end today
  and the user swaps in art later.

### 2. Solve the grid (in memory, no GameObjects yet)
Use the algorithm in `scripts/BackroomsMapGenerator.cs` (`SolveGrid`):
1. Init both floors as solid `Wall`.
2. **Stamp fixed anchors** (spawn, exit, stairwell) and mark them protected so
   procedural passes never overwrite them. Stairwell is stamped identically on
   both floors.
3. **Carve procedural rooms**: attempt `roomAttempts` random rectangles within
   `[roomMin, roomMax]`, rejecting ones that collide with already-placed rooms
   (keep a little separation so rooms read as distinct spaces).
4. **Connect** every room center to the graph with L-shaped corridors, then add
   a few extra loops (`loopChance`) so the map isn't a pure tree — dead ends are
   good for Backrooms, but some loops keep it from feeling linear.
5. **Wire the stairwell** into the nearest corridor on BOTH floors.
6. **Validate connectivity**: flood-fill from the spawn; if any walkable cell is
   unreachable, carve the shortest connector and repeat until clean.

### 3. Build geometry
Call `BuildGeometry`. For every walkable cell place a floor tile and (unless it's
under the stairwell opening) a ceiling tile; for every walkable/solid boundary
place a wall segment on that edge; scatter fluorescent lights on a regular
cadence (`lightSpacing`) so lighting reads as an office grid, not random. Place
the stair module across the stairwell and cut the ceiling/floor holes. Parent
everything under a single `__BackroomsMap` root so regeneration can clear it in
one destroy.

### 4. Apply the mood
Assign the materials and lighting described in `references/aesthetic-spec.md`.
The mood is 60% of why this reads as "Backrooms" rather than "gray dungeon" —
do not skip it. Add the ambient hum AudioSource and the fog/lighting settings.

### 5. Finalize for play
Place the player spawn at the spawn-room center on floor 1 and the exit marker
on floor 2. Add colliders. Bake the NavMesh across both floors + stairs (editor
step — trigger via MCP menu execution or tell the user the exact menu path if
the bake can't be triggered headless).

### 6. Report
Tell the user the seed used, room count per floor, and confirm each acceptance
criterion (#1–#7) is met. If you generated placeholder prefabs, say so and list
what to replace with real art.

## Regeneration

To reshuffle: change `seed` (or enable `randomizeSeed`) and re-run `Generate`.
The generator destroys the previous `__BackroomsMap` root first, so the scene
never accumulates duplicates. Fixed anchors stay put; only rooms/corridors move.

## Parameters

| Parameter        | Default | Meaning |
|------------------|---------|---------|
| `gridSize`       | 35      | Cells per side (square). |
| `cellSize`       | 3.0     | World units per cell. |
| `floors`         | 2       | Number of stacked floors. |
| `floorHeight`    | 3.2     | Vertical gap between floors. |
| `seed`           | 0       | RNG seed; same seed = same map. |
| `randomizeSeed`  | true    | Pick a fresh seed each Generate. |
| `roomAttempts`   | 22      | Random room placements tried per floor. |
| `roomMin`/`roomMax` | 3 / 8 | Room size range in cells. |
| `corridorWidth`  | 1       | Corridor thickness in cells. |
| `loopChance`     | 0.15    | Chance to add an extra loop edge. |
| `lightSpacing`   | 3       | Place a fluorescent light every N cells. |

Fixed-anchor rects (`spawnRoom`, `exitRoom`, `stairwell`) are hand-authored grid
rectangles — see the top of the C# file. Editing them is how a designer changes
the "parts that never move."

## Files in this skill

- `scripts/BackroomsMapGenerator.cs` — the generator: grid solver, geometry
  builder, stairwell + hole cutting, connectivity validation, regeneration.
  This is a runnable scaffold; wire it to the prefab kit via the inspector.
- `references/aesthetic-spec.md` — exact materials, colors, lighting, fog, and
  audio values for the liminal mood. Read it before styling anything.

## Common failure modes to avoid

- **Placing geometry before validating connectivity** — you get pretty maps the
  player can't finish. Solve + flood-fill first.
- **Mixing `UnityEngine.Random` into a seeded run** — breaks reproducibility.
- **Forgetting the stairwell ceiling/floor holes** — the stairs exist but the
  player hits a ceiling. Always cut both holes over the stairwell footprint.
- **Skipping the mood pass** — geometry alone looks like a generic maze, not
  Backrooms. The aesthetic spec is not optional decoration.
- **Not clearing the old root on regenerate** — scene fills with duplicates.
