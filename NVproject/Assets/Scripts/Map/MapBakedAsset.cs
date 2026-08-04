using System.Collections.Generic;
using NV.Shared.Collision;
using UnityEngine;

namespace NV.Client.Map
{
    /// <summary>
    /// A generated level, frozen. **This is the source of truth for what the server is told**, and
    /// the prefab beside it is only what the level looks like.
    ///
    /// Recovering collision by walking a prefab's transforms would make the box order depend on the
    /// hierarchy, and the order is part of the map hash — so moving one wall in the inspector, or
    /// Unity reordering children for its own reasons, would change the hash of unchanged terrain.
    /// Worse, the reverse also holds: a person nudging a wall in the scene would change the terrain
    /// without changing anything the server sees, and the map hash would still match. Keeping the
    /// boxes in an asset makes both of those explicit rather than silent.
    /// </summary>
    public sealed class MapBakedAsset : ScriptableObject
    {
        [Tooltip("Export filename and the server's map id — the server registers each file in its " +
                 "map directory under that file's own name.")]
        [SerializeField] private string mapName = "unnamed";

        [Tooltip("Collision, in the order the generator emitted it. The order is part of the map hash.")]
        [SerializeField] private Bounds[] boxes = new Bounds[0];

        [SerializeField] private Vector3[] spawnPositions = new Vector3[0];

        [SerializeField] private float[] spawnYaws = new float[0];

        [Tooltip("Where the match puts the Seeker at the start of a round.")]
        [SerializeField] private Vector3 spawnCentre;

        [Tooltip("Where the ambience component hangs lamps. Positions only — a Light is not geometry.")]
        [SerializeField] private Vector3[] lights = new Vector3[0];

        [Header("Walkability grid — absent is a normal answer")]
        [SerializeField] private bool hasGrid;
        [SerializeField] private int gridFloors;
        [SerializeField] private int gridWidth;
        [SerializeField] private int gridDepth;
        [SerializeField] private float gridCellSize;
        [SerializeField] private float gridFloorHeight;
        [SerializeField] private float gridOriginX;
        [SerializeField] private float gridOriginZ;
        [SerializeField] private byte[] gridCells = new byte[0];

        [Header("Shown in the lobby — never used for judgement")]
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private string description = string.Empty;
        [SerializeField] private int recommendedPlayersMin;
        [SerializeField] private int recommendedPlayersMax;
        [SerializeField] private string[] tags = new string[0];

        [Header("Provenance — for people, never for judgement")]
        [SerializeField] private string generator;
        [SerializeField] private int usedSeed;
        [SerializeField] private string bakedAtUtc;

        public string MapName => mapName;

        /// <summary>What the lobby shows. Falls back to <see cref="MapName"/>.</summary>
        public string DisplayName => string.IsNullOrEmpty(displayName) ? mapName : displayName;

        public string Description => description ?? string.Empty;

        public IReadOnlyList<Bounds> Boxes => boxes;

        public int SpawnCount => spawnPositions == null ? 0 : spawnPositions.Length;

        public Vector3 SpawnCentre => spawnCentre;

        public IReadOnlyList<Vector3> Lights => lights;

        public string Generator => generator;

        public int UsedSeed => usedSeed;

        public string BakedAtUtc => bakedAtUtc;

        public bool HasGrid => hasGrid;

        /// <summary>Feeds <c>INetworkMapSource.GetSpawns</c>, which wants the pair.</summary>
        public void GetSpawns(List<(Vector3 position, float yaw)> into)
        {
            if (into == null || spawnPositions == null || spawnYaws == null) return;

            var count = Mathf.Min(spawnPositions.Length, spawnYaws.Length);
            for (var index = 0; index < count; index++)
                into.Add((spawnPositions[index], spawnYaws[index]));
        }

        /// <summary>
        /// Rebuilds the grid the level offered, or <c>null</c> if it offered none.
        ///
        /// A fresh instance every call. <c>MapExport.AttachGrid</c> writes
        /// <see cref="MapCellFlags.FreeFloor"/> into whatever it is handed, and handing out the
        /// asset's own array would mean an export mutating a project asset on disk — which the
        /// editor would then keep, so the second export would start from the first one's answer.
        /// </summary>
        public MapGridData BuildGrid()
        {
            if (!hasGrid) return null;

            var cells = new byte[gridCells == null ? 0 : gridCells.Length];
            if (gridCells != null) System.Array.Copy(gridCells, cells, cells.Length);

            return new MapGridData
            {
                Floors = gridFloors,
                Width = gridWidth,
                Depth = gridDepth,
                CellSize = gridCellSize,
                FloorHeight = gridFloorHeight,
                OriginX = gridOriginX,
                OriginZ = gridOriginZ,
                Cells = cells,
            };
        }

        /// <summary>
        /// What the lobby shows for this map, or <c>null</c> if nothing was authored.
        ///
        /// **Returning null rather than a struct of empty strings matters.** The export writes this
        /// block into the map file, and a block full of blanks would make the server prefer those
        /// blanks over the fallback it computes from the map itself — the lobby would then show an
        /// unnamed row instead of the map's id.
        /// </summary>
        public MapMetaInfo BuildMeta()
        {
            var authored = !string.IsNullOrEmpty(displayName)
                || !string.IsNullOrEmpty(description)
                || recommendedPlayersMin > 0
                || recommendedPlayersMax > 0
                || (tags != null && tags.Length > 0);

            if (!authored) return null;

            return new MapMetaInfo
            {
                DisplayName = displayName ?? string.Empty,
                Description = description ?? string.Empty,
                RecommendedPlayersMin = recommendedPlayersMin,
                RecommendedPlayersMax = recommendedPlayersMax,
                Tags = tags ?? new string[0],
            };
        }

        /// <summary>
        /// Overwrites this asset from a blueprint. The bake pipeline is the only caller — an asset
        /// that anything else could write would stop being the single description of the level.
        ///
        /// <paramref name="settings"/> carries the authored lobby text. It is passed rather than
        /// read off the blueprint because a blueprint is the *solved geometry* — putting a display
        /// name through the solver would make every generator copy a value it never reads.
        /// </summary>
        public void Fill(
            MapBlueprint blueprint, MapGeneratorSettings settings, string generatorName, string bakedAt)
        {
            mapName = blueprint.MapName;
            generator = generatorName;
            usedSeed = blueprint.UsedSeed;
            bakedAtUtc = bakedAt;

            if (settings != null)
            {
                displayName = settings.displayName ?? string.Empty;
                description = settings.description ?? string.Empty;
                recommendedPlayersMin = settings.recommendedPlayersMin;
                recommendedPlayersMax = settings.recommendedPlayersMax;
                tags = settings.tags ?? new string[0];
            }

            var collected = new List<Bounds>(blueprint.Pieces.Count);
            blueprint.CollectCollisionBoxes(collected);
            boxes = collected.ToArray();

            spawnCentre = blueprint.SpawnCentre;
            lights = blueprint.Lights.ToArray();
            spawnPositions = new Vector3[blueprint.Spawns.Count];
            spawnYaws = new float[blueprint.Spawns.Count];

            for (var index = 0; index < blueprint.Spawns.Count; index++)
            {
                spawnPositions[index] = blueprint.Spawns[index].Position;
                spawnYaws[index] = blueprint.Spawns[index].Yaw;
            }

            var grid = blueprint.Grid;
            hasGrid = grid != null;

            if (!hasGrid)
            {
                gridCells = new byte[0];
                return;
            }

            gridFloors = grid.Floors;
            gridWidth = grid.Width;
            gridDepth = grid.Depth;
            gridCellSize = grid.CellSize;
            gridFloorHeight = grid.FloorHeight;
            gridOriginX = grid.OriginX;
            gridOriginZ = grid.OriginZ;

            gridCells = new byte[grid.Cells == null ? 0 : grid.Cells.Length];
            if (grid.Cells != null) System.Array.Copy(grid.Cells, gridCells, gridCells.Length);
        }
    }
}
