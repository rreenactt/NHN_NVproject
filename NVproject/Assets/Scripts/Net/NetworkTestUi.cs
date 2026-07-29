using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NV.Client.Net
{
    /// 접속부터 플레이까지의 전체 플로우를 화면에서 돌릴 수 있게 하는 UI.
    ///
    /// IMGUI 로 그린다. UGUI 캔버스를 쓰면 프리팹과 씬 배선이 늘어나는데, 이 프로젝트는
    /// 씬에 아무것도 authoring 하지 않는 쪽을 택했고 — 몸도 레벨도 코드가 만든다 —
    /// 이건 계측 도구이지 제품 UI 가 아니다. 새 패키지도 필요하지 않다.
    ///
    /// UI 가 존재하는 이유는 상태를 보여주는 것보다 **구분하게 해 주는 것**에 있다.
    /// "안 되는데요" 는 서버 미기동, 주소 오타, 프로토콜 버전 불일치, 룸 정원 초과,
    /// 맵 해시 불일치가 전부 같은 모습으로 나타나는 상황이다. 각 단계를 따로 표시하면
    /// 그 다섯 가지가 화면에서 갈린다.
    ///
    /// 커서를 UI 와 플레이어가 번갈아 갖는다. 패널이 열려 있으면 컨트롤러의 입력을 끊고
    /// 커서를 놓아 준다. 그러지 않으면 버튼을 누르려는 클릭이 매번 커서를 다시 잠근다.
    [DefaultExecutionOrder(-80)]
    [RequireComponent(typeof(NetworkBootstrap))]
    public sealed class NetworkTestUi : MonoBehaviour
    {
        [Header("Panel")]
        [Tooltip("시작할 때 접속 패널을 열어 둔다. 끄면 자동 접속 설정에만 의존한다.")]
        public bool openOnStart = true;

        [Tooltip("플레이 중 이 키로 패널을 다시 연다. 커서도 함께 풀린다.")]
        public Key togglePanelKey = Key.Escape;

        [Header("Layout")]
        public int panelWidth = 380;
        public int hudWidth = 300;

        private NetworkBootstrap _bootstrap;
        private string _host = "localhost:5202";
        private string _room = "test";
        private bool _panelOpen;
        private readonly StringBuilder _text = new StringBuilder(256);
        private GUIStyle _title;
        private GUIStyle _body;

        private void Awake()
        {
            _bootstrap = GetComponent<NetworkBootstrap>();
            _host = _bootstrap.host;
            _room = _bootstrap.room;
        }

        private void Start()
        {
            // 자동 접속이 켜져 있으면 패널을 띄우지 않는다. 두 개가 동시에 접속을 걸면
            // 두 번째 호출이 조용히 무시되고, 왜 주소를 고쳐도 안 먹는지 알 수 없게 된다.
            SetPanel(openOnStart && !_bootstrap.connectOnStart);
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
            var client = _bootstrap.Client;
            if (client == null)
            {
                return;
            }

            if (_panelOpen)
            {
                DrawPanel(client);
            }
            else
            {
                DrawHud(client);
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

        private void DrawPanel(NetworkClient client)
        {
            EnsureStyles();

            var height = client.State == ConnectionState.Playing ? 250 : 290;
            var area = new Rect(
                (Screen.width - panelWidth) * 0.5f,
                Mathf.Max(20f, (Screen.height - height) * 0.5f),
                panelWidth,
                height);

            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 14f, area.y + 12f, area.width - 28f, area.height - 24f));

            GUILayout.Label("NV 멀티플레이 테스트", _title);
            GUILayout.Space(6f);

            GUI.enabled = client.State == ConnectionState.Disconnected || client.State == ConnectionState.Failed;

            GUILayout.Label("서버 (host:port)");
            _host = GUILayout.TextField(_host);
            // 룸 id 형식은 서버가 검사하고 어긋나면 업그레이드 전에 400 으로 거부한다.
            // 그 규칙은 Realtime 모듈 내부에 있어 클라이언트가 공유하지 못하므로,
            // 여기서는 검사하지 않고 형식만 알려 준다. 두 곳에 규칙을 두면 갈린다.
            GUILayout.Label("룸  (소문자·숫자·하이픈, 32자 이하)");
            _room = GUILayout.TextField(_room);

            GUI.enabled = true;
            GUILayout.Space(8f);

            DrawStateLine(client);
            GUILayout.Space(8f);
            DrawButtons(client);

            GUILayout.FlexibleSpace();
            GUILayout.Label(
                $"맵 {_bootstrap.MapName} · 해시 {_bootstrap.MapHashStatus}\n" +
                $"{togglePanelKey} 로 이 패널을 여닫는다. 닫으면 마우스가 잠기고 조작이 시작된다.",
                _body);

            GUILayout.EndArea();
        }

        private void DrawStateLine(NetworkClient client)
        {
            switch (client.State)
            {
                case ConnectionState.Disconnected:
                    GUILayout.Label("접속하지 않았다. 서버는 dotnet run --project Api 로 띄운다.", _body);
                    break;

                case ConnectionState.Connecting:
                    GUILayout.Label($"소켓 여는 중… {client.StateElapsed:F1}초", _body);
                    break;

                case ConnectionState.Connected:
                    GUILayout.Label("소켓은 열렸다. 서버의 Welcome 을 기다린다.", _body);
                    break;

                case ConnectionState.Playing:
                    GUILayout.Label(
                        $"플레이 중. 플레이어 {client.LocalPlayerId}, " +
                        $"서버 {client.ServerTickRate}Hz, 입력 지연 {client.InputLag}틱",
                        _body);
                    break;

                case ConnectionState.Failed:
                    // 실패 사유를 그대로 보여준다. 요약하면 위의 다섯 가지가 다시 뭉친다.
                    GUILayout.Label("실패: " + client.LastError, _body);
                    break;
            }
        }

        private void DrawButtons(NetworkClient client)
        {
            GUILayout.BeginHorizontal();

            switch (client.State)
            {
                case ConnectionState.Disconnected:
                case ConnectionState.Failed:
                    if (GUILayout.Button("접속", GUILayout.Height(28f)))
                    {
                        _bootstrap.Connect(_host.Trim(), _room.Trim());
                    }

                    break;

                case ConnectionState.Connecting:
                case ConnectionState.Connected:
                    if (GUILayout.Button("취소", GUILayout.Height(28f)))
                    {
                        _bootstrap.Disconnect();
                    }

                    break;

                case ConnectionState.Playing:
                    if (GUILayout.Button("플레이", GUILayout.Height(28f)))
                    {
                        SetPanel(false);
                    }

                    if (GUILayout.Button("접속 해제", GUILayout.Height(28f)))
                    {
                        _bootstrap.Disconnect();
                    }

                    break;
            }

            GUILayout.EndHorizontal();
        }

        /// 플레이 중 표시. 스냅샷에 실린 id 를 그대로 보여준다 — 서버가 누구를 보내고
        /// 있는지와 화면에 누가 보이는지가 어긋나는 경우를 눈으로 잡을 수 있다.
        private void DrawHud(NetworkClient client)
        {
            EnsureStyles();

            _text.Clear();
            _text.Append("NV  ").Append(StateLabel(client.State)).Append('\n');
            _text.Append(client.Endpoint).Append('\n');

            if (client.State == ConnectionState.Playing)
            {
                var snapshots = client.Snapshots;
                _text.Append("나 = 플레이어 ").Append(client.LocalPlayerId)
                    .Append("   엔티티 ").Append(snapshots != null ? snapshots.LatestEntityCount : 0).Append('\n');
                _text.Append("서버 틱 ").Append(snapshots != null ? snapshots.LatestTick : 0u)
                    .Append("   입력 지연 ").Append(client.InputLag).Append("틱\n");
                _text.Append("맵 ").Append(_bootstrap.MapHashStatus);
            }
            else if (!string.IsNullOrEmpty(client.LastError))
            {
                _text.Append(client.LastError);
            }

            _text.Append("\n").Append(togglePanelKey).Append(" : 접속 패널");

            GUI.Box(new Rect(10f, 10f, hudWidth, 92f), GUIContent.none);
            GUI.Label(new Rect(20f, 16f, hudWidth - 20f, 84f), _text.ToString(), _body);
        }

        private static string StateLabel(ConnectionState state)
        {
            switch (state)
            {
                case ConnectionState.Disconnected: return "미접속";
                case ConnectionState.Connecting: return "접속 중";
                case ConnectionState.Connected: return "핸드셰이크";
                case ConnectionState.Playing: return "플레이";
                default: return "실패";
            }
        }
    }
}
