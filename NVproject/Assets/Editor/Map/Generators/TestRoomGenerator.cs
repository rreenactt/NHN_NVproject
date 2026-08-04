using System;
using NV.Client.Map;
using UnityEngine;

namespace NV.Client.EditorTools.Generators
{
    /// <summary>
    /// The multiplayer test arena, as data.
    ///
    /// A straight port of <c>TestRoomMap.BuildGeometry</c> and <c>TestRoomMap.GetSpawns</c>, and
    /// **it has to stay a straight port**: the first thing the new pipeline has to prove is that it
    /// produces a byte-identical <c>NVserver/MapData/test-room.json</c>. That is the cheapest
    /// regression test available for a change this shape — if the bytes match, the box order, every
    /// float, the spawn ring and the absence of a grid all match at once.
    ///
    /// The emission order is therefore load-bearing: floor, four walls, centre platform, four cover
    /// blocks. It is part of the map hash.
    /// </summary>
    public sealed class TestRoomGenerator : IMapGenerator
    {
        public string DisplayName => "Test Room";

        public string DefaultMapName => "test-room";

        public Type SettingsType => typeof(TestRoomSettings);

        public MapBlueprint Generate(MapGeneratorSettings settings)
        {
            var room = settings as TestRoomSettings;

            if (room == null)
            {
                throw new ArgumentException(
                    $"TestRoomGenerator 는 TestRoomSettings 를 읽는다. {settings?.GetType().Name ?? "null"} 을 받았다.",
                    nameof(settings));
            }

            var blueprint = new MapBlueprint
            {
                MapName = room.mapName,
                UsedSeed = 0,                     // draws no randomness; a seed here would be a lie
                Grid = null,

                // The arena is centred on the origin and the spawn ring surrounds it. Nothing reads
                // this today — the match rules never run here — but leaving it at a default that
                // happens to be right by accident would be worse than saying so.
                SpawnCentre = Vector3.zero,
                Blocker = room.DescribeBlocker(),
            };

            // The opposite of the Backrooms treatment: plain, evenly lit, nothing hidden. You are
            // looking for a network artefact here, and a mood that obscures distance obscures it too.
            blueprint.Palette[MapSurface.Floor] = new Color(0.42f, 0.44f, 0.47f);
            blueprint.Palette[MapSurface.Wall] = new Color(0.62f, 0.63f, 0.66f);
            blueprint.Palette[MapSurface.Cover] = new Color(0.72f, 0.55f, 0.28f);

            BuildGeometry(blueprint, room);
            BuildSpawns(blueprint, room);

            return blueprint;
        }

        /// <summary>
        /// Floor, four walls, centre platform, four cover blocks — in that order.
        ///
        /// It earns the cover blocks and the platform: a flat floor tests nothing. The blocks are
        /// what a player slides along and gets stopped by, and the platform is the only thing here
        /// that tests standing on top of geometry rather than on the floor slab.
        ///
        /// The platform and cover dimensions are literals rather than settings, exactly as they are
        /// in <c>TestRoomMap</c>. Promoting them to fields would change no value today and would
        /// give the parity check a way to drift.
        /// </summary>
        private static void BuildGeometry(MapBlueprint blueprint, TestRoomSettings room)
        {
            var half = room.floorSize * 0.5f;
            var span = room.floorSize + room.wallThickness * 2f;
            var wallY = room.wallHeight * 0.5f;
            var edge = half + room.wallThickness * 0.5f;

            blueprint.Add("Floor", new Vector3(0f, -0.1f, 0f),
                new Vector3(room.floorSize, 0.2f, room.floorSize), MapSurface.Floor, true);

            blueprint.Add("Wall +Z", new Vector3(0f, wallY, edge),
                new Vector3(span, room.wallHeight, room.wallThickness), MapSurface.Wall, true);
            blueprint.Add("Wall -Z", new Vector3(0f, wallY, -edge),
                new Vector3(span, room.wallHeight, room.wallThickness), MapSurface.Wall, true);
            blueprint.Add("Wall +X", new Vector3(edge, wallY, 0f),
                new Vector3(room.wallThickness, room.wallHeight, span), MapSurface.Wall, true);
            blueprint.Add("Wall -X", new Vector3(-edge, wallY, 0f),
                new Vector3(room.wallThickness, room.wallHeight, span), MapSurface.Wall, true);

            blueprint.Add("Platform", new Vector3(0f, 0.5f, 0f),
                new Vector3(6f, 1f, 6f), MapSurface.Cover, true);

            blueprint.Add("Cover +X+Z", new Vector3(8f, 0.75f, 8f),
                new Vector3(3f, 1.5f, 3f), MapSurface.Cover, true);
            blueprint.Add("Cover -X+Z", new Vector3(-8f, 0.75f, 8f),
                new Vector3(3f, 1.5f, 3f), MapSurface.Cover, true);
            blueprint.Add("Cover +X-Z", new Vector3(8f, 0.75f, -8f),
                new Vector3(3f, 1.5f, 3f), MapSurface.Cover, true);
            blueprint.Add("Cover -X-Z", new Vector3(-8f, 0.75f, -8f),
                new Vector3(3f, 1.5f, 3f), MapSurface.Cover, true);
        }

        /// <summary>
        /// Eight spawns on a ring, all facing the middle, so whoever connects is looking straight at
        /// everybody else.
        ///
        /// <c>Mathf.Sin</c>/<c>Mathf.Cos</c> and not <c>DeterministicMath</c>: this reproduces the
        /// values already in <c>test-room.json</c>, and the export writes floats round-trip exact.
        /// The determinism rule that bans them applies to <c>Shared</c>, where client and server
        /// both compute — nothing recomputes a spawn point.
        /// </summary>
        private static void BuildSpawns(MapBlueprint blueprint, TestRoomSettings room)
        {
            for (var index = 0; index < room.spawnCount; index++)
            {
                // 0 is +Z, clockwise. The server's move function uses the same convention.
                var angle = index * (2f * Mathf.PI / room.spawnCount);

                // Wound into [0, 2pi) rather than left to a range reduction we do not control.
                var yaw = angle + Mathf.PI;
                if (yaw >= 2f * Mathf.PI) yaw -= 2f * Mathf.PI;

                blueprint.Spawns.Add(new MapSpawnPoint
                {
                    Position = new Vector3(
                        room.spawnRadius * Mathf.Sin(angle),
                        0f,
                        room.spawnRadius * Mathf.Cos(angle)),
                    Yaw = yaw,
                });
            }
        }
    }
}
