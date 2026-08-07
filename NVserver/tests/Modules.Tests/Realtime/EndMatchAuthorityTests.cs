using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 매치를 끝낼 수 있는 사람은 누구인가.
    ///
    /// **`Control(EndMatch)` 는 클라이언트에 남은 마지막 권위 경로다.** 서버는 탈출·피격·열쇠를
    /// 다 세지만 결과 코드를 정하지 않는다(IG-007 미이관 — 막던 OQ-2·OQ-6 은 답이 나왔다).
    /// 그동안 방장이 판정해 보고하고
    /// 서버는 중계한다 — 즉 **이 경로의 인증이 곧 "클라이언트가 게임 결과를 결정하지 못한다"(§9)를
    /// 지키는 유일한 장치다.**
    ///
    /// 그런데 기존 테스트는 전부 세션 1(방장)에서만 보냈다. **거부되는 쪽이 검사되지 않았다** —
    /// 인증을 지우는 변경은 모든 테스트를 통과하고, 그러면 아무 클라이언트나 아무 결과 코드로
    /// 매치를 끝낼 수 있다.
    public class EndMatchAuthorityTests
    {
        /// **이것이 이 파일의 이유다.**
        [Fact]
        public void 방장이_아니면_매치를_끝낼_수_없다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);

            // 세션 2 는 플레이어 1 이고 방장이 아니다(픽스처가 세션 1 을 방장으로 넣는다).
            room.PostCommand(RoomCommand.EndMatch(2, 7));
            room.Advance();
            room.Broadcast(transport);

            Assert.Equal(RoomPhase.Playing, room.Phase);
            Assert.Equal(0, OutcomeOf(room, transport));
        }

        [Fact]
        public void 방장의_보고는_결과_코드를_그대로_중계한다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);

            room.PostCommand(RoomCommand.EndMatch(1, 7));
            room.Advance();
            room.Broadcast(transport);

            Assert.Equal(RoomPhase.Ended, room.Phase);
            Assert.Equal(7, OutcomeOf(room, transport));
        }

        /// 대기 단계에서는 끝낼 것이 없다. `EndMatch` 가 단계를 확인하지 않으면 시작하지 않은
        /// 룸이 `Ended` 로 가고, 그 룸은 시작도 로비 복귀도 어색한 상태가 된다.
        [Fact]
        public void 시작하지_않은_룸은_끝낼_수_없다()
        {
            var room = RoomFixture.Create();

            room.PostCommand(RoomCommand.Join(1, 0, string.Empty, true));
            room.PostCommand(RoomCommand.EndMatch(1, 3));
            room.Advance();

            Assert.Equal(RoomPhase.Waiting, room.Phase);
        }

        /// **종료 뒤에도 매치 전문이 최종 수치를 싣는다.** 결과 화면이 "열쇠 7/10, 탈출 1" 을
        /// 보여 줄 수 있는 이유이고, `Match.ForceEnd` 가 그 값들을 0 으로 만들지 **않는** 것이
        /// 의도라는 근거다(`Reset` 만 지운다).
        [Fact]
        public void 종료_뒤에도_최종_수치가_전문에_남는다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);

            room.Match.InsertKey();
            room.Match.InsertKey();
            room.Match.RegisterEscape();

            room.PostCommand(RoomCommand.EndMatch(1, 1));
            room.Advance();
            room.Broadcast(transport);

            // 전문은 Runner 사본으로 확인한다 — Seeker 사본에서는 열쇠 진행도가 0 이다.
            //
            // **누가 Runner 인지 물어봐야 한다.** `Room.PickSeeker` 는 `Random.Shared` 로
            // 고르므로 세션 2가 Seeker 인 경우가 절반이고, 그때 이 테스트는 열쇠 진행도가
            // 0 이라고 실패한다. 원인이 역할 배정에 있다는 신호는 어디에도 없어서 "종료 뒤
            // 수치가 사라진다" 로 읽힌다.
            Assert.True(transport.TryLastMatchState(RunnerSessionOf(transport), out var header, out _));

            Assert.Equal(MatchPhase.Ended, header.Phase);
            Assert.Equal(2, header.KeysInserted);
            Assert.Equal(1, header.Escapes);
        }

        /// 로비로 돌아가면 결과가 지워진다. 남으면 다음 매치가 시작 전부터 결과를 갖는다.
        [Fact]
        public void 로비로_돌아가면_결과가_지워진다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);

            room.PostCommand(RoomCommand.EndMatch(1, 7));
            room.Advance();

            room.PostCommand(RoomCommand.ReturnToLobby(1));
            room.Advance();
            room.Broadcast(transport);

            Assert.Equal(RoomPhase.Waiting, room.Phase);
            Assert.Equal(0, OutcomeOf(room, transport));
            Assert.Equal(0, room.Match.KeysInserted);
            Assert.Equal(0, room.Match.Escapes);
        }

        /// 비방장은 로비로도 돌릴 수 없다. 같은 `IsAuthorized` 를 쓰지만 별개의 명령이므로
        /// 한쪽만 검사하면 다른 쪽이 열린 채 남을 수 있다.
        [Fact]
        public void 방장이_아니면_로비로_돌릴_수_없다()
        {
            var room = RoomFixture.Create();

            RoomFixture.FillAndStart(room);

            room.PostCommand(RoomCommand.ReturnToLobby(2));
            room.Advance();

            Assert.Equal(RoomPhase.Playing, room.Phase);
        }

        /// **정적 룸은 일부러 다르다.** 설정으로 미리 열어 둔 개발용 룸에는 코드를 발급받는
        /// 경로가 없어 방장도 없다 — 그래서 아무나 시작하고 아무나 끝낸다. 시작 쪽은
        /// `RoomTests` 가 이미 고정하고, 종료 쪽도 같은지 여기서 확인한다.
        [Fact]
        public void 정적_룸은_아무나_끝낼_수_있다()
        {
            var room = RoomFixture.Create(isStatic: true);

            RoomFixture.FillAndStart(room);

            room.PostCommand(RoomCommand.EndMatch(2, 5));
            room.Advance();

            Assert.Equal(RoomPhase.Ended, room.Phase);
        }

        /// Runner 한 명의 세션 id. 술래가 무작위로 정해지므로 물어봐야 안다.
        ///
        /// `RoomFixture.FillAndStart` 는 세션 `n` 을 playerId `n-1` 로 넣으므로 둘의 변환은
        /// +1 이다. 룸 상태는 어느 사본이든 같은 `SeekerPlayerId` 를 싣는다(정적 룸의 host
        /// 바이트만 수신자별로 다르다).
        private static int RunnerSessionOf(RecordingTransport transport)
        {
            Assert.True(transport.TryLastRoomState(1, out var header, out var players));

            for (var index = 0; index < players.Length; index++)
            {
                if (players[index].PlayerId != header.SeekerPlayerId)
                {
                    return players[index].PlayerId + 1;
                }
            }

            Assert.Fail("명단에 Runner 가 없다.");
            return 0;
        }

        private static int OutcomeOf(Room room, RecordingTransport transport)
        {
            room.Broadcast(transport);

            Assert.True(transport.TryLastRoomState(1, out var header, out _));
            return header.Outcome;
        }
    }
}
