using System.Collections.Generic;
using NV.Shared.Collision;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// `MapSpawn.Team` — 스폰의 역할 구분 (0 = 역할 무관, 1 = Seeker 전용, 2 = Runner 전용).
    ///
    /// 스폰은 맵 해시 밖이므로 이 필드는 해시를 바꾸지 않고, 기존 파일은 team 없이 0 으로
    /// 읽힌다 — 하위 호환이 이 테스트의 절반이다.
    public class MapSpawnTeamTests
    {
        /// team 을 적지 않은 맵(기존 파일 전부)에서 Runner 조회는 전체 조회와 같아야 한다.
        /// 다르면 이 필드의 도입만으로 기존 맵의 배치가 바뀐 것이다.
        [Fact]
        public void team이_없으면_Runner_조회가_전체_조회와_같다()
        {
            var map = new WorldMap(TwoPlainSpawns());

            Assert.Equal(0, map.SeekerSpawnCount);

            for (var index = 0; index < map.SpawnCount + 2; index++)
            {
                Assert.Equal(map.SpawnPosition(index), map.RunnerSpawnPosition(index));
                Assert.Equal(map.SpawnYaw(index), map.RunnerSpawnYaw(index));
            }
        }

        [Fact]
        public void Seeker_전용_스폰은_Runner_조회에서_빠진다()
        {
            var data = TwoPlainSpawns();
            data.Spawns = new[]
            {
                data.Spawns[0],
                new MapSpawn { X = 9f, Y = 0f, Z = 9f, Yaw = 1f, Team = 1 },
                data.Spawns[1],
            };

            var map = new WorldMap(data);

            Assert.Equal(1, map.SeekerSpawnCount);
            Assert.Equal(9f, map.SeekerSpawnPosition(0).X);

            // Runner 목록은 두 개뿐이고, 어떤 인덱스로 감아도 Seeker 자리(9,0,9)가 나오지 않는다.
            for (var index = 0; index < 6; index++)
            {
                Assert.NotEqual(9f, map.RunnerSpawnPosition(index).X);
            }
        }

        [Fact]
        public void 모르는_team_값은_검증이_거절한다()
        {
            var data = TwoPlainSpawns();
            data.Spawns[0].Team = 3;

            var errors = new List<string>();

            Assert.False(MapDataValidator.TryValidateSchema(data, errors));
            Assert.Contains(errors, error => error.Contains("team"));
        }

        /// Seeker 는 한 명이고 나머지 전원이 Runner 다 — Runner 가 설 곳 없는 맵은 실수다.
        [Fact]
        public void 전부_Seeker_전용이면_검증이_거절한다()
        {
            var data = TwoPlainSpawns();
            data.Spawns[0].Team = 1;
            data.Spawns[1].Team = 1;

            var errors = new List<string>();

            Assert.False(MapDataValidator.TryValidateSchema(data, errors));
            Assert.Contains(errors, error => error.Contains("Runner"));
        }

        private static MapData TwoPlainSpawns()
        {
            return new MapData
            {
                Name = "test",
                Boxes = new[]
                {
                    new MapBox { MinX = -20f, MinY = -1f, MinZ = -20f, MaxX = 20f, MaxY = 0f, MaxZ = 20f },
                },
                Spawns = new[]
                {
                    new MapSpawn { X = 0f, Y = 0f, Z = 0f, Yaw = 0f },
                    new MapSpawn { X = -2f, Y = 0f, Z = 0f, Yaw = 0f },
                },
            };
        }
    }
}
