using System.Collections.Generic;
using NV.Client.Config;
using UnityEditor;
using UnityEngine;

// 폴더 이름이 `Build` 가 아니라 `BuildManager` 인 것은 취향이 아니다. `.gitignore` 의
// 표준 Unity 줄 `[Bb]uild/` 는 트리 **어디에 있든** 그 이름의 폴더를 잡으므로,
// `Assets/Editor/Build/` 에 둔 스크립트는 커밋되지 않는다. 폴더의 `.meta` 는 파일이라
// 살아남기 때문에 증상이 더 나쁘다 — 남의 작업 폴더에는 내용 없는 폴더 등록만 들어간다.
// 그 규칙은 빌드 출력물을 막는 것이므로 약화시키지 않고 이름을 피한다.
//
// 네임스페이스도 `...EditorTools.Build` 로 두지 않는다. `UnityEditor.Build` 가 있어
// 이 안의 코드에서 `Build.Reporting.BuildReport` 같은 이름이 어느 쪽인지 애매해진다 —
// `TestClientBuild.cs` 가 `System.Diagnostics.Debug` 로 이미 한 번 겪은 종류의 충돌이다.
namespace NV.Client.EditorTools
{
    /// 에디터에서 지금 고른 환경. Build Manager 창과 Play 모드가 같은 값을 본다.
    ///
    /// 빌드된 플레이어는 `Resources/NVEnvironment.asset` 을 읽지만 에디터에는 그 사본이
    /// 없다. 그래서 선택은 `EditorPrefs` 에 남고, <see cref="NVEnvironment.Active"/> 가
    /// 그것을 읽는다 — 키 문자열은 런타임 쪽 상수 하나(`EditorSelectionKey`)를 함께 쓴다.
    ///
    /// `EditorPrefs` 는 사람별이고 커밋되지 않는다. 그것이 맞다. 내가 개발 서버를 보고
    /// 있다는 사실이 남의 작업 폴더에 들어갈 이유가 없다.
    ///
    /// 이 클래스는 창이 없는 동안의 유일한 전환 경로이면서, 창이 생긴 뒤에도 그 창이
    /// 쓰는 저장소다. 창에 상태를 두면 도메인 리로드에 날아간다.
    public static class NVEnvironmentSelection
    {
        /// <summary>선택이 바뀌었다. 열려 있는 창이 자기 화면을 다시 만든다.</summary>
        ///
        /// 창이 이것을 듣지 않으면, 메뉴로 환경을 바꿨을 때 이미 열려 있는 창은 낡은
        /// 주소를 계속 보여준다 — 창이 자기 화면을 다시 만드는 계기는 포커스를 얻는
        /// 것뿐이고, 메뉴를 누르는 동안 창은 이미 포커스를 갖고 있다. 창이 dev 를
        /// 보여주면서 빌드가 local 을 굽는 상태가 이 한 줄로 막힌다.
        ///
        /// 방향은 창 → 선택이 아니라 **선택 → 창**이다. 이 클래스가 창의 타입을 알면
        /// 배치모드에서 쓸 수 없게 된다.
        public static event System.Action Changed;

        /// <summary>지금 고른 환경의 애셋 경로. 비어 있으면 기본 환경을 쓴다.</summary>
        public static string Path
        {
            get => EditorPrefs.GetString(NVEnvironment.EditorSelectionKey, string.Empty);

            set
            {
                EditorPrefs.SetString(NVEnvironment.EditorSelectionKey, value ?? string.Empty);

                // 이미 읽어 둔 환경을 버린다. 이것을 잊으면 Play 모드가 방금 바꾼
                // 선택이 아니라 처음 읽은 환경으로 계속 접속한다.
                NVEnvironment.Invalidate();

                Changed?.Invoke();
            }
        }

        /// <summary>`Assets/Settings/Environments` 안의 환경 전부. 이름 순.</summary>
        ///
        /// 애셋을 찾아서 목록을 만든다. 메뉴 항목을 환경마다 하나씩 두지 않는 이유가
        /// 이것이다 — 환경을 추가하는 일이 애셋 하나 만드는 일로 끝나야 한다.
        public static List<NVEnvironment> All()
        {
            var found = new List<NVEnvironment>();

            // 없는 폴더를 검색 범위로 주면 `FindAssets` 가 콘솔에 에러를 낸다. 환경
            // 폴더가 아직 없는 저장소에서 창을 여는 것은 정상이므로 조용히 빈 목록을 준다.
            if (!AssetDatabase.IsValidFolder(NVEnvironment.AssetFolder))
            {
                return found;
            }

            var guids = AssetDatabase.FindAssets("t:" + nameof(NVEnvironment), new[] { NVEnvironment.AssetFolder });

            for (var index = 0; index < guids.Length; index++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[index]);
                var environment = AssetDatabase.LoadAssetAtPath<NVEnvironment>(path);

                if (environment != null)
                {
                    found.Add(environment);
                }
            }

            found.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
            return found;
        }

        /// 다음 환경으로 넘긴다.
        ///
        /// 창이 생기기 전까지의 전환 경로다. 메뉴는 항목 이름을 실행 시점에 만들 수
        /// 없으므로 목록을 그리는 대신 순환시키고, 지금 무엇이 잡혔는지 로그에 적는다.
        [MenuItem("Tools/NV/Environment/Switch Environment", priority = 30)]
        public static void SwitchEnvironment()
        {
            var all = All();

            if (all.Count == 0)
            {
                Debug.LogWarning(
                    "[NV] " + NVEnvironment.AssetFolder + " 에 환경 애셋이 없다. "
                    + "Assets ▸ Create ▸ NV ▸ Environment 로 만든다.");
                return;
            }

            var current = Path;
            var next = 0;

            for (var index = 0; index < all.Count; index++)
            {
                if (AssetDatabase.GetAssetPath(all[index]) == current)
                {
                    next = (index + 1) % all.Count;
                    break;
                }
            }

            Path = AssetDatabase.GetAssetPath(all[next]);
            Log();
        }

        [MenuItem("Tools/NV/Environment/Show Current Environment", priority = 31)]
        public static void Log()
        {
            var environment = NVEnvironment.Active;

            Debug.Log(
                $"[NV] 환경 {environment.Id} ({environment.DisplayName}) — {environment.BaseUrl}"
                + $", 주소 변경 {(environment.AllowHostOverride ? "허용" : "잠김")}"
                + $", 디버그 키 {(environment.AllowDebugKeys ? "켬" : "끔")}");
        }
    }
}
