using System;
using System.Numerics;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 장치 사용이 서버에서 판정되는가(IG-013, 기획서 §5).
    ///
    /// **이것이 없던 동안 장치는 클라이언트가 혼자 판정했다.** 순수 연출인 둘(전체 지도·술래
    /// 시점)은 그래도 됐지만, 서버가 소유한 상태를 건드리는 나머지는 로컬로 바뀌었다가 다음
    /// 전문이 그대로 되돌렸다 — 포탈은 제자리로 튕기고, 지혈은 한 프레임 뒤 다시 피가 흐르고,
    /// 시간 추가는 시계가 원래 값으로 돌아갔다. 소진·쿨다운도 각자 세고 있었으므로 1회용
    /// 장치를 **인원수만큼** 쓸 수 있었다.
    ///
    /// 그래서 여기서 검사하는 것은 효과 하나하나가 아니라 **판정이 한 곳에 있는가**다.
    public class DeviceUseTests
    {
        /// 사수와 장치의 거리(m). `HitTests.OffLatticeRange` 와 같은 이유로 격자를 피한다.
        private const float ShootRange = 2.5f;

        [Fact]
        public void 포탈은_몸을_옮긴다()
        {
            var world = Fitted(MatchDeviceType.Teleport);
            var before = world.ActorPosition();

            world.Interact();

            Assert.True(
                Vector3.Distance(world.ActorPosition(), before) > 1f,
                $"포탈을 썼는데 {before} 에서 움직이지 않았다.");
        }

        /// **몸을 옮기는 것이 서버여야 하는 이유.** 클라이언트가 옮기면 다음 스냅샷이 되돌린다.
        /// 그 스냅샷을 만드는 것이 이 값이므로, 여기에 실려야 되돌려지지 않는다.
        [Fact]
        public void 포탈로_옮긴_자리가_스냅샷에_실린다()
        {
            var world = Fitted(MatchDeviceType.Teleport);

            world.Interact();
            world.Room.Broadcast(world.Transport);

            Assert.True(world.Transport.TryLastSnapshot(world.Session, out _, out var entities));

            foreach (var entity in entities)
            {
                if (entity.Id != world.Actor)
                {
                    continue;
                }

                var wire = new Vector3(
                    Quantization.ToMeters(entity.X),
                    Quantization.ToMeters(entity.Y),
                    Quantization.ToMeters(entity.Z));

                Assert.True(Vector3.Distance(wire, world.ActorPosition()) < 0.1f);
                return;
            }

            Assert.Fail("스냅샷에 그 몸이 없다.");
        }

        [Fact]
        public void 지혈은_출혈을_멈춘다()
        {
            var world = Fitted(MatchDeviceType.StopBleeding);
            world.WoundActor();

            Assert.True(world.ActorBleeding(), "피격했는데 출혈 상태가 아니다.");

            world.Interact();

            Assert.False(world.ActorBleeding(), "지혈 장치를 썼는데 계속 출혈 중이다.");
        }

        /// **지혈은 부활이 아니다.** 피격 수를 0 으로 되돌리는 방식으로 고치면 두 번째 탄을
        /// 맞아도 죽지 않게 되어, 장치 하나가 목숨 하나가 된다.
        [Fact]
        public void 지혈해도_피격_수는_남는다()
        {
            var world = Fitted(MatchDeviceType.StopBleeding);
            world.WoundActor();

            world.Interact();

            Assert.Equal(1, world.ActorHits());
        }

        /// 멀쩡한 몸으로 쓰면 1회용이 그냥 사라진다. 클라이언트가 거절하는 것과 같은 이유로
        /// 서버도 거절해야 한다 — 한쪽만 거절하면 화면에는 남아 있는 장치가 서버에는 없다.
        [Fact]
        public void 출혈_중이_아니면_지혈이_소진되지_않는다()
        {
            var world = Fitted(MatchDeviceType.StopBleeding);

            world.Interact();

            Assert.False(world.DeviceHas(0, MatchDeviceState.Spent), "안 쓴 장치가 소진됐다.");
        }

        [Fact]
        public void 시간_추가는_시계를_늘린다()
        {
            var world = Fitted(MatchDeviceType.AddTime);
            var before = world.Room.Match.MatchTicksRemaining;

            world.Interact();

            // 한 틱 지나므로 보너스에서 1을 뺀 만큼은 늘어야 한다.
            var bonus = (int)MathF.Round(MatchConstants.DeviceTimeBonus * SimConstants.TickRate);
            Assert.True(
                world.Room.Match.MatchTicksRemaining >= before + bonus - 2,
                $"시계가 {before} → {world.Room.Match.MatchTicksRemaining} 밖에 늘지 않았다.");
        }

        /// 1회용은 한 번이다. **한 사람당 한 번이 아니다** — 그것이 클라이언트가 각자 세던
        /// 시절의 동작이었고, 방에 넷이 있으면 시계가 네 번 늘어났다.
        [Fact]
        public void 한_번_쓴_1회용은_다시_쓸_수_없다()
        {
            var world = Fitted(MatchDeviceType.AddTime);

            world.Interact();
            var after = world.Room.Match.MatchTicksRemaining;

            world.Advance(5);
            world.Interact();

            Assert.True(
                world.Room.Match.MatchTicksRemaining < after,
                "1회용 장치가 두 번 발동했다(시계가 다시 늘었다).");

            Assert.True(world.DeviceHas(0, MatchDeviceState.Spent));
        }

        /// 기획서 §5.2 — 순간이동은 **한 대를 쓰면 전부** 잠긴다.
        [Fact]
        public void 포탈은_전역_쿨다운을_공유한다()
        {
            var world = Fitted(MatchDeviceType.Teleport, MatchDeviceType.Teleport);

            world.Interact();
            world.Room.Broadcast(world.Transport);

            Assert.True(world.DeviceHas(1, MatchDeviceState.Cooling), "다른 포탈이 잠기지 않았다.");
        }

        [Fact]
        public void 전체_정지는_전원을_얼린다()
        {
            var world = Fitted(MatchDeviceType.FreezeAndXray);

            world.Interact();

            Assert.True(world.EveryoneFrozen(), "정지 장치를 썼는데 얼지 않은 몸이 있다.");
        }

        /// 클라이언트가 "왜 못 움직이는가" 를 가르는 근거다. 스냅샷의 `Frozen` 은 체인 견인과
        /// 한 비트를 나눠 쓰므로, 이 플래그가 없으면 정지 중인 Seeker 에게 사슬이 그려진다.
        [Fact]
        public void 전체_정지가_전문에_실린다()
        {
            var world = Fitted(MatchDeviceType.FreezeAndXray);

            world.Interact();
            world.Room.Broadcast(world.Transport);

            Assert.True(world.DeviceHas(0, MatchDeviceState.Active), "정지가 도는데 Active 가 없다.");
        }

        [Fact]
        public void 전체_정지는_끝나고_풀린다()
        {
            var world = Fitted(MatchDeviceType.FreezeAndXray);

            world.Interact();
            world.Advance((int)MathF.Ceiling(MatchConstants.FreezeDuration * SimConstants.TickRate) + 2);
            world.Room.Broadcast(world.Transport);

            Assert.False(world.EveryoneFrozen(), "정지가 끝났는데 아직 얼어 있다.");
            Assert.False(world.DeviceHas(0, MatchDeviceState.Active), "정지가 끝났는데 Active 가 남았다.");
        }

        [Fact]
        public void 멀면_쓰지_못한다()
        {
            var world = Fitted(MatchDeviceType.AddTime, offset: new Vector3(MatchConstants.DeviceUseRadius + 2f, 0f, 0f));
            var before = world.Room.Match.MatchTicksRemaining;

            world.Interact();

            Assert.True(world.Room.Match.MatchTicksRemaining < before, "사거리 밖의 장치가 발동했다.");
        }

        /// 문에 넣지 못한 E 가 장치로 흘러야 한다. 한 번의 입력이 문 조건에 걸려 사라지면
        /// 증상은 "가끔 장치가 안 눌린다" 가 된다.
        [Fact]
        public void 열쇠가_없어도_장치는_눌린다()
        {
            var world = Fitted(MatchDeviceType.AddTime);
            var before = world.Room.Match.MatchTicksRemaining;

            // 문을 같은 자리에 둔다. 열쇠는 들고 있지 않으므로 삽입은 실패한다.
            world.Room.Objectives.SetDoor(world.ActorPosition(), 0f);

            world.Interact();

            var bonus = (int)MathF.Round(MatchConstants.DeviceTimeBonus * SimConstants.TickRate);
            Assert.True(
                world.Room.Match.MatchTicksRemaining >= before + bonus - 2,
                "문 앞에서 누른 E 가 장치에 닿지 않았다.");
        }

        // ==================================================== 파괴 (IG-015)

        /// 기획서 §5 — `DeviceDestroyHits` 발이면 부서진다. Seeker 의 유일한 반격이다.
        [Fact]
        public void 네_발을_맞으면_부서진다()
        {
            var world = Shootable();

            world.ShootDevice(MatchConstants.DeviceDestroyHits);

            Assert.True(world.DeviceHas(0, MatchDeviceState.Destroyed), "네 발을 맞았는데 멀쩡하다.");
        }

        [Fact]
        public void 세_발로는_부서지지_않는다()
        {
            var world = Shootable();

            world.ShootDevice(MatchConstants.DeviceDestroyHits - 1);

            Assert.False(world.DeviceHas(0, MatchDeviceState.Destroyed), "덜 맞았는데 부서졌다.");
        }

        /// 맞은 수가 전문에 실린다. Seeker 의 프롬프트가 "2/4" 를 그리는 값이고, 없으면 몇 발
        /// 더 쏴야 하는지 알 수 없다.
        [Fact]
        public void 맞은_수가_전문에_실린다()
        {
            var world = Shootable();

            world.ShootDevice(2);

            Assert.Equal(2, world.DeviceHits(0));
        }

        /// 부서진 장치는 쓸 수 없다. 그것이 부수는 이유의 전부다.
        [Fact]
        public void 부서진_장치는_쓸_수_없다()
        {
            var world = Shootable();
            world.ShootDevice(MatchConstants.DeviceDestroyHits);

            // 부순 자리로 걸어가 눌러 본다.
            world.StandOnDevice();

            var before = world.Room.Match.MatchTicksRemaining;
            world.Interact();

            Assert.True(world.Room.Match.MatchTicksRemaining < before, "부서진 장치가 발동했다.");
        }

        /// 잔해가 총알을 계속 먹으면 그 자리가 영구적인 엄폐물이 된다.
        [Fact]
        public void 부서진_뒤에는_총알을_막지_않는다()
        {
            var world = Shootable();
            world.ShootDevice(MatchConstants.DeviceDestroyHits);

            world.ShootDevice(3);

            Assert.Equal(MatchConstants.DeviceDestroyHits, world.DeviceHits(0));
        }

        // ==================================================== 조립

        /// 배우 발밑에 장치를 놓는다. 배치가 아니라 판정을 검사하므로 손으로 놓는다 —
        /// `KeyInsertTests` 가 문과 열쇠를 놓는 것과 같은 이유다.
        private static DeviceWorld Fitted(params MatchDeviceType[] types)
        {
            return Fitted(default, types);
        }

        private static DeviceWorld Fitted(MatchDeviceType type, Vector3 offset)
        {
            return Fitted(offset, new[] { type });
        }

        /// Seeker 앞 2.5m 에 장치 하나를 세운다. 파괴 검사는 총알이 닿아야 하므로 배우가 아니라
        /// **사수** 기준으로 놓는다.
        ///
        /// 2.5m 인 이유는 `HitTests.OffLatticeRange` 와 같다 — 픽스처 격자의 셀 중심(4m 간격)에
        /// 걸리지 않으면서 한 틱에 총알이 닿는 거리다.
        private static DeviceWorld Shootable()
        {
            var world = Fitted(default, new[] { MatchDeviceType.AddTime }, atSeeker: true);
            return world;
        }

        private static DeviceWorld Fitted(Vector3 offset, MatchDeviceType[] types, bool atSeeker = false)
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

            var spawn = atSeeker
                ? RoomFixture.Map().SpawnPosition(seeker) + new Vector3(0f, 0f, ShootRange)
                : RoomFixture.Map().SpawnPosition(actor);

            room.Objectives.Reset();

            // 문은 멀리 둔다. 가까우면 E 가 삽입으로 먼저 걸린다.
            room.Objectives.SetDoor(spawn + new Vector3(50f, 0f, 0f), 0f);

            for (var index = 0; index < types.Length; index++)
            {
                // 두 번째부터는 옆으로 밀어 둔다. 겹쳐 놓으면 어느 것이 잡히는지 모호하다.
                var slot = index == 0
                    ? spawn + offset
                    : spawn + new Vector3(0f, 0f, MatchConstants.DeviceUseRadius + 3f + index);

                room.Objectives.AddDevice(new DevicePlacement(types[index], slot, 0f));
            }

            room.Objectives.MarkPlaced();

            return new DeviceWorld(room, transport, actor, seeker);
        }

        private sealed class DeviceWorld
        {
            private uint _inputTick;

            public DeviceWorld(Room room, RecordingTransport transport, byte actor, byte seeker)
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

            public Vector3 ActorPosition() => Find(Actor).State.Position;

            /// 사수가 앞의 장치를 향해 한 발씩 쏜다.
            ///
            /// 탄창은 세 발이고 그 이상은 체인이 걸리므로, 발마다 채워 준다 — 여기서 검사하는
            /// 것은 장치가 몇 발에 부서지는가이지 탄창 규칙이 아니다(그쪽은 `ChainTests`).
            public void ShootDevice(int shots)
            {
                for (var shot = 0; shot < shots; shot++)
                {
                    var shooter = Find(Seeker);
                    shooter.Ammo = MatchConstants.SeekerMagazine;
                    shooter.NextFireTick = 0u;
                    shooter.ChainStartTick = 0u;
                    shooter.ChainDragUntilTick = 0u;
                    shooter.ChainReleaseTick = 0u;

                    _inputTick++;

                    // 요 0 이 +Z 다(`PlayerMovement.Forward`). **피치를 내려야 한다** — 총구는
                    // 눈높이(1.62m)에 있고 콘솔은 1m 라, 수평으로 쏘면 그 위를 지나간다.
                    // 콘솔 한가운데(0.5m)를 겨눈다. 피치는 **양수가 아래**다.
                    var eye = SimConstants.PlayerHeight * SimConstants.EyeHeightRatio;
                    var drop = eye - (MatchConstants.DeviceHeight * 0.5f);
                    var pitch = MathF.Atan2(drop, ShootRange);

                    Room.PostInput(
                        Seeker + 1,
                        _inputTick,
                        new InputFrame(
                            ButtonFlags.Fire,
                            0,
                            0,
                            Quantization.ToFixedYaw(0f),
                            Quantization.ToFixedPitch(pitch)));

                    Room.Advance();

                    // 발사한 틱에는 총알이 진행하지 않는다(IG-014a).
                    Advance(1);
                }
            }

            /// 배우를 장치 위로 옮긴다. 부순 뒤에 눌러 보는 검사가 쓴다.
            public void StandOnDevice()
            {
                Find(Actor).State.Position = Room.Objectives.Devices[0].Position;
            }

            public int DeviceHits(int index)
            {
                Room.Broadcast(Transport);

                Assert.True(Transport.TryLastDevices(Session, out var devices), "장치 전문이 오지 않았다.");
                Assert.True(index < devices.Length, $"장치 {index} 가 전문에 없다.");

                return MatchDeviceHits.Of(devices[index].State);
            }

            public bool ActorBleeding() => Find(Actor).Bleeding;

            public int ActorHits() => Find(Actor).Hits;

            /// 배우를 한 대 맞은 상태로 만든다. 피격 판정 자체는 `HitTests` 의 몫이므로
            /// 여기서는 결과만 세운다.
            public void WoundActor()
            {
                var victim = Find(Actor);
                victim.Hits = 1;
                victim.BleedingCleared = false;
            }

            public bool EveryoneFrozen()
            {
                foreach (var player in Room.Players)
                {
                    if ((player.Wire.Flags & EntityFlags.Frozen) == 0)
                    {
                        return false;
                    }
                }

                return true;
            }

            public bool DeviceHas(int index, MatchDeviceState flag)
            {
                Room.Broadcast(Transport);

                Assert.True(Transport.TryLastDevices(Session, out var devices), "장치 전문이 오지 않았다.");
                Assert.True(index < devices.Length, $"장치 {index} 가 전문에 없다.");

                return (devices[index].State & flag) != 0;
            }

            private PlayerEntity Find(byte playerId)
            {
                foreach (var player in Room.Players)
                {
                    if (player.PlayerId == playerId)
                    {
                        return player;
                    }
                }

                Assert.Fail("룸에 그 플레이어가 없다.");
                return null;
            }
        }
    }
}
