using System.IO;
using UnityEngine;

// System.Diagnostics 를 통째로 들이면 그 안의 Debug 가 UnityEngine.Debug 와 충돌해
// CS0104 가 난다. 필요한 두 타입만 별칭으로 가져온다.
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace NV.Client.EditorTools
{
    /// 빌드된 플레이어를 여러 개 띄운다.
    ///
    /// 에디터 인스턴스는 한 프로젝트에 하나뿐이므로, 두 플레이어를 보려면 최소한 하나는
    /// 빌드된 플레이어여야 한다. WebGL 이 최종 타겟이지만 빌드가 몇 분 걸려 반복에 쓸 수
    /// 없다. 그래서 확인용으로는 Windows 스탠드얼론을 쓴다 — 전송 구현은 에디터와 같은
    /// `ClientWebSocket` 경로이므로 WebGL 전용 결함만 빠지고, 그 외의 동기화 문제는
    /// 그대로 재현된다.
    ///
    /// 두 플레이어를 실제로 못 쓰게 만드는 설정이 두 개 있고 둘 다 프로젝트 설정에서
    /// 이미 고쳐 두었다. 증상이 네트워크 결함으로 보이기 때문에 여기 적어 둔다.
    ///
    /// - **Run In Background 가 꺼져 있으면** 포커스를 잃은 창이 스크립트를 멈춘다.
    ///   상대가 얼어붙은 것처럼 보이고, 다시 클릭하면 순간이동한다. 서버는 정상이다.
    /// - **전체 화면이면** 창 두 개를 나란히 볼 수 없다. 기본값이 Fullscreen Window 였다.
    ///
    /// `forceSingleInstance` 도 꺼져 있어야 두 번째 실행이 거부되지 않는다.
    public static class PlayerLaunchService
    {
        /// <summary>빌드물이 있으면 선택한 수만큼 띄운다. 하나도 못 띄우면 false.</summary>
        public static bool Launch(BuildSelection selection)
        {
            if (!selection.CanLaunch)
            {
                Debug.LogError("[NV] " + selection.Platform + " 빌드물은 여기서 띄울 수 없다.");
                return false;
            }

            var path = Path.GetFullPath(selection.OutputPath);

            if (!File.Exists(path))
            {
                Debug.LogError("[NV] 빌드된 클라이언트가 없다. 먼저 빌드한다.\n" + path);
                return false;
            }

            var count = Mathf.Max(1, selection.InstanceCount);

            for (var index = 0; index < count; index++)
            {
                LaunchOne(selection, path);
            }

            var environment = selection.Environment;

            Debug.Log(
                "[NV] 클라이언트 " + count + "개를 띄웠다. 환경 " + environment.Id
                + " → " + environment.BaseUrl + "\n"
                + "한쪽에서 방을 만들어 코드를 다른 쪽 '코드로 참가' 에 넣는다. "
                + "공개로 만든 방만 목록에 보인다.\n"
                + "서버가 떠 있어야 한다: dotnet run --project Api");

            return true;
        }

        /// 인스턴스마다 로그 파일을 따로 준다. 같은 파일에 쓰면 두 번째 인스턴스가
        /// 로그를 남기지 못하고, 문제가 생겼을 때 볼 것이 없다.
        private static void LaunchOne(BuildSelection selection, string executablePath)
        {
            var stamp = System.DateTime.Now.ToString("HHmmss-fff");
            var directory = Path.GetFullPath(selection.OutputDirectory);
            var logPath = Path.Combine(directory, "client-" + stamp + ".log");

            var arguments =
                "-screen-fullscreen 0"
                + " -screen-width " + selection.WindowWidth
                + " -screen-height " + selection.WindowHeight
                + " -logFile \"" + logPath + "\"";

            var info = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                UseShellExecute = true,
                WorkingDirectory = directory,
            };

            Process.Start(info);
            Debug.Log("[NV] 클라이언트 실행. 로그 " + logPath);
        }
    }
}
