using System.Numerics;

namespace NV.Shared.Collision
{
    /// 로드된 맵. 콜리전 월드와 해시를 함께 들고 다닌다.
    ///
    /// 클라이언트도 예측을 위해 같은 맵을 로드하고 같은 해시를 계산한다.
    /// 해시가 다르면 서로 다른 지형에서 시뮬레이션하고 있다는 뜻이며,
    /// 증상은 "특정 위치에서만 캐릭터가 튐" 으로 나타난다.
    public sealed class WorldMap
    {
        private static readonly Vector3 FallbackSpawn = new Vector3(0f, 0f, 0f);

        public WorldMap(MapData data)
        {
            Data = data;
            Name = data == null || data.Name == null ? string.Empty : data.Name;
            Collision = data == null ? new CollisionWorld(new Aabb[0]) : data.ToCollisionWorld();
            Hash = data == null ? 0u : data.ComputeHash();
            Grid = data != null && data.HasGrid ? new MapGrid(data.Grid) : null;
        }

        public MapData Data { get; }

        public string Name { get; }

        public CollisionWorld Collision { get; }

        public uint Hash { get; }

        /// 걸을 수 있는 곳의 격자에 대한 질의. **격자가 없는 맵에서는 `null` 이다.**
        ///
        /// 이동 판정은 격자를 쓰지 않으므로 격자 없는 맵도 정상으로 플레이된다. 격자를
        /// 요구하는 것은 목표물 배치처럼 나중에 붙는 기능이고, 그쪽에서 `null` 을 보고
        /// 거절해야 한다 — 조용히 원점을 쓰면 목표물이 전부 (0,0,0) 에 생긴다.
        public MapGrid Grid { get; }

        public bool HasGrid => Grid != null;

        public int SpawnCount => Data == null || Data.Spawns == null ? 0 : Data.Spawns.Length;

        /// 스폰 지점 선택. 인덱스를 개수로 감아 항상 유효한 값을 돌려준다.
        /// 어느 지점을 고를지는 모듈의 판정이고, 여기는 조회만 한다.
        public Vector3 SpawnPosition(int index)
        {
            var count = SpawnCount;
            if (count == 0)
            {
                return FallbackSpawn;
            }

            return Data.Spawns[Wrap(index, count)].ToPosition();
        }

        public float SpawnYaw(int index)
        {
            var count = SpawnCount;
            if (count == 0)
            {
                return 0f;
            }

            return Data.Spawns[Wrap(index, count)].Yaw;
        }

        private static int Wrap(int index, int count)
        {
            var wrapped = index % count;
            return wrapped < 0 ? wrapped + count : wrapped;
        }
    }
}
