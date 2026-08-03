using System.Collections.Generic;
using System.IO;
using NV.Client.Config;
using UnityEditor;

namespace NV.Client.EditorTools
{
    /// 빌드하기 전에 알아야 하는 것들. **막지는 않는다.**
    ///
    /// 이 창의 실질적인 값이 여기 있다. 지금 두 클라이언트가 서로 못 붙는 원인은 대부분
    /// 빌드가 아니라 셋 중 하나이고, 셋 다 화면에 아무 단서를 남기지 않은 채 네트워크
    /// 결함처럼 보인다.
    ///
    /// - **서버가 안 떠 있다.** 증상은 로비의 "서버 응답 없음" 뿐이다.
    /// - **맵을 export 하지 않았다.** 빌드도 되고 실행도 되고, 접속한 뒤 맵 해시 불일치만
    ///   콘솔에 남는다. 씬을 고쳤는데 export 를 잊은 것이 이 저장소에서 가장 자주 나는 실수다.
    /// - **진입 씬이 밀렸다.** 빌드를 실행해야 보인다.
    ///
    /// 검사는 요청할 때만 돌린다. `OnGUI` 마다 소켓을 열면 창이 멈춘다.
    public static class BuildDiagnostics
    {
        /// 서버 생존 확인에 기다리는 시간(ms).
        ///
        /// 로컬 서버는 뜬 상태면 1ms 안에 답한다. 이 값은 "꺼져 있다" 를 판정하는 데 드는
        /// 시간이고, 그동안 창이 멈추므로 짧아야 한다.
        private const int ProbeTimeoutMilliseconds = 300;

        /// 씬과 맵 이름의 짝. 이 프로젝트에서 씬과 맵은 짝으로만 존재한다.
        private static readonly (string Scene, string Map)[] MapPairs =
        {
            ("Assets/Scenes/SampleScene.unity", "backrooms"),
            ("Assets/Scenes/MultiplayerTest.unity", "test-room"),
        };

        /// 서버가 읽는 맵 파일이 있는 곳. `MapCollisionExporter` 와 같은 값이다.
        private const string MapDataRelativePath = "../../NVserver/MapData";

        public enum Level
        {
            Ok,
            Warning,
        }

        public readonly struct Line
        {
            public Line(Level level, string text)
            {
                Level = level;
                Text = text;
            }

            public Level Level { get; }

            public string Text { get; }
        }

        public static List<Line> Collect(BuildSelection selection)
        {
            var lines = new List<Line>(4);

            lines.Add(CheckServer(selection.Environment));
            lines.Add(CheckEntryScene());
            lines.AddRange(CheckMapData());

            return lines;
        }

        /// 서버가 지금 응답하는가.
        ///
        /// `GET /health` 를 보내지 않고 TCP 접속만 해 본다. 응답 본문이 필요 없고,
        /// `UnityWebRequest` 는 비동기라 창이 그것을 기다리려면 폴링 루프가 하나 생긴다 —
        /// "포트가 열려 있는가" 는 소켓 하나로 답이 나온다.
        private static Line CheckServer(NVEnvironment environment)
        {
            var host = environment.Host;
            var colon = host.LastIndexOf(':');

            if (colon <= 0 || !int.TryParse(host.Substring(colon + 1), out var port))
            {
                return new Line(Level.Warning, "환경의 주소에 포트가 없다: " + host);
            }

            var name = host.Substring(0, colon);

            try
            {
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    var pending = client.BeginConnect(name, port, null, null);

                    if (!pending.AsyncWaitHandle.WaitOne(ProbeTimeoutMilliseconds))
                    {
                        return new Line(Level.Warning, "서버 응답 없음 (" + host + ") — dotnet run --project Api");
                    }

                    client.EndConnect(pending);
                }
            }
            catch
            {
                // 접속 거부·주소 해석 실패 모두 "지금 붙을 수 없다" 로 같다. 사유를 갈라
                // 보여 주면 읽는 사람이 판단할 것이 늘기만 한다.
                return new Line(Level.Warning, "서버 응답 없음 (" + host + ") — dotnet run --project Api");
            }

            return new Line(Level.Ok, "서버 응답함 (" + host + ")");
        }

        private static Line CheckEntryScene()
        {
            var scenes = EditorBuildSettings.scenes;

            for (var index = 0; index < scenes.Length; index++)
            {
                if (!scenes[index].enabled)
                {
                    continue;
                }

                return scenes[index].path.EndsWith("/MainLobby.unity")
                    ? new Line(Level.Ok, "진입 씬(0번)이 MainLobby 다")
                    : new Line(Level.Warning, "진입 씬(0번)이 MainLobby 가 아니다: " + scenes[index].path);
            }

            return new Line(Level.Warning, "빌드 설정에 켜져 있는 씬이 없다");
        }

        /// 서버가 읽는 맵 파일이 씬보다 낡았는가.
        ///
        /// export 를 빌드 단계로 만들지 않은 이유가 여기 적혀 있어야 한다. 레벨은 진입
        /// 씬에 없다 — `backrooms` 는 `SampleScene`, `test-room` 은 `MultiplayerTest` 에
        /// 있고 export 는 열린 씬에서만 동작한다. 빌드 도중에 씬 두 개를 열고 되돌리는
        /// 것은 이 도구가 감당할 크기가 아니고, 실패하면 사람의 씬 편집을 잃는다.
        /// 같은 실수를 같은 순간에 잡는 더 싼 방법이 이 비교다.
        private static IEnumerable<Line> CheckMapData()
        {
            var lines = new List<Line>(MapPairs.Length);
            var directory = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, MapDataRelativePath));

            for (var index = 0; index < MapPairs.Length; index++)
            {
                var pair = MapPairs[index];

                if (!File.Exists(pair.Scene))
                {
                    continue;
                }

                var mapPath = Path.Combine(directory, pair.Map + ".json");

                if (!File.Exists(mapPath))
                {
                    lines.Add(new Line(
                        Level.Warning,
                        pair.Map + ".json 이 없다 — " + Path.GetFileName(pair.Scene)
                        + " 를 열고 Map ▸ Export Map Collision"));
                    continue;
                }

                if (File.GetLastWriteTimeUtc(mapPath) < File.GetLastWriteTimeUtc(pair.Scene))
                {
                    lines.Add(new Line(
                        Level.Warning,
                        pair.Map + ".json 이 씬보다 오래됐다 — 맵 해시가 어긋날 수 있다"));
                }
            }

            if (lines.Count == 0)
            {
                lines.Add(new Line(Level.Ok, "맵 데이터가 씬보다 새롭다"));
            }

            return lines;
        }
    }
}
