using NV.Client.Map;
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

        /// 대기방 씬. 방에 들어가 있고 매치가 시작되지 않았을 때 여기다.
        public const string GameLobbyScene = MapSceneTable.GameLobbyScene;

        private NetSession _session;

        /// 이 라우터가 게임 씬을 한 번이라도 열었는가.
        ///
        /// 매치가 끝나 대기방으로 돌아올 때 내려간다 — 내리지 않으면 두 번째 매치에서 게임
        /// 씬이 다시 열리지 않는다.
        private bool _enteredGame;

        /// 대기방 씬을 열 수 없다고 이미 말했는가. 같은 오류를 프레임마다 쌓지 않기 위한 것이다.
        private bool _gameLobbyMissing;

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
                case SessionState.InLobby:
                case SessionState.Ended:
                    EnterGameLobby();
                    break;

                case SessionState.InGame:
                    EnterGame();
                    break;

                case SessionState.Idle:
                case SessionState.Failed:
                    ReturnToLobby();
                    break;
            }
        }

        /// 방에 들어갔다. 대기방 씬으로.
        ///
        /// **`Ended` 도 여기다.** 매치가 끝나면 결과를 보고 방장이 로비로 되돌릴 수 있어야
        /// 하고, 그 화면은 대기방이 갖는다.
        ///
        /// 이미 그 씬이면 아무것도 하지 않는다. `Update` 가 방에 있는 동안 계속 이 함수를
        /// 부르므로 판정을 앞에 두지 않으면 매 프레임 같은 씬을 다시 로드한다.
        private void EnterGameLobby()
        {
            if (SceneManager.GetActiveScene().name == GameLobbyScene)
            {
                return;
            }

            // **없는 씬을 로드하면 조용히 실패한다.** 로그도 예외도 없고, 증상은 방을 만든 뒤
            // 아무 일도 일어나지 않는 것뿐이다. 대기방 씬은 생성기가 만드는 씬이므로
            // (`Tools ▸ NV ▸ Scene ▸ Create Game Lobby Scene`) 아직 만들지 않았거나 빌드
            // 설정에서 빠진 상태가 실제로 있을 수 있다.
            if (!Application.CanStreamedLevelBeLoaded(GameLobbyScene))
            {
                // 한 번만 남긴다. `Update` 가 방에 있는 동안 계속 이 함수를 부르므로,
                // 표식이 없으면 프레임마다 같은 오류가 쌓여 콘솔이 그것으로 덮인다.
                if (!_gameLobbyMissing)
                {
                    _gameLobbyMissing = true;

                    Debug.LogError(
                        $"[NV] 대기방 씬 '{GameLobbyScene}' 을 열 수 없다. "
                        + "Tools ▸ NV ▸ Scene ▸ Create Game Lobby Scene 으로 만들고 "
                        + "빌드 설정에 등록되었는지 확인한다.");
                }

                return;
            }

            // 게임 씬에서 방으로 돌아온 경우다. 다음 매치가 시작되면 다시 열어야 하므로
            // 이 표식을 내린다 — 내리지 않으면 두 번째 매치에서 씬이 바뀌지 않는다.
            _enteredGame = false;

            SceneManager.LoadScene(GameLobbyScene);
        }

        private void EnterGame()
        {
            // **이미 들어와 있으면 아무것도 묻지 않는다.** `Update` 는 매치가 도는 동안 계속
            // 이 함수를 부르고, `SceneFor` 는 카탈로그를 `Resources.Load` 로 찾아 훑는다 —
            // 판정 순서를 뒤집으면 그 조회가 매 프레임 돈다. 답이 필요한 것은 씬을 바꿀지
            // 정하는 한 번뿐이다.
            if (_enteredGame)
            {
                return;
            }

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

        /// 방을 벗어났다(스스로 나갔거나 실패했다). 메인 로비로.
        ///
        /// **`_enteredGame` 을 보지 않는다.** 예전에는 그것으로 걸렀는데, 그때는 방에 들어가
        /// 있는 동안에도 씬이 메인 로비였기 때문이다. 지금은 대기방이 별개의 씬이라 매치를
        /// 한 번도 시작하지 않고 나가는 길이 흔하고, 그 경우 이 표식은 거짓이다 — 그대로
        /// 두면 대기방 씬에 갇힌다.
        ///
        /// 이 컴포넌트가 붙는 순간(메인 로비의 대기 상태)에 다시 로드하지 않는 것은 씬 이름
        /// 판정이 이미 막는다.
        private void ReturnToLobby()
        {
            if (SceneManager.GetActiveScene().name == LobbyScene)
            {
                return;
            }

            _enteredGame = false;
            SceneManager.LoadScene(LobbyScene);
        }

        /// 이 맵을 여는 씬.
        ///
        /// **순서가 이 함수의 내용이다.**
        ///
        /// 1. 카탈로그가 이 맵에 전용 씬을 적어 두었으면 그것. 베이크가 `MapSceneTable` 에서
        ///    읽어 적으므로 표와 갈리지 않는다.
        /// 2. 표에 짝이 있으면 그것. 카탈로그가 없는 빌드에서도 **오늘의 동작이 그대로 유지되는
        ///    자리다** — 이 줄이 없으면 카탈로그를 굽기 전의 빌드가 아무 맵도 열지 못한다.
        /// 3. 카탈로그에 프리팹이 있으면 공용 런타임 씬. 새로 굽힌 맵이 이 길로 열린다.
        /// 4. 아무것도 없으면 빈 문자열 — 호출자가 어느 맵이 빠졌는지 남긴다.
        ///
        /// 예외를 던지지 않는다. 서버는 클라이언트가 모르는 맵을 물린 룸을 열 수 있고, 그때는
        /// "그 맵의 씬을 모른다" 를 사람에게 말해야 한다.
        private static string SceneFor(string mapName)
        {
            if (string.IsNullOrEmpty(mapName))
            {
                return string.Empty;
            }

            var entry = MapCatalog.Load()?.Find(mapName);

            if (entry != null && !string.IsNullOrEmpty(entry.sceneOverride))
            {
                return entry.sceneOverride;
            }

            var paired = MapSceneTable.SceneFor(mapName);

            if (!string.IsNullOrEmpty(paired))
            {
                return paired;
            }

            return entry != null && entry.prefab != null ? MapSceneTable.RuntimeScene : string.Empty;
        }
    }
}
