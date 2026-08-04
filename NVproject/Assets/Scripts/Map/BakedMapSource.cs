using System.Collections.Generic;
using NV.Client.Net;
using NV.Shared.Collision;
using UnityEngine;

namespace NV.Client.Map
{
    /// <summary>
    /// A level that was generated in the editor and is only opened at runtime.
    ///
    /// It builds nothing and computes nothing — every answer comes off <see cref="asset"/>. That is
    /// the whole difference from <c>BackroomsMapGenerator</c>, and it removes three things at once:
    /// the second geometry path kept in step by replaying a seeded random, the grid that a domain
    /// reload wipes, and the collision rebuild that runs on the frame a client connects.
    ///
    /// Put it on the level's root object in place of a generator. Everything that used to ask the
    /// generator — the export pipeline through <see cref="INetworkMapSource"/>, the match layer
    /// through <c>ILevelQuery</c> — asks this instead.
    /// </summary>
    public sealed class BakedMapSource : MonoBehaviour, INetworkMapSource
    {
        [Tooltip("The baked level. Produced by Tools ▸ NV ▸ Map ▸ Map Generator.")]
        public MapBakedAsset asset;

        private static readonly Bounds[] NoBoxes = new Bounds[0];

        /// <inheritdoc />
        public string MapName => asset == null ? string.Empty : asset.MapName;

        /// <inheritdoc />
        public IReadOnlyList<Bounds> CollisionBoxes => asset == null ? NoBoxes : asset.Boxes;

        /// <inheritdoc />
        ///
        /// <remarks>
        /// Nothing to compute. The name is <c>INetworkMapSource</c>'s and it exists because a
        /// generated level cannot hand out collision in edit mode without dumping a whole level
        /// into the open scene; a baked one has had the answer on disk since it was baked.
        /// </remarks>
        public IReadOnlyList<Bounds> ComputeCollision() => CollisionBoxes;

        /// <inheritdoc />
        public void GetSpawns(List<(Vector3 position, float yaw)> into)
        {
            if (asset != null) asset.GetSpawns(into);
        }

        /// <inheritdoc />
        public MapGridData BuildGrid() => asset == null ? null : asset.BuildGrid();

        /// <inheritdoc />
        ///
        /// <remarks>
        /// A baked level reproduces by definition — it is not generated again, it is read. The one
        /// way it cannot be exported is having nothing to read.
        ///
        /// The seed is deliberately not checked here. Whether the *generator* was reproducible was
        /// decided when this was baked, and re-litigating it now would mean the asset carrying the
        /// generator's settings around for no other purpose.
        /// </remarks>
        public string DescribeExportBlocker()
        {
            if (asset != null) return null;

            return $"'{name}' 에 구운 맵 에셋이 없다. Tools ▸ NV ▸ Map ▸ Map Generator 에서 " +
                   "레벨을 구운 뒤 그 에셋을 이 컴포넌트에 물린다.";
        }
    }
}
