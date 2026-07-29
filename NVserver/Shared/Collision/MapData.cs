using System.Numerics;
using NV.Shared.Simulation;

namespace NV.Shared.Collision
{
    /// Unity 에서 export 한 맵 콜리전의 스키마.
    ///
    /// System.Text.Json 은 NuGet 이라 Shared 에서 어트리뷰트를 붙일 수 없다.
    /// 명명 규칙은 양쪽 직렬화 설정에서 맞춘다.
    ///
    /// Vector3 를 직렬화 대상으로 노출하지 않는다. X·Y·Z 가 프로퍼티가 아니라 필드라
    /// 기본 설정의 System.Text.Json 이 빈 객체로 직렬화한다. 증상이 "맵이 통째로
    /// 사라짐" 으로만 나타나 추적이 어렵다.
    public sealed class MapData
    {
        public string Name { get; set; }

        public MapBox[] Boxes { get; set; }

        public MapSpawn[] Spawns { get; set; }

        public Aabb[] ToAabbArray()
        {
            if (Boxes == null)
            {
                return new Aabb[0];
            }

            var result = new Aabb[Boxes.Length];
            for (var index = 0; index < Boxes.Length; index++)
            {
                result[index] = Boxes[index].ToAabb();
            }

            return result;
        }

        public CollisionWorld ToCollisionWorld()
        {
            return new CollisionWorld(ToAabbArray());
        }

        /// 클라이언트와 서버가 같은 맵을 보고 있는지 확인하는 값.
        /// Welcome 에 실어 보내고 클라이언트가 자기 계산값과 비교한다.
        /// 같은 코드가 양쪽에서 돌아야 하므로 여기(Shared)에 있어야 한다.
        public uint ComputeHash()
        {
            var hash = StateHash.Seed;

            hash = StateHash.Combine(hash, Name ?? string.Empty);

            if (Boxes != null)
            {
                hash = StateHash.Combine(hash, (uint)Boxes.Length);

                for (var index = 0; index < Boxes.Length; index++)
                {
                    var box = Boxes[index];
                    hash = StateHash.Combine(hash, box.MinX);
                    hash = StateHash.Combine(hash, box.MinY);
                    hash = StateHash.Combine(hash, box.MinZ);
                    hash = StateHash.Combine(hash, box.MaxX);
                    hash = StateHash.Combine(hash, box.MaxY);
                    hash = StateHash.Combine(hash, box.MaxZ);
                }
            }

            return hash;
        }
    }

    public sealed class MapBox
    {
        public float MinX { get; set; }

        public float MinY { get; set; }

        public float MinZ { get; set; }

        public float MaxX { get; set; }

        public float MaxY { get; set; }

        public float MaxZ { get; set; }

        public Aabb ToAabb()
        {
            return new Aabb(new Vector3(MinX, MinY, MinZ), new Vector3(MaxX, MaxY, MaxZ));
        }
    }

    public sealed class MapSpawn
    {
        public float X { get; set; }

        public float Y { get; set; }

        public float Z { get; set; }

        public float Yaw { get; set; }

        public Vector3 ToPosition()
        {
            return new Vector3(X, Y, Z);
        }
    }
}
