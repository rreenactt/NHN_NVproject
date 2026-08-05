using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using NV.Realtime;
using NV.Realtime.Contracts;
using NV.Realtime.Simulation;
using NV.Shared.Collision;
using NV.Shared.Contracts;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 정적 룸이 자기 봇 구성을 갖는다. 전역 설정은 기본값이고 프로필이 그 위를 덮는다.
    ///
    /// 봇 설정이 전역 하나였을 때는 정적 룸이 둘이 되는 순간 같은 행동이 강제됐다.
    /// 이 테스트들이 고정하는 것은 두 가지다 — 겹치기의 방향(프로필이 이긴다, 생략은
    /// 전역이다)과, 그 결과가 룸까지 닿는다는 것.
    public sealed class TestRoomProfileTests
    {
        [Fact]
        public void 생략한_필드는_전역을_따른다()
        {
            var global = new BotOptions
            {
                Enabled = true,
                FillTo = 3,
                Behavior = BotBehavior.Wander,
                Role = BotRolePreference.Seeker,
                Seed = 7u,
            };

            var resolved = new TestRoomProfile("test-room").ResolveBots(global);

            Assert.True(resolved.Enabled);
            Assert.Equal(3, resolved.FillTo);
            Assert.Equal(BotBehavior.Wander, resolved.Behavior);
            Assert.Equal(BotRolePreference.Seeker, resolved.Role);
            Assert.Equal(7u, resolved.Seed);
        }

        [Fact]
        public void 적은_필드가_전역을_덮는다()
        {
            var global = new BotOptions
            {
                Enabled = true,
                FillTo = 2,
                Behavior = BotBehavior.Idle,
                Role = BotRolePreference.Runner,
                Seed = 0u,
            };

            var profile = new TestRoomProfile(
                "test-room",
                fillTo: 4,
                behavior: BotBehavior.Objective,
                role: BotRolePreference.Seeker,
                seed: 99u);

            var resolved = profile.ResolveBots(global);

            Assert.Equal(4, resolved.FillTo);
            Assert.Equal(BotBehavior.Objective, resolved.Behavior);
            Assert.Equal(BotRolePreference.Seeker, resolved.Role);
            Assert.Equal(99u, resolved.Seed);

            // 전역 객체는 그대로다. 겹치기가 전역을 고쳐 쓰면 한 룸의 오버라이드가
            // 다른 룸의 기본값이 된다.
            Assert.Equal(2, global.FillTo);
            Assert.Equal(BotBehavior.Idle, global.Behavior);
        }

        [Fact]
        public void 봇_스위치는_전역만이_정한다()
        {
            // `TestRoomProfile` 에는 Enabled 를 적을 자리 자체가 없다. 이 테스트가
            // 고정하는 것은 겹치기가 그 값을 만들어 내지 않는다는 것이다 — 꺼진 전역이
            // 프로필을 지나며 켜지면 `GuardDevelopmentOnlyOptions` 의 방어선이 무너진다.
            var profile = new TestRoomProfile("test-room", fillTo: 4);

            Assert.False(profile.ResolveBots(new BotOptions { Enabled = false }).Enabled);
            Assert.True(profile.ResolveBots(new BotOptions { Enabled = true }).Enabled);
        }

        [Fact]
        public void 맵_id_없는_프로필은_거부된다()
        {
            Assert.Throws<ArgumentException>(() => new TestRoomProfile(string.Empty));
        }

        [Fact]
        public void 문자열_형태는_프로필_생략과_같다()
        {
            // 옛 설정(룸 id → 맵 id 문자열)이 그대로 돌아야 한다. 겹칠 것이 없으므로
            // 룸의 실효 설정은 전역과 같다.
            var registry = Registry(
                GlobalBots(fillTo: 3, behavior: BotBehavior.Wander),
                new StaticRooms(new Dictionary<string, string> { ["test"] = "test-room" }));

            Assert.True(registry.TryGet("test", out var room));
            Assert.Equal(3, room.Bots.FillTo);
            Assert.Equal(BotBehavior.Wander, room.Bots.Behavior);
        }

        [Fact]
        public void 룸마다_다른_봇_구성이_적용된다()
        {
            var registry = Registry(
                GlobalBots(fillTo: 2, behavior: BotBehavior.Idle),
                new StaticRooms(new Dictionary<string, TestRoomProfile>
                {
                    ["test"] = new TestRoomProfile("test-room"),
                    ["test-more"] = new TestRoomProfile(
                        "test-room",
                        fillTo: 4,
                        behavior: BotBehavior.Wander),
                }));

            Assert.True(registry.TryGet("test", out var plain));
            Assert.True(registry.TryGet("test-more", out var custom));

            Assert.Equal(2, plain.Bots.FillTo);
            Assert.Equal(BotBehavior.Idle, plain.Bots.Behavior);
            Assert.Equal(4, custom.Bots.FillTo);
            Assert.Equal(BotBehavior.Wander, custom.Bots.Behavior);
        }

        [Fact]
        public void 프로필의_채움_인원이_실제_봇_수를_정한다()
        {
            // 설정이 룸까지 닿는 것과 룸이 그것으로 움직이는 것은 다른 문장이다.
            // 사람 하나가 들어오면 프로필의 FillTo 까지 봇이 채워져야 한다.
            var registry = Registry(
                GlobalBots(fillTo: 2, behavior: BotBehavior.Idle),
                new StaticRooms(new Dictionary<string, TestRoomProfile>
                {
                    ["test-more"] = new TestRoomProfile("test-room", fillTo: 4),
                }));

            Assert.True(registry.TryGet("test-more", out var room));

            RoomFixture.JoinHuman(room, 1);
            RoomFixture.SettleBots(room, 4);

            Assert.Equal(3, room.BotCount);
            Assert.Equal(4, room.PlayerCount);
        }

        private static RealtimeOptions GlobalBots(int fillTo, BotBehavior behavior)
        {
            return new RealtimeOptions
            {
                Bots = new BotOptions
                {
                    Enabled = true,
                    FillTo = fillTo,
                    Behavior = behavior,
                },
            };
        }

        private static RoomRegistry Registry(RealtimeOptions options, StaticRooms staticRooms)
        {
            var maps = new RoomMaps(new Dictionary<string, WorldMap>
            {
                [RoomMaps.DefaultMapId] = Map("arena", 20f),
                ["test-room"] = Map("test-room", 8f),
            });

            return new RoomRegistry(
                maps,
                staticRooms,
                RoomFixture.NoConditions(),
                options,
                NullLogger<RoomRegistry>.Instance);
        }

        private static WorldMap Map(string name, float half)
        {
            return new WorldMap(new MapData
            {
                Name = name,
                Boxes = new[]
                {
                    new MapBox
                    {
                        MinX = -half,
                        MinY = -1f,
                        MinZ = -half,
                        MaxX = half,
                        MaxY = 0f,
                        MaxZ = half,
                    },
                },
                Spawns = new[]
                {
                    new MapSpawn { X = 0f, Y = 0f, Z = 0f, Yaw = 0f },
                    new MapSpawn { X = -2f, Y = 0f, Z = 0f, Yaw = 0f },
                },
            });
        }
    }
}
