using System.IO;
using NV.Client.Config;
using UnityEditor;

namespace NV.Client.EditorTools
{
    /// 어떤 플레이어를 만들 것인가. 창이 고르고 `BuildRunner` 가 읽는다.
    ///
    /// 값은 `EditorPrefs` 에 산다 — 사람별이고 커밋되지 않는다. 그것이 맞다. 내가
    /// 지금 WebGL 을 보고 있다는 사실이 남의 작업 폴더에 들어갈 이유가 없다.
    ///
    /// **씬 목록은 여기 없다.** 그것은 `EditorBuildSettings` 가 소유하고 창은 그것을
    /// 직접 편집한다. 목록을 두 벌로 두면 반드시 어긋나고, 그 어긋남은 빌드를 실행해야
    /// 보인다 — 예전 `TestClientBuild` 가 `MultiplayerTest` 하나를 하드코딩해 두어
    /// 계측용 빌드의 진입 화면이 제품과 달라졌던 것이 그 사례다.
    ///
    /// 도메인 리로드를 넘어 살아야 하므로 창은 이 객체를 필드로 들지 않고 매번 읽는다.
    public sealed class BuildSelection
    {
        private const string PlatformKey = "nv.build.platform";
        private const string DevelopmentKey = "nv.build.development";
        private const string LaunchKey = "nv.build.launch";
        private const string InstancesKey = "nv.build.instances";
        private const string WidthKey = "nv.build.width";
        private const string HeightKey = "nv.build.height";
        private const string CompressionKey = "nv.build.webgl.compression";

        /// 빌드물이 쌓이는 곳. `.gitignore` 의 `[Bb]uilds/` 가 이미 무시한다.
        public const string OutputRoot = "Builds";

        public BuildPlatform Platform { get; set; } = BuildPlatform.Windows64;

        /// 개발 빌드로 만드는가. 로그가 남고 빌드가 빠르다.
        public bool Development { get; set; } = true;

        public bool LaunchAfterBuild { get; set; } = true;

        /// 띄울 인스턴스 수. 매치에 두 명이 필요하므로 기본이 2다.
        public int InstanceCount { get; set; } = 2;

        public int WindowWidth { get; set; } = 1280;

        public int WindowHeight { get; set; } = 720;

        /// WebGL 빌드의 압축 형식. **기본이 `Disabled` 인 것이 의도다.**
        ///
        /// Unity 의 기본값은 Brotli 이고, 그렇게 뽑은 빌드는 서버가
        /// `Content-Encoding: br` 를 붙여 줘야만 브라우저가 읽는다. `python -m http.server`
        /// 같은 평범한 정적 서버는 그것을 붙이지 않으므로, 브라우저는 압축된 바이트를
        /// 스크립트로 해석하려다 실패한다 — 증상은 검은 화면과 콘솔의 파싱 오류이고
        /// 빌드나 코드를 의심하게 만든다.
        ///
        /// 배포할 때는 서버가 그 헤더를 붙일 수 있으므로 Brotli 가 맞다. 그 선택을
        /// 사람에게 남기되, 기본값은 **지금 바로 열어 볼 수 있는 쪽**으로 둔다.
        public WebGLCompressionFormat WebGlCompression { get; set; } = WebGLCompressionFormat.Disabled;

        /// <summary>이 빌드가 붙을 환경. 선택은 <see cref="NVEnvironmentSelection"/> 이 갖는다.</summary>
        ///
        /// 환경을 여기 복사해 두지 않는다. 에디터의 Play 모드도 같은 선택을 읽으므로
        /// (`NVEnvironment.Active`), 저장소가 둘이면 "창에서는 dev 인데 Play 는 local"
        /// 같은 상태가 만들어진다.
        public NVEnvironment Environment => NVEnvironment.Active;

        public BuildTarget Target =>
            Platform == BuildPlatform.WebGL ? BuildTarget.WebGL : BuildTarget.StandaloneWindows64;

        public BuildTargetGroup TargetGroup =>
            Platform == BuildPlatform.WebGL ? BuildTargetGroup.WebGL : BuildTargetGroup.Standalone;

        /// <summary>지금 에디터가 이 플랫폼에 맞춰져 있는가.</summary>
        ///
        /// 아니라면 빌드가 플랫폼을 전환하며 애셋을 전부 다시 임포트한다 — 수 분이 걸리고
        /// 그동안 진행바만 돌아 멈춘 것처럼 보인다. 창이 이 값으로 미리 경고한다.
        public bool MatchesActiveTarget => EditorUserBuildSettings.activeBuildTarget == Target;

        /// <summary>`Builds/{환경}/{플랫폼}`. 환경이 경로에 들어가야 두 빌드물을 나란히 둘 수 있다.</summary>
        public string OutputDirectory =>
            Path.Combine(OutputRoot, Environment.Id, Platform.ToString());

        /// <summary>빌드가 만들 것의 경로. 스탠드얼론은 exe, WebGL 은 폴더다.</summary>
        public string OutputPath =>
            Platform == BuildPlatform.WebGL
                ? OutputDirectory
                : Path.Combine(OutputDirectory, "NVClient.exe");

        /// <summary>실행할 수 있는 결과물인가. WebGL 은 브라우저가 필요하므로 여기서 제외된다.</summary>
        public bool CanLaunch => Platform != BuildPlatform.WebGL;

        public static BuildSelection Load()
        {
            return new BuildSelection
            {
                Platform = (BuildPlatform)EditorPrefs.GetInt(PlatformKey, (int)BuildPlatform.Windows64),
                Development = EditorPrefs.GetBool(DevelopmentKey, true),
                LaunchAfterBuild = EditorPrefs.GetBool(LaunchKey, true),
                InstanceCount = EditorPrefs.GetInt(InstancesKey, 2),
                WindowWidth = EditorPrefs.GetInt(WidthKey, 1280),
                WindowHeight = EditorPrefs.GetInt(HeightKey, 720),
                WebGlCompression = (WebGLCompressionFormat)EditorPrefs.GetInt(
                    CompressionKey, (int)WebGLCompressionFormat.Disabled),
            };
        }

        public void Save()
        {
            EditorPrefs.SetInt(PlatformKey, (int)Platform);
            EditorPrefs.SetBool(DevelopmentKey, Development);
            EditorPrefs.SetBool(LaunchKey, LaunchAfterBuild);
            EditorPrefs.SetInt(InstancesKey, InstanceCount);
            EditorPrefs.SetInt(WidthKey, WindowWidth);
            EditorPrefs.SetInt(HeightKey, WindowHeight);
            EditorPrefs.SetInt(CompressionKey, (int)WebGlCompression);
        }
    }

    /// 만들 수 있는 플레이어.
    ///
    /// `BuildTarget` 을 그대로 쓰지 않는다. 그 열거형에는 이 프로젝트가 만들지 않는
    /// 값이 수십 개 있고, 창의 라디오 버튼이 그것을 전부 보여줄 이유가 없다.
    public enum BuildPlatform
    {
        Windows64 = 0,
        WebGL = 1,
    }
}
