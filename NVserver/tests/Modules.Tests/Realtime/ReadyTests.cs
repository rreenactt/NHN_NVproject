using NV.Realtime;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 준비가 매치 시작의 조건이다.
    ///
    /// 규칙은 셋이고 각각 이유가 다르다 — 방장은 준비하지 않고(시작 버튼이 그 사람의 준비다),
    /// 봇은 세지 않고(요청을 보낼 입이 없다), 정적 룸은 조건 자체를 건너뛴다(두 클라이언트
    /// 개발 루프가 그 룸으로 돌아간다).
    public class ReadyTests
    {
        [Fact]
        public void 준비하지_않은_사람이_있으면_시작하지_않는다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1));
            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            Assert.Equal(RoomPhase.Waiting, room.Phase);
        }

        [Fact]
        public void 전원_준비하면_방장이_시작할_수_있다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1));
            RoomFixture.Ready(room, 2);
            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            Assert.Equal(RoomPhase.Playing, room.Phase);
        }

        /// 방장에게 토글을 요구하면 같은 뜻의 조작이 둘이 되고, 자기 준비를 잊은 방장이
        /// 시작하지 못하는 상태가 생긴다.
        [Fact]
        public void 방장은_준비하지_않고_시작한다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1));
            RoomFixture.Ready(room, 2);
            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            Assert.Equal(RoomPhase.Playing, room.Phase);
            Assert.False(ReadyOf(room, 1), "방장의 준비는 켜지지 않았어야 한다.");
        }

        /// 준비를 껐으면 다시 막혀야 한다. 켜는 것만 되면 되돌릴 수 없는 조작이 된다.
        [Fact]
        public void 준비를_끄면_다시_막힌다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1));
            RoomFixture.Ready(room, 2);
            room.Advance();

            room.PostCommand(RoomCommand.SetReady(2, false));
            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            Assert.Equal(RoomPhase.Waiting, room.Phase);
        }

        /// **매치 중의 준비는 받지 않는다.** 받아 두면 로비로 돌아온 순간 이미 준비된 사람이
        /// 있게 되고, `ResetToWaiting` 이 지우는 것과 정반대로 움직인다.
        [Fact]
        public void 매치_중의_준비는_무시된다()
        {
            var room = RoomFixture.Create();

            RoomFixture.FillAndStart(room);

            room.PostCommand(RoomCommand.SetReady(1, true));
            room.Advance();

            Assert.False(ReadyOf(room, 1));
        }

        /// 자리를 비운 사람을 데리고 다음 매치가 시작되지 않게 한다.
        [Fact]
        public void 로비로_되돌리면_준비가_전원_내려간다()
        {
            var room = RoomFixture.Create();

            RoomFixture.FillAndStart(room);
            Assert.True(ReadyOf(room, 2));

            room.PostCommand(RoomCommand.EndMatch(1, 1));
            room.Advance();
            room.PostCommand(RoomCommand.ReturnToLobby(1));
            room.Advance();

            Assert.False(ReadyOf(room, 2));

            // 그래서 다시 누르지 않으면 두 번째 매치가 시작되지 않는다.
            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            Assert.Equal(RoomPhase.Waiting, room.Phase);
        }

        /// 봇은 준비 요청을 보낼 입이 없다. 세면 개발용 방이 영구히 시작되지 않는다.
        [Fact]
        public void 봇이_있어도_시작할_수_있다()
        {
            var room = RoomFixture.WithBots(fillTo: RealtimeConstants.Rooms.MinPlayersToStart);

            RoomFixture.JoinHuman(room, 1);
            RoomFixture.SettleBots(room);

            Assert.True(room.PlayerCount >= RealtimeConstants.Rooms.MinPlayersToStart);
            Assert.True(room.BotCount > 0);

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            Assert.Equal(RoomPhase.Playing, room.Phase);
        }

        /// 한 명만 준비해도 안 된다. 명단을 **전부** 훑는지 보는 검사다 — 첫 번째에서
        /// 멈추는 구현은 2인 검사를 통과하고 3인에서만 샌다.
        [Fact]
        public void 셋_중_하나만_준비해도_시작하지_않는다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1));
            room.PostCommand(RoomCommand.Join(3, 2));
            room.PostCommand(RoomCommand.SetReady(2, true));
            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            Assert.Equal(RoomPhase.Waiting, room.Phase);
        }

        /// **정적 룸도 준비를 요구한다.**
        ///
        /// 한동안 건너뛰었다 — 두 클라이언트 개발 루프가 그 룸으로 돌아가므로 편의였다.
        /// 그런데 그 룸은 **공개**라서 방 목록과 빠른 참가가 보통 사람을 그리로 넣고, 그래서
        /// 개발용 예외가 개발용 경계 밖으로 샜다. 증상은 "아무도 레디를 안 눌렀는데 게임이
        /// 시작된다" 였다.
        [Fact]
        public void 정적_룸도_준비를_요구한다()
        {
            var room = RoomFixture.Create(isStatic: true);

            RoomFixture.JoinHuman(room, 1);
            RoomFixture.JoinHuman(room, 2);

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            Assert.Equal(RoomPhase.Waiting, room.Phase);

            // 게스트가 누르면 시작된다. 방장은 누르지 않는다 — START 가 그 사람의 준비다.
            room.PostCommand(RoomCommand.SetReady(2, true));
            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            Assert.Equal(RoomPhase.Playing, room.Phase);
        }

        /// 명단에 없는 세션의 준비는 조용히 버린다. 없는 사람의 준비를 적어 둘 자리가 없다.
        [Fact]
        public void 참가하지_않은_세션의_준비는_무시된다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1));
            room.PostCommand(RoomCommand.SetReady(99, true));
            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            // 세션 2 는 여전히 준비하지 않았다.
            Assert.Equal(RoomPhase.Waiting, room.Phase);
        }

        /// 명단 전문이 준비를 실어 보내는가. 화면이 읽는 것은 이 비트다.
        [Fact]
        public void 준비는_명단_전문에_실린다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1));
            RoomFixture.Ready(room, 2);
            room.Advance();
            room.Broadcast(transport);

            Assert.True(transport.TryLastRoomState(1, out _, out var roster));
            Assert.Equal(2, roster.Length);
            Assert.False(roster[0].IsReady);
            Assert.True(roster[1].IsReady);
        }

        /// 봇 여부도 명단 전문에 실린다. 이름으로 짐작하게 두면 스스로 `BOT 1` 이라고
        /// 이름을 지은 사람과 구분되지 않는다.
        [Fact]
        public void 봇_여부는_명단_전문에_실린다()
        {
            var room = RoomFixture.WithBots(fillTo: RealtimeConstants.Rooms.MinPlayersToStart);
            var transport = new RecordingTransport();

            RoomFixture.JoinHuman(room, 1);
            RoomFixture.SettleBots(room);
            room.Broadcast(transport);

            Assert.True(transport.TryLastRoomState(1, out _, out var roster));

            var humans = 0;
            var bots = 0;

            foreach (var entry in roster)
            {
                if (entry.IsBot)
                {
                    bots++;
                }
                else
                {
                    humans++;
                }
            }

            Assert.Equal(1, humans);
            Assert.True(bots > 0);
        }

        /// 아무도 미배정으로 남지 않고, 서로 다른 캐릭터를 입는다.
        ///
        /// 입장 시 배정은 규칙이 아니라 데이터다 — 고르지 않은 사람을 만들지 않으면
        /// "고르지 않으면 시작할 수 없다" 는 규칙을 하나 더 만들 필요가 없다.
        [Fact]
        public void 입장하면_남는_캐릭터를_받는다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            room.PostCommand(RoomCommand.Join(1, 0, "host", true));
            room.PostCommand(RoomCommand.Join(2, 1));
            room.PostCommand(RoomCommand.Join(3, 2));
            room.Advance();
            room.Broadcast(transport);

            Assert.True(transport.TryLastRoomState(1, out _, out var roster));
            Assert.Equal(3, roster.Length);

            var seen = new System.Collections.Generic.HashSet<byte>();

            foreach (var entry in roster)
            {
                Assert.NotEqual(NV.Shared.Contracts.Messages.RoomPlayerEntry.NoCharacter, entry.CharacterId);
                Assert.True(seen.Add(entry.CharacterId), "같은 캐릭터를 두 사람이 입었다.");
            }
        }

        /// 명단 전문에서 이 세션의 준비 상태를 읽는다.
        private static bool ReadyOf(Room room, int sessionId)
        {
            return RoomFixture.EntryOf(room, sessionId).IsReady;
        }
    }
}
