using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NV.Client.Net;
using NV.Shared.Collision;
using UnityEditor;
using UnityEngine;

namespace NV.Client.EditorTools
{
    /// 열린 씬의 레벨 콜리전을 서버가 읽는 JSON 으로 export 한다.
    ///
    /// 서버는 물리 엔진을 쓰지 않고 이 박스 목록으로 이동을 판정한다. 레벨이 코드로
    /// 생성되므로 export 가 유일한 전달 경로다. 씨드나 격자 수치를 바꾸면 다시 돌려야
    /// 하고, 잊으면 접속 직후 콘솔에 맵 해시 불일치가 뜬다.
    ///
    /// 파일명은 맵 이름에서 나온다 — Backrooms 는 `backrooms.json`, 테스트 룸은
    /// `test-room.json`. 서버의 `Game:MapPath` 가 어느 쪽을 가리키는지가 곧 어느 맵으로
    /// 판정하는지다.
    ///
    /// System.Text.Json 을 쓰지 않는다. Shared 는 NuGet 참조를 갖지 않고 Unity 에도
    /// 그 어셈블리가 없다. 필드가 여섯 개인 박스 목록이라 직접 쓰는 편이 싸다.
    /// 부동소수점은 왕복 보존 형식("R")으로 쓴다 — 자릿수를 줄이면 서버가 파싱한 값이
    /// 클라이언트의 값과 비트가 달라지고, 맵 해시가 이유 없이 불일치한다.
    public static class MapCollisionExporter
    {
        /// 저장소 배치는 NHN_NVproject/{NVproject,NVserver} 다.
        private const string RelativeOutputDirectory = "../../NVserver/MapData";

        [MenuItem("Tools/NV/Map/Export Map Collision", priority = 60)]
        public static void Export()
        {
            if (!TryFindSource(out var map))
            {
                return;
            }

            // **재현되지 않는 레벨은 export 하지 않는다.** 여기서 거절하지 않으면 파일과
            // 다음 실행의 지형이 갈리고, 그 사실을 알려주는 신호는 접속할 때마다의 맵 해시
            // 불일치뿐이다. 씨드에 원인이 있다는 말은 어디에도 나오지 않는다.
            var blocker = map.DescribeExportBlocker();
            if (blocker != null)
            {
                Refuse($"이 레벨은 지금 export 할 수 없다.\n\n{blocker}");
                return;
            }

            // Play 중에도 막지 않는다 — 런타임 목록(`CollisionBoxes`)은 실제로 플레이하고
            // 있는 지형이므로 export 대상으로 옳다. 다만 어느 목록을 썼는지는 남긴다.
            if (Application.isPlaying)
            {
                Debug.LogWarning(
                    "[NV] Play 모드에서 export 한다. 지오메트리를 다시 계산하지 않고 지금 씬에 " +
                    "만들어져 있는 콜리전 목록을 그대로 쓴다.");
            }

            if (!TryResolveOutputDirectory(out var directory, out var pathError))
            {
                Refuse(pathError);
                return;
            }

            var data = MapExport.BuildMapData(map, out var report);

            if (!TryAcceptGrid(report, out var gridError))
            {
                Refuse(gridError);
                return;
            }

            var path = Path.Combine(directory, data.Name + ".json");
            File.WriteAllText(path, Serialize(data), new UTF8Encoding(false));

            var grid = data.Grid == null
                ? "격자 없음"
                : $"격자 {data.Grid.Floors}층 {data.Grid.Width}×{data.Grid.Depth}" +
                  $" (설 수 있는 셀 {CountFlag(data.Grid, MapCellFlags.Standable)}개," +
                  $" 몸이 들어가는 셀 {CountFlag(data.Grid, MapCellFlags.FreeFloor)}개)";

            Debug.Log(
                $"[NV] 맵 콜리전 export 완료: {path}\n" +
                $"박스 {data.Boxes.Length}개, 스폰 {data.Spawns.Length}개, {grid}, 해시 {data.ComputeHash():X8}");
        }

        /// 씬의 레벨을 찾는다. **둘 이상이면 하나를 고르지 않고 거절한다.**
        ///
        /// 씬 스캔 순서는 규정되지 않았으므로 "처음 만난 것" 을 쓰는 것은 어느 파일이 쓰일지를
        /// 운에 맡기는 일이다. 특히 `MapName` 이 같은 둘이 있으면 두 export 가 같은 파일을
        /// 두고 서로 다른 내용을 쓰게 되고, 한쪽이 격자를 내놓지 않으면 그 차이가 맵 파일에서
        /// 사라진 격자로만 나타난다.
        private static bool TryFindSource(out INetworkMapSource map)
        {
            var found = new List<INetworkMapSource>(2);
            MapExport.FindAllInScene(found);

            map = null;

            if (found.Count == 0)
            {
                Refuse(
                    "씬에서 INetworkMapSource 를 구현한 레벨을 찾지 못했다.\n" +
                    "SampleScene(Backrooms) 이나 MultiplayerTest(테스트 룸) 를 열고 다시 실행한다.");
                return false;
            }

            if (found.Count > 1)
            {
                var list = new StringBuilder();
                var names = new List<string>(found.Count);

                for (var index = 0; index < found.Count; index++)
                {
                    var behaviour = found[index] as MonoBehaviour;
                    var where = behaviour == null ? "(MonoBehaviour 가 아니다)" : behaviour.name;
                    var type = found[index].GetType().Name;

                    list.Append($"\n  · {where} / {type} → \"{found[index].MapName}\"");
                    names.Add(found[index].MapName);
                }

                var duplicate = FirstDuplicate(names);
                if (duplicate != null)
                {
                    list.Append($"\n\n그중 \"{duplicate}\" 이 둘 이상이다. 같은 파일을 두고 서로 다른 " +
                                "내용을 쓰게 되므로, export 하려는 하나만 씬에 남긴다.");
                }

                Refuse($"씬에 레벨이 {found.Count}개 있다. 어느 것을 export 할지 알 수 없다.{list}");
                return false;
            }

            map = found[0];
            return true;
        }

        private static string FirstDuplicate(List<string> names)
        {
            for (var outer = 0; outer < names.Count; outer++)
            {
                for (var inner = outer + 1; inner < names.Count; inner++)
                {
                    if (string.Equals(names[outer], names[inner], StringComparison.Ordinal))
                    {
                        return names[outer];
                    }
                }
            }

            return null;
        }

        /// 격자가 실릴 만한 상태인가.
        ///
        /// 격자를 내놓지 않는 레벨은 통과시킨다 — `test-room` 이 그렇고 그것이 정상이다.
        /// 거절하는 두 경우는 **격자를 내놓았는데 실리지 않은 것**과 **실렸는데 몸이 들어가는
        /// 셀이 하나도 없는 것**이다. 뒤쪽은 격자와 콜리전이 서로 다른 좌표계를 말하고 있다는
        /// 뜻이고, 그때도 크기 검증·맵 해시·서버 기동은 전부 통과한다. 잘못은 한참 뒤
        /// "열쇠가 벽 안에 생김" 으로만 드러난다.
        private static bool TryAcceptGrid(MapBuildReport report, out string error)
        {
            if (report.GridOffered && report.GridError != null)
            {
                error = "레벨이 내놓은 격자가 잘못됐다.\n\n" + report.GridError +
                        "\n\n격자 없이 쓰면 서버가 그것을 정상으로 받아들이고, 매치에 열쇠도 문도 " +
                        "생기지 않는다. 파일을 쓰지 않았다.";
                return false;
            }

            if (report.GridAttached && report.FreeFloorCells == 0)
            {
                error = "격자에 몸이 들어가는 셀(FreeFloor)이 하나도 없다.\n\n" +
                        "격자와 콜리전 박스가 서로 다른 좌표계를 말하고 있다는 뜻이다 — 원점이나 " +
                        "셀 크기, CellIndex 의 축 순서를 확인한다. 이 상태로 쓴 파일은 크기 검증과 " +
                        "맵 해시를 모두 통과하고, 잘못은 목표물 배치 단계에서야 드러난다. " +
                        "파일을 쓰지 않았다.";
                return false;
            }

            error = null;
            return true;
        }

        /// 출력 폴더를 정하고 **그것이 정말 이 저장소의 서버인지 확인한다.**
        ///
        /// 경로는 저장소 배치(`NHN_NVproject/{NVproject,NVserver}`)를 가정한 상대 경로다.
        /// 배치가 다르면 예전 코드는 `Directory.CreateDirectory` 로 엉뚱한 곳에 폴더를 만들고
        /// 맵을 거기 썼다 — export 는 성공했다고 말하고 서버는 옛 파일로 계속 판정한다.
        /// 서버의 설정 파일이 옆에 있는지 보는 것이 가장 싼 확인이다.
        private static bool TryResolveOutputDirectory(out string directory, out string error)
        {
            directory = Path.GetFullPath(Path.Combine(Application.dataPath, RelativeOutputDirectory));

            var settings = Path.GetFullPath(Path.Combine(directory, "..", "Api", "appsettings.json"));

            if (!File.Exists(settings))
            {
                error = $"출력 폴더가 이 저장소의 서버가 아니다.\n\n{directory}\n\n" +
                        $"옆에 서버 설정({settings})이 없다. NVproject 와 NVserver 가 같은 " +
                        "저장소에 나란히 있는지 확인한다. 폴더를 새로 만들지 않았다.";
                return false;
            }

            Directory.CreateDirectory(directory);

            error = null;
            return true;
        }

        private static void Refuse(string message)
        {
            Debug.LogError("[NV] 맵 export 를 하지 않았다. " + message);
            EditorUtility.DisplayDialog("NV — 맵 export 거절", message, "확인");
        }

        private static string Serialize(MapData data)
        {
            var text = new StringBuilder(data.Boxes.Length * 96);

            text.Append("{\n  \"name\": \"").Append(data.Name).Append("\",\n  \"boxes\": [\n");

            for (var index = 0; index < data.Boxes.Length; index++)
            {
                var box = data.Boxes[index];
                text.Append("    { \"minX\": ").Append(F(box.MinX))
                    .Append(", \"minY\": ").Append(F(box.MinY))
                    .Append(", \"minZ\": ").Append(F(box.MinZ))
                    .Append(", \"maxX\": ").Append(F(box.MaxX))
                    .Append(", \"maxY\": ").Append(F(box.MaxY))
                    .Append(", \"maxZ\": ").Append(F(box.MaxZ))
                    .Append(" }");

                if (index < data.Boxes.Length - 1) text.Append(',');
                text.Append('\n');
            }

            text.Append("  ],\n  \"spawns\": [\n");

            for (var index = 0; index < data.Spawns.Length; index++)
            {
                var spawn = data.Spawns[index];
                text.Append("    { \"x\": ").Append(F(spawn.X))
                    .Append(", \"y\": ").Append(F(spawn.Y))
                    .Append(", \"z\": ").Append(F(spawn.Z))
                    .Append(", \"yaw\": ").Append(F(spawn.Yaw))
                    .Append(" }");

                if (index < data.Spawns.Length - 1) text.Append(',');
                text.Append('\n');
            }

            text.Append("  ]");

            AppendGrid(text, data.Grid);

            text.Append("\n}\n");
            return text.ToString();
        }

        /// 격자를 쓴다. 없으면 아무것도 쓰지 않는다 — 필드가 없으면 서버가 `null` 로
        /// 읽고, 그것이 "격자를 내놓지 않는 레벨" 의 정상 표현이다.
        ///
        /// `cells` 는 base64 다. System.Text.Json 이 `byte[]` 를 그렇게 읽으므로 서버는
        /// 파싱 코드를 한 줄도 쓰지 않는다. 2층 35×35 = 2450 셀이 한 줄에 들어가고,
        /// 숫자 배열로 쓰면 같은 정보가 4배 넘게 커진다.
        private static void AppendGrid(StringBuilder text, MapGridData grid)
        {
            if (grid == null) return;

            text.Append(",\n  \"grid\": {\n")
                .Append("    \"floors\": ").Append(grid.Floors).Append(",\n")
                .Append("    \"width\": ").Append(grid.Width).Append(",\n")
                .Append("    \"depth\": ").Append(grid.Depth).Append(",\n")
                .Append("    \"cellSize\": ").Append(F(grid.CellSize)).Append(",\n")
                .Append("    \"floorHeight\": ").Append(F(grid.FloorHeight)).Append(",\n")
                .Append("    \"originX\": ").Append(F(grid.OriginX)).Append(",\n")
                .Append("    \"originZ\": ").Append(F(grid.OriginZ)).Append(",\n")
                .Append("    \"cells\": \"")
                .Append(Convert.ToBase64String(grid.Cells ?? new byte[0]))
                .Append("\"\n  }");
        }

        private static int CountFlag(MapGridData grid, MapCellFlags flag)
        {
            if (grid == null || grid.Cells == null) return 0;

            var count = 0;
            for (var index = 0; index < grid.Cells.Length; index++)
            {
                if ((((MapCellFlags)grid.Cells[index]) & flag) == flag) count++;
            }

            return count;
        }

        private static string F(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
