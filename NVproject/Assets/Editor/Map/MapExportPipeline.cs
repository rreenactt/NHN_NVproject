using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NV.Client.Net;
using NV.Shared.Collision;
using UnityEngine;

namespace NV.Client.EditorTools
{
    /// export 의 판정과 쓰기. **UI 를 갖지 않는다.**
    ///
    /// 메뉴 항목과 창이 같은 것을 부르고, EditMode 테스트도 부를 수 있다. 예전에는 판정과
    /// 대화상자와 파일 쓰기가 한 함수에 있었고, 그래서 "무엇을 쓸 것인지" 를 쓰지 않고
    /// 확인할 방법이 없었다 — 메뉴를 누르는 것이 곧 덮어쓰기였다.
    ///
    /// 판정은 `Plan` 을 만드는 것으로 끝나고, `Write` 는 그 계획이 통과했을 때만 쓴다.
    public static class MapExportPipeline
    {
        /// 저장소 배치는 NHN_NVproject/{NVproject,NVserver} 다.
        private const string RelativeOutputDirectory = "../../NVserver/MapData";

        /// 서버 설정. 여기 등록되지 않은 맵 id 는 방을 만들 때 거절된다.
        private const string RelativeSettingsPath = "../../NVserver/Api/appsettings.json";

        /// 씬에서 레벨을 찾아 계획을 세운다. 아무것도 쓰지 않는다.
        public static MapExportPlan Plan()
        {
            var sources = new List<INetworkMapSource>(2);
            MapExport.FindAllInScene(sources);

            var plan = new MapExportPlan(sources);

            if (sources.Count != 1)
            {
                return plan;
            }

            var source = sources[0];

            plan.Blocker = source.DescribeExportBlocker();
            if (plan.Blocker != null)
            {
                return plan;
            }

            plan.OutputDirectory = ResolveOutputDirectory(out var pathError);
            plan.PathError = pathError;

            if (pathError != null)
            {
                return plan;
            }

            plan.Data = MapExport.BuildMapData(source, out var report);
            plan.Report = report;
            plan.OutputPath = Path.Combine(plan.OutputDirectory, plan.Data.Name + ".json");

            InspectGridReport(plan);

            if (MapDataValidator.TryValidateSchema(plan.Data, plan.Errors))
            {
                MapDataValidator.InspectSimulation(plan.Data, plan.Errors, plan.Warnings);
            }

            plan.Serialized = Serialize(plan.Data);
            plan.Hash = plan.Data.ComputeHash();

            // **텍스트로 비교한다.** 기존 파일의 해시를 알려면 JSON 을 파싱해야 하는데
            // Unity 에는 `System.Text.Json` 이 없고, 맵 파일을 읽는 파서를 여기에 하나 더
            // 두는 것은 스키마가 갈릴 자리를 하나 더 만드는 일이다. 직렬화가 결정적이므로
            // 바이트가 같은 것과 같은 맵인 것은 같은 말이고, 그것으로 결정에 필요한 답
            // ("다시 쓸 필요가 있는가")이 나온다.
            plan.ExistingText = File.Exists(plan.OutputPath)
                ? Normalize(File.ReadAllText(plan.OutputPath))
                : null;

            CheckRegistration(plan);

            return plan;
        }

        /// 계획대로 쓴다. 통과하지 않은 계획은 거절한다.
        ///
        /// **원자적으로 쓴다.** 임시 파일에 쓴 뒤 옮긴다 — 쓰는 중에 에디터가 죽으면 반쯤
        /// 쓰인 맵이 남고, 서버는 그것을 파싱 실패로 거절하므로 증상은 "갑자기 서버가 안
        /// 뜬다" 가 된다.
        ///
        /// **내용이 같으면 쓰지 않는다.** 파일의 수정 시각이 흔들리면 git 은 조용해도 Unity
        /// 와 서버는 다시 빌드한다.
        public static bool TryWrite(MapExportPlan plan, out string message)
        {
            if (plan == null || !plan.CanExport)
            {
                message = "통과하지 않은 계획으로는 쓰지 않는다.";
                return false;
            }

            if (plan.Unchanged)
            {
                message = $"내용이 같아 쓰지 않았다: {plan.OutputPath}";
                return true;
            }

            Directory.CreateDirectory(plan.OutputDirectory);

            var temporary = plan.OutputPath + ".tmp";
            var encoding = new UTF8Encoding(false);

            File.WriteAllText(temporary, plan.Serialized, encoding);

            if (File.Exists(plan.OutputPath))
            {
                File.Replace(temporary, plan.OutputPath, null);
            }
            else
            {
                File.Move(temporary, plan.OutputPath);
            }

            message = $"export 완료: {plan.OutputPath}\n{plan.Describe()}";
            return true;
        }

        /// 격자 보고를 오류로 옮긴다.
        ///
        /// 거절하는 두 경우는 **격자를 내놓았는데 실리지 않은 것**과 **실렸는데 몸이 들어가는
        /// 셀이 하나도 없는 것**이다. 격자를 내놓지 않는 레벨은 정상이므로 통과시킨다.
        private static void InspectGridReport(MapExportPlan plan)
        {
            if (plan.Report.GridOffered && plan.Report.GridError != null)
            {
                plan.Errors.Add(
                    $"레벨이 내놓은 격자가 잘못돼 실리지 않았다: {plan.Report.GridError}. " +
                    "격자 없이 쓰면 서버가 그것을 정상으로 받아들이고, 매치에 열쇠도 문도 생기지 않는다.");
            }

            if (plan.Report.GridAttached && plan.Report.FreeFloorCells == 0)
            {
                plan.Errors.Add(
                    "격자에 몸이 들어가는 셀(FreeFloor)이 하나도 없다. 격자와 콜리전 박스가 " +
                    "서로 다른 좌표계를 말하고 있다 — 원점, 셀 크기, CellIndex 의 축 순서를 본다.");
            }
        }

        /// 이 맵 id 가 서버에 등록되어 있는가.
        ///
        /// **경고까지만 한다.** 등록은 서버 설정의 몫이고, 에디터가 서버 설정을 고치는 것은
        /// 되돌리기 어렵다. 다만 등록하지 않으면 그 맵으로 방을 만들 수 없고(등록되지 않은 맵
        /// id 는 기본 맵으로 열리지 않고 거절된다), export 한 사람은 자기 파일이 왜 안 먹는지
        /// 알 수 없다 — `backrooms2f` 가 정확히 그렇게 죽었다.
        ///
        /// 판정은 문자열 검색이다. Unity 에는 JSON 파서가 없고, 이 답은 조언이지 관문이
        /// 아니므로 파서를 하나 더 들일 값은 아니다. 못 읽으면 아무 말도 하지 않는다 —
        /// 틀린 경고는 없는 경고보다 나쁘다.
        private static void CheckRegistration(MapExportPlan plan)
        {
            var settings = Path.GetFullPath(Path.Combine(Application.dataPath, RelativeSettingsPath));

            if (!File.Exists(settings))
            {
                return;
            }

            var text = File.ReadAllText(settings);
            var maps = text.IndexOf("\"Maps\"", StringComparison.Ordinal);

            if (maps < 0)
            {
                return;
            }

            var open = text.IndexOf('{', maps);
            var close = open < 0 ? -1 : text.IndexOf('}', open);

            if (open < 0 || close < 0)
            {
                return;
            }

            var section = text.Substring(open, close - open);

            plan.RegistrationKnown = true;
            plan.Registered = section.IndexOf($"\"{plan.Data.Name}\"", StringComparison.Ordinal) >= 0;

            if (plan.Registered)
            {
                return;
            }

            // `default` 로 등록된 파일이 이 맵일 수 있다. 그때는 이름으로 등록되어 있지
            // 않아도 서버가 이 맵을 쓰고 있으므로, 확정해 말하지 않고 조각만 내놓는다.
            plan.RegistrationSnippet =
                $"\"{plan.Data.Name}\": \"../MapData/{plan.Data.Name}.json\"";
        }

        /// 출력 폴더를 정하고 **그것이 정말 이 저장소의 서버인지 확인한다.**
        ///
        /// 경로는 저장소 배치를 가정한 상대 경로다. 배치가 다르면 예전 코드는
        /// `Directory.CreateDirectory` 로 엉뚱한 곳에 폴더를 만들고 맵을 거기 썼다 — export 는
        /// 성공했다고 말하고 서버는 옛 파일로 계속 판정한다. 서버의 설정 파일이 옆에 있는지
        /// 보는 것이 가장 싼 확인이다.
        private static string ResolveOutputDirectory(out string error)
        {
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, RelativeOutputDirectory));
            var settings = Path.GetFullPath(Path.Combine(Application.dataPath, RelativeSettingsPath));

            if (!File.Exists(settings))
            {
                error = $"출력 폴더가 이 저장소의 서버가 아니다.\n{directory}\n" +
                        $"옆에 서버 설정({settings})이 없다. NVproject 와 NVserver 가 같은 " +
                        "저장소에 나란히 있는지 확인한다.";
                return directory;
            }

            error = null;
            return directory;
        }

        // ==================================================== 직렬화

        /// 손으로 쓴다. `System.Text.Json` 을 쓰지 않는다 — `Shared` 는 NuGet 참조를 갖지
        /// 않고 Unity 에도 그 어셈블리가 없다. 필드가 여섯 개인 박스 목록이라 직접 쓰는 편이
        /// 싸다.
        ///
        /// 부동소수점은 왕복 보존 형식("R")으로 쓴다 — 자릿수를 줄이면 서버가 파싱한 값이
        /// 클라이언트의 값과 비트가 달라지고, 맵 해시가 이유 없이 불일치한다.
        public static string Serialize(MapData data)
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

        /// 격자를 쓴다. 없으면 아무것도 쓰지 않는다 — 필드가 없으면 서버가 `null` 로 읽고,
        /// 그것이 "격자를 내놓지 않는 레벨" 의 정상 표현이다.
        ///
        /// `cells` 는 base64 다. System.Text.Json 이 `byte[]` 를 그렇게 읽으므로 서버는 파싱
        /// 코드를 한 줄도 쓰지 않는다. 2층 35×35 = 2450 셀이 한 줄에 들어가고, 숫자 배열로
        /// 쓰면 같은 정보가 4배 넘게 커진다.
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

        private static string F(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        /// 줄바꿈을 맞춘다. **없으면 "변경 없음" 이 영구히 거짓이 된다.**
        ///
        /// 직렬화는 `\n` 으로 쓰는데 `core.autocrlf` 가 켜진 클론에서는 체크아웃이 `\r\n` 을
        /// 돌려준다. 그러면 내용이 같아도 텍스트가 달라 매번 다시 쓰고, 그 쓰기가 다시 `\n`
        /// 이므로 git 이 파일 전체를 변경으로 보여 준다 — 다시 쓰지 않으려고 만든 비교가
        /// 오히려 매번 전체 diff 를 만든다.
        ///
        /// 저장소에는 `.gitattributes` 로 이 파일들을 `\n` 에 고정해 두었다. 이 함수는 그
        /// 설정이 없는 작업 사본에서도 판정이 맞게 하는 쪽이다.
        private static string Normalize(string text)
        {
            return text == null ? null : text.Replace("\r\n", "\n");
        }
    }

    /// 무엇을 쓸 것인가, 그리고 쓸 수 있는가.
    ///
    /// 쓰기 전에 사람이 볼 수 있어야 하는 것을 전부 들고 있다. 예전에는 이 값들이 파일을 쓴
    /// 뒤 콘솔 한 줄로만 나왔다.
    public sealed class MapExportPlan
    {
        public MapExportPlan(List<INetworkMapSource> sources)
        {
            Sources = sources ?? new List<INetworkMapSource>();
            Errors = new List<string>();
            Warnings = new List<string>();
        }

        public List<INetworkMapSource> Sources { get; }

        public INetworkMapSource Source => Sources.Count == 1 ? Sources[0] : null;

        /// 재현되지 않는 레벨의 이유. 없으면 <c>null</c>.
        public string Blocker { get; set; }

        public string OutputDirectory { get; set; }

        public string OutputPath { get; set; }

        /// 출력 경로 자체가 잘못된 이유. 없으면 <c>null</c>.
        public string PathError { get; set; }

        public MapData Data { get; set; }

        public MapBuildReport Report { get; set; }

        public List<string> Errors { get; }

        public List<string> Warnings { get; }

        public string Serialized { get; set; }

        public uint Hash { get; set; }

        /// 지금 파일에 있는 내용. 파일이 없으면 <c>null</c>.
        public string ExistingText { get; set; }

        public bool IsNewFile => Data != null && ExistingText == null;

        public bool Unchanged => Serialized != null
            && ExistingText != null
            && string.Equals(Serialized, ExistingText, StringComparison.Ordinal);

        /// 서버 설정을 읽을 수 있었는가. 못 읽었으면 등록 여부를 말하지 않는다.
        public bool RegistrationKnown { get; set; }

        public bool Registered { get; set; }

        /// 등록되어 있지 않을 때 붙여 넣을 조각.
        public string RegistrationSnippet { get; set; }

        /// 씬에 레벨이 둘 이상일 때, 이름이 겹치는 것이 있으면 그 이름.
        public string DuplicateName
        {
            get
            {
                for (var outer = 0; outer < Sources.Count; outer++)
                {
                    for (var inner = outer + 1; inner < Sources.Count; inner++)
                    {
                        if (string.Equals(Sources[outer].MapName, Sources[inner].MapName, StringComparison.Ordinal))
                        {
                            return Sources[outer].MapName;
                        }
                    }
                }

                return null;
            }
        }

        public bool CanExport => Sources.Count == 1
            && Blocker == null
            && PathError == null
            && Data != null
            && Errors.Count == 0;

        /// 사람이 읽을 한 줄 요약. 콘솔과 창이 같은 문장을 쓴다.
        public string Describe()
        {
            if (Data == null)
            {
                return "쓸 것이 없다.";
            }

            var grid = Data.Grid == null
                ? "격자 없음"
                : $"격자 {Data.Grid.Floors}층 {Data.Grid.Width}×{Data.Grid.Depth}" +
                  $" (설 수 있는 셀 {CountFlag(Data.Grid, MapCellFlags.Standable)}개," +
                  $" 몸이 들어가는 셀 {Report.FreeFloorCells}개)";

            return $"박스 {Data.Boxes.Length}개, 스폰 {Data.Spawns.Length}개, {grid}, 해시 {Hash:X8}";
        }

        public static int CountFlag(MapGridData grid, MapCellFlags flag)
        {
            if (grid == null || grid.Cells == null) return 0;

            var count = 0;
            for (var index = 0; index < grid.Cells.Length; index++)
            {
                if ((((MapCellFlags)grid.Cells[index]) & flag) == flag) count++;
            }

            return count;
        }
    }
}
