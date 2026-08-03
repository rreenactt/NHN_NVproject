using NV.Shared.Contracts;
using UnityEngine;
using UnityEngine.UIElements;

namespace NV.Client.Net.Session
{
    /// 로비 화면. 방을 만들거나 코드로 참가하고, 명단을 보고, 방장이 시작을 누른다.
    ///
    /// UI Toolkit 으로 만든다. 게임 HUD 가 이미 그것이고, 두 화면이 같은 스타일시트
    /// 규칙을 쓰는 편이 톤을 유지하는 유일한 방법이다 — 로비가 게임 메뉴처럼 보이면
    /// 매치가 시작될 때 레벨의 분위기가 0 에서 다시 시작한다.
    ///
    /// 화면은 둘이고 동시에 참이 되지 않는다. `#home` 은 입구, `#room` 은 방 안이다.
    ///
    /// 상태를 직접 들고 있지 않다. 전부 `NetSession` 에서 읽어 그린다 — 화면이 자기
    /// 사본을 들면 서버가 보낸 명단과 화면에 보이는 명단이 어긋날 수 있고, 그 차이는
    /// 눈으로 잡을 수 없다.
    [DefaultExecutionOrder(-60)]
    public sealed class LobbyController : MonoBehaviour
    {
        private const string UxmlPath = "UI/Lobby";
        private const string UssPath = "UI/lobby";
        private const string PanelPath = "UI/LobbyPanelSettings";

        private UIDocument _document;
        private VisualTreeAsset _uxml;

        private VisualElement _root;
        private VisualElement _home;
        private VisualElement _room;
        private VisualElement _roster;
        private VisualElement _status;

        private TextField _host;
        private TextField _name;
        private TextField _map;
        private TextField _code;

        private Label _codeHint;
        private Label _roomCode;
        private Label _roomMap;
        private Label _roomNote;
        private Label _copyResult;
        private Label _statusLine;
        private Label _statusAction;

        private Button _create;
        private Button _join;
        private Button _start;
        private Button _leave;
        private Button _copyCode;
        private Button _copyLink;
        private Button _retry;

        private NetSession _session;
        private Texture2D _scanlines;
        private int _scanlineHeight;

        /// 트리가 살아 있는가.
        ///
        /// bool 플래그를 두지 않는다. `VisualElement` 는 스크립트 편집이 유발하는
        /// 도메인 리로드를 넘기지 못하는데 bool 은 넘긴다 — 그러면 전부 null 인 트리를
        /// "빌드됨" 으로 오인해 화면이 빈 채로 남는다. 게임 HUD 가 같은 이유로 같은
        /// 패턴을 쓴다.
        private bool TreeIsLive => _root != null && _root.panel != null && _statusLine != null;

        private void OnEnable()
        {
            _session = NetSession.Current;
            _session.StateChanged += Refresh;

            // 로비에서 들어왔을 때만 씬 이동을 붙인다. 개발용 씬에서 바로 시작한
            // 경우에는 돌아갈 로비가 없다.
            if (_session.GetComponent<SessionSceneRouter>() == null)
            {
                _session.gameObject.AddComponent<SessionSceneRouter>();
            }
        }

        private void OnDisable()
        {
            if (_session != null)
            {
                _session.StateChanged -= Refresh;
            }
        }

        private void Update()
        {
            if (!TreeIsLive)
            {
                Build();
                return;
            }

            EnsureScanlines();
        }

        private void Build()
        {
            if (_uxml == null)
            {
                _uxml = Resources.Load<VisualTreeAsset>(UxmlPath);
            }

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
                _document.panelSettings = Resources.Load<PanelSettings>(PanelPath);
            }

            if (_uxml == null || _document.panelSettings == null)
            {
                Debug.LogError($"[Lobby] Assets/Resources 에 {UxmlPath} 또는 {PanelPath} 가 없다.");
                enabled = false;
                return;
            }

            _document.visualTreeAsset = null;
            _document.rootVisualElement.Clear();

            _root = _uxml.Instantiate();
            _root.style.flexGrow = 1f;

            var style = Resources.Load<StyleSheet>(UssPath);
            if (style != null)
            {
                _root.styleSheets.Add(style);
            }

            _document.rootVisualElement.Add(_root);

            _home = _root.Q<VisualElement>("home");
            _room = _root.Q<VisualElement>("room");
            _roster = _root.Q<VisualElement>("roster");
            _status = _root.Q<VisualElement>("status");

            _host = _root.Q<TextField>("host");
            _name = _root.Q<TextField>("name");
            _map = _root.Q<TextField>("map");
            _code = _root.Q<TextField>("code");

            _codeHint = _root.Q<Label>("code-hint");
            _roomCode = _root.Q<Label>("room-code");
            _roomMap = _root.Q<Label>("room-map");
            _roomNote = _root.Q<Label>("room-note");
            _copyResult = _root.Q<Label>("copy-result");
            _statusLine = _root.Q<Label>("status-line");
            _statusAction = _root.Q<Label>("status-action");

            _create = _root.Q<Button>("create");
            _join = _root.Q<Button>("join");
            _start = _root.Q<Button>("start");
            _leave = _root.Q<Button>("leave");
            _copyCode = _root.Q<Button>("copy-code");
            _copyLink = _root.Q<Button>("copy-link");
            _retry = _root.Q<Button>("retry");

            _create.clicked += OnCreate;
            _join.clicked += OnJoin;
            _start.clicked += OnStart;
            _leave.clicked += () => _session.Leave();
            _copyCode.clicked += OnCopyCode;
            _copyLink.clicked += OnCopyLink;
            _retry.clicked += () => _session.Retry();

            _code.RegisterValueChangedCallback(OnCodeChanged);

            // 링크로 들어온 경우 코드 칸을 채운다. 자동 접속은 하지 않는다 —
            // 이름과 서버 주소를 확인할 화면을 건너뛰면 잘못 눌렀을 때 되돌릴 곳이 없다.
            var launchCode = InviteCodeFormat.Normalize(InviteLink.ReadCodeFromLaunchUrl());
            if (InviteCodeFormat.IsValid(launchCode))
            {
                _code.SetValueWithoutNotify(InviteCodeText.ToDisplay(launchCode));
            }

            _host.SetValueWithoutNotify(_session.host);
            _name.SetValueWithoutNotify(_session.displayName);

            Refresh();
        }

        // ==================================================== 입력

        private void OnCodeChanged(ChangeEvent<string> change)
        {
            // 화면에는 대문자로 남기고 내부 표현은 소문자다. 정규화를 여기서 한 번만
            // 하므로 붙여넣기에 섞인 공백과 하이픈도 같은 자리에서 사라진다.
            var normalized = InviteCodeText.Normalize(change.newValue);
            var display = InviteCodeText.ToDisplay(normalized);

            if (!string.Equals(change.newValue, display, System.StringComparison.Ordinal))
            {
                _code.SetValueWithoutNotify(display);
            }

            _codeHint.text = InviteCodeText.Hint(normalized);
        }

        private void OnCreate()
        {
            Configure();
            _session.CreateAndJoin(_map.value.Trim());
        }

        private void OnJoin()
        {
            Configure();
            _session.JoinByCode(_code.value);
        }

        private void OnStart()
        {
            // 자격과 인원은 서버가 다시 본다. 버튼이 꺼져 있는 것은 UI 의 친절이고
            // 판정이 아니다.
            _session.RequestStart();
        }

        private void Configure()
        {
            _session.host = _host.value.Trim();
            _session.displayName = _name.value.Trim();
        }

        private void OnCopyCode()
        {
            GUIUtility.systemCopyBuffer = InviteCodeText.ToDisplay(_session.Code);
            _copyResult.text = "코드를 복사했다.";
        }

        /// 링크는 클라이언트가 자기 실행 위치에서 조립한다. 서버는 배포 URL 을 모른다.
        private void OnCopyLink()
        {
            if (!InviteLink.TryBuild(_session.Code, out var link))
            {
                // 에디터와 스탠드얼론에는 링크가 동작할 방법이 없다. 조용히 실패하면
                // 사용자는 복사됐다고 믿고 아무것도 없는 것을 붙여넣는다.
                _copyResult.text = "이 빌드에서는 링크를 만들 수 없다. 코드를 전달한다.";
                return;
            }

            GUIUtility.systemCopyBuffer = link;
            _copyResult.text = link;
        }

        // ==================================================== 그리기

        private void Refresh()
        {
            if (!TreeIsLive)
            {
                return;
            }

            var inRoom = _session.State == SessionState.InLobby
                || _session.State == SessionState.InGame
                || _session.State == SessionState.Ended;

            Show(_home, !inRoom);
            Show(_room, inRoom);

            var failed = _session.Failure.HasFailed;
            _status.EnableInClassList("status-failed", failed);
            Show(_retry, failed && _session.Failure.Retryable);

            _statusLine.text = failed ? _session.Failure.Message : StateText();
            _statusAction.text = failed ? "→ " + _session.Failure.NextAction : string.Empty;

            var busy = _session.State == SessionState.Creating
                || _session.State == SessionState.Resolving
                || _session.State == SessionState.Connecting
                || _session.State == SessionState.Handshaking;

            _create.SetEnabled(!busy);
            _join.SetEnabled(!busy);

            if (inRoom)
            {
                RefreshRoom();
            }
        }

        private void RefreshRoom()
        {
            _roomCode.text = InviteCodeText.ToDisplay(_session.Code);
            _roomMap.text = "MAP  " + _session.Room.MapName;

            // 링크를 만들 수 없는 플랫폼에서는 버튼을 숨긴다. 눌러 봐야 실패한다.
            Show(_copyLink, InviteLink.TryBuild(_session.Code, out _));

            RefreshRoster();

            _start.SetEnabled(_session.CanStart);
            _roomNote.text = StartNote();
        }

        private void RefreshRoster()
        {
            _roster.Clear();

            var client = _session.Client;
            if (client == null)
            {
                return;
            }

            for (var index = 0; index < client.RosterCount; index++)
            {
                var entry = client.RosterEntry(index);
                var isSelf = client.HasWelcome && entry.PlayerId == client.LocalPlayerId;

                var row = new VisualElement();
                row.AddToClassList("roster-row");
                if (isSelf)
                {
                    row.AddToClassList("roster-self");
                }

                var name = new Label(string.IsNullOrEmpty(entry.Name)
                    ? "플레이어 " + entry.PlayerId
                    : entry.Name);
                name.AddToClassList("roster-name");
                row.Add(name);

                var tag = new Label(Tag(entry.PlayerId, client, isSelf));
                tag.AddToClassList("roster-tag");
                row.Add(tag);

                _roster.Add(row);
            }
        }

        private static string Tag(byte playerId, NetworkClient client, bool isSelf)
        {
            var host = client.HasRoomState && client.RoomState.HostPlayerId == playerId;

            if (host && isSelf)
            {
                return "방장 · 나";
            }

            return host ? "방장" : isSelf ? "나" : string.Empty;
        }

        /// 시작 버튼이 꺼져 있는 이유를 쓴다. 이유 없이 꺼진 버튼은 고장으로 읽힌다.
        private string StartNote()
        {
            if (_session.State == SessionState.InGame)
            {
                return "매치 진행 중.";
            }

            if (_session.State == SessionState.Ended)
            {
                return "매치가 끝났다. 방장이 로비로 되돌릴 수 있다.";
            }

            if (!_session.IsHost)
            {
                return "방장이 시작하기를 기다린다.";
            }

            var count = _session.Client != null ? _session.Client.RosterCount : 0;
            return count < _session.MinPlayers
                ? $"{_session.MinPlayers}명부터 시작할 수 있다. 지금 {count}명."
                : "시작할 수 있다.";
        }

        private string StateText()
        {
            switch (_session.State)
            {
                case SessionState.Idle: return "방을 만들거나 초대 코드를 입력한다.";
                case SessionState.Creating: return "방을 만드는 중…";
                case SessionState.Resolving: return "방을 확인하는 중…";
                case SessionState.Connecting: return "접속하는 중…";
                case SessionState.Handshaking: return "서버 응답을 기다린다…";
                case SessionState.InLobby: return $"대기 중 · {_session.Client.RosterCount}/{_session.Room.Capacity}";
                case SessionState.InGame: return "매치 진행 중.";
                case SessionState.Ended: return "매치 종료.";
                case SessionState.Leaving: return "나가는 중…";
                default: return string.Empty;
            }
        }

        private static void Show(VisualElement element, bool visible)
        {
            if (element != null)
            {
                element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
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

            if (_scanlines != null)
            {
                Destroy(_scanlines);
            }

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

        private void OnDestroy()
        {
            if (_scanlines != null)
            {
                Destroy(_scanlines);
            }
        }
    }
}
