using System.Numerics;

namespace NV.Shared.Collision
{
    /// 축 정렬 박스. 맵 콜리전과 플레이어 충돌의 유일한 형태다.
    /// 물리 엔진을 쓰지 않으므로 이 구조체가 충돌의 밑바닥이다.
    public readonly struct Aabb
    {
        public Aabb(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        public Vector3 Min { get; }

        public Vector3 Max { get; }

        public static Aabb FromCenter(Vector3 center, Vector3 halfExtents)
        {
            return new Aabb(
                new Vector3(center.X - halfExtents.X, center.Y - halfExtents.Y, center.Z - halfExtents.Z),
                new Vector3(center.X + halfExtents.X, center.Y + halfExtents.Y, center.Z + halfExtents.Z));
        }

        public Vector3 Center =>
            new Vector3(
                (Min.X + Max.X) * 0.5f,
                (Min.Y + Max.Y) * 0.5f,
                (Min.Z + Max.Z) * 0.5f);

        public Vector3 HalfExtents =>
            new Vector3(
                (Max.X - Min.X) * 0.5f,
                (Max.Y - Min.Y) * 0.5f,
                (Max.Z - Min.Z) * 0.5f);

        public bool Overlaps(in Aabb other)
        {
            return Min.X < other.Max.X && Max.X > other.Min.X
                && Min.Y < other.Max.Y && Max.Y > other.Min.Y
                && Min.Z < other.Max.Z && Max.Z > other.Min.Z;
        }

        public bool Contains(Vector3 point)
        {
            return point.X >= Min.X && point.X <= Max.X
                && point.Y >= Min.Y && point.Y <= Max.Y
                && point.Z >= Min.Z && point.Z <= Max.Z;
        }

        /// 민코프스키 합. 이동하는 박스를 점으로 바꿔 레이 교차로 환원한다.
        public Aabb Expand(Vector3 halfExtents)
        {
            return new Aabb(
                new Vector3(Min.X - halfExtents.X, Min.Y - halfExtents.Y, Min.Z - halfExtents.Z),
                new Vector3(Max.X + halfExtents.X, Max.Y + halfExtents.Y, Max.Z + halfExtents.Z));
        }
    }
}
