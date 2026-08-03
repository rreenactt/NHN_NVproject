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

            Debug.Log(
                $"[NV] 맵 콜리전 export 완료: {path}\n" +
                $"박스 {data.Boxes.Length}개, 스폰 {data.Spawns.Length}개, 해시 {data.ComputeHash():X8}");
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

            text.Append("  ]\n}\n");
            return text.ToString();
        }

        private static string F(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
