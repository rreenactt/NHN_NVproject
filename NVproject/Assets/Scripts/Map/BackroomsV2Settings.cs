using UnityEngine;

namespace NV.Client.Map
{
    /// <summary>
    /// Parameters for the Backrooms V2 level — a single-storey, open-plan floor of concrete
    /// halls, pillar fields and partition clusters, carved by BSP zoning.
    ///
    /// Deliberately shares nothing with the original Backrooms: not the algorithm (BSP zones with
    /// connectivity by construction, against room-attempts with post-hoc repair), not the vertical
    /// structure (one storey, no stairwell), and not the palette (cold concrete and teal
    /// fluorescents against the original's mono-yellow). The plan and its isolation boundary live
    /// in <c>NVserver/docs/backrooms-v2-plan.md</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "NV/Map/Backrooms V2 Settings", fileName = "BackroomsV2Settings")]
    public sealed class BackroomsV2Settings : MapGeneratorSettings
    {
        [Header("Grid")]
        [Tooltip("Cells per side. The floor is square: gridSize * cellSize metres across.")]
        public int gridSize = 44;

        [Tooltip("Metres per cell. Finer than the original map's 3.0 so objective placement " +
                 "candidates spread more evenly.")]
        public float cellSize = 2.5f;

        [Tooltip("Vertical span the walkability grid declares for its single storey. Must stay " +
                 "above MatchConstants.InteractHeight (2.5) — the match rules assume a storey is " +
                 "taller than an interaction reach.")]
        public float floorHeight = 3.6f;

        [Header("Structure")]
        [Tooltip("Thickness of every wall, metres.")]
        public float wallThickness = 0.3f;

        [Tooltip("Interior clear height, floor to ceiling slab, metres.")]
        public float ceilingHeight = 3.4f;

        [Header("BSP zoning")]
        [Tooltip("A zone is never split below this many cells per side.")]
        public int zoneMin = 5;

        [Tooltip("A zone larger than this many cells on either side is always split again.")]
        public int zoneMax = 10;

        [Tooltip("Chance an adjacency beyond the spanning tree also gets a doorway. The spanning " +
                 "tree alone makes a maze with one route between any two zones; loops are what " +
                 "make a chase survivable.")]
        [Range(0f, 1f)]
        public float loopChance = 0.35f;

        [Tooltip("Doorway width in cells. Two cells = 5 m, wide enough for a Seeker and a Runner " +
                 "to cross.")]
        public int doorwayWidth = 2;

        [Header("Zone interiors")]
        [Tooltip("Pillar grid pitch in cells inside open halls.")]
        public int pillarSpacing = 3;

        [Header("Lighting")]
        [Tooltip("Ceiling light strip pitch in cells.")]
        public int lightSpacing = 4;

        [Header("Palette — cold concrete, not the original's yellow")]
        public Color wallColor = new Color(0.58f, 0.62f, 0.58f);

        public Color floorColor = new Color(0.38f, 0.40f, 0.39f);

        public Color ceilingColor = new Color(0.66f, 0.70f, 0.67f);

        public Color trimColor = new Color(0.45f, 0.51f, 0.48f);

        public Color lightColor = new Color(0.82f, 1.00f, 0.94f);

        /// <summary>World-space X/Z of the outer corner of cell (0,0). The floor is centred on the origin.</summary>
        public float Origin => -(gridSize * cellSize) * 0.5f;
    }
}
