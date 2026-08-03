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
    /// 룸은 명시적으로 만들어지고 초대 코드로만 참가한다.
    ///
    /// 예전에는 접속 쿼리의 룸 id 로 룸이 그 자리에서 생겼다. 그 구조에서는 코드를
    /// 모르는 사람도 아무 id 로 방을 만들 수 있고, 설정 파일이 코드를 미리 알 수 없어
    /// 모든 초대 코드 방이 기본 맵으로 조용히 열린다.
    public sealed class RoomRegistryTests
    {
        [Fact]
        public void 맵_id_로_맵이_갈린다()
        {
            var registry = Registry(out var maps, StaticRooms.Empty);

            Assert.True(registry.TryCreate(RoomMaps.DefaultMapId, out var defaultCode, out _, out _));
            Assert.True(registry.TryCreate("test-room", out var namedCode, out _, out _));

            Assert.True(registry.TryGet(defaultCode, out var defaultRoom));
            Assert.True(registry.TryGet(namedCode, out var namedRoom));

            Assert.Equal(maps.ByMapId(RoomMaps.DefaultMapId)!.Hash, defaultRoom.MapHash);
            Assert.Equal(maps.ByMapId("test-room")!.Hash, namedRoom.MapHash);
            Assert.NotEqual(defaultRoom.MapHash, namedRoom.MapHash);
        }

        [Fact]
        public void 등록되지_않은_맵은_거절한다()
        {
            var registry = Registry(out _, StaticRooms.Empty);

            Assert.False(registry.TryCreate("no-such-map", out _, out _, out var error));
            Assert.Equal(RoomCreateError.UnknownMap, error);

            // 기본 맵으로 대신 열면 방을 만든 사람은 자기가 무엇을 잘못 골랐는지
            // 알 수 없고, 증상은 맵 해시 불일치로만 나타난다.
            Assert.Empty(registry.ListRooms());
        }

        [Fact]
        public void 맵을_지정하지_않으면_기본_맵으로_만든다()
        {
            var registry = Registry(out var maps, StaticRooms.Empty);

            Assert.True(registry.TryCreate(null, out var code, out _, out _));
            Assert.True(registry.TryGet(code, out var room));
            Assert.Equal(maps.Default.Hash, room.MapHash);
        }

        [Fact]
        public void 만든_코드는_형식과_룸_id_규칙을_모두_만족한다()
        {
            var registry = Registry(out _, StaticRooms.Empty);

            Assert.True(registry.TryCreate(RoomMaps.DefaultMapId, out var code, out var token, out _));

            Assert.True(InviteCodeFormat.IsValid(code), code);

            // 코드가 룸 id 이기도 하다. 두 규칙이 갈리면 만든 코드로 접속할 수 없다.
            Assert.True(RoomRegistry.IsValidRoomId(code), code);

            Assert.Equal(32, token.Length);
        }

        [Fact]
        public void 없는_코드로는_룸을_얻을_수_없다()
        {
            var registry = Registry(out _, StaticRooms.Empty);

            Assert.False(registry.TryGet("qqqqqq", out _));
            Assert.False(registry.TryGet(null, out _));
        }

        [Fact]
        public void 방장_토큰만_방장_자격을_준다()
        {
            var registry = Registry(out _, StaticRooms.Empty);

            Assert.True(registry.TryCreate(RoomMaps.DefaultMapId, out var code, out var token, out _));
            Assert.True(registry.TryCreate(RoomMaps.DefaultMapId, out var otherCode, out var otherToken, out _));

            Assert.True(registry.IsHostToken(code, token));
            Assert.False(registry.IsHostToken(code, otherToken));
            Assert.False(registry.IsHostToken(otherCode, token));
            Assert.False(registry.IsHostToken(code, string.Empty));
            Assert.False(registry.IsHostToken(code, token + "0"));
        }

        [Fact]
        public void 정적_룸은_미리_열려_있고_방장이_없다()
        {
            var registry = Registry(out _, new StaticRooms(new Dictionary<string, string> { ["test"] = "test-room" }));

            Assert.True(registry.TryGet("test", out var room));
            Assert.True(room.IsStatic);

            // 코드를 발급받는 경로가 없으니 방장을 주장할 토큰도 없다.
            Assert.False(registry.IsHostToken("test", string.Empty));
        }

        [Fact]
        public void 정적_룸_설정이_어긋나면_기동을_멈춘다()
        {
            // 조용히 건너뛰면 개발용 룸이 없는 채로 서버가 올라가고, 증상은 접속이
            // "없는 방" 으로 거부되는 것뿐이라 설정 오타를 찾는 데 시간이 걸린다.
            Assert.Throws<InvalidOperationException>(
                () => Registry(out _, new StaticRooms(new Dictionary<string, string> { ["TEST"] = "test-room" })));

            Assert.Throws<InvalidOperationException>(
                () => Registry(out _, new StaticRooms(new Dictionary<string, string> { ["test"] = "no-such-map" })));
        }

        [Fact]
        public void 룸_상한을_넘으면_만들지_못한다()
        {
            var registry = Registry(out _, StaticRooms.Empty);

            for (var index = 0; index < RealtimeConstants.Rooms.MaxRooms; index++)
            {
                Assert.True(registry.TryCreate(RoomMaps.DefaultMapId, out _, out _, out _));
            }

            Assert.False(registry.TryCreate(RoomMaps.DefaultMapId, out _, out _, out var error));
            Assert.Equal(RoomCreateError.RoomLimit, error);
        }

        [Fact]
        public void 아무도_들어오지_않은_룸은_회수된다()
        {
            var registry = Registry(out _, new StaticRooms(new Dictionary<string, string> { ["test"] = "test-room" }));

            Assert.True(registry.TryCreate(RoomMaps.DefaultMapId, out var code, out _, out _));

            registry.Sweep(RealtimeConstants.Rooms.EmptyExpiryTicks - 1u);
            Assert.True(registry.TryGet(code, out _));

            registry.Sweep(RealtimeConstants.Rooms.EmptyExpiryTicks);
            Assert.False(registry.TryGet(code, out _));

            // 정적 룸은 남는다. 사라지면 다음 테스트에서 다시 만들 방법이 없다.
            Assert.True(registry.TryGet("test", out _));
        }

        [Fact]
        public void 사람이_있는_룸은_회수되지_않는다()
        {
            var registry = Registry(out _, StaticRooms.Empty);

            Assert.True(registry.TryCreate(RoomMaps.DefaultMapId, out var code, out _, out _));
            Assert.True(registry.TryGet(code, out var room));

            room.PostCommand(RoomCommand.Join(1, 0));
            room.Advance();

            registry.Sweep(RealtimeConstants.Rooms.EmptyExpiryTicks * 10u);

            Assert.True(registry.TryGet(code, out _));
        }

        [Fact]
        public void 전원이_나간_룸은_대기로_돌아간_뒤_회수된다()
        {
            var registry = Registry(out _, StaticRooms.Empty);

            Assert.True(registry.TryCreate(RoomMaps.DefaultMapId, out var code, out _, out _));
            Assert.True(registry.TryGet(code, out var room));

            RoomFixture.FillAndStart(room);

            room.PostCommand(RoomCommand.Leave(1, 0));
            room.PostCommand(RoomCommand.Leave(2, 1));
            room.Advance();

            // 비어 있으면서 진행 중인 룸은 존재하지 않는다. 그래서 회수 기준이 하나다.
            Assert.Equal(NV.Shared.Contracts.Enums.RoomPhase.Waiting, room.Phase);

            registry.Sweep(RealtimeConstants.Rooms.EmptyExpiryTicks);
            Assert.False(registry.TryGet(code, out _));
        }

        [Fact]
        public void 요약에_단계와_맵이_실린다()
        {
            var registry = Registry(out _, StaticRooms.Empty);

            Assert.True(registry.TryCreate("test-room", out var code, out _, out _));
            Assert.True(registry.TryGetRoom(code, out var summary));

            Assert.Equal(code, summary.RoomId);
            Assert.Equal(RealtimeConstants.Rooms.MaxPlayers, summary.Capacity);
            Assert.Equal(0, summary.PlayerCount);
            Assert.False(summary.IsFull);
            Assert.Equal("test-room", summary.MapName);
            Assert.Equal(NV.Shared.Contracts.Enums.RoomPhase.Waiting, summary.Phase);
        }

        [Fact]
        public void 서로_다른_맵은_해시가_다르다()
        {
            // 해시가 크기 차이를 잡지 못하면 맵 배정을 검증할 수단이 사라진다.
            Assert.NotEqual(Map("arena", 20f).Hash, Map("arena", 21f).Hash);
        }

        private static RoomRegistry Registry(out RoomMaps maps, StaticRooms staticRooms)
        {
            maps = new RoomMaps(new Dictionary<string, WorldMap>
            {
                [RoomMaps.DefaultMapId] = Map("arena", 20f),
                ["test-room"] = Map("test-room", 8f),
            });

            return new RoomRegistry(maps, staticRooms, RoomFixture.NoConditions(), NullLogger<RoomRegistry>.Instance);
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
