using UnityEngine;
using UnityEngine.SceneManagement;

namespace NV.Client.Net.Session
{
    /// 세션 단계에 따라 씬을 오간다. 세션 오브젝트에 붙어 씬보다 오래 산다.
    ///
    /// 로비에서 들어온 경우에만 존재한다(`LobbyController` 가 붙인다). 개발용 씬에서
    /// 바로 시작한 경우에는 돌아갈 로비가 없으므로 이 컴포넌트도 없다 — 있으면
    /// MultiplayerTest 를 열자마자 대기 상태를 보고 로비로 튕겨 버린다.
    ///
    /// 어느 씬을 열지는 룸의 맵으로 정한다. 서버가 룸마다 다른 맵을 물릴 수 있고,
    /// 클라이언트가 그 맵과 다른 씬을 열면 증상이 맵 해시 불일치 하나로만 나타난다.
    public sealed class SessionSceneRouter : MonoBehaviour
    {
        /// 로비 씬 이름. 매치가 끝나거나 나가면 여기로 돌아온다.
        public const string LobbyScene = "Lobby";

        /// 맵 이름 → 씬 이름.
        ///
        /// 표를 코드에 둔다. 맵을 하나 늘리는 것은 이미 코드(레벨 생성)와 서버 설정을
        /// 함께 건드리는 일이므로, 여기에 한 줄이 더 붙는 것이 흩어지는 것보다 낫다.
        /// 맵 이름은 서버가 로드한 `MapData` 의 이름이며, 클라이언트가 export 한 값이다.
        private static readonly string[,] SceneByMap =
        {
            { "backrooms", "SampleScene" },
            { "test-room", "MultiplayerTest" },
        };

        private NetSession _session;

        /// 이 라우터가 게임 씬을 한 번이라도 열었는가.
        ///
        /// 열지 않았다면 로비로 되돌리지 않는다. 그러지 않으면 이 컴포넌트가 붙은 순간
        /// (아직 아무것도 안 한 대기 상태)에 로비를 다시 로드한다.
        private bool _enteredGame;

        private void Awake()
        {
            _session = GetComponent<NetSession>();
        }

        private void Update()
        {
            if (_session == null)
            {
                return;
            }

            switch (_session.State)
            {
                case SessionState.InGame:
                    EnterGame();
                    break;

                case SessionState.Idle:
                case SessionState.Failed:
                    ReturnToLobby();
                    break;
            }
        }

        private void EnterGame()
        {
            var scene = SceneFor(_session.Room.MapName);

            if (string.IsNullOrEmpty(scene))
            {
                // 조용히 넘기면 로비 화면에 매치가 시작됐다고 뜬 채로 아무 일도
                // 일어나지 않는다. 어느 맵이 빠졌는지 남긴다.
                Debug.LogError(
                    $"[NV] 맵 '{_session.Room.MapName}' 에 대응하는 씬이 없다. " +
                    "SessionSceneRouter 의 표와 Build Settings 를 확인한다.");

                _enteredGame = true;
                return;
            }

            if (SceneManager.GetActiveScene().name == scene)
            {
                _enteredGame = true;
                return;
            }

            _enteredGame = true;
            SceneManager.LoadScene(scene);
        }

        private void ReturnToLobby()
        {
            if (!_enteredGame || SceneManager.GetActiveScene().name == LobbyScene)
            {
                return;
            }

            _enteredGame = false;
            SceneManager.LoadScene(LobbyScene);
        }

        private static string SceneFor(string mapName)
        {
            if (string.IsNullOrEmpty(mapName))
            {
                return string.Empty;
            }

            for (var index = 0; index < SceneByMap.GetLength(0); index++)
            {
                if (string.Equals(SceneByMap[index, 0], mapName, System.StringComparison.Ordinal))
                {
                    return SceneByMap[index, 1];
                }
            }

            return string.Empty;
        }
    }
}
