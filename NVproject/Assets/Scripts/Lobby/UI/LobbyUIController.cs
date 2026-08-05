using System;
using NV.Client.Lobby.Events;
using NV.Client.Lobby.Models;
using NV.Client.Lobby.Services;
using NV.Client.Net.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace NV.Client.Lobby.UI
{
    /// 화면 조립. UXML 을 인스턴스화하고 뷰를 만들어 붙이고, 갱신 신호를 뷰로 나눠 준다.
    ///
    /// 이벤트를 구독하는 유일한 지점이다. 뷰까지 각자 구독하게 만들면 해제 지점이 뷰
    /// 수만큼 생기고, 하나만 빠뜨려도 증상이 "가끔 화면이 두 번 그려진다" 로만 나타난다.
    ///
    /// 버튼이 무엇을 하는지는 여기서 정하지 않는다. `Action` 으로 밖에 내보내고
    /// `LobbyController` 가 채운다 — 뷰가 서비스를 직접 부르기 시작하면 화면 흐름이
    /// 열두 파일에 흩어진다.
    public sealed class LobbyUIController
    {
        private readonly VisualElement _root;
        private readonly LobbyModel _model;
        private readonly LobbyEvents _events;
        private readonly RoomService _rooms;
        private readonly NetSession _session;

        private readonly PlayerInfoView _playerInfo;
        private readonly ConnectionStatusView _connection;
        private readonly RoomListView _roomList;

        private readonly VisualElement _pageBrowser;
        private readonly VisualElement _pageRoom;

        private readonly Label _statusLine;
        private readonly Label _statusAction;
        private readonly VisualElement _status;
        private readonly Button _retry;
        private readonly Button _quickJoin;
        private readonly Label _quickJoinNote;
        private readonly Button _create;
        private readonly Button _joinCode;

        private Texture2D _scanlines;
        private int _scanlineHeight;
        private float _nextClockRefresh;

        public LobbyUIController(
            VisualElement root,
            NetSession session,
            LobbyModel model,
            LobbyEvents events,
            RoomService rooms)
        {
            _root = root;
            _session = session;
            _model = model;
            _events = events;
            _rooms = rooms;

            Popups = new PopupHost(root.Q<VisualElement>("popup-root"));
            Toasts = new ToastMessage(root.Q<VisualElement>("toast-root"));
            Loading = new LoadingOverlay(root.Q<VisualElement>("loading-overlay"));

            _playerInfo = new PlayerInfoView(root.Q<VisualElement>("player-info"));
            _connection = new ConnectionStatusView(root.Q<VisualElement>("connection"));
            _roomList = new RoomListView(root, () => OnRefresh?.Invoke());

            _pageBrowser = root.Q<VisualElement>("page-browser");
            _pageRoom = root.Q<VisualElement>("page-room");

            GameLobby = new GameLobbyView(_pageRoom, session);

            // 로비 페이지로 시작한다. 방에 들어가 있는 상태로 화면이 다시 만들어지는 경우
            // (도메인 리로드)는 `GameLobbyController.Sync` 가 곧바로 바로잡는다.
            ShowRoomPage(false);

            _status = root.Q<VisualElement>("status");
            _statusLine = root.Q<Label>("status-line");
            _statusAction = root.Q<Label>("status-action");
            _retry = root.Q<Button>("retry-button");

            _create = root.Q<Button>("create-room-button");
            _joinCode = root.Q<Button>("join-code-button");
            _quickJoin = root.Q<Button>("quick-join-button");
            _quickJoinNote = root.Q<Label>("quick-join-note");

            WireButtons(root);
            WireEscape(root);

            _events.ModelChanged += RefreshAll;
            _events.RoomListChanged += RefreshRooms;
            _events.ConnectionChanged += RefreshConnection;
            _events.ProfileChanged += RefreshProfile;
            _events.ToastRequested += OnToast;
        }

        /// 대기방 페이지. `GameLobbyController` 가 언제 보일지 정한다.
        public GameLobbyView GameLobby { get; }

        public PopupHost Popups { get; }

        public ToastMessage Toasts { get; }

        public LoadingOverlay Loading { get; }

        public Action OnCreateRoom { get; set; }

        public Action OnJoinByCode { get; set; }

        public Action OnQuickJoin { get; set; }

        public Action OnSettings { get; set; }

        public Action OnQuit { get; set; }

        public Action OnRefresh { get; set; }

        public Action OnRetry { get; set; }

        public Action<RoomInfo> OnJoinRoom
        {
            set => _roomList.SetJoinHandler(value);
        }

        /// 구독을 끊는다. 화면을 다시 만들기 전에 반드시 부른다.
        ///
        /// 이것을 빠뜨리면 죽은 트리를 가리키는 핸들러가 이벤트마다 예외를 던진다.
        /// 도메인 리로드로 트리만 죽고 이 객체는 살아 있는 경우가 정확히 그것이다.
        public void Dispose()
        {
            _events.ModelChanged -= RefreshAll;
            _events.RoomListChanged -= RefreshRooms;
            _events.ConnectionChanged -= RefreshConnection;
            _events.ProfileChanged -= RefreshProfile;
            _events.ToastRequested -= OnToast;

            // 대기방은 씬 오브젝트(미리보기 카메라·렌더 텍스처)를 들고 있다. 트리만 버리면
            // 그것들이 씬에 남아 도메인 리로드마다 하나씩 늘어난다.
            GameLobby?.Dispose();

            Toasts.Clear();
            Popups.CloseAll();
            Loading.Reset();

            DestroyScanlines();
        }

        private void WireButtons(VisualElement root)
        {
            if (_create != null)
            {
                _create.clicked += () => OnCreateRoom?.Invoke();
            }

            if (_joinCode != null)
            {
                _joinCode.clicked += () => OnJoinByCode?.Invoke();
            }

            if (_quickJoin != null)
            {
                _quickJoin.clicked += () => OnQuickJoin?.Invoke();
            }

            var settings = root.Q<Button>("settings-button");
            if (settings != null)
            {
                settings.clicked += () => OnSettings?.Invoke();
            }

            var quit = root.Q<Button>("quit-button");
            if (quit != null)
            {
                quit.clicked += () => OnQuit?.Invoke();

#if UNITY_WEBGL && !UNITY_EDITOR
                // 브라우저에서 Application.Quit() 은 아무 일도 하지 않는다. 눌러도
                // 반응 없는 버튼을 남기느니 없앤다.
                quit.style.display = DisplayStyle.None;
#endif
            }

            if (_retry != null)
            {
                _retry.clicked += () => OnRetry?.Invoke();
            }
        }

        /// Esc 로 맨 위 팝업을 닫는다.
        ///
        /// `TrickleDown` 으로 받는다. 버블 단계에서 받으면 포커스를 가진 `TextField` 가
        /// 먼저 키를 먹고, 코드 입력 칸에 커서가 있을 때 Esc 가 듣지 않는다.
        private void WireEscape(VisualElement root)
        {
            root.focusable = true;

            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape)
                {
                    return;
                }

                if (Popups.CloseTop())
                {
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
        }

        // ==================================================== 페이지

        /// 대기방 페이지를 보인다(또는 로비 페이지로 되돌린다).
        ///
        /// `display` 로 가른다. 트리에서 떼면 돌아올 때 다시 만들어야 하고, 여기에는 감춰야
        /// 하는 정보가 없다 — 게임 HUD 가 역할별 패널을 떼어내는 것은 그쪽에는 있기 때문이다.
        ///
        /// 두 페이지를 동시에 보이지 않게 하는 것이 이 함수의 전부다. 두 곳에서 각자
        /// `display` 를 만지면 둘 다 켜진 상태가 표현 가능해지고, 그러면 로비 목록이 대기방
        /// 뒤에 겹쳐 보인다.
        public void ShowRoomPage(bool show)
        {
            if (_pageBrowser != null)
            {
                _pageBrowser.style.display = show ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_pageRoom != null)
            {
                _pageRoom.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // ==================================================== 갱신

        public void RefreshAll()
        {
            RefreshProfile();
            RefreshConnection();
            RefreshRooms();
            RefreshStatus();
        }

        public void RefreshProfile()
        {
            _playerInfo.Refresh();
        }

        public void RefreshConnection()
        {
            _connection.Refresh(_model, _session.Host);
        }

        public void RefreshRooms()
        {
            _roomList.Refresh(_model, _rooms);
            RefreshQuickJoin();
        }

        private void RefreshQuickJoin()
        {
            if (_quickJoin == null)
            {
                return;
            }

            var can = _rooms.CanQuickJoin(out var reason) && !_rooms.IsQuickJoining;

            _quickJoin.SetEnabled(can);
            _quickJoin.text = _rooms.IsQuickJoining ? "참가하는 중…" : "빠른 참가";

            if (_quickJoinNote != null)
            {
                _quickJoinNote.text = can ? string.Empty : reason;
            }
        }

        /// 세션 상태와 실패를 상태 줄에 쓴다.
        ///
        /// 문구를 새로 짓지 않는다. `SessionFailure` 가 13종의 사유와 각각의 다음 행동을
        /// 이미 들고 있고, 여기서 다시 쓰면 두 벌이 되어 반드시 어긋난다.
        public void RefreshStatus()
        {
            if (_statusLine == null)
            {
                return;
            }

            var failed = _session.Failure.HasFailed;

            _status.EnableInClassList("status-failed", failed);
            _statusLine.text = failed ? _session.Failure.Message : StateText();
            _statusAction.text = failed ? "→ " + _session.Failure.NextAction : string.Empty;

            _retry.style.display = failed && _session.Failure.Retryable
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            var busy = _session.State == SessionState.Creating
                || _session.State == SessionState.Resolving
                || _session.State == SessionState.Connecting
                || _session.State == SessionState.Handshaking;

            _create?.SetEnabled(!busy);
            _joinCode?.SetEnabled(!busy);
        }

        private string StateText()
        {
            switch (_session.State)
            {
                case SessionState.Idle: return "방을 만들거나 초대 코드로 참가한다.";
                case SessionState.Creating: return "방을 만드는 중…";
                case SessionState.Resolving: return "방을 확인하는 중…";
                case SessionState.Connecting: return "접속하는 중…";
                case SessionState.Handshaking: return "서버 응답을 기다린다…";
                case SessionState.InLobby: return "방에서 대기 중.";
                case SessionState.InGame: return "매치 진행 중.";
                case SessionState.Ended: return "매치 종료.";
                case SessionState.Leaving: return "나가는 중…";
                default: return string.Empty;
            }
        }

        private void OnToast(string message, bool isError)
        {
            Toasts.Show(message, isError);
        }

        // ==================================================== 프레임마다

        public void Tick()
        {
            Toasts.Tick();
            EnsureScanlines();

            // "12초 전" 과 새로고침 쿨다운은 시간이 흐르면 저절로 낡는다. 매 프레임
            // 다시 그릴 이유는 없으므로 초당 한 번만 손본다.
            if (Time.unscaledTime >= _nextClockRefresh)
            {
                _nextClockRefresh = Time.unscaledTime + 1f;
                RefreshRooms();
            }
        }

        /// 스캔라인을 화면 높이에 맞춰 만든다.
        ///
        /// 늘려 쓰면 회색 얼룩이 되어 효과가 사라진다. 한 줄이 정확히 한 픽셀이어야
        /// 하므로 해상도가 바뀔 때마다 다시 만든다. 게임 HUD 와 같은 규칙이다.
        private void EnsureScanlines()
        {
            var target = _root.Q<VisualElement>("scanlines");

            if (target == null || _scanlineHeight == Screen.height)
            {
                return;
            }

            _scanlineHeight = Screen.height;

            DestroyScanlines();

            _scanlines = new Texture2D(1, Mathf.Max(2, _scanlineHeight), TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
            };

            var pixels = new Color32[_scanlines.height];

            for (var y = 0; y < pixels.Length; y++)
            {
                pixels[y] = (y & 1) == 0
                    ? new Color32(0, 0, 0, 90)
                    : new Color32(0, 0, 0, 0);
            }

            _scanlines.SetPixels32(pixels);
            _scanlines.Apply(false);

            target.style.backgroundImage = new StyleBackground(_scanlines);
        }

        private void DestroyScanlines()
        {
            if (_scanlines != null)
            {
                UnityEngine.Object.Destroy(_scanlines);
                _scanlines = null;
            }

            _scanlineHeight = 0;
        }
    }
}
