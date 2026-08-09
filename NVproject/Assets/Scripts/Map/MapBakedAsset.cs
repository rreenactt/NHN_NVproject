using System.Collections.Generic;
using NV.Client.Net;
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

            StoreGrid(blueprint.Grid);
        }

        /// <summary>
        /// Overwrites this asset from a level standing in the **open scene** instead of from a
        /// blueprint.
        ///
        /// **This exists for the levels that are still generated at runtime inside their own
        /// scene** — <c>backrooms</c> in <c>SampleScene</c>. Nothing produces a
        /// <see cref="MapBlueprint"/> for those, so <see cref="Fill"/> can never describe them; and
        /// a map with no asset gets no <c>MapCatalog</c> row, which the lobby reads as "the server
        /// serves this map and this build cannot draw it" and refuses to make the room. Exporting
        /// the map file does not help, because that file is the *server's* half of the pair.
        ///
        /// **The boxes are copied verbatim rather than rebuilt from a min/max pair.** The map hash
        /// is taken from <c>Bounds.min</c> and <c>Bounds.max</c>, and a <c>Bounds</c> put back
        /// together through <c>SetMinMax</c> can return a value one ULP away — which surfaces only
        /// as terrain that differs for no visible reason. The choice between the built collision
        /// and the computed one is <c>MapExport</c>'s, repeated here for the same reason: picking
        /// differently is picking different terrain.
        ///
        /// **Scene volumes are deliberately not baked in.** <c>MapExport</c> appends them to the
        /// level's own list at export time, and it does that on every path — including the one that
        /// recomputes this asset's hash. Baking them here would count them twice.
        ///
        /// What this cannot carry is <see cref="Lights"/>: a level asked for its collision does not
        /// offer them, and a lamp is not geometry. That costs nothing for the maps this path is
        /// for, since they are opened by their own scene (<c>MapSceneTable</c>) which builds its own
        /// lighting. A map that has to be drawn from a prefab goes through <see cref="Fill"/>.
        /// </summary>
        public void FillFromScene(INetworkMapSource source, string sourceName, string bakedAt)
        {
            if (source == null) return;

            mapName = source.MapName;
            generator = sourceName;

            // Not a seed anybody can act on. The level's own component holds the seed it was built
            // from, and copying it here would invite reading it back as the value to reproduce with.
            usedSeed = 0;
            bakedAtUtc = bakedAt;

            var level = source.CollisionBoxes;
            if (level == null || level.Count == 0) level = source.ComputeCollision();

            boxes = new Bounds[level == null ? 0 : level.Count];
            for (var index = 0; index < boxes.Length; index++) boxes[index] = level[index];

            var spawns = new List<(Vector3 position, float yaw)>(8);
            source.GetSpawns(spawns);

            spawnPositions = new Vector3[spawns.Count];
            spawnYaws = new float[spawns.Count];

            for (var index = 0; index < spawns.Count; index++)
            {
                spawnPositions[index] = spawns[index].position;
                spawnYaws[index] = spawns[index].yaw;
            }

            // Where the match puts the Seeker. Only a level that also answers ILevelQuery knows it,
            // and one that does not is a level the match layer never runs on.
            spawnCentre = source is ILevelQuery query ? query.SpawnCentre : Vector3.zero;
            lights = new Vector3[0];

            var meta = source.BuildMeta();

            displayName = meta == null ? string.Empty : meta.DisplayName ?? string.Empty;
            description = meta == null ? string.Empty : meta.Description ?? string.Empty;
            recommendedPlayersMin = meta == null ? 0 : meta.RecommendedPlayersMin;
            recommendedPlayersMax = meta == null ? 0 : meta.RecommendedPlayersMax;
            tags = meta == null || meta.Tags == null ? new string[0] : meta.Tags;

            StoreGrid(source.BuildGrid());
        }

        /// <summary>
        /// Copies a grid in, or records that the level offered none.
        ///
        /// The cells are copied rather than referenced: the array handed in belongs to whoever
        /// built it, and <c>MapExport.AttachGrid</c> writes <see cref="MapCellFlags.FreeFloor"/>
        /// into the grid it is given — an asset sharing that array would be rewritten on disk by an
        /// export.
        /// </summary>
        private void StoreGrid(MapGridData grid)
        {
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
