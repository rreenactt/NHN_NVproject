using System.Collections.Generic;
using NV.Shared.Collision;
using UnityEngine;

namespace NV.Client.Net
{
    /// 클라이언트가 만든 지형을 서버가 판정할 수 있는 형태로 옮긴다.
    ///
    /// 에디터가 이 결과를 JSON 으로 export 하고, 런타임에는 같은 함수로 해시를 계산해
    /// 서버가 로드한 맵과 대조한다. 두 경로가 같은 함수를 지나는 것이 요점이다 —
    /// export 와 검증이 서로 다른 계산을 하면 검증이 아무것도 잡지 못한다.
    public static class MapExport
    {
        private static INetworkMapSource _cachedSource;
        private static MapData _cachedData;

        public static MapData BuildMapData(INetworkMapSource source)
        {
            return BuildMapData(source, out _);
        }

        /// 런타임용. 같은 레벨에 대해 **한 번만** 만들고 그 뒤로는 같은 것을 돌려준다.
        ///
        /// **왜 필요한가.** `AttachGrid` 는 격자 셀마다 겹침 해소를 부르고, 겹침 해소는 박스
        /// 목록을 선형으로 훑는다(브로드페이즈가 없다). `backrooms` 는 2450셀 × 736박스이므로
        /// 상한이 수백만 회 AABB 검사다. export 에서 한 번이면 문제가 없는데, 런타임에도 두
        /// 곳이 이것을 부른다 — 접속 시 맵 해시 대조(`NetworkBootstrap.OnWelcome`)와 오프라인
        /// 매치 시작(`MatchManager.OfflineGrid`). 앞은 **접속하는 프레임**에 돈다.
        ///
        /// **에디터 export 는 이것을 쓰지 않는다.** 그쪽은 파일에 무엇을 쓸지 정하는 경로이고
        /// 결과를 스탬프로 고치므로, 캐시된 인스턴스를 넘겨주면 런타임이 보는 자료가 함께
        /// 바뀐다. 매번 새로 만드는 것이 맞고, 그 비용은 사람이 버튼을 누를 때 한 번이다.
        ///
        /// 한 칸짜리 캐시다. 씬에 레벨은 하나이고, 둘이면 서로 밀어내며 매번 다시 만든다 —
        /// 느려지지만 틀리지는 않는다.
        public static MapData BuildMapDataCached(INetworkMapSource source)
        {
            if (source == null)
            {
                return null;
            }

            if (ReferenceEquals(_cachedSource, source) && _cachedData != null)
            {
                return _cachedData;
            }

            _cachedData = BuildMapData(source, out _);
            _cachedSource = source;

            return _cachedData;
        }

        /// 지형이 다시 만들어졌다고 알린다. **레벨을 다시 만드는 쪽이 불러야 한다.**
        ///
        /// 캐시가 알아서 눈치챌 방법이 없다. 콜리전 목록은 같은 `List` 인스턴스를 비우고 다시
        /// 채우므로 참조도 그대로이고, 개수만 보는 것은 씨드가 바뀌어도 개수가 같을 수 있으니
        /// 검사가 아니다. 지형이 바뀐 것을 아는 것은 그것을 바꾼 쪽뿐이다.
        ///
        /// 도메인 리로드는 이 정적 필드를 지우므로 그 경로는 저절로 안전하다.
        public static void InvalidateCache()
        {
            _cachedSource = null;
            _cachedData = null;
        }

        /// 만들면서 알게 된 것을 함께 돌려준다.
        ///
        /// **런타임 경로와 export 경로가 같은 함수를 지나야 하는데, 잘못에 대한 반응은
        /// 서로 달라야 한다.** 런타임(해시 대조, 오프라인 배치)은 격자가 어긋나도 계속
        /// 돌아야 한다 — 이동 판정은 격자를 쓰지 않으므로 게임은 진행된다. export 는
        /// 반대로 멈춰야 한다: 어긋난 격자를 파일로 굳히면 서버가 그것을 그대로 신뢰한다.
        ///
        /// 그래서 판정을 여기서 하지 않고 보고만 하고, 무엇을 할지는 호출자가 정한다.
        public static MapData BuildMapData(INetworkMapSource source, out MapBuildReport report)
        {
            // 런타임에는 레벨 생성이 이미 채워 두었다. 에디터 export 는 지오메트리를
            // 만들지 않는 경로로 같은 목록을 다시 계산한다.
            var levelBoxes = source.CollisionBoxes;
            if (levelBoxes == null || levelBoxes.Count == 0)
            {
                levelBoxes = source.ComputeCollision();
            }

            var boxes = new List<Bounds>(levelBoxes.Count + 8);
            for (var index = 0; index < levelBoxes.Count; index++)
            {
                boxes.Add(levelBoxes[index]);
            }

            var volumes = AppendSceneVolumes(boxes, out var rejectedVolumes);

            var spawns = new List<(Vector3 position, float yaw)>(8);
            source.GetSpawns(spawns);

            var data = new MapData
            {
                Name = source.MapName,
                Boxes = new MapBox[boxes.Count],
                Spawns = new MapSpawn[spawns.Count],
            };

            for (var index = 0; index < boxes.Count; index++)
            {
                var min = boxes[index].min;
                var max = boxes[index].max;

                data.Boxes[index] = new MapBox
                {
                    MinX = min.x,
                    MinY = min.y,
                    MinZ = min.z,
                    MaxX = max.x,
                    MaxY = max.y,
                    MaxZ = max.z,
                };
            }

            for (var index = 0; index < spawns.Count; index++)
            {
                var spawn = spawns[index];

                data.Spawns[index] = new MapSpawn
                {
                    X = spawn.position.x,

                    // 서버의 위치는 발밑 기준이다. 바닥 슬래브의 윗면이 y = 0 이다.
                    Y = spawn.position.y,
                    Z = spawn.position.z,
                    Yaw = spawn.yaw,
                };
            }

            AttachGrid(data, source, out report);

            report.SceneVolumes = volumes;
            report.RejectedVolumes = rejectedVolumes;

            return data;
        }

        /// 씬에 손으로 놓은 `NVCollisionVolume` 을 박스 목록에 더한다.
        ///
        /// **격자를 붙이기 전에 불려야 한다.** `FreeFloor` 는 이 목록으로 판정하므로, 프랍이
        /// 차지한 셀이 배치 후보에서 빠지려면 그 프랍이 이미 목록에 있어야 한다.
        ///
        /// **순서를 고정한다.** `FindObjectsByType` 의 순서는 규정되지 않았고, 박스 목록의
        /// 순서는 맵 해시에 그대로 들어간다(`MapData.ComputeHash` 가 순서대로 섞는다). 정렬하지
        /// 않으면 같은 씬에서 export 한 파일과 런타임이 계산한 해시가 실행마다 달라지고, 증상은
        /// 재현되지 않는 맵 해시 불일치다.
        ///
        /// 회전한 볼륨은 **건너뛴다.** 그것이 서버와 일치하는 유일한 선택이다 — 서버는 export
        /// 된 목록만 아니까, 양쪽이 똑같이 빼면 판정이 갈리지 않는다. 사람에게는 export 가
        /// 거절로 알린다.
        private static int AppendSceneVolumes(List<Bounds> into, out List<string> rejected)
        {
            rejected = null;

            var volumes = Object.FindObjectsByType<NVCollisionVolume>(FindObjectsSortMode.None);
            if (volumes.Length == 0)
            {
                return 0;
            }

            var accepted = new List<Bounds>(volumes.Length);

            for (var index = 0; index < volumes.Length; index++)
            {
                var reason = volumes[index].DescribeRejection();

                if (reason != null)
                {
                    rejected = rejected ?? new List<string>();
                    rejected.Add($"{volumes[index].name}: {reason}");
                    continue;
                }

                if (volumes[index].TryGetWorldBounds(out var bounds))
                {
                    accepted.Add(bounds);
                }
            }

            accepted.Sort(CompareBounds);

            for (var index = 0; index < accepted.Count; index++)
            {
                into.Add(accepted[index]);
            }

            return accepted.Count;
        }

        /// 박스의 전순서. 같은 자리에 겹친 두 볼륨까지 가리도록 max 까지 본다.
        private static int CompareBounds(Bounds left, Bounds right)
        {
            var order = Compare(left.min.x, right.min.x);
            if (order != 0) return order;

            order = Compare(left.min.y, right.min.y);
            if (order != 0) return order;

            order = Compare(left.min.z, right.min.z);
            if (order != 0) return order;

            order = Compare(left.max.x, right.max.x);
            if (order != 0) return order;

            order = Compare(left.max.y, right.max.y);
            if (order != 0) return order;

            return Compare(left.max.z, right.max.z);
        }

        private static int Compare(float left, float right)
        {
            return left < right ? -1 : left > right ? 1 : 0;
        }

        /// 레벨의 격자를 싣고 `FreeFloor` 를 채운다.
        ///
        /// 레벨은 `Standable` 과 `StairLink` 만 준다. `FreeFloor` 는 여기서 — 방금 만든
        /// 콜리전 박스와 서버의 플레이어 박스로 — 계산한다. 그 플래그의 뜻이 "서버가
        /// 여기에 플레이어를 놓아도 밀려나지 않는다" 이므로, 판정을 `Shared` 에 두어야
        /// 서버의 기준과 갈리지 않는다.
        ///
        /// 순서가 중요하다. 박스가 `data` 에 들어간 **뒤에** 불려야 같은 지형으로
        /// 판정한다.
        private static void AttachGrid(MapData data, INetworkMapSource source, out MapBuildReport report)
        {
            var grid = source.BuildGrid();
            if (grid == null)
            {
                // 격자를 내놓지 않는 레벨이 있다. 매치 규칙을 돌리지 않는 개발용 맵이
                // 그렇고, 없으면 맵 해시에도 들어가지 않는다.
                data.Grid = null;
                report = MapBuildReport.WithoutGrid();
                return;
            }

            if (!grid.TryValidate(out var error))
            {
                Debug.LogError($"[NV] 레벨이 내놓은 격자가 잘못됐다: {error}. 격자를 싣지 않는다.");
                data.Grid = null;
                report = MapBuildReport.GridRejected(error);
                return;
            }

            // **반환값을 버리지 않는다.** 0 이면 격자와 콜리전이 서로 다른 좌표계를 말하고
            // 있다는 뜻이고(`MapGridBuilder.MarkFreeFloor`), 그때도 크기 검증과 맵 해시는
            // 전부 통과한다. export 쪽이 이것을 보고 멈춘다.
            var freeFloor = MapGridBuilder.MarkFreeFloor(grid, data.ToCollisionWorld());

            data.Grid = grid;
            report = MapBuildReport.WithGrid(freeFloor);
        }

        /// 씬에서 콜리전을 내놓을 수 있는 레벨을 **전부** 찾는다. 인터페이스로는
        /// FindAnyObjectByType 을 쓸 수 없어 MonoBehaviour 를 훑는다. 한 번만 호출되는
        /// 경로다.
        ///
        /// **하나만 돌려주지 않는다.** `FindObjectsSortMode.None` 은 순서를 규정하지 않으므로
        /// "처음 만난 것" 은 규정되지 않은 값이고, 이 저장소에는 실제로 `MapName` 이 같은
        /// 구현이 둘 있었다 — 한쪽은 격자를 만들고 한쪽은 `null` 을 돌려준다. 어느 쪽이
        /// 뽑히는지에 따라 격자 없는 맵 파일이 쓰이는데, 격자 없는 맵도 서버가 정상
        /// 로드하므로 증상은 "열쇠도 문도 없는 매치" 로만 나타난다.
        ///
        /// 그래서 목록을 돌려주고 몇 개인지를 호출자가 보게 한다. 둘 이상이면 고를 것이
        /// 아니라 씬이 잘못된 것이다.
        public static void FindAllInScene(List<INetworkMapSource> into)
        {
            if (into == null)
            {
                return;
            }

            var candidates = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

            for (var index = 0; index < candidates.Length; index++)
            {
                if (candidates[index] is INetworkMapSource source)
                {
                    into.Add(source);
                }
            }
        }

        /// 씬의 유일한 레벨. 없거나 둘 이상이면 <c>null</c> 이다.
        ///
        /// 런타임 경로(해시 대조, 오프라인 배치)가 쓴다. 씬이 잘못된 것을 사람에게 설명하는
        /// 것은 에디터 툴의 몫이므로 여기서는 값만 돌려주고, 둘 이상일 때 하나를 고르지
        /// 않는다 — 고르면 그것이 곧 규정되지 않은 순서에 기대는 일이다.
        public static INetworkMapSource FindInScene()
        {
            var found = new List<INetworkMapSource>(2);
            FindAllInScene(found);

            return found.Count == 1 ? found[0] : null;
        }
    }

    /// `BuildMapData` 가 만들면서 알게 된 것.
    ///
    /// 격자가 없는 것과 격자가 거절된 것을 구별한다. 앞은 정상이고 뒤는 사고다. 둘을 합치면
    /// export 가 "격자 없는 맵" 으로 조용히 파일을 쓴다.
    public struct MapBuildReport
    {
        /// 레벨이 격자를 내놓았는가. 내놓지 않는 것은 정상이다.
        public bool GridOffered { get; private set; }

        /// 격자가 거절된 이유. 거절되지 않았으면 <c>null</c>.
        public string GridError { get; private set; }

        /// `FreeFloor` 로 표시된 셀 수. 격자가 실렸을 때만 뜻이 있다.
        public int FreeFloorCells { get; private set; }

        /// 박스 목록에 더해진 씬 볼륨의 수.
        public int SceneVolumes { get; set; }

        /// 실을 수 없어 건너뛴 씬 볼륨과 그 이유. 없으면 <c>null</c>.
        ///
        /// **런타임은 그냥 건너뛰고 export 는 거절한다.** 건너뛰는 것이 서버와 일치하는
        /// 유일한 선택이고(서버는 export 된 목록만 안다), 그런 씬이 배포되지 않게 막는 것은
        /// export 의 몫이다.
        public List<string> RejectedVolumes { get; set; }

        public bool GridAttached => GridOffered && GridError == null;

        public static MapBuildReport WithoutGrid()
        {
            return new MapBuildReport { GridOffered = false, GridError = null, FreeFloorCells = 0 };
        }

        public static MapBuildReport GridRejected(string error)
        {
            return new MapBuildReport { GridOffered = true, GridError = error, FreeFloorCells = 0 };
        }

        public static MapBuildReport WithGrid(int freeFloorCells)
        {
            return new MapBuildReport
            {
                GridOffered = true,
                GridError = null,
                FreeFloorCells = freeFloorCells,
            };
        }
    }
}
