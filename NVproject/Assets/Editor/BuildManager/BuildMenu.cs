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

        [MenuItem("Tools/NV/Build Production (Windows)", priority = 14)]
        public static void BuildProductionWindows()
        {
            BuildProduction(BuildPlatform.Windows64);
        }

        [MenuItem("Tools/NV/Build Production (WebGL)", priority = 15)]
        public static void BuildProductionWebGl()
        {
            BuildProduction(BuildPlatform.WebGL);
        }

        /// production 환경 애셋이 사는 곳. 이 메뉴는 지금 무엇이 선택돼 있든 이것으로 굽는다.
        private const string ProductionEnvironmentPath =
            NV.Client.Config.NVEnvironment.AssetFolder + "/production.asset";

        /// 배포용 빌드. 환경은 production, 개발 빌드 아님, 빌드 후 실행 없음.
        ///
        /// 환경은 <see cref="BuildSelection.EnvironmentOverride"/> 로만 얹는다 —
        /// `NVEnvironmentSelection` 을 바꾸면 이 빌드 한 번이 에디터의 환경 선택을
        /// 갈아치우고, 다음 Play 모드가 말없이 배포 서버에 붙는다.
        private static void BuildProduction(BuildPlatform platform)
        {
            var production = AssetDatabase.LoadAssetAtPath<NV.Client.Config.NVEnvironment>(
                ProductionEnvironmentPath);

            if (production == null)
            {
                Debug.LogError(
                    "[NV] " + ProductionEnvironmentPath + " 이 없다. "
                    + "Assets ▸ Create ▸ NV ▸ Environment 로 만들고 host 에 배포 서버를 적는다.");
                return;
            }

            var selection = BuildSelection.Load();
            selection.Platform = platform;
            selection.EnvironmentOverride = production;

            // 배포물은 릴리스다. 개발 빌드는 로그·디버거 포트가 열린 채로 나간다.
            selection.Development = false;

            if (platform == BuildPlatform.WebGL)
            {
                // 로컬 확인용 기본값(Disabled)과 달리 배포는 서버(Caddy)가
                // Content-Encoding 을 붙일 수 있으므로 Brotli 가 맞다.
                selection.WebGlCompression = WebGLCompressionFormat.Brotli;
            }

            Debug.Log(
                "[NV] Production 빌드: " + platform + " → " + production.BaseUrl
                + (platform == BuildPlatform.WebGL ? " · 압축 Brotli" : string.Empty));

            BuildRunner.Run(selection);
        }
    }
}
