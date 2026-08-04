using System.Collections.Generic;
using NV.Shared.Collision;
using UnityEngine;

namespace NV.Client.Map
{
    /// <summary>
    /// A whole level as data, before anything exists in the scene.
    ///
    /// This is the one intermediate representation. A generator produces it and touches no
    /// <see cref="UnityEngine.Object"/> doing so; the scene builder, the baked asset and the
    /// server export are all downstream of it and none of them can disagree about the level,
    /// because there is only one description of it.
    ///
    /// **That is the point of the type.** <c>BackroomsMapGenerator</c> currently has two paths
    /// through its geometry pass — one that builds GameObjects and one that only records collision
    /// — kept in step by both replaying the same seeded random in the same order. Nothing enforces
    /// that; it is a comment. With a blueprint there is one path, and what to do with the result is
    /// the caller's business.
    /// </summary>
    public sealed class MapBlueprint
    {
        /// <summary>
        /// Export filename, server <c>Game:Maps</c> key and <c>MapSceneTable</c> entry, all at
        /// once. Those four have to agree; <c>backrooms2f</c> is what happens when they do not.
        /// </summary>
        public string MapName;

        /// <summary>The seed this level actually came from. 0 for levels that draw no randomness.</summary>
        public int UsedSeed;

        /// <summary>
        /// Every piece of the level, in the order the generator emitted them.
        ///
        /// **The order is part of the map hash** — <c>MapData.ComputeHash</c> mixes the boxes in
        /// sequence. Reordering this list produces a different hash for identical terrain, and the
        /// only symptom is a mismatch warning on connect.
        /// </summary>
        public readonly List<MapPiece> Pieces = new List<MapPiece>();

        /// <summary>Where players start. Position is at the feet; yaw is radians with 0 at +Z.</summary>
        public readonly List<MapSpawnPoint> Spawns = new List<MapSpawnPoint>();

        /// <summary>
        /// The walkability grid, or <c>null</c> for a level that does not offer one.
        ///
        /// <c>null</c> is a normal answer, not a gap: a level that never runs the match rules has
        /// nothing to place. Only <see cref="MapCellFlags.Standable"/> and
        /// <see cref="MapCellFlags.StairLink"/> belong here —
        /// <see cref="MapCellFlags.FreeFloor"/> is filled in at export time by the *server's*
        /// collision code, because that flag means "the server can stand a player here".
        /// </summary>
        public MapGridData Grid;

        /// <summary>
        /// Why this level would not reproduce if generated again, or <c>null</c> if it would.
        ///
        /// Carried on the blueprint rather than asked of the generator afterwards, because it is a
        /// property of *this* result — a generator with a randomised seed produces a fine level and
        /// an unexportable one at the same time.
        /// </summary>
        public string Blocker;

        /// <summary>
        /// What each surface looks like.
        ///
        /// Colours, not <see cref="Material"/>s. A blueprint must not hold a reference to an asset
        /// — it is produced in the editor and consumed by a scene builder, a baked asset and an
        /// export, and only the first of those has any business with materials. Turning a colour
        /// into a shared material asset is the scene builder's job, and only it knows whether the
        /// result has to survive being saved into a prefab.
        /// </summary>
        public readonly Dictionary<MapSurface, Color> Palette = new Dictionary<MapSurface, Color>();

        /// <summary>Pieces that the server has to know about. The rest are decoration.</summary>
        public int CollidingPieceCount
        {
            get
            {
                var count = 0;
                for (var index = 0; index < Pieces.Count; index++)
                    if (Pieces[index].Collides) count++;
                return count;
            }
        }

        /// <summary>
        /// The collision boxes, in emission order, with the non-colliding pieces dropped.
        ///
        /// This is what a level hands the export, and it has to be built by walking
        /// <see cref="Pieces"/> in order — filtering does not disturb the order, but sorting or
        /// grouping would, and that changes the map hash.
        /// </summary>
        public void CollectCollisionBoxes(List<Bounds> into)
        {
            if (into == null) return;

            for (var index = 0; index < Pieces.Count; index++)
                if (Pieces[index].Collides) into.Add(Pieces[index].Bounds);
        }

        /// <summary>Records one box. The single way a generator adds anything.</summary>
        public void Add(string name, Vector3 centre, Vector3 size, MapSurface surface, bool collides)
        {
            Pieces.Add(new MapPiece
            {
                Name = name,
                Bounds = new Bounds(centre, size),
                Surface = surface,
                Collides = collides,
            });
        }
    }

    /// <summary>
    /// One box of level. Corresponds exactly to one <c>AddBox</c> call in the generators this
    /// replaces, which is what keeps the emitted order — and so the map hash — identical.
    /// </summary>
    public struct MapPiece
    {
        /// <summary>What it is called in the hierarchy. Not part of the hash.</summary>
        public string Name;

        public Bounds Bounds;

        /// <summary>
        /// What it should look like. A blueprint knows nothing about <see cref="Material"/> —
        /// deciding that is the scene builder's job, and a level's data must not carry a reference
        /// to an asset that only exists while the editor is open.
        /// </summary>
        public MapSurface Surface;

        /// <summary>
        /// Whether the server has to know about this box.
        ///
        /// Ceiling tiles and light panels are <c>false</c>: a grid of them would be a thousand
        /// colliders, they never stop anything, and a light panel that blocked shots would be a
        /// bug rather than a feature.
        /// </summary>
        public bool Collides;
    }

    /// <summary>Which look a piece takes. The scene builder maps these onto materials.</summary>
    public enum MapSurface
    {
        Wall = 0,
        Floor = 1,
        Ceiling = 2,
        Trim = 3,
        LightPanel = 4,
        Cover = 5,
    }

    /// <summary>Where a player starts. Feet position, and yaw in radians with 0 at +Z.</summary>
    public struct MapSpawnPoint
    {
        public Vector3 Position;
        public float Yaw;
    }
}
