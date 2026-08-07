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
        /// 스키마 버전. 없으면 0 이고 그때는 1 로 읽는다 — `MapSchema` 를 본다.
        ///
        /// **해시에 넣지 않는다.** 넣으면 버전을 도입하는 이 커밋에서 기존 맵 전부의 해시가
        /// 바뀌어 재-export 를 돌려야 하는데, 그 재-export 는 아무 정보도 늘리지 않는다.
        public int Version { get; set; }

        public string Name { get; set; }

        public MapBox[] Boxes { get; set; }

        public MapSpawn[] Spawns { get; set; }

        /// 걸을 수 있는 곳의 격자. 없을 수 있다.
        ///
        /// 콜리전 박스만으로는 "여기 설 수 있는가" 를 답할 수 없어서 추가했다. 격자가
        /// 없는 맵 파일도 로드된다 — 이동 판정은 박스만으로 되고, 격자를 요구하는 것은
        /// 목표물 배치처럼 나중에 붙는 기능이다. 그쪽에서 없음을 확인하고 거절한다.
        public MapGridData Grid { get; set; }

        /// 이 파일이 어디서 나왔는가. 없을 수 있다 — 출처를 싣기 전에 만들어진 파일이다.
        /// **해시에 들어가지 않는다.** 이유는 `MapSourceInfo` 에 있다.
        public MapSourceInfo Source { get; set; }

        /// 사람에게 보여 줄 값(표시용 이름, 설명, 권장 인원). 없을 수 있다 — 스키마 1 파일이다.
        /// **해시에 들어가지 않는다.** 이유는 `MapMetaInfo` 에 있다.
        public MapMetaInfo Meta { get; set; }

        public bool HasGrid => Grid != null && Grid.Cells != null;

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
        ///
        /// **격자는 있을 때만 해시에 들어간다.** 없을 때 0 을 섞으면 격자를 도입하는
        /// 커밋에서 기존 맵 파일 전부의 해시가 바뀌어 export 를 다시 돌려야 하는데,
        /// 그 재-export 는 아무 정보도 늘리지 않는다. 반대로 격자가 **있으면 반드시**
        /// 넣는다 — 빼면 격자가 어긋난 채로 해시가 일치해, 증상이 "가끔 열쇠가 벽 안에
        /// 생김" 으로만 나타난다. 이동 판정은 격자를 쓰지 않으므로 그 불일치는
        /// 걸어 다니는 동안에는 아무 신호도 내지 않는다.
        ///
        /// **`Version`·`Source`·`Meta` 는 일부러 들어가지 않는다.** 이 값은 "클라이언트와 서버가
        /// 같은 지형을 보고 있는가" 를 답해야 하고, 스키마 버전과 export 시각은 지형이 아니다.
        /// 넣으면 재-export 마다 해시가 바뀌어 대조가 뜻을 잃는다. 반대로 **지형에 영향을 주는
        /// 필드를 새로 넣는다면 반드시 여기에도 넣어야 한다** — 빼면 지형이 다른데 해시가 맞고,
        /// 그것이 이 값이 막아야 하는 유일한 경우다.
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

            if (Grid != null)
            {
                hash = Grid.CombineInto(hash);
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
        /// 이 스폰이 누구의 것인가. 0 = 역할 무관, 1 = Seeker 전용, 2 = Runner 전용.
        ///
        /// 없으면 0 으로 읽힌다 — 그래서 이 필드를 도입해도 기존 맵 파일은 그대로 읽히고,
        /// 스폰은 애초에 해시 밖이므로 재-export 도 강제되지 않는다
        /// (`docs/map-generator-tool-plan.md` §8.4 의 계획이 이것이다).
        ///
        /// Seeker 전용 스폰이 없는 맵에서는 서버가 제단 착지점을 Seeker 시작점으로
        /// 파생시킨다 — 이 필드는 맵이 그 파생값을 덮어쓰고 싶을 때만 적는다.
        ///
        /// **2(Runner 전용)는 아직 선언일 뿐이다** — 판정은 "1 인가 아닌가" 만 보므로
        /// 지금은 0 과 같이 동작한다. Seeker 전용 스폰도 제단도 없는 열화 맵에서 Seeker
        /// 가 team 2 스폰에 설 수 있고, 그것을 막는 판정은 필요해질 때 넣는다.
        public int Team { get; set; }

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
