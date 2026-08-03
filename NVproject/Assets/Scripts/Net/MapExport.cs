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

            AttachGrid(data, source);

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
        private static void AttachGrid(MapData data, INetworkMapSource source)
        {
            var grid = source.BuildGrid();
            if (grid == null)
            {
                // 격자를 내놓지 않는 레벨이 있다. 매치 규칙을 돌리지 않는 개발용 맵이
                // 그렇고, 없으면 맵 해시에도 들어가지 않는다.
                data.Grid = null;
                return;
            }

            if (!grid.TryValidate(out var error))
            {
                Debug.LogError($"[NV] 레벨이 내놓은 격자가 잘못됐다: {error}. 격자를 싣지 않는다.");
                data.Grid = null;
                return;
            }

            MapGridBuilder.MarkFreeFloor(grid, data.ToCollisionWorld());

            data.Grid = grid;
        }

        /// 씬에서 콜리전을 내놓을 수 있는 레벨을 찾는다. 인터페이스로는 FindAnyObjectByType
        /// 을 쓸 수 없어 MonoBehaviour 를 훑는다. 한 번만 호출되는 경로다.
        public static INetworkMapSource FindInScene()
        {
            var candidates = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

            for (var index = 0; index < candidates.Length; index++)
            {
                if (candidates[index] is INetworkMapSource source)
                {
                    return source;
                }
            }

            return null;
        }
    }
}
