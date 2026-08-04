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
    public sealed class BakedMapSource : MonoBehaviour, INetworkMapSource, ILevelQuery
    {
        [Tooltip("The baked level. Produced by Tools ▸ NV ▸ Map ▸ Map Generator.")]
        public MapBakedAsset asset;

        [Tooltip("The shared wall material, so the freeze device's x-ray has something to fade. " +
                 "Assigned at bake time; leave it alone.")]
        public Material wallMaterial;

        private static readonly Bounds[] NoBoxes = new Bounds[0];

        /// <summary>
        /// The grid, rebuilt from the asset on first use.
        ///
        /// A plain field would be wiped by a domain reload mid-play while <see cref="asset"/> — a
        /// UnityEngine.Object reference — survives, so this is rebuilt on demand rather than in
        /// <c>Awake</c>. That is the same failure <c>BackroomsMapGenerator.EnsureGrid</c> exists to
        /// paper over; here it costs a copy of a byte array instead of re-solving a maze.
        /// </summary>
        private MapGridData _grid;

        /// <summary>
        /// The wall material this level actually renders with — an instance, not the asset.
        ///
        /// <see cref="SetWallTransparency"/> writes shader keywords and the render queue, and doing
        /// that to the shared asset in the editor **persists it into the project**: leave play mode
        /// mid-freeze and the walls are transparent for everybody, for good.
        /// </summary>
        private Material _wallInstance;

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
        public MapMetaInfo BuildMeta() => asset == null ? null : asset.BuildMeta();

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

        // ================================================================ ILevelQuery

        /// <inheritdoc />
        public int GridSize => Grid == null ? 0 : Grid.Width;

        /// <inheritdoc />
        public int FloorCount => Grid == null ? 1 : Mathf.Max(1, Grid.Floors);

        /// <inheritdoc />
        public Vector3 SpawnCentre => asset == null ? Vector3.zero : asset.SpawnCentre;

        /// <inheritdoc />
        public bool HasGrid => Grid != null;

        /// <inheritdoc />
        ///
        /// <remarks>Nothing to do — the grid is an asset, and assets survive a domain reload.</remarks>
        public void EnsureGrid()
        {
        }

        /// <inheritdoc />
        public bool IsStandable(int floor, int x, int z)
        {
            return Grid != null && Grid.Has(floor, x, z, MapCellFlags.Standable);
        }

        /// <inheritdoc />
        ///
        /// <remarks>
        /// Delegated to <c>MapGridData</c>, which is <c>Shared</c> — so this floors the division
        /// exactly the way the server does. Two copies of the "which storey" rule is how a jumping
        /// player ends up on the floor above in one of them.
        /// </remarks>
        public int FloorIndexAt(float worldY)
        {
            return Grid == null ? 0 : Grid.FloorIndexAt(worldY);
        }

        /// <inheritdoc />
        public bool TryWorldToCell(Vector3 world, out int floor, out int x, out int z)
        {
            if (Grid == null)
            {
                floor = 0;
                x = 0;
                z = 0;
                return false;
            }

            return Grid.TryWorldToCell(ToNumerics(world), out floor, out x, out z);
        }

        /// <inheritdoc />
        ///
        /// <remarks>
        /// Rejection sampling, like the generator's. Roughly half the grid is standable, so this
        /// lands in a couple of draws — cheaper than materialising every cell for one point.
        /// </remarks>
        public bool TryRandomPoint(System.Random random, out Vector3 point, float margin = 0.55f)
        {
            point = SpawnCentre;
            if (Grid == null || random == null) return false;

            for (var attempt = 0; attempt < 512; attempt++)
            {
                var f = random.Next(FloorCount);
                var x = random.Next(Grid.Width);
                var z = random.Next(Grid.Depth);

                if (!IsStandable(f, x, z)) continue;

                var centre = ToUnity(Grid.CellToWorld(f, x, z));
                var spread = Mathf.Max(0f, Grid.CellSize * 0.5f - margin);

                point = new Vector3(
                    centre.x + (float)(random.NextDouble() * 2.0 - 1.0) * spread,
                    centre.y,
                    centre.z + (float)(random.NextDouble() * 2.0 - 1.0) * spread);

                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public bool TryNearestStandablePoint(Vector3 near, out Vector3 point)
        {
            point = near;
            if (Grid == null || !TryWorldToCell(near, out var floor, out var cx, out var cz)) return false;

            for (var radius = 0; radius < Grid.Width; radius++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                for (var dz = -radius; dz <= radius; dz++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != radius) continue;   // ring only

                    int x = cx + dx, z = cz + dz;
                    if (!IsStandable(floor, x, z)) continue;

                    point = ToUnity(Grid.CellToWorld(floor, x, z));
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        ///
        /// <remarks>
        /// One material drives every wall, so this is a single material switch rather than a pass
        /// over a thousand renderers — which also means it cannot be done per player, and does not
        /// need to be: the freeze is global by rule.
        /// </remarks>
        public void SetWallTransparency(float alpha)
        {
            var material = WallInstance();
            if (material == null) return;

            var transparent = alpha < 0.999f;

            material.SetFloat("_Surface", transparent ? 1f : 0f);
            material.SetInt("_SrcBlend", (int)(transparent
                ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.One));
            material.SetInt("_DstBlend", (int)(transparent
                ? UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : UnityEngine.Rendering.BlendMode.Zero));
            material.SetInt("_ZWrite", transparent ? 0 : 1);
            material.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");

            if (transparent) material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            else material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");

            material.renderQueue = (int)(transparent
                ? UnityEngine.Rendering.RenderQueue.Transparent
                : UnityEngine.Rendering.RenderQueue.Geometry);

            var colour = material.color;
            colour.a = Mathf.Clamp01(alpha);
            material.color = colour;
        }

        // ================================================================ 잡동사니

        private MapGridData Grid => _grid ?? (_grid = asset == null ? null : asset.BuildGrid());

        /// <summary>
        /// Swaps the shared wall material for a private copy across every wall renderer, once.
        ///
        /// One pass over the children at the first x-ray rather than at <c>Awake</c>: most rounds
        /// never freeze, and a level of this size is a few thousand renderers to walk.
        /// </summary>
        private Material WallInstance()
        {
            if (_wallInstance != null) return _wallInstance;
            if (wallMaterial == null) return null;

            _wallInstance = new Material(wallMaterial) { name = wallMaterial.name + " (instance)" };

            var renderers = GetComponentsInChildren<MeshRenderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                if (renderers[index].sharedMaterial == wallMaterial)
                {
                    renderers[index].sharedMaterial = _wallInstance;
                }
            }

            return _wallInstance;
        }

        private static System.Numerics.Vector3 ToNumerics(Vector3 value)
        {
            return new System.Numerics.Vector3(value.x, value.y, value.z);
        }

        private static Vector3 ToUnity(System.Numerics.Vector3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }
    }
}
