using NV.Realtime;
using NV.Realtime.Contracts;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 소켓 없는 봇 참가자. 사람 한 명으로 매치를 돌리기 위한 것이다.
    ///
    /// 룸은 전송도 인증도 모르므로 여기서 검사하는 것이 실제 경로와 같다 — 봇을 넣는
    /// 것도 틱 루프이고, 테스트가 그 틱 루프다.
    public class BotTests
    {
        [Fact]
        public void 사람이_들어오면_정적_룸이_봇으로_채워진다()
        {
            var room = RoomFixture.WithBots();

            RoomFixture.JoinHuman(room, 1, isHost: true);
            RoomFixture.SettleBots(room);

            Assert.Equal(RealtimeConstants.Rooms.MinPlayersToStart, room.PlayerCount);
        }

        [Fact]
        public void 봇_이름은_슬롯_번호에서_나온다()
        {
            var room = RoomFixture.WithBots();

            RoomFixture.JoinHuman(room, 1, isHost: true);
            RoomFixture.SettleBots(room);

            var bot = Assert.Single(Bots(room));

            // 사람이 0번 슬롯을 쥐고 있으므로 봇은 1번이고, 표시는 1 을 더한 값이다.
            Assert.Equal("BOT 2", bot.Name);
            Assert.Equal(1, bot.PlayerId);

            // 세션 id 가 음수라는 것이 "봇인가" 의 유일한 근거다.
            Assert.True(bot.SessionId < 0);
        }

        [Fact]
        public void 사람이_없으면_봇을_채우지_않는다()
        {
            // 채우지 않는 이유는 절약이 아니다. 채우면 서버가 기동하자마자 빈 방에서
            // 봇끼리 매치가 돌고, 로그가 그것으로 덮인다.
            var room = RoomFixture.WithBots();

            RoomFixture.SettleBots(room, ticks: 8);

            Assert.Equal(0, room.PlayerCount);
        }

        [Fact]
        public void 초대_코드_룸에는_봇이_생기지_않는다()
        {
            // 설정이 켜져 있어도다. 참가자가 있는 룸은 회수되지 않으므로, 봇이 남은
            // 초대 코드 룸은 영구히 살아남는다.
            var room = RoomFixture.WithBots(isStatic: false);

            RoomFixture.JoinHuman(room, 1, isHost: true);
            RoomFixture.SettleBots(room, ticks: 8);

            Assert.Equal(1, room.PlayerCount);
        }

        [Fact]
        public void 설정이_꺼져_있으면_정적_룸에도_봇이_생기지_않는다()
        {
            var room = RoomFixture.WithBots(enabled: false);

            RoomFixture.JoinHuman(room, 1, isHost: true);
            RoomFixture.SettleBots(room, ticks: 8);

            Assert.Equal(1, room.PlayerCount);
        }

        [Fact]
        public void 사람_하나와_봇_하나로_매치가_시작된다()
        {
            // 이 테스트가 이 기능의 목적 전체다.
            var room = RoomFixture.WithBots();

            RoomFixture.JoinHuman(room, 1, isHost: true);
            RoomFixture.SettleBots(room);

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            Assert.Equal(RoomPhase.Playing, room.Phase);
            Assert.Equal(MatchPhase.RoleReveal, room.MatchPhase);
        }

        [Fact]
        public void 봇에게는_한_프레임도_보내지_않는다()
        {
            var room = RoomFixture.WithBots();
            var transport = new RecordingTransport();

            RoomFixture.JoinHuman(room, 1, isHost: true);
            RoomFixture.SettleBots(room);

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();
            RoomFixture.SkipReveal(room);

            for (var tick = 0; tick < 10; tick++)
            {
                room.Advance();
                room.Broadcast(transport);
            }

            var bot = Assert.Single(Bots(room));

            Assert.Equal(0, transport.CountFor(bot.SessionId));
            Assert.True(transport.CountFor(1) > 0);
        }

        [Fact]
        public void 봇도_스냅샷의_몸으로는_실린다()
        {
            // 보내지 않는 것과 실리지 않는 것은 다르다. 사람들이 봇의 몸을 봐야 한다.
            var room = RoomFixture.WithBots();
            var transport = new RecordingTransport();

            RoomFixture.JoinHuman(room, 1, isHost: true);
            RoomFixture.SettleBots(room);

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();
            RoomFixture.SkipReveal(room);

            room.Advance();
            room.Broadcast(transport);

            Assert.True(transport.TryLastSnapshot(1, out _, out var entities));
            Assert.Equal(2, entities.Length);

            Assert.True(transport.TryLastRoomState(1, out _, out var roster));
            Assert.Equal(2, roster.Length);
        }

        [Fact]
        public void 서_있는_봇은_수평으로_움직이지_않는다()
        {
            var room = RoomFixture.WithBots();

            RoomFixture.JoinHuman(room, 1, isHost: true);
            RoomFixture.SettleBots(room);

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();
            RoomFixture.SkipReveal(room);

            var bot = Assert.Single(Bots(room));
            var spawn = bot.State.Position;

            for (var tick = 0; tick < 60; tick++)
            {
                room.Advance();
            }

            // 수직은 확인하지 않는다 — 중력을 받아 바닥에 내려앉는 것이 맞고, 그것이
            // 봇도 같은 시뮬레이션을 지난다는 증거다.
            Assert.Equal(spawn.X, bot.State.Position.X, 3);
            Assert.Equal(spawn.Z, bot.State.Position.Z, 3);
        }

        [Fact]
        public void 봇이_Runner_희망이면_술래는_사람이다()
        {
            var room = RoomFixture.WithBots(role: BotRolePreference.Runner);
            var transport = new RecordingTransport();

            RoomFixture.JoinHuman(room, 1, isHost: true);
            RoomFixture.SettleBots(room);

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();
            room.Broadcast(transport);

            Assert.True(transport.TryLastRoomState(1, out var header, out _));
            Assert.Equal(0, header.SeekerPlayerId);
        }

        [Fact]
        public void 봇이_Seeker_희망이면_술래는_봇이다()
        {
            var room = RoomFixture.WithBots(role: BotRolePreference.Seeker);
            var transport = new RecordingTransport();

            RoomFixture.JoinHuman(room, 1, isHost: true);
            RoomFixture.SettleBots(room);

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();
            room.Broadcast(transport);

            var bot = Assert.Single(Bots(room));

            Assert.True(transport.TryLastRoomState(1, out var header, out _));
            Assert.Equal(bot.PlayerId, header.SeekerPlayerId);
        }

        [Fact]
        public void 마지막_사람이_나가면_봇도_사라지고_대기로_돌아간다()
        {
            // 남기면 빈 룸 판정이 성립하지 않아 단계가 되돌아가지 않고, 정적이 아닌
            // 룸이라면 회수도 되지 않는다.
            var room = RoomFixture.WithBots();

            RoomFixture.JoinHuman(room, 1, isHost: true);
            RoomFixture.SettleBots(room);

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            room.PostCommand(RoomCommand.Leave(1, 0));
            room.Advance();

            Assert.Equal(0, room.PlayerCount);
            Assert.Equal(RoomPhase.Waiting, room.Phase);
        }

        [Fact]
        public void 봇은_방장을_승계하지_않는다()
        {
            // 봇의 슬롯 번호가 남은 사람보다 작아지는 배치를 만든다. 그러지 않으면
            // 이 규칙이 검사되지 않는다 — 봇은 보통 사람보다 나중 슬롯을 받는다.
            var room = RoomFixture.WithBots(fillTo: 4);

            for (var index = 0; index < 3; index++)
            {
                RoomFixture.JoinHuman(room, index + 1, isHost: index == 0);
            }

            RoomFixture.SettleBots(room);
            Assert.Equal(4, room.PlayerCount);

            // 0번 슬롯의 방장이 나간다. 승계는 1번 슬롯의 사람에게 가고, 빈 0번 슬롯은
            // 다음 채우기가 봇으로 메운다.
            room.PostCommand(RoomCommand.Leave(1, 0));
            RoomFixture.SettleBots(room);

            Assert.Contains(Bots(room), bot => bot.PlayerId == 0);

            // 이제 방장(1번 슬롯)이 나간다. 가장 작은 슬롯은 봇의 0번이지만 승계는
            // 2번 슬롯의 사람에게 가야 한다.
            room.PostCommand(RoomCommand.Leave(2, 1));
            room.Advance();

            Assert.Equal(2, room.Summarize().HostPlayerId);
        }

        [Fact]
        public void 진행_중인_매치에는_봇이_합류하지_않는다()
        {
            // `/ws` 가 사람에게 막는 것과 같다 — 역할도 배치도 이미 정해져 있어
            // 비대칭 매치의 규칙이 성립하지 않는다.
            var room = RoomFixture.WithBots();

            RoomFixture.JoinHuman(room, 1, isHost: true);
            RoomFixture.SettleBots(room);

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            room.PostCommand(RoomCommand.AddBot());
            room.Advance();

            Assert.Equal(2, room.PlayerCount);
        }

        [Fact]
        public void 요약이_봇을_따로_센다()
        {
            // 로비의 온라인 인원 표시가 이 값을 쓴다. 인원에서 빼지 않는 이유는
            // 봇도 슬롯을 차지하므로 `PlayerCount` 가 정원 판정의 근거여야 하기 때문이다.
            var room = RoomFixture.WithBots();

            RoomFixture.JoinHuman(room, 1, isHost: true);
            RoomFixture.SettleBots(room);

            var summary = room.Summarize();

            Assert.Equal(2, summary.PlayerCount);
            Assert.Equal(1, summary.BotCount);
        }

        [Fact]
        public void 봇이_없으면_요약의_봇_수가_0이다()
        {
            var room = RoomFixture.Create(isStatic: true);

            RoomFixture.JoinHuman(room, 1, isHost: true);
            RoomFixture.SettleBots(room);

            Assert.Equal(0, room.Summarize().BotCount);
        }

        [Fact]
        public void 정원을_넘겨_채우지_않는다()
        {
            // 설정이 정원보다 큰 값을 요구해도 룸이 자른다.
            var room = RoomFixture.WithBots(fillTo: RealtimeConstants.Rooms.MaxPlayers + 4);

            RoomFixture.JoinHuman(room, 1, isHost: true);
            RoomFixture.SettleBots(room, ticks: 8);

            Assert.Equal(RealtimeConstants.Rooms.MaxPlayers, room.PlayerCount);
        }

        private static System.Collections.Generic.List<PlayerEntity> Bots(Room room)
        {
            var bots = new System.Collections.Generic.List<PlayerEntity>();

            foreach (var player in room.Players)
            {
                if (player.IsBot)
                {
                    bots.Add(player);
                }
            }

            return bots;
        }
    }
}
