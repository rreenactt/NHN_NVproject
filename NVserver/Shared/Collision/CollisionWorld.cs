using System.Numerics;
using NV.Shared.Simulation;

namespace NV.Shared.Collision
{
    /// 맵 콜리전. 생성 후 변경되지 않으므로 클라이언트와 서버가 같은 입력에서
    /// 같은 결과를 낸다. 브로드페이즈가 없다 — 맵 1개, 박스 수십 개 규모다.
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
        public MoveResult MoveBox(Vector3 center, Vector3 halfExtents, Vector3 velocity, float deltaTime)
        {
            var position = Depenetrate(center, halfExtents);
            var currentVelocity = velocity;
            var remaining = DeterministicMath.Scale(velocity, deltaTime);

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
