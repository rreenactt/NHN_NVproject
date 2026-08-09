using System.Numerics;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 시계로 끝난 매치도 승패를 갖는가.
    ///
    /// **여기 있던 구멍은 조용했다.** 서버의 시계가 매치를 끝낼 때 결과 코드를 0(미정)으로
    /// 두고 방장의 보고를 기다렸는데, 그 보고는 도착할 수 없다 — `EndMatch` 는 `Playing`
    /// 게이트를 지나야 하고 단계는 이미 `Ended` 다. 그래서 시간 초과로 끝난 모든 매치가
    /// 승패 없이 나갔고, 화면에는 이긴 쪽도 진 쪽도 아닌 "MATCH OVER" 만 떴다.
    public class TimeoutOutcomeTests
    {
        /// 클라이언트 `MatchOutcome` 과 같은 값.
        private const byte RunnersEscaped = 1;
        private const byte SeekerTimeout = 2;

        /// 시계가 0 이 되기까지의 틱. 한 틱 더 돌려 넘긴다.
        private static readonly int MatchTicks =
            (int)(MatchConstants.MatchDuration * SimConstants.TickRate) + 2;

        [Fact]
        public void 시계가_다_되면_술래_승리로_끝난다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room, 2, skipReveal: true);

            for (var tick = 0; tick < MatchTicks; tick++)
            {
                room.Advance();
            }

            Assert.Equal(RoomPhase.Ended, room.Phase);

            // 전문에서 읽는다 — 서버 내부 값이 아니라 클라이언트가 받는 byte 다.
            room.Broadcast(transport);
            Assert.True(transport.TryLastRoomState(1, out var header, out _));
            Assert.Equal(SeekerTimeout, header.Outcome);
        }

        /// 시계와 탈출이 겹쳤을 때 이미 나간 팀에게서 승리를 빼앗지 않는다.
        ///
        /// 방장의 보고가 늦어 시계가 먼저 닿는 경우가 이것이다. 시간 초과를 무조건 술래
        /// 승으로 적으면, 목표 인원이 이미 문을 통과한 매치가 술래 승으로 뒤집힌다.
        [Fact]
        public void 목표만큼_탈출했으면_시계가_다_되어도_Runner_승리다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room, 2, skipReveal: true);
            room.Broadcast(transport);

            // 2인 매치의 Runner 는 하나이므로 목표는 1 이다
            // (`MatchConstants.EscapesToWinWith`).
            Assert.Equal(1, MatchConstants.EscapesToWinWith(1));

            // Runner 를 열린 문 위에 세워 실제로 탈출시킨다. 문을 그 자리에 놓는 것이
            // 사람을 옮기는 것보다 짧고, 판정은 어차피 거리만 본다.
            Assert.True(transport.TryLastMatchState(1, out _, out var participants));

            byte runner = 0;
            foreach (var participant in participants)
            {
                if (participant.Role == MatchRole.Runner)
                {
                    runner = participant.PlayerId;
                }
            }

            room.Objectives.Reset();
            room.Objectives.SetDoor(RoomFixture.Map().SpawnPosition(runner), 0f);
            room.Objectives.MarkPlaced();

            for (var key = 0; key < MatchConstants.KeysRequired; key++)
            {
                room.Match.InsertKey();
            }

            Assert.True(room.Match.DoorOpen);

            for (var tick = 0; tick < MatchTicks; tick++)
            {
                room.Advance();
            }

            room.Broadcast(transport);
            Assert.True(transport.TryLastRoomState(1, out var header, out _));
            Assert.Equal(RunnersEscaped, header.Outcome);
        }
    }
}
