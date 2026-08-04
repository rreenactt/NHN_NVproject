using UnityEngine;

namespace NV.Client.Map
{
    /// <summary>
    /// A two-floor Backrooms level: a square grid of rooms and corridors on two stacked floors,
    /// joined by a stairwell that occupies the *same cells* on both floors so the flights line up.
    /// Spawn, exit and stairwell are hand-authored rectangles that never move; everything else
    /// reshuffles with the seed.
    ///
    /// **Every default here matches what <c>SampleScene</c> has serialized on
    /// <c>BackroomsMapGenerator</c>**, so a fresh settings object reproduces the shipped
    /// <c>backrooms.json</c> exactly. That is not a nicety — it is what makes the port checkable.
    /// </summary>
    [CreateAssetMenu(menuName = "NV/Map/Backrooms Settings", fileName = "BackroomsSettings")]
    public sealed class BackroomsSettings : MapGeneratorSettings
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

        [Header("Lighting — panels are geometry; the lamps themselves are the ambience component's")]
        [Tooltip("A fluorescent panel every N cells, so the lighting reads as an office grid.")]
        public int lightSpacing = 3;

        [Header("Mood — values from the aesthetic spec. None of this reaches the server.")]
        public Color wallColor = new Color32(0xC9, 0xB3, 0x6B, 0xFF);

        public Color trimColor = new Color32(0xA8, 0x92, 0x4E, 0xFF);

        public Color carpetColor = new Color32(0x8A, 0x7F, 0x52, 0xFF);

        public Color ceilingColor = new Color32(0xD8, 0xCF, 0xA8, 0xFF);

        public Color lightColor = new Color32(0xFF, 0xF6, 0xD6, 0xFF);

        /// <summary>Storeys, never below one.</summary>
        public int FloorCount => Mathf.Max(1, floors);

        /// <summary>Ceiling sits this far above its floor.</summary>
        public float CeilingHeight => floorHeight - 0.2f;
    }
}
