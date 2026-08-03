using System;
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
            var map = MapExport.FindInScene();
            if (map == null)
            {
                EditorUtility.DisplayDialog(
                    "NV",
                    "씬에서 INetworkMapSource 를 구현한 레벨을 찾지 못했다.\n" +
                    "SampleScene(Backrooms) 이나 MultiplayerTest(테스트 룸) 를 열고 다시 실행한다.",
                    "확인");
                return;
            }

            var data = MapExport.BuildMapData(map);
            var path = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                RelativeOutputDirectory,
                data.Name + ".json"));

            Directory.CreateDirectory(Path.GetDirectoryName(path));
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
