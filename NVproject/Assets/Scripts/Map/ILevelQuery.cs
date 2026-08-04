using UnityEngine;

namespace NV.Client.Map
{
    /// <summary>
    /// What the match layer is allowed to ask a level.
    ///
    /// The rules need answers the renderer never had to give — where can a key sit, where does a
    /// shot Runner land, which storey is that height on. Until now those answers came off
    /// <c>BackroomsMapGenerator</c> by name, so <c>MatchManager</c>, <c>MatchBootstrap</c>,
    /// <c>DeviceSystem</c>, <c>GameHudController</c> and <c>MatchMapView</c> all held that one
    /// concrete type. That is the single thing standing between this project and a level that is
    /// generated in the editor and merely *opened* at runtime: a baked level answers every
    /// question here, and none of them by re-running a generator.
    ///
    /// **This is deliberately the surface those five files already used, and nothing more.**
    /// A level knows plenty of other things (cell size, exit centre, every standable cell); adding
    /// them here would be inventing callers that do not exist, and every one of them is a member a
    /// second implementation has to get right.
    /// </summary>
    /// <remarks>
    /// **Implementations must be <see cref="MonoBehaviour"/>s.** Unity cannot serialize an
    /// interface-typed field, so the components that hold a level keep a <c>MonoBehaviour</c> field
    /// — which survives a domain reload and shows up in the inspector — and view it through this
    /// interface. That also keeps Unity's "destroyed object compares equal to null" behaviour
    /// working at the field, which a plain interface reference silently loses.
    /// </remarks>
    public interface ILevelQuery
    {
        /// <summary>Cells per side.</summary>
        int GridSize { get; }

        /// <summary>Stacked storeys. At least 1.</summary>
        int FloorCount { get; }

        /// <summary>Centre of the spawn room, on the ground floor.</summary>
        Vector3 SpawnCentre { get; }

        /// <summary>
        /// True once this level can answer the questions below.
        ///
        /// Not the same as "the level exists": a generated level's grid is plain managed memory
        /// that a domain reload wipes while the geometry, being made of UnityEngine.Objects,
        /// survives. Call <see cref="EnsureGrid"/> first if you are about to read this.
        /// </summary>
        bool HasGrid { get; }

        /// <summary>
        /// Makes <see cref="HasGrid"/> true again if it can, without rebuilding any geometry.
        ///
        /// A baked level has nothing to do here. A generated one re-solves from its seed, which
        /// reproduces exactly the grid the standing geometry was built from.
        /// </summary>
        void EnsureGrid();

        /// <summary>Can something stand in this cell?</summary>
        bool IsStandable(int floor, int x, int z);

        /// <summary>
        /// Which storey a world height belongs to — the one whose floor is *below* you, so this
        /// floors the division rather than rounding it.
        /// </summary>
        int FloorIndexAt(float worldY);

        /// <summary>Nearest cell to a world position, whether or not that cell is standable.</summary>
        bool TryWorldToCell(Vector3 world, out int floor, out int x, out int z);

        /// <summary>
        /// A random standable point, jittered inside its cell. The caller owns the
        /// <see cref="System.Random"/>: the match seed has to stay separate from the level seed, or
        /// moving a key would reshape the walls.
        /// </summary>
        bool TryRandomPoint(System.Random random, out Vector3 point, float margin = 0.55f);

        /// <summary>Nearest standable point to somewhere that may be inside a wall.</summary>
        bool TryNearestStandablePoint(Vector3 near, out Vector3 point);

        /// <summary>Fades the walls out for the freeze device's x-ray. 1 is opaque.</summary>
        void SetWallTransparency(float alpha);
    }
}
