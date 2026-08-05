using System.Numerics;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 열쇠 습득이 서버에서 판정되는가(IG-012a).
    ///
    /// **열쇠를 손으로 놓는다.** 배치가 고른 자리까지 플레이어를 걸어가게 하면 테스트가
    /// 미로 모양과 이동 속도에 묶이고, 실패했을 때 "습득이 안 된다" 와 "거기까지 못 갔다"
    /// 를 구별할 수 없다. 여기서 검사하는 것은 판정이지 배치가 아니다.
    public class KeyPickupTests
    {
        [Fact]
        public void Runner가_열쇠_위에_서면_주워진다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            var runner = StartAndPlaceKeyAtRunner(room, transport, Vector3.Zero);

            room.Advance();

            Assert.Empty(room.Objectives.Keys);
            Assert.Equal(1, CarriedKeysOf(room, transport, runner));
        }

        /// 기획서 §3 — 열쇠는 Runner 의 목표다. Seeker 가 주워 없앨 수 있으면 그것이
        /// 가장 강한 전략이 되고 매치가 성립하지 않는다.
        [Fact]
        public void Seeker는_열쇠를_주울_수_없다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            var seeker = FindPlayer(room, transport, MatchRole.Seeker);
            PlaceSingleKey(room, SpawnOf(seeker));

            room.Advance();

            Assert.Single(room.Objectives.Keys);
        }

        /// 위층이 아래층 열쇠를 빨아들이지 않는다. 이 허용치가 층 간격보다 크면
        /// 계단 근처에서 남의 층 열쇠가 사라진다.
        [Fact]
        public void 수직으로_멀면_주워지지_않는다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            StartAndPlaceKeyAtRunner(
                room,
                transport,
                new Vector3(0f, MatchConstants.KeyPickupHeight + 0.5f, 0f));

            room.Advance();

            Assert.Single(room.Objectives.Keys);
        }

        [Fact]
        public void 수평으로_멀면_주워지지_않는다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            StartAndPlaceKeyAtRunner(
                room,
                transport,
                new Vector3(0f, 0f, MatchConstants.KeyPickupRadius + 0.5f));

            room.Advance();

            Assert.Single(room.Objectives.Keys);
        }

        /// 반경 안쪽이면 주워진다. 위의 두 검사만 두면 판정을 아예 끄는 변경이 통과한다.
        [Fact]
        public void 반경_안쪽이면_주워진다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            StartAndPlaceKeyAtRunner(
                room,
                transport,
                new Vector3(0f, 0f, MatchConstants.KeyPickupRadius - 0.2f));

            room.Advance();

            Assert.Empty(room.Objectives.Keys);
        }

        /// 역할 공개 중에는 이동이 잠긴다. 그 구간에 스폰 자리의 열쇠가 주워지면
        /// 아무도 움직이지 않은 매치에서 열쇠가 사라진다.
        [Fact]
        public void 역할_공개_중에는_주워지지_않는다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room, skipReveal: false);
            room.Broadcast(transport);

            var runner = FindPlayer(room, transport, MatchRole.Runner);
            PlaceSingleKey(room, SpawnOf(runner));

            Assert.Equal(MatchPhase.RoleReveal, room.MatchPhase);

            room.Advance();

            Assert.Single(room.Objectives.Keys);
        }

        /// 주워진 열쇠는 다음 전문에서 사라진다. 5초 주기만으로 보내면 주운 열쇠가
        /// 그동안 화면에 남아 있고, 그것을 다시 주우러 가는 사람이 생긴다.
        [Fact]
        public void 주워지면_목표물_전문이_즉시_갱신된다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            var runner = StartAndPlaceKeyAtRunner(room, transport, Vector3.Zero);
            var before = transport.CountOfEvent(SessionOf(runner), EventKind.ObjectiveState);

            room.Advance();
            room.Broadcast(transport);

            // 주기(5초 = 150틱)가 아직 오지 않았는데도 나갔다는 것이 "즉시" 다.
            Assert.Equal(before + 1, transport.CountOfEvent(SessionOf(runner), EventKind.ObjectiveState));

            Assert.True(transport.TryLastObjectiveState(SessionOf(runner), out var header, out _, out var keys));
            Assert.Equal(0, header.KeyCount);
            Assert.Empty(keys);
        }

        [Fact]
        public void 소지_수가_매치_전문에_실린다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            var runner = StartAndPlaceKeyAtRunner(room, transport, Vector3.Zero);

            room.Advance();
            room.Broadcast(transport);

            Assert.True(transport.TryLastMatchState(SessionOf(runner), out _, out var participants));
            Assert.Equal(1, FindParticipant(participants, runner).CarriedKeys);
        }

        /// 매치를 다시 시작하면 지난 매치의 소지 수가 남아 있지 않다. 남으면 두 번째
        /// 매치에서 아무것도 하지 않은 Runner 가 문을 열 수 있다.
        [Fact]
        public void 다음_매치는_소지_수를_물려받지_않는다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            var runner = StartAndPlaceKeyAtRunner(room, transport, Vector3.Zero);
            room.Advance();
            Assert.Equal(1, CarriedKeysOf(room, transport, runner));

            room.PostCommand(RoomCommand.ReturnToLobby(1));
            room.Advance();

            // 로비로 돌아오면 준비가 전원 내려간다. 다시 누르지 않으면 시작되지 않는다.
            RoomFixture.Ready(room, 2);

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();
            RoomFixture.SkipReveal(room);

            Assert.Equal(0, CarriedKeysOf(room, transport, runner));
        }

        /// 매치를 시작하고, Runner 를 찾아 그 스폰 자리를 기준으로 열쇠 하나만 놓는다.
        /// 돌려주는 것은 그 Runner 의 playerId 다.
        private static byte StartAndPlaceKeyAtRunner(
            Room room,
            RecordingTransport transport,
            Vector3 offsetFromSpawn)
        {
            var runner = FindPlayer(room, transport, MatchRole.Runner);
            PlaceSingleKey(room, SpawnOf(runner) + offsetFromSpawn);
            return runner;
        }

        /// 배치를 비우고 열쇠 하나만 남긴다. 제단과 문은 이 검사에 관여하지 않는다.
        private static void PlaceSingleKey(Room room, Vector3 position)
        {
            room.Objectives.Reset();
            room.Objectives.AddKey(position);
            room.Objectives.MarkPlaced();
        }

        /// 역할은 서버가 정한다. 그 배정을 테스트가 다시 계산하지 않고 전문에서 읽는다 —
        /// 계산하면 `PickSeeker` 를 바꿀 때 테스트가 조용히 반대편을 검사한다.
        private static byte FindPlayer(Room room, RecordingTransport transport, MatchRole role)
        {
            if (room.MatchPhase == MatchPhase.Lobby)
            {
                RoomFixture.FillAndStart(room);
                room.Broadcast(transport);
            }

            Assert.True(transport.TryLastMatchState(1, out _, out var participants));

            foreach (var participant in participants)
            {
                if (participant.Role == role)
                {
                    return participant.PlayerId;
                }
            }

            Assert.Fail($"{role} 가 배정되지 않았다.");
            return 0;
        }

        private static MatchParticipant FindParticipant(MatchParticipant[] participants, byte playerId)
        {
            foreach (var participant in participants)
            {
                if (participant.PlayerId == playerId)
                {
                    return participant;
                }
            }

            Assert.Fail($"플레이어 {playerId} 가 전문에 없다.");
            return default;
        }

        /// 전문에서 소지 수를 읽는다. **그 사람 자신의 사본에서 읽는다** — Seeker 사본은
        /// 전원 0 이므로 남의 사본으로 확인하면 항상 0 이 나온다.
        private static int CarriedKeysOf(Room room, RecordingTransport transport, byte playerId)
        {
            room.Broadcast(transport);

            Assert.True(transport.TryLastMatchState(SessionOf(playerId), out _, out var participants));
            return FindParticipant(participants, playerId).CarriedKeys;
        }

        /// 픽스처는 세션 1..N 을 playerId 0..N-1 로 넣는다.
        private static int SessionOf(byte playerId) => playerId + 1;

        private static Vector3 SpawnOf(byte playerId) => RoomFixture.Map().SpawnPosition(playerId);
    }
}
