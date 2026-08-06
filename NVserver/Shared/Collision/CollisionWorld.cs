using System.Numerics;
using NV.Shared.Simulation;

namespace NV.Shared.Collision
{
    /// 맵 콜리전. 생성 후 변경되지 않으므로 클라이언트와 서버가 같은 입력에서
    /// 같은 결과를 낸다.
    ///
    /// **브로드페이즈가 없고, 재 보고 그대로 두기로 했다.** 예전 주석은 "박스 수십 개 규모"
    /// 라고 적혀 있었는데 `backrooms` 는 736박스다. 그래서 쟀다 — `PlayerMovement.Step` 이
    /// 0.0129ms 이므로 8명 한 틱이 0.103ms, 33.3ms 예산의 0.31% 다. 겹치지 않는 자리에서는
    /// 겹침 해소가 한 패스로 끝나고 `Aabb.Overlaps` 는 비교 여섯 번이라, 상한(반복 4회 ×
    /// 박스 수)과 실제 비용이 한 자리 이상 다르다.
    ///
    /// 넣지 않는 이유는 값이 작고 위험이 크기 때문이다. `Depenetrate` 는 박스를 **순차적으로**
    /// 밀어내므로 순회 순서가 결과를 바꾸고, 한 패스 중에 위치가 움직여 처음에는 겹치지 않던
    /// 박스와 겹칠 수 있다 — 후보를 미리 정하는 방식으로는 그 경우에 지금과 같은 결과를 보장할
    /// 수 없다. 어긋나면 클라이언트 예측과 비트가 갈리고, 증상은 "특정 위치에서만 캐릭터가 튐"
    /// 이다. 박스 수를 크게 늘릴 때 `docs/conventions.md` 의 기준선으로 다시 잰다.
    public sealed class CollisionWorld
    {
        private static readonly Aabb[] EmptyBoxes = new Aabb[0];

        private readonly Aabb[] _boxes;

        public CollisionWorld(Aabb[] boxes)
        {
            _boxes = boxes ?? EmptyBoxes;
        }

        public Aabb[] Boxes => _boxes;

        public int BoxCount => _boxes.Length;

        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RayHit hit)
        {
            return Raycaster.Raycast(origin, direction, maxDistance, _boxes, out hit);
        }

        /// 박스를 velocity * deltaTime 만큼 옮기고 충돌을 해소한다.
        /// 접촉면을 만나면 남은 이동과 속도를 그 평면에 투사해 미끄러진다.
        ///
        /// 수직면에 막혀 멈췄고 발이 땅에 붙어 있으면 **턱을 넘어 본다**
        /// (<see cref="SimConstants.StepHeight"/>). 그것이 없으면 계단의 챌면이 벽과
        /// 구별되지 않아 한 단씩 점프해야 올라간다.
        public MoveResult MoveBox(Vector3 center, Vector3 halfExtents, Vector3 velocity, float deltaTime)
        {
            var start = Depenetrate(center, halfExtents);
            var delta = DeterministicMath.Scale(velocity, deltaTime);

            var flat = Slide(start, halfExtents, delta, velocity);

            if (!BlockedByWall(flat, delta))
            {
                return flat;
            }

            // 공중에는 계단이 없다. 이 검사를 빼면 벽에 붙어 점프하는 것으로 벽을 오른다.
            if (!IsGrounded(start, halfExtents, SimConstants.GroundProbeDistance))
            {
                return flat;
            }

            return TryStepOver(start, halfExtents, delta, velocity, flat, out var stepped) ? stepped : flat;
        }

        /// 막힌 것이 **벽인가**. 바닥이나 천장에 닿아 멈춘 것은 넘을 턱이 아니다.
        ///
        /// 수평 이동이 거의 없으면 넘을 것도 없다 — 제자리에서 중력만 받는 틱마다
        /// 계단 시도를 돌리지 않기 위한 조건이기도 하다.
        private static bool BlockedByWall(in MoveResult result, Vector3 delta)
        {
            if (!result.Hit)
            {
                return false;
            }

            if (DeterministicMath.Abs(result.LastNormal.Y) >= SimConstants.GroundNormalY)
            {
                return false;
            }

            var horizontal = new Vector3(delta.X, 0f, delta.Z);

            return DeterministicMath.LengthSquared(horizontal) > DeterministicMath.Epsilon;
        }

        /// 올라가고, 가고, 내려놓는다. 셋 다 성공해야 채택한다.
        ///
        /// **내려놓는 거리는 올라간 만큼이다.** 더 내려가게 두면 턱을 넘은 것이 아니라
        /// 구덩이를 건너뛰는 것이 되고, 낙하가 이 함수 안에서 공짜로 일어난다.
        ///
        /// **더 나아간 경우에만 채택한다.** 벽에 붙어 걷는 동안 오르내리기를 반복하면
        /// 서 있는 높이가 틱마다 흔들리고, 그것은 클라이언트에서 떨림으로 보인다.
        private bool TryStepOver(
            Vector3 start,
            Vector3 halfExtents,
            Vector3 delta,
            Vector3 velocity,
            in MoveResult flat,
            out MoveResult stepped)
        {
            stepped = flat;

            var raised = SweepStraight(start, halfExtents, new Vector3(0f, SimConstants.StepHeight, 0f));
            var lift = raised.Y - start.Y;

            // 머리 위가 막혀 있다. 낮은 통로 안에서는 턱을 넘지 못하는 것이 맞다.
            if (lift <= SimConstants.SkinWidth)
            {
                return false;
            }

            var horizontal = new Vector3(delta.X, 0f, delta.Z);
            var moved = Slide(raised, halfExtents, horizontal, velocity);

            var landed = SweepStraight(moved.Center, halfExtents, new Vector3(0f, -lift, 0f));

            // 턱 위에 발을 딛지 못했으면 넘은 것이 아니다 — 난간 너머 허공이 그렇다.
            if (!IsGrounded(landed, halfExtents, SimConstants.GroundProbeDistance))
            {
                return false;
            }

            if (HorizontalDistanceSquared(start, landed) <= HorizontalDistanceSquared(start, flat.Center))
            {
                return false;
            }

            stepped = new MoveResult(landed, moved.Velocity, new Vector3(0f, 1f, 0f), moved.Hit, true);
            return true;
        }

        private static float HorizontalDistanceSquared(Vector3 from, Vector3 to)
        {
            var dx = to.X - from.X;
            var dz = to.Z - from.Z;

            return (dx * dx) + (dz * dz);
        }

        /// 미끄러지지 않고 닿을 때까지만 간다. 오르기·내려놓기가 쓴다 —
        /// 그 둘이 미끄러지면 턱 위가 아니라 옆으로 흘러간 자리에 놓인다.
        private Vector3 SweepStraight(Vector3 position, Vector3 halfExtents, Vector3 delta)
        {
            if (!SweepEarliest(position, halfExtents, delta, out var time, out var normal))
            {
                return DeterministicMath.Add(position, delta);
            }

            var contact = DeterministicMath.Add(position, DeterministicMath.Scale(delta, time));

            return DeterministicMath.Add(contact, DeterministicMath.Scale(normal, SimConstants.SkinWidth));
        }

        private MoveResult Slide(Vector3 position, Vector3 halfExtents, Vector3 move, Vector3 velocity)
        {
            var currentVelocity = velocity;
            var remaining = move;

            var hit = false;
            var grounded = false;
            var lastNormal = new Vector3(0f, 0f, 0f);

            for (var iteration = 0; iteration < SimConstants.MaxSlideIterations; iteration++)
            {
                if (DeterministicMath.LengthSquared(remaining) < DeterministicMath.Epsilon)
                {
                    remaining = new Vector3(0f, 0f, 0f);
                    break;
                }

                if (!SweepEarliest(position, halfExtents, remaining, out var time, out var normal))
                {
                    position = DeterministicMath.Add(position, remaining);
                    remaining = new Vector3(0f, 0f, 0f);
                    break;
                }

                hit = true;
                lastNormal = normal;

                if (normal.Y >= SimConstants.GroundNormalY)
                {
                    grounded = true;
                }

                // 접촉 지점까지 이동한 뒤 표면에서 살짝 띄운다.
                position = DeterministicMath.Add(position, DeterministicMath.Scale(remaining, time));
                position = DeterministicMath.Add(position, DeterministicMath.Scale(normal, SimConstants.SkinWidth));

                var leftover = DeterministicMath.Scale(remaining, 1f - time);
                remaining = DeterministicMath.ProjectOnPlane(leftover, normal);
                currentVelocity = DeterministicMath.ProjectOnPlane(currentVelocity, normal);
            }

            return new MoveResult(position, currentVelocity, lastNormal, hit, grounded);
        }

        /// 발밑 아래로 짧게 탐침해 착지 상태를 판정한다.
        public bool IsGrounded(Vector3 center, Vector3 halfExtents, float probeDistance)
        {
            var probe = new Vector3(0f, -probeDistance, 0f);

            if (!SweepEarliest(center, halfExtents, probe, out _, out var normal))
            {
                return false;
            }

            return normal.Y >= SimConstants.GroundNormalY;
        }

        /// 이미 박스와 겹쳐 있으면 관통이 가장 얕은 축으로 밀어낸다.
        /// 겹친 상태에서 스윕하면 tEnter 가 음수로 나와 이동이 통과해버린다.
        public Vector3 Depenetrate(Vector3 center, Vector3 halfExtents)
        {
            var position = center;

            for (var iteration = 0; iteration < SimConstants.MaxDepenetrationIterations; iteration++)
            {
                var resolved = true;

                for (var index = 0; index < _boxes.Length; index++)
                {
                    var moving = Aabb.FromCenter(position, halfExtents);
                    if (!moving.Overlaps(_boxes[index]))
                    {
                        continue;
                    }

                    position = PushOut(position, moving, _boxes[index]);
                    resolved = false;
                }

                if (resolved)
                {
                    break;
                }
            }

            return position;
        }

        private static Vector3 PushOut(Vector3 position, in Aabb moving, in Aabb obstacle)
        {
            var leftX = obstacle.Min.X - moving.Max.X;
            var rightX = obstacle.Max.X - moving.Min.X;
            var pushX = DeterministicMath.Abs(leftX) < DeterministicMath.Abs(rightX) ? leftX : rightX;

            var downY = obstacle.Min.Y - moving.Max.Y;
            var upY = obstacle.Max.Y - moving.Min.Y;
            var pushY = DeterministicMath.Abs(downY) < DeterministicMath.Abs(upY) ? downY : upY;

            var backZ = obstacle.Min.Z - moving.Max.Z;
            var frontZ = obstacle.Max.Z - moving.Min.Z;
            var pushZ = DeterministicMath.Abs(backZ) < DeterministicMath.Abs(frontZ) ? backZ : frontZ;

            var absX = DeterministicMath.Abs(pushX);
            var absY = DeterministicMath.Abs(pushY);
            var absZ = DeterministicMath.Abs(pushZ);

            if (absX <= absY && absX <= absZ)
            {
                return new Vector3(position.X + pushX + (pushX < 0f ? -SimConstants.SkinWidth : SimConstants.SkinWidth), position.Y, position.Z);
            }

            if (absY <= absZ)
            {
                return new Vector3(position.X, position.Y + pushY + (pushY < 0f ? -SimConstants.SkinWidth : SimConstants.SkinWidth), position.Z);
            }

            return new Vector3(position.X, position.Y, position.Z + pushZ + (pushZ < 0f ? -SimConstants.SkinWidth : SimConstants.SkinWidth));
        }

        /// 가장 먼저 닿는 장애물을 찾는다. time 은 delta 를 1 로 본 매개변수다.
        private bool SweepEarliest(
            Vector3 center,
            Vector3 halfExtents,
            Vector3 delta,
            out float time,
            out Vector3 normal)
        {
            time = 1f;
            normal = new Vector3(0f, 0f, 0f);

            var found = false;

            for (var index = 0; index < _boxes.Length; index++)
            {
                var expanded = _boxes[index].Expand(halfExtents);

                if (!Raycaster.RayAabb(center, delta, expanded, out var tEnter, out var tExit, out var hitNormal))
                {
                    continue;
                }

                // tEnter 가 음수면 시작 시점에 이미 겹쳐 있다. Depenetrate 가 처리할 몫이다.
                if (tEnter < 0f || tEnter > 1f || tExit < 0f)
                {
                    continue;
                }

                if (tEnter >= time)
                {
                    continue;
                }

                time = tEnter;
                normal = hitNormal;
                found = true;
            }

            return found;
        }
    }
}
