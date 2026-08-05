using NV.Realtime;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 방장의 두 권한 — 강제 퇴장과 방장 위임.
    ///
    /// 둘 다 `IsAuthorized` 를 쓰지 않고 방장 세션인지 직접 본다. 그 함수는 정적 룸에서
    /// 전원에게 참을 돌려주는데 그것은 "시작을 누를 수 있다" 를 위한 예외이고, 남을
    /// 쫓아내는 권한은 다른 것이다.
    public class HostPowerTests
    {
        [Fact]
        public void 방장이_내보내면_명단에서_빠지고_소켓이_끊긴다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1, "guest"));
            room.Advance();

            room.PostCommand(RoomCommand.Kick(1, 1));
            room.Advance();
            room.Broadcast(transport);

            Assert.Equal(1, room.PlayerCount);
            Assert.Contains(RealtimeConstants.Kick.Reason, transport.Disconnected);
        }

        /// 끊기만 하면 그 세션의 수신 펌프가 끝날 때까지 명단에 남는다. 명단에서만 빼면
        /// 소켓이 살아 스냅샷 없는 방에 붙어 있는 클라이언트가 된다.
        [Fact]
        public void 내보낸_사람은_명단_전문에서도_사라진다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1, "guest"));
            room.Advance();

            room.PostCommand(RoomCommand.Kick(1, 1));
            room.Advance();

            var roster = RoomFixture.RosterOf(room, 1);

            Assert.Single(roster);
            Assert.Equal(0, roster[0].PlayerId);
        }

        [Fact]
        public void 방장이_아니면_내보낼_수_없다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1, "guest"));
            room.Advance();

            room.PostCommand(RoomCommand.Kick(2, 0));
            room.Advance();
            room.Broadcast(transport);

            Assert.Equal(2, room.PlayerCount);
            Assert.Empty(transport.Disconnected);
        }

        /// 정적 룸은 시작 권한만 전원에게 열려 있다. 쫓아내는 권한은 아니다.
        [Fact]
        public void 정적_룸에서는_아무도_내보낼_수_없다()
        {
            var room = RoomFixture.Create(isStatic: true);
            var transport = new RecordingTransport();

            RoomFixture.JoinHuman(room, 1);
            RoomFixture.JoinHuman(room, 2);
            room.Advance();

            room.PostCommand(RoomCommand.Kick(1, 1));
            room.Advance();
            room.Broadcast(transport);

            Assert.Equal(2, room.PlayerCount);
            Assert.Empty(transport.Disconnected);
        }

        /// 방장이 자기를 내보내는 것은 나가기이며, 그 경로는 소켓 종료다.
        [Fact]
        public void 방장은_자기를_내보낼_수_없다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1, "guest"));
            room.Advance();

            room.PostCommand(RoomCommand.Kick(1, 0));
            room.Advance();
            room.Broadcast(transport);

            Assert.Equal(2, room.PlayerCount);
            Assert.Empty(transport.Disconnected);
        }

        [Fact]
        public void 없는_대상은_아무_일도_일어나지_않는다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1, "guest"));
            room.Advance();

            room.PostCommand(RoomCommand.Kick(1, 7));
            room.Advance();
            room.Broadcast(transport);

            Assert.Equal(2, room.PlayerCount);
            Assert.Empty(transport.Disconnected);
        }

        /// **강제 퇴장은 퇴장을 두 번 만든다.** 룸이 명단에서 빼고, 그 뒤 실제로 소켓이 닫히면
        /// 접속 경로가 같은 세션의 퇴장을 한 번 더 붙인다.
        ///
        /// 두 번째 호출이 슬롯을 그냥 반납하면 그 사이에 들어온 사람의 번호를 푼다 — 그러면
        /// 다음에 들어온 사람이 같은 번호를 또 받아 한 룸에 같은 `PlayerId` 가 둘이 된다.
        [Fact]
        public void 강제_퇴장_뒤_늦은_퇴장이_남의_슬롯을_풀지_않는다()
        {
            var room = RoomFixture.Create();

            // **둘 다 슬롯을 예약해서 넣는다.** 손으로 `PlayerId` 를 정하면 예약되지 않은
            // 슬롯이 남아, 뒤에 예약으로 들어오는 쪽이 이미 쓰이는 번호를 집는다 — 이 테스트가
            // 확인하려는 것이 바로 번호 충돌이므로 설정에서 그것을 만들면 안 된다.
            RoomFixture.JoinHuman(room, 1, isHost: true);
            var kicked = RoomFixture.JoinHuman(room, 2);
            room.Advance();

            room.PostCommand(RoomCommand.Kick(1, kicked));
            room.Advance();

            // 슬롯이 풀렸으므로 새 클라이언트가 그 번호를 받는다.
            var replacement = RoomFixture.JoinHuman(room, 3);
            room.Advance();

            Assert.Equal(kicked, replacement);

            // 이제 내보내진 쪽의 소켓이 닫힌다. 접속 경로가 옛 세션·옛 슬롯으로 퇴장을 붙인다.
            room.PostCommand(RoomCommand.Leave(2, kicked));
            room.Advance();

            // 새 사람은 그대로 있어야 하고, 그 번호는 아직 예약된 상태여야 한다.
            Assert.Equal(2, room.PlayerCount);
            Assert.True(room.TryReserveSlot(out var next), "남은 슬롯이 있어야 한다.");
            Assert.NotEqual(replacement, next);
        }

        // ==================================================== 방장 위임

        [Fact]
        public void 방장을_넘기면_시작_권한이_함께_옮겨진다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1, "guest"));
            room.Advance();

            room.PostCommand(RoomCommand.TransferHost(1, 1));
            room.Advance();

            Assert.Equal(1, HostOf(room, 1));

            // 옛 방장은 이제 시작할 수 없다. 준비까지 맞춰 두고도 거부되어야 한다.
            RoomFixture.Ready(room, 1);
            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            Assert.Equal(RoomPhase.Waiting, room.Phase);

            // 새 방장은 시작할 수 있다.
            room.PostCommand(RoomCommand.Start(2));
            room.Advance();

            Assert.Equal(RoomPhase.Playing, room.Phase);
        }

        [Fact]
        public void 방장이_아니면_넘길_수_없다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1, "guest"));
            room.Advance();

            room.PostCommand(RoomCommand.TransferHost(2, 1));
            room.Advance();

            Assert.Equal(0, HostOf(room, 1));
        }

        /// 봇은 아무 요청도 보내지 않는다. 넘기면 그 방은 시작할 수 없는 방이 된다 —
        /// 승계 규칙이 봇을 후보에서 빼는 것과 같은 이유다.
        [Fact]
        public void 봇에게는_넘길_수_없다()
        {
            var room = RoomFixture.WithBots(fillTo: RealtimeConstants.Rooms.MinPlayersToStart);

            var host = RoomFixture.JoinHuman(room, 1, isHost: true);
            RoomFixture.SettleBots(room);

            var roster = RoomFixture.RosterOf(room, 1);
            var bot = byte.MaxValue;

            foreach (var entry in roster)
            {
                if (entry.IsBot)
                {
                    bot = entry.PlayerId;
                    break;
                }
            }

            Assert.NotEqual(byte.MaxValue, bot);

            room.PostCommand(RoomCommand.TransferHost(1, bot));
            room.Advance();

            Assert.Equal(host, HostOf(room, 1));
        }

        /// 명단 전문의 방장 바이트. 클라이언트가 자기 id 와 비교해 방장을 판단하는 값이다.
        private static byte HostOf(Room room, int sessionId)
        {
            for (var guard = 0; guard <= RealtimeConstants.Rooms.RoomStateIntervalTicks + 1; guard++)
            {
                var transport = new RecordingTransport();
                room.Broadcast(transport);

                if (transport.TryLastRoomState(sessionId, out var header, out _))
                {
                    return header.HostPlayerId;
                }

                room.Advance();
            }

            Assert.Fail("명단 전문이 나가지 않았다.");
            return 0;
        }
    }
}
