using System.Numerics;
using NV.Shared.Simulation;

namespace NV.Shared.Collision
{
    /// 레이와 AABB 의 교차. 스윕도 이 함수로 환원된다.
    /// 이동하는 박스를 장애물에 민코프스키 합으로 더하면 중심점의 레이 교차가 된다.
    public static class Raycaster
    {
        /// direction 은 정규화하지 않는다. t 는 direction 길이를 1 로 보는 매개변수다.
        /// 스윕에서 direction 에 이동량을 그대로 넣으면 t 가 [0,1] 이 된다.
        ///
        /// 시작점이 박스 안이면 tEnter 가 음수로 나온다. 호출자가 겹침으로 판단해야 한다.
        public static bool RayAabb(
            Vector3 origin,
            Vector3 direction,
            in Aabb box,
            out float tEnter,
            out float tExit,
            out Vector3 normal)
        {
            tEnter = float.NegativeInfinity;
            tExit = float.PositiveInfinity;
            normal = new Vector3(0f, 0f, 0f);

            var enterAxis = -1;
            var enterSign = 0f;

            for (var axis = 0; axis < 3; axis++)
            {
                var originComponent = Component(origin, axis);
                var directionComponent = Component(direction, axis);
                var minComponent = Component(box.Min, axis);
                var maxComponent = Component(box.Max, axis);

                // 축 방향 이동이 없으면 나눗셈이 0 또는 NaN 을 만든다. 슬랩 포함 여부만 본다.
                if (DeterministicMath.Abs(directionComponent) < DeterministicMath.Epsilon)
                {
                    if (originComponent < minComponent || originComponent > maxComponent)
                    {
                        return false;
                    }

                    continue;
                }

                var inverse = 1f / directionComponent;
                var t1 = (minComponent - originComponent) * inverse;
                var t2 = (maxComponent - originComponent) * inverse;

                var sign = -1f;
                if (t1 > t2)
                {
                    var swap = t1;
                    t1 = t2;
                    t2 = swap;
                    sign = 1f;
                }

                if (t1 > tEnter)
                {
                    tEnter = t1;
                    enterAxis = axis;
                    enterSign = sign;
                }

                if (t2 < tExit)
                {
                    tExit = t2;
                }

                if (tEnter > tExit)
                {
                    return false;
                }
            }

            if (enterAxis >= 0)
            {
                normal = AxisNormal(enterAxis, enterSign);
            }

            return true;
        }

        /// 정규화된 방향과 최대 거리로 레이를 쏜다. 가장 가까운 교차를 반환한다.
        public static bool Raycast(
            Vector3 origin,
            Vector3 direction,
            float maxDistance,
            Aabb[] boxes,
            out RayHit hit)
        {
            hit = default;

            if (boxes == null)
            {
                return false;
            }

            var closest = maxDistance;
            var found = false;

            for (var index = 0; index < boxes.Length; index++)
            {
                if (!RayAabb(origin, direction, boxes[index], out var tEnter, out var tExit, out var normal))
                {
                    continue;
                }

                // 시작점이 박스 안이면 거리 0 접촉으로 본다.
                var distance = tEnter < 0f ? 0f : tEnter;

                if (tExit < 0f || distance > closest)
                {
                    continue;
                }

                closest = distance;
                found = true;

                hit = new RayHit(
                    index,
                    distance,
                    DeterministicMath.Add(origin, DeterministicMath.Scale(direction, distance)),
                    normal);
            }

            return found;
        }

        private static float Component(Vector3 value, int axis)
        {
            if (axis == 0)
            {
                return value.X;
            }

            return axis == 1 ? value.Y : value.Z;
        }

        private static Vector3 AxisNormal(int axis, float sign)
        {
            if (axis == 0)
            {
                return new Vector3(sign, 0f, 0f);
            }

            if (axis == 1)
            {
                return new Vector3(0f, sign, 0f);
            }

            return new Vector3(0f, 0f, sign);
        }
    }
}
