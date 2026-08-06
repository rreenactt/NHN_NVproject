using System;
using System.Collections.Generic;
using System.Numerics;
using NV.Shared.Collision;
using NV.Shared.Contracts.Enums;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Simulation
{
    /// 계단 오르기(`SimConstants.StepHeight`).
    ///
    /// **박스 스윕에는 계단이라는 개념이 없다.** 수직면을 만나면 그 평면으로 미끄러질 뿐이라,
    /// 턱을 넘는 규칙을 따로 두지 않으면 0.2m 짜리 계단 챌면이 벽과 똑같이 몸을 세운다. 서버가
    /// 위치를 정하므로 증상은 클라이언트에서 **올라가려다 끌려 내려오는 것**으로 보이고,
    /// 오프라인에서는 같은 계단이 멀쩡히 올라가진다 — `CharacterController` 가 자기
    /// `stepOffset` 으로 처리하기 때문이다.
    ///
    /// 그래서 이 파일이 지키는 것은 두 가지다: **올라가야 할 것은 올라가고, 올라가면 안 되는
    /// 것은 못 올라간다.** 뒤쪽이 없으면 계단 규칙이 곧 벽 타기 규칙이 된다.
    public class StairClimbTests
    {
        /// backrooms 계단 한 단의 높이. 3.2m 를 16단으로 나눈 값이다.
        private const float RealStepRise = 3.2f / 16f;

        [Fact]
        public void 계단을_점프하지_않고_올라간다()
        {
            var state = WalkInto(Steps(RealStepRise, count: 4), ticks: 120);

            Assert.True(state.IsGrounded, "계단참에서 착지 상태가 아니다.");
            Assert.True(
                state.Position.Y > RealStepRise * 3.5f,
                $"네 단을 걸었는데 Y = {state.Position.Y} 다 — 올라가지 못했다.");
        }

        /// 층 하나를 통째로. 한 단만 검사하면 "첫 단은 넘는데 두 번째에서 걸린다" 를 놓친다.
        [Fact]
        public void 한_층_전체를_걸어서_올라간다()
        {
            var state = WalkInto(Steps(RealStepRise, count: 16), ticks: 240);

            Assert.True(
                state.Position.Y > 3.1f,
                $"3.2m 계단을 다 오르지 못했다. Y = {state.Position.Y}");
        }

        /// **턱이 아닌 것은 넘지 않는다.** 이 검사가 없으면 계단 규칙이 벽 타기 규칙이 된다.
        /// 0.5m 는 제단 기단의 높이다.
        [Fact]
        public void 제단_높이의_턱은_올라가지_못한다()
        {
            var state = WalkInto(Ledge(0.5f), ticks: 60);

            Assert.True(state.Position.Y < 0.05f, $"0.5m 턱을 걸어서 올라갔다. Y = {state.Position.Y}");
        }

        [Fact]
        public void 벽은_여전히_막는다()
        {
            var state = WalkInto(Ledge(4f), ticks: 60);

            Assert.True(state.Position.Y < 0.05f, $"벽을 타고 올랐다. Y = {state.Position.Y}");
            Assert.True(state.Position.Z < 5f, $"벽을 통과했다. Z = {state.Position.Z}");
        }

        /// 공중에서는 턱을 넘지 않는다. 넘게 두면 벽에 붙어 점프하는 것으로 벽을 오른다.
        [Fact]
        public void 공중에서는_턱을_넘지_않는다()
        {
            var world = Ledge(0.25f);

            // 턱 옆에서 점프한 순간을 만든다. 발이 떠 있고 앞으로 밀고 있다.
            var state = PlayerState.Spawn(new Vector3(0f, 1.2f, 4.5f), 0f, 100);
            state.Velocity = new Vector3(0f, 2f, SimConstants.MoveSpeed);

            state = PlayerMovement.Step(state, Forward(), world);

            Assert.True(state.Position.Z < 4.9f, $"공중에서 턱 위로 건너갔다. Z = {state.Position.Z}");
        }

        // ==================================================== 세계 만들기

        /// 바닥 + z = 5 에서 시작하는 계단 한 벌 + 그 위의 계단참. 각 단은 바닥부터 자기
        /// 디딤면까지 꽉 찬 상자다 — 생성기가 내보내는 모양(`BackroomsGenerator.BuildStairs`)과
        /// 같다.
        ///
        /// **계단참이 없으면 안 된다.** 마지막 단을 지나면 몸이 그대로 걸어 나가 아래층
        /// 바닥까지 떨어지고, 그러면 다 오르고 나서 재는 높이가 0 이 된다.
        private static CollisionWorld Steps(float rise, int count)
        {
            var boxes = new List<Aabb> { Ground() };
            var top = 0f;
            var z = 5f;

            for (var index = 0; index < count; index++)
            {
                top = (index + 1) * rise;
                z = 5f + (index * 0.5f);

                boxes.Add(new Aabb(new Vector3(-5f, -1f, z), new Vector3(5f, top, z + 0.5f)));
            }

            boxes.Add(new Aabb(new Vector3(-5f, -1f, z + 0.5f), new Vector3(5f, top, 25f)));

            return new CollisionWorld(boxes.ToArray());
        }

        /// z = 5 부터 이어지는 높이 <paramref name="height"/> 의 턱 하나.
        private static CollisionWorld Ledge(float height)
        {
            return new CollisionWorld(new[]
            {
                Ground(),
                new Aabb(new Vector3(-5f, -1f, 5f), new Vector3(5f, height, 25f)),
            });
        }

        private static Aabb Ground()
        {
            return new Aabb(new Vector3(-50f, -1f, -50f), new Vector3(50f, 0f, 50f));
        }

        private static MoveIntent Forward()
        {
            return new MoveIntent(0f, 1f, 0f, 0f, ButtonFlags.None);
        }

        /// 원점에서 +Z 로 걸어 계단·턱에 부딪히게 한다.
        private static PlayerState WalkInto(CollisionWorld world, int ticks)
        {
            var state = PlayerState.Spawn(new Vector3(0f, 0f, 0f), 0f, 100);
            state.Flags |= EntityFlags.OnGround;

            for (var tick = 0; tick < ticks; tick++)
            {
                state = PlayerMovement.Step(state, Forward(), world);
            }

            return state;
        }
    }
}
