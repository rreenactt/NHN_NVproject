using System.Numerics;
using NV.Shared.Collision;
using NV.Shared.Contracts.Enums;

namespace NV.Shared.Simulation
{
    /// 이동 계산. 클라이언트와 서버가 같은 함수를 호출한다.
    ///
    /// 순수 함수다. 난수·시간·정적 가변 상태를 읽지 않는다.
    /// 이 규칙이 깨지면 리컨실리에이션 재적용 결과가 달라지고,
    /// 증상은 "가끔 캐릭터가 떨림" 으로만 나타난다.
    ///
    /// 판정(policy)은 여기 없다. 입력이 정당한지, 명중을 인정할지는 모듈이 정한다.
    public static class PlayerMovement
    {
        /// 요·피치가 가리키는 단위 방향.
        ///
        /// **여기 있는 이유는 요 규약이 여기 살기 때문이다.** `ApplyHorizontal` 이 전방을
        /// `(sin yaw, 0, cos yaw)` 로 두고 있고, 그 규약을 다른 파일에서 다시 세우면 한쪽을
        /// 고칠 때 다른 쪽이 남는다 — 증상은 총알이 옆으로 날아가는 것이다.
        ///
        /// 피치는 **음수가 위**다(클라이언트의 카메라 규약). Unity 의
        /// `Quaternion.Euler(pitch, yaw, 0) * Vector3.forward` 와 같은 값이 나온다.
        ///
        /// 사인·코사인은 결정적 다항식으로 계산한다 — `MathF` 는 libm 차이로 갈린다.
        public static Vector3 Forward(float yaw, float pitch)
        {
            DeterministicMath.SinCos(yaw, out var yawSin, out var yawCos);
            DeterministicMath.SinCos(pitch, out var pitchSin, out var pitchCos);

            return new Vector3(yawSin * pitchCos, -pitchSin, yawCos * pitchCos);
        }

        /// 한 틱 진행한다. deltaTime 을 받지 않는다. 고정 틱 델타만 쓴다.
        public static PlayerState Step(in PlayerState state, in MoveIntent intent, CollisionWorld world)
        {
            var next = state;

            next.Yaw = intent.Yaw;
            next.Pitch = intent.Pitch;

            var wasGrounded = state.IsGrounded;

            next.Flags = ResolveCrouch(state, intent, world, wasGrounded);

            var velocity = ApplyHorizontal(state.Velocity, intent, wasGrounded, IsCrouching(next.Flags));
            velocity = ApplyVertical(velocity, intent, wasGrounded, out var jumped);

            var halfExtents = HalfExtentsFor(next.Flags);
            var center = CenterFor(state.Position, next.Flags);

            var move = world.MoveBox(center, halfExtents, velocity, SimConstants.TickDelta);

            next.Position = FeetFrom(move.Center, next.Flags);
            next.Velocity = move.Velocity;

            var grounded = !jumped
                && (move.Grounded || world.IsGrounded(move.Center, halfExtents, SimConstants.GroundProbeDistance));

            if (grounded)
            {
                next.Flags |= EntityFlags.OnGround;

                // 바닥에 붙은 뒤에도 중력이 누적되면 다음 틱의 스윕이 매번 접촉을 만든다.
                if (next.Velocity.Y < 0f)
                {
                    next.Velocity = new Vector3(next.Velocity.X, 0f, next.Velocity.Z);
                }
            }
            else
            {
                next.Flags &= ~EntityFlags.OnGround;
            }

            return next;
        }

        /// 입력 프레임을 그대로 받는 편의 오버로드. 역양자화를 빠뜨리지 않게 한다.
        public static PlayerState Step(in PlayerState state, in Contracts.Messages.InputFrame frame, CollisionWorld world)
        {
            return Step(state, MoveIntent.FromInput(frame), world);
        }

        public static PlayerState Step(
            in PlayerState state,
            in Contracts.Messages.InputFrame frame,
            CollisionWorld world,
            bool seekerLegs)
        {
            return Step(state, MoveIntent.FromInput(frame, seekerLegs), world);
        }

        public static Vector3 HalfExtentsFor(EntityFlags flags)
        {
            var height = IsCrouching(flags) ? SimConstants.PlayerCrouchHeight : SimConstants.PlayerHeight;
            return new Vector3(SimConstants.PlayerRadius, height * 0.5f, SimConstants.PlayerRadius);
        }

        public static Vector3 CenterFor(Vector3 feet, EntityFlags flags)
        {
            var height = IsCrouching(flags) ? SimConstants.PlayerCrouchHeight : SimConstants.PlayerHeight;
            return new Vector3(feet.X, feet.Y + (height * 0.5f), feet.Z);
        }

        public static Vector3 FeetFrom(Vector3 center, EntityFlags flags)
        {
            var height = IsCrouching(flags) ? SimConstants.PlayerCrouchHeight : SimConstants.PlayerHeight;
            return new Vector3(center.X, center.Y - (height * 0.5f), center.Z);
        }

        private static bool IsCrouching(EntityFlags flags)
        {
            return (flags & EntityFlags.Crouching) != 0;
        }

        /// 앉기는 즉시 적용되지만 일어서기는 머리 위 공간이 있어야 한다.
        /// 이 검사가 없으면 천장 아래에서 일어설 때 지형에 파묻힌다.
        private static EntityFlags ResolveCrouch(
            in PlayerState state,
            in MoveIntent intent,
            CollisionWorld world,
            bool grounded)
        {
            var flags = state.Flags;

            if (intent.Crouch)
            {
                return flags | EntityFlags.Crouching;
            }

            if (!IsCrouching(flags))
            {
                return flags;
            }

            var standingFlags = flags & ~EntityFlags.Crouching;
            var standingCenter = CenterFor(state.Position, standingFlags);
            var standingHalfExtents = HalfExtentsFor(standingFlags);

            var blocked = world.Depenetrate(standingCenter, standingHalfExtents) != standingCenter;

            if (blocked && grounded)
            {
                return flags;
            }

            return standingFlags;
        }

        private static Vector3 ApplyHorizontal(Vector3 velocity, in MoveIntent intent, bool grounded, bool crouching)
        {
            // 요 기준 좌우·전후 축. 사인·코사인은 결정적 다항식으로 계산한다.
            DeterministicMath.SinCos(intent.Yaw, out var sin, out var cos);

            var wish = DeterministicMath.ClampToUnit(new Vector3(intent.MoveX, 0f, intent.MoveZ));

            var wishX = (wish.X * cos) + (wish.Z * sin);
            var wishZ = (wish.Z * cos) - (wish.X * sin);

            var speed = SimConstants.MoveSpeed;
            if (crouching)
            {
                speed *= SimConstants.CrouchMultiplier;
            }
            else if (intent.Sprint)
            {
                // 달릴 자격은 이미 걸러져 있다. 게이지가 빈 술래는 `Sprint` 비트가 지워진
                // 채로 여기 온다(`Room.StepPlayer`) — 자격 판정을 이동 계산 안에 넣으면
                // 게이지가 이동 함수의 입력이 되고, 그것은 결정성의 표면을 넓힌다.
                speed *= intent.SeekerLegs
                    ? SimConstants.SeekerSprintMultiplier
                    : SimConstants.SprintMultiplier;
            }

            var targetX = wishX * speed;
            var targetZ = wishZ * speed;

            var hasInput = DeterministicMath.LengthSquared(wish) > DeterministicMath.Epsilon;

            if (!grounded && !hasInput)
            {
                // 공중에서 입력이 없으면 감속하지 않는다. 점프 궤적이 입력에 따라 달라지면 안 된다.
                return velocity;
            }

            var acceleration = grounded ? SimConstants.GroundAcceleration : SimConstants.AirAcceleration;
            var maxDelta = acceleration * SimConstants.TickDelta;

            return new Vector3(
                DeterministicMath.Converge(velocity.X, targetX, maxDelta),
                velocity.Y,
                DeterministicMath.Converge(velocity.Z, targetZ, maxDelta));
        }

        private static Vector3 ApplyVertical(Vector3 velocity, in MoveIntent intent, bool grounded, out bool jumped)
        {
            jumped = false;
            var verticalVelocity = velocity.Y;

            if (grounded && intent.Jump)
            {
                verticalVelocity = SimConstants.JumpSpeed;
                jumped = true;
            }
            else
            {
                verticalVelocity -= SimConstants.Gravity * SimConstants.TickDelta;

                if (verticalVelocity < -SimConstants.TerminalVelocity)
                {
                    verticalVelocity = -SimConstants.TerminalVelocity;
                }
            }

            return new Vector3(velocity.X, verticalVelocity, velocity.Z);
        }
    }
}
