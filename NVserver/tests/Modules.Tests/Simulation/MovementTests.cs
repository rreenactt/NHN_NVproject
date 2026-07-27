using System;
using System.Numerics;
using NV.Shared.Collision;
using NV.Shared.Contracts.Enums;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Simulation
{
    public class MovementTests
    {
        private static CollisionWorld Arena()
        {
            return new CollisionWorld(new[]
            {
                // 바닥
                new Aabb(new Vector3(-50f, -1f, -50f), new Vector3(50f, 0f, 50f)),
                // X = 5 의 벽
                new Aabb(new Vector3(5f, 0f, -50f), new Vector3(6f, 4f, 50f)),
            });
        }

        private static CollisionWorld FloorWithCeiling()
        {
            return new CollisionWorld(new[]
            {
                new Aabb(new Vector3(-50f, -1f, -50f), new Vector3(50f, 0f, 50f)),
                // 발밑에서 1.4m 위의 천장. 서 있는 키(1.8m)로는 못 선다.
                new Aabb(new Vector3(-50f, 1.4f, -50f), new Vector3(50f, 3f, 50f)),
            });
        }

        private static PlayerState Standing()
        {
            return PlayerState.Spawn(new Vector3(0f, 0f, 0f), 0f, 100);
        }

        private static PlayerState Run(PlayerState state, MoveIntent intent, CollisionWorld world, int ticks)
        {
            for (var tick = 0; tick < ticks; tick++)
            {
                state = PlayerMovement.Step(state, intent, world);
            }

            return state;
        }

        private static MoveIntent Idle()
        {
            return new MoveIntent(0f, 0f, 0f, 0f, ButtonFlags.None);
        }

        [Fact]
        public void 공중에서_출발하면_바닥에_착지한다()
        {
            var state = PlayerState.Spawn(new Vector3(0f, 5f, 0f), 0f, 100);

            state = Run(state, Idle(), Arena(), 60);

            Assert.True(state.IsGrounded);
            Assert.True(MathF.Abs(state.Position.Y) < 0.05f, $"Y = {state.Position.Y}");
            Assert.Equal(0f, state.Velocity.Y);
        }

        [Fact]
        public void 바닥에_서_있으면_아래로_가라앉지_않는다()
        {
            var state = Standing();
            state.Flags |= EntityFlags.OnGround;

            state = Run(state, Idle(), Arena(), 120);

            Assert.True(state.IsGrounded);
            Assert.True(MathF.Abs(state.Position.Y) < 0.01f, $"Y = {state.Position.Y}");
        }

        [Fact]
        public void 전진_입력은_이동_속도로_수렴한다()
        {
            var state = Standing();
            state.Flags |= EntityFlags.OnGround;

            var forward = new MoveIntent(0f, 1f, 0f, 0f, ButtonFlags.None);
            state = Run(state, forward, Arena(), 30);

            var horizontalSpeed = MathF.Sqrt((state.Velocity.X * state.Velocity.X) + (state.Velocity.Z * state.Velocity.Z));

            Assert.True(MathF.Abs(horizontalSpeed - SimConstants.MoveSpeed) < 0.01f, $"speed = {horizontalSpeed}");
            Assert.True(state.Position.Z > 3f, $"Z = {state.Position.Z}");
        }

        [Fact]
        public void 대각_입력이_축_입력보다_빠르지_않다()
        {
            var world = Arena();

            var straight = Standing();
            straight.Flags |= EntityFlags.OnGround;
            straight = Run(straight, new MoveIntent(0f, 1f, 0f, 0f, ButtonFlags.None), world, 30);

            var diagonal = Standing();
            diagonal.Flags |= EntityFlags.OnGround;
            diagonal = Run(diagonal, new MoveIntent(1f, 1f, 0f, 0f, ButtonFlags.None), world, 30);

            var straightSpeed = MathF.Sqrt((straight.Velocity.X * straight.Velocity.X) + (straight.Velocity.Z * straight.Velocity.Z));
            var diagonalSpeed = MathF.Sqrt((diagonal.Velocity.X * diagonal.Velocity.X) + (diagonal.Velocity.Z * diagonal.Velocity.Z));

            Assert.True(diagonalSpeed <= straightSpeed + 0.01f, $"{diagonalSpeed} vs {straightSpeed}");
        }

        [Fact]
        public void 요를_돌리면_전진_방향이_따라간다()
        {
            var world = Arena();

            var state = Standing();
            state.Flags |= EntityFlags.OnGround;

            // 요 90도. 전진이 +X 를 향해야 한다.
            var intent = new MoveIntent(0f, 1f, DeterministicMath.HalfPi, 0f, ButtonFlags.None);
            state = Run(state, intent, world, 20);

            Assert.True(state.Position.X > 2f, $"X = {state.Position.X}");
            Assert.True(MathF.Abs(state.Position.Z) < 0.2f, $"Z = {state.Position.Z}");
        }

        [Fact]
        public void 입력을_놓으면_정지한다()
        {
            var state = Standing();
            state.Flags |= EntityFlags.OnGround;

            state = Run(state, new MoveIntent(0f, 1f, 0f, 0f, ButtonFlags.None), Arena(), 30);
            state = Run(state, Idle(), Arena(), 30);

            Assert.Equal(0f, state.Velocity.X, 4);
            Assert.Equal(0f, state.Velocity.Z, 4);
        }

        [Fact]
        public void 점프는_올라갔다_내려온다()
        {
            var world = Arena();

            var state = Standing();
            state.Flags |= EntityFlags.OnGround;

            var jump = new MoveIntent(0f, 0f, 0f, 0f, ButtonFlags.Jump);
            state = PlayerMovement.Step(state, jump, world);

            Assert.False(state.IsGrounded);
            Assert.True(state.Velocity.Y > 0f);

            var peak = state.Position.Y;
            for (var tick = 0; tick < 60; tick++)
            {
                state = PlayerMovement.Step(state, Idle(), world);
                if (state.Position.Y > peak)
                {
                    peak = state.Position.Y;
                }
            }

            Assert.True(peak > 1f, $"peak = {peak}");
            Assert.True(state.IsGrounded);
            Assert.True(MathF.Abs(state.Position.Y) < 0.05f, $"Y = {state.Position.Y}");
        }

        [Fact]
        public void 공중에서는_점프할_수_없다()
        {
            var world = Arena();
            var state = PlayerState.Spawn(new Vector3(0f, 5f, 0f), 0f, 100);

            var jump = new MoveIntent(0f, 0f, 0f, 0f, ButtonFlags.Jump);
            state = PlayerMovement.Step(state, jump, world);

            Assert.True(state.Velocity.Y < 0f, $"Vy = {state.Velocity.Y}");
        }

        [Fact]
        public void 벽을_통과하지_않는다()
        {
            var state = Standing();
            state.Flags |= EntityFlags.OnGround;

            // 벽은 X = 5 에 있다. 충분히 오래 밀어붙인다.
            state = Run(state, new MoveIntent(1f, 0f, 0f, 0f, ButtonFlags.None), Arena(), 120);

            Assert.True(state.Position.X < 5f, $"X = {state.Position.X}");
            Assert.True(state.Position.X > 4f, $"벽까지 도달하지 못했다. X = {state.Position.X}");
        }

        [Fact]
        public void 앉으면_키가_줄고_속도가_느려진다()
        {
            var world = Arena();

            var state = Standing();
            state.Flags |= EntityFlags.OnGround;

            var crouchForward = new MoveIntent(0f, 1f, 0f, 0f, ButtonFlags.Crouch);
            state = Run(state, crouchForward, world, 40);

            Assert.True(state.IsCrouching);
            Assert.Equal(SimConstants.PlayerCrouchHeight, state.Height);

            var speed = MathF.Sqrt((state.Velocity.X * state.Velocity.X) + (state.Velocity.Z * state.Velocity.Z));
            var expected = SimConstants.MoveSpeed * SimConstants.CrouchMultiplier;

            Assert.True(MathF.Abs(speed - expected) < 0.01f, $"speed = {speed}, expected = {expected}");
        }

        [Fact]
        public void 천장_아래에서는_일어서지_않는다()
        {
            var world = FloorWithCeiling();

            var state = Standing();
            state.Flags |= EntityFlags.OnGround | EntityFlags.Crouching;

            // 앉기를 놓아도 머리 위 공간이 없으면 앉은 상태가 유지된다.
            state = Run(state, Idle(), world, 10);

            Assert.True(state.IsCrouching);
        }

        [Fact]
        public void 천장이_없으면_앉기를_놓으면_일어선다()
        {
            var world = Arena();

            var state = Standing();
            state.Flags |= EntityFlags.OnGround | EntityFlags.Crouching;

            state = Run(state, Idle(), world, 5);

            Assert.False(state.IsCrouching);
            Assert.Equal(SimConstants.PlayerHeight, state.Height);
        }

        [Fact]
        public void 낙하_속도는_종단_속도를_넘지_않는다()
        {
            var world = new CollisionWorld(new Aabb[0]);
            var state = PlayerState.Spawn(new Vector3(0f, 0f, 0f), 0f, 100);

            state = Run(state, Idle(), world, 600);

            Assert.True(state.Velocity.Y >= -SimConstants.TerminalVelocity, $"Vy = {state.Velocity.Y}");
            Assert.Equal(-SimConstants.TerminalVelocity, state.Velocity.Y, 3);
        }

        [Fact]
        public void 입력_프레임_오버로드는_역양자화를_거친다()
        {
            var world = Arena();

            var state = Standing();
            state.Flags |= EntityFlags.OnGround;

            var frame = new NV.Shared.Contracts.Messages.InputFrame(ButtonFlags.None, 0, 127, 0, 0);

            var viaFrame = PlayerMovement.Step(state, frame, world);
            var viaIntent = PlayerMovement.Step(state, MoveIntent.FromInput(frame), world);

            Assert.Equal(StateHash.Of(viaIntent), StateHash.Of(viaFrame));
        }
    }
}
