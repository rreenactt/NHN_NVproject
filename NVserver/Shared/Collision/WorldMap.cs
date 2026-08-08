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

        // 생성 시점에 갈라 둔 캐시다. `MapData.Spawns` 를 생성 뒤에 바꾸면 이 목록과
        // 조용히 어긋난다 — 격자의 후보 목록과 같은 규약이다(`RoomFixture.Map` 참조).
        private readonly int[] _seekerSpawns;
        private readonly int[] _runnerSpawns;

        public WorldMap(MapData data)
        {
            Data = data;
            Name = data == null || data.Name == null ? string.Empty : data.Name;
            Collision = data == null ? new CollisionWorld(new Aabb[0]) : data.ToCollisionWorld();
            Hash = data == null ? 0u : data.ComputeHash();
            Grid = data != null && data.HasGrid ? new MapGrid(data.Grid) : null;

            SplitSpawnsByTeam(data, out _seekerSpawns, out _runnerSpawns);
        }

        /// 스폰을 팀별 인덱스 목록으로 갈라 둔다. 조회마다 훑지 않기 위한 캐시일 뿐,
        /// 어느 목록을 누구에게 줄지는 여전히 모듈의 판정이다.
        ///
        /// Runner 목록이 비면(모든 스폰이 Seeker 전용이면) 전체 목록으로 폴백한다 —
        /// 그런 파일은 검증이 거절하지만, 조회가 원점을 돌려주는 것보다는 낫다.
        private static void SplitSpawnsByTeam(MapData data, out int[] seekers, out int[] runners)
        {
            var spawns = data == null ? null : data.Spawns;

            if (spawns == null || spawns.Length == 0)
            {
                seekers = new int[0];
                runners = new int[0];
                return;
            }

            var seekerCount = 0;
            for (var index = 0; index < spawns.Length; index++)
            {
                if (spawns[index].Team == SeekerTeam)
                {
                    seekerCount++;
                }
            }

            seekers = new int[seekerCount];
            var runnerCount = spawns.Length - seekerCount;
            runners = new int[runnerCount == 0 ? spawns.Length : runnerCount];

            var seekerAt = 0;
            var runnerAt = 0;
            for (var index = 0; index < spawns.Length; index++)
            {
                if (spawns[index].Team == SeekerTeam)
                {
                    seekers[seekerAt++] = index;
                }

                if (spawns[index].Team != SeekerTeam || runnerCount == 0)
                {
                    runners[runnerAt++] = index;
                }
            }
        }

        /// `MapSpawn.Team` 의 Seeker 전용 값. 0 = 역할 무관, 2 = Runner 전용.
        private const int SeekerTeam = 1;

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

        /// Seeker 전용(team 1) 스폰의 개수. 0 이면 이 맵은 Seeker 시작점을 적지 않았고,
        /// 모듈이 파생값(제단 착지점)으로 판정한다.
        public int SeekerSpawnCount => _seekerSpawns.Length;

        /// Runner 가 설 수 있는 스폰(team ≠ 1)의 개수. **정원과 비교해야 하는 값이다** —
        /// 이보다 참가자가 많으면 조회가 감아서 두 사람이 같은 자리에 겹친다. 정원은
        /// 모듈의 것이므로 비교도 모듈(기동 로그)이 한다.
        public int RunnerSpawnCount => _runnerSpawns.Length;

        public Vector3 SeekerSpawnPosition(int index)
        {
            if (_seekerSpawns.Length == 0)
            {
                return FallbackSpawn;
            }

            return Data.Spawns[_seekerSpawns[Wrap(index, _seekerSpawns.Length)]].ToPosition();
        }

        public float SeekerSpawnYaw(int index)
        {
            if (_seekerSpawns.Length == 0)
            {
                return 0f;
            }

            return Data.Spawns[_seekerSpawns[Wrap(index, _seekerSpawns.Length)]].Yaw;
        }

        /// Runner 가 설 수 있는 스폰(team ≠ 1) 조회. Seeker 전용 스폰이 없는 맵에서는
        /// 전체 목록과 같으므로, team 을 적지 않은 기존 맵의 동작이 바뀌지 않는다.
        public Vector3 RunnerSpawnPosition(int index)
        {
            if (_runnerSpawns.Length == 0)
            {
                return FallbackSpawn;
            }

            return Data.Spawns[_runnerSpawns[Wrap(index, _runnerSpawns.Length)]].ToPosition();
        }

        public float RunnerSpawnYaw(int index)
        {
            if (_runnerSpawns.Length == 0)
            {
                return 0f;
            }

            return Data.Spawns[_runnerSpawns[Wrap(index, _runnerSpawns.Length)]].Yaw;
        }

        private static int Wrap(int index, int count)
        {
            var wrapped = index % count;
            return wrapped < 0 ? wrapped + count : wrapped;
        }
    }
}
