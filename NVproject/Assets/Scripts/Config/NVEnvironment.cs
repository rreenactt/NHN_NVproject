using UnityEngine;

namespace NV.Client.Config
{
    /// 이 빌드가 어느 서버에 붙는가. 그리고 그 서버에 맞춰 화면이 무엇을 허용하는가.
    ///
    /// 예전에는 접속 대상이 세 곳에 있었다 — `PlayerProfile.DefaultHost` 상수, 씬에
    /// 직렬화된 `NetSession.host`, 그리고 `PlayerPrefs`. 이 프로젝트의 규칙대로 새로
    /// 붙인 컴포넌트는 그 시점의 기본값을 씬에 굽기 때문에, `.cs` 의 기본값을 고쳐도
    /// 저장된 씬은 옛 주소를 계속 들고 있었다. 세 곳이 조용히 어긋나고, 그 어긋남은
    /// 빌드를 실행해 접속해 봐야 보인다.
    ///
    /// 그래서 접속 대상은 **애셋 하나**가 소유하고, 나머지는 그것을 읽는다.
    ///
    /// **결정 순서** (`PlayerProfile.Host` 가 이 순서로 답한다):
    ///
    /// 1. `PlayerPrefs` — 단, 저장된 환경 id 가 지금 빌드의 id 와 같고 이 환경이
    ///    `allowHostOverride` 를 켜 두었을 때만.
    /// 2. 이 애셋의 `host`. 빌드에 구워진 기본값이다.
    /// 3. 애셋을 찾지 못하면 로컬 폴백 + 경고 로그. 에디터에서만 도달할 수 있다.
    ///
    /// 1번의 "같은 환경일 때만" 이 실제 버그 하나를 막는다. `PlayerPrefs` 는 빌드를
    /// 갈아도 그 기기에 남으므로, 로컬 서버에 한 번 붙어 본 기기에 배포 빌드를 깔면
    /// 배포 빌드가 `localhost` 를 가리킨다. 증상은 "서버 응답 없음" 뿐이고 어디에도
    /// 단서가 없다. 환경마다 다른 서랍(`nv.{id}.lobby.host`)을 쓰면 새어 나올 수 없다.
    ///
    /// 이 타입은 **`Assets/Editor/` 밖에 있어야 한다.** 런타임이 읽는 타입이므로
    /// 에디터 어셈블리에 넣으면 빌드에서 통째로 사라진다.
    [CreateAssetMenu(menuName = "NV/Environment", fileName = "NVEnvironment")]
    public sealed class NVEnvironment : ScriptableObject
    {
        /// 빌드에 구워진 환경이 `Resources` 안에서 갖는 이름.
        ///
        /// 빌드 직전에 선택된 환경이 `Assets/Resources/NVEnvironment.asset` 으로
        /// 복사되고, 런타임은 이 이름으로 그것을 읽는다. `StreamingAssets` JSON 을
        /// 쓰지 않는 이유는 WebGL 에서 그 읽기가 비동기가 되어 부팅 순서를 건드리기
        /// 때문이다 — 이 프로젝트가 UI 를 `Resources/UI/` 에 두는 것과 같은 이유다.
        public const string ResourceName = "NVEnvironment";

        /// 환경 애셋이 사는 곳. Build Manager 가 목록을 여기서 읽는다.
        public const string AssetFolder = "Assets/Settings/Environments";

        /// 에디터에서 지금 고른 환경의 애셋 경로가 `EditorPrefs` 에 이 키로 남는다.
        ///
        /// 에디터에는 구워진 사본이 없으므로 Play 모드는 이 선택을 따른다. 키 문자열을
        /// 창과 런타임이 나눠 갖지 않도록 여기 한 곳에 둔다.
        public const string EditorSelectionKey = "nv.build.environment.path";

        public const string FallbackId = "local";
        public const string FallbackHost = "localhost:5202";

        [Tooltip("소문자 식별자. 출력 경로와 PlayerPrefs 키의 접두어가 된다.")]
        [SerializeField] private string id = FallbackId;

        [Tooltip("창과 화면에 보이는 이름.")]
        [SerializeField] private string displayName = "로컬";

        [Tooltip("host:port. 로컬 개발 서버는 dotnet run --project Api 의 5202 포트다.")]
        [SerializeField] private string host = FallbackHost;

        [Tooltip("wss / https 를 쓴다. 배포 환경에서는 반드시 켠다 — HTTPS 페이지의 "
                 + "ws:// 는 mixed content 로 차단되고, 그 실패는 로컬에서 재현되지 않는다.")]
        [SerializeField] private bool secure;

        [Tooltip("로비 설정 팝업에서 서버 주소를 바꿀 수 있게 한다. 로컬·개발은 켜고 배포는 끈다.")]
        [SerializeField] private bool allowHostOverride = true;

        [Tooltip("MatchBootstrap 의 디버그 키(F1/F2/F5)를 허용한다. 배포 환경에서는 끈다.")]
        [SerializeField] private bool allowDebugKeys = true;

        private static NVEnvironment _active;

        /// <summary>지금 이 실행이 쓰는 환경. 없으면 로컬 폴백을 만들고 경고한다.</summary>
        ///
        /// 도메인 리로드는 static 필드를 지운다. 이 프로퍼티는 그때 다시 찾으므로
        /// 스크립트를 고친 뒤에도 값이 살아 있다 — 이 프로젝트의 `MatchManager.Instance`,
        /// `NetSession.Current` 와 같은 형태다.
        public static NVEnvironment Active
        {
            get
            {
                if (_active != null)
                {
                    return _active;
                }

                _active = Resolve();
                return _active;
            }
        }

        public string Id => Clean(id, FallbackId);

        public string DisplayName => Clean(displayName, Id);

        public string Host => Clean(host, FallbackHost);

        public bool Secure => secure;

        public bool AllowHostOverride => allowHostOverride;

        public bool AllowDebugKeys => allowDebugKeys;

        /// <summary>`https://` 또는 `http://` 가 붙은 API 주소.</summary>
        public string BaseUrl => (secure ? "https://" : "http://") + Host;

        /// <summary>원격 서버를 평문으로 가리키는 조합인가. 빌드를 막는 유일한 사유다.</summary>
        ///
        /// HTTPS 로 서비스되는 페이지에서 `ws://` 는 차단되므로, 이 조합으로 뽑은
        /// WebGL 빌드는 접속이 원리적으로 불가능하다. `localhost` 는 예외다 —
        /// 로컬 개발 서버는 평문으로 뜨고 브라우저도 로컬을 안전한 출처로 본다.
        public bool IsInsecureRemote => !secure && !IsLoopback(Host);

        public static bool IsLoopback(string hostPort)
        {
            if (string.IsNullOrEmpty(hostPort))
            {
                return false;
            }

            var colon = hostPort.IndexOf(':');
            var name = colon < 0 ? hostPort : hostPort.Substring(0, colon);

            return name == "localhost" || name == "127.0.0.1" || name == "::1" || name == "[::1]";
        }

        /// <summary>다음 읽기에서 환경을 다시 찾게 한다. 창이 선택을 바꿀 때 부른다.</summary>
        public static void Invalidate()
        {
            _active = null;
        }

        private static NVEnvironment Resolve()
        {
#if UNITY_EDITOR
            // 에디터에는 구워진 사본이 없다. 창이 고른 애셋을 그대로 쓴다.
            var selected = UnityEditor.EditorPrefs.GetString(EditorSelectionKey, string.Empty);

            if (!string.IsNullOrEmpty(selected))
            {
                var picked = UnityEditor.AssetDatabase.LoadAssetAtPath<NVEnvironment>(selected);
                if (picked != null)
                {
                    return picked;
                }
            }

            // 아직 아무것도 고르지 않았으면 기본 환경을 찾는다.
            var fallbackPath = AssetFolder + "/" + FallbackId + ".asset";
            var local = UnityEditor.AssetDatabase.LoadAssetAtPath<NVEnvironment>(fallbackPath);

            if (local != null)
            {
                return local;
            }
#endif

            var baked = Resources.Load<NVEnvironment>(ResourceName);

            if (baked != null)
            {
                return baked;
            }

            // 여기 오는 것은 빌드에 환경을 굽지 않았다는 뜻이다. 접속은 로컬로 시도하되
            // 조용히 넘어가지 않는다 — 배포 빌드가 localhost 를 두드리는 증상은
            // "서버 응답 없음" 하나로만 보이므로, 이유가 로그에 남아 있어야 한다.
            Debug.LogWarning(
                "[NV] 환경 애셋이 없다. " + FallbackHost + " (평문) 으로 폴백한다. "
                + "Tools ▸ NV ▸ Build Manager 에서 환경을 골라 빌드한다.");

            var synthetic = CreateInstance<NVEnvironment>();
            synthetic.name = FallbackId + " (fallback)";
            return synthetic;
        }

        private static string Clean(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
