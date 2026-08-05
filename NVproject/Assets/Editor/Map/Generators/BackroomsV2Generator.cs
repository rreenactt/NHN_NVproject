using System;
using NV.Client.Map;
using UnityEngine;

namespace NV.Client.EditorTools.Generators
{
    /// <summary>
    /// Backrooms V2 — a single-storey, open-plan concrete floor, as data.
    ///
    /// Written from the <see cref="IMapGenerator"/> contract alone: it reuses no code, no
    /// algorithm and no numbers from the original Backrooms generators, by requirement
    /// (<c>NVserver/docs/backrooms-v2-plan.md</c> §1). Where the original solves layout by
    /// stamping anchor rooms and repairing connectivity afterwards, this one will zone the floor
    /// by BSP and open doorways along a spanning tree, so connectivity holds by construction.
    ///
    /// Phase 1 skeleton: perimeter shell, floor and ceiling slabs, and the spawn ring. The BSP
    /// interior, the walkability grid and the light plan land in the next phase — the point of
    /// this file today is that the registry lists the type and a preview generates.
    /// </summary>
    public sealed class BackroomsV2Generator : IMapGenerator
    {
        public string DisplayName => "Backrooms V2";

        public string DefaultMapName => "backrooms-v2";

        public Type SettingsType => typeof(BackroomsV2Settings);

        public MapBlueprint Generate(MapGeneratorSettings settings)
        {
            var v2 = settings as BackroomsV2Settings;

            if (v2 == null)
            {
                throw new ArgumentException(
                    $"BackroomsV2Generator 는 BackroomsV2Settings 를 읽는다. {settings?.GetType().Name ?? "null"} 을 받았다.",
                    nameof(settings));
            }

            var blueprint = new MapBlueprint
            {
                MapName = v2.mapName,
                UsedSeed = v2.ResolveSeed(),
                Grid = null,
                Blocker = v2.DescribeBlocker(),
            };

            blueprint.Palette[MapSurface.Wall] = v2.wallColor;
            blueprint.Palette[MapSurface.Floor] = v2.floorColor;
            blueprint.Palette[MapSurface.Ceiling] = v2.ceilingColor;
            blueprint.Palette[MapSurface.Trim] = v2.trimColor;
            blueprint.Palette[MapSurface.LightPanel] = v2.lightColor;

            BuildShell(blueprint, v2);
            BuildSpawns(blueprint, v2);

            return blueprint;
        }

        /// <summary>
        /// Floor slab, four perimeter walls, ceiling slab — in that order, and the order is
        /// frozen: emission order is the map hash. Interior pieces will be emitted between the
        /// perimeter and the ceiling, so adding them later inserts into the sequence at one
        /// declared place instead of reshuffling it.
        /// </summary>
        private static void BuildShell(MapBlueprint blueprint, BackroomsV2Settings v2)
        {
            var span = v2.gridSize * v2.cellSize;
            var half = span * 0.5f;
            var wallY = v2.ceilingHeight * 0.5f;
            var outer = span + v2.wallThickness * 2f;
            var edge = half + v2.wallThickness * 0.5f;

            blueprint.Add("Floor", new Vector3(0f, -0.1f, 0f),
                new Vector3(span, 0.2f, span), MapSurface.Floor, true);

            blueprint.Add("Perimeter +Z", new Vector3(0f, wallY, edge),
                new Vector3(outer, v2.ceilingHeight, v2.wallThickness), MapSurface.Wall, true);
            blueprint.Add("Perimeter -Z", new Vector3(0f, wallY, -edge),
                new Vector3(outer, v2.ceilingHeight, v2.wallThickness), MapSurface.Wall, true);
            blueprint.Add("Perimeter +X", new Vector3(edge, wallY, 0f),
                new Vector3(v2.wallThickness, v2.ceilingHeight, outer), MapSurface.Wall, true);
            blueprint.Add("Perimeter -X", new Vector3(-edge, wallY, 0f),
                new Vector3(v2.wallThickness, v2.ceilingHeight, outer), MapSurface.Wall, true);

            // One collide=true lid over the whole floor. A storey with no storey above it is
            // otherwise open sky, and a player on top of anything climbable can jump out of the
            // level. One box costs one collider.
            blueprint.Add("Ceiling Lid", new Vector3(0f, v2.ceilingHeight + 0.1f, 0f),
                new Vector3(span, 0.2f, span), MapSurface.Ceiling, true);
        }

        /// <summary>
        /// Eight spawns — the map contract: the server picks a spawn by PlayerId and the exported
        /// map tests assert exactly eight. A ring around the floor centre, everyone facing in,
        /// until the BSP pass decides a real spawn zone.
        /// </summary>
        private static void BuildSpawns(MapBlueprint blueprint, BackroomsV2Settings v2)
        {
            const int spawnCount = 8;
            var radius = v2.cellSize * 2.4f;

            blueprint.SpawnCentre = Vector3.zero;

            for (var index = 0; index < spawnCount; index++)
            {
                // 0 is +Z, clockwise — the server's yaw convention.
                var angle = index * (2f * Mathf.PI / spawnCount);

                // Facing the centre, wound into [0, 2pi) by hand rather than left to a range
                // reduction we do not control.
                var yaw = angle + Mathf.PI;
                if (yaw >= 2f * Mathf.PI) yaw -= 2f * Mathf.PI;

                blueprint.Spawns.Add(new MapSpawnPoint
                {
                    Position = new Vector3(
                        radius * Mathf.Sin(angle),
                        0f,
                        radius * Mathf.Cos(angle)),
                    Yaw = yaw,
                });
            }
        }
    }
}
