using System;
using System.IO;
using NV.Infrastructure.FileSystem;
using NV.Shared.Collision;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 스키마 버전을 서버가 어떻게 읽는가.
    ///
    /// **해시로는 이것을 대신할 수 없다.** 맵 해시는 "같은 지형인가" 를 답하고 버전은 "이 파일을
    /// 읽을 수 있는가" 를 답한다. 새 필드는 해시에 들어가지 않으므로, 버전이 없으면 옛 서버가
    /// 새 파일을 조용히 기본값으로 읽고 증상은 그 기능이 그냥 안 도는 것이다.
    public class MapSchemaVersionTests
    {
        private const string BoxesAndSpawns =
            "\"boxes\": [ { \"minX\": -4, \"minY\": -1, \"minZ\": -4, \"maxX\": 4, \"maxY\": 0, \"maxZ\": 4 } ], " +
            "\"spawns\": [ { \"x\": 0, \"y\": 0, \"z\": 0, \"yaw\": 0 } ]";

        /// **버전 필드가 없는 파일을 거절하지 않는다.**
        ///
        /// 거절하면 버전을 도입하는 커밋에서 기존 맵 전부를 재-export 해야 하는데, 그 재-export
        /// 는 아무 정보도 늘리지 않는다. 격자를 해시에 조건부로 넣은 것과 같은 논리다.
        [Fact]
        public void 버전이_없는_파일은_1로_읽는다()
        {
            var map = LoadJson("{ \"name\": \"v\", " + BoxesAndSpawns + " }");

            Assert.Equal(MapSchema.Unversioned, map.Data.Version);
            Assert.Equal(1, MapSchema.Effective(map.Data.Version));
            Assert.True(MapSchema.IsReadable(map.Data.Version));
        }

        [Fact]
        public void 지금_버전은_로드된다()
        {
            var map = LoadJson(
                "{ \"version\": " + MapSchema.Current + ", \"name\": \"v\", " + BoxesAndSpawns + " }");

            Assert.Equal(MapSchema.Current, map.Data.Version);
        }

        /// **미래 버전은 기동을 실패시킨다.**
        ///
        /// 모르는 필드를 무시하고 읽으면 그 필드가 필요한 기능이 조용히 꺼진 채로 돌아간다.
        /// 기동을 멈추면 무엇이 문제인지 로그 한 줄로 끝난다.
        [Fact]
        public void 모르는_미래_버전은_기동을_실패시킨다()
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => LoadJson(
                    "{ \"version\": " + (MapSchema.Current + 1) + ", \"name\": \"v\", " + BoxesAndSpawns + " }"));

            Assert.Contains("스키마 버전", exception.Message);
        }

        /// 출처는 있으면 읽히고 없어도 된다. **해시에 들어가지 않는다** — 들어가면 재-export
        /// 마다 해시가 바뀌어 지형 대조가 뜻을 잃는다.
        [Fact]
        public void 출처는_읽히지만_해시를_바꾸지_않는다()
        {
            var without = LoadJson("{ \"name\": \"v\", " + BoxesAndSpawns + " }");

            var with = LoadJson(
                "{ \"version\": 1, \"name\": \"v\", " +
                "\"source\": { \"scene\": \"SampleScene\", \"component\": \"BackroomsMapGenerator\", " +
                "\"exportedAtUtc\": \"2026-08-04T00:00:00Z\", \"exporterVersion\": 1 }, " +
                BoxesAndSpawns + " }");

            Assert.NotNull(with.Data.Source);
            Assert.Equal("SampleScene", with.Data.Source.Scene);
            Assert.Equal("BackroomsMapGenerator", with.Data.Source.Component);
            Assert.Equal(1, with.Data.Source.ExporterVersion);

            Assert.Null(without.Data.Source);
            Assert.Equal(without.Hash, with.Hash);
        }

        /// 버전이 다르기만 해도 해시는 같다. 스키마는 지형이 아니다.
        [Fact]
        public void 버전은_해시를_바꾸지_않는다()
        {
            var unversioned = LoadJson("{ \"name\": \"v\", " + BoxesAndSpawns + " }");
            var versioned = LoadJson("{ \"version\": 1, \"name\": \"v\", " + BoxesAndSpawns + " }");

            Assert.Equal(unversioned.Hash, versioned.Hash);
        }

        private static WorldMap LoadJson(string json)
        {
            var path = Path.Combine(Path.GetTempPath(), $"nv-map-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, json);

            try
            {
                return MapLoader.Load(path);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
