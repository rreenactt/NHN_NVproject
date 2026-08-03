using System;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 발사 알림(IG-028b, ADR 0003).
    ///
    /// **이 프로젝트의 유일한 알림이므로 "반복하지 않는다" 가 핵심 검사다.** 다른 모든 서버 발신
    /// 메시지는 전문이고, 그 성질을 검사하는 테스트들은 "주기마다 또 온다" 를 확인한다 — 여기서는
    /// 반대를 확인한다.
    public class FireEventTests
    {
        [Fact]
        public void 와이어_크기는_17바이트다()
        {
            Assert.Equal(17, FireEventMessage.WireSize);
        }

        [Fact]
        public void 왕복해서_같은_값이_된다()
        {
            var written = new FireEventMessage(3, -1234, 567, 890, 40000, -3000, 987654u);
            var buffer = new byte[FireEventMessage.WireSize];

            var length = MessageCodec.WriteFireEvent(buffer, written);
            Assert.Equal(FireEventMessage.WireSize, length);

            var read = MessageCodec.ReadFireEvent(buffer);

            Assert.Equal(written.ShooterId, read.ShooterId);
            Assert.Equal(written.X, read.X);
            Assert.Equal(written.Y, read.Y);
            Assert.Equal(written.Z, read.Z);
            Assert.Equal(written.Yaw, read.Yaw);
            Assert.Equal(written.Pitch, read.Pitch);
            Assert.Equal(written.Tick, read.Tick);
        }

        /// 종류가 다른 전문을 발사 알림으로 읽으면 던진다. 클라이언트의 `DispatchEvent` 가
        /// 종류를 먼저 보는 이유이고, 그것을 잊으면 2Hz 로 예외가 난다.
        [Fact]
        public void 다른_종류를_읽으면_던진다()
        {
            var buffer = new byte[FireEventMessage.WireSize];
            buffer[0] = (byte)MessageOpcode.Event;
            buffer[1] = (byte)EventKind.RoomState;

            Assert.Throws<InvalidOperationException>(() => MessageCodec.ReadFireEvent(buffer));
        }

        [Fact]
        public void 쏘면_모두에게_한_번_간다()
        {
            var world = Armed();

            world.Fire();

            // 역할 필터가 없다 — 총성이 이미 술래의 위치를 알려 주므로 예광탄을 숨기면
            // 소리의 정보만 줄어든다.
            Assert.Equal(1, world.FireEventCount(world.SeekerSession));
            Assert.Equal(1, world.FireEventCount(world.RunnerSession));
        }

        /// **이것이 "알림" 의 정의다.** 전문이면 다음 주기에 또 오지만 이것은 오지 않는다.
        [Fact]
        public void 다음_틱에는_다시_오지_않는다()
        {
            var world = Armed();

            world.Fire();
            Assert.Equal(1, world.FireEventCount(world.SeekerSession));

            // 전문 주기(2Hz = 15틱)와 목표물 주기(5초)를 모두 넘긴다.
            world.Advance(200);

            Assert.Equal(1, world.FireEventCount(world.SeekerSession));
        }

        [Fact]
        public void 아무도_쏘지_않으면_나가지_않는다()
        {
            var world = Armed();

            world.Advance(60);

            Assert.Equal(0, world.FireEventCount(world.SeekerSession));
        }

        /// 알림의 시작점이 총알의 시작점과 같아야 한다. 다르면 예광탄이 탄도와 다른 곳에서
        /// 출발하고, 그 어긋남은 총구 오프셋처럼 보여 원인을 찾기 어렵다.
        [Fact]
        public void 시작점이_총알과_같다()
        {
            var world = Armed();

            world.Fire();

            var fire = world.LastFireEvent(world.SeekerSession);
            var projectile = world.FirstProjectile();

            Assert.Equal(projectile.Position.X, Quantization.ToMeters(fire.X), 2);
            Assert.Equal(projectile.Position.Y, Quantization.ToMeters(fire.Y), 2);
            Assert.Equal(projectile.Position.Z, Quantization.ToMeters(fire.Z), 2);
        }

        /// 사수를 실어야 클라이언트가 자기 발사를 두 번 그리지 않는다.
        [Fact]
        public void 사수가_실린다()
        {
            var world = Armed();

            world.Fire();

            Assert.Equal(world.Seeker, world.LastFireEvent(world.SeekerSession).ShooterId);
        }

        /// 탄창이 비면 발사도 알림도 없다.
        [Fact]
        public void 탄창이_비면_알림도_없다()
        {
            var world = Armed();

            for (var shot = 0; shot < MatchConstants.SeekerMagazine; shot++)
            {
                world.Fire();
                world.Advance(Match.FireIntervalTicks);
            }

            var before = world.FireEventCount(world.SeekerSession);
            Assert.Equal(MatchConstants.SeekerMagazine, before);

            world.Fire();

            Assert.Equal(before, world.FireEventCount(world.SeekerSession));
        }

        private static FireWorld Armed()
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

            return new FireWorld(room, transport, seeker, runner);
        }

        private sealed class FireWorld
        {
            private uint _inputTick;

            public FireWorld(Room room, RecordingTransport transport, byte seeker, byte runner)
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

            public int SeekerSession => Seeker + 1;

            public int RunnerSession => Runner + 1;

            /// 트리거를 눌러 한 틱 돌리고 내보낸다.
            public void Fire()
            {
                _inputTick++;

                Room.PostInput(
                    SeekerSession,
                    _inputTick,
                    new InputFrame(ButtonFlags.Fire, 0, 0, 0, 0));

                Room.Advance();
                Room.Broadcast(Transport);
            }

            /// 입력 없이 돌린다. **매 틱 내보낸다** — 알림이 반복되면 여기서 수가 늘어난다.
            public void Advance(int ticks)
            {
                for (var tick = 0; tick < ticks; tick++)
                {
                    Room.Advance();
                    Room.Broadcast(Transport);
                }
            }

            public int FireEventCount(int session) =>
                Transport.CountOfEvent(session, EventKind.FireEvent);

            public FireEventMessage LastFireEvent(int session)
            {
                Assert.True(Transport.TryLastEvent(session, EventKind.FireEvent, out var payload));
                return MessageCodec.ReadFireEvent(payload);
            }

            public Projectile FirstProjectile()
            {
                var projectiles = Room.Projectiles;

                for (var index = 0; index < projectiles.Length; index++)
                {
                    if (projectiles[index].Active)
                    {
                        return projectiles[index];
                    }
                }

                Assert.Fail("살아 있는 총알이 없다.");
                return default;
            }
        }
    }
}
