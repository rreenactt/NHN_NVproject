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
    /// 등록된 맵마다 `test-{맵 id}` 룸이 자동으로 열린다.
    ///
    /// 맵 등록이 "파일을 쓰면 끝"(디렉터리 스캔)이므로 테스트 룸도 그래야 한다.
    /// 여기서 고정하는 규칙은 셋이다 — 명시 룸이 여는 맵은 건너뛴다, 같은 id 의
    /// 명시 룸이 이긴다, 자동 룸도 정적 룸의 규칙 전부를 따른다.
    public sealed class PerMapTestRoomTests
    {
        [Fact]
        public void 맵마다_테스트_룸이_열리고_공개다()
        {
            var registry = Registry(out var maps, StaticRooms.Empty, perMap: true);

            Assert.True(registry.TryGet("test-arena", out var arena));
            Assert.True(registry.TryGet("test-maze", out var maze));

            Assert.True(arena.IsStatic);
            Assert.True(maze.IsStatic);
            Assert.Equal(maps.ByMapId("arena")!.Hash, arena.MapHash);
            Assert.Equal(maps.ByMapId("maze")!.Hash, maze.MapHash);

            // 공개가 아니면 로비 목록에서 여기에 닿을 길이 없다 — 명시 정적 룸과 같은 규칙이다.
            Assert.Equal(2, registry.ListPublicRooms().Count);
        }

        [Fact]
        public void 꺼져_있으면_명시_룸만_열린다()
        {
            var registry = Registry(
                out _,
                new StaticRooms(new Dictionary<string, string> { ["test"] = "arena" }),
                perMap: false);

            Assert.True(registry.TryGet("test", out _));
            Assert.False(registry.TryGet("test-arena", out _));
            Assert.False(registry.TryGet("test-maze", out _));
        }

        [Fact]
        public void 명시_룸이_여는_맵은_건너뛴다()
        {
            var registry = Registry(
                out _,
                new StaticRooms(new Dictionary<string, string> { ["test"] = "arena" }),
                perMap: true);

            // `test` 가 이미 arena 를 연다. `test-arena` 까지 생기면 같은 맵의
            // 테스트 룸이 둘이 되고, 어느 쪽이 진짜인지 목록으로는 알 수 없다.
            Assert.False(registry.TryGet("test-arena", out _));
            Assert.True(registry.TryGet("test-maze", out _));
        }

        [Fact]
        public void 별칭으로_적은_명시_룸도_그_맵을_가린다()
        {
            // `default` 는 arena 의 별칭이다. 풀지 않고 문자열로 비교하면
            // 별칭으로 적은 룸이 있는데도 `test-arena` 가 또 생긴다.
            var registry = Registry(
                out _,
                new StaticRooms(new Dictionary<string, string> { ["test"] = RoomMaps.DefaultMapId }),
                perMap: true);

            Assert.False(registry.TryGet("test-arena", out _));
            Assert.True(registry.TryGet("test-maze", out _));
        }

        [Fact]
        public void 같은_id_의_명시_룸이_이긴다()
        {
            // 사용자가 `test-arena` 라는 id 를 직접 적었다. 자동 생성이 그것을 덮으면
            // 설정에 적은 맵(maze)이 조용히 무시된다.
            var registry = Registry(
                out var maps,
                new StaticRooms(new Dictionary<string, string> { ["test-arena"] = "maze" }),
                perMap: true);

            Assert.True(registry.TryGet("test-arena", out var room));
            Assert.Equal(maps.ByMapId("maze")!.Hash, room.MapHash);
        }

        [Fact]
        public void 격자_없는_맵의_자동_룸은_봇이_서_있는다()
        {
            // 격자 없는 맵에서 Wander 는 어차피 서 있는다(`BotBrain` 이 방어한다).
            // 내리는 것은 기동 로그를 정직하게 만드는 일이다 — 설정이 Wander 라고
            // 말하는데 봇이 서 있으면 그 차이를 버그로 찾게 된다.
            var registry = Registry(out _, StaticRooms.Empty, perMap: true, behavior: BotBehavior.Wander);

            Assert.True(registry.TryGet("test-arena", out var withGrid));
            Assert.True(registry.TryGet("test-maze", out var gridless));

            Assert.Equal(BotBehavior.Wander, withGrid.Bots.Behavior);
            Assert.Equal(BotBehavior.Idle, gridless.Bots.Behavior);
        }

        [Fact]
        public void 자동_룸은_회수되지_않는다()
        {
            var registry = Registry(out _, StaticRooms.Empty, perMap: true);

            registry.Sweep(RealtimeConstants.Rooms.UnjoinedExpiryTicks * 10u);

            Assert.True(registry.TryGet("test-arena", out _));
            Assert.True(registry.TryGet("test-maze", out _));
        }

        /// arena 는 격자가 있는 맵, maze 는 격자가 없는 맵이다. `default` 는 arena 의 별칭.
        private static RoomRegistry Registry(
            out RoomMaps maps,
            StaticRooms staticRooms,
            bool perMap,
            BotBehavior behavior = BotBehavior.Idle)
        {
            maps = new RoomMaps(
                new Dictionary<string, WorldMap>
                {
                    ["arena"] = RoomFixture.Map(withGrid: true),
                    ["maze"] = RoomFixture.Map(withGrid: false),
                },
                new Dictionary<string, string>
                {
                    [RoomMaps.DefaultMapId] = "arena",
                });

            var options = new RealtimeOptions
            {
                Bots = new BotOptions
                {
                    Enabled = true,
                    Behavior = behavior,
                },
            };

            return new RoomRegistry(
                maps,
                new StaticRooms(staticRooms.Rooms, perMap),
                RoomFixture.NoConditions(),
                options,
                NullLogger<RoomRegistry>.Instance);
        }
    }
}
