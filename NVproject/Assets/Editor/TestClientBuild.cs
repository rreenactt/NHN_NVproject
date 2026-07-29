using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// System.Diagnostics 를 통째로 들이면 그 안의 Debug 가 UnityEngine.Debug 와 충돌해
// CS0104 가 난다. 필요한 두 타입만 별칭으로 가져온다.
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace NV.Client.EditorTools
{
    /// 두 번째 클라이언트를 띄우는 가장 짧은 경로.
    ///
    /// 에디터 인스턴스는 한 프로젝트에 하나뿐이므로, 두 플레이어를 보려면 최소한 하나는
    /// 빌드된 플레이어여야 한다. WebGL 이 최종 타겟이지만 빌드가 몇 분 걸려 반복에 쓸 수
    /// 없다. 그래서 확인용으로는 Windows 스탠드얼론을 쓴다 — 전송 구현은 에디터와 같은
    /// `ClientWebSocket` 경로이므로 WebGL 전용 결함만 빠지고, 그 외의 동기화 문제는
    /// 그대로 재현된다.
    ///
    /// 두 플레이어를 실제로 못 쓰게 만드는 설정이 두 개 있고 둘 다 프로젝트 설정에서
    /// 이미 고쳐 두었다. 증상이 네트워크 결함으로 보이기 때문에 적어 둔다.
    ///
    /// - **Run In Background 가 꺼져 있으면** 포커스를 잃은 창이 스크립트를 멈춘다.
    ///   상대가 얼어붙은 것처럼 보이고, 다시 클릭하면 순간이동한다. 서버는 정상이다.
    /// - **전체 화면이면** 창 두 개를 나란히 볼 수 없다. 기본값이 Fullscreen Window 였다.
    ///
    /// `forceSingleInstance` 도 꺼져 있어야 두 번째 실행이 거부되지 않는다.
    public static class TestClientBuild
    {
        private const string ScenePath = "Assets/Scenes/MultiplayerTest.unity";
        private const string OutputDirectory = "Builds/TestClient";
        private const string ExecutableName = "NVTestClient.exe";

        [MenuItem("Tools/NV Network/Build Test Client (Windows)", priority = 200)]
        public static void Build()
        {
            BuildInternal();
        }

        [MenuItem("Tools/NV Network/Launch Test Client", priority = 201)]
        public static void Launch()
        {
            var path = ExecutablePath();
            if (!File.Exists(path))
            {
                if (!EditorUtility.DisplayDialog(
                        "NV",
                        "빌드된 클라이언트가 없다. 지금 빌드할까?\n\n" + path,
                        "빌드한다",
                        "취소"))
                {
                    return;
                }

                if (!BuildInternal())
                {
                    return;
                }
            }

            LaunchInstance(path);
        }

        [MenuItem("Tools/NV Network/Build and Launch 2 Clients", priority = 202)]
        public static void BuildAndLaunchTwo()
        {
            if (!BuildInternal())
            {
                return;
            }

            var path = ExecutablePath();
            LaunchInstance(path);
            LaunchInstance(path);

            Debug.Log(
                "[NV] 클라이언트 2개를 띄웠다. 각 창의 접속 패널에서 같은 룸으로 접속한다.\n" +
                "서버가 떠 있어야 한다: dotnet run --project Api");
        }

        private static bool BuildInternal()
        {
            if (!File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog(
                    "NV",
                    $"{ScenePath} 가 없다. Tools ▸ NV Network ▸ Create Multiplayer Test Scene 을 먼저 실행한다.",
                    "확인");
                return false;
            }

            Directory.CreateDirectory(OutputDirectory);

            // 개발 빌드로 만든다. 로그가 남고 빌드가 빠르다 — 이건 계측용이고 배포물이 아니다.
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ExecutablePath(),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.AllowDebugging,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError(
                    $"[NV] 클라이언트 빌드 실패: {summary.result}, 에러 {summary.totalErrors}개. " +
                    "Build 창의 로그를 확인한다.");
                return false;
            }

            Debug.Log(
                $"[NV] 클라이언트 빌드 완료: {summary.outputPath}\n" +
                $"{summary.totalSize / (1024 * 1024)}MB, {summary.totalTime.TotalSeconds:F0}초");

            return true;
        }

        /// 인스턴스마다 로그 파일을 따로 준다. 같은 파일에 쓰면 두 번째 인스턴스가
        /// 로그를 남기지 못하고, 문제가 생겼을 때 볼 것이 없다.
        private static void LaunchInstance(string path)
        {
            var stamp = System.DateTime.Now.ToString("HHmmss-fff");
            var logPath = Path.GetFullPath(Path.Combine(OutputDirectory, $"client-{stamp}.log"));

            var info = new ProcessStartInfo
            {
                FileName = Path.GetFullPath(path),
                Arguments = $"-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile \"{logPath}\"",
                UseShellExecute = true,
                WorkingDirectory = Path.GetFullPath(OutputDirectory),
            };

            Process.Start(info);
            Debug.Log($"[NV] 클라이언트 실행. 로그 {logPath}");
        }

        private static string ExecutablePath()
        {
            return Path.Combine(OutputDirectory, ExecutableName);
        }
    }
}
