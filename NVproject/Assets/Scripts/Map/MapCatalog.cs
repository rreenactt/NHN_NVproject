using System;
using UnityEngine;

namespace NV.Client.Map
{
    /// <summary>
    /// Every baked map **this build can draw**, and what to show for it.
    ///
    /// The lobby needs two different answers about a map and they come from different places. What
    /// maps *exist* is the server's answer (`GET /maps`) — it is the thing that opens rooms, and it
    /// can gain a map without this build knowing. Whether this build can *render* one is a local
    /// answer, because a WebGL player ships its assets baked in. Merging the two is what lets the
    /// create-room screen say "the server has this map but you cannot play it" instead of failing
    /// after connect.
    ///
    /// **Written by the bake pipeline, not by hand.** A table a person maintains goes stale, and the
    /// array in <c>CreateRoomPopup</c> that this replaces is exactly what that looks like. Baking a
    /// map updates its row; nothing else writes here.
    /// </summary>
    public sealed class MapCatalog : ScriptableObject
    {
        /// <summary>Where <see cref="Load"/> reads it from. Under Resources so a build carries it.</summary>
        public const string ResourcePath = "MapCatalog";

        [Tooltip("One row per baked map. Maintained by Tools ▸ NV ▸ Map ▸ Map Generator.")]
        [SerializeField] private MapCatalogEntry[] entries = new MapCatalogEntry[0];

        public MapCatalogEntry[] Entries => entries ?? new MapCatalogEntry[0];

        /// <summary>
        /// Replaces every row. The bake pipeline is the only caller — see the class remark on why
        /// nothing else may write here.
        /// </summary>
        public void Replace(MapCatalogEntry[] rows)
        {
            entries = rows ?? new MapCatalogEntry[0];
        }

        /// <summary>
        /// The catalog, or <c>null</c> if this build has none.
        ///
        /// Not cached in a static field. A domain reload wipes those while leaving play mode
        /// running, and the result is code that believes it loaded something and holds null —
        /// the same trap <c>MainLobbyAssets</c> avoids for the same reason. <c>Resources.Load</c>
        /// does not re-read an already loaded asset, so there is nothing to gain.
        /// </summary>
        public static MapCatalog Load()
        {
            return Resources.Load<MapCatalog>(ResourcePath);
        }

        /// <summary>The row for this map id, or <c>null</c>.</summary>
        public MapCatalogEntry Find(string mapId)
        {
            if (string.IsNullOrEmpty(mapId)) return null;

            var rows = Entries;

            for (var index = 0; index < rows.Length; index++)
            {
                if (rows[index] != null
                    && string.Equals(rows[index].mapId, mapId, StringComparison.Ordinal))
                {
                    return rows[index];
                }
            }

            return null;
        }
    }

    /// <summary>
    /// One map this build can draw.
    ///
    /// A class rather than a struct so <see cref="MapCatalog.Find"/> can answer "no such map" with
    /// null instead of a zeroed row that looks like a map named "".
    /// </summary>
    [Serializable]
    public sealed class MapCatalogEntry
    {
        [Tooltip("Map id — the same string the server uses, which is the map's name and its export " +
                 "filename. What POST /rooms is given.")]
        public string mapId = string.Empty;

        [Tooltip("The baked level. The source of truth for what the server was told.")]
        public MapBakedAsset asset;

        [Tooltip("What the level looks like. Instantiated by the runtime map scene.")]
        public GameObject prefab;

        [Tooltip("Shown in the create-room list. Copied from the asset at bake time.")]
        public string displayName = string.Empty;

        public string description = string.Empty;

        [Tooltip("The map hash as baked, compared against the server's copy so a mismatch is named " +
                 "before a room is made rather than after connecting. Stored as long because " +
                 "Unity does not serialize uint in the inspector cleanly.")]
        public long bakedHash;

        [Tooltip("Open this scene instead of the shared runtime map scene. For the two levels whose " +
                 "scenes carry more than the map (SampleScene, MultiplayerTest).")]
        public string sceneOverride = string.Empty;

        /// <summary>What the lobby shows. Falls back to the id — never to a blank row.</summary>
        public string DisplayNameOrId =>
            string.IsNullOrEmpty(displayName) ? mapId : displayName;

        public uint BakedHash => unchecked((uint)bakedHash);

        /// <summary>Can this build actually put the level on screen?</summary>
        public bool IsPlayable => asset != null && (prefab != null || !string.IsNullOrEmpty(sceneOverride));
    }
}
