using System;
using System.Collections.Generic;
using NV.Client.Map;
using NV.Shared.Collision;
using UnityEngine;

namespace NV.Client.EditorTools.Generators
{
    /// <summary>
    /// The two-floor Backrooms level, as data.
    ///
    /// A port of <c>BackroomsMapGenerator</c>'s grid solver and geometry pass — and a port, not a
    /// rewrite. **Every draw from the seeded random has to happen in the same order it does there**,
    /// because a single extra or missing draw produces a completely different but perfectly
    /// plausible level, and the only symptom is that the shipped <c>backrooms.json</c> stops
    /// matching. <c>MapGeneratorParityTests</c> is what holds this honest.
    ///
    /// What did **not** come across: lights, fog, the ambient hum and the flicker. Those are
    /// scene-global or per-frame and belong to <c>BackroomsAmbience</c> at runtime. The light
    /// *panels* did come across — they are boxes, they are part of what the level looks like, and
    /// they carry no collider so they never reach the server.
    ///
    /// The layout is solved completely before any geometry is emitted. Placing geometry first and
    /// fixing connectivity afterwards leaves orphaned tiles behind and produces maps the player
    /// cannot finish.
    /// </summary>
    public sealed class BackroomsGenerator : IMapGenerator
    {
        public string DisplayName => "Backrooms";

        public string DefaultMapName => "backrooms";

        public Type SettingsType => typeof(BackroomsSettings);

        public MapBlueprint Generate(MapGeneratorSettings settings)
        {
            var backrooms = settings as BackroomsSettings;

            if (backrooms == null)
            {
                throw new ArgumentException(
                    $"BackroomsGenerator 는 BackroomsSettings 를 읽는다. {settings?.GetType().Name ?? "null"} 을 받았다.",
                    nameof(settings));
            }

            // State lives on a fresh solver, not on this object. The registry keeps one generator
            // instance forever, and a second Generate that saw the first one's grid would produce a
            // level nobody can reproduce.
            return new Solver(backrooms).Run();
        }

        /// <summary>One run. Holds the grid while it is being solved and then emits the blueprint.</summary>
        private sealed class Solver
        {
            private enum Cell : byte { Solid = 0, Room = 1, Corridor = 2, Anchor = 3 }

            private readonly BackroomsSettings _s;
            private readonly MapBlueprint _blueprint = new MapBlueprint();

            private Cell[][,] _cell;
            private bool[][,] _protected;
            private List<RectInt>[] _rooms;

            private float _originX, _originZ;

            internal Solver(BackroomsSettings settings)
            {
                _s = settings;
            }

            internal MapBlueprint Run()
            {
                _blueprint.MapName = _s.mapName;
                _blueprint.Blocker = _s.DescribeBlocker();
                _blueprint.UsedSeed = _s.ResolveSeed();

                _blueprint.Palette[MapSurface.Wall] = _s.wallColor;
                _blueprint.Palette[MapSurface.Trim] = _s.trimColor;
                _blueprint.Palette[MapSurface.Floor] = _s.carpetColor;
                _blueprint.Palette[MapSurface.Ceiling] = _s.ceilingColor;
                _blueprint.Palette[MapSurface.LightPanel] = _s.lightColor;

                _originX = -_s.gridSize * _s.cellSize * 0.5f;
                _originZ = -_s.gridSize * _s.cellSize * 0.5f;

                SolveGrid(new System.Random(_blueprint.UsedSeed));

                BuildGeometry();
                BuildLightPanels();

                _blueprint.Grid = BuildGrid();
                _blueprint.SpawnCentre = RectCentre(0, _s.spawnRoom);
                CollectSpawns();

                return _blueprint;
            }

            // ============================================================ 1. grid

            private void SolveGrid(System.Random random)
            {
                var floorCount = _s.FloorCount;

                _cell = new Cell[floorCount][,];
                _protected = new bool[floorCount][,];

                // Sized by the floor count rather than fixed at two. The original array was
                // `new List<RectInt>[2]`, which throws on any level with three storeys — a trap
                // rather than a decision, and free to remove while moving the code.
                _rooms = new List<RectInt>[floorCount];

                for (var f = 0; f < floorCount; f++)
                {
                    _cell[f] = new Cell[_s.gridSize, _s.gridSize];
                    _protected[f] = new bool[_s.gridSize, _s.gridSize];
                    _rooms[f] = new List<RectInt>();
                }

                StampAnchors();

                for (var f = 0; f < floorCount; f++)
                {
                    CarveRooms(f, random);
                    ConnectRooms(f, random);
                    WireStairwell(f);
                }

                EnforceConnectivity();
            }

            /// <summary>
            /// The parts that never move, stamped before anything procedural and marked protected so
            /// a random room can never eat the spawn or seal the stairs. The stairwell goes down on
            /// every floor at identical coordinates — that is what makes the flights line up.
            /// </summary>
            private void StampAnchors()
            {
                Stamp(0, _s.spawnRoom);
                Stamp(Mathf.Min(1, _s.floors - 1), _s.exitRoom);

                for (var f = 0; f < _s.floors; f++) Stamp(f, _s.stairwell);

                // On the lower floor the stairwell's last row is the underside of the upper landing.
                // Left walkable it becomes a three-cell pocket with a lid on it: floor and headroom,
                // but the flight rises to 3.2 m in front of it and the stairwell wall closes the
                // rest, so nothing can ever reach it. Making it solid seals it and lets the wall
                // pass build a face.
                var underLanding = _s.stairwell.yMax - 1;
                for (var x = _s.stairwell.x; x < _s.stairwell.xMax; x++)
                    if (InGrid(x, underLanding)) _cell[0][x, underLanding] = Cell.Solid;
            }

            private void Stamp(int floor, RectInt rect)
            {
                for (var x = rect.x; x < rect.xMax; x++)
                for (var z = rect.y; z < rect.yMax; z++)
                {
                    if (!InGrid(x, z)) continue;
                    _cell[floor][x, z] = Cell.Anchor;
                    _protected[floor][x, z] = true;
                }

                _rooms[floor].Add(rect);
            }

            private bool InGrid(int x, int z) =>
                x >= 1 && z >= 1 && x < _s.gridSize - 1 && z < _s.gridSize - 1;

            private void CarveRooms(int floor, System.Random random)
            {
                for (var attempt = 0; attempt < _s.roomAttempts; attempt++)
                {
                    // Four draws per attempt, always, even when the candidate is rejected. Moving
                    // any of them inside the rejection test changes every level from here on.
                    var w = random.Next(_s.roomMin, _s.roomMax + 1);
                    var h = random.Next(_s.roomMin, _s.roomMax + 1);
                    var x = random.Next(1, Mathf.Max(2, _s.gridSize - w - 1));
                    var z = random.Next(1, Mathf.Max(2, _s.gridSize - h - 1));

                    var candidate = new RectInt(x, z, w, h);

                    // Keep a cell of separation so rooms read as distinct spaces rather than one blob.
                    var padded = new RectInt(x - 1, z - 1, w + 2, h + 2);
                    var collides = false;

                    foreach (var existing in _rooms[floor])
                        if (padded.Overlaps(existing)) { collides = true; break; }

                    if (collides) continue;

                    for (var cx = candidate.x; cx < candidate.xMax; cx++)
                    for (var cz = candidate.y; cz < candidate.yMax; cz++)
                        if (InGrid(cx, cz) && !_protected[floor][cx, cz])
                            _cell[floor][cx, cz] = Cell.Room;

                    _rooms[floor].Add(candidate);
                }
            }

            /// <summary>
            /// Chains every room into one graph with L-shaped corridors, then adds a few extra
            /// edges. The chain is what guarantees a connected floor; the extras are what stop it
            /// being a tree, because a level with exactly one route between any two points reads as
            /// a maze puzzle rather than as being lost.
            /// </summary>
            private void ConnectRooms(int floor, System.Random random)
            {
                var rooms = _rooms[floor];
                if (rooms.Count < 2) return;

                for (var i = 1; i < rooms.Count; i++)
                    CarveCorridor(floor, RoomCell(rooms[i - 1]), RoomCell(rooms[i]), random);

                for (var i = 0; i < rooms.Count; i++)
                {
                    if (random.NextDouble() >= _s.loopChance) continue;

                    var other = random.Next(rooms.Count);
                    if (other == i) continue;

                    CarveCorridor(floor, RoomCell(rooms[i]), RoomCell(rooms[other]), random);
                }
            }

            private static Vector2Int RoomCell(RectInt rect) =>
                new Vector2Int(rect.x + rect.width / 2, rect.y + rect.height / 2);

            /// <summary>
            /// A single L: one leg along X, one along Z, meeting at the elbow. Which leg comes first
            /// is a coin flip so corridors do not all turn the same way.
            /// </summary>
            private void CarveCorridor(int floor, Vector2Int from, Vector2Int to, System.Random random)
            {
                if (random.Next(2) == 0)
                {
                    CarveLine(floor, from.x, to.x, from.y, true);    // along X at the start row
                    CarveLine(floor, from.y, to.y, to.x, false);     // then along Z at the end column
                }
                else
                {
                    CarveLine(floor, from.y, to.y, from.x, false);   // along Z at the start column
                    CarveLine(floor, from.x, to.x, to.y, true);      // then along X at the end row
                }
            }

            private void CarveLine(int floor, int a, int b, int fixedCoord, bool alongX)
            {
                var step = a <= b ? 1 : -1;

                for (var v = a; v != b + step; v += step)
                for (var w = 0; w < _s.corridorWidth; w++)
                {
                    var x = alongX ? v : fixedCoord + w;
                    var z = alongX ? fixedCoord + w : v;

                    if (!InGrid(x, z) || _protected[floor][x, z]) continue;
                    if (_cell[floor][x, z] == Cell.Solid) _cell[floor][x, z] = Cell.Corridor;
                }
            }

            /// <summary>
            /// Runs a corridor from the stairwell to the nearest carved space on this floor.
            ///
            /// **Which cell it joins matters.** The flight fills the stairwell as a rising wedge, so
            /// on the lower floor the only place you can step onto it is the bottom row, and on the
            /// upper floor the only place you can step off is the landing row. Joining the corridor
            /// to the stairwell's centre — the obvious thing — puts the doorway halfway up a 1.6 m
            /// wall of steps, and the upper floor becomes unreachable while still looking connected
            /// on the grid.
            /// </summary>
            private void WireStairwell(int floor)
            {
                var entryZ = floor == 0 ? _s.stairwell.y : _s.stairwell.yMax - 1;
                var stairCell = new Vector2Int(_s.stairwell.x + _s.stairwell.width / 2, entryZ);
                var nearest = stairCell;
                var best = int.MaxValue;

                for (var x = 1; x < _s.gridSize - 1; x++)
                for (var z = 1; z < _s.gridSize - 1; z++)
                {
                    if (_cell[floor][x, z] == Cell.Solid) continue;
                    if (_protected[floor][x, z]) continue;

                    var distance = Mathf.Abs(x - stairCell.x) + Mathf.Abs(z - stairCell.y);
                    if (distance < best) { best = distance; nearest = new Vector2Int(x, z); }
                }

                if (best == int.MaxValue) return;

                CarveLine(floor, stairCell.x, nearest.x, stairCell.y, true);
                CarveLine(floor, stairCell.y, nearest.y, nearest.x, false);
            }

            /// <summary>
            /// Flood-fills from the spawn across BOTH floors — the stairwell is a vertical edge —
            /// and carves a connector to anything stranded, repeating until the whole level is
            /// reachable. Without this the generator happily produces pretty maps with rooms you
            /// cannot get to.
            /// </summary>
            private void EnforceConnectivity()
            {
                for (var pass = 0; pass < 12; pass++)
                {
                    var seen = FloodFromSpawn();

                    var stranded = FindStranded(seen);
                    if (stranded.x < 0) return;                 // everything reachable

                    var anchor = NearestReachable(seen, stranded);
                    if (anchor.x < 0) return;

                    CarveLine(stranded.z, stranded.x, anchor.x, stranded.y, true);
                    CarveLine(stranded.z, stranded.y, anchor.y, anchor.x, false);
                }
            }

            private bool[][,] FloodFromSpawn()
            {
                var seen = new bool[_s.floors][,];
                for (var f = 0; f < _s.floors; f++) seen[f] = new bool[_s.gridSize, _s.gridSize];

                var queue = new Queue<Vector3Int>();
                var start = RoomCell(_s.spawnRoom);

                seen[0][start.x, start.y] = true;
                queue.Enqueue(new Vector3Int(start.x, start.y, 0));

                var steps = new[]
                {
                    new Vector2Int(1, 0), new Vector2Int(-1, 0),
                    new Vector2Int(0, 1), new Vector2Int(0, -1),
                };

                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();

                    foreach (var s in steps)
                    {
                        int nx = c.x + s.x, nz = c.y + s.y;
                        if (nx < 0 || nz < 0 || nx >= _s.gridSize || nz >= _s.gridSize) continue;
                        if (seen[c.z][nx, nz] || _cell[c.z][nx, nz] == Cell.Solid) continue;

                        seen[c.z][nx, nz] = true;
                        queue.Enqueue(new Vector3Int(nx, nz, c.z));
                    }

                    // The stairwell is the only way between floors, so it is the only vertical edge.
                    if (!InRect(_s.stairwell, c.x, c.y)) continue;

                    for (var f = 0; f < _s.floors; f++)
                    {
                        if (f == c.z || seen[f][c.x, c.y] || _cell[f][c.x, c.y] == Cell.Solid) continue;

                        seen[f][c.x, c.y] = true;
                        queue.Enqueue(new Vector3Int(c.x, c.y, f));
                    }
                }

                return seen;
            }

            private Vector3Int FindStranded(bool[][,] seen)
            {
                for (var f = 0; f < _s.floors; f++)
                for (var x = 1; x < _s.gridSize - 1; x++)
                for (var z = 1; z < _s.gridSize - 1; z++)
                    if (_cell[f][x, z] != Cell.Solid && !seen[f][x, z])
                        return new Vector3Int(x, z, f);

                return new Vector3Int(-1, -1, -1);
            }

            private Vector3Int NearestReachable(bool[][,] seen, Vector3Int from)
            {
                var best = int.MaxValue;
                var found = new Vector3Int(-1, -1, -1);

                for (var x = 1; x < _s.gridSize - 1; x++)
                for (var z = 1; z < _s.gridSize - 1; z++)
                {
                    if (!seen[from.z][x, z]) continue;

                    var distance = Mathf.Abs(x - from.x) + Mathf.Abs(z - from.y);
                    if (distance < best) { best = distance; found = new Vector3Int(x, z, from.z); }
                }

                return found;
            }

            // ============================================================ 2. geometry

            private void BuildGeometry()
            {
                for (var f = 0; f < _s.floors; f++)
                {
                    BuildTiles(f);
                    BuildWalls(f);
                }

                BuildStairs();
                BuildCeilingLid();
            }

            private void BuildTiles(int floor)
            {
                var y = FloorY(floor);

                for (var x = 0; x < _s.gridSize; x++)
                for (var z = 0; z < _s.gridSize; z++)
                {
                    if (!Walkable(floor, x, z)) continue;

                    var centre = CellCentre(floor, x, z);
                    var overShaft = InRect(_s.stairwell, x, z) && IsShaftCell(z);

                    // Floor. Skipped where the stairwell shaft passes through this floor, or the
                    // upper storey would be a lid over its own staircase.
                    if (!(floor > 0 && overShaft))
                    {
                        _blueprint.Add("Carpet", new Vector3(centre.x, y - 0.1f, centre.z),
                            new Vector3(_s.cellSize, 0.2f, _s.cellSize), MapSurface.Floor, true);
                    }

                    // Ceiling. Skipped over the whole stairwell on the lower floor — that hole is
                    // what lets the player actually walk up rather than meeting a ceiling halfway.
                    var topFloor = floor == _s.floors - 1;
                    var underShaft = !topFloor && InRect(_s.stairwell, x, z);

                    if (!underShaft)
                    {
                        _blueprint.Add("Ceiling Tile", new Vector3(centre.x, y + _s.CeilingHeight, centre.z),
                            new Vector3(_s.cellSize - 0.06f, 0.12f, _s.cellSize - 0.06f),
                            MapSurface.Ceiling, false);
                    }
                }
            }

            /// <summary>The stairwell cells the flight itself passes through; the last row is the landing.</summary>
            private bool IsShaftCell(int z) => z < _s.stairwell.yMax - 1;

            /// <summary>
            /// One wall on every boundary where a walkable cell meets a solid one, merged into runs
            /// so a ten-cell corridor wall is one box with one collider instead of ten. This is why
            /// 35 x 35 across two floors costs a few hundred boxes rather than a few thousand.
            /// </summary>
            private void BuildWalls(int floor)
            {
                var y = FloorY(floor);
                var height = _s.CeilingHeight;

                // Boundaries perpendicular to X, i.e. running along Z.
                for (var x = 0; x <= _s.gridSize; x++)
                {
                    var runStart = -1;

                    for (var z = 0; z <= _s.gridSize; z++)
                    {
                        var wall = z < _s.gridSize && Walkable(floor, x - 1, z) != Walkable(floor, x, z);
                        if (wall && runStart < 0) runStart = z;
                        if (wall || runStart < 0) continue;

                        var count = z - runStart;

                        _blueprint.Add("Wall Z",
                            new Vector3(_originX + x * _s.cellSize, y + height * 0.5f,
                                        _originZ + (runStart + count * 0.5f) * _s.cellSize),
                            new Vector3(_s.wallThickness, height, count * _s.cellSize + _s.wallThickness),
                            MapSurface.Wall, true);

                        runStart = -1;
                    }
                }

                // Boundaries perpendicular to Z, running along X.
                for (var z = 0; z <= _s.gridSize; z++)
                {
                    var runStart = -1;

                    for (var x = 0; x <= _s.gridSize; x++)
                    {
                        var wall = x < _s.gridSize && Walkable(floor, x, z - 1) != Walkable(floor, x, z);
                        if (wall && runStart < 0) runStart = x;
                        if (wall || runStart < 0) continue;

                        var count = x - runStart;

                        _blueprint.Add("Wall X",
                            new Vector3(_originX + (runStart + count * 0.5f) * _s.cellSize, y + height * 0.5f,
                                        _originZ + z * _s.cellSize),
                            new Vector3(count * _s.cellSize + _s.wallThickness, height, _s.wallThickness),
                            MapSurface.Wall, true);

                        runStart = -1;
                    }
                }
            }

            /// <summary>
            /// A straight flight filling the stairwell, from the lower floor up to the landing row.
            /// Each step is a box, so the stairs are collision the server sees exactly as the client
            /// does.
            /// </summary>
            private void BuildStairs()
            {
                if (_s.floors < 2 || _s.stairSteps < 1) return;

                var steps = Mathf.Max(1, _s.stairSteps);

                // A landing at BOTH ends, not just the top. Running the flight right up to the
                // stairwell edge means the first cell's centre is already half a metre up the steps,
                // so there is nowhere flat to step on from the corridor and the upper floor is
                // unreachable.
                var runCells = Mathf.Max(1, _s.stairwell.height - 2);
                var totalRun = runCells * _s.cellSize;
                var rise = _s.floorHeight / steps;
                var tread = totalRun / steps;

                // Both constants exist to kill z-fighting. Running the flight to exactly the wall
                // plane put the step's side face in the same place as the wall's, and butting each
                // step exactly against the next put their end faces in the same place too —
                // coincident coplanar faces are what flickers. A couple of centimetres of overlap
                // buries those faces inside solid geometry, where they cannot be drawn at all.
                const float sideInset = 0.04f;
                const float stepOverlap = 0.03f;

                var width = _s.stairwell.width * _s.cellSize - sideInset * 2f;
                var centreX = _originX + (_s.stairwell.x + _s.stairwell.width * 0.5f) * _s.cellSize;
                var startZ = _originZ + (_s.stairwell.y + 1) * _s.cellSize;

                for (var i = 0; i < steps; i++)
                {
                    var top = (i + 1) * rise;

                    // Grow backwards, into the shorter step behind, which hides this step's back face.
                    var depth = tread + stepOverlap;
                    var z = startZ + (i + 0.5f) * tread - stepOverlap * 0.5f;

                    // Each step is a solid block from the floor up to its own tread, so there is no
                    // gap to fall through and the server's box list needs no special case for stairs.
                    _blueprint.Add("Step", new Vector3(centreX, top * 0.5f, z),
                        new Vector3(width, top, depth), MapSurface.Trim, true);
                }
            }

            /// <summary>
            /// One invisible slab across the top storey, level with its ceiling.
            ///
            /// Ceiling tiles carry no collider — deliberately, since a grid of them would be a
            /// thousand colliders and they never need to stop anything from below. That holds on
            /// every floor but the last: the storey above provides the barrier, because its carpet
            /// slab *is* solid. The top floor has nothing above it, so a player who climbs onto a
            /// device console (1 m) and jumps (1.2 m) puts their eyes at 7.0 m against a 6.2 m
            /// ceiling and sees straight out of the level.
            ///
            /// Emitted last, because that is where it is in the box order it has to reproduce.
            /// </summary>
            private void BuildCeilingLid()
            {
                var span = _s.gridSize * _s.cellSize;
                var y = FloorY(_s.FloorCount - 1) + _s.CeilingHeight;

                // Sits *on* the ceiling plane rather than through it, so it takes no headroom away.
                _blueprint.Add("Ceiling Lid",
                    new Vector3(_originX + span * 0.5f, y + 0.1f, _originZ + span * 0.5f),
                    new Vector3(span, 0.2f, span), MapSurface.Ceiling, true);
            }

            /// <summary>
            /// The emissive panels, so the lighting reads as an office grid.
            ///
            /// **Emitted after everything else and never colliding**, which is what keeps them out
            /// of the collision list entirely — their position in the piece order cannot affect the
            /// map hash. The lamps themselves, and which of them flicker, belong to the runtime
            /// ambience component: a Light is not geometry, and the flicker draws its own randomness.
            /// </summary>
            private void BuildLightPanels()
            {
                var step = Mathf.Max(1, _s.lightSpacing);

                for (var f = 0; f < _s.floors; f++)
                for (var x = step / 2; x < _s.gridSize; x += step)
                for (var z = step / 2; z < _s.gridSize; z += step)
                {
                    if (!Walkable(f, x, z)) continue;
                    if (f < _s.floors - 1 && InRect(_s.stairwell, x, z)) continue;   // no ceiling to mount on

                    var centre = CellCentre(f, x, z);
                    var y = FloorY(f) + _s.CeilingHeight;

                    _blueprint.Add("Panel", new Vector3(centre.x, y - 0.07f, centre.z),
                        new Vector3(_s.cellSize * 0.42f, 0.06f, _s.cellSize * 0.42f),
                        MapSurface.LightPanel, false);
                }
            }

            // ============================================================ 3. what the server needs

            /// <summary>
            /// The walkability grid.
            ///
            /// Only <c>Standable</c> and <c>StairLink</c> are set. <c>FreeFloor</c> is filled in at
            /// export time from the collision boxes, because that flag means "the server can put a
            /// player here without pushing them out" and so has to be judged with the server's own
            /// player box.
            ///
            /// <c>StairLink</c> covers the whole stairwell rectangle rather than just the steps. The
            /// upper storey's shaft cells are deliberately *not* standable — there is no floor built
            /// over them — yet they are exactly where a route crosses between storeys, so a path
            /// solver needs them marked even though nothing stands there.
            /// </summary>
            private MapGridData BuildGrid()
            {
                var floorCount = _s.FloorCount;

                var grid = new MapGridData
                {
                    Floors = floorCount,
                    Width = _s.gridSize,
                    Depth = _s.gridSize,
                    CellSize = _s.cellSize,
                    FloorHeight = _s.floorHeight,

                    // The same origin CellCentre uses. MapGridData.CellToWorld reproduces that
                    // formula, and half a cell of disagreement would read as "keys sunk halfway
                    // into walls".
                    OriginX = _originX,
                    OriginZ = _originZ,
                    Cells = new byte[floorCount * _s.gridSize * _s.gridSize],
                };

                for (var f = 0; f < floorCount; f++)
                for (var x = 0; x < _s.gridSize; x++)
                for (var z = 0; z < _s.gridSize; z++)
                {
                    var flags = MapCellFlags.None;

                    if (IsStandable(f, x, z)) flags |= MapCellFlags.Standable;
                    if (InRect(_s.stairwell, x, z)) flags |= MapCellFlags.StairLink;

                    grid.Cells[grid.CellIndex(f, x, z)] = (byte)flags;
                }

                return grid;
            }

            /// <summary>
            /// Can something stand here? Solid cells are wall; the upper storey's stairwell shaft is
            /// walkable in the grid but has no floor built over it, so anything dropped there falls
            /// through to the storey below.
            /// </summary>
            private bool IsStandable(int floor, int x, int z)
            {
                if (!Walkable(floor, x, z)) return false;

                return !(floor > 0 && InRect(_s.stairwell, x, z) && IsShaftCell(z));
            }

            /// <summary>
            /// Eight spawns on a ring inside the spawn room, facing its centre. A ring rather than
            /// the whole room, or eight players stand shoulder to shoulder in one corner.
            /// </summary>
            private void CollectSpawns()
            {
                // Matches the server's Room.MaxPlayers.
                const int spawnCount = 8;

                var centre = RectCentre(0, _s.spawnRoom);

                for (var x = _s.spawnRoom.x; x < _s.spawnRoom.xMax && _blueprint.Spawns.Count < spawnCount; x++)
                for (var z = _s.spawnRoom.y; z < _s.spawnRoom.yMax && _blueprint.Spawns.Count < spawnCount; z++)
                {
                    var onRing = x == _s.spawnRoom.x || x == _s.spawnRoom.xMax - 1
                              || z == _s.spawnRoom.y || z == _s.spawnRoom.yMax - 1;
                    if (!onRing) continue;

                    var cell = CellCentre(0, x, z);
                    var toCentre = new Vector3(centre.x - cell.x, 0f, centre.z - cell.z);

                    // Yaw 0 is +Z, the same convention the server's move function uses.
                    var yaw = toCentre.sqrMagnitude > 1e-4f ? Mathf.Atan2(toCentre.x, toCentre.z) : 0f;

                    _blueprint.Spawns.Add(new MapSpawnPoint
                    {
                        Position = new Vector3(cell.x, FloorY(0), cell.z),
                        Yaw = yaw,
                    });
                }
            }

            // ============================================================ geometry helpers

            private float FloorY(int floor) => floor * _s.floorHeight;

            private Vector3 CellCentre(int floor, int x, int z) => new Vector3(
                _originX + (x + 0.5f) * _s.cellSize, FloorY(floor), _originZ + (z + 0.5f) * _s.cellSize);

            private Vector3 RectCentre(int floor, RectInt rect)
            {
                var near = CellCentre(floor, rect.x, rect.y);
                var far = CellCentre(floor, rect.xMax - 1, rect.yMax - 1);

                return new Vector3((near.x + far.x) * 0.5f, FloorY(floor), (near.z + far.z) * 0.5f);
            }

            private static bool InRect(RectInt rect, int x, int z) =>
                x >= rect.x && x < rect.xMax && z >= rect.y && z < rect.yMax;

            private bool Walkable(int floor, int x, int z) =>
                x >= 0 && z >= 0 && x < _s.gridSize && z < _s.gridSize && _cell[floor][x, z] != Cell.Solid;
        }
    }
}
