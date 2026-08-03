using System.Collections.Generic;
using System.IO;
using NV.Client.Config;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NV.Client.EditorTools
{
    /// 실제로 빌드하는 곳. 창도 메뉴도 여기만 호출한다.
    ///
    /// **이 클래스는 대화창을 띄우지 않는다.** 전부 로그로 말하고 `bool` 로 답한다.
    /// 이유가 둘이다. 하나는 배치모드에는 사람이 없어 모달이 그대로 멈춤이 되는 것이고,
    /// 다른 하나는 MCP 커맨드 안에서 `EditorUtility.DisplayDialog` 가 "User interactions
    /// are not supported" 로 실패하며 커맨드를 중간에 죽이는 것이다. 물어볼 일은 이것을
    /// 부르는 메뉴가 먼저 묻는다.
    ///
    /// 저장되지 않은 씬도 같은 이유로 묻지 않는다. 빌드는 씬 **파일**을 읽으므로 저장하지
    /// 않은 편집은 그냥 들어가지 않고, 그 사실을 씬 이름과 함께 경고로 적는 것이 모달보다
    /// 쓸모 있다.
    public static class BuildRunner
    {
        /// 선택된 환경이 빌드에 구워지는 자리.
        ///
        /// `Resources` 안에 있어야 런타임이 `Resources.Load` 로 읽을 수 있다.
        /// `StreamingAssets` + JSON 을 쓰지 않는 이유는 WebGL 에서 그 읽기가 비동기가
        /// 되어 부팅 순서를 건드리기 때문이다.
        private const string BakedEnvironmentPath = "Assets/Resources/NVEnvironment.asset";

        /// <summary>선택대로 빌드한다. 성공하면 true.</summary>
        public static bool Run(BuildSelection selection)
        {
            if (selection == null)
            {
                Debug.LogError("[NV] 빌드 선택이 없다.");
                return false;
            }

            if (!CheckPreconditions())
            {
                return false;
            }

            var environment = selection.Environment;

            if (!Validate(selection, environment))
            {
                return false;
            }

            var scenes = ScenesToBuild();

            if (scenes.Length == 0)
            {
                Debug.LogError(
                    "[NV] 빌드 설정에 켜져 있는 씬이 없다. "
                    + "Tools ▸ NV ▸ Scene ▸ Create Main Lobby Scene 을 먼저 실행한다.");
                return false;
            }

            WarnAboutDirtyScenes();
            WarnIfEntrySceneIsNotLobby(scenes);

            if (!BakeEnvironment(environment))
            {
                return false;
            }

            if (!SwitchTargetIfNeeded(selection))
            {
                return false;
            }

            Directory.CreateDirectory(selection.OutputDirectory);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = selection.OutputPath,
                target = selection.Target,
                options = BuildOptions(selection),
            };

            Debug.Log(
                "[NV] 빌드 시작: " + selection.Platform + " · 환경 " + environment.Id
                + " (" + environment.BaseUrl + ") · 씬 " + scenes.Length + "개\n"
                + selection.OutputPath);

            BuildSummary summary;
            var restoreCompression = PlayerSettings.WebGL.compressionFormat;

            try
            {
                ApplyWebGlSettings(selection);
                summary = BuildPipeline.BuildPlayer(options).summary;
            }
            finally
            {
                // 프로젝트 설정을 빌린 것은 반드시 갚는다. 갚지 않으면 이 한 번의 빌드가
                // 프로젝트 설정을 영구히 바꾸고, 그 커밋이 남의 빌드를 조용히 바꾼다.
                if (selection.Platform == BuildPlatform.WebGL)
                {
                    PlayerSettings.WebGL.compressionFormat = restoreCompression;
                }
            }

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError(
                    "[NV] 빌드 실패: " + summary.result + ", 에러 " + summary.totalErrors + "개. "
                    + "Build 창의 로그를 확인한다.");
                return false;
            }

            var megabytes = (summary.totalSize / (1024ul * 1024ul)).ToString();
            var seconds = summary.totalTime.TotalSeconds.ToString("F0");

            Debug.Log(
                "[NV] 빌드 완료: " + summary.outputPath + "\n"
                + megabytes + "MB, " + seconds + "초 · 환경 " + environment.Id
                + " → " + environment.BaseUrl);

            return true;
        }

        /// <summary>빌드한 뒤 인스턴스를 띄운다. 창의 "빌드 후 실행" 이 이것을 부른다.</summary>
        public static bool RunAndLaunch(BuildSelection selection)
        {
            if (!Run(selection))
            {
                return false;
            }

            if (!selection.CanLaunch)
            {
                LogWebGlNextSteps(selection);
                return true;
            }

            PlayerLaunchService.Launch(selection);
            return true;
        }

        // ==================================================== 단계

        private static bool CheckPreconditions()
        {
            // 플레이 중의 에디터 편집은 조용히 버려진다. 환경을 굽는 것도 그 편집에 든다.
            if (Application.isPlaying)
            {
                Debug.LogError("[NV] 플레이 모드를 먼저 나간다 — 플레이 중의 에디터 편집은 버려진다.");
                return false;
            }

            if (EditorApplication.isCompiling)
            {
                Debug.LogError("[NV] 스크립트 컴파일이 끝난 뒤에 다시 실행한다.");
                return false;
            }

            return true;
        }

        /// 빌드를 막는 사유. **하나뿐이다.**
        ///
        /// 원격 호스트를 평문으로 가리키는 조합은 접속이 원리적으로 불가능한 빌드다 —
        /// HTTPS 로 서비스되는 페이지에서 `ws://` 는 mixed content 로 차단되고, 그 실패는
        /// 로컬에서 재현되지 않는다. 나머지는 경고만 하고 통과시킨다. 도구가 사람을
        /// 가로막기 시작하면 사람이 도구를 우회한다.
        private static bool Validate(BuildSelection selection, NVEnvironment environment)
        {
            if (!environment.IsInsecureRemote)
            {
                return true;
            }

            Debug.LogError(
                "[NV] 환경 '" + environment.Id + "' 이 원격 호스트(" + environment.Host
                + ")를 평문으로 가리킨다. 이 빌드는 접속하지 못한다 — HTTPS 페이지의 ws:// 는 "
                + "브라우저가 차단한다. 환경 애셋의 secure 를 켠다.");

            return false;
        }

        /// 빌드에 넣을 씬. **`EditorBuildSettings` 의 순서를 그대로 쓴다.**
        ///
        /// 목록을 두 벌로 두면 반드시 어긋난다. 예전 `TestClientBuild` 는 `MultiplayerTest`
        /// 하나를 하드코딩해 넣었고, 그래서 계측용 빌드의 0번 씬이 제품의 진입 씬과
        /// 달라졌다 — 두 클라이언트가 제품과 다른 화면으로 뜨고 그 차이는 화면에 아무
        /// 단서도 남기지 않는다.
        ///
        /// 씬을 더 넣어도 빌드는 거의 무거워지지 않는다. 이 프로젝트의 씬은 레벨을 담고
        /// 있지 않고 — 지형도 플레이어도 `Awake` 에서 코드로 만들어진다 — 실제로 담긴
        /// 것은 카메라와 컴포넌트 몇 개뿐이다.
        ///
        /// `SessionSceneRouter` 가 룸의 맵으로 씬을 고르므로 대상 씬들도 함께 들어가야
        /// 한다. 빠지면 매치 시작 순간 씬 로드가 실패한다.
        private static string[] ScenesToBuild()
        {
            var all = EditorBuildSettings.scenes;
            var paths = new List<string>(all.Length);

            for (var index = 0; index < all.Length; index++)
            {
                if (all[index].enabled && File.Exists(all[index].path))
                {
                    paths.Add(all[index].path);
                }
            }

            return paths.ToArray();
        }

        private static void WarnAboutDirtyScenes()
        {
            for (var index = 0; index < EditorSceneManager.sceneCount; index++)
            {
                var scene = EditorSceneManager.GetSceneAt(index);

                if (scene.isDirty)
                {
                    Debug.LogWarning(
                        "[NV] 씬 '" + scene.name + "' 에 저장하지 않은 편집이 있다. "
                        + "빌드는 저장된 파일을 읽으므로 그 편집은 들어가지 않는다.");
                }
            }
        }

        /// 0번 씬은 빌드가 처음 여는 씬이다. 그 자리가 메인 로비의 자리다.
        ///
        /// 막지는 않는다 — 진입 씬을 일부러 바꿔 뽑는 경우가 있다. 다만 조용히 넘어가면
        /// 안 된다. 이 증상은 빌드를 실행해야 보이고, 저장소에는 그것을 잡아 줄 테스트가 없다.
        private static void WarnIfEntrySceneIsNotLobby(string[] scenes)
        {
            if (scenes.Length > 0 && !scenes[0].EndsWith("/MainLobby.unity"))
            {
                Debug.LogWarning(
                    "[NV] 진입 씬(0번)이 MainLobby 가 아니다: " + scenes[0] + "\n"
                    + "Tools ▸ NV ▸ Scene ▸ Create Main Lobby Scene 이 그 자리를 다시 잡아 준다.");
            }
        }

        /// 선택된 환경을 `Resources` 안으로 옮긴다. 이것이 빌드와 런타임을 잇는 유일한 줄이다.
        ///
        /// `CopyAsset` 이 아니라 `CopySerialized` 를 쓴다. `CopyAsset` 은 목적지가 이미
        /// 있으면 실패하고, 그것을 지우려면 `AssetDatabase.DeleteAsset` 이 필요한데 그
        /// 호출은 MCP 커맨드 안에서 실패하며 커맨드를 중간에 죽인다. 같은 애셋에 값만
        /// 덮어쓰면 지울 일이 없다.
        ///
        /// 빌드가 끝난 뒤 이 파일을 지우지 않는다. 에디터는 이것을 읽지 않고
        /// (`NVEnvironment.Active` 가 `EditorPrefs` 의 선택을 먼저 본다) `.gitignore` 가
        /// 무시하므로, 남겨 두면 "마지막으로 어느 환경을 구웠는지" 가 파일로 남는다.
        private static bool BakeEnvironment(NVEnvironment environment)
        {
            var sourcePath = AssetDatabase.GetAssetPath(environment);

            if (string.IsNullOrEmpty(sourcePath))
            {
                Debug.LogError(
                    "[NV] 지금 환경이 애셋이 아니다. "
                    + NVEnvironment.AssetFolder + " 에 환경을 만들고 "
                    + "Tools ▸ NV ▸ Environment 에서 고른다.");
                return false;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            var baked = AssetDatabase.LoadAssetAtPath<NVEnvironment>(BakedEnvironmentPath);

            if (baked == null)
            {
                baked = ScriptableObject.CreateInstance<NVEnvironment>();
                AssetDatabase.CreateAsset(baked, BakedEnvironmentPath);
            }

            EditorUtility.CopySerialized(environment, baked);

            // `CopySerialized` 는 이름까지 복사한다. 파일 이름과 어긋난 채로 두면
            // 나중에 애셋을 열어 본 사람이 어느 쪽이 정본인지 헷갈린다.
            baked.name = Path.GetFileNameWithoutExtension(BakedEnvironmentPath);

            EditorUtility.SetDirty(baked);
            AssetDatabase.SaveAssets();

            Debug.Log("[NV] 환경 '" + environment.Id + "' 을 빌드에 구웠다: " + BakedEnvironmentPath);
            return true;
        }

        /// WebGL 빌드가 실제로 열리게 하는 설정. 압축 하나뿐이다.
        ///
        /// 나머지 WebGL 설정(스트리핑, 예외 처리, 템플릿)은 손대지 않는다. 프로젝트 설정을
        /// 만지는 것은 되돌릴 책임을 지는 일이고, 지금 되돌릴 값을 하나로 유지할 만큼만
        /// 만진다. 필요해지면 그때 하나씩 늘린다.
        private static void ApplyWebGlSettings(BuildSelection selection)
        {
            if (selection.Platform != BuildPlatform.WebGL)
            {
                return;
            }

            PlayerSettings.WebGL.compressionFormat = selection.WebGlCompression;

            Debug.Log("[NV] WebGL 압축 = " + selection.WebGlCompression
                + " (빌드가 끝나면 프로젝트 설정을 되돌린다)");
        }

        /// 브라우저에서 열기까지 남은 일. WebGL 빌드는 파일을 더블클릭해서 열 수 없다.
        ///
        /// `file://` 로 열면 브라우저가 fetch 를 차단해 로딩바에서 멈춘다. 그 증상은
        /// 빌드 실패처럼 보이므로 여기서 미리 말해 둔다.
        private static void LogWebGlNextSteps(BuildSelection selection)
        {
            var full = Path.GetFullPath(selection.OutputDirectory);
            var compressed = selection.WebGlCompression != WebGLCompressionFormat.Disabled;

            var message =
                "[NV] WebGL 빌드는 정적 서버로 열어야 한다 — file:// 로 열면 로딩바에서 멈춘다.\n"
                + "  cd \"" + full + "\"\n"
                + "  python -m http.server 8080   →  http://localhost:8080\n"
                + "초대 링크는 페이지 주소에 ?code=XXXXXX 를 붙인 형태다(InviteLink).";

            if (compressed)
            {
                message +=
                    "\n⚠ 압축이 " + selection.WebGlCompression + " 이다. 평범한 정적 서버는 "
                    + "Content-Encoding 헤더를 붙이지 않으므로 브라우저가 읽지 못한다 — "
                    + "로컬에서 열어 볼 빌드라면 압축을 Disabled 로 두고 다시 뽑는다.";
            }

            Debug.Log(message);
        }

        private static bool SwitchTargetIfNeeded(BuildSelection selection)
        {
            if (selection.MatchesActiveTarget)
            {
                return true;
            }

            Debug.Log(
                "[NV] 플랫폼을 " + selection.Target + " 로 전환한다. 애셋을 다시 임포트하므로 "
                + "처음에는 수 분이 걸린다 — 멈춘 것이 아니다.");

            if (EditorUserBuildSettings.SwitchActiveBuildTarget(selection.TargetGroup, selection.Target))
            {
                return true;
            }

            Debug.LogError(
                "[NV] " + selection.Target + " 로 전환하지 못했다. "
                + "그 플랫폼 모듈이 설치되어 있는지 Unity Hub 에서 확인한다.");

            return false;
        }

        /// 개발 빌드로 만들면 로그가 남고 빌드가 빠르다.
        private static BuildOptions BuildOptions(BuildSelection selection)
        {
            return selection.Development
                ? UnityEditor.BuildOptions.Development | UnityEditor.BuildOptions.AllowDebugging
                : UnityEditor.BuildOptions.None;
        }
    }
}
