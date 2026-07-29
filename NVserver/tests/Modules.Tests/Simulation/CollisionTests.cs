using System;
using System.Numerics;
using NV.Shared.Collision;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Simulation
{
    public class CollisionTests
    {
        private static CollisionWorld FlatFloor()
        {
            return new CollisionWorld(new[]
            {
                new Aabb(new Vector3(-50f, -1f, -50f), new Vector3(50f, 0f, 50f)),
            });
        }

        [Fact]
        public void 겹치지_않는_박스는_교차하지_않는다()
        {
            var left = new Aabb(new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 1f));
            var right = new Aabb(new Vector3(2f, 0f, 0f), new Vector3(3f, 1f, 1f));

            Assert.False(left.Overlaps(right));
        }

        [Fact]
        public void 면이_맞닿은_박스는_교차로_보지_않는다()
        {
            // 접촉을 겹침으로 처리하면 바닥에 서 있는 것만으로 매 틱 밀려난다.
            var left = new Aabb(new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 1f));
            var right = new Aabb(new Vector3(1f, 0f, 0f), new Vector3(2f, 1f, 1f));

            Assert.False(left.Overlaps(right));
        }

        [Fact]
        public void 레이는_박스_진입_시점과_법선을_돌려준다()
        {
            var box = new Aabb(new Vector3(1f, -1f, -1f), new Vector3(2f, 1f, 1f));

            var crossed = Raycaster.RayAabb(
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                box,
                out var tEnter,
                out var tExit,
                out var normal);

            Assert.True(crossed);
            Assert.Equal(1f, tEnter, 5);
            Assert.Equal(2f, tExit, 5);
            Assert.Equal(-1f, normal.X);
        }

        [Fact]
        public void 축_방향_이동이_없으면_슬랩_포함_여부만_본다()
        {
            var box = new Aabb(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));

            // Y 로만 움직이는 레이. X·Z 슬랩 밖에서 출발하면 절대 닿지 않는다.
            Assert.False(Raycaster.RayAabb(
                new Vector3(5f, -10f, 0f),
                new Vector3(0f, 1f, 0f),
                box,
                out _,
                out _,
                out _));

            Assert.True(Raycaster.RayAabb(
                new Vector3(0f, -10f, 0f),
                new Vector3(0f, 1f, 0f),
                box,
                out _,
                out _,
                out _));
        }

        [Fact]
        public void 레이캐스트는_가장_가까운_박스를_고른다()
        {
            var world = new CollisionWorld(new[]
            {
                new Aabb(new Vector3(5f, -1f, -1f), new Vector3(6f, 1f, 1f)),
                new Aabb(new Vector3(2f, -1f, -1f), new Vector3(3f, 1f, 1f)),
            });

            var found = world.Raycast(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), 100f, out var hit);

            Assert.True(found);
            Assert.Equal(1, hit.BoxIndex);
            Assert.Equal(2f, hit.Distance, 4);
        }

        [Fact]
        public void 레이캐스트는_최대_거리를_넘으면_실패한다()
        {
            var world = new CollisionWorld(new[]
            {
                new Aabb(new Vector3(10f, -1f, -1f), new Vector3(11f, 1f, 1f)),
            });

            Assert.False(world.Raycast(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), 5f, out _));
        }

        [Fact]
        public void 스윕은_벽을_통과하지_않는다()
        {
            var world = new CollisionWorld(new[]
            {
                new Aabb(new Vector3(2f, -1f, -10f), new Vector3(3f, 10f, 10f)),
            });

            var halfExtents = new Vector3(0.4f, 0.9f, 0.4f);
            var result = world.MoveBox(new Vector3(0f, 0.9f, 0f), halfExtents, new Vector3(100f, 0f, 0f), 1f);

            Assert.True(result.Hit);
            Assert.True(result.Center.X <= 2f - 0.4f + 0.01f, $"X = {result.Center.X}");
        }

        [Fact]
        public void 스윕은_벽을_따라_미끄러진다()
        {
            var world = new CollisionWorld(new[]
            {
                new Aabb(new Vector3(2f, -1f, -10f), new Vector3(3f, 10f, 10f)),
            });

            var halfExtents = new Vector3(0.4f, 0.9f, 0.4f);

            // 벽으로 45도 들이받는다. X 는 막히고 Z 는 살아야 한다.
            var result = world.MoveBox(new Vector3(0f, 0.9f, 0f), halfExtents, new Vector3(10f, 0f, 10f), 1f);

            Assert.True(result.Hit);
            Assert.True(result.Center.Z > 5f, $"Z = {result.Center.Z}");
            Assert.Equal(0f, result.Velocity.X, 4);
            Assert.Equal(10f, result.Velocity.Z, 4);
        }

        [Fact]
        public void 스윕은_바닥에_닿으면_바닥으로_보고한다()
        {
            var world = FlatFloor();
            var halfExtents = new Vector3(0.4f, 0.9f, 0.4f);

            var result = world.MoveBox(new Vector3(0f, 5f, 0f), halfExtents, new Vector3(0f, -100f, 0f), 1f);

            Assert.True(result.Grounded);
            Assert.Equal(1f, result.LastNormal.Y);
            Assert.True(result.Center.Y >= 0.9f, $"Y = {result.Center.Y}");
        }

        [Fact]
        public void 겹친_상태는_가장_얕은_축으로_밀려난다()
        {
            var world = FlatFloor();
            var halfExtents = new Vector3(0.4f, 0.9f, 0.4f);

            // 바닥에 0.4m 파묻힌 상태.
            var pushed = world.Depenetrate(new Vector3(0f, 0.5f, 0f), halfExtents);

            Assert.True(pushed.Y >= 0.9f, $"Y = {pushed.Y}");
            Assert.Equal(0f, pushed.X);
            Assert.Equal(0f, pushed.Z);
        }

        [Fact]
        public void 착지_탐침은_공중에서_거짓이다()
        {
            var world = FlatFloor();
            var halfExtents = new Vector3(0.4f, 0.9f, 0.4f);

            Assert.False(world.IsGrounded(new Vector3(0f, 5f, 0f), halfExtents, SimConstants.GroundProbeDistance));
            Assert.True(world.IsGrounded(new Vector3(0f, 0.9f, 0f), halfExtents, SimConstants.GroundProbeDistance));
        }

        [Fact]
        public void 빈_월드에서는_이동이_그대로_적용된다()
        {
            var world = new CollisionWorld(new Aabb[0]);

            var result = world.MoveBox(
                new Vector3(0f, 0f, 0f),
                new Vector3(0.4f, 0.9f, 0.4f),
                new Vector3(3f, 0f, 4f),
                1f);

            Assert.False(result.Hit);
            Assert.Equal(3f, result.Center.X, 5);
            Assert.Equal(4f, result.Center.Z, 5);
        }

        [Fact]
        public void 맵_해시는_박스가_바뀌면_달라진다()
        {
            var first = new MapData
            {
                Name = "arena",
                Boxes = new[] { new MapBox { MinX = 0f, MinY = 0f, MinZ = 0f, MaxX = 1f, MaxY = 1f, MaxZ = 1f } },
            };

            var second = new MapData
            {
                Name = "arena",
                Boxes = new[] { new MapBox { MinX = 0f, MinY = 0f, MinZ = 0f, MaxX = 1f, MaxY = 1f, MaxZ = 1.5f } },
            };

            Assert.NotEqual(first.ComputeHash(), second.ComputeHash());
            Assert.Equal(first.ComputeHash(), first.ComputeHash());
        }

        [Fact]
        public void 맵_해시는_맵없음_센티널과_충돌하지_않는다()
        {
            var map = new MapData { Name = "arena", Boxes = new MapBox[0] };

            Assert.NotEqual(0u, map.ComputeHash());
        }
    }
}
