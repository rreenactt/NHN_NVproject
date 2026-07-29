using System.Numerics;
using NV.Shared.Collision;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Simulation
{
    /// 이동 로직이 깨지면 증상이 "가끔 캐릭터가 떨림" 으로만 나타나 추적이 어렵다.
    /// 이 파일은 그 증상의 원인이 되는 성질을 직접 검사한다.
    public class DeterminismTests
    {
        private static CollisionWorld Arena()
        {
            return new CollisionWorld(new[]
            {
                new Aabb(new Vector3(-50f, -1f, -50f), new Vector3(50f, 0f, 50f)),
                new Aabb(new Vector3(5f, 0f, -50f), new Vector3(6f, 4f, 50f)),
                new Aabb(new Vector3(-3f, 0f, 2f), new Vector3(-1f, 1f, 4f)),
            });
        }

        /// 결정성 검사가 실제로 걷고, 부딪히고, 점프하는 경로를 지나게 만든다.
        /// 가만히 서 있는 입력만으로는 아무것도 검증하지 못한다.
        private static InputFrame[] InputSequence(int length)
        {
            var frames = new InputFrame[length];

            for (var index = 0; index < length; index++)
            {
                var buttons = ButtonFlags.None;
                if (index % 17 == 0)
                {
                    buttons |= ButtonFlags.Jump;
                }

                if (index % 23 < 6)
                {
                    buttons |= ButtonFlags.Crouch;
                }

                if (index % 31 < 10)
                {
                    buttons |= ButtonFlags.Sprint;
                }

                // 입력값은 반드시 양자화를 거친 값이어야 한다.
                var moveX = (sbyte)(((index * 37) % 255) - 127);
                var moveZ = (sbyte)(((index * 53) % 255) - 127);
                var yaw = (ushort)((index * 2113) % 65536);
                var pitch = (short)(((index * 811) % 60000) - 30000);

                frames[index] = new InputFrame(buttons, moveX, moveZ, yaw, pitch);
            }

            return frames;
        }

        private static PlayerState Simulate(PlayerState state, InputFrame[] frames, int from, int to, CollisionWorld world)
        {
            for (var index = from; index < to; index++)
            {
                state = PlayerMovement.Step(state, frames[index], world);
            }

            return state;
        }

        private static PlayerState Start()
        {
            var state = PlayerState.Spawn(new Vector3(0f, 0f, 0f), 0f, 100);
            state.Flags |= EntityFlags.OnGround;
            return state;
        }

        [Fact]
        public void 같은_입력_시퀀스는_같은_상태_해시를_만든다()
        {
            var frames = InputSequence(200);

            var first = Simulate(Start(), frames, 0, frames.Length, Arena());
            var second = Simulate(Start(), frames, 0, frames.Length, Arena());

            Assert.Equal(StateHash.Of(first), StateHash.Of(second));
        }

        [Fact]
        public void 반복_실행해도_해시가_변하지_않는다()
        {
            var frames = InputSequence(120);
            var expected = StateHash.Of(Simulate(Start(), frames, 0, frames.Length, Arena()));

            for (var repeat = 0; repeat < 20; repeat++)
            {
                Assert.Equal(expected, StateHash.Of(Simulate(Start(), frames, 0, frames.Length, Arena())));
            }
        }

        /// 리컨실리에이션의 핵심 성질이다.
        /// 중간 상태에서 남은 입력을 재적용한 결과가 통째로 돌린 결과와 같아야 한다.
        /// 이것이 깨지면 서버 보정이 도착할 때마다 클라이언트가 떨린다.
        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(33)]
        [InlineData(99)]
        [InlineData(150)]
        public void 중간_상태에서_재적용한_결과가_통째로_돌린_결과와_같다(int resumeAt)
        {
            var frames = InputSequence(200);
            var world = Arena();

            var continuous = Simulate(Start(), frames, 0, frames.Length, world);

            var checkpoint = Simulate(Start(), frames, 0, resumeAt, world);
            var replayed = Simulate(checkpoint, frames, resumeAt, frames.Length, world);

            Assert.Equal(StateHash.Of(continuous), StateHash.Of(replayed));
        }

        [Fact]
        public void 여러_지점에서_끊어_재적용해도_같다()
        {
            var frames = InputSequence(200);
            var world = Arena();

            var continuous = Simulate(Start(), frames, 0, frames.Length, world);

            var piecewise = Start();
            var cuts = new[] { 0, 3, 11, 40, 41, 87, 130, 199, 200 };
            for (var index = 1; index < cuts.Length; index++)
            {
                piecewise = Simulate(piecewise, frames, cuts[index - 1], cuts[index], world);
            }

            Assert.Equal(StateHash.Of(continuous), StateHash.Of(piecewise));
        }

        [Fact]
        public void 다른_입력은_다른_해시를_만든다()
        {
            var world = Arena();
            var frames = InputSequence(60);

            var baseline = Simulate(Start(), frames, 0, frames.Length, world);

            frames[30] = new InputFrame(ButtonFlags.None, 100, 100, 1000, 0);
            var altered = Simulate(Start(), frames, 0, frames.Length, world);

            Assert.NotEqual(StateHash.Of(baseline), StateHash.Of(altered));
        }

        [Fact]
        public void 해시는_부호만_다른_영을_같게_본다()
        {
            var positive = Start();
            positive.Velocity = new Vector3(0f, 0f, 0f);

            var negative = Start();
            negative.Velocity = new Vector3(-0f, -0f, -0f);

            Assert.Equal(StateHash.Of(positive), StateHash.Of(negative));
        }

        [Fact]
        public void 결정적_난수는_같은_좌표에서_같은_값을_낸다()
        {
            Assert.Equal(
                DeterministicRandom.NextUInt(1234u, 7u, 99u),
                DeterministicRandom.NextUInt(1234u, 7u, 99u));

            Assert.NotEqual(
                DeterministicRandom.NextUInt(1234u, 7u, 99u),
                DeterministicRandom.NextUInt(1235u, 7u, 99u));

            Assert.NotEqual(
                DeterministicRandom.NextUInt(1234u, 7u, 99u),
                DeterministicRandom.NextUInt(1234u, 8u, 99u));

            Assert.NotEqual(
                DeterministicRandom.NextUInt(1234u, 7u, 99u),
                DeterministicRandom.NextUInt(1234u, 7u, 100u));
        }

        [Fact]
        public void 결정적_난수의_단위_구간은_범위를_지킨다()
        {
            for (var tick = 0u; tick < 500u; tick++)
            {
                var value = DeterministicRandom.NextUnitFloat(tick, 3u, 1u);

                Assert.True(value >= 0f && value < 1f, $"tick {tick}: {value}");
            }
        }
    }
}
