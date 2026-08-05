using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using NV.Realtime.Contracts;
using NV.Realtime.Simulation;
using NV.Realtime.Transport;
using NV.Shared.Collision;
using NV.Shared.Contracts;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 참가 전 조회가 룸을 찾는 규칙. 원문 id 가 먼저, 사람이 옮겨 적은 코드의
    /// 정돈본이 다음이다.
    ///
    /// `InviteCodeFormat.Normalize` 는 하이픈을 버리므로(붙여넣은 코드의 구분선),
    /// 정돈본만으로 찾으면 하이픈이 든 정적 룸(`test-backrooms`)이 **목록에는
    /// 실리는데 조회는 404** 가 된다. 프리플라이트가 조회이므로 그 증상은
    /// "목록에 보이는 방에 참가가 안 된다" 다.
    public sealed class RoomLookupTests
    {
        [Fact]
        public void 하이픈이_든_정적_룸을_원문으로_찾는다()
        {
            var registry = Registry(
                new StaticRooms(new Dictionary<string, string> { ["test-backrooms"] = "test-room" }));

            var raw = "test-backrooms";
            var normalized = InviteCodeFormat.Normalize(raw);

            // 정돈본은 이미 다른 문자열이다. 이것이 이 테스트가 지키는 전제다 —
            // 같아지면 아래 단정은 아무것도 재지 않는다.
            Assert.Equal("testbackrooms", normalized);

            Assert.True(RealtimeEndpoints.TryFindRoom(registry, raw, normalized, out var summary));
            Assert.Equal("test-backrooms", summary.RoomId);
        }

        [Fact]
        public void 구분선이_섞인_초대_코드를_정돈본으로_찾는다()
        {
            var registry = Registry(StaticRooms.Empty);

            Assert.True(registry.TryCreate(RoomMaps.DefaultMapId, isPublic: false, out var code, out _, out _));

            // 사람이 "abc-def" 처럼 구분선을 넣어 옮겨 적은 경우다. 원문은 그 id 의
            // 방이 없고, 정돈본이 코드와 일치한다.
            var pasted = code.Substring(0, 3) + "-" + code.Substring(3);

            Assert.True(RealtimeEndpoints.TryFindRoom(
                registry,
                pasted,
                InviteCodeFormat.Normalize(pasted),
                out var summary));

            Assert.Equal(code, summary.RoomId);
        }

        [Fact]
        public void 어느_해석에도_없는_방은_찾지_못한다()
        {
            var registry = Registry(StaticRooms.Empty);

            Assert.False(RealtimeEndpoints.TryFindRoom(
                registry,
                "no-such-room",
                InviteCodeFormat.Normalize("no-such-room"),
                out _));
        }

        private static RoomRegistry Registry(StaticRooms staticRooms)
        {
            var maps = new RoomMaps(new Dictionary<string, WorldMap>
            {
                [RoomMaps.DefaultMapId] = RoomFixture.Map(withGrid: false),
                ["test-room"] = RoomFixture.Map(withGrid: true),
            });

            return new RoomRegistry(
                maps,
                staticRooms,
                RoomFixture.NoConditions(),
                new RealtimeOptions(),
                NullLogger<RoomRegistry>.Instance);
        }
    }
}
