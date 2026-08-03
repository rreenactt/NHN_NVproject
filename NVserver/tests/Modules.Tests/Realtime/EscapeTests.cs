using System.Numerics;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 탈출이 서버에서 판정되는가(IG-012c).
    ///
    /// 문을 **열린 상태로** 만들어 두고 시작한다. 열쇠 10개를 실제로 넣는 경로는
    /// `KeyInsertTests` 가 덮으므로, 여기서 그것을 다시 하면 실패했을 때 "탈출이 안 된다" 와
    /// "문이 안 열렸다" 를 구별할 수 없다.
    public class EscapeTests
    {
        [Fact]
        public void 문간에_유지_시간만큼_서_있으면_탈출한다()
        {
            var world = AtOpenDoor();

            world.Advance(Match.EscapeHoldTicks);

            Assert.Equal(1, world.Room.Match.Escapes);
        }

        /// 유지 시간이 목표의 마지막 한 걸음을 Seeker 가 끊을 수 있는 순간으로 만든다.
        /// 즉시 탈출이면 문이 열리는 순간 매치가 끝난다.
        [Fact]
        public void 유지_시간_전에는_탈출하지_않는다()
        {
            var world = AtOpenDoor();

            world.Advance(Match.EscapeHoldTicks - 1);

            Assert.Equal(0, world.Room.Match.Escapes);
        }

        /// **연속이어야 한다.** 누적이면 문 앞을 여러 번 스쳐 지나가는 것으로도 탈출이 성립하고,
        /// 끊을 수 있는 순간이라는 설계가 사라진다.
        [Fact]
        public void 문에서_벗어나면_유지가_초기화된다()
        {
            var world = AtOpenDoor();

            world.Advance(Match.EscapeHoldTicks - 1);

            // 문을 멀리 옮겨 한 틱 — 사람이 걸어 나간 것과 같다.
            world.MoveDoor(new Vector3(50f, 0f, 50f));
            world.Advance(1);
            Assert.Equal(0, world.Room.Match.Escapes);

            // 돌아와도 처음부터 다시 세어야 한다. 스폰 자리로 돌려놓는다 — 원점으로 두면
            // 플레이어 1 의 스폰(-2,0,0)에서 2m 라 반경 안이고, 그 우연에 검사가 의존한다.
            world.MoveDoor(world.Spawn);
            world.Advance(Match.EscapeHoldTicks - 1);
            Assert.Equal(0, world.Room.Match.Escapes);

            world.Advance(1);
            Assert.Equal(1, world.Room.Match.Escapes);
        }

        /// 문이 닫혀 있으면 문간이 없다.
        [Fact]
        public void 닫힌_문에서는_탈출하지_않는다()
        {
            var world = AtOpenDoor(openDoor: false);

            world.Advance(Match.EscapeHoldTicks * 2);

            Assert.Equal(0, world.Room.Match.Escapes);
        }

        /// **탈출 수로 확인할 수 없다.** 픽스처의 두 스폰은 2m 간격이고 문 반경은 2.2m 이므로,
        /// Seeker 의 스폰에 문을 놓으면 자기 스폰에 서 있는 Runner 도 그 반경 안에 있다 —
        /// 실제로 그 Runner 가 나가면서 수가 1 이 된다(그것이 맞는 동작이다). 그래서 이
        /// 검사는 **그 Seeker 자신**이 나갔는지를 본다.
        [Fact]
        public void Seeker는_탈출하지_않는다()
        {
            var world = AtOpenDoor(asSeeker: true);

            world.Advance(Match.EscapeHoldTicks * 2);

            Assert.False(world.EscapedFlagOn(world.Actor));
        }

        [Fact]
        public void 문에서_수평으로_멀면_탈출하지_않는다()
        {
            var world = AtOpenDoor(doorOffset: new Vector3(MatchConstants.DoorUseRadius + 1f, 0f, 0f));

            world.Advance(Match.EscapeHoldTicks * 2);

            Assert.Equal(0, world.Room.Match.Escapes);
        }

        [Fact]
        public void 문에서_수직으로_멀면_탈출하지_않는다()
        {
            var world = AtOpenDoor(doorOffset: new Vector3(0f, MatchConstants.InteractHeight + 0.5f, 0f));

            world.Advance(Match.EscapeHoldTicks * 2);

            Assert.Equal(0, world.Room.Match.Escapes);
        }

        /// 한 번 나간 사람이 계속 세어지면 한 명이 매치를 끝낸다.
        [Fact]
        public void 같은_사람이_두_번_세어지지_않는다()
        {
            var world = AtOpenDoor();

            world.Advance(Match.EscapeHoldTicks * 4);

            Assert.Equal(1, world.Room.Match.Escapes);
        }

        /// 빠져나간 사람은 목표물 판정에서 빠진다. 남아 있으면 문간에 서서 계속 열쇠를 줍는다.
        [Fact]
        public void 탈출한_사람은_열쇠를_줍지_않는다()
        {
            var world = AtOpenDoor();

            world.Advance(Match.EscapeHoldTicks);
            Assert.Equal(1, world.Room.Match.Escapes);

            // 발밑에 열쇠를 놓는다. 판정에서 빠졌으면 그대로 남는다.
            world.Room.Objectives.AddKey(world.Spawn);
            world.Advance(1);

            Assert.Single(world.Room.Objectives.Keys);
        }

        /// 탈출 수는 **Seeker 도 받는다.** 자기가 막아야 하는 수다(기획서 §2.1 이 숨기는 것은
        /// 목표의 위치와 진행도이지 이것이 아니다).
        [Fact]
        public void 탈출_수는_양쪽_전문에_실린다()
        {
            var world = AtOpenDoor();

            world.Advance(Match.EscapeHoldTicks);
            world.Room.Broadcast(world.Transport);

            Assert.True(world.Transport.TryLastMatchState(world.Session, out var runnerView, out _));
            Assert.Equal(1, runnerView.Escapes);

            Assert.True(world.Transport.TryLastMatchState(world.SeekerSession, out var seekerView, out _));
            Assert.Equal(1, seekerView.Escapes);
        }

        /// 몸은 남고 플래그만 선다. 서버에서 빼면 전멸 판정이 탈출을 사망으로 셀 수 있다.
        [Fact]
        public void 탈출하면_스냅샷에_Escaped_플래그가_선다()
        {
            var world = AtOpenDoor();

            world.Advance(Match.EscapeHoldTicks);

            // **같은 틱의 스냅샷에 실려야 한다.** 와이어 상태를 이동 안에서 만들면 판정이
            // 세운 플래그가 다음 틱에나 나가고, 탈출이 33ms 늦게 보인다.
            Assert.True(world.EscapedFlagOn(world.Actor));
        }

        /// 다음 매치를 탈출한 상태로 시작하지 않는다.
        [Fact]
        public void 다음_매치는_탈출_상태를_물려받지_않는다()
        {
            var world = AtOpenDoor();

            world.Advance(Match.EscapeHoldTicks);
            Assert.Equal(1, world.Room.Match.Escapes);

            world.Room.PostCommand(RoomCommand.ReturnToLobby(1));
            world.Room.Advance();

            world.Room.PostCommand(RoomCommand.Start(1));
            world.Room.Advance();
            RoomFixture.SkipReveal(world.Room);

            Assert.Equal(0, world.Room.Match.Escapes);

            // 문을 다시 열어 두고 유지 시간을 채우면 또 한 번 나갈 수 있다 — 상태가 남아
            // 있으면 첫 틱에 세어지거나 영구히 세어지지 않는다.
            world.OpenDoorAt(world.Spawn);
            world.Advance(Match.EscapeHoldTicks);

            Assert.Equal(1, world.Room.Match.Escapes);
        }

        /// 열린 문 앞에 서 있는 Runner 를 만든다.
        ///
        /// 문을 여는 것은 열쇠를 넣지 않고 `Objectives` 와 `Match` 를 직접 세워서 한다 — 삽입
        /// 경로는 `KeyInsertTests` 의 몫이다.
        private static EscapeWorld AtOpenDoor(
            bool asSeeker = false,
            bool openDoor = true,
            Vector3 doorOffset = default)
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);
            room.Broadcast(transport);

            Assert.True(transport.TryLastMatchState(1, out _, out var participants));

            byte runner = 0;
            byte seeker = 0;
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

            var actor = asSeeker ? seeker : runner;
            var spawn = RoomFixture.Map().SpawnPosition(actor);
            var world = new EscapeWorld(room, transport, actor, seeker, spawn);

            world.PlaceDoor(spawn + doorOffset);

            if (openDoor)
            {
                world.FillDoor();
            }

            return world;
        }

        private sealed class EscapeWorld
        {
            public EscapeWorld(
                Room room,
                RecordingTransport transport,
                byte actor,
                byte seeker,
                Vector3 spawn)
            {
                Room = room;
                Transport = transport;
                Actor = actor;
                Seeker = seeker;
                Spawn = spawn;
            }

            public Room Room { get; }

            public RecordingTransport Transport { get; }

            public byte Actor { get; }

            public byte Seeker { get; }

            public Vector3 Spawn { get; }

            public int Session => Actor + 1;

            public int SeekerSession => Seeker + 1;

            /// 배치를 문 하나로 만든다.
            public void PlaceDoor(Vector3 position)
            {
                Room.Objectives.Reset();
                Room.Objectives.SetDoor(position, 0f);
                Room.Objectives.MarkPlaced();
            }

            public void MoveDoor(Vector3 position)
            {
                Room.Objectives.SetDoor(position, 0f);
            }

            /// 문을 여는 데 필요한 만큼 삽입 수를 채운다. 열쇠를 실제로 넣지 않는다.
            public void FillDoor()
            {
                for (var index = 0; index < MatchConstants.KeysRequired; index++)
                {
                    Room.Match.InsertKey();
                }

                Assert.True(Room.Match.DoorOpen);
            }

            public void OpenDoorAt(Vector3 position)
            {
                PlaceDoor(position);
                FillDoor();
            }

            public void Advance(int ticks)
            {
                for (var tick = 0; tick < ticks; tick++)
                {
                    Room.Advance();
                }
            }

            /// 마지막 스냅샷에서 이 플레이어의 `Escaped` 비트.
            public bool EscapedFlagOn(byte playerId)
            {
                Room.Broadcast(Transport);

                Assert.True(Transport.TryLastSnapshot(Session, out _, out var entities));

                foreach (var entity in entities)
                {
                    if (entity.Id == playerId)
                    {
                        return (entity.Flags & EntityFlags.Escaped) != 0;
                    }
                }

                Assert.Fail($"플레이어 {playerId} 가 스냅샷에 없다.");
                return false;
            }
        }
    }
}
