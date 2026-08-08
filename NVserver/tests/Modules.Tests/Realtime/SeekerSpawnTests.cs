using System.Numerics;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 매치 시작 배치 — Seeker 는 체인이 끝나는 자리(제단 착지점)에서 시작한다.
    ///
    /// **역할은 무작위이므로 가정하지 않는다.** `PickSeeker` 가 `Random.Shared` 를 쓰므로
    /// "플레이어 0 이 Seeker 다" 라고 적으면 절반의 확률로 다른 것을 검사한다 — 룸이 말하는
    /// `Seeker` 를 물어서 그 사람을 본다(`conventions.md` 의 flaky 사례와 같은 규칙).
    ///
    /// 위치는 XZ 로 비교한다. 스폰 직후의 첫 틱에 중력이 발밑을 정리하므로 Y 는 배치 값
    /// 그대로가 아니다 — 배치가 검사 대상이고, 배치는 XZ 다.
    public class SeekerSpawnTests
    {
        [Fact]
        public void Seeker는_제단_착지점에서_시작한다()
        {
            var room = RoomFixture.Create();
            RoomFixture.FillAndStart(room);

            var seeker = FindByRole(room, MatchRole.Seeker);
            var altar = room.Objectives.AltarDragPoint;

            Assert.True(
                DistanceXZ(seeker.State.Position, altar) < 0.05f,
                $"Seeker 가 제단 착지점({altar})이 아니라 {seeker.State.Position} 에서 시작했다.");
        }

        [Fact]
        public void Runner는_자기_링_스폰에서_시작한다()
        {
            var room = RoomFixture.Create();
            RoomFixture.FillAndStart(room);

            var runner = FindByRole(room, MatchRole.Runner);
            var expected = RoomFixture.Map().SpawnPosition(runner.PlayerId);

            Assert.True(
                DistanceXZ(runner.State.Position, expected) < 0.05f,
                $"Runner {runner.PlayerId} 가 링 스폰({expected})이 아니라 {runner.State.Position} 에서 시작했다.");
        }

        /// 격자가 없는 맵에는 제단이 없다. 그 맵은 체인 벌칙도 없으므로 Seeker 도 링
        /// 스폰으로 돌아간다 — 열화의 경계가 하나여야 한다.
        [Fact]
        public void 격자가_없는_맵에서는_Seeker도_링_스폰이다()
        {
            var room = RoomFixture.Create(withGrid: false);
            RoomFixture.FillAndStart(room);

            var seeker = FindByRole(room, MatchRole.Seeker);
            var expected = RoomFixture.Map(withGrid: false).SpawnPosition(seeker.PlayerId);

            Assert.True(
                DistanceXZ(seeker.State.Position, expected) < 0.05f,
                $"격자 없는 맵의 Seeker 가 링 스폰({expected})이 아니라 {seeker.State.Position} 에서 시작했다.");
        }

        /// 제단 *위치* 에서 시작할 뿐 체인 *상태* 로 시작하지 않는다. 탄창이 비어 있으면
        /// 기획서 §4.3 의 벌칙(빈 탄창의 대가)이 매치 시작에 공짜로 지불된다.
        [Fact]
        public void 시작_시_체인에_걸려_있지_않고_탄창이_차_있다()
        {
            var room = RoomFixture.Create();
            RoomFixture.FillAndStart(room);

            var seeker = FindByRole(room, MatchRole.Seeker);

            Assert.False(seeker.Chained, "매치 시작부터 체인에 걸려 있다.");
            Assert.Equal(MatchConstants.SeekerMagazine, seeker.Ammo);
        }

        /// 시작 방향은 제단을 등진다 — 착지점은 제단 옆 셀이므로 그 반대가 열린 쪽이다.
        [Fact]
        public void Seeker는_제단을_등지고_시작한다()
        {
            var room = RoomFixture.Create();
            RoomFixture.FillAndStart(room);

            var seeker = FindByRole(room, MatchRole.Seeker);
            var away = DeterministicMath.Subtract(room.Objectives.AltarDragPoint, room.Objectives.AltarPosition);
            var forward = new Vector3(DeterministicMath.Sin(seeker.State.Yaw), 0f, DeterministicMath.Cos(seeker.State.Yaw));

            Assert.True(
                DeterministicMath.Dot(forward, DeterministicMath.Normalize(new Vector3(away.X, 0f, away.Z))) > 0.9f,
                $"시작 방향(yaw {seeker.State.Yaw:F3})이 제단에서 나가는 쪽({away})을 보지 않는다.");
        }

        /// 맵이 Seeker 전용 스폰(team 1)을 적었으면 그것이 제단 착지점보다 먼저다.
        [Fact]
        public void 맵이_적은_Seeker_스폰이_제단보다_먼저다()
        {
            var room = AuthoredRoom();
            RoomFixture.FillAndStart(room);

            var seeker = FindByRole(room, MatchRole.Seeker);

            Assert.True(
                DistanceXZ(seeker.State.Position, AuthoredSeekerSpawn) < 0.05f,
                $"Seeker 가 맵이 적은 자리({AuthoredSeekerSpawn})가 아니라 {seeker.State.Position} 에서 시작했다.");
        }

        /// Runner 는 Seeker 전용 스폰에 배정되지 않는다 — 링 스폰 목록은 team ≠ 1 만 감는다.
        [Fact]
        public void Runner는_Seeker_전용_스폰에_서지_않는다()
        {
            var room = AuthoredRoom();
            RoomFixture.FillAndStart(room);

            var runner = FindByRole(room, MatchRole.Runner);

            Assert.True(
                DistanceXZ(runner.State.Position, AuthoredSeekerSpawn) > 0.5f,
                $"Runner {runner.PlayerId} 가 Seeker 전용 스폰({AuthoredSeekerSpawn})에 서 있다.");
        }

        private static readonly Vector3 AuthoredSeekerSpawn = new Vector3(4f, 0f, -4f);

        /// 픽스처 맵에 Seeker 전용 스폰 하나를 얹은 룸. 나머지는 `RoomFixture.Map()` 과 같다.
        private static Room AuthoredRoom()
        {
            var data = new NV.Shared.Collision.MapData
            {
                Name = "test",
                Boxes = new[]
                {
                    new NV.Shared.Collision.MapBox { MinX = -20f, MinY = -1f, MinZ = -20f, MaxX = 20f, MaxY = 0f, MaxZ = 20f },
                    new NV.Shared.Collision.MapBox { MinX = 5f, MinY = 0f, MinZ = -20f, MaxX = 6f, MaxY = 4f, MaxZ = 20f },
                },
                Spawns = new[]
                {
                    new NV.Shared.Collision.MapSpawn { X = 0f, Y = 0f, Z = 0f, Yaw = 0f },
                    new NV.Shared.Collision.MapSpawn { X = -2f, Y = 0f, Z = 0f, Yaw = 0f },
                    new NV.Shared.Collision.MapSpawn
                    {
                        X = AuthoredSeekerSpawn.X,
                        Y = AuthoredSeekerSpawn.Y,
                        Z = AuthoredSeekerSpawn.Z,
                        Yaw = 0f,
                        Team = 1,
                    },
                },
            };

            return new Room(
                "test",
                new NV.Shared.Collision.WorldMap(data),
                RoomFixture.NoConditions(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                isStatic: false);
        }

        /// 역할을 전문에서 읽고 그 몸을 룸에서 집는다. 역할은 무작위이므로 전문이 말하는
        /// 것을 따르고(`ChainWorld.Create` 와 같은 이유), 위치·탄창은 룸의 판정을 본다.
        private static PlayerEntity FindByRole(Room room, MatchRole role)
        {
            var transport = new RecordingTransport();
            room.Broadcast(transport);

            Assert.True(transport.TryLastMatchState(1, out _, out var participants), "매치 전문이 나가지 않았다.");

            foreach (var participant in participants)
            {
                if (participant.Role != role)
                {
                    continue;
                }

                foreach (var player in room.Players)
                {
                    if (player.PlayerId == participant.PlayerId)
                    {
                        return player;
                    }
                }
            }

            Assert.Fail($"룸에 {role} 가 없다.");
            return null;
        }

        private static float DistanceXZ(Vector3 left, Vector3 right)
        {
            var dx = left.X - right.X;
            var dz = left.Z - right.Z;
            return DeterministicMath.Sqrt((dx * dx) + (dz * dz));
        }
    }
}
