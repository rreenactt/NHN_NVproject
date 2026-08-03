using System.Text;
using NV.Client.Net.Session;
using NV.Shared.Contracts.Messages;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NV.Client.Net
{
    /// 계측 도구. 제품 UI 는 로비 씬의 `LobbyController` 다.
    ///
    /// IMGUI 로 그린다. 이 프로젝트는 씬에 아무것도 authoring 하지 않는 쪽을 택했고 —
    /// 몸도 레벨도 코드가 만든다 — 이건 게임 화면이 아니라 개발용 계기판이다.
    ///
    /// 남겨 두는 이유가 둘이다. 하나는 로비 씬을 거치지 않고 설정으로 열어 둔 개발
    /// 룸(`test`)에 바로 붙는 경로가 필요하다는 것 — Build and Launch 2 Clients 가
    /// 그 흐름에 기대고 있다. 다른 하나는 수치다. "안 되는데요" 는 서버 미기동, 주소
    /// 오타, 버전 불일치, 정원 초과, 맵 해시 불일치가 전부 같은 모습으로 나타나는
    /// 상황이고, 단계와 간격을 따로 보여 주면 그것들이 화면에서 갈린다.
    ///
    /// 커서를 UI 와 플레이어가 번갈아 갖는다. 패널이 열려 있으면 컨트롤러의 입력을 끊고
    /// 커서를 놓아 준다. 그러지 않으면 버튼을 누르려는 클릭이 매번 커서를 다시 잠근다.
    [DefaultExecutionOrder(-80)]
    [RequireComponent(typeof(NetworkBootstrap))]
    public sealed class NetworkTestUi : MonoBehaviour
    {
        [Header("Panel")]
        [Tooltip("시작할 때 접속 패널을 열어 둔다.")]
        public bool openOnStart = true;

        [Tooltip("플레이 중 이 키로 패널을 다시 연다. 커서도 함께 풀린다.")]
        public Key togglePanelKey = Key.Escape;

        [Header("Layout")]
        public int panelWidth = 400;
        public int hudWidth = 320;

        private NetworkBootstrap _bootstrap;
        private string _host = "localhost:5202";
        private string _name = string.Empty;

        /// 설정으로 열어 둔 개발 룸. 코드를 발급받지 않고 바로 붙는다.
        private string _code = "test";

        private bool _panelOpen;
        private readonly StringBuilder _text = new StringBuilder(512);
        private GUIStyle _title;
        private GUIStyle _body;

        private void Awake()
        {
            _bootstrap = GetComponent<NetworkBootstrap>();
        }

        private void Start()
        {
            SetPanel(openOnStart);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard[togglePanelKey].wasPressedThisFrame)
            {
                SetPanel(!_panelOpen);
            }
        }

        /// 패널이 열리면 플레이어에게서 입력과 커서를 회수한다.
        private void SetPanel(bool open)
        {
            _panelOpen = open;

            var player = _bootstrap.LocalPlayer;
            if (player != null)
            {
                player.InputEnabled = !open;
            }
        }

        private void OnGUI()
        {
            EnsureStyles();

            if (_panelOpen)
            {
                DrawPanel();
            }
            else
            {
                DrawHud();
            }
        }

        private void EnsureStyles()
        {
            if (_title != null)
            {
                return;
            }

            _title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 14 };
            _body = new GUIStyle(GUI.skin.label) { wordWrap = true };
        }

        private void DrawPanel()
        {
            const int height = 320;
            var area = new Rect(
                (Screen.width - panelWidth) * 0.5f,
                Mathf.Max(20f, (Screen.height - height) * 0.5f),
                panelWidth,
                height);

            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 14f, area.y + 12f, area.width - 28f, area.height - 24f));

            GUILayout.Label("NV 개발 계기판", _title);
            GUILayout.Space(6f);

            var session = NetSession.Exists ? NetSession.Current : null;
            var idle = session == null
                || session.State == SessionState.Idle
                || session.State == SessionState.Failed;

            GUI.enabled = idle;

            GUILayout.Label("서버 (host:port)");
            _host = GUILayout.TextField(_host);

            GUILayout.Label("이름 (12자, 비워도 된다)");
            _name = GUILayout.TextField(_name);

            GUILayout.Label("초대 코드 또는 개발 룸 id");
            _code = GUILayout.TextField(_code);

            GUI.enabled = true;
            GUILayout.Space(6f);

            DrawStateLine(session);
            GUILayout.Space(6f);
            DrawButtons(session, idle);

            GUILayout.FlexibleSpace();
            GUILayout.Label(
                $"맵 {_bootstrap.MapName} · 해시 {_bootstrap.MapHashStatus}\n" +
                $"{togglePanelKey} 로 이 패널을 여닫는다. 닫으면 마우스가 잠긴다.",
                _body);

            GUILayout.EndArea();
        }

        private void DrawStateLine(NetSession session)
        {
            if (session == null)
            {
                GUILayout.Label("세션이 없다. 오프라인으로 돌고 있다.", _body);
                return;
            }

            if (session.State == SessionState.Failed)
            {
                // 사유와 다음 행동을 그대로 보여준다. 요약하면 실패들이 다시 뭉친다.
                GUILayout.Label("실패: " + session.Failure.Message + "\n→ " + session.Failure.NextAction, _body);
                return;
            }

            GUILayout.Label(StateLabel(session), _body);
        }

        private static string StateLabel(NetSession session)
        {
            switch (session.State)
            {
                case SessionState.Idle: return "접속하지 않았다. 서버는 dotnet run --project Api 로 띄운다.";
                case SessionState.Creating: return "방을 만드는 중…";
                case SessionState.Resolving: return "방 상태를 확인하는 중…";
                case SessionState.Connecting: return "소켓 여는 중…";
                case SessionState.Handshaking: return "소켓은 열렸다. Welcome 을 기다린다.";
                case SessionState.InLobby: return $"대기 중. {session.Client.RosterCount}/{session.Room.Capacity}명, 최소 {session.MinPlayers}명 필요.";
                case SessionState.InGame: return "매치 진행 중.";
                case SessionState.Ended: return "매치 종료. 방장이 로비로 되돌릴 수 있다.";
                case SessionState.Leaving: return "나가는 중…";
                default: return string.Empty;
            }
        }

        private void DrawButtons(NetSession session, bool idle)
        {
            GUILayout.BeginHorizontal();

            if (idle)
            {
                if (GUILayout.Button("참가", GUILayout.Height(26f)))
                {
                    Configure().JoinByCode(_code.Trim());
                }

                if (GUILayout.Button("방 만들기", GUILayout.Height(26f)))
                {
                    Configure().CreateAndJoin(null);
                }
            }
            else if (session != null)
            {
                if (session.State == SessionState.InLobby)
                {
                    // 방장이 아니거나 인원이 모자라면 누를 수 없다. 이유는 위 상태 줄에 있다.
                    GUI.enabled = session.CanStart;
                    if (GUILayout.Button("시작", GUILayout.Height(26f)))
                    {
                        session.RequestStart();
                    }

                    GUI.enabled = true;
                }

                if (session.State == SessionState.Ended && session.IsHost
                    && GUILayout.Button("로비로", GUILayout.Height(26f)))
                {
                    session.RequestReturnToLobby();
                }

                if (GUILayout.Button("나가기", GUILayout.Height(26f)))
                {
                    session.Leave();
                }

                if (session.State == SessionState.InGame && GUILayout.Button("플레이", GUILayout.Height(26f)))
                {
                    SetPanel(false);
                }
            }

            GUILayout.EndHorizontal();
        }

        /// 버튼을 누르는 순간에만 세션을 만든다.
        ///
        /// 화면을 그리는 동안 만들면 오프라인으로 씬을 여는 것만으로 세션이 생기고,
        /// 그 세션은 아무 곳에도 접속하지 않은 채 남는다.
        private NetSession Configure()
        {
            var session = NetSession.Current;
            session.host = _host.Trim();
            session.displayName = _name.Trim();
            return session;
        }

        /// 플레이 중 표시. 수치를 그대로 보여준다 — 요약하면 원인이 갈리지 않는다.
        private void DrawHud()
        {
            var session = NetSession.Exists ? NetSession.Current : null;
            var client = _bootstrap.Client;

            _text.Clear();

            if (session == null)
            {
                _text.Append("NV  오프라인");
            }
            else
            {
                _text.Append("NV  ").Append(session.State);

                if (client != null && client.HasRoomState)
                {
                    _text.Append("  [").Append(client.RoomState.Phase).Append(']');
                }

                _text.Append('\n');
                _text.Append(InviteCodeText.ToDisplay(session.Code));

                if (!string.IsNullOrEmpty(session.HostToken))
                {
                    _text.Append(" (방장)");
                }

                _text.Append("   왕복 ").Append((session.ProbeSeconds * 1000f).ToString("F0")).Append("ms\n");

                if (client != null && client.HasWelcome)
                {
                    var snapshots = client.Snapshots;

                    _text.Append("나 = 플레이어 ").Append(client.LocalPlayerId)
                        .Append("   명단 ").Append(client.RosterCount).Append('\n');

                    _text.Append("서버 틱 ").Append(snapshots != null ? snapshots.LatestTick : 0u)
                        .Append("   입력 지연 ").Append(client.InputLag).Append("틱\n");

                    _text.Append("스냅샷 ").Append((client.SnapshotInterval * 1000f).ToString("F0"))
                        .Append("ms (최대 ").Append((client.SnapshotIntervalMax * 1000f).ToString("F0"))
                        .Append(", 경과 ").Append((client.SinceLastSnapshot * 1000f).ToString("F0")).Append(")\n");

                    if (client.RoomState.SeekerPlayerId != RoomStateHeader.NoPlayer)
                    {
                        _text.Append("Seeker ").Append(client.RoomState.SeekerPlayerId)
                            .Append("   씨드 ").Append(client.RoomState.PlacementSeed).Append('\n');
                    }
                }

                _text.Append("맵 ").Append(_bootstrap.MapHashStatus);

                if (session.Failure.HasFailed)
                {
                    _text.Append('\n').Append(session.Failure.Message);
                }
            }

            _text.Append('\n').Append(togglePanelKey).Append(" : 계기판");

            var height = 140f;
            GUI.Box(new Rect(10f, 10f, hudWidth, height), GUIContent.none);
            GUI.Label(new Rect(20f, 16f, hudWidth - 20f, height - 12f), _text.ToString(), _body);
        }
    }
}
