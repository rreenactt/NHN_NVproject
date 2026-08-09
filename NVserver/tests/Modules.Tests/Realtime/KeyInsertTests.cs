using System.Numerics;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 열쇠 삽입과 문 개방이 서버에서 판정되는가(IG-012b2).
    ///
    /// 문과 열쇠를 손으로 놓는다 — 이유는 `KeyPickupTests` 와 같다. 여기서 검사하는 것은
    /// 판정이지 배치가 아니다.
    public class KeyInsertTests
    {
        /// 픽스처는 2인 매치이고, 2인의 열쇠 요구량은 3 이다
        /// (`MatchConstants.KeysRequiredWith`). **상수 `KeysRequired`(10)가 아니다** —
        /// 그것은 어떤 매치도 넘지 않는 상한이고, 실제 요구량은 인원이 정한다.
        private static readonly int Needed = MatchConstants.KeysRequiredWith(2);

        [Fact]
        public void 문_앞의_Runner가_열쇠를_넣는다()
        {
            var world = Insertable(keys: 1);

            world.Interact();

            Assert.Equal(1, world.KeysInserted());
            Assert.Equal(0, world.CarriedKeys());
        }

        [Fact]
        public void 소지한_열쇠가_없으면_넣지_못한다()
        {
            var world = Insertable(keys: 0);

            world.Interact();

            Assert.Equal(0, world.KeysInserted());
        }

        /// 기획서 §3 — 문은 Runner 의 목표다. Seeker 가 열 수 있으면 목표가 뒤집힌다.
        /// (Seeker 는 소지 열쇠도 가질 수 없으므로 이중으로 막히지만, 역할 검사가 사라지는
        /// 변경을 잡으려면 별도로 확인해야 한다.)
        [Fact]
        public void Seeker는_넣지_못한다()
        {
            var world = Insertable(keys: 1, asSeeker: true);

            world.Interact();

            Assert.Equal(0, world.KeysInserted());
        }

        [Fact]
        public void 문에서_멀면_넣지_못한다()
        {
            var world = Insertable(keys: 1, doorOffset: new Vector3(MatchConstants.DoorUseRadius + 1f, 0f, 0f));

            world.Interact();

            Assert.Equal(0, world.KeysInserted());
        }

        /// 위층에서 아래층 문에 넣을 수 없다. 층 간격(3.2m)이 `InteractHeight` 보다 크므로
        /// 이 검사가 곧 층 분리다.
        [Fact]
        public void 수직으로_멀면_넣지_못한다()
        {
            var world = Insertable(keys: 1, doorOffset: new Vector3(0f, MatchConstants.InteractHeight + 0.5f, 0f));

            world.Interact();

            Assert.Equal(0, world.KeysInserted());
        }

        /// 간격이 없으면 한 번의 입력으로 열쇠 10개가 다 들어간다.
        [Fact]
        public void 간격_안에는_두_번_넣지_못한다()
        {
            var world = Insertable(keys: 2);

            world.Interact();
            world.Interact();

            Assert.Equal(1, world.KeysInserted());
        }

        [Fact]
        public void 간격이_지나면_다시_넣는다()
        {
            var world = Insertable(keys: 2);

            world.Interact();
            world.Advance(Match.InsertIntervalTicks);
            world.Interact();

            Assert.Equal(2, world.KeysInserted());
        }

        /// 입력이 끊긴 뒤에도 삽입이 반복되지 않는다.
        ///
        /// 새 입력이 없으면 서버는 마지막 입력을 최대 `MaxInputRepeatTicks` 만큼 반복하므로
        /// (`Room.StepPlayer`) 상호작용이 그 반복에 실릴 수 있는 구조다. 실리지 않게 하는 것은
        /// **요청을 새 입력 갈래에서만 세우는 것**이고, 이 검사가 그 설계를 못질한다.
        ///
        /// `InputValidator.WithoutEdgeButtons` 는 같은 것을 이중으로 막는 방어다. 지금은
        /// 그것을 빼도 이 검사가 통과한다 — 확인했다. 그래서 이 검사가 지키는 것은 스트립이
        /// 아니라 "반복 갈래는 버튼을 읽지 않는다" 는 쪽이다.
        [Fact]
        public void 입력이_끊겨도_삽입은_반복되지_않는다()
        {
            var world = Insertable(keys: 5);

            world.Interact();

            // 새 입력을 주지 않고 간격의 세 배를 돌린다. 반복이 상호작용을 실어 나르면
            // 여기서 삽입 수가 늘어난다.
            world.Advance(Match.InsertIntervalTicks * 3);

            Assert.Equal(1, world.KeysInserted());
        }

        [Fact]
        public void 열쇠_전부가_들어가면_문이_열린다()
        {
            var world = Insertable(keys: Needed);

            world.InsertAll();

            Assert.Equal(Needed, world.KeysInserted());
            Assert.True(world.Room.Match.DoorOpen);
        }

        /// 문턱을 넘은 뒤에는 세지 않는다. 더 세면 HUD 가 "13/10" 을 그린다.
        ///
        /// **소지 수는 삽입이 끝난 직후에 본다.** 이 플레이어는 열린 문간에 서 있으므로
        /// `EscapeHoldTicks`(24틱) 를 채우면 탈출하고 그때 소지 열쇠가 0 이 된다(IG-012c) —
        /// 뒤에서 확인하면 "넣지 못했다" 와 "들고 나갔다" 를 구별할 수 없다.
        [Fact]
        public void 열린_문에는_더_넣지_않는다()
        {
            var world = Insertable(keys: Needed + 3);

            world.InsertAll();

            Assert.Equal(Needed, world.KeysInserted());
            Assert.Equal(3, world.CarriedKeys());

            // 열린 문에 한 번 더 요청한다. 삽입 수가 늘지 않아야 한다.
            world.Advance(Match.InsertIntervalTicks);
            world.Interact();

            Assert.Equal(Needed, world.KeysInserted());
        }

        /// 문이 열린 틱에 목표물 전문이 나가야 한다. 5초 주기만 기다리면 그동안 Runner 가
        /// 열린 문을 잠긴 것으로 본다.
        [Fact]
        public void 문이_열리면_목표물_전문이_즉시_갱신된다()
        {
            var world = Insertable(keys: Needed);

            world.InsertAll();
            world.Room.Broadcast(world.Transport);

            Assert.True(world.Transport.TryLastObjectiveState(
                world.Session, out var header, out _, out _));

            Assert.True(header.HasDoor);
            Assert.True(world.LastDoorOpen());
        }

        [Fact]
        public void 삽입_수가_매치_전문에_실린다()
        {
            var world = Insertable(keys: 2);

            world.Interact();
            world.Room.Broadcast(world.Transport);

            Assert.True(world.Transport.TryLastMatchState(world.Session, out var header, out _));
            Assert.Equal(1, header.KeysInserted);
        }

        /// 기획서 §2.1 — Seeker 는 목표 진행도를 알 수 없다. 코덱이 거르지만, 룸이 실제 값을
        /// 채우기 시작한 지금 그 필터가 살아 있는지 확인해야 한다.
        [Fact]
        public void Seeker_사본의_삽입_수는_0이다()
        {
            var world = Insertable(keys: 2);

            world.Interact();
            world.Room.Broadcast(world.Transport);

            Assert.True(world.Transport.TryLastMatchState(world.SeekerSession, out var header, out _));
            Assert.Equal(0, header.KeysInserted);
        }

        /// 문 앞에 열쇠를 든 Runner 를 세운 상태를 만든다.
        ///
        /// 열쇠는 스폰 자리에 겹쳐 놓고 한 틱 돌려 습득 판정으로 들린다(IG-012a) — 소지 수를
        /// 직접 써 넣으면 습득과 삽입 사이가 끊어져, 습득이 깨져도 이 테스트가 통과한다.
        private static InsertWorld Insertable(
            int keys,
            bool asSeeker = false,
            Vector3 doorOffset = default)
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);
            room.Broadcast(transport);

            Assert.True(transport.TryLastMatchState(1, out _, out var participants));

            byte actor = 0;
            byte seeker = 0;
            foreach (var participant in participants)
            {
                if (participant.Role == MatchRole.Seeker)
                {
                    seeker = participant.PlayerId;
                }
                else
                {
                    actor = participant.PlayerId;
                }
            }

            if (asSeeker)
            {
                actor = seeker;
            }

            var spawn = RoomFixture.Map().SpawnPosition(actor);

            room.Objectives.Reset();
            room.Objectives.SetDoor(spawn + doorOffset, 0f);

            for (var index = 0; index < keys; index++)
            {
                room.Objectives.AddKey(spawn);
            }

            room.Objectives.MarkPlaced();

            // 습득 판정이 한 틱에 겹친 열쇠를 전부 집는다.
            room.Advance();

            return new InsertWorld(room, transport, actor, seeker);
        }

        /// 테스트용 조립. 입력 틱을 이어서 붙이는 일이 여러 검사에 걸쳐 반복되므로 모아 둔다.
        private sealed class InsertWorld
        {
            private uint _inputTick;

            public InsertWorld(Room room, RecordingTransport transport, byte actor, byte seeker)
            {
                Room = room;
                Transport = transport;
                Actor = actor;
                Seeker = seeker;
            }

            public Room Room { get; }

            public RecordingTransport Transport { get; }

            public byte Actor { get; }

            public byte Seeker { get; }

            public int Session => Actor + 1;

            public int SeekerSession => Seeker + 1;

            /// E 를 한 번 누르고 한 틱 돌린다.
            public void Interact()
            {
                _inputTick++;
                Room.PostInput(Session, _inputTick, new InputFrame(ButtonFlags.Interact, 0, 0, 0, 0));
                Room.Advance();
            }

            public void Advance(int ticks)
            {
                for (var tick = 0; tick < ticks; tick++)
                {
                    Room.Advance();
                }
            }

            /// 간격을 지켜 필요한 수만큼 넣는다.
            public void InsertAll()
            {
                for (var index = 0; index < Needed; index++)
                {
                    Interact();
                    Advance(Match.InsertIntervalTicks);
                }
            }

            public int KeysInserted() => Room.Match.KeysInserted;

            public int CarriedKeys()
            {
                Room.Broadcast(Transport);

                Assert.True(Transport.TryLastMatchState(Session, out _, out var participants));

                foreach (var participant in participants)
                {
                    if (participant.PlayerId == Actor)
                    {
                        return participant.CarriedKeys;
                    }
                }

                Assert.Fail($"플레이어 {Actor} 가 전문에 없다.");
                return 0;
            }

            /// 마지막 목표물 전문의 문 개방 비트.
            public bool LastDoorOpen()
            {
                Assert.True(Transport.TryLastEvent(Session, EventKind.ObjectiveState, out var payload));

                var keyBuffer = new ObjectivePoint[64];
                var deviceBuffer = new ObjectiveDevice[16];

                MessageCodec.ReadObjectiveState(
                    payload,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out var doorOpen,
                    keyBuffer,
                    deviceBuffer);

                return doorOpen;
            }
        }
    }
}
