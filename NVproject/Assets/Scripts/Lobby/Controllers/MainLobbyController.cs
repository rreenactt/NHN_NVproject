using NV.Client.Lobby.Events;
using NV.Client.Lobby.Models;
using NV.Client.Lobby.Services;
using NV.Client.Lobby.UI;
using NV.Client.Net.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace NV.Client.Lobby.Controllers
{
    /// 메인 로비 씬의 유일한 MonoBehaviour.
    ///
    /// 뷰를 MonoBehaviour 로 만들지 않는 이유가 이 클래스의 존재 이유다. `VisualElement`
    /// 는 스크립트 편집이 유발하는 도메인 리로드를 넘기지 못하는데 컴포넌트는 넘긴다 —
    /// 뷰마다 컴포넌트를 두면 살아 있는 컴포넌트가 죽은 요소를 가리키는 반쯤 살아 있는
    /// 객체가 열 개 생긴다. 트리의 생사를 여기서 한 번만 판정하고, 죽었으면 통째로
    /// 다시 만든다.
    [DefaultExecutionOrder(-60)]
    public sealed class MainLobbyController : MonoBehaviour
    {
        private UIDocument _document;

        private VisualElement _root;

        private LobbyModel _model;
        private LobbyEvents _events;
        private LobbyService _lobbyService;
        private RoomService _roomService;
        private MapChoiceService _mapChoices;
        private LobbyUIController _ui;
        private LobbyController _flow;

        private NetSession _session;

        /// 트리가 살아 있는가.
        ///
        /// bool 플래그를 두지 않는다. bool 은 도메인 리로드를 넘어 살아남고
        /// `VisualElement` 는 넘지 못하므로, 전부 null 인 트리를 "빌드됨" 으로 오인해
        /// 화면이 빈 채로 남고 프레임마다 예외가 난다. 게임 HUD 와 옛 로비가 같은
        /// 이유로 같은 패턴을 쓴다.
        private bool TreeIsLive => _root != null && _root.panel != null && _ui != null;

        private void OnEnable()
        {
            _session = NetSession.Current;
            _session.StateChanged += OnSessionChanged;

            // 로비에서 들어왔을 때만 씬 이동을 붙인다. 개발용 씬에서 바로 시작한
            // 경우에는 돌아갈 로비가 없다 — 붙어 있으면 그 씬을 열자마자 대기 상태를
            // 보고 로비로 튕겨 버린다.
            if (_session.GetComponent<SessionSceneRouter>() == null)
            {
                _session.gameObject.AddComponent<SessionSceneRouter>();
            }
        }

        private void OnDisable()
        {
            if (_session != null)
            {
                _session.StateChanged -= OnSessionChanged;
            }

            _roomService?.Stop();
            _lobbyService?.StopWatching();

            Teardown();
        }

        private void Update()
        {
            if (!TreeIsLive)
            {
                Build();
                return;
            }

            _ui.Tick();
        }

        private void Build()
        {
            Teardown();

            var uxml = MainLobbyAssets.Screen();
            var panel = MainLobbyAssets.Panel();

            if (_document == null)
            {
                _document = GetComponent<UIDocument>();

                if (_document == null)
                {
                    _document = gameObject.AddComponent<UIDocument>();
                }
            }

            if (_document.panelSettings == null)
            {
                _document.panelSettings = panel;
            }

            if (uxml == null || _document.panelSettings == null)
            {
                Debug.LogError(
                    $"[MainLobby] Assets/Resources 에 {MainLobbyAssets.ScreenName} 또는 "
                    + $"{MainLobbyAssets.PanelName} 이 없다.");

                enabled = false;
                return;
            }

            _document.visualTreeAsset = null;
            _document.rootVisualElement.Clear();

            _root = uxml.Instantiate();
            _root.style.flexGrow = 1f;

            var style = MainLobbyAssets.Style();

            if (style != null)
            {
                _root.styleSheets.Add(style);
            }

            _document.rootVisualElement.Add(_root);

            // 모델과 이벤트는 트리보다 오래 산다. 도메인 리로드로 화면만 다시 만들 때
            // 받아 둔 방 목록까지 버리면 화면이 이유 없이 비어 보인다.
            _model ??= new LobbyModel();
            _events ??= new LobbyEvents();

            _lobbyService ??= new LobbyService(this, _model, _events);
            _roomService ??= new RoomService(this, _model, _events);

            // 맵 목록도 트리보다 오래 산다. 서버가 다시 뜨기 전까지 변하지 않는 값이므로
            // 도메인 리로드마다 다시 받을 이유가 없다.
            _mapChoices ??= new MapChoiceService(this);

            _ui = new LobbyUIController(_root, _session, _model, _events, _roomService);
            _flow = new LobbyController(_session, _events, _lobbyService, _roomService, _mapChoices, _ui);

            _lobbyService.ApplyStoredProfile();
            _lobbyService.StartWatching();

            _ui.RefreshAll();
            _flow.OnSessionChanged();

            // 화면에 들어온 순간 한 번만 받는다. 이 경로에는 서버 레이트리밋이 없어
            // 주기적으로 두드리면 무방비로 맞는다 — 그 뒤로는 수동 새로고침뿐이다.
            _roomService.Refresh(force: true);
        }

        private void Teardown()
        {
            _ui?.Dispose();
            _ui = null;
            _flow = null;
            _root = null;
        }

        private void OnSessionChanged()
        {
            if (!TreeIsLive)
            {
                return;
            }

            _flow.OnSessionChanged();
        }
    }
}
