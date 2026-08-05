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
    /// 서버가 총알을 날리는가(IG-014a).
    ///
    /// 피격 판정은 아직 없다(IG-014b) — 여기서 검사하는 것은 **발사 자격과 비행**이다.
    /// 픽스처 맵은 x = 5~6 에 벽이 있고 스폰은 원점 부근이므로, +X 로 쏘면 벽에 맞는다.
    public class ProjectileTests
    {
        [Fact]
        public void Seeker가_쏘면_총알이_생긴다()
        {
            var world = Armed();

            world.Fire(yaw: 0f);

            Assert.Equal(1, world.ActiveProjectiles());
        }

        /// 기획서 §4 의 총은 술래의 것이다. `RunnerHitsToDie` 가 있는 것도 맞는 쪽이
        /// Runner 뿐이기 때문이다.
        [Fact]
        public void Runner는_쏠_수_없다()
        {
            var world = Armed(asRunner: true);

            world.Fire(yaw: 0f);

            Assert.Equal(0, world.ActiveProjectiles());
        }

        /// `Fire` 는 엣지가 아니라 누르고 있는 상태다. 간격이 없으면 트리거를 누르고 있는
        /// 동안 초당 30발이 나간다.
        [Fact]
        public void 연사_간격_안에는_한_발만_나간다()
        {
            var world = Armed();

            // 같은 입력을 간격보다 짧게 여러 틱 넣는다.
            world.Fire(yaw: 0f);
            world.FireHeldFor(Match.FireIntervalTicks - 1);

            Assert.Equal(1, world.ActiveProjectiles());
        }

        [Fact]
        public void 간격이_지나면_다시_나간다()
        {
            var world = Armed();

            world.Fire(yaw: 0f);
            world.FireHeldFor(Match.FireIntervalTicks);

            Assert.Equal(2, world.ActiveProjectiles());
        }

        /// 기획서 §4.3 — 탄창 3발. 재장전은 체인이 놓아준 뒤이고 그 경로가 아직 없다(IG-016).
        [Fact]
        public void 탄창을_비우면_더_쏘지_못한다()
        {
            var world = Armed();

            // 위로 쏜다 — 천장이 없는 픽스처 맵이라 총알이 살아남아 셀 수 있다.
            for (var shot = 0; shot < MatchConstants.SeekerMagazine + 3; shot++)
            {
                world.Fire(yaw: 0f, pitch: -1.4f);
                world.Advance(Match.FireIntervalTicks);
            }

            Assert.Equal(MatchConstants.SeekerMagazine, world.ActiveProjectiles());
        }

        /// **한 틱에 4m 를 지난다.** 도착 지점만 검사하면 0.25m 벽을 통과한다 — 클라이언트의
        /// `Bullet` 이 스윕 레이캐스트를 쓰는 이유와 같다.
        [Fact]
        public void 벽에_맞으면_사라진다()
        {
            var world = Armed();

            // +X 로 쏜다. 벽은 x = 5~6 이고 스폰은 원점 부근이므로 두 틱 안에 닿는다.
            world.Fire(yaw: MathF.PI * 0.5f);
            Assert.Equal(1, world.ActiveProjectiles());

            world.Advance(3);

            Assert.Equal(0, world.ActiveProjectiles());
        }

        /// 콜리전 틈으로 빠져나간 총알을 영원히 시뮬레이션하지 않는다.
        [Fact]
        public void 수명이_지나면_사라진다()
        {
            var world = Armed();

            world.Fire(yaw: 0f, pitch: -1.4f);
            Assert.Equal(1, world.ActiveProjectiles());

            world.Advance(Match.BulletLifetimeTicks + 1);

            Assert.Equal(0, world.ActiveProjectiles());
        }

        /// 눈높이에서 나간다. 발밑에서 쏘면 바닥에 즉시 맞는다.
        [Fact]
        public void 총알은_눈높이에서_나간다()
        {
            var world = Armed();

            world.Fire(yaw: 0f);

            var expected = SimConstants.PlayerHeight * SimConstants.EyeHeightRatio;
            Assert.Equal(expected, world.FirstProjectile().Position.Y, 2);
        }

        /// 요 규약이 이동과 같아야 한다. 다르면 총알이 조준한 곳과 다른 쪽으로 날아가고,
        /// 그 증상은 "총이 이상하다" 로만 보인다.
        [Fact]
        public void 요_90도면_플러스X_로_날아간다()
        {
            var world = Armed();

            world.Fire(yaw: MathF.PI * 0.5f);

            var direction = world.FirstProjectile().Direction;

            Assert.Equal(1f, direction.X, 2);
            Assert.Equal(0f, direction.Y, 2);
            Assert.Equal(0f, direction.Z, 2);
        }

        /// 역할 공개 중에는 쏠 수 없다. 이동만 잠기고 총이 나가면 리빌이 사격장이 된다.
        [Fact]
        public void 역할_공개_중에는_쏠_수_없다()
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room, skipReveal: false);
            room.Broadcast(transport);

            var world = new ProjectileWorld(room, transport, SeekerOf(transport));

            Assert.Equal(MatchPhase.RoleReveal, room.MatchPhase);

            world.Fire(yaw: 0f);

            Assert.Equal(0, world.ActiveProjectiles());
        }

        /// 다음 매치가 지난 매치의 총알을 물려받지 않는다.
        [Fact]
        public void 다음_매치는_총알을_물려받지_않는다()
        {
            var world = Armed();

            world.Fire(yaw: 0f, pitch: -1.4f);
            Assert.Equal(1, world.ActiveProjectiles());

            world.Room.PostCommand(RoomCommand.ReturnToLobby(1));
            world.Room.Advance();

            // 로비로 돌아오면 준비가 전원 내려간다. 다시 누르지 않으면 시작되지 않는다.
            RoomFixture.Ready(world.Room, 2);

            world.Room.PostCommand(RoomCommand.Start(1));
            world.Room.Advance();

            Assert.Equal(0, world.ActiveProjectiles());
        }

        private static byte SeekerOf(RecordingTransport transport)
        {
            Assert.True(transport.TryLastMatchState(1, out _, out var participants));

            foreach (var participant in participants)
            {
                if (participant.Role == MatchRole.Seeker)
                {
                    return participant.PlayerId;
                }
            }

            Assert.Fail("Seeker 가 배정되지 않았다.");
            return 0;
        }

        private static byte RunnerOf(RecordingTransport transport)
        {
            Assert.True(transport.TryLastMatchState(1, out _, out var participants));

            foreach (var participant in participants)
            {
                if (participant.Role == MatchRole.Runner)
                {
                    return participant.PlayerId;
                }
            }

            Assert.Fail("Runner 가 배정되지 않았다.");
            return 0;
        }

        /// 탄창이 찬 Seeker(또는 Runner)로 시작한 매치.
        private static ProjectileWorld Armed(bool asRunner = false)
        {
            var room = RoomFixture.Create();
            var transport = new RecordingTransport();

            RoomFixture.FillAndStart(room);
            room.Broadcast(transport);

            var actor = asRunner ? RunnerOf(transport) : SeekerOf(transport);
            return new ProjectileWorld(room, transport, actor);
        }

        private sealed class ProjectileWorld
        {
            private uint _inputTick;

            public ProjectileWorld(Room room, RecordingTransport transport, byte actor)
            {
                Room = room;
                Transport = transport;
                Actor = actor;
            }

            public Room Room { get; }

            public RecordingTransport Transport { get; }

            public byte Actor { get; }

            public int Session => Actor + 1;

            /// 트리거를 눌러 한 틱 돌린다.
            public void Fire(float yaw, float pitch = 0f)
            {
                Post(yaw, pitch);
                Room.Advance();
            }

            /// 같은 입력을 계속 보내며 여러 틱 돌린다 — 트리거를 누르고 있는 것과 같다.
            public void FireHeldFor(int ticks)
            {
                for (var tick = 0; tick < ticks; tick++)
                {
                    Post(0f, 0f);
                    Room.Advance();
                }
            }

            public void Advance(int ticks)
            {
                for (var tick = 0; tick < ticks; tick++)
                {
                    Room.Advance();
                }
            }

            public int ActiveProjectiles()
            {
                var count = 0;
                var projectiles = Room.Projectiles;

                for (var index = 0; index < projectiles.Length; index++)
                {
                    if (projectiles[index].Active)
                    {
                        count++;
                    }
                }

                return count;
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

            private void Post(float yaw, float pitch)
            {
                _inputTick++;

                Room.PostInput(
                    Session,
                    _inputTick,
                    new InputFrame(
                        ButtonFlags.Fire,
                        0,
                        0,
                        Quantization.ToFixedYaw(yaw),
                        Quantization.ToFixedPitch(pitch)));
            }
        }
    }
}
