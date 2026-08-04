using UnityEngine;

namespace NV.Client.Map
{
    /// <summary>
    /// The small, bright, deliberately boring arena used to test multiplayer.
    ///
    /// Defaults match <c>TestRoomMap</c>'s serialized values exactly, and they have to: the first
    /// thing this generator has to prove is that it produces byte-identical
    /// <c>NVserver/MapData/test-room.json</c>.
    ///
    /// The numbers are all exact halves and quarters on purpose. Collision boxes are hashed against
    /// the server's copy, and values that round-trip exactly through float remove one whole class of
    /// "why does the hash differ".
    /// </summary>
    [CreateAssetMenu(menuName = "NV/Map/Test Room Settings", fileName = "TestRoomSettings")]
    public sealed class TestRoomSettings : MapGeneratorSettings
    {
        [Header("Size")]
        [Tooltip("Floor is this many metres square, centred on the origin.")]
        public float floorSize = 40f;

        public float wallHeight = 4f;

        public float wallThickness = 0.5f;

        [Header("Spawns")]
        [Tooltip("Radius of the spawn ring. Keep well inside the walls so nobody spawns in one.")]
        public float spawnRadius = 15f;

        [Tooltip("Matches the server's Room.MaxPlayers.")]
        public int spawnCount = 8;

        /// <inheritdoc />
        ///
        /// <remarks>
        /// The seed cannot block this one. Every box and spawn is derived from the fields above
        /// with no random draw anywhere, so <see cref="MapGeneratorSettings.randomizeSeed"/> cannot
        /// make this level differ between two generations — and refusing the export over it would
        /// be a lie that costs somebody an afternoon.
        ///
        /// The empty-name case still applies, so the base is asked and only the seed clause skipped.
        /// </remarks>
        public override string DescribeBlocker() => DescribeNameBlocker();
    }
}
