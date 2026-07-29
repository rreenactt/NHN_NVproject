using System.Numerics;

namespace NV.Shared.Collision
{
    public readonly struct RayHit
    {
        public RayHit(int boxIndex, float distance, Vector3 point, Vector3 normal)
        {
            BoxIndex = boxIndex;
            Distance = distance;
            Point = point;
            Normal = normal;
        }

        public int BoxIndex { get; }

        public float Distance { get; }

        public Vector3 Point { get; }

        public Vector3 Normal { get; }
    }
}
