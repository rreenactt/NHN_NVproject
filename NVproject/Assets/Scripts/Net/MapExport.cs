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
        public static MapData BuildMapData(INetworkMapSource source)
        {
            return BuildMapData(source, out _);
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
            var boxes = source.CollisionBoxes;
            if (boxes == null || boxes.Count == 0)
            {
                boxes = source.ComputeCollision();
            }

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

            return data;
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
