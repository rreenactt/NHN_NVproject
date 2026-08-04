using NV.Realtime.Simulation;
using NV.Shared.Contracts.Messages;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 정적 룸에서는 전원이 방장이다.
    ///
    /// **권한이 아니라 권한에 대한 진술을 검사한다.** `IsAuthorized` 는 정적 룸에서 처음부터
    /// 전원의 요청을 받아들였고(`RoomTests` 가 그것을 고정한다), 전문만 "방장 없음" 을
    /// 말하고 있었다. 클라이언트는 `RoomState.HostPlayerId == LocalPlayerId` 로 방장을
    /// 판단하므로 그 어긋남의 증상은 아무도 시작 버튼을 누를 수 없는 개발용 룸이다.
    public class StaticRoomHostTests
    {
        [Fact]
        public void 정적_룸에서는_각자_자기를_방장으로_본다()
        {
            var room = RoomFixture.Create(isStatic: true);
            var transport = new RecordingTransport();

            var first = RoomFixture.JoinHuman(room, 1);
            var second = RoomFixture.JoinHuman(room, 2);

            room.Advance();
            room.Broadcast(transport);

            Assert.True(transport.TryLastRoomState(1, out var firstView, out var roster));
            Assert.Equal(first, firstView.HostPlayerId);

            Assert.True(transport.TryLastRoomState(2, out var secondView, out _));
            Assert.Equal(second, secondView.HostPlayerId);

            // 방장만 갈린다. 명단은 그대로 전원의 것이어야 한다 — 세션별 인코딩이
            // 본문 전체를 바꾸면 화면의 참가자 목록이 사람마다 달라진다.
            Assert.Equal(2, roster.Length);
        }

        [Fact]
        public void 정적_룸의_방장은_아무도_나가지_않는다()
        {
            // 승계가 없다. 자기가 방장이므로 누가 나가도 자기 값이 바뀌지 않는다 —
            // 초대 코드 룸에서 방장이 나갈 때 벌어지는 일(승계)이 여기서는 필요 없다.
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
            // 봇에게는 전문이 가지 않으므로 자기를 방장으로 볼 일이 없고, 사람의 사본에도
            // 봇의 id 가 방장으로 실리지 않는다 — 각자 자기 id 를 받기 때문이다.
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
