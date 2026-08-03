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
    /// 피격 판정이 서버에서 이루어지는가(IG-014b).
    ///
    /// 픽스처의 두 스폰은 (0,0,0) 과 (-2,0,0) 으로 2m 떨어져 있다. **Seeker 가 Runner 를 향해
    /// 요를 맞추면 한 틱 안에 닿는다**(한 틱 4m) — 그래서 이 검사들은 걷지 않는다.
    public class HitTests
    {
        /// Seeker 스폰에서 Runner 스폰을 향하는 요.
        ///
        /// 전방이 `(sin yaw, 0, cos yaw)` 이므로 -X 를 보려면 yaw = -90° 다. 스폰이 바뀌면
        /// 이 값도 바뀌어야 하므로 좌표에서 구한다.
        private static float YawFromTo(Vector3 from, Vector3 to)
        {
            var delta = to - from;
            return MathF.Atan2(delta.X, delta.Z);
        }

        [Fact]
        public void Seeker가_Runner를_쏘면_맞는다()
        {
            var world = Duel();

            world.FireAtRunner();

            Assert.Equal(1, world.HitsOnRunner());
        }

        /// 기획서 §4.1 — 1방은 출혈이다. 흔적이 도망치는 Runner 를 쫓는 장치이므로
        /// 클라이언트가 즉시 알아야 하고, 그래서 스냅샷 플래그다.
        [Fact]
        public void 한_방_맞으면_출혈_플래그가_선다()
        {
            var world = Duel();

            world.FireAtRunner();

            Assert.True(world.HasFlag(world.Runner, EntityFlags.Bleeding));
            Assert.False(world.HasFlag(world.Runner, EntityFlags.Downed));
        }

        /// 순간이동이 벌칙의 무게다 — 하던 일이 끝나고 자기가 어디 있는지 모르게 된다.
        [Fact]
        public void 맞으면_다른_곳으로_옮겨진다()
        {
            var world = Duel();

            // `FireAtRunner` 가 Runner 를 사수 앞 2m 에 세운다. 맞은 뒤 그 자리에 있으면
            // 순간이동이 일어나지 않은 것이다.
            var shotAt = world.PositionOf(world.Seeker) + new Vector3(0f, 0f, 2f);

            world.FireAtRunner();

            Assert.NotEqual(shotAt, world.PositionOf(world.Runner));
        }

        /// **이것이 무적 창의 존재 이유다.** 탄창 3발이 동시에 공중에 있을 수 있으므로,
        /// 창이 없으면 한 번의 연사가 순간이동을 관통해 죽인다.
        [Fact]
        public void 무적_창_안의_두_번째_피격은_무시된다()
        {
            var world = Duel();

            world.FireAtRunner();
            Assert.Equal(1, world.HitsOnRunner());

            // 옮겨진 Runner 를 다시 조준해 쏜다 — 창이 없으면 이것이 두 번째 피격이 된다.
            world.Advance(Match.FireIntervalTicks);
            world.FireAtRunner();

            Assert.Equal(1, world.HitsOnRunner());
            Assert.False(world.HasFlag(world.Runner, EntityFlags.Downed));
        }

        /// 기획서 §4.1 — 2방이면 쓰러진다.
        [Fact]
        public void 창이_지난_뒤의_두_번째_피격은_쓰러뜨린다()
        {
            var world = Duel();

            world.FireAtRunner();
            world.Advance(Match.HitImmunityTicks);
            world.FireAtRunner();

            Assert.Equal(MatchConstants.RunnerHitsToDie, world.HitsOnRunner());
            Assert.True(world.HasFlag(world.Runner, EntityFlags.Downed));

            // 쓰러진 뒤에는 출혈이 의미가 없다.
            Assert.False(world.HasFlag(world.Runner, EntityFlags.Bleeding));
        }

        /// 총구가 자기 몸 안에서 시작하므로, 제외하지 않으면 발사한 틱에 자기가 맞는다.
        ///
        /// **Runner 를 사격선에서 먼저 빼낸다.** 스폰 두 개는 X 만 다르므로(0 과 -2), 어느
        /// 플레이어가 Seeker 로 뽑히느냐에 따라 +X 사격선에 Runner 가 정확히 놓인다 —
        /// 처음 쓴 버전이 그 우연에 걸려 전체 실행에서만 실패했다.
        [Fact]
        public void 쏜_사람은_자기_총알에_맞지_않는다()
        {
            var world = Duel();

            world.Teleport(world.Runner, new Vector3(0f, 0f, -10f));

            // +X 로 쏜다. 벽(x 5~6)에 맞고 사라질 뿐이다.
            world.Fire(MathF.PI * 0.5f);
            world.Advance(3);

            Assert.Equal(0, world.HitsOn(world.Seeker));
        }

        // 기획서 §4 의 "술래는 총을 맞지 않는다" 는 **실사격으로 검사할 수 없다.** 2인 매치에
        // Seeker 는 한 명이고 Runner 는 쏠 수 없으므로(IG-014a), 술래를 향해 날아가는 총알을
        // 만들 방법이 없다. `쏜_사람은_자기_총알에_맞지_않는다` 가 소유자 제외까지만 덮는다 —
        // 역할 검사는 3인 이상 매치나 IG-014c 이후의 스모크에서 확인해야 한다.

        /// 쓰러진 사람은 더 맞지 않는다. 계속 맞으면 피격 수가 무한히 오른다.
        [Fact]
        public void 쓰러진_사람은_더_맞지_않는다()
        {
            var world = Duel();

            world.FireAtRunner();
            world.Advance(Match.HitImmunityTicks);
            world.FireAtRunner();
            Assert.True(world.HasFlag(world.Runner, EntityFlags.Downed));

            world.Advance(Match.HitImmunityTicks);
            world.FireAtRunner();

            Assert.Equal(MatchConstants.RunnerHitsToDie, world.HitsOnRunner());
        }

        /// 사망 지점에 열쇠를 흘린다. 룰셋 — 목표가 되돌아온다.
        [Fact]
        public void 쓰러지면_소지한_열쇠를_흘린다()
        {
            var world = Duel();

            // Runner 스폰에 열쇠 두 개를 놓고 한 틱 — 습득 판정이 둘 다 집는다(IG-012a).
            world.Room.Objectives.Reset();
            world.Room.Objectives.AddKey(world.PositionOf(world.Runner));
            world.Room.Objectives.AddKey(world.PositionOf(world.Runner));
            world.Room.Objectives.MarkPlaced();
            world.Advance(1);
            Assert.Empty(world.Room.Objectives.Keys);

            world.FireAtRunner();
            world.Advance(Match.HitImmunityTicks);
            world.FireAtRunner();

            Assert.True(world.HasFlag(world.Runner, EntityFlags.Downed));
            Assert.Equal(2, world.Room.Objectives.Keys.Count);
        }

        /// 벽 뒤의 사람은 맞지 않는다. 지오메트리와 사람을 따로 검사하면 이것이 깨진다.
        [Fact]
        public void 벽_뒤의_사람은_맞지_않는다()
        {
            var world = Duel();

            // 벽(x 5~6) 반대편으로 Runner 를 옮긴다. 그쪽을 향해 쏘면 벽이 먼저다.
            world.Teleport(world.Runner, new Vector3(10f, 0f, 0f));

            world.Fire(YawFromTo(world.PositionOf(world.Seeker), new Vector3(10f, 0f, 0f)));
            world.Advance(5);

            Assert.Equal(0, world.HitsOnRunner());
        }

        /// 피격 수가 매치 전문에 실린다. HUD 가 그것으로 부상 표시를 그린다.
        [Fact]
        public void 피격_수가_매치_전문에_실린다()
        {
            var world = Duel();

            world.FireAtRunner();
            world.Room.Broadcast(world.Transport);

            Assert.True(world.Transport.TryLastMatchState(world.Runner + 1, out _, out var participants));

            foreach (var participant in participants)
            {
                if (participant.PlayerId == world.Runner)
                {
                    Assert.Equal(1, participant.Hits);
                    return;
                }
            }

            Assert.Fail($"플레이어 {world.Runner} 가 전문에 없다.");
        }

        /// 다음 매치를 부상 상태로 시작하지 않는다.
        [Fact]
        public void 다음_매치는_피격_수를_물려받지_않는다()
        {
            var world = Duel();

            world.FireAtRunner();
            Assert.Equal(1, world.HitsOnRunner());

            world.Room.PostCommand(RoomCommand.ReturnToLobby(1));
            world.Room.Advance();

            world.Room.PostCommand(RoomCommand.Start(1));
            world.Room.Advance();
            RoomFixture.SkipReveal(world.Room);

            Assert.Equal(0, world.HitsOnRunner());
        }

        private static HitWorld Duel()
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

            return new HitWorld(room, transport, seeker, runner);
        }

        private sealed class HitWorld
        {
            private uint _inputTick;

            public HitWorld(Room room, RecordingTransport transport, byte seeker, byte runner)
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

            /// Runner 를 사수 앞 2m 에 세우고 한 발 쏜다.
            ///
            /// **자리를 정해 놓고 쏘는 것이 의도다.** 피격 순간이동의 착지점은 무작위이고
            /// 픽스처 맵에는 벽(x 5~6)이 있으므로, 옮겨진 자리를 그대로 조준하면 검사가
            /// "벽 뒤로 옮겨졌는지" 에 따라 흔들린다. 여기서 확인하는 것은 판정이다.
            public void FireAtRunner()
            {
                var target = PositionOf(Seeker) + new Vector3(0f, 0f, 2f);
                Teleport(Runner, target);

                // 요 0 이 +Z 다(`PlayerMovement.Forward`).
                Fire(0f);

                // 발사한 틱에는 총알이 진행하지 않는다(IG-014a) — 한 틱 더 돌려야 닿는다.
                Advance(1);
            }

            public void Fire(float yaw)
            {
                _inputTick++;

                Room.PostInput(
                    Seeker + 1,
                    _inputTick,
                    new InputFrame(
                        ButtonFlags.Fire,
                        0,
                        0,
                        Quantization.ToFixedYaw(yaw),
                        0));

                Room.Advance();
            }

            public void Advance(int ticks)
            {
                for (var tick = 0; tick < ticks; tick++)
                {
                    Room.Advance();
                }
            }

            public int HitsOnRunner() => HitsOn(Runner);

            /// 전문에서 읽는다 — 서버 내부 값이 아니라 나가는 값을 확인한다.
            public int HitsOn(byte playerId)
            {
                Room.Broadcast(Transport);

                Assert.True(Transport.TryLastMatchState(playerId + 1, out _, out var participants));

                foreach (var participant in participants)
                {
                    if (participant.PlayerId == playerId)
                    {
                        return participant.Hits;
                    }
                }

                Assert.Fail($"플레이어 {playerId} 가 전문에 없다.");
                return 0;
            }

            public bool HasFlag(byte playerId, EntityFlags flag)
            {
                Room.Broadcast(Transport);

                Assert.True(Transport.TryLastSnapshot(Seeker + 1, out _, out var entities));

                foreach (var entity in entities)
                {
                    if (entity.Id == playerId)
                    {
                        return (entity.Flags & flag) != 0;
                    }
                }

                Assert.Fail($"플레이어 {playerId} 가 스냅샷에 없다.");
                return false;
            }

            public Vector3 PositionOf(byte playerId)
            {
                Room.Broadcast(Transport);

                Assert.True(Transport.TryLastSnapshot(Seeker + 1, out _, out var entities));

                foreach (var entity in entities)
                {
                    if (entity.Id == playerId)
                    {
                        return new Vector3(
                            Quantization.ToMeters(entity.X),
                            Quantization.ToMeters(entity.Y),
                            Quantization.ToMeters(entity.Z));
                    }
                }

                Assert.Fail($"플레이어 {playerId} 가 스냅샷에 없다.");
                return default;
            }

            /// 판정을 검사하기 위해 몸을 옮긴다. 이동 경로를 타지 않는다.
            public void Teleport(byte playerId, Vector3 position)
            {
                foreach (var player in Room.Players)
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
        }
    }
}
