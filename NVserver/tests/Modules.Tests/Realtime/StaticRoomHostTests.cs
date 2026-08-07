using NV.Realtime.Simulation;
using NV.Shared.Contracts.Messages;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 정적 룸의 방장은 **먼저 들어온 사람 하나**다.
    ///
    /// 한동안 "전원이 방장" 이었다. 토큰을 받을 사람이 없는 개발용 룸에서 시작 버튼을
    /// 살리려고 전문을 **받는 사람마다** 자기 id 로 채웠던 것인데, 그 방은 공개라 로비
    /// 목록과 빠른 참가로도 들어올 수 있다 — 그렇게 만난 두 사람은 서로 자기가 방장인
    /// 방에 있게 되고 강퇴·위임·준비 게이트가 전부 뜻을 잃는다.
    ///
    /// `IsAuthorized` 는 정적 룸에서 여전히 전원의 요청을 받아들인다(`RoomTests`). 그것은
    /// 개발 편의이고, 화면에 그리는 방장은 한 명이어야 한다는 것과 다른 이야기다.
    public class StaticRoomHostTests
    {
        [Fact]
        public void 정적_룸에서도_방장은_한_명이다()
        {
            var room = RoomFixture.Create(isStatic: true);
            var transport = new RecordingTransport();

            var first = RoomFixture.JoinHuman(room, 1);
            RoomFixture.JoinHuman(room, 2);

            room.Advance();
            room.Broadcast(transport);

            // **둘이 같은 답을 봐야 한다.** 갈리면 서로 자기가 방장인 방이 된다.
            Assert.True(transport.TryLastRoomState(1, out var firstView, out var roster));
            Assert.Equal(first, firstView.HostPlayerId);

            Assert.True(transport.TryLastRoomState(2, out var secondView, out _));
            Assert.Equal(first, secondView.HostPlayerId);

            Assert.Equal(2, roster.Length);
        }

        /// 방장이 나가면 승계한다. **이것이 정적 룸을 특별 취급하지 않아도 되는 이유다** —
        /// 처음 이 자리를 비워 둔 근거가 "나갈 때마다 승계가 필요해진다" 였는데, 승계는
        /// 초대 코드 룸을 위해 이미 만들어져 있고 같은 코드가 여기서도 돈다.
        [Fact]
        public void 정적_룸의_방장이_나가면_승계한다()
        {
            var room = RoomFixture.Create(isStatic: true);
            var transport = new RecordingTransport();

            RoomFixture.JoinHuman(room, 1);
            var second = RoomFixture.JoinHuman(room, 2);

            room.Advance();

            room.PostCommand(RoomCommand.Leave(1, 0));
            room.Advance();
            room.Broadcast(transport);

            Assert.True(transport.TryLastRoomState(2, out var view, out _));
            Assert.Equal(second, view.HostPlayerId);
        }

        [Fact]
        public void 초대_코드_룸에서는_방장이_한_명이다()
        {
            // 이쪽은 바뀌지 않아야 한다. 세션별 인코딩이 정적 룸 밖으로 새면 모든 방에서
            // 전원이 방장이 되고, 그것은 시작 권한을 아무에게나 주는 것이다.
            var room = RoomFixture.Create(isStatic: false);
            var transport = new RecordingTransport();

            var host = RoomFixture.JoinHuman(room, 1, isHost: true);
            RoomFixture.JoinHuman(room, 2);

            room.Advance();
            room.Broadcast(transport);

            Assert.True(transport.TryLastRoomState(1, out var hostView, out _));
            Assert.Equal(host, hostView.HostPlayerId);

            Assert.True(transport.TryLastRoomState(2, out var guestView, out _));
            Assert.Equal(host, guestView.HostPlayerId);
        }

        [Fact]
        public void 봇은_방장로_보이지_않는다()
        {
            // 봇은 방장 후보가 아니다(`LowestRemainingSessionId`). 사람이 먼저 들어오든
            // 나중에 들어오든 방장은 그 사람이어야 한다 — 봇에게 가면 아무도 시작을
            // 요청하지 않는 방이 된다.
            var room = RoomFixture.WithBots();
            var transport = new RecordingTransport();

            var human = RoomFixture.JoinHuman(room, 1);
            RoomFixture.SettleBots(room);

            room.Broadcast(transport);

            Assert.True(transport.TryLastRoomState(1, out var view, out _));
            Assert.Equal(human, view.HostPlayerId);
            Assert.NotEqual(RoomStateHeader.NoPlayer, view.HostPlayerId);
        }
    }
}
