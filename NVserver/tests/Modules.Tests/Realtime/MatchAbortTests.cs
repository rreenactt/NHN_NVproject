using NV.Realtime;
using NV.Realtime.Contracts;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 진행 불가 매치의 서버 중단 (`docs/match-abort-plan.md`).
    ///
    /// **역할은 무작위이므로 가정하지 않는다** — 전문에서 술래를 물어 그 세션을
    /// 내보낸다(`SeekerSpawnTests` 와 같은 규칙). 유예 값도 박지 않고 상수에서
    /// 계산한다 — 체인 테스트가 시간 창을 다루는 방식과 같다.
    public class MatchAbortTests
    {
        /// 클라이언트 `MatchOutcome.Aborted` 와 같은 값. 서버의 `Room.AbortedOutcome`
        /// 은 private 이므로 여기 다시 적고, 어긋나면 이 테스트가 잡는다.
        private const byte Aborted = 4;

        private static readonly int Grace = (int)RealtimeConstants.Match.AbortGraceTicks;

        [Fact]
        public void 술래가_나가면_유예_뒤_중단된다()
        {
            var (room, transport, seekerId) = StartedRoom();

            room.PostCommand(RoomCommand.Leave(seekerId + 1, seekerId));
            room.Advance();

            // 유예 동안은 계속 진행된다 — 남은 사람이 상황을 볼 시간이다.
            Advance(room, Grace - 2);
            Assert.Equal(RoomPhase.Playing, room.Phase);

            Advance(room, 3);
            Assert.Equal(RoomPhase.Ended, room.Phase);

            // 결과는 승패가 아니라 중단(4)이고, 전문으로 나간다 — 클라이언트가 읽는
            // 것은 이 byte 다.
            var runnerSession = seekerId == 0 ? 2 : 1;
            room.Broadcast(transport);
            Assert.True(transport.TryLastRoomState(runnerSession, out var header, out _));
            Assert.Equal(Aborted, header.Outcome);
        }

        [Fact]
        public void Runner가_하나_나가도_매치는_계속된다()
        {
            var (room, _, seekerId) = StartedRoom(count: 3);

            // 술래가 아닌 사람 하나를 내보낸다. 남는 명단은 술래 1 + Runner 1 이다.
            var leavingRunner = FirstRunnerId(seekerId, playerCount: 3);
            room.PostCommand(RoomCommand.Leave(leavingRunner + 1, leavingRunner));
            room.Advance();

            Advance(room, Grace + 10);
            Assert.Equal(RoomPhase.Playing, room.Phase);
        }

        [Fact]
        public void Runner가_모두_나가면_유예_뒤_중단된다()
        {
            var (room, _, seekerId) = StartedRoom();

            var runnerId = FirstRunnerId(seekerId, playerCount: 2);
            room.PostCommand(RoomCommand.Leave(runnerId + 1, runnerId));
            room.Advance();

            Advance(room, Grace - 2);
            Assert.Equal(RoomPhase.Playing, room.Phase);

            Advance(room, 3);
            Assert.Equal(RoomPhase.Ended, room.Phase);
        }

        /// 전원 퇴장은 기존 경로가 우선이다 — 결과 화면 없이 대기로 되돌아간다.
        /// 볼 사람이 없는 결과 화면은 만들지 않는다.
        [Fact]
        public void 전원이_나가면_중단이_아니라_대기로_돌아간다()
        {
            var (room, _, _) = StartedRoom();

            room.PostCommand(RoomCommand.Leave(1, 0));
            room.PostCommand(RoomCommand.Leave(2, 1));
            room.Advance();

            Assert.Equal(RoomPhase.Waiting, room.Phase);

            // 유예가 지나도 `Ended` 로 넘어가지 않는다.
            Advance(room, Grace + 10);
            Assert.Equal(RoomPhase.Waiting, room.Phase);
        }

        /// 강제 퇴장도 퇴장이다 — 경로가 달라도 판정은 명단에서 하므로 같이 걸린다.
        [Fact]
        public void 술래를_강제_퇴장시켜도_유예_뒤_중단된다()
        {
            var (room, _, seekerId) = StartedRoom();

            // 방장은 자기를 내보낼 수 없다(`Room.Kick`). 술래가 방장(세션 1)으로 뽑힌
            // 절반의 경우를 그대로 두면 이 테스트는 동전 던지기가 되므로, 방장을 먼저
            // Runner 에게 넘겨 어느 추첨에서도 "방장이 술래를 내보내는" 모양을 만든다.
            var runnerId = FirstRunnerId(seekerId, playerCount: 2);
            room.PostCommand(RoomCommand.TransferHost(1, runnerId));
            room.PostCommand(RoomCommand.Kick(runnerId + 1, seekerId));
            room.Advance();

            Advance(room, Grace + 2);
            Assert.Equal(RoomPhase.Ended, room.Phase);
        }

        /// 재매치가 지난 매치의 중단 예약을 물려받지 않는다 — 체인 틱 필드를 지우는
        /// 것과 같은 이유다.
        [Fact]
        public void 재매치는_지난_중단_예약을_물려받지_않는다()
        {
            var (room, _, seekerId) = StartedRoom();

            // 술래가 나가 예약이 걸리고, 유예가 끝나기 전에 나머지도 나가 방이 빈다.
            room.PostCommand(RoomCommand.Leave(seekerId + 1, seekerId));
            room.Advance();
            Advance(room, 10);

            var runnerId = FirstRunnerId(seekerId, playerCount: 2);
            room.PostCommand(RoomCommand.Leave(runnerId + 1, runnerId));
            room.Advance();
            Assert.Equal(RoomPhase.Waiting, room.Phase);

            // 다시 채워 새 매치를 시작한다. 지난 예약이 남아 있으면 몇 틱 만에 끝난다.
            RoomFixture.FillAndStart(room);
            Advance(room, Grace + 10);
            Assert.Equal(RoomPhase.Playing, room.Phase);
        }

        private static void Advance(Room room, int ticks)
        {
            for (var tick = 0; tick < ticks; tick++)
            {
                room.Advance();
            }
        }

        /// 세션 1..count 가 playerId 0..count-1 인 것은 `FillAndStart` 의 규칙이다.
        private static byte FirstRunnerId(byte seekerId, int playerCount)
        {
            for (byte id = 0; id < playerCount; id++)
            {
                if (id != seekerId)
                {
                    return id;
                }
            }

            Assert.Fail("Runner 가 없다.");
            return 0;
        }

        private static (Room room, RecordingTransport transport, byte seekerId) StartedRoom(int count = 2)
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room, count);
            room.Broadcast(transport);

            Assert.True(transport.TryLastMatchState(1, out _, out var participants), "매치 전문이 나가지 않았다.");

            byte seekerId = 0;
            var found = false;
            foreach (var participant in participants)
            {
                if (participant.Role == MatchRole.Seeker)
                {
                    seekerId = participant.PlayerId;
                    found = true;
                }
            }

            Assert.True(found, "Seeker 가 배정되지 않았다.");
            return (room, transport, seekerId);
        }
    }
}
