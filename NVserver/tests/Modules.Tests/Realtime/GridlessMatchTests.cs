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
    /// 격자가 없는 맵에서 매치가 어디까지 도는가.
    ///
    /// **이것이 개발 루프의 기본 경로다.** `test-room` 은 의도적으로 격자를 내놓지 않는다(D-6 —
    /// 중앙 플랫폼과 커버 블록이 있어 전부 `FreeFloor` 로 채우면 블록 안이 걸을 수 있는 곳이 되고,
    /// 그 씬은 매치 규칙을 돌리지 않는다). 그런데 `MultiplayerTest` 씬이 그 룸에 붙으므로
    /// **두 클라이언트로 확인할 때 실제로 도는 것은 이 열화 모드다.**
    ///
    /// 목표물 전문이 나가지 않는 것은 이미 `RoomTests` 가 고정한다. **여기서 고정하는 것은
    /// 전투다** — 격자는 피격 순간이동에만 필요하므로, 나머지가 전부 정상이고 그 하나만 빠지는
    /// 것이 정확한 경계다. 그 경계가 코드 어디에도 적혀 있지 않았다.
    public class GridlessMatchTests
    {
        [Fact]
        public void 격자가_없어도_매치는_진행_단계로_들어간다()
        {
            var world = GridlessDuel();

            Assert.Equal(MatchPhase.Playing, world.Room.MatchPhase);
            Assert.False(world.Room.Objectives.Placed);
        }

        /// 목표물 판정 세 개가 전부 조용히 지나가야 한다. 하나라도 `Placed` 를 확인하지 않으면
        /// 여기서 터진다 — `Objectives` 의 목록이 비어 있고 문 좌표가 원점이기 때문이다.
        [Fact]
        public void 목표물_판정은_조용히_지나간다()
        {
            var world = GridlessDuel();

            Assert.Null(Record.Exception(() => world.Advance(120)));
            Assert.Equal(0, world.Room.Match.KeysInserted);
            Assert.Equal(0, world.Room.Match.Escapes);
        }

        /// 발사와 비행은 격자를 쓰지 않는다.
        [Fact]
        public void 격자가_없어도_총알이_날아가_벽에_맞는다()
        {
            var world = GridlessDuel();

            // 픽스처 맵의 벽은 x = 5~6 이고 격자와 무관하게 콜리전에 있다.
            world.Fire(MathF.PI * 0.5f);
            Assert.Equal(1, world.ActiveProjectiles());

            world.Advance(4);

            Assert.Equal(0, world.ActiveProjectiles());
        }

        /// **피격은 성립한다.** 격자가 없다고 총을 맞지 않게 되면 개발 루프에서 전투를 확인할
        /// 방법이 없어진다.
        [Fact]
        public void 격자가_없어도_피격이_성립한다()
        {
            var world = GridlessDuel();

            world.ShootRunner();

            Assert.Equal(1, world.HitsOnRunner());
            Assert.True(world.HasFlag(world.Runner, EntityFlags.Bleeding));
        }

        /// **여기가 정확한 경계다 — 순간이동만 빠진다.** `TeleportToRandomFreeFloor` 가 격자를
        /// 요구하므로 맞은 Runner 는 제자리에 남는다(경고 로그만 남는다).
        ///
        /// 이 검사가 없으면 나중에 그 early-return 을 예외로 바꾸거나 `ApplyHit` 의 순서를
        /// 뒤집는 변경이 **개발 루프만 조용히 깨뜨린다** — 격자가 있는 맵의 테스트는 전부 통과한다.
        [Fact]
        public void 격자가_없으면_피격_순간이동이_일어나지_않는다()
        {
            var world = GridlessDuel();

            // `ShootRunner` 가 Runner 를 사수 앞 2m 로 옮긴 뒤 쏜다. 맞은 뒤에도 **그 자리에
            // 있어야** 순간이동이 일어나지 않은 것이다 — 호출 전 위치(스폰)와 비교하면 헬퍼가
            // 옮긴 것을 순간이동으로 착각한다.
            var shotAt = world.PositionOf(world.Seeker) + new Vector3(0f, 0f, 2f);

            world.ShootRunner();

            var after = world.PositionOf(world.Runner);

            Assert.Equal(shotAt.X, after.X, 2);
            Assert.Equal(shotAt.Z, after.Z, 2);
        }

        /// 사망 판정도 격자와 무관하다.
        [Fact]
        public void 격자가_없어도_두_번_맞으면_쓰러진다()
        {
            var world = GridlessDuel();

            world.ShootRunner();
            world.Advance(Match.HitImmunityTicks);
            world.ShootRunner();

            Assert.Equal(MatchConstants.RunnerHitsToDie, world.HitsOnRunner());
            Assert.True(world.HasFlag(world.Runner, EntityFlags.Downed));
        }

        private static GridlessWorld GridlessDuel()
        {
            var room = RoomFixture.Create(withGrid: false);
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

            return new GridlessWorld(room, transport, seeker, runner);
        }

        private sealed class GridlessWorld
        {
            private uint _inputTick;

            public GridlessWorld(Room room, RecordingTransport transport, byte seeker, byte runner)
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

            public int Session => Seeker + 1;

            /// Runner 를 사수 앞 2m 에 세우고 한 발 맞힌다 — `HitTests` 와 같은 이유로 자리를
            /// 고정한다(누가 Seeker 로 뽑히는지가 매치마다 다르다).
            public void ShootRunner()
            {
                Teleport(Runner, PositionOf(Seeker) + new Vector3(0f, 0f, 2f));

                Fire(0f);
                Advance(1);
            }

            public void Fire(float yaw)
            {
                _inputTick++;

                Room.PostInput(
                    Session,
                    _inputTick,
                    new InputFrame(ButtonFlags.Fire, 0, 0, Quantization.ToFixedYaw(yaw), 0));

                Room.Advance();
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

                for (var index = 0; index < Room.Projectiles.Length; index++)
                {
                    if (Room.Projectiles[index].Active)
                    {
                        count++;
                    }
                }

                return count;
            }

            public int HitsOnRunner()
            {
                Room.Broadcast(Transport);

                Assert.True(Transport.TryLastMatchState(Session, out _, out var participants));

                foreach (var participant in participants)
                {
                    if (participant.PlayerId == Runner)
                    {
                        return participant.Hits;
                    }
                }

                Assert.Fail($"플레이어 {Runner} 가 전문에 없다.");
                return 0;
            }

            public bool HasFlag(byte playerId, EntityFlags flag)
            {
                Room.Broadcast(Transport);

                Assert.True(Transport.TryLastSnapshot(Session, out _, out var entities));

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
                foreach (var player in Room.Players)
                {
                    if (player.PlayerId == playerId)
                    {
                        return player.State.Position;
                    }
                }

                Assert.Fail($"플레이어 {playerId} 가 룸에 없다.");
                return default;
            }

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
