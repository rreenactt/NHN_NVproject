using Microsoft.Extensions.Logging.Abstractions;
using NV.Realtime;
using NV.Realtime.Contracts;
using NV.Realtime.Simulation;
using NV.Shared.Collision;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 룸별 맵 배정.
    ///
    /// 맵이 서버 전체에 하나뿐이면 클라이언트가 다른 씬을 열 때마다 서버 설정을 바꾸고
    /// 재기동해야 한다. 그 왕복을 잊으면 증상이 맵 해시 불일치 하나로만 나타나고,
    /// 서버는 보이지 않는 지형으로 이동을 판정한다.
    public sealed class RoomMapsTests
    {
        [Fact]
        public void 룸_id_로_맵이_갈린다()
        {
            var arena = Map("arena", 20f);
            var testRoom = Map("test-room", 8f);

            var registry = Registry(new RoomMaps(arena, new System.Collections.Generic.Dictionary<string, WorldMap>
            {
                { "test", testRoom },
            }));

            var defaultRoom = registry.GetOrCreate(RealtimeConstants.Rooms.DefaultRoomId);
            var named = registry.GetOrCreate("test");

            Assert.NotNull(defaultRoom);
            Assert.NotNull(named);
            Assert.Equal(arena.Hash, defaultRoom!.MapHash);
            Assert.Equal(testRoom.Hash, named!.MapHash);
            Assert.NotEqual(defaultRoom.MapHash, named.MapHash);
        }

        [Fact]
        public void 등록되지_않은_룸은_기본_맵을_쓴다()
        {
            var arena = Map("arena", 20f);
            var registry = Registry(new RoomMaps(arena));

            var room = registry.GetOrCreate("something-else");

            Assert.NotNull(room);
            Assert.Equal(arena.Hash, room!.MapHash);
        }

        [Fact]
        public void 서로_다른_맵은_해시가_다르다()
        {
            // 해시가 크기 차이를 잡지 못하면 룸별 맵 배정을 검증할 수단이 사라진다.
            Assert.NotEqual(Map("arena", 20f).Hash, Map("arena", 21f).Hash);
        }

        private static RoomRegistry Registry(RoomMaps maps)
        {
            return new RoomRegistry(maps, RoomFixture.NoConditions(), NullLogger<RoomRegistry>.Instance);
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
                },
            });
        }
    }
}
