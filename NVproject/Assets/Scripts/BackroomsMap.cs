using System.Collections.Generic;
using NV.Client.Net;
using UnityEngine;

/// <summary>
/// Generates a large Backrooms-style level in code: yellowed wallpaper walls, damp carpet,
/// a low ceiling of humming fluorescent panels, and a maze that mixes tight corridors with
/// open rooms so it reads as endless office rather than as a puzzle.
///
/// Built at Awake like the rest of this project — nothing is authored in the scene, so the
/// whole level is a seed plus these numbers. <see cref="seed"/> makes it reproducible.
///
/// Layout comes in three passes, and all three matter to the feel:
///  1. A recursive-backtracker maze over the cell grid. This alone guarantees every cell is
///     reachable, which no amount of random wall placement does.
///  2. Rectangular rooms carved out of it. A pure maze is uniformly tight; the Backrooms look
///     needs the contrast between a corridor and a big empty room.
///  3. Random extra doorways (<see cref="loopChance"/>). A perfect maze has exactly one path
///     between any two points, which reads as a puzzle to be solved — loops read as *lost*.
///
/// Geometry is kept cheap by merging each straight run of wall into a single box, so a ten-cell
/// corridor wall is one object with one collider instead of ten. That typically cuts the object
/// count by about half.
/// </summary>
public class BackroomsMap : MonoBehaviour, INetworkMapSource
{
    [Header("Size")]
    [Tooltip("Cells across. 56 x 56 at 3.2 m per cell is roughly 180 m square.")]
    public int gridWidth = 56;
    public int gridHeight = 56;
    [Tooltip("Metres per cell — this *is* the corridor width. 3.2 m against a 3 m ceiling is " +
             "properly claustrophobic; 4 m already reads as a warehouse.")]
    public float cellSize = 3.2f;
    [Tooltip("Ceiling height. Backrooms ceilings are oppressively low; much above 3.2 stops feeling wrong.")]
    public float wallHeight = 3f;
    public float wallThickness = 0.25f;

    [Header("Layout")]
    public int seed = 1337;
    [Tooltip("How many open rooms to carve out of the maze. Keep this restrained — rooms are the " +
             "contrast, not the substance, and too many turn the level into one big hall.")]
    public int roomCount = 18;
    public int roomMinCells = 3;
    public int roomMaxCells = 7;
    [Tooltip("Chance of knocking through any given interior wall to make a loop. " +
             "0 gives a perfect maze, which feels like a puzzle instead of feeling lost. " +
             "Note a spanning-tree maze is already ~51% open by neighbour-link count, so this " +
             "and roomCount are what push it above that — around 57% keeps it corridor-heavy.")]
    [Range(0f, 0.5f)] public float loopChance = 0.08f;

    [Header("Lighting")]
    [Tooltip("Place a ceiling light panel every N cells.")]
    public int panelSpacing = 3;
    [Tooltip("Place an actual point light every N cells. Panels are nearly free; lights are not.")]
    public int lightSpacing = 5;
    public float lightRange = 11f;
    public float lightIntensity = 2.2f;
    [Tooltip("Fraction of lights that buzz and flicker.")]
    [Range(0f, 0.3f)] public float flickerFraction = 0.07f;

    [Header("Look")]
    public Color wallColor = new Color(0.80f, 0.72f, 0.38f);
    public Color floorColor = new Color(0.50f, 0.43f, 0.28f);
    public Color ceilingColor = new Color(0.76f, 0.73f, 0.60f);
    public Color lightColor = new Color(1f, 0.96f, 0.78f);
    public Color fogColor = new Color(0.36f, 0.33f, 0.20f);
    public float fogDensity = 0.028f;

    [Header("Materials (created at runtime if empty)")]
    public Material wallMaterial;
    public Material floorMaterial;
    public Material ceilingMaterial;
    public Material lightMaterial;

    [Header("Spawn")]
    [Tooltip("Player to drop into the spawn room. Found by name if left empty.")]
    public Transform player;
    [Tooltip("Mirror to stand against the spawn room wall, so you can still see your own character.")]
    public Transform mirror;
    public Transform mirrorFrame;

    // Wall grids. southWall[x, z] is the wall on the -Z face of cell (x, z), so it needs one
    // extra row; westWall[x, z] is the -X face and needs one extra column.
    private bool[,] _southWall;
    private bool[,] _westWall;

    private readonly List<Light> _flickerLights = new List<Light>();
    private readonly List<float> _flickerPhase = new List<float>();
    private readonly List<Bounds> _collisionBoxes = new List<Bounds>();
    private Vector2Int _spawnCell;
    private float _originX, _originZ;

    /// <summary>Number of wall boxes actually built, after run merging.</summary>
    public int WallPieces { get; private set; }
    public int LightCount { get; private set; }

    /// <summary>
    /// Every box that carries a collider, in build order. This is what the game server judges
    /// movement against, so it has to be the same list the player is walking into rather than a
    /// second description of the level — the two drifting apart is what makes a player stick to
    /// nothing, or walk through a wall that is plainly there.
    ///
    /// Light panels are absent because they carry no collider: they must not block shots.
    /// </summary>
    public IReadOnlyList<Bounds> CollisionBoxes => _collisionBoxes;

    /// <summary>The cell the player is dropped into. The spawn room is cleared around it.</summary>
    public Vector2Int SpawnCell => _spawnCell;

    /// <summary>World centre of a cell, at floor level.</summary>
    public Vector3 CellCentreOf(int x, int z) => CellCentre(x, z);

    /// <inheritdoc />
    public string MapName => "backrooms";

    /// <summary>
    /// Spawns inside the cleared spawn room, all facing its middle. Scattering them through
    /// the maze instead means two players lose each other immediately, which is exactly the
    /// wrong property for the only room where they are guaranteed to meet.
    /// </summary>
    public void GetSpawns(List<(Vector3 position, float yaw)> into)
    {
        // 스폰 개수는 서버 Room.MaxPlayers 와 같다.
        const int spawnCount = 8;

        if (_spawnCell == Vector2Int.zero) _spawnCell = new Vector2Int(gridWidth / 2, gridHeight / 2);

        int x0 = Mathf.Max(1, _spawnCell.x - 1);
        int x1 = Mathf.Min(gridWidth - 1, _spawnCell.x + 2);
        int z0 = Mathf.Max(1, _spawnCell.y - 1);
        int z1 = Mathf.Min(gridHeight - 1, _spawnCell.y + 2);

        Vector3 near = CellCentre(x0, z0);
        Vector3 far = CellCentre(x1, z1);
        var roomCentre = new Vector3((near.x + far.x) * 0.5f, 0f, (near.z + far.z) * 0.5f);

        for (int x = x0; x <= x1 && into.Count < spawnCount; x++)
        {
            for (int z = z0; z <= z1 && into.Count < spawnCount; z++)
            {
                Vector3 cell = CellCentre(x, z);
                var toCentre = new Vector3(roomCentre.x - cell.x, 0f, roomCentre.z - cell.z);

                // Yaw 0 is +Z, and the server's move function uses the same convention.
                float yaw = toCentre.sqrMagnitude > 1e-4f ? Mathf.Atan2(toCentre.x, toCentre.z) : 0f;

                // The floor slab's top face is y = 0, and the server's position is the feet.
                into.Add((new Vector3(cell.x, 0f, cell.z), yaw));
            }
        }
    }

    private void Awake()
    {
        Generate();
    }

    public void Generate()
    {
        var random = new System.Random(seed);

        // Centre the level on the origin so the coordinates stay small and symmetrical.
        _originX = -gridWidth * cellSize * 0.5f;
        _originZ = -gridHeight * cellSize * 0.5f;

        _collisionBoxes.Clear();

        EnsureMaterials();
        CarveMaze(random);
        CarveRooms(random);
        PunchLoops(random);

        Vector2Int spawn = new Vector2Int(gridWidth / 2, gridHeight / 2);
        _spawnCell = spawn;
        OpenSpawnRoom(spawn);

        BuildFloorAndCeiling();
        BuildWalls();
        BuildLights(random);
        PlaceActors(spawn);
        ApplyAtmosphere();
    }

    /// <summary>
    /// Runs the layout passes and records the collision boxes without building any geometry.
    /// The editor exporter needs the box list in edit mode, where instantiating a hundred and
    /// eighty metres of level into the open scene would be unacceptable.
    ///
    /// It draws from the seeded random in exactly the order <see cref="Generate"/> does, and
    /// stops before the lighting pass, which draws afterwards and touches no collider. Change
    /// that order in one place and not the other and the exported map silently stops matching
    /// the level the player sees.
    /// </summary>
    public IReadOnlyList<Bounds> ComputeCollision()
    {
        var random = new System.Random(seed);

        _originX = -gridWidth * cellSize * 0.5f;
        _originZ = -gridHeight * cellSize * 0.5f;

        _collisionBoxes.Clear();

        CarveMaze(random);
        CarveRooms(random);
        PunchLoops(random);

        _spawnCell = new Vector2Int(gridWidth / 2, gridHeight / 2);
        OpenSpawnRoom(_spawnCell);

        _collisionOnly = true;
        try
        {
            BuildFloorAndCeiling();
            BuildWalls();
        }
        finally
        {
            _collisionOnly = false;
        }

        return _collisionBoxes;
    }

    private bool _collisionOnly;

    // ---------------------------------------------------------------- layout

    /// <summary>
    /// Recursive backtracker, iterative so a big grid cannot blow the stack. Every cell ends up
    /// visited exactly once, which is what guarantees the whole level is connected.
    /// </summary>
    private void CarveMaze(System.Random random)
    {
        _southWall = new bool[gridWidth, gridHeight + 1];
        _westWall = new bool[gridWidth + 1, gridHeight];
        for (int x = 0; x < gridWidth; x++)
            for (int z = 0; z <= gridHeight; z++) _southWall[x, z] = true;
        for (int x = 0; x <= gridWidth; x++)
            for (int z = 0; z < gridHeight; z++) _westWall[x, z] = true;

        var visited = new bool[gridWidth, gridHeight];
        var stack = new Stack<Vector2Int>();
        var current = new Vector2Int(random.Next(gridWidth), random.Next(gridHeight));
        visited[current.x, current.y] = true;
        stack.Push(current);

        var neighbours = new List<Vector2Int>(4);
        while (stack.Count > 0)
        {
            current = stack.Peek();
            neighbours.Clear();

            if (current.x > 0 && !visited[current.x - 1, current.y]) neighbours.Add(new Vector2Int(-1, 0));
            if (current.x < gridWidth - 1 && !visited[current.x + 1, current.y]) neighbours.Add(new Vector2Int(1, 0));
            if (current.y > 0 && !visited[current.x, current.y - 1]) neighbours.Add(new Vector2Int(0, -1));
            if (current.y < gridHeight - 1 && !visited[current.x, current.y + 1]) neighbours.Add(new Vector2Int(0, 1));

            if (neighbours.Count == 0) { stack.Pop(); continue; }

            Vector2Int step = neighbours[random.Next(neighbours.Count)];
            Vector2Int next = current + step;

            if (step.x == 1) _westWall[next.x, current.y] = false;
            else if (step.x == -1) _westWall[current.x, current.y] = false;
            else if (step.y == 1) _southWall[current.x, next.y] = false;
            else _southWall[current.x, current.y] = false;

            visited[next.x, next.y] = true;
            stack.Push(next);
        }
    }

    /// <summary>Opens rectangular rooms, so the level is not uniformly corridor-width.</summary>
    private void CarveRooms(System.Random random)
    {
        for (int i = 0; i < roomCount; i++)
        {
            int width = random.Next(roomMinCells, roomMaxCells + 1);
            int height = random.Next(roomMinCells, roomMaxCells + 1);
            int x0 = random.Next(1, Mathf.Max(2, gridWidth - width - 1));
            int z0 = random.Next(1, Mathf.Max(2, gridHeight - height - 1));

            for (int x = x0; x < x0 + width && x < gridWidth; x++)
                for (int z = z0; z < z0 + height && z < gridHeight; z++)
                {
                    if (x > x0) _westWall[x, z] = false;      // interior walls only
                    if (z > z0) _southWall[x, z] = false;
                }
        }
    }

    /// <summary>Knocks extra doorways through interior walls, turning the tree into a mess of loops.</summary>
    private void PunchLoops(System.Random random)
    {
        for (int x = 0; x < gridWidth; x++)
            for (int z = 1; z < gridHeight; z++)           // z 0 and gridHeight are the outer shell
                if (_southWall[x, z] && random.NextDouble() < loopChance) _southWall[x, z] = false;

        for (int x = 1; x < gridWidth; x++)
            for (int z = 0; z < gridHeight; z++)
                if (_westWall[x, z] && random.NextDouble() < loopChance) _westWall[x, z] = false;
    }

    /// <summary>
    /// Clears the inside of a block of cells around the spawn, so the player never opens their
    /// eyes in a dead-end cupboard. Only interior walls go — the room keeps its own outer shell.
    /// </summary>
    private void OpenSpawnRoom(Vector2Int centre)
    {
        int x0 = Mathf.Max(1, centre.x - 1);
        int x1 = Mathf.Min(gridWidth - 1, centre.x + 2);
        int z0 = Mathf.Max(1, centre.y - 1);
        int z1 = Mathf.Min(gridHeight - 1, centre.y + 2);

        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                if (x > x0) _westWall[x, z] = false;
                if (z > z0) _southWall[x, z] = false;
            }
    }

    // ---------------------------------------------------------------- geometry

    private Vector3 CellCentre(int x, int z) =>
        new Vector3(_originX + (x + 0.5f) * cellSize, 0f, _originZ + (z + 0.5f) * cellSize);

    private void BuildFloorAndCeiling()
    {
        float width = gridWidth * cellSize;
        float depth = gridHeight * cellSize;

        // Two slabs rather than per-cell tiles: nothing about them varies, so nothing is gained
        // by splitting them, and it saves thousands of objects.
        AddBox("Floor", new Vector3(0f, -0.1f, 0f), new Vector3(width, 0.2f, depth), floorMaterial, true);
        AddBox("Ceiling", new Vector3(0f, wallHeight + 0.1f, 0f), new Vector3(width, 0.2f, depth), ceilingMaterial, true);
    }

    /// <summary>
    /// Builds the walls, merging each straight run into one box. Scanning for runs is what keeps
    /// the object and collider count reasonable on a level this size.
    /// </summary>
    private void BuildWalls()
    {
        Transform walls = null;
        if (!_collisionOnly)
        {
            walls = new GameObject("Walls").transform;
            walls.SetParent(transform, false);
        }

        int pieces = 0;

        // Runs along X, for walls lying on the -Z face of a row of cells.
        for (int z = 0; z <= gridHeight; z++)
        {
            int x = 0;
            while (x < gridWidth)
            {
                if (!_southWall[x, z]) { x++; continue; }
                int start = x;
                while (x < gridWidth && _southWall[x, z]) x++;
                int count = x - start;

                float length = count * cellSize + wallThickness;
                float centreX = _originX + (start + count * 0.5f) * cellSize;
                float centreZ = _originZ + z * cellSize;
                AddBox("Wall X", new Vector3(centreX, wallHeight * 0.5f, centreZ),
                    new Vector3(length, wallHeight, wallThickness), wallMaterial, true, walls);
                pieces++;
            }
        }

        // Runs along Z, for walls on the -X face of a column of cells.
        for (int x = 0; x <= gridWidth; x++)
        {
            int z = 0;
            while (z < gridHeight)
            {
                if (!_westWall[x, z]) { z++; continue; }
                int start = z;
                while (z < gridHeight && _westWall[x, z]) z++;
                int count = z - start;

                float length = count * cellSize + wallThickness;
                float centreX = _originX + x * cellSize;
                float centreZ = _originZ + (start + count * 0.5f) * cellSize;
                AddBox("Wall Z", new Vector3(centreX, wallHeight * 0.5f, centreZ),
                    new Vector3(wallThickness, wallHeight, length), wallMaterial, true, walls);
                pieces++;
            }
        }

        WallPieces = pieces;
    }

    private void BuildLights(System.Random random)
    {
        var root = new GameObject("Ceiling Lights").transform;
        root.SetParent(transform, false);
        int lights = 0;

        // Panels are just emissive boxes — they read as strip lights without costing a light.
        int panelStep = Mathf.Max(1, panelSpacing);
        for (int x = panelStep / 2; x < gridWidth; x += panelStep)
        for (int z = panelStep / 2; z < gridHeight; z += panelStep)
        {
            Vector3 centre = CellCentre(x, z);
            AddBox("Panel", new Vector3(centre.x, wallHeight - 0.04f, centre.z),
                new Vector3(cellSize * 0.34f, 0.08f, cellSize * 0.34f), lightMaterial, false, root);
        }

        // Real lights are sparser, and are what actually illuminate the place.
        int lightStep = Mathf.Max(1, lightSpacing);
        for (int x = lightStep / 2; x < gridWidth; x += lightStep)
        for (int z = lightStep / 2; z < gridHeight; z += lightStep)
        {
            Vector3 centre = CellCentre(x, z);

            var lightGo = new GameObject("Fluorescent");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.position = new Vector3(centre.x, wallHeight - 0.25f, centre.z);

            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = lightColor;
            light.range = lightRange;
            light.intensity = lightIntensity;
            // Shadows from a hundred lights would be ruinous, and flat diffuse light is what
            // the reference actually looks like anyway.
            light.shadows = LightShadows.None;
            lights++;

            if (random.NextDouble() < flickerFraction)
            {
                _flickerLights.Add(light);
                _flickerPhase.Add((float)random.NextDouble() * 10f);
            }
        }

        LightCount = lights;
    }

    private void PlaceActors(Vector2Int spawn)
    {
        Vector3 centre = CellCentre(spawn.x, spawn.y);

        if (player == null)
        {
            var found = GameObject.Find("Player");
            if (found != null) player = found.transform;
        }
        if (player != null)
        {
            var body = player.GetComponent<CharacterController>();
            // Nudge up by the skin width so the capsule starts clear of the floor slab.
            float lift = body != null ? body.skinWidth + 0.02f : 0.1f;
            player.position = new Vector3(centre.x, lift, centre.z);
        }

        // Keep the mirror in the spawn room, a few metres ahead and facing back — it is the only
        // way to see your own character, so it should not be lost somewhere in the maze.
        if (mirror != null)
        {
            float mirrorZ = centre.z + cellSize * 0.9f;
            mirror.position = new Vector3(centre.x, 1.3f, mirrorZ);
            mirror.rotation = Quaternion.identity;
            if (mirrorFrame != null)
            {
                mirrorFrame.position = new Vector3(centre.x, 1.3f, mirrorZ + 0.06f);
                mirrorFrame.rotation = Quaternion.identity;
            }
        }
    }

    /// <summary>
    /// Fog and ambient do most of the work here: the dread comes from not being able to see how
    /// far the room goes, and from there being no sky and no sun anywhere in it.
    /// </summary>
    private void ApplyAtmosphere()
    {
        RenderSettings.fog = true;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = fogDensity;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.20f, 0.18f, 0.12f);
        RenderSettings.skybox = null;
    }

    private void Update()
    {
        // Cheap buzzing flicker: a couple of out-of-phase sines plus a hard dropout.
        for (int i = 0; i < _flickerLights.Count; i++)
        {
            Light light = _flickerLights[i];
            if (light == null) continue;

            float t = Time.time * 9f + _flickerPhase[i];
            float buzz = 0.82f + 0.18f * Mathf.Sin(t) * Mathf.Sin(t * 0.37f);
            if (Mathf.Sin(t * 0.11f) > 0.985f) buzz *= 0.15f;
            light.intensity = lightIntensity * buzz;
        }
    }

    // ---------------------------------------------------------------- helpers

    private void AddBox(string name, Vector3 centre, Vector3 size, Material material,
        bool collider, Transform parent = null)
    {
        // Recorded before the early-out, so the exported list is exactly the set of boxes that
        // would have carried a collider — one source, not a parallel description.
        if (collider) _collisionBoxes.Add(new Bounds(centre, size));
        if (_collisionOnly) return;

        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent != null ? parent : transform, false);
        box.transform.localPosition = centre;
        box.transform.localScale = size;

        if (!collider)
        {
            var boxCollider = box.GetComponent<Collider>();
            if (boxCollider != null) Destroy(boxCollider);
        }

        box.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    private void EnsureMaterials()
    {
        if (wallMaterial == null) wallMaterial = MakeMaterial("Backrooms Wall", wallColor, 0.06f);
        if (floorMaterial == null) floorMaterial = MakeMaterial("Backrooms Floor", floorColor, 0.03f);
        if (ceilingMaterial == null) ceilingMaterial = MakeMaterial("Backrooms Ceiling", ceilingColor, 0.05f);
        if (lightMaterial == null)
        {
            lightMaterial = MakeMaterial("Backrooms Light Panel", lightColor, 0.2f);
            lightMaterial.EnableKeyword("_EMISSION");
            lightMaterial.SetColor("_EmissionColor", lightColor * 3.2f);
            lightMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
    }

    private static Material MakeMaterial(string name, Color color, float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader) { name = name };
        material.color = color;
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        // Every wall shares one mesh and one material, so instancing collapses the draw calls.
        material.enableInstancing = true;
        return material;
    }
}
