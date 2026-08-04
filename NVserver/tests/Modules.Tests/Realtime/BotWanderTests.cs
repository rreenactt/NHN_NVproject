using System;
using System.Collections.Generic;
using System.Numerics;
using NV.Realtime;
using NV.Realtime.Contracts;
using NV.Realtime.Simulation;
using NV.Shared.Collision;
using NV.Shared.Contracts.Enums;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    /// 배회하는 봇. 격자에서 목표점을 뽑아 걷고, 막히면 다른 목표를 뽑는다.
    ///
    /// 여기서 검증하는 것은 봇의 영리함이 아니라 **봇이 사람과 같은 판정을 지난다는
    /// 것**이다 — 벽에 막히고, 속도 상한 안에 있고, 씨드가 같으면 같은 궤적이다.
    public class BotWanderTests
    {
        /// `RoomFixture.Map` 의 벽. x 5~6, 높이 4.
        private static readonly Aabb Wall = new Aabb(
            new Vector3(5f, 0f, -20f),
            new Vector3(6f, 4f, 20f));

        [Fact]
        public void 배회_봇은_스폰에서_벗어난다()
        {
            var room = Playing(BotBehavior.Wander, seed: 20260804u);
            var bot = TheBot(room);
            var spawn = bot.State.Position;

            Advance(room, 90);

            Assert.True(
                Horizontal(bot.State.Position, spawn) > 1f,
                $"봇이 스폰 부근에 머물렀다. {spawn} → {bot.State.Position}");
        }

        [Fact]
        public void 배회_봇은_벽_안으로_들어가지_않는다()
        {
            // 봇은 목표를 향해 직선으로 걷는다. 목표가 벽 건너편이면 벽에 부딪히며,
            // 그때 몸이 벽 안으로 들어가지 않는 것은 `PlayerMovement` 의 판정이다 —
            // 봇이 그 판정을 지난다는 증거가 이것이다.
            var room = Playing(BotBehavior.Wander, seed: 7u);
            var bot = TheBot(room);

            for (var tick = 0; tick < 600; tick++)
            {
                room.Advance();

                // 발밑 점이 아니라 **몸 박스**로 본다. 점으로 보면 벽에 반쯤 파묻힌
                // 상태를 통과시키고, 이동 판정이 쓰는 것도 이 박스다.
                Assert.False(
                    Body(bot).Overlaps(Wall),
                    $"틱 {tick} 에 봇의 몸이 벽과 겹쳤다. {bot.State.Position}");
            }
        }

        [Fact]
        public void 배회_봇은_속도_상한을_넘지_않는다()
        {
            var room = Playing(BotBehavior.Wander, seed: 99u);
            var bot = TheBot(room);

            var limit = RealtimeConstants.Validation.MaxHorizontalSpeed
                * RealtimeConstants.Validation.SpeedTolerance;

            for (var tick = 0; tick < 300; tick++)
            {
                room.Advance();

                var velocity = bot.State.Velocity;
                var speed = MathF.Sqrt((velocity.X * velocity.X) + (velocity.Z * velocity.Z));

                Assert.True(speed <= limit, $"틱 {tick} 의 수평 속도 {speed} 가 상한 {limit} 을 넘었다.");
            }
        }

        [Fact]
        public void 씨드가_같으면_궤적이_같다()
        {
            // 눈으로 본 문제를 다시 만들 수 있어야 한다. 봇 난수가 `Random.Shared` 를
            // 쓰면 이 테스트가 깨지고, 그것이 이 테스트가 지키는 것이다.
            var first = Trace(seed: 4242u);
            var second = Trace(seed: 4242u);

            Assert.Equal(first, second);
        }

        [Fact]
        public void 씨드가_다르면_궤적이_갈린다()
        {
            var first = Trace(seed: 1u);
            var second = Trace(seed: 2u);

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void 격자가_없는_맵에서는_배회_봇도_서_있는다()
        {
            // 목표를 뽑을 곳이 없다. 아무 방향으로 걷게 하면 벽을 밀며 진동하고,
            // 그것은 이동 판정을 검증하는 데 도움이 되지 않는다.
            var room = Playing(BotBehavior.Wander, seed: 5u, withGrid: false);
            var bot = TheBot(room);
            var spawn = bot.State.Position;

            Advance(room, 120);

            Assert.Equal(spawn.X, bot.State.Position.X, 3);
            Assert.Equal(spawn.Z, bot.State.Position.Z, 3);
        }

        [Fact]
        public void 리빌_중에는_배회_봇도_움직이지_않는다()
        {
            // 이동 잠금이 사람과 봇에 같이 걸린다는 것. `ApplyFrame` 을 공유하는 이유다.
            var room = RoomFixture.WithBots(behavior: BotBehavior.Wander, seed: 11u);

            RoomFixture.JoinHuman(room, 1, isHost: true);
            RoomFixture.SettleBots(room);

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();

            var bot = TheBot(room);
            var atStart = bot.State.Position;

            // 리빌 구간 안에서만 돈다. `RoleRevealDuration` 4초 = 120틱이므로 60틱은 그 안이다.
            Advance(room, 60);

            Assert.Equal(MatchPhase.RoleReveal, room.MatchPhase);
            Assert.Equal(atStart.X, bot.State.Position.X, 3);
            Assert.Equal(atStart.Z, bot.State.Position.Z, 3);
        }

        /// 봇 하나가 배회하는 매치. 리빌까지 지나간 상태로 돌려준다.
        private static Room Playing(BotBehavior behavior, uint seed, bool withGrid = true)
        {
            var room = RoomFixture.WithBots(behavior: behavior, seed: seed, withGrid: withGrid);

            RoomFixture.JoinHuman(room, 1, isHost: true);
            RoomFixture.SettleBots(room);

            room.PostCommand(RoomCommand.Start(1));
            room.Advance();
            RoomFixture.SkipReveal(room);

            return room;
        }

        /// 이 씨드로 60틱 동안의 봇 위치를 기록한다.
        private static List<Vector3> Trace(uint seed)
        {
            var room = Playing(BotBehavior.Wander, seed);
            var bot = TheBot(room);
            var path = new List<Vector3>(60);

            for (var tick = 0; tick < 60; tick++)
            {
                room.Advance();
                path.Add(bot.State.Position);
            }

            return path;
        }

        private static PlayerEntity TheBot(Room room)
        {
            PlayerEntity? found = null;

            foreach (var player in room.Players)
            {
                if (player.IsBot)
                {
                    Assert.Null(found);
                    found = player;
                }
            }

            Assert.NotNull(found);
            return found!;
        }

        /// 이 몸의 판정 박스. `Room.BodyOf` 와 같은 치수여야 의미가 있다.
        private static Aabb Body(PlayerEntity player)
        {
            var half = new Vector3(
                SimConstants.PlayerRadius,
                SimConstants.PlayerHeight * 0.5f,
                SimConstants.PlayerRadius);

            return Aabb.FromCenter(player.State.Position + new Vector3(0f, half.Y, 0f), half);
        }

        private static void Advance(Room room, int ticks)
        {
            for (var index = 0; index < ticks; index++)
            {
                room.Advance();
            }
        }

        private static float Horizontal(Vector3 a, Vector3 b)
        {
            var dx = a.X - b.X;
            var dz = a.Z - b.Z;

            return MathF.Sqrt((dx * dx) + (dz * dz));
        }
    }
}
