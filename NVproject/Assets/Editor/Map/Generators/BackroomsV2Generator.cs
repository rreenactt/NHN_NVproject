using System;
using System.Collections.Generic;
using NV.Client.Map;
using NV.Shared.Collision;
using UnityEngine;

namespace NV.Client.EditorTools.Generators
{
    /// <summary>
    /// Backrooms V2 — a single-storey, open-plan concrete floor, as data.
    ///
    /// Written from the <see cref="IMapGenerator"/> contract alone: it reuses no code, no
    /// algorithm and no numbers from the original Backrooms generators, by requirement
    /// (<c>NVserver/docs/backrooms-v2-plan.md</c> §1). Where the original solves layout by
    /// stamping anchor rooms and repairing connectivity afterwards, this one zones the floor by
    /// BSP and opens doorways along a spanning tree of the zone graph — so connectivity holds by
    /// construction, and the flood-fill at the end is a bug detector rather than a repair step.
    ///
    /// The other structural guarantee is the fill margin: every interior obstacle keeps one open
    /// cell of separation from its zone's edge and from every other obstacle. An obstacle that
    /// cannot touch anything can never close a pocket, so no fill can undo what the spanning tree
    /// promised.
    /// </summary>
    public sealed class BackroomsV2Generator : IMapGenerator
    {
        public string DisplayName => "Backrooms V2";

        public string DefaultMapName => "backrooms-v2";

        public Type SettingsType => typeof(BackroomsV2Settings);

        public MapBlueprint Generate(MapGeneratorSettings settings)
        {
            var v2 = settings as BackroomsV2Settings;

            if (v2 == null)
            {
                throw new ArgumentException(
                    $"BackroomsV2Generator 는 BackroomsV2Settings 를 읽는다. {settings?.GetType().Name ?? "null"} 을 받았다.",
                    nameof(settings));
            }

            return new Solver(v2).Run();
        }

        /// <summary>
        /// All generation state for one run. Every random draw comes from the one seeded
        /// <see cref="System.Random"/>, in the order the methods below run — that order is the
        /// reproducibility contract, so nothing here may draw conditionally on anything but the
        /// draws that came before it.
        /// </summary>
        private sealed class Solver
        {
            private enum ZoneKind
            {
                OpenHall = 0,
                Partitions = 1,
                RoomBlocks = 2,
                Empty = 3,
            }

            private sealed class Zone
            {
                public RectInt Cells;
                public ZoneKind Kind;
                public bool IsSpawnZone;
            }

            /// <summary>One shared border between two zones, and where its doorway went.</summary>
            private struct Adjacency
            {
                public int ZoneA;
                public int ZoneB;

                /// <summary>True when the border line runs along Z (the wall blocks X travel).</summary>
                public bool VerticalBorder;

                /// <summary>Grid line index the border sits on.</summary>
                public int Line;

                /// <summary>First cell and cell count of the shared segment along the border.</summary>
                public int SegmentStart;
                public int SegmentLength;
            }

            private readonly BackroomsV2Settings _settings;
            private readonly MapBlueprint _blueprint;
            private readonly System.Random _random;
            private readonly int _size;

            /// <summary>
            /// Solver failure, as opposed to a settings blocker. A randomised seed makes a level
            /// that generates fine and merely refuses export; a failure here means the layout
            /// itself is unusable and the passes after it would dereference what never got built.
            /// </summary>
            private bool _failed;

            private readonly List<Zone> _zones = new List<Zone>();
            private readonly List<Adjacency> _adjacencies = new List<Adjacency>();

            /// <summary>Solid cells — interior obstacles. Perimeter and zone borders live on edges, not cells.</summary>
            private readonly bool[,] _solid;

            /// <summary>_wallX[x, z]: a wall on the line between cell (x-1, z) and (x, z). Index 1..size-1.</summary>
            private readonly bool[,] _wallX;

            /// <summary>_wallZ[x, z]: a wall on the line between cell (x, z-1) and (x, z). Index 1..size-1.</summary>
            private readonly bool[,] _wallZ;

            public Solver(BackroomsV2Settings settings)
            {
                _settings = settings;
                _size = settings.gridSize;
                _solid = new bool[_size, _size];
                _wallX = new bool[_size + 1, _size];
                _wallZ = new bool[_size, _size + 1];

                _blueprint = new MapBlueprint
                {
                    MapName = settings.mapName,
                    UsedSeed = settings.ResolveSeed(),
                    Blocker = settings.DescribeBlocker(),
                };

                _random = new System.Random(_blueprint.UsedSeed);
            }

            public MapBlueprint Run()
            {
                _blueprint.Palette[MapSurface.Wall] = _settings.wallColor;
                _blueprint.Palette[MapSurface.Floor] = _settings.floorColor;
                _blueprint.Palette[MapSurface.Ceiling] = _settings.ceilingColor;
                _blueprint.Palette[MapSurface.Trim] = _settings.trimColor;
                _blueprint.Palette[MapSurface.LightPanel] = _settings.lightColor;

                // Solve order is the draw order. Geometry emission below draws nothing.
                SplitZones();
                RaiseBorders();
                AssignKinds();
                OpenDoorways();
                FillInteriors();
                PlaceSpawns();
                Validate();

                BuildGeometry();
                BuildLights();
                BuildGrid();

                return _blueprint;
            }

            // ---- layout ------------------------------------------------------------------

            /// <summary>
            /// Recursive halving. A rect wider than zoneMax on either side is always split along
            /// its longer side, at the middle ± a quarter — clamped so both halves keep zoneMin.
            /// Only rects that must split draw, so the draw sequence is a function of the seed
            /// alone.
            /// </summary>
            private void SplitZones()
            {
                Split(new RectInt(0, 0, _size, _size));
            }

            private void Split(RectInt rect)
            {
                var alongX = rect.width >= rect.height;
                var side = alongX ? rect.width : rect.height;

                if (side <= _settings.zoneMax)
                {
                    _zones.Add(new Zone { Cells = rect });
                    return;
                }

                var quarter = Mathf.Max(1, side / 4);
                var cut = side / 2 + _random.Next(-quarter, quarter + 1);
                cut = Mathf.Clamp(cut, _settings.zoneMin, side - _settings.zoneMin);

                if (alongX)
                {
                    Split(new RectInt(rect.x, rect.y, cut, rect.height));
                    Split(new RectInt(rect.x + cut, rect.y, rect.width - cut, rect.height));
                }
                else
                {
                    Split(new RectInt(rect.x, rect.y, rect.width, cut));
                    Split(new RectInt(rect.x, rect.y + cut, rect.width, rect.height - cut));
                }
            }

            /// <summary>
            /// Walls up every shared border and records each as an adjacency. Draws nothing —
            /// doorways are opened later, so a border starts fully closed.
            /// </summary>
            private void RaiseBorders()
            {
                for (var a = 0; a < _zones.Count; a++)
                {
                    for (var b = a + 1; b < _zones.Count; b++)
                    {
                        var ra = _zones[a].Cells;
                        var rb = _zones[b].Cells;

                        if (ra.xMax == rb.xMin || rb.xMax == ra.xMin)
                        {
                            var line = ra.xMax == rb.xMin ? ra.xMax : rb.xMax;
                            var start = Mathf.Max(ra.yMin, rb.yMin);
                            var end = Mathf.Min(ra.yMax, rb.yMax);

                            if (end - start >= _settings.doorwayWidth)
                            {
                                for (var z = start; z < end; z++) _wallX[line, z] = true;

                                _adjacencies.Add(new Adjacency
                                {
                                    ZoneA = a, ZoneB = b, VerticalBorder = true,
                                    Line = line, SegmentStart = start, SegmentLength = end - start,
                                });
                            }
                            else
                            {
                                // Too short for a doorway: wall it and do not offer it to the tree.
                                for (var z = start; z < end; z++) _wallX[line, z] = true;
                            }
                        }
                        else if (ra.yMax == rb.yMin || rb.yMax == ra.yMin)
                        {
                            var line = ra.yMax == rb.yMin ? ra.yMax : rb.yMax;
                            var start = Mathf.Max(ra.xMin, rb.xMin);
                            var end = Mathf.Min(ra.xMax, rb.xMax);

                            if (end - start >= _settings.doorwayWidth)
                            {
                                for (var x = start; x < end; x++) _wallZ[x, line] = true;

                                _adjacencies.Add(new Adjacency
                                {
                                    ZoneA = a, ZoneB = b, VerticalBorder = false,
                                    Line = line, SegmentStart = start, SegmentLength = end - start,
                                });
                            }
                            else
                            {
                                for (var x = start; x < end; x++) _wallZ[x, line] = true;
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// One kind draw per zone, in zone order, then two overrides that draw nothing: the
            /// zone holding the grid centre becomes an open hall (the altar searches outward from
            /// the grid centre on the ground floor and must find standable cells), and the largest
            /// open hall becomes the spawn zone.
            /// </summary>
            private void AssignKinds()
            {
                for (var index = 0; index < _zones.Count; index++)
                {
                    var roll = _random.NextDouble();

                    if (roll < 0.35) _zones[index].Kind = ZoneKind.OpenHall;
                    else if (roll < 0.65) _zones[index].Kind = ZoneKind.Partitions;
                    else if (roll < 0.90) _zones[index].Kind = ZoneKind.RoomBlocks;
                    else _zones[index].Kind = ZoneKind.Empty;
                }

                var centre = new Vector2Int(_size / 2, _size / 2);
                var centreZone = ZoneAt(centre);
                if (centreZone != null) centreZone.Kind = ZoneKind.OpenHall;

                Zone spawnZone = null;
                var bestArea = -1;
                var bestDistance = float.MaxValue;

                foreach (var zone in _zones)
                {
                    if (zone.Kind != ZoneKind.OpenHall) continue;

                    var area = zone.Cells.width * zone.Cells.height;
                    var toCentre = ((Vector2)zone.Cells.center - (Vector2)centre).sqrMagnitude;

                    if (area > bestArea || (area == bestArea && toCentre < bestDistance))
                    {
                        spawnZone = zone;
                        bestArea = area;
                        bestDistance = toCentre;
                    }
                }

                // Unreachable with the centre override in place, but a solver that can crash on a
                // weird parameter set should say why instead.
                if (spawnZone == null)
                {
                    _blueprint.Blocker = "개방 홀이 하나도 나오지 않았다 — 스폰 존을 정할 수 없다.";
                    _failed = true;
                    return;
                }

                spawnZone.IsSpawnZone = true;
            }

            /// <summary>
            /// Kruskal over the adjacency list: shuffle, take every edge that joins two components
            /// (a doorway each), then offer every remaining edge a loop doorway at
            /// <see cref="BackroomsV2Settings.loopChance"/>. Every edge draws — accepted or not —
            /// so the sequence stays predictable.
            /// </summary>
            private void OpenDoorways()
            {
                if (_failed) return;

                var order = new List<int>(_adjacencies.Count);
                for (var index = 0; index < _adjacencies.Count; index++) order.Add(index);

                for (var index = order.Count - 1; index > 0; index--)
                {
                    var swap = _random.Next(index + 1);
                    (order[index], order[swap]) = (order[swap], order[index]);
                }

                var parent = new int[_zones.Count];
                for (var index = 0; index < parent.Length; index++) parent[index] = index;

                foreach (var edgeIndex in order)
                {
                    var edge = _adjacencies[edgeIndex];
                    var rootA = Find(parent, edge.ZoneA);
                    var rootB = Find(parent, edge.ZoneB);

                    if (rootA != rootB)
                    {
                        parent[rootA] = rootB;
                        CutDoorway(edge);
                    }
                    else if (_random.NextDouble() < _settings.loopChance)
                    {
                        CutDoorway(edge);
                    }
                    else
                    {
                        // Draw consumed above; nothing to do.
                    }
                }
            }

            private static int Find(int[] parent, int node)
            {
                while (parent[node] != node)
                {
                    parent[node] = parent[parent[node]];
                    node = parent[node];
                }

                return node;
            }

            private void CutDoorway(Adjacency edge)
            {
                var width = Mathf.Min(_settings.doorwayWidth, edge.SegmentLength);
                var offset = _random.Next(edge.SegmentLength - width + 1);
                var start = edge.SegmentStart + offset;

                for (var cell = start; cell < start + width; cell++)
                {
                    if (edge.VerticalBorder) _wallX[edge.Line, cell] = false;
                    else _wallZ[cell, edge.Line] = false;
                }
            }

            // ---- interiors ---------------------------------------------------------------

            /// <summary>
            /// Obstacles, zone by zone in zone order. Two rules keep the connectivity guarantee:
            /// every obstacle cell stays at least one cell inside the zone (the perimeter ring of
            /// every zone is open, and every doorway opens onto that ring), and
            /// <see cref="CanPlace"/> refuses any cell whose neighbourhood already holds another
            /// obstacle, so obstacles never merge into a wall that could close a pocket. The grid
            /// centre's 3×3 is also kept clear for the altar search.
            /// </summary>
            private void FillInteriors()
            {
                if (_failed) return;

                foreach (var zone in _zones)
                {
                    if (zone.IsSpawnZone) continue;   // the marshalling hall stays clear

                    switch (zone.Kind)
                    {
                        case ZoneKind.OpenHall: FillPillars(zone); break;
                        case ZoneKind.Partitions: FillPartitions(zone); break;
                        case ZoneKind.RoomBlocks: FillBlocks(zone); break;
                        case ZoneKind.Empty: break;
                    }
                }
            }

            private void FillPillars(Zone zone)
            {
                var offsetX = _random.Next(_settings.pillarSpacing);
                var offsetZ = _random.Next(_settings.pillarSpacing);

                for (var z = zone.Cells.yMin + 1; z < zone.Cells.yMax - 1; z++)
                {
                    for (var x = zone.Cells.xMin + 1; x < zone.Cells.xMax - 1; x++)
                    {
                        if ((x - zone.Cells.xMin) % _settings.pillarSpacing != offsetX) continue;
                        if ((z - zone.Cells.yMin) % _settings.pillarSpacing != offsetZ) continue;
                        if (!CanPlace(x, z)) continue;

                        _solid[x, z] = true;
                    }
                }
            }

            private void FillPartitions(Zone zone)
            {
                var stubs = 2 + _random.Next(3);

                for (var stub = 0; stub < stubs; stub++)
                {
                    var alongX = _random.Next(2) == 0;
                    var interiorW = zone.Cells.width - 2;
                    var interiorH = zone.Cells.height - 2;
                    var lane = _random.Next(alongX ? interiorH : interiorW);
                    var start = _random.Next(alongX ? interiorW : interiorH);
                    var length = 2 + _random.Next(Mathf.Max(1, (alongX ? interiorW : interiorH) - 2));

                    for (var step = 0; step < length; step++)
                    {
                        var x = alongX ? zone.Cells.xMin + 1 + start + step : zone.Cells.xMin + 1 + lane;
                        var z = alongX ? zone.Cells.yMin + 1 + lane : zone.Cells.yMin + 1 + start + step;

                        if (x >= zone.Cells.xMax - 1 || z >= zone.Cells.yMax - 1) break;
                        if (!CanPlace(x, z, alongX, step > 0)) break;

                        _solid[x, z] = true;
                    }
                }
            }

            private void FillBlocks(Zone zone)
            {
                var blocks = 1 + _random.Next(2);

                for (var block = 0; block < blocks; block++)
                {
                    var w = 2 + _random.Next(2);
                    var d = 2 + _random.Next(2);
                    var maxX = zone.Cells.width - 2 - w;
                    var maxZ = zone.Cells.height - 2 - d;

                    // Draws happen whether or not the block fits — the sequence must not depend
                    // on zone shape more than it already does through the counts above.
                    var offX = _random.Next(Mathf.Max(1, maxX + 1));
                    var offZ = _random.Next(Mathf.Max(1, maxZ + 1));

                    if (maxX < 0 || maxZ < 0) continue;

                    var originX = zone.Cells.xMin + 1 + offX;
                    var originZ = zone.Cells.yMin + 1 + offZ;

                    var fits = true;
                    for (var z = originZ; z < originZ + d && fits; z++)
                        for (var x = originX; x < originX + w && fits; x++)
                            if (!CanPlace(x, z)) fits = false;

                    if (!fits) continue;

                    for (var z = originZ; z < originZ + d; z++)
                        for (var x = originX; x < originX + w; x++)
                            _solid[x, z] = true;
                }
            }

            /// <summary>
            /// May an obstacle take this cell? Refuses the grid centre's 3×3 (altar search area)
            /// and any cell whose 8-neighbourhood already holds an obstacle — except, for a
            /// partition stub extending itself, the run direction it is growing along.
            /// </summary>
            private bool CanPlace(int x, int z, bool alongX = false, bool extending = false)
            {
                var centre = _size / 2;
                if (Mathf.Abs(x - centre) <= 1 && Mathf.Abs(z - centre) <= 1) return false;

                for (var dz = -1; dz <= 1; dz++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;

                        // The cell the stub grew out of is its own component, not a neighbour.
                        if (extending && (alongX ? dz == 0 && dx == -1 : dx == 0 && dz == -1)) continue;

                        var nx = x + dx;
                        var nz = z + dz;

                        if (nx < 0 || nz < 0 || nx >= _size || nz >= _size) continue;
                        if (_solid[nx, nz]) return false;
                    }
                }

                return true;
            }

            // ---- spawns ------------------------------------------------------------------

            /// <summary>
            /// Eight spawns — the map contract: the server picks by PlayerId and the exported map
            /// tests assert exactly eight — spread at even strides along the spawn zone's
            /// perimeter ring, which the fill rules keep open. Everyone faces the zone centre.
            /// </summary>
            private void PlaceSpawns()
            {
                if (_failed) return;

                Zone spawnZone = null;
                foreach (var zone in _zones)
                    if (zone.IsSpawnZone) spawnZone = zone;

                var ring = new List<Vector2Int>();
                var rect = spawnZone.Cells;

                for (var x = rect.xMin; x < rect.xMax; x++) ring.Add(new Vector2Int(x, rect.yMin));
                for (var z = rect.yMin + 1; z < rect.yMax; z++) ring.Add(new Vector2Int(rect.xMax - 1, z));
                for (var x = rect.xMax - 2; x >= rect.xMin; x--) ring.Add(new Vector2Int(x, rect.yMax - 1));
                for (var z = rect.yMax - 2; z > rect.yMin; z--) ring.Add(new Vector2Int(rect.xMin, z));

                var zoneCentre = CellToWorld(rect.center.x, rect.center.y);
                _blueprint.SpawnCentre = zoneCentre;

                const int spawnCount = 8;

                for (var index = 0; index < spawnCount; index++)
                {
                    var cell = ring[index * ring.Count / spawnCount];
                    var position = CellToWorld(cell.x + 0.5f, cell.y + 0.5f);
                    var toCentre = zoneCentre - position;

                    // 0 is +Z, clockwise — the server's yaw convention. Wound into [0, 2pi) by
                    // hand rather than left to a range reduction we do not control.
                    var yaw = Mathf.Atan2(toCentre.x, toCentre.z);
                    if (yaw < 0f) yaw += 2f * Mathf.PI;

                    _blueprint.Spawns.Add(new MapSpawnPoint { Position = position, Yaw = yaw });
                }
            }

            // ---- validation --------------------------------------------------------------

            /// <summary>
            /// Flood-fill from the first spawn over open cells, honouring border walls. The
            /// spanning tree plus the fill margin make full reachability true by construction, so
            /// a shortfall here is an implementation bug — the answer is a blocker, never a
            /// repair pass.
            /// </summary>
            private void Validate()
            {
                if (_failed) return;

                var openCells = 0;
                for (var z = 0; z < _size; z++)
                    for (var x = 0; x < _size; x++)
                        if (!_solid[x, z]) openCells++;

                var start = WorldToCell(_blueprint.Spawns[0].Position);
                var seen = new bool[_size, _size];
                var queue = new Queue<Vector2Int>();

                seen[start.x, start.y] = true;
                queue.Enqueue(start);
                var reached = 1;

                while (queue.Count > 0)
                {
                    var cell = queue.Dequeue();

                    TryStep(cell.x - 1, cell.y, !_wallX[cell.x, cell.y], seen, queue, ref reached);
                    TryStep(cell.x + 1, cell.y, !_wallX[cell.x + 1, cell.y], seen, queue, ref reached);
                    TryStep(cell.x, cell.y - 1, !_wallZ[cell.x, cell.y], seen, queue, ref reached);
                    TryStep(cell.x, cell.y + 1, !_wallZ[cell.x, cell.y + 1], seen, queue, ref reached);
                }

                if (reached != openCells)
                {
                    _blueprint.Blocker =
                        $"연결성 검증 실패: 열린 셀 {openCells}개 중 {reached}개만 스폰에서 닿는다. " +
                        "구성 단계가 보장해야 하는 조건이므로 이것은 생성기 버그다.";
                }
            }

            private void TryStep(
                int x, int z, bool passable, bool[,] seen, Queue<Vector2Int> queue, ref int reached)
            {
                if (!passable || x < 0 || z < 0 || x >= _size || z >= _size) return;
                if (_solid[x, z] || seen[x, z]) return;

                seen[x, z] = true;
                queue.Enqueue(new Vector2Int(x, z));
                reached++;
            }

            // ---- geometry ----------------------------------------------------------------

            /// <summary>
            /// Emission order is the map hash, so it is frozen here: floor, perimeter, border
            /// walls (X-normal then Z-normal, line by line, runs merged), interior obstacles
            /// (zone order, row runs), ceiling lid last of the colliders, then the non-colliding
            /// light panels. Draws nothing.
            /// </summary>
            private void BuildGeometry()
            {
                var span = _size * _settings.cellSize;
                var half = span * 0.5f;
                var wallY = _settings.ceilingHeight * 0.5f;
                var outer = span + _settings.wallThickness * 2f;
                var edge = half + _settings.wallThickness * 0.5f;

                _blueprint.Add("Floor", new Vector3(0f, -0.1f, 0f),
                    new Vector3(span, 0.2f, span), MapSurface.Floor, true);

                _blueprint.Add("Perimeter +Z", new Vector3(0f, wallY, edge),
                    new Vector3(outer, _settings.ceilingHeight, _settings.wallThickness), MapSurface.Wall, true);
                _blueprint.Add("Perimeter -Z", new Vector3(0f, wallY, -edge),
                    new Vector3(outer, _settings.ceilingHeight, _settings.wallThickness), MapSurface.Wall, true);
                _blueprint.Add("Perimeter +X", new Vector3(edge, wallY, 0f),
                    new Vector3(_settings.wallThickness, _settings.ceilingHeight, outer), MapSurface.Wall, true);
                _blueprint.Add("Perimeter -X", new Vector3(-edge, wallY, 0f),
                    new Vector3(_settings.wallThickness, _settings.ceilingHeight, outer), MapSurface.Wall, true);

                BuildBorderWalls(wallY);
                BuildObstacles(wallY);

                // One collide=true lid over the whole floor — a storey with nothing above it is
                // open sky, and a player on anything climbable can jump out of the level.
                _blueprint.Add("Ceiling Lid", new Vector3(0f, _settings.ceilingHeight + 0.1f, 0f),
                    new Vector3(span, 0.2f, span), MapSurface.Ceiling, true);
            }

            private void BuildBorderWalls(float wallY)
            {
                var cell = _settings.cellSize;

                for (var x = 1; x < _size; x++)
                {
                    var z = 0;
                    while (z < _size)
                    {
                        if (!_wallX[x, z]) { z++; continue; }

                        var run = 0;
                        while (z + run < _size && _wallX[x, z + run]) run++;

                        // Runs extend by one thickness so crossings close instead of pinholing.
                        _blueprint.Add($"Border X {x} {z}",
                            new Vector3(Origin + x * cell, wallY, Origin + (z + run * 0.5f) * cell),
                            new Vector3(_settings.wallThickness, _settings.ceilingHeight,
                                run * cell + _settings.wallThickness),
                            MapSurface.Wall, true);

                        z += run;
                    }
                }

                for (var z = 1; z < _size; z++)
                {
                    var x = 0;
                    while (x < _size)
                    {
                        if (!_wallZ[x, z]) { x++; continue; }

                        var run = 0;
                        while (x + run < _size && _wallZ[x + run, z]) run++;

                        _blueprint.Add($"Border Z {z} {x}",
                            new Vector3(Origin + (x + run * 0.5f) * cell, wallY, Origin + z * cell),
                            new Vector3(run * cell + _settings.wallThickness, _settings.ceilingHeight,
                                _settings.wallThickness),
                            MapSurface.Wall, true);

                        x += run;
                    }
                }
            }

            private void BuildObstacles(float wallY)
            {
                var cell = _settings.cellSize;

                foreach (var zone in _zones)
                {
                    var isHall = zone.Kind == ZoneKind.OpenHall;

                    for (var z = zone.Cells.yMin; z < zone.Cells.yMax; z++)
                    {
                        var x = zone.Cells.xMin;
                        while (x < zone.Cells.xMax)
                        {
                            if (!_solid[x, z]) { x++; continue; }

                            var run = 0;
                            while (x + run < zone.Cells.xMax && _solid[x + run, z]) run++;

                            // Pillars read as columns, partitions and blocks as walls — the
                            // surface split is visual only, both collide identically.
                            _blueprint.Add(isHall ? $"Pillar {x} {z}" : $"Fill {x} {z}",
                                new Vector3(
                                    Origin + (x + run * 0.5f) * cell, wallY,
                                    Origin + (z + 0.5f) * cell),
                                new Vector3(run * cell, _settings.ceilingHeight, cell),
                                isHall ? MapSurface.Trim : MapSurface.Wall, true);

                            x += run;
                        }
                    }
                }
            }

            /// <summary>
            /// Ceiling light strips over open cells at a fixed pitch. Panels are boxes you can
            /// see and never collide; the point-light positions ride
            /// <see cref="MapBlueprint.Lights"/> for the ambience component. Draws nothing —
            /// which lamps flicker is the ambience's business, from its own random.
            /// </summary>
            private void BuildLights()
            {
                var pitch = Mathf.Max(2, _settings.lightSpacing);
                var offset = pitch / 2;
                var panelY = _settings.ceilingHeight - 0.03f;

                for (var z = offset; z < _size; z += pitch)
                {
                    for (var x = offset; x < _size; x += pitch)
                    {
                        if (_solid[x, z]) continue;

                        var centre = CellToWorld(x + 0.5f, z + 0.5f);

                        _blueprint.Add($"Light {x} {z}",
                            new Vector3(centre.x, panelY, centre.z),
                            new Vector3(1.4f, 0.06f, 1.4f),
                            MapSurface.LightPanel, false);

                        _blueprint.Lights.Add(new Vector3(centre.x, panelY - 0.3f, centre.z));
                    }
                }
            }

            // ---- grid --------------------------------------------------------------------

            /// <summary>
            /// One storey of Standable flags. FreeFloor is deliberately absent — the export fills
            /// it in with the server's own collision and player box, because that flag means "the
            /// server can stand a player here", not "the generator thinks it is open".
            /// </summary>
            private void BuildGrid()
            {
                var cells = new byte[_size * _size];

                for (var z = 0; z < _size; z++)
                    for (var x = 0; x < _size; x++)
                        if (!_solid[x, z])
                            cells[z * _size + x] = (byte)MapCellFlags.Standable;

                _blueprint.Grid = new MapGridData
                {
                    Floors = 1,
                    Width = _size,
                    Depth = _size,
                    CellSize = _settings.cellSize,
                    FloorHeight = _settings.floorHeight,
                    OriginX = Origin,
                    OriginZ = Origin,
                    Cells = cells,
                };
            }

            // ---- helpers -----------------------------------------------------------------

            private float Origin => _settings.Origin;

            private Vector3 CellToWorld(float cellX, float cellZ)
            {
                return new Vector3(
                    Origin + cellX * _settings.cellSize,
                    0f,
                    Origin + cellZ * _settings.cellSize);
            }

            private Vector2Int WorldToCell(Vector3 world)
            {
                return new Vector2Int(
                    Mathf.Clamp(Mathf.FloorToInt((world.x - Origin) / _settings.cellSize), 0, _size - 1),
                    Mathf.Clamp(Mathf.FloorToInt((world.z - Origin) / _settings.cellSize), 0, _size - 1));
            }

            private Zone ZoneAt(Vector2Int cell)
            {
                foreach (var zone in _zones)
                    if (zone.Cells.Contains(cell)) return zone;

                return null;
            }
        }
    }
}
