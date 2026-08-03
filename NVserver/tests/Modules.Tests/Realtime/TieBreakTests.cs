using System.Numerics;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 한 틱에 두 판정이 겹칠 때 무엇이 이기는가.
    ///
    /// **이 파일은 결정된 규칙을 검사하지 않는다. 결정되지 않은 것을 고정한다.**
    /// `Room.Advance` 의 판정 순서가 답을 정하고 있는데(`TickEscapes` 가 `StepProjectiles` 보다
    /// 앞이다) 그 순서는 기획서가 정한 것이 아니다 — 구현 순서가 규칙이 된 자리다.
    ///
    /// **왜 사소하지 않은가:** `EscapesToWin` 이 2 이므로 어느 Runner 가 나갔는지 죽었는지가
    /// 매치 결과를 바꾼다. 창은 한 틱(33ms)이지만 결과는 이진이다.
    ///
    /// **그리고 지금 순서는 문서화된 의도와 어긋난다.** `MatchConstants.EscapeHoldTime` 과
    /// `Room.TickEscapes` 는 유지 시간의 목적을 "목표의 마지막 한 걸음을 Seeker 가 끊을 수 있는
    /// 순간으로 만드는 것" 이라고 적는다. 그런데 **마지막 틱에 도착한 총알은 끊지 못한다.**
    ///
    /// 순서를 바꾸는 것은 규칙 변경이므로 §6.4 에 따라 추측하지 않고 **OQ-8** 로 올렸다.
    /// 이 테스트는 그 답이 오면 **무엇이 뒤집히는지를 정확히 보여 준다.**
    public class TieBreakTests
    {
        /// 현재 동작: 탈출이 이긴다.
        ///
        /// 답이 "피격이 이긴다" 로 정해지면 이 테스트가 실패하고, `Room.Advance` 에서
        /// `StepProjectiles`·`FireWeapons` 를 목표물 판정 앞으로 옮기는 것이 수정이다.
        [Fact]
        public void 같은_틱에_탈출과_피격이_겹치면_현재는_탈출이_이긴다()
        {
            // 유지를 두 틱 남긴다: 발사 틱에 한 틱, 총알이 도착하는 틱에 나머지 한 틱이 차서
            // **총알이 도착하는 틱과 탈출이 성립하는 틱이 같아진다.**
            var world = AtOpenDoorway(holdShortBy: 2);

            world.FireOnce();
            Assert.Equal(0, world.Room.Match.Escapes);

            world.Room.Advance();

            Assert.Equal(1, world.Room.Match.Escapes);
            Assert.False(HasFlag(world.Room, world.Transport, world.Seeker, world.Runner, EntityFlags.Downed));

            // 피격 수가 0 인 것이 "탈출이 이겼다" 의 정확한 표현이다 — 맞은 뒤 나간 것이 아니라
            // 아예 대상에서 빠졌다(`TryFindVictim` 이 `Escaped` 를 걸러낸다).
            Assert.Equal(0, HitsOf(world.Room, world.Transport, world.Seeker, world.Runner));
        }

        /// **대조군이고, 위 검사를 의미 있게 만드는 것이 이것이다.** 총알을 한 틱 앞세우면
        /// 피격이 성립하고 탈출은 일어나지 않는다 — 즉 위 검사에서 총알은 **빗나간 것이 아니라
        /// 대상에서 빠진 것**이고, 끊을 수 있는 창은 정확히 한 틱 차이로 닫힌다.
        ///
        /// 맞으면 순간이동이 Runner 를 문에서 떼어내므로 유지도 초기화된다.
        [Fact]
        public void 총알이_한_틱_앞서면_피격이_이기고_탈출이_막힌다()
        {
            var world = AtOpenDoorway(holdShortBy: 3);

            world.FireOnce();
            world.Room.Advance();

            Assert.Equal(0, world.Room.Match.Escapes);
            Assert.Equal(1, HitsOf(world.Room, world.Transport, world.Seeker, world.Runner));
        }

        /// 열린 문간에 선 Runner 와 그를 향해 쏠 수 있는 Seeker.
        ///
        /// Runner 를 사수 앞 2m 에 세우고 **그 자리에 문을 놓는다** — 움직이지 않으므로 문 반경
        /// 안에 계속 있고, 동시에 사수의 +Z 사격선 위에 있다. 이 두 조건이 겹쳐야 타이브레이크가
        /// 재현된다.
        ///
        /// `holdShortBy` 만큼 유지 시간을 남겨 두고 나머지를 미리 채운다.
        private static TieWorld AtOpenDoorway(int holdShortBy)
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);
            room.Broadcast(transport);

            Assert.True(transport.TryLastMatchState(1, out _, out var participants));

            byte seeker = 0;
            byte runner = 0;
            foreach (var participant in participants)
            {
                if (participant.Role == MatchRole.Seeker)
                {
                    seeker = participant.PlayerId;
                }
                else
                {
                    runner = participant.PlayerId;
                }
            }

            var doorway = FeetOf(room, seeker) + new Vector3(0f, 0f, 2f);

            Place(room, runner, doorway);

            room.Objectives.Reset();
            room.Objectives.SetDoor(doorway, 0f);
            room.Objectives.MarkPlaced();

            for (var key = 0; key < MatchConstants.KeysRequired; key++)
            {
                room.Match.InsertKey();
            }

            Assert.True(room.Match.DoorOpen);

            Advance(room, Match.EscapeHoldTicks - holdShortBy);
            Assert.Equal(0, room.Match.Escapes);

            return new TieWorld(room, transport, seeker, runner);
        }

        private sealed class TieWorld
        {
            public TieWorld(Room room, RecordingTransport transport, byte seeker, byte runner)
            {
                Room = room;
                Transport = transport;
                Seeker = seeker;
                Runner = runner;
            }

            public Room Room { get; }

            public RecordingTransport Transport { get; }

            public byte Seeker { get; }

            public byte Runner { get; }

            /// 한 발 쏘고 그 틱만 돌린다. 발사 틱에는 총알이 진행하지 않는다(IG-014a).
            public void FireOnce()
            {
                Room.PostInput(Seeker + 1, 1u, new InputFrame(ButtonFlags.Fire, 0, 0, 0, 0));
                Room.Advance();
            }
        }

        private static void Advance(Room room, int ticks)
        {
            for (var tick = 0; tick < ticks; tick++)
            {
                room.Advance();
            }
        }

        private static Vector3 FeetOf(Room room, byte playerId)
        {
            foreach (var player in room.Players)
            {
                if (player.PlayerId == playerId)
                {
                    return player.State.Position;
                }
            }

            Assert.Fail($"플레이어 {playerId} 가 룸에 없다.");
            return default;
        }

        private static void Place(Room room, byte playerId, Vector3 position)
        {
            foreach (var player in room.Players)
            {
                if (player.PlayerId != playerId)
                {
                    continue;
                }

                player.State.Position = position;
                player.State.Velocity = Vector3.Zero;
                return;
            }

            Assert.Fail($"플레이어 {playerId} 가 룸에 없다.");
        }

        private static bool HasFlag(
            Room room,
            RecordingTransport transport,
            byte viewer,
            byte target,
            EntityFlags flag)
        {
            room.Broadcast(transport);

            Assert.True(transport.TryLastSnapshot(viewer + 1, out _, out var entities));

            foreach (var entity in entities)
            {
                if (entity.Id == target)
                {
                    return (entity.Flags & flag) != 0;
                }
            }

            Assert.Fail($"플레이어 {target} 가 스냅샷에 없다.");
            return false;
        }

        private static int HitsOf(Room room, RecordingTransport transport, byte viewer, byte target)
        {
            room.Broadcast(transport);

            Assert.True(transport.TryLastMatchState(viewer + 1, out _, out var participants));

            foreach (var participant in participants)
            {
                if (participant.PlayerId == target)
                {
                    return participant.Hits;
                }
            }

            Assert.Fail($"플레이어 {target} 가 전문에 없다.");
            return 0;
        }
    }
}
