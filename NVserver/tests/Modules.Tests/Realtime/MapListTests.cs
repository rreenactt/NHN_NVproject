using System;
using System.Collections.Generic;
using NV.Realtime;
using NV.Realtime.Contracts;
using NV.Realtime.Transport;
using NV.Shared.Collision;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 별칭이 풀리는지, 그리고 맵 목록 응답이 무엇을 말하는지 본다.
    public sealed class MapListTests
    {
        // ==================================================== 별칭

        [Fact]
        public void 별칭이_맵으로_풀린다()
        {
            var maps = Two();

            Assert.Equal("alpha", maps.ResolveId("default"));
            Assert.Same(maps.ByMap["alpha"], maps.ByMapId("default"));
            Assert.Equal("alpha", maps.DefaultId);
        }

        [Fact]
        public void 맵_id_는_그대로_풀린다()
        {
            var maps = Two();

            Assert.Equal("beta", maps.ResolveId("beta"));
            Assert.Same(maps.ByMap["beta"], maps.ByMapId("beta"));
        }

        /// 맵을 지정하지 않은 요청이다. 예전부터 기본 맵으로 답해 왔다.
        [Fact]
        public void 빈_id_는_기본_맵이다()
        {
            var maps = Two();

            Assert.Equal("alpha", maps.ResolveId(null));
            Assert.Same(maps.ByMap["alpha"], maps.ByMapId(string.Empty));
        }

        /// 기본 맵으로 대신 열지 않는다. 요청한 맵과 다른 지형으로 방이 열리면 증상이 맵 해시
        /// 불일치 하나로 나타나고, 방을 만든 사람은 자기가 무엇을 잘못 골랐는지 알 수 없다.
        [Fact]
        public void 모르는_id_는_거절이다()
        {
            var maps = Two();

            Assert.Null(maps.ResolveId("gamma"));
            Assert.Null(maps.ByMapId("gamma"));
            Assert.False(maps.IsRegistered("gamma"));
        }

        [Fact]
        public void 없는_맵을_가리키는_별칭은_거절이다()
        {
            Assert.Throws<ArgumentException>(() => new RoomMaps(
                new Dictionary<string, WorldMap>(StringComparer.Ordinal) { ["alpha"] = Map("alpha") },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["default"] = "nope" }));
        }

        /// `default` 로 풀리는 것이 없으면 맵을 지정하지 않은 요청 전부가 실패하고,
        /// 증상은 방 만들기가 안 되는 것으로만 나타난다.
        [Fact]
        public void 기본_맵이_없으면_거절이다()
        {
            Assert.Throws<ArgumentException>(() => new RoomMaps(
                new Dictionary<string, WorldMap>(StringComparer.Ordinal) { ["alpha"] = Map("alpha") }));
        }

        /// 단일 맵 생성자는 맵을 **자기 이름으로** 등록하고 `default` 를 별칭으로 붙인다.
        /// 맵 자체를 `default` 로 등록하면 id 와 이름이 다시 갈린다.
        [Fact]
        public void 단일_맵은_자기_이름으로_등록된다()
        {
            var maps = new RoomMaps(Map("alpha"));

            Assert.Equal(new[] { "alpha" }, new List<string>(maps.ByMap.Keys));
            Assert.Equal("alpha", maps.DefaultId);
            Assert.Same(maps.Default, maps.ByMapId("default"));
        }

        // ==================================================== 목록 응답

        [Fact]
        public void 목록은_id_순이고_기본_맵을_표시한다()
        {
            var payload = new MapListPayload(Two());

            Assert.Equal(2, payload.Maps.Length);
            Assert.Equal("alpha", payload.Maps[0].Id);
            Assert.Equal("beta", payload.Maps[1].Id);
            Assert.True(payload.Maps[0].IsDefault);
            Assert.False(payload.Maps[1].IsDefault);
        }

        [Fact]
        public void 목록이_지형의_해시와_규모를_싣는다()
        {
            var maps = Two();
            var payload = new MapListPayload(maps);

            Assert.Equal(maps.ByMap["alpha"].Hash, payload.Maps[0].Hash);
            Assert.Equal(maps.ByMap["alpha"].Collision.BoxCount, payload.Maps[0].BoxCount);
            Assert.Equal(1, payload.Maps[0].SpawnCount);
            Assert.Equal(RealtimeConstants.Rooms.MinPlayersToStart, payload.Maps[0].RecommendedPlayersMin);
            Assert.Equal(RealtimeConstants.Rooms.MaxPlayers, payload.Maps[0].RecommendedPlayersMax);
        }

        /// 격자가 없는 맵에서는 목표물을 배치할 수 없다 — 열쇠도 문도 생기지 않는다.
        /// 그 판정은 서버가 하고 클라이언트는 결론만 받는다.
        [Fact]
        public void 격자가_없으면_매치를_지원하지_않는다()
        {
            var payload = new MapListPayload(Two());

            Assert.False(payload.Maps[0].HasGrid);
            Assert.False(payload.Maps[0].SupportsMatch);
            Assert.Equal(0, payload.Maps[0].Floors);
        }

        /// 표시용 이름이 없는 맵은 id 로 답한다. 빈 문자열을 주면 화면에 빈 줄이 생긴다.
        [Fact]
        public void 표시용_이름이_없으면_id_다()
        {
            var payload = new MapListPayload(Two());

            Assert.Equal("alpha", payload.Maps[0].DisplayName);
            Assert.Equal(string.Empty, payload.Maps[0].Description);
        }

        [Fact]
        public void 버전이_없는_맵은_1로_읽힌다()
        {
            var payload = new MapListPayload(Two());

            Assert.Equal(1, payload.Maps[0].SchemaVersion);
        }

        /// ETag 는 본문에서 나온다. 맵 목록이 다르면 값도 달라야 하고, 같으면 같아야 한다 —
        /// 흔들리면 로비가 화면에 들어올 때마다 전체 본문을 받는다.
        [Fact]
        public void ETag_는_본문에_따라_정해진다()
        {
            var same = new MapListPayload(Two()).ETag;
            var again = new MapListPayload(Two()).ETag;

            var one = new MapListPayload(new RoomMaps(Map("alpha"))).ETag;

            Assert.Equal(same, again);
            Assert.NotEqual(same, one);
            Assert.StartsWith("\"", same);
        }

        [Fact]
        public void 본문은_camelCase_다()
        {
            var json = new MapListPayload(Two()).Json;

            Assert.Contains("\"id\":\"alpha\"", json);
            Assert.Contains("\"supportsMatch\":false", json);
            Assert.Contains("\"isDefault\":true", json);
        }

        // ==================================================== 도구

        /// `default` 가 `alpha` 를 가리키는 두 맵.
        private static RoomMaps Two()
        {
            return new RoomMaps(
                new Dictionary<string, WorldMap>(StringComparer.Ordinal)
                {
                    ["alpha"] = Map("alpha"),
                    ["beta"] = Map("beta"),
                },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["default"] = "alpha" });
        }

        private static WorldMap Map(string name)
        {
            return new WorldMap(new MapData
            {
                Name = name,
                Boxes = new[]
                {
                    new MapBox { MinX = -8f, MinY = -1f, MinZ = -8f, MaxX = 8f, MaxY = 0f, MaxZ = 8f },
                },
                Spawns = new[] { new MapSpawn { X = 0f, Y = 0f, Z = 0f, Yaw = 0f } },
            });
        }
    }
}
