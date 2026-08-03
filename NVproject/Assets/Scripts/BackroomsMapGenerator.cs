using System.Collections.Generic;
using NV.Client.Net;
using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// A two-floor Backrooms level, generated from a seed and built entirely in code.
///
/// This replaces the single-floor <see cref="BackroomsMap"/>. The shape of the level comes
/// from the backrooms-map-generator skill's contract: a square grid of rooms and corridors on
/// two stacked floors, joined by a stairwell that occupies the *same cells* on both floors so
/// the stairs line up vertically. Spawn, exit and stairwell are hand-authored rectangles that
/// never move; everything else reshuffles with the seed.
///
/// **Determinism is not cosmetic here.** The collision boxes this produces are hashed and
/// compared against the game server's copy of the map (<see cref="INetworkMapSource"/>), so a
/// single stray <c>UnityEngine.Random</c> call would make client and server disagree about
/// where the walls are. Every procedural choice draws from one seeded <see cref="System.Random"/>,
/// and <see cref="ComputeCollision"/> replays the draws in exactly the order
/// <see cref="Generate"/> does.
///
/// Layout is solved completely in memory before a single GameObject exists. Placing geometry
/// first and fixing connectivity afterwards leaves orphaned tiles behind and produces maps the
/// player cannot finish.
/// </summary>
public class BackroomsMapGenerator : MonoBehaviour, INetworkMapSource
{
    [Header("Footprint")]
    [Tooltip("Cells per side. 35 x 35 at 3 m is a ~105 m square.")]
    public int gridSize = 35;
    [Tooltip("World units per cell. This is also the corridor width.")]
    public float cellSize = 3f;
    [Tooltip("Stacked floors. The stairwell joins floor 0 to floor 1.")]
    public int floors = 2;
    [Tooltip("Vertical gap between floors. Ceiling height is this minus 0.2.")]
    public float floorHeight = 3.2f;
    public float wallThickness = 0.25f;

    [Header("Layout")]
    public int seed = 0;
    [Tooltip("Fresh layout every run. OFF by default in this project: the collision boxes are " +
             "hashed against the server's copy of the map, and a seed that changes per run makes " +
             "the two disagree on the first connection.")]
    public bool randomizeSeed = false;
    [Tooltip("Random rooms attempted per floor. Overlapping candidates are rejected.")]
    public int roomAttempts = 22;
    public int roomMin = 3;
    public int roomMax = 8;
    [Range(1, 3)] public int corridorWidth = 1;
    [Tooltip("Chance of an extra corridor beyond the spanning set. Zero gives a pure tree, " +
             "which reads as a puzzle to solve rather than as being lost.")]
    [Range(0f, 0.6f)] public float loopChance = 0.15f;

    [Header("Fixed anchors (grid cells — edit these to move what never moves)")]
    public RectInt spawnRoom = new RectInt(3, 3, 6, 6);
    public RectInt exitRoom = new RectInt(26, 26, 6, 6);
    [Tooltip("Stamped identically on both floors so the stairs line up.")]
    public RectInt stairwell = new RectInt(16, 15, 3, 5);
    [Tooltip("Steps in the flight. 16 over 9 m of run gives a 0.2 m rise — well inside the " +
             "CharacterController's step offset.")]
    public int stairSteps = 16;

    [Header("Lighting")]
    [Tooltip("A fluorescent panel every N cells, so the lighting reads as an office grid.")]
    public int lightSpacing = 3;
    public float lightIntensity = 1.6f;
    [Tooltip("Fraction of lights that buzz and flicker. Occasional is eerier than constant.")]
    [Range(0f, 0.4f)] public float flickerFraction = 0.18f;

    [Header("Mood — values from the aesthetic spec")]
    public Color wallColor = new Color32(0xC9, 0xB3, 0x6B, 0xFF);
    public Color trimColor = new Color32(0xA8, 0x92, 0x4E, 0xFF);
    public Color carpetColor = new Color32(0x8A, 0x7F, 0x52, 0xFF);
    public Color ceilingColor = new Color32(0xD8, 0xCF, 0xA8, 0xFF);
    public Color lightColor = new Color32(0xFF, 0xF6, 0xD6, 0xFF);
    public Color fogColor = new Color32(0xB7, 0xAC, 0x7E, 0xFF);
    public float fogDensity = 0.022f;
    [Tooltip("Low fluorescent/HVAC drone, generated in code. This one sound does a lot of work.")]
    public bool ambientHum = true;
    [Range(0f, 1f)] public float humVolume = 0.35f;

    [Header("Actors")]
    public Transform player;
    public Transform mirror;
    public Transform mirrorFrame;
    [Tooltip("Bake a NavMesh over both floors and the stairs after building.")]
    public bool bakeNavMesh = true;

    // --- grid ---
    private enum Cell : byte { Solid = 0, Room = 1, Corridor = 2, Anchor = 3 }

    private Cell[][,] _cell;          // [floor][x, z]
    private bool[][,] _protected;     // anchors procedural passes must not overwrite
    private readonly List<RectInt>[] _rooms = new List<RectInt>[2];

    private const string RootName = "__BackroomsMap";
    private Transform _root;
    private bool _collisionOnly;

    private readonly List<Bounds> _collisionBoxes = new List<Bounds>();
    private readonly List<Light> _flickerLights = new List<Light>();
    private readonly List<float> _flickerPhase = new List<float>();

    private BoxCollider _ceilingLid;
    private Material _wallMaterial, _carpetMaterial, _ceilingMaterial, _lightMaterial, _trimMaterial;
    private float _originX, _originZ;

    public int WallPieces { get; private set; }
    public int LightCount { get; private set; }
    public int UsedSeed { get; private set; }

    /// <inheritdoc />
    ///
    /// This name is load-bearing in three places at once and they all have to agree:
    /// it is the export filename (<c>MapData/backrooms.json</c>), the key the server
    /// registers the map under (<c>Game:Maps</c>), and what
    /// <c>SessionSceneRouter.SceneByMap</c> looks up to decide that this scene is the
    /// one to open. Two of those already said "backrooms" while this said
    /// "backrooms2f", so a room made through the lobby opened this scene, built this
    /// terrain, and was judged against a stale export of a level that no longer
    /// exists — a guaranteed map-hash mismatch on every connect, and the only symptom
    /// was the mismatch warning itself.
    ///
    /// Renaming here rather than there is what costs least: the router table and the
    /// server's default map entry are both already "backrooms".
    ///
    /// Changing this string changes the export's destination file. Re-run
    /// **Tools ▸ NV ▸ Map ▸ Export Map Collision** after touching it.
    public string MapName => "backrooms";

    /// <inheritdoc />
    public IReadOnlyList<Bounds> CollisionBoxes => _collisionBoxes;

    private float FloorY(int floor) => floor * floorHeight;
    private float CeilingHeight => floorHeight - 0.2f;

    private Vector3 CellCentre(int floor, int x, int z) => new Vector3(
        _originX + (x + 0.5f) * cellSize, FloorY(floor), _originZ + (z + 0.5f) * cellSize);

    private void Awake()
    {
        Generate();
    }

    // ================================================================ entry points

    public void Generate()
    {
        UsedSeed = randomizeSeed ? new System.Random().Next() : seed;
        var random = new System.Random(UsedSeed);

        Prepare();
        SolveGrid(random);

        BuildGeometry();
        BuildLights(random);
        ApplyAtmosphere();
        PlaceActors();
        if (bakeNavMesh) BakeNavMesh();
    }

    /// <summary>
    /// Solves the layout and records collision without building anything. The editor exporter
    /// needs the box list in edit mode, where dumping a whole level into the open scene is not
    /// acceptable. It draws from the seeded random in exactly the order <see cref="Generate"/>
    /// does and stops before the lighting pass, which touches no collider.
    /// </summary>
    public IReadOnlyList<Bounds> ComputeCollision()
    {
        UsedSeed = randomizeSeed ? new System.Random().Next() : seed;
        var random = new System.Random(UsedSeed);

        Prepare();
        SolveGrid(random);

        _collisionOnly = true;
        try { BuildGeometry(); }
        finally { _collisionOnly = false; }

        return _collisionBoxes;
    }

    /// <inheritdoc />
    public void GetSpawns(List<(Vector3 position, float yaw)> into)
    {
        // Matches the server's Room.MaxPlayers.
        const int spawnCount = 8;

        Vector3 centre = RectCentre(0, spawnRoom);

        for (int x = spawnRoom.x; x < spawnRoom.xMax && into.Count < spawnCount; x++)
        for (int z = spawnRoom.y; z < spawnRoom.yMax && into.Count < spawnCount; z++)
        {
            // A ring inside the room, not the whole room, or eight players stand shoulder to
            // shoulder in one corner.
            bool onRing = x == spawnRoom.x || x == spawnRoom.xMax - 1
                       || z == spawnRoom.y || z == spawnRoom.yMax - 1;
            if (!onRing) continue;

            Vector3 cell = CellCentre(0, x, z);
            var toCentre = new Vector3(centre.x - cell.x, 0f, centre.z - cell.z);

            // Yaw 0 is +Z, the same convention the server's move function uses.
            float yaw = toCentre.sqrMagnitude > 1e-4f ? Mathf.Atan2(toCentre.x, toCentre.z) : 0f;
            into.Add((new Vector3(cell.x, FloorY(0), cell.z), yaw));
        }
    }

    // ================================================================ level queries
    //
    // The match layer needs to ask the level questions the renderer never had to: where can a key
    // sit, where does a shot Runner land, is that point inside a wall. All of it comes off the
    // solved grid rather than off the colliders — the grid already knows, and a physics sweep for
    // "somewhere valid, anywhere" would be both slower and vaguer.

    public int GridSize => gridSize;
    public int FloorCount => Mathf.Max(1, floors);
    public float CellSize => cellSize;
    public float FloorSpacing => floorHeight;

    /// <summary>True once <see cref="Generate"/> has solved a grid. Nothing below means anything before that.</summary>
    public bool HasGrid => _cell != null;

    /// <summary>
    /// Re-derives the grid if it has gone missing, without rebuilding a single GameObject.
    ///
    /// The grid is a plain C# array, so a domain reload — which a script edit triggers mid-play,
    /// without exiting play mode — wipes it while the level's geometry, being made of
    /// UnityEngine.Objects, survives intact. Anything that then asks the level a question gets
    /// "there is no level". Re-solving from the same seed reproduces exactly the grid the standing
    /// geometry was built from, because every procedural draw comes from that one seeded sequence.
    /// </summary>
    /// <remarks>
    /// Every public query below calls this first, so a caller can never be handed "there is no
    /// level" by a reload it had no way to know about. The symptoms were quiet ones — a door
    /// compass with no storey arrow, a blank map overlay — not exceptions.
    /// </remarks>
    public void EnsureGrid()
    {
        if (HasGrid) return;

        _originX = -gridSize * cellSize * 0.5f;
        _originZ = -gridSize * cellSize * 0.5f;
        SolveGrid(new System.Random(UsedSeed != 0 ? UsedSeed : seed));
    }

    /// <summary>Floor level of a storey in world Y.</summary>
    public float FloorLevel(int floor) => FloorY(floor);

    /// <summary>Centre of the spawn room, on the ground floor.</summary>
    public Vector3 SpawnCentre => RectCentre(0, spawnRoom);

    /// <summary>Centre of the exit room, which is on the top floor — so it needs the stairs first.</summary>
    public Vector3 ExitCentre => RectCentre(FloorCount - 1, exitRoom);

    /// <summary>
    /// Can something stand here? Solid cells are wall; the upper storey's stairwell shaft is
    /// walkable in the grid but has no floor built over it, so anything dropped there falls
    /// through to the storey below.
    /// </summary>
    public bool IsStandable(int floor, int x, int z)
    {
        EnsureGrid();
        if (!HasGrid || floor < 0 || floor >= _cell.Length) return false;
        if (!Walkable(floor, x, z)) return false;
        return !(floor > 0 && InRect(stairwell, x, z) && IsShaftCell(z));
    }

    /// <summary>Floor-level world position at the centre of a cell.</summary>
    public Vector3 CellToWorld(int floor, int x, int z) => CellCentre(floor, x, z);

    /// <summary>
    /// Which storey a world height belongs to. The storey you are on is the one whose floor is
    /// *below* you, so this floors the division rather than rounding it.
    ///
    /// Rounding to the nearest storey was a real bug: the jump apex is 1.2 m and the storeys are
    /// 3.2 m apart, so the nearest floor level to a jumping player is the one overhead, and the
    /// map overlay flicked them up to the floor above every time they pressed space.
    ///
    /// The tolerance is deliberately one-sided. A little *below* the floor level is still this
    /// storey — a CharacterController rests its skin width low, and a stair lip is lower still —
    /// but any height above it is this storey until the next floor is actually reached.
    /// </summary>
    public int FloorIndexAt(float worldY)
    {
        float spacing = Mathf.Max(0.01f, floorHeight);
        return Mathf.Clamp(Mathf.FloorToInt((worldY + 0.35f) / spacing), 0, FloorCount - 1);
    }

    /// <summary>Nearest cell to a world position, whether or not that cell is standable.</summary>
    public bool TryWorldToCell(Vector3 world, out int floor, out int x, out int z)
    {
        floor = FloorIndexAt(world.y);
        x = Mathf.FloorToInt((world.x - _originX) / cellSize);
        z = Mathf.FloorToInt((world.z - _originZ) / cellSize);
        return x >= 0 && z >= 0 && x < gridSize && z < gridSize;
    }

    /// <summary>Every standable cell centre in the level, as (x, z, floor).</summary>
    public void CollectStandableCells(List<Vector3Int> into)
    {
        EnsureGrid();
        if (into == null || !HasGrid) return;
        for (int f = 0; f < FloorCount; f++)
        for (int x = 0; x < gridSize; x++)
        for (int z = 0; z < gridSize; z++)
            if (IsStandable(f, x, z)) into.Add(new Vector3Int(x, z, f));
    }

    /// <summary>
    /// A random standable point, jittered inside its cell so ten keys do not line up on a lattice.
    /// Drawn from a caller-owned <see cref="System.Random"/>: the match seed has to stay separate
    /// from the level seed, or moving a key would reshape the walls.
    /// </summary>
    public bool TryRandomPoint(System.Random random, out Vector3 point, float margin = 0.55f)
    {
        EnsureGrid();
        point = SpawnCentre;
        if (!HasGrid) return false;

        // Rejection sampling. Roughly half the grid is standable, so this lands in a couple of
        // draws — cheaper than materialising the whole cell list for one point.
        for (int attempt = 0; attempt < 512; attempt++)
        {
            int f = random.Next(FloorCount);
            int x = random.Next(gridSize);
            int z = random.Next(gridSize);
            if (!IsStandable(f, x, z)) continue;

            Vector3 centre = CellCentre(f, x, z);
            float spread = Mathf.Max(0f, cellSize * 0.5f - margin);
            point = new Vector3(
                centre.x + (float)(random.NextDouble() * 2.0 - 1.0) * spread,
                centre.y,
                centre.z + (float)(random.NextDouble() * 2.0 - 1.0) * spread);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Nearest standable point to somewhere that may be inside a wall. This is what the "teleport
    /// landed in an invalid cell" edge case rerolls to.
    /// </summary>
    public bool TryNearestStandablePoint(Vector3 near, out Vector3 point)
    {
        EnsureGrid();
        point = near;
        if (!HasGrid || !TryWorldToCell(near, out int floor, out int cx, out int cz)) return false;

        for (int radius = 0; radius < gridSize; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != radius) continue;   // ring only
                int x = cx + dx, z = cz + dz;
                if (!IsStandable(floor, x, z)) continue;
                point = CellCentre(floor, x, z);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Fades the walls out for the freeze device's x-ray. One shared material drives every wall in
    /// the level, so this is a single material switch rather than a pass over 1300 renderers —
    /// which also means it cannot be done per-player, and does not need to be: the freeze is
    /// global by rule.
    /// </summary>
    public void SetWallTransparency(float alpha)
    {
        if (_wallMaterial == null) return;
        bool transparent = alpha < 0.999f;

        _wallMaterial.SetFloat("_Surface", transparent ? 1f : 0f);
        _wallMaterial.SetInt("_SrcBlend", (int)(transparent
            ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.One));
        _wallMaterial.SetInt("_DstBlend", (int)(transparent
            ? UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : UnityEngine.Rendering.BlendMode.Zero));
        _wallMaterial.SetInt("_ZWrite", transparent ? 0 : 1);
        _wallMaterial.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");

        if (transparent) _wallMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        else _wallMaterial.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");

        _wallMaterial.renderQueue = (int)(transparent
            ? UnityEngine.Rendering.RenderQueue.Transparent
            : UnityEngine.Rendering.RenderQueue.Geometry);

        Color c = wallColor;
        c.a = Mathf.Clamp01(alpha);
        _wallMaterial.color = c;
    }

    private Vector3 RectCentre(int floor, RectInt rect)
    {
        Vector3 near = CellCentre(floor, rect.x, rect.y);
        Vector3 far = CellCentre(floor, rect.xMax - 1, rect.yMax - 1);
        return new Vector3((near.x + far.x) * 0.5f, FloorY(floor), (near.z + far.z) * 0.5f);
    }

    private void Prepare()
    {
        _originX = -gridSize * cellSize * 0.5f;
        _originZ = -gridSize * cellSize * 0.5f;
        _collisionBoxes.Clear();
        _flickerLights.Clear();
        _flickerPhase.Clear();
        WallPieces = 0;
        LightCount = 0;
    }

    // ================================================================ 1. grid

    private void SolveGrid(System.Random random)
    {
        int floorCount = Mathf.Max(1, floors);
        _cell = new Cell[floorCount][,];
        _protected = new bool[floorCount][,];

        for (int f = 0; f < floorCount; f++)
        {
            _cell[f] = new Cell[gridSize, gridSize];
            _protected[f] = new bool[gridSize, gridSize];
            _rooms[f] = new List<RectInt>();
        }

        StampAnchors();

        for (int f = 0; f < floorCount; f++)
        {
            CarveRooms(f, random);
            ConnectRooms(f, random);
            WireStairwell(f);
        }

        EnforceConnectivity();
    }

    /// <summary>
    /// The parts that never move. Stamped before anything procedural and marked protected, so a
    /// random room can never eat the spawn or seal the stairs. The stairwell goes down on every
    /// floor at identical coordinates — that is what makes the flights line up.
    /// </summary>
    private void StampAnchors()
    {
        Stamp(0, spawnRoom);
        Stamp(Mathf.Min(1, floors - 1), exitRoom);

        for (int f = 0; f < floors; f++) Stamp(f, stairwell);

        // On the lower floor the stairwell's last row is the underside of the upper landing.
        // Left walkable it becomes a three-cell pocket with a lid on it: floor and headroom, but
        // the flight rises to 3.2 m in front of it and the stairwell wall closes the rest, so
        // nothing can ever reach it. Making it solid seals it and lets the wall pass build a face.
        int underLanding = stairwell.yMax - 1;
        for (int x = stairwell.x; x < stairwell.xMax; x++)
            if (InGrid(x, underLanding)) _cell[0][x, underLanding] = Cell.Solid;
    }

    private void Stamp(int floor, RectInt rect)
    {
        for (int x = rect.x; x < rect.xMax; x++)
        for (int z = rect.y; z < rect.yMax; z++)
        {
            if (!InGrid(x, z)) continue;
            _cell[floor][x, z] = Cell.Anchor;
            _protected[floor][x, z] = true;
        }
        _rooms[floor].Add(rect);
    }

    private bool InGrid(int x, int z) => x >= 1 && z >= 1 && x < gridSize - 1 && z < gridSize - 1;

    private void CarveRooms(int floor, System.Random random)
    {
        for (int attempt = 0; attempt < roomAttempts; attempt++)
        {
            int w = random.Next(roomMin, roomMax + 1);
            int h = random.Next(roomMin, roomMax + 1);
            int x = random.Next(1, Mathf.Max(2, gridSize - w - 1));
            int z = random.Next(1, Mathf.Max(2, gridSize - h - 1));
            var candidate = new RectInt(x, z, w, h);

            // Keep a cell of separation so rooms read as distinct spaces rather than one blob.
            var padded = new RectInt(x - 1, z - 1, w + 2, h + 2);
            bool collides = false;
            foreach (RectInt existing in _rooms[floor])
                if (padded.Overlaps(existing)) { collides = true; break; }
            if (collides) continue;

            for (int cx = candidate.x; cx < candidate.xMax; cx++)
            for (int cz = candidate.y; cz < candidate.yMax; cz++)
                if (InGrid(cx, cz) && !_protected[floor][cx, cz])
                    _cell[floor][cx, cz] = Cell.Room;

            _rooms[floor].Add(candidate);
        }
    }

    /// <summary>
    /// Chains every room into one graph with L-shaped corridors, then adds a few extra edges.
    /// The chain is what guarantees a connected floor; the extras are what stop it being a tree,
    /// because a level with exactly one route between any two points reads as a maze puzzle.
    /// </summary>
    private void ConnectRooms(int floor, System.Random random)
    {
        List<RectInt> rooms = _rooms[floor];
        if (rooms.Count < 2) return;

        for (int i = 1; i < rooms.Count; i++)
            CarveCorridor(floor, RoomCell(rooms[i - 1]), RoomCell(rooms[i]), random);

        for (int i = 0; i < rooms.Count; i++)
        {
            if (random.NextDouble() >= loopChance) continue;
            int other = random.Next(rooms.Count);
            if (other == i) continue;
            CarveCorridor(floor, RoomCell(rooms[i]), RoomCell(rooms[other]), random);
        }
    }

    private static Vector2Int RoomCell(RectInt rect) =>
        new Vector2Int(rect.x + rect.width / 2, rect.y + rect.height / 2);

    /// <summary>
    /// A single L: one leg along X, one along Z, meeting at the elbow. Which leg comes first is
    /// a coin flip so corridors do not all turn the same way.
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
        int step = a <= b ? 1 : -1;
        for (int v = a; v != b + step; v += step)
        for (int w = 0; w < corridorWidth; w++)
        {
            int x = alongX ? v : fixedCoord + w;
            int z = alongX ? fixedCoord + w : v;
            if (!InGrid(x, z) || _protected[floor][x, z]) continue;
            if (_cell[floor][x, z] == Cell.Solid) _cell[floor][x, z] = Cell.Corridor;
        }
    }

    /// <summary>
    /// Runs a corridor from the stairwell to the nearest carved space on this floor.
    ///
    /// **Which cell it joins matters.** The flight fills the stairwell as a rising wedge, so on
    /// the lower floor the only place you can step onto it is the bottom row, and on the upper
    /// floor the only place you can step off is the landing row. Joining the corridor to the
    /// stairwell's centre — the obvious thing — puts the doorway halfway up a 1.6 m wall of
    /// steps, and the upper floor becomes unreachable while still looking connected on the grid.
    /// </summary>
    private void WireStairwell(int floor)
    {
        int entryZ = floor == 0 ? stairwell.y : stairwell.yMax - 1;
        var stairCell = new Vector2Int(stairwell.x + stairwell.width / 2, entryZ);
        Vector2Int nearest = stairCell;
        int best = int.MaxValue;

        for (int x = 1; x < gridSize - 1; x++)
        for (int z = 1; z < gridSize - 1; z++)
        {
            if (_cell[floor][x, z] == Cell.Solid) continue;
            if (_protected[floor][x, z]) continue;
            int distance = Mathf.Abs(x - stairCell.x) + Mathf.Abs(z - stairCell.y);
            if (distance < best) { best = distance; nearest = new Vector2Int(x, z); }
        }

        if (best == int.MaxValue) return;
        CarveLine(floor, stairCell.x, nearest.x, stairCell.y, true);
        CarveLine(floor, stairCell.y, nearest.y, nearest.x, false);
    }

    /// <summary>
    /// Flood-fills from the spawn across BOTH floors — the stairwell is a vertical edge — and
    /// carves a connector to anything stranded, repeating until the whole level is reachable.
    /// Without this the generator happily produces pretty maps with rooms you cannot get to.
    /// </summary>
    private void EnforceConnectivity()
    {
        for (int pass = 0; pass < 12; pass++)
        {
            bool[][,] seen = FloodFromSpawn();

            Vector3Int stranded = FindStranded(seen);
            if (stranded.x < 0) return;                 // everything reachable

            // Join the stranded cell to the nearest reachable cell on its own floor.
            Vector3Int anchor = NearestReachable(seen, stranded);
            if (anchor.x < 0) return;

            CarveLine(stranded.z, stranded.x, anchor.x, stranded.y, true);
            CarveLine(stranded.z, stranded.y, anchor.y, anchor.x, false);
        }
    }

    private bool[][,] FloodFromSpawn()
    {
        var seen = new bool[floors][,];
        for (int f = 0; f < floors; f++) seen[f] = new bool[gridSize, gridSize];

        var queue = new Queue<Vector3Int>();
        Vector2Int start = RoomCell(spawnRoom);
        seen[0][start.x, start.y] = true;
        queue.Enqueue(new Vector3Int(start.x, start.y, 0));

        var steps = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

        while (queue.Count > 0)
        {
            Vector3Int c = queue.Dequeue();
            foreach (Vector2Int s in steps)
            {
                int nx = c.x + s.x, nz = c.y + s.y;
                if (nx < 0 || nz < 0 || nx >= gridSize || nz >= gridSize) continue;
                if (seen[c.z][nx, nz] || _cell[c.z][nx, nz] == Cell.Solid) continue;
                seen[c.z][nx, nz] = true;
                queue.Enqueue(new Vector3Int(nx, nz, c.z));
            }

            // The stairwell is the only way between floors, so it is the only vertical edge.
            if (!InRect(stairwell, c.x, c.y)) continue;
            for (int f = 0; f < floors; f++)
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
        for (int f = 0; f < floors; f++)
        for (int x = 1; x < gridSize - 1; x++)
        for (int z = 1; z < gridSize - 1; z++)
            if (_cell[f][x, z] != Cell.Solid && !seen[f][x, z])
                return new Vector3Int(x, z, f);
        return new Vector3Int(-1, -1, -1);
    }

    private Vector3Int NearestReachable(bool[][,] seen, Vector3Int from)
    {
        int best = int.MaxValue;
        var found = new Vector3Int(-1, -1, -1);
        for (int x = 1; x < gridSize - 1; x++)
        for (int z = 1; z < gridSize - 1; z++)
        {
            if (!seen[from.z][x, z]) continue;
            int distance = Mathf.Abs(x - from.x) + Mathf.Abs(z - from.y);
            if (distance < best) { best = distance; found = new Vector3Int(x, z, from.z); }
        }
        return found;
    }

    private static bool InRect(RectInt rect, int x, int z) =>
        x >= rect.x && x < rect.xMax && z >= rect.y && z < rect.yMax;

    private bool Walkable(int floor, int x, int z) =>
        x >= 0 && z >= 0 && x < gridSize && z < gridSize && _cell[floor][x, z] != Cell.Solid;

    // ================================================================ 2. geometry

    private void BuildGeometry()
    {
        if (!_collisionOnly)
        {
            ClearRoot();
            EnsureMaterials();
            _root = new GameObject(RootName).transform;
            _root.SetParent(transform, false);
        }

        for (int f = 0; f < floors; f++)
        {
            BuildTiles(f);
            BuildWalls(f);
        }
        BuildStairs();
        BuildCeilingLid();
    }

    /// <summary>
    /// One invisible slab across the top storey, level with its ceiling.
    ///
    /// Ceiling tiles carry no collider — deliberately, since a grid of them would be a thousand
    /// colliders and they never need to stop anything from below. That holds on every floor but
    /// the last: the storey above provides the barrier, because its carpet slab *is* solid. The
    /// top floor has nothing above it, so a player who climbs onto a device console (1 m) and
    /// jumps (1.2 m) puts their eyes at 7.0 m against a 6.2 m ceiling and sees straight out of
    /// the level.
    ///
    /// One box the size of the grid fixes it for the cost of a single collider. It is switched off
    /// across the NavMesh bake — see <see cref="BakeNavMesh"/> — or the bots get a floor on the roof.
    /// </summary>
    private void BuildCeilingLid()
    {
        float span = gridSize * cellSize;
        float y = FloorY(FloorCount - 1) + CeilingHeight;

        // Sits *on* the ceiling plane rather than through it, so it takes no headroom away.
        var centre = new Vector3(_originX + span * 0.5f, y + 0.1f, _originZ + span * 0.5f);
        var size = new Vector3(span, 0.2f, span);

        _collisionBoxes.Add(new Bounds(centre, size));
        if (_collisionOnly) return;

        var lid = new GameObject("Ceiling Lid");
        lid.transform.SetParent(_root, false);
        lid.transform.localPosition = centre;

        _ceilingLid = lid.AddComponent<BoxCollider>();
        _ceilingLid.size = size;
    }

    /// <summary>
    /// Destroys the previous build in one go. Without this every regeneration stacks another
    /// whole level on top of the last one.
    /// </summary>
    private void ClearRoot()
    {
        Transform existing = transform.Find(RootName);
        if (existing == null) return;
        if (Application.isPlaying) Destroy(existing.gameObject);
        else DestroyImmediate(existing.gameObject);
    }

    private void BuildTiles(int floor)
    {
        float y = FloorY(floor);

        for (int x = 0; x < gridSize; x++)
        for (int z = 0; z < gridSize; z++)
        {
            if (!Walkable(floor, x, z)) continue;
            Vector3 centre = CellCentre(floor, x, z);
            bool overShaft = InRect(stairwell, x, z) && IsShaftCell(z);

            // Floor. Skipped where the stairwell shaft passes through this floor, or the upper
            // storey would be a lid over its own staircase.
            if (!(floor > 0 && overShaft))
                AddBox("Carpet", new Vector3(centre.x, y - 0.1f, centre.z),
                    new Vector3(cellSize, 0.2f, cellSize), _carpetMaterial, true);

            // Ceiling. Skipped over the whole stairwell on the lower floor — that hole is what
            // lets the player actually walk up rather than meeting a ceiling halfway.
            bool topFloor = floor == floors - 1;
            bool underShaft = !topFloor && InRect(stairwell, x, z);
            if (!underShaft)
                AddBox("Ceiling Tile", new Vector3(centre.x, y + CeilingHeight, centre.z),
                    new Vector3(cellSize - 0.06f, 0.12f, cellSize - 0.06f), _ceilingMaterial, false);
        }
    }

    /// <summary>The stairwell cells the flight itself passes through; the last row is the landing.</summary>
    private bool IsShaftCell(int z) => z < stairwell.yMax - 1;

    /// <summary>
    /// One wall on every boundary where a walkable cell meets a solid one, merged into runs so a
    /// ten-cell corridor wall is one box with one collider instead of ten.
    /// </summary>
    private void BuildWalls(int floor)
    {
        float y = FloorY(floor);
        float height = CeilingHeight;

        // Boundaries perpendicular to X, i.e. running along Z.
        for (int x = 0; x <= gridSize; x++)
        {
            int runStart = -1;
            for (int z = 0; z <= gridSize; z++)
            {
                bool wall = z < gridSize && Walkable(floor, x - 1, z) != Walkable(floor, x, z);
                if (wall && runStart < 0) runStart = z;
                if (wall || runStart < 0) continue;

                int count = z - runStart;
                AddBox("Wall Z",
                    new Vector3(_originX + x * cellSize, y + height * 0.5f,
                                _originZ + (runStart + count * 0.5f) * cellSize),
                    new Vector3(wallThickness, height, count * cellSize + wallThickness),
                    _wallMaterial, true);
                WallPieces++;
                runStart = -1;
            }
        }

        // Boundaries perpendicular to Z, running along X.
        for (int z = 0; z <= gridSize; z++)
        {
            int runStart = -1;
            for (int x = 0; x <= gridSize; x++)
            {
                bool wall = x < gridSize && Walkable(floor, x, z - 1) != Walkable(floor, x, z);
                if (wall && runStart < 0) runStart = x;
                if (wall || runStart < 0) continue;

                int count = x - runStart;
                AddBox("Wall X",
                    new Vector3(_originX + (runStart + count * 0.5f) * cellSize, y + height * 0.5f,
                                _originZ + z * cellSize),
                    new Vector3(count * cellSize + wallThickness, height, wallThickness),
                    _wallMaterial, true);
                WallPieces++;
                runStart = -1;
            }
        }
    }

    /// <summary>
    /// A straight flight filling the stairwell, from the lower floor up to the landing row. Each
    /// step is a box, so the stairs are collision the server sees exactly as the client does.
    /// </summary>
    private void BuildStairs()
    {
        if (floors < 2 || stairSteps < 1) return;

        int steps = Mathf.Max(1, stairSteps);

        // A landing at BOTH ends, not just the top. Running the flight right up to the stairwell
        // edge means the first cell's centre is already half a metre up the steps, so there is
        // nowhere flat to step on from the corridor and the upper floor is unreachable.
        float runCells = Mathf.Max(1, stairwell.height - 2);
        float totalRun = runCells * cellSize;
        float rise = floorHeight / steps;
        float tread = totalRun / steps;

        // Inset from the stairwell walls, and overlap each step into the one behind it.
        //
        // Both numbers exist to kill z-fighting. Running the flight to exactly the wall plane put
        // the step's side face in the same place as the wall's, and butting each step exactly
        // against the next put their end faces in the same place too — coincident coplanar faces
        // are what flickers. A couple of centimetres of overlap buries those faces inside solid
        // geometry, where they cannot be drawn at all.
        const float sideInset = 0.04f;
        const float stepOverlap = 0.03f;

        float width = stairwell.width * cellSize - sideInset * 2f;
        float centreX = _originX + (stairwell.x + stairwell.width * 0.5f) * cellSize;
        float startZ = _originZ + (stairwell.y + 1) * cellSize;

        for (int i = 0; i < steps; i++)
        {
            float top = (i + 1) * rise;

            // Grow backwards, into the shorter step behind, which hides this step's back face.
            float depth = tread + stepOverlap;
            float z = startZ + (i + 0.5f) * tread - stepOverlap * 0.5f;

            // Each step is a solid block from the floor up to its own tread, so there is no gap
            // to fall through and the server's box list needs no special case for stairs.
            AddBox("Step", new Vector3(centreX, top * 0.5f, z),
                new Vector3(width, top, depth), _trimMaterial, true);
        }
    }

    private void AddBox(string name, Vector3 centre, Vector3 size, Material material, bool collider)
    {
        if (collider) _collisionBoxes.Add(new Bounds(centre, size));
        if (_collisionOnly) return;

        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(_root, false);
        box.transform.localPosition = centre;
        box.transform.localScale = size;

        if (!collider)
        {
            var boxCollider = box.GetComponent<Collider>();
            if (boxCollider != null) Destroy(boxCollider);
        }
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    // ================================================================ 3. mood

    private void BuildLights(System.Random random)
    {
        var root = new GameObject("Ceiling Lights").transform;
        root.SetParent(_root, false);
        int step = Mathf.Max(1, lightSpacing);

        for (int f = 0; f < floors; f++)
        for (int x = step / 2; x < gridSize; x += step)
        for (int z = step / 2; z < gridSize; z += step)
        {
            if (!Walkable(f, x, z)) continue;
            if (f < floors - 1 && InRect(stairwell, x, z)) continue;   // no ceiling there to mount on

            Vector3 centre = CellCentre(f, x, z);
            float y = FloorY(f) + CeilingHeight;

            AddBox("Panel", new Vector3(centre.x, y - 0.07f, centre.z),
                new Vector3(cellSize * 0.42f, 0.06f, cellSize * 0.42f), _lightMaterial, false);

            var lightGo = new GameObject("Fluorescent");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.position = new Vector3(centre.x, y - 0.3f, centre.z);

            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = lightColor;
            light.range = cellSize * 2.5f;
            light.intensity = lightIntensity;
            // Shadows off: a hundred shadow-casting point lights is ruinous, and the reference
            // look is flat diffuse fluorescent light with barely a shadow in it anyway.
            light.shadows = LightShadows.None;
            LightCount++;

            if (random.NextDouble() < flickerFraction)
            {
                _flickerLights.Add(light);
                _flickerPhase.Add((float)random.NextDouble() * 10f);
            }
        }
    }

    private void ApplyAtmosphere()
    {
        RenderSettings.fog = true;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = fogDensity;

        // No sun indoors, and no skybox to leak blue into a yellow room.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.19f, 0.17f, 0.12f);
        RenderSettings.skybox = null;

        if (ambientHum) BuildHum();
    }

    /// <summary>
    /// A looping fluorescent/HVAC drone built in code, since this project ships no audio assets.
    /// Two low sines slightly detuned beat against each other, plus a little filtered noise for
    /// air. 2D, so it sits at a constant level wherever the player is.
    /// </summary>
    private void BuildHum()
    {
        const int sampleRate = 44100;
        const int seconds = 4;
        int sampleCount = sampleRate * seconds;
        var samples = new float[sampleCount];

        var noiseRandom = new System.Random(12345);
        float smoothedNoise = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float mains = Mathf.Sin(2f * Mathf.PI * 60f * t) * 0.5f;
            float harmonic = Mathf.Sin(2f * Mathf.PI * 121.5f * t) * 0.22f;
            float sub = Mathf.Sin(2f * Mathf.PI * 39f * t) * 0.18f;

            float white = (float)(noiseRandom.NextDouble() * 2.0 - 1.0);
            smoothedNoise = Mathf.Lerp(smoothedNoise, white, 0.02f);   // cheap low-pass = air, not hiss

            samples[i] = (mains + harmonic + sub + smoothedNoise * 0.6f) * 0.22f;
        }

        // Cross-fade the seam so the loop does not click every four seconds.
        int fade = sampleRate / 20;
        for (int i = 0; i < fade; i++)
        {
            float k = i / (float)fade;
            samples[i] = Mathf.Lerp(samples[sampleCount - fade + i], samples[i], k);
        }

        var clip = AudioClip.Create("Backrooms Hum", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);

        var humGo = new GameObject("Ambient Hum");
        humGo.transform.SetParent(_root, false);
        var source = humGo.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = humVolume;
        source.playOnAwake = true;
        source.Play();
    }

    private void Update()
    {
        for (int i = 0; i < _flickerLights.Count; i++)
        {
            Light light = _flickerLights[i];
            if (light == null) continue;

            float t = Time.time * 9f + _flickerPhase[i];
            float buzz = 0.84f + 0.16f * Mathf.Sin(t) * Mathf.Sin(t * 0.37f);
            if (Mathf.Sin(t * 0.11f) > 0.985f) buzz *= 0.2f;
            light.intensity = lightIntensity * buzz;
        }
    }

    // ================================================================ 4. finalise

    private void PlaceActors()
    {
        Vector3 spawnCentre = RectCentre(0, spawnRoom);

        if (player == null)
        {
            var found = GameObject.Find("Player");
            if (found != null) player = found.transform;
        }
        if (player != null)
        {
            var body = player.GetComponent<CharacterController>();
            float lift = body != null ? body.skinWidth + 0.02f : 0.1f;
            player.position = new Vector3(spawnCentre.x, FloorY(0) + lift, spawnCentre.z);
        }

        if (mirror != null)
        {
            float z = spawnCentre.z + cellSize * 0.9f;
            mirror.position = new Vector3(spawnCentre.x, 1.3f, z);
            mirror.rotation = Quaternion.identity;
            if (mirrorFrame != null)
            {
                mirrorFrame.position = new Vector3(spawnCentre.x, 1.3f, z + 0.06f);
                mirrorFrame.rotation = Quaternion.identity;
            }
        }

        // The exit is on the top floor, so reaching it means finding the stairs first.
        Vector3 exitCentre = RectCentre(floors - 1, exitRoom);
        var marker = new GameObject("Exit Marker");
        marker.transform.SetParent(_root, false);
        marker.transform.position = new Vector3(exitCentre.x, FloorY(floors - 1) + 1.1f, exitCentre.z);

        var glow = GameObject.CreatePrimitive(PrimitiveType.Cube);
        glow.name = "Exit Sign";
        Destroy(glow.GetComponent<Collider>());
        glow.transform.SetParent(marker.transform, false);
        glow.transform.localScale = new Vector3(1.2f, 0.5f, 0.1f);
        glow.GetComponent<MeshRenderer>().sharedMaterial = _lightMaterial;
    }

    private void BakeNavMesh()
    {
        var surface = GetComponent<NavMeshSurface>();
        if (surface == null) surface = gameObject.AddComponent<NavMeshSurface>();

        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;

        // The ceiling lid is a ceiling, not a floor, and a 105 x 105 m horizontal collider is
        // exactly the shape that bakes into a walkable roof. Measurement says it does not — the
        // navmesh tops out at 6.30 with or without it, and those vertices are the tops of the
        // top-floor walls, which have always been in there — but the lid is switched off across
        // the bake anyway, because relying on that is relying on a detail of the voxelizer.
        // (NavMeshModifier.ignoreFromBuild was tried first and did not take.)
        bool hadLid = _ceilingLid != null && _ceilingLid.enabled;
        if (hadLid) _ceilingLid.enabled = false;

        surface.BuildNavMesh();

        if (hadLid) _ceilingLid.enabled = true;
    }

    // ================================================================ materials

    private void EnsureMaterials()
    {
        _wallMaterial = MakeMaterial("Backrooms Wall", wallColor, 0.15f);
        _trimMaterial = MakeMaterial("Backrooms Trim", trimColor, 0.12f);
        _carpetMaterial = MakeMaterial("Backrooms Carpet", carpetColor, 0.05f);
        _ceilingMaterial = MakeMaterial("Backrooms Ceiling", ceilingColor, 0.1f);

        _lightMaterial = MakeMaterial("Backrooms Light Panel", lightColor, 0.2f);
        _lightMaterial.EnableKeyword("_EMISSION");
        _lightMaterial.SetColor("_EmissionColor", lightColor * 2.6f);
        _lightMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
    }

    private static Material MakeMaterial(string name, Color color, float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader) { name = name };
        material.color = color;
        // Nothing here is glossy. Reflections break the flat, airless feeling entirely.
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        material.enableInstancing = true;
        return material;
    }
}
