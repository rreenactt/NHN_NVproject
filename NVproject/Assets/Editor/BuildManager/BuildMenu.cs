using UnityEditor;
using UnityEngine;

namespace NV.Client.EditorTools
{
    /// 창을 열지 않고 가는 지름길.
    ///
    /// Build Manager 창이 정식 입구이고 이 항목들은 그 창에서 매번 같은 선택을 하게 되는
    /// 경로만 남긴 것이다. 특히 **Build and Launch 2 Clients** 는 이 저장소에서 가장 많이
    /// 쓰이는 명령이라 창을 거치지 않는 자리를 유지한다.
    ///
    /// 선택은 여기서 만들지 않고 <see cref="BuildSelection"/> 이 `EditorPrefs` 에서
    /// 읽어 온다 — 창에서 바꾼 환경·창 크기가 이 지름길에도 그대로 적용되어야 한다.
    public static class BuildMenu
    {
        [MenuItem("Tools/NV/Build and Launch 2 Clients", priority = 11)]
        public static void BuildAndLaunchTwo()
        {
            var selection = BuildSelection.Load();

            // 이름이 약속한 것을 이름이 지킨다. 창의 선택이 WebGL 이나 1개였더라도
            // 이 항목은 Windows 두 개다 — 그렇지 않으면 같은 메뉴가 어떤 날은 브라우저
            // 빌드를 뽑는다.
            selection.Platform = BuildPlatform.Windows64;
            selection.InstanceCount = 2;

            BuildRunner.RunAndLaunch(selection);
        }

        [MenuItem("Tools/NV/Build (current selection)", priority = 12)]
        public static void BuildCurrentSelection()
        {
            var selection = BuildSelection.Load();

            Debug.Log(
                "[NV] 선택: " + selection.Platform + " · 환경 " + selection.Environment.Id
                + " · " + (selection.Development ? "개발" : "릴리스") + " 빌드");

            if (selection.LaunchAfterBuild)
            {
                BuildRunner.RunAndLaunch(selection);
                return;
            }

            BuildRunner.Run(selection);
        }

        [MenuItem("Tools/NV/Launch Clients (no build)", priority = 13)]
        public static void LaunchOnly()
        {
            var selection = BuildSelection.Load();
            selection.Platform = BuildPlatform.Windows64;

            PlayerLaunchService.Launch(selection);
        }
    }
}
