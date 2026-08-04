using UnityEngine;
using UnityEngine.SceneManagement;

namespace NV.Client.Net.Session
{
    /// 세션 단계에 따라 씬을 오간다. 세션 오브젝트에 붙어 씬보다 오래 산다.
    ///
    /// 로비에서 들어온 경우에만 존재한다(`MainLobbyController` 가 붙인다). 개발용 씬에서
    /// 바로 시작한 경우에는 돌아갈 로비가 없으므로 이 컴포넌트도 없다 — 있으면
    /// MultiplayerTest 를 열자마자 대기 상태를 보고 로비로 튕겨 버린다.
    ///
    /// 어느 씬을 열지는 룸의 맵으로 정한다. 서버가 룸마다 다른 맵을 물릴 수 있고,
    /// 클라이언트가 그 맵과 다른 씬을 열면 증상이 맵 해시 불일치 하나로만 나타난다.
    public sealed class SessionSceneRouter : MonoBehaviour
    {
        /// 로비 씬 이름. 표는 `MapSceneTable` 에 있다.
        ///
        /// **표를 여기에 두지 않는 이유.** 배치 export 도 같은 짝을 알아야 하고(어느 씬을 열어야
        /// 그 맵이 나오는가), 표가 둘이면 갈린다. 이 표가 갈리는 방식은 특히 조용하다 — 맵 A 를
        /// 보고 씬 B 를 열면 그 씬은 다른 지형을 만들고, 증상은 맵 해시 불일치 하나다.
        public const string LobbyScene = MapSceneTable.LobbyScene;

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
            return MapSceneTable.SceneFor(mapName);
        }
    }
}
