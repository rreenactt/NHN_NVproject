using System.Collections.Generic;
using NV.Client.Net;
using NV.Shared.Collision;
using UnityEngine;

/// <summary>
/// A small, bright, deliberately boring arena for testing multiplayer.
///
/// The Backrooms maze is the game, but it is a poor place to test networking: 180 m of
/// identical corridor means two players spend the first minute failing to find each other,
/// and every symptom you are looking for — a stutter, a rubber-band, a body that slides —
/// needs the other player *in view* to be visible at all. So this is 40 m square, walled,
/// open, and lit, with the eight spawns on a ring facing the middle. Whoever connects is
/// looking straight at everybody else.
///
/// It earns its cover blocks and centre platform: the flat floor tests nothing. The blocks
/// are what a player slides along and gets stopped by, and the platform is the only thing
/// here that tests standing on top of geometry rather than on the floor slab.
///
/// Built at Awake from these numbers, like everything else in this project — nothing is
/// authored in the scene. The numbers are all exact halves and quarters on purpose: the
/// collision boxes are hashed and compared against the server's copy, and values that
/// round-trip exactly through float remove one whole class of "why does the hash differ".
/// </summary>
public class TestRoomMap : MonoBehaviour, INetworkMapSource
{
    [Header("Size")]
    [Tooltip("Floor is this many metres square, centred on the origin.")]
    public float floorSize = 40f;
    public float wallHeight = 4f;
    public float wallThickness = 0.5f;

    [Header("Spawns")]
    [Tooltip("Radius of the spawn ring. Keep well inside the walls so nobody spawns in one.")]
    public float spawnRadius = 15f;

    [Header("Look")]
    public Color floorColor = new Color(0.42f, 0.44f, 0.47f);
    public Color wallColor = new Color(0.62f, 0.63f, 0.66f);
    public Color coverColor = new Color(0.72f, 0.55f, 0.28f);

    [Header("Materials (created at runtime if empty)")]
    public Material floorMaterial;
    public Material wallMaterial;
    public Material coverMaterial;

    [Header("Spawn")]
    [Tooltip("Player to drop onto a spawn point. Found by name if left empty.")]
    public Transform player;

    /// 스폰 개수. 서버 Room.MaxPlayers 와 같다.
    private const int SpawnCount = 8;

    private readonly List<Bounds> _collisionBoxes = new List<Bounds>();
    private bool _collisionOnly;

    public string MapName => "test-room";

    public IReadOnlyList<Bounds> CollisionBoxes => _collisionBoxes;

    private void Awake()
    {
        Generate();
    }

    public void Generate()
    {
        _collisionBoxes.Clear();
        EnsureMaterials();
        BuildGeometry();
        PlacePlayer();
        ApplyAtmosphere();
    }

    /// <inheritdoc />
    public IReadOnlyList<Bounds> ComputeCollision()
    {
        _collisionBoxes.Clear();
        _collisionOnly = true;
        try
        {
            BuildGeometry();
        }
        finally
        {
            _collisionOnly = false;
        }

        return _collisionBoxes;
    }

    /// <inheritdoc />
    ///
    /// <remarks>
    /// This map has no grid, and that is the right answer rather than a gap.
    ///
    /// The grid exists so the server can place objectives and pick teleport landing spots. This
    /// arena never runs the match rules — <c>MultiplayerTest</c> wants bodies without rules, and
    /// <c>RemotePlayerPuppet</c> only attaches the match components when a <c>MatchManager</c> is
    /// present. Nothing here asks "where can a player stand".
    ///
    /// Filling one in anyway would be worse than useless: it is a single open room *with a centre
    /// platform and four cover blocks*, so a blanket "all cells walkable" grid would declare the
    /// inside of those blocks to be floor. A map with no grid contributes nothing to the map hash,
    /// so this also keeps <c>test-room.json</c> stable.
    /// </remarks>
    public MapGridData BuildGrid() => null;

    /// <inheritdoc />
    ///
    /// <remarks>
    /// Always reproducible. Every box and spawn here is derived from serialized fields with no
    /// random draw anywhere, so two exports of the same scene produce the same bytes.
    /// </remarks>
    public string DescribeExportBlocker() => null;

    /// <inheritdoc />
    public void GetSpawns(List<(Vector3 position, float yaw)> into)
    {
        for (var index = 0; index < SpawnCount; index++)
        {
            // 0 은 +Z, 시계 방향. 서버의 이동 함수도 같은 규약을 쓴다.
            var angle = index * (2f * Mathf.PI / SpawnCount);
            var position = new Vector3(
                spawnRadius * Mathf.Sin(angle),
                0f,
                spawnRadius * Mathf.Cos(angle));

            // 링 위에서 중앙을 본다. 접속하자마자 서로가 화면에 있다.
            // [0, 2pi) 로 감아 둔다 — 결정적 삼각함수의 범위 축소에 의존하지 않는다.
            var yaw = angle + Mathf.PI;
            if (yaw >= 2f * Mathf.PI) yaw -= 2f * Mathf.PI;

            into.Add((position, yaw));
        }
    }

    /// <summary>
    /// Floor, four walls, centre platform, four cover blocks — in that order. The order is
    /// part of the map hash, so it has to be the same here and in whatever produced the JSON
    /// the server loaded.
    /// </summary>
    private void BuildGeometry()
    {
        var half = floorSize * 0.5f;
        var span = floorSize + wallThickness * 2f;
        var wallY = wallHeight * 0.5f;
        var edge = half + wallThickness * 0.5f;

        AddBox("Floor", new Vector3(0f, -0.1f, 0f), new Vector3(floorSize, 0.2f, floorSize), floorMaterial);

        AddBox("Wall +Z", new Vector3(0f, wallY, edge), new Vector3(span, wallHeight, wallThickness), wallMaterial);
        AddBox("Wall -Z", new Vector3(0f, wallY, -edge), new Vector3(span, wallHeight, wallThickness), wallMaterial);
        AddBox("Wall +X", new Vector3(edge, wallY, 0f), new Vector3(wallThickness, wallHeight, span), wallMaterial);
        AddBox("Wall -X", new Vector3(-edge, wallY, 0f), new Vector3(wallThickness, wallHeight, span), wallMaterial);

        // 올라설 수 있는 유일한 지형. 바닥 슬래브 위에 서는 것만으로는 착지 판정이 검증되지 않는다.
        AddBox("Platform", new Vector3(0f, 0.5f, 0f), new Vector3(6f, 1f, 6f), coverMaterial);

        AddBox("Cover +X+Z", new Vector3(8f, 0.75f, 8f), new Vector3(3f, 1.5f, 3f), coverMaterial);
        AddBox("Cover -X+Z", new Vector3(-8f, 0.75f, 8f), new Vector3(3f, 1.5f, 3f), coverMaterial);
        AddBox("Cover +X-Z", new Vector3(8f, 0.75f, -8f), new Vector3(3f, 1.5f, 3f), coverMaterial);
        AddBox("Cover -X-Z", new Vector3(-8f, 0.75f, -8f), new Vector3(3f, 1.5f, 3f), coverMaterial);
    }

    private void PlacePlayer()
    {
        if (player == null)
        {
            var found = GameObject.Find("Player");
            if (found != null) player = found.transform;
        }

        if (player == null) return;

        var spawns = new List<(Vector3 position, float yaw)>(SpawnCount);
        GetSpawns(spawns);

        var body = player.GetComponent<CharacterController>();
        var lift = body != null ? body.skinWidth + 0.02f : 0.1f;

        // 서버에 붙으면 첫 스냅샷이 이 위치를 곧 덮어쓴다. 혼자 돌 때를 위한 값이다.
        player.position = spawns[0].position + new Vector3(0f, lift, 0f);
        player.rotation = Quaternion.Euler(0f, spawns[0].yaw * Mathf.Rad2Deg, 0f);
    }

    /// <summary>
    /// The opposite of the Backrooms treatment: no fog, plain ambient. You are looking for
    /// a network artefact here, and fog that hides how far the room goes would hide it too.
    /// </summary>
    private void ApplyAtmosphere()
    {
        RenderSettings.fog = false;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.48f);
    }

    private void AddBox(string name, Vector3 centre, Vector3 size, Material material)
    {
        _collisionBoxes.Add(new Bounds(centre, size));
        if (_collisionOnly) return;

        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(transform, false);
        box.transform.localPosition = centre;
        box.transform.localScale = size;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    private void EnsureMaterials()
    {
        if (floorMaterial == null) floorMaterial = MakeMaterial("Test Room Floor", floorColor);
        if (wallMaterial == null) wallMaterial = MakeMaterial("Test Room Wall", wallColor);
        if (coverMaterial == null) coverMaterial = MakeMaterial("Test Room Cover", coverColor);
    }

    private static Material MakeMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader) { name = name };
        material.color = color;
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        material.enableInstancing = true;
        return material;
    }
}
