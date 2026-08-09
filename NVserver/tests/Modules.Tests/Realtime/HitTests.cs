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

            // `FireAtRunner` 가 Runner 를 사수 앞에 세운다. 맞은 뒤 그 자리에 있으면
            // 순간이동이 일어나지 않은 것이다. 그 자리가 격자 셀 중심이 아니어야 이 비교가
            // 성립한다 — 이유는 `FireAtRunner` 에 적혀 있다.
            var shotAt = world.PositionOf(world.Seeker)
                       + new Vector3(0f, 0f, HitWorld.OffLatticeRange);

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

        /// 쓰러진 몸은 걷지 않는다.
        ///
        /// **클라이언트가 이미 스스로 멈춘다**(`PlayerAgent.ApplyLock` 의 `!InPlay`). 그것은
        /// 부탁이고 이것이 규칙이다 — 몸은 지워지지 않고 좌표도 계속 나가므로, 서버가 막지
        /// 않으면 고쳐진 클라이언트가 **보이지 않는 몸으로 맵을 정찰한다.** 관전이 붙은
        /// 뒤로는 남의 눈으로 보면서 자기 몸으로 걷는 셈이 되어 값이 더 커졌다.
        [Fact]
        public void 쓰러진_몸은_입력을_받아도_움직이지_않는다()
        {
            var world = Duel();

            world.FireAtRunner();
            world.Advance(Match.HitImmunityTicks);
            world.FireAtRunner();
            Assert.True(world.HasFlag(world.Runner, EntityFlags.Downed));

            // 쓰러진 **뒤**의 자리를 기준으로 삼는다. 피격 순간이동이 이미 옮겨 놓았다.
            var restingPlace = world.PositionOf(world.Runner);

            // 전속력 전진을 여러 틱 보낸다. 반복 갈래(입력이 끊긴 뒤)도 함께 지나도록
            // 넣은 프레임 수보다 더 돌린다.
            world.PushForward(world.Runner, ticks: 10);
            world.Advance(10);

            var after = world.PositionOf(world.Runner);

            Assert.Equal(restingPlace.X, after.X, 3);
            Assert.Equal(restingPlace.Z, after.Z, 3);
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

        /// 흘린 열쇠는 **서로 다른 자리**에 놓인다(IG-027).
        ///
        /// 한 점에 쌓으면 그 무더기를 밟은 Runner 가 한 틱에 전부 줍고 시각적으로도 하나로
        /// 보인다. 개수만 세는 위 검사로는 그것을 구별할 수 없다.
        [Fact]
        public void 흘린_열쇠는_서로_다른_자리에_놓인다()
        {
            var world = Duel();

            world.Room.Objectives.Reset();
            for (var index = 0; index < 3; index++)
            {
                world.Room.Objectives.AddKey(world.PositionOf(world.Runner));
            }

            world.Room.Objectives.MarkPlaced();
            world.Advance(1);
            Assert.Empty(world.Room.Objectives.Keys);

            world.FireAtRunner();
            world.Advance(Match.HitImmunityTicks);
            world.FireAtRunner();

            var keys = world.Room.Objectives.Keys;
            Assert.Equal(3, keys.Count);

            for (var a = 0; a < keys.Count; a++)
            {
                for (var b = a + 1; b < keys.Count; b++)
                {
                    Assert.NotEqual(keys[a], keys[b]);
                }
            }
        }

        /// **퍼뜨린 열쇠는 전부 사망 지점에서 주울 수 있어야 한다.** 그래서 격자에 스냅하지
        /// 않아도 벽 쪽으로 밀린 열쇠가 회수 불가능해지지 않는다 — 흩뿌림 반경이 습득 반경보다
        /// 작다는 관계가 그것을 보장하고, 이 검사가 그 관계를 못질한다.
        [Fact]
        public void 흩뿌림_반경은_습득_반경보다_작다()
        {
            Assert.True(
                MatchConstants.KeyDropRadius < MatchConstants.KeyPickupRadius,
                $"흩뿌림 {MatchConstants.KeyDropRadius}m 가 습득 {MatchConstants.KeyPickupRadius}m 보다 크다. "
                + "사망 지점에서 닿지 않는 열쇠가 생기므로 격자 스냅이 필요해진다.");
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

            // 로비로 돌아오면 준비가 전원 내려간다. 다시 누르지 않으면 시작되지 않는다.
            RoomFixture.Ready(world.Room, 2);

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
            /// 사수와 표적의 거리. 픽스처 격자의 셀 중심(4m 간격)에 걸리지 않아야 한다.
            /// 이유는 <see cref="FireAtRunner"/> 에 적혀 있다.
            public const float OffLatticeRange = 2.5f;

            /// 사수 앞으로 Runner 를 세우고 한 발 쏜다.
            ///
            /// **거리가 2.5m 인 것은 격자를 피하기 위해서다.** 픽스처의 셀 중심은 4m 간격의
            /// {-10,-6,-2,2,6,10} 이고 스폰 하나가 (-2,0,0) 이므로, 2m 앞은 (-2,0,2) 즉
            /// **X 와 Z 가 모두 셀 중심**이다. 피격 순간이동은 무작위 `FreeFloor` 셀 중심으로
            /// 보내므로 그 셀이 뽑히면 사격 전후의 좌표가 같아지고, "옮겨졌다" 를 좌표 비교로
            /// 확인하는 테스트가 간헐적으로 실패한다. 원인이 격자에 있다는 신호는 어디에도
            /// 없어서 순간이동이 안 일어난 것처럼 읽힌다.
            ///
            /// 스폰을 옮기지 않는 이유는 다른 테스트들이 두 스폰의 2m 간격에 기대고 있기
            /// 때문이다(`EscapeTests` 의 반경, 이 클래스의 "한 틱 안에 닿는다"). 한 틱에
            /// 총알이 4m 가므로 2.5m 도 같은 틱에 닿는다.
            public void FireAtRunner()
            {
                var target = PositionOf(Seeker) + new Vector3(0f, 0f, OffLatticeRange);
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

            /// 이 플레이어에게 전속력 전진을 여러 틱 보낸다. 틱을 돌리지는 않는다 —
            /// 호출자가 넣은 것보다 더 돌려 반복 갈래까지 지나게 할 수 있어야 한다.
            public void PushForward(byte playerId, int ticks)
            {
                for (var sent = 0; sent < ticks; sent++)
                {
                    _inputTick++;

                    Room.PostInput(
                        playerId + 1,
                        _inputTick,
                        new InputFrame(ButtonFlags.None, 0, 127, 0, 0));
                }
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
