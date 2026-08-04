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

            return PlanFor(sources);
        }

        /// 주어진 레벨로 계획을 세운다. 씬을 훑지 않는다.
        ///
        /// **맵 생성 도구가 쓴다.** 그 도구는 방금 만든 레벨을 손에 들고 있으므로 씬에서 다시
        /// 찾을 이유가 없고, 찾으려 하면 오히려 막힌다 — `SampleScene` 에는 예전 런타임 생성기가
        /// 아직 서 있어서 도구가 세운 레벨과 둘이 되고, 씬 스캔은 둘이면 (옳게) 거절한다.
        ///
        /// **판정은 하나도 다르지 않다.** 갈라지는 것은 "레벨을 어디서 얻는가" 한 걸음뿐이고
        /// 나머지는 같은 함수를 지난다. 두 벌이 되면 한쪽만 느슨해지는 날이 오고, 그 느슨한 쪽이
        /// 쓴 파일은 서버가 그대로 신뢰한다.
        public static MapExportPlan PlanFor(INetworkMapSource source)
        {
            var sources = new List<INetworkMapSource>(1);

            if (source != null)
            {
                sources.Add(source);
            }

            return PlanFor(sources);
        }

        private static MapExportPlan PlanFor(List<INetworkMapSource> sources)
        {
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

            StampProvenance(plan.Data, source);

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

            plan.SerializedKey = ComparisonKey(plan.Serialized);
            plan.ExistingKey = ComparisonKey(plan.ExistingText);

            // **등록 여부를 더 이상 묻지 않는다.** 서버가 이 디렉터리를 훑으므로 여기에 쓰는
            // 것이 곧 등록이다(`MapCatalogLoader`). 예전에는 `appsettings.json` 의 `Game:Maps`
            // 에 한 줄이 더 필요했고 이 함수가 그것을 경고했는데, 그 표는 이제 **별칭 표**다 —
            // 같은 검사를 남겨 두면 등록된 맵을 "등록되지 않았다" 고 말하고, 붙여 넣으라고
            // 내주는 조각은 아무 일도 하지 않는다. 틀린 경고는 없는 경고보다 나쁘다.
            //
            // 출력 폴더가 정말 이 저장소의 서버인지는 `ResolveOutputDirectory` 가 본다.
            // 등록에 대해 확인할 것이 그것 하나로 줄었다.
            return plan;
        }

        /// 출처를 고쳐 적고 **그것에 딸린 값을 전부 다시 계산한다.**
        ///
        /// 맵 생성 도구가 쓴다. 그 도구는 임시로 만들었다 지우는 오브젝트를 파이프라인에 건네므로
        /// `StampProvenance` 가 적는 씬 이름이 비고 컴포넌트 이름이 `BakedMapSource` 가 된다 —
        /// 나중에 이 파일이 어디서 나왔는지 묻는 사람에게 아무 말도 해 주지 않는다.
        ///
        /// **직렬화만 고치면 안 되기 때문에 여기 있다.** 계획은 직렬화 문자열과 그것의 비교용
        /// 형태를 함께 들고 있고, 둘은 같은 순간에 나와야 한다. 지금은 비교용 형태가 출처 줄을
        /// 버리므로 한쪽만 고쳐도 답이 같지만, 그것은 `ComparisonKey` 의 현재 구현에 기댄 우연이다
        /// — 출처를 여러 줄로 쓰게 되는 날 조용히 틀린다.
        ///
        /// 맵 해시는 바뀌지 않는다. 출처는 해시에 들어가지 않는다.
        public static void Restamp(MapExportPlan plan, string scene, string component)
        {
            if (plan?.Data?.Source == null)
            {
                return;
            }

            plan.Data.Source.Scene = scene ?? string.Empty;
            plan.Data.Source.Component = component ?? string.Empty;

            plan.Serialized = Serialize(plan.Data);
            plan.SerializedKey = ComparisonKey(plan.Serialized);
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

        /// 스키마 버전과 출처를 찍는다.
        ///
        /// **`MapExport.BuildMapData` 가 아니라 여기서 한다.** 그 함수는 런타임에도 불린다
        /// (접속 시 해시 대조, 오프라인 배치) — 거기서 시각을 찍으면 매 호출마다 다른 값이
        /// 들어가고, 해시에 안 들어가더라도 "런타임이 export 를 흉내낸다" 는 모양이 된다.
        /// 출처는 파일에만 있는 값이므로 파일을 만드는 쪽이 찍는다.
        private static void StampProvenance(MapData data, INetworkMapSource source)
        {
            data.Version = MapSchema.Current;

            var behaviour = source as MonoBehaviour;
            var scene = behaviour == null
                ? string.Empty
                : behaviour.gameObject.scene.name ?? string.Empty;

            data.Source = new MapSourceInfo
            {
                Scene = scene,
                Component = source.GetType().Name,
                ExportedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                ExporterVersion = ExporterVersion,
            };
        }

        /// export 도구의 버전. 바이트가 달라지는 변경을 할 때 올린다.
        ///
        /// 1 = 창과 검사가 붙고 스키마 버전·출처가 실리기 시작한 버전.
        /// 2 = 로비에 보여 줄 값(`meta`)이 실리기 시작한 버전.
        public const int ExporterVersion = 2;

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

            if (plan.Report.RejectedVolumes != null)
            {
                for (var index = 0; index < plan.Report.RejectedVolumes.Count; index++)
                {
                    plan.Errors.Add(
                        "씬 볼륨을 실을 수 없다 — " + plan.Report.RejectedVolumes[index] +
                        " 이 상태로 쓰면 클라이언트에는 그 지형이 있고 서버에는 없다. " +
                        "맵 해시는 그때도 일치한다.");
                }
            }

            if (plan.Report.GridAttached && plan.Report.FreeFloorCells == 0)
            {
                plan.Errors.Add(
                    "격자에 몸이 들어가는 셀(FreeFloor)이 하나도 없다. 격자와 콜리전 박스가 " +
                    "서로 다른 좌표계를 말하고 있다 — 원점, 셀 크기, CellIndex 의 축 순서를 본다.");
            }
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

            text.Append("{\n  \"version\": ").Append(data.Version)
                .Append(",\n  \"name\": \"").Append(data.Name).Append("\",\n");

            AppendSource(text, data.Source);
            AppendMeta(text, data.Meta);

            text.Append("  \"boxes\": [\n");

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

        /// 출처를 쓴다. 없으면 아무것도 쓰지 않는다.
        ///
        /// **해시에 들어가지 않으므로 파일에 추가해도 맵 해시가 바뀌지 않는다.** 그래서 이
        /// 필드를 도입하는 것만으로 재-export 를 강제하지 않는다.
        ///
        /// 문자열은 이스케이프하지 않는다. 들어가는 값이 씬 이름과 타입 이름과 ISO 8601
        /// 시각뿐이고, 그중 어느 것도 따옴표나 역슬래시를 가질 수 없다 — Unity 는 씬 이름에
        /// 그 문자를 허용하지 않고 타입 이름은 C# 식별자다.
        private static void AppendSource(StringBuilder text, MapSourceInfo source)
        {
            if (source == null) return;

            text.Append("  \"source\": { \"scene\": \"").Append(source.Scene)
                .Append("\", \"component\": \"").Append(source.Component)
                .Append("\", \"exportedAtUtc\": \"").Append(source.ExportedAtUtc)
                .Append("\", \"exporterVersion\": ").Append(source.ExporterVersion)
                .Append(" },\n");
        }

        /// 로비에 보여 줄 값을 쓴다. 없으면 아무것도 쓰지 않는다 — 서버가 그때 맵 자체에서
        /// 합성한다(표시용 이름은 맵 id).
        ///
        /// **여기 들어가는 문자열은 사람이 적는다.** 그래서 `AppendSource` 와 달리 이스케이프가
        /// 필요하다 — 설명에 따옴표를 하나 넣는 것으로 맵 파일이 파싱되지 않게 되고, 증상은
        /// "export 한 뒤로 서버가 안 뜬다" 가 된다.
        ///
        /// **비교(`ComparisonKey`)에서 빼지 않는다.** 출처의 시각은 매번 달라지므로 빼지만,
        /// 이 값은 사람이 고쳤을 때만 달라지고 그때는 파일을 다시 쓰는 것이 맞다.
        private static void AppendMeta(StringBuilder text, MapMetaInfo meta)
        {
            if (meta == null) return;

            text.Append("  \"meta\": {\n")
                .Append("    \"displayName\": ").Append(Quoted(meta.DisplayName)).Append(",\n")
                .Append("    \"description\": ").Append(Quoted(meta.Description)).Append(",\n")
                .Append("    \"recommendedPlayersMin\": ").Append(meta.RecommendedPlayersMin).Append(",\n")
                .Append("    \"recommendedPlayersMax\": ").Append(meta.RecommendedPlayersMax).Append(",\n")
                .Append("    \"tags\": [");

            var tags = meta.Tags ?? new string[0];

            for (var index = 0; index < tags.Length; index++)
            {
                if (index > 0) text.Append(", ");
                text.Append(Quoted(tags[index]));
            }

            text.Append("]\n  },\n");
        }

        /// JSON 문자열 하나. `null` 은 빈 문자열로 쓴다.
        ///
        /// 이스케이프하는 것은 역슬래시·따옴표와 제어문자다. 줄바꿈은 `\n` 으로 쓴다 —
        /// 그대로 넣으면 문자열 리터럴이 줄을 넘어 파싱이 깨지고, 게다가 `ComparisonKey` 가
        /// 줄 단위로 도는 함수라 비교까지 어긋난다.
        private static string Quoted(string value)
        {
            var text = new StringBuilder((value == null ? 0 : value.Length) + 2);
            text.Append('"');

            for (var index = 0; value != null && index < value.Length; index++)
            {
                var character = value[index];

                switch (character)
                {
                    case '\\': text.Append("\\\\"); break;
                    case '"': text.Append("\\\""); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (character < ' ')
                        {
                            text.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            text.Append(character);
                        }

                        break;
                }
            }

            text.Append('"');
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

        /// 두 파일이 같은 맵인가를 비교하기 위한 형태.
        ///
        /// **출처 줄을 뺀다.** 거기에는 export 시각이 있어 매번 달라지고, 그것을 그대로 비교하면
        /// "내용이 같으면 쓰지 않는다" 가 영구히 거짓이 된다 — 두 기능이 정면으로 부딪힌다.
        ///
        /// 빼는 쪽이 맞는 이유는 뜻에 있다. 시각은 지형이 아니므로 지형 비교에 들어갈 값이
        /// 아니고, 빼 두면 그 시각이 "export 를 마지막으로 돌린 때" 가 아니라 **"맵이 마지막으로
        /// 바뀐 때"** 가 된다. 후자가 알고 싶은 값이다.
        ///
        /// 출처는 정확히 한 줄로 쓰이므로(`AppendSource`) 그 줄만 버리면 된다. 여러 줄로 쓰게
        /// 바꾸면 이 함수도 같이 바꿔야 한다.
        private static string ComparisonKey(string text)
        {
            if (text == null)
            {
                return null;
            }

            var lines = text.Split('\n');
            var kept = new StringBuilder(text.Length);

            for (var index = 0; index < lines.Length; index++)
            {
                if (lines[index].StartsWith("  \"source\":", StringComparison.Ordinal))
                {
                    continue;
                }

                kept.Append(lines[index]);

                if (index < lines.Length - 1)
                {
                    kept.Append('\n');
                }
            }

            return kept.ToString();
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

        /// 지금 파일과 같은 맵인가. 출처(export 시각)는 비교하지 않는다 — 이유는
        /// `MapExportPipeline.ComparisonKey` 에 있다.
        public bool Unchanged => SerializedKey != null
            && ExistingKey != null
            && string.Equals(SerializedKey, ExistingKey, StringComparison.Ordinal);

        public string SerializedKey { get; set; }

        public string ExistingKey { get; set; }

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

            var volumes = Report.SceneVolumes == 0
                ? string.Empty
                : $" (그중 씬 볼륨 {Report.SceneVolumes}개)";

            return $"박스 {Data.Boxes.Length}개{volumes}, 스폰 {Data.Spawns.Length}개, " +
                   $"{grid}, 해시 {Hash:X8}";
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
