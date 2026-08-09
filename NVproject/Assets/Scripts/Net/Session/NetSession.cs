using System;
using NV.Client.Config;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using UnityEngine;

namespace NV.Client.Net.Session
{
    /// 서버 세션 하나. 씬보다 오래 산다.
    ///
    /// 예전에는 `NetworkBootstrap` 이 접속을 소유했다. 그것은 씬 오브젝트이고 씬에
    /// 플레이어가 없으면 아예 비활성이 되므로, 로비 씬에서 접속해 게임 씬으로
    /// 넘어가는 흐름을 만들 수 없었다. 접속은 여기로 옮기고 씬 쪽은 스냅샷을 몸에
    /// 적용하는 일만 남긴다.
    ///
    /// 이 클래스는 상태 기계와 흐름만 갖는다. 와이어는 `NetworkClient`, HTTP 는
    /// `RoomApi`, 화면은 `MainLobbyController` 다. 그 경계를 넘기면 WebGL 에서만 나는
    /// 버그가 세션 로직 안으로 들어온다.
    [DefaultExecutionOrder(-120)]
    public sealed class NetSession : MonoBehaviour
    {
        /// 백오프 간격(초). 이 배열 길이가 재시도 횟수 상한이다.
        private static readonly float[] RetryDelays = { 0.5f, 1f, 2f, 4f };

        /// 서버가 강제 퇴장에 쓰는 WebSocket 닫힘 코드.
        ///
        /// 서버의 `RealtimeConstants.Kick.CloseCode` 와 **같은 값이어야 한다.** `Shared` 에
        /// 두지 않은 이유는 그것이 와이어 포맷이 아니라 전송 계층의 값이라는 것이다 —
        /// `Shared/Transport` 가 게임의 강제 퇴장을 알게 만들지 않는다. 두 곳이 갈리면
        /// 강제 퇴장이 회선 절단으로 읽혀 자동 재시도가 그 방에 다시 붙는다.
        private const int KickedCloseCode = 4003;

        private static NetSession _instance;

        [Tooltip("명단에 보일 이름. 비우면 '플레이어 N' 으로 표시된다.")]
        public string displayName = string.Empty;

        [Header("Smoothing")]
        [Tooltip("원격 플레이어 보간 버퍼 길이(초). 고정 파라미터는 100ms 다.")]
        public float interpolationDelay = 0.1f;

        private RoomApi _api;
        private Coroutine _pending;

        private int _retryIndex;
        private float _retryAt;
        private bool _retryScheduled;

        /// <summary>지금 살아 있는 세션. 없으면 만든다.</summary>
        ///
        /// 도메인 리로드는 static 필드를 지우면서 `Awake` 를 다시 실행하지 않는다.
        /// 그래서 매번 다시 찾는다 — 게임오브젝트는 리로드를 넘어 살아 있다.
        public static NetSession Current
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                _instance = FindAnyObjectByType<NetSession>();

                if (_instance == null)
                {
                    var carrier = new GameObject("NV Session");
                    _instance = carrier.AddComponent<NetSession>();
                }

                return _instance;
            }
        }

        /// <summary>씬에 세션이 있는지. 없을 때 만들지 않는다 — 오프라인 경로가 이것으로 갈린다.</summary>
        public static bool Exists => _instance != null || FindAnyObjectByType<NetSession>() != null;

        /// <summary>지금 붙는 서버. `host:port`.</summary>
        ///
        /// 직렬화 필드가 아니다. 예전에는 인스펙터 필드였고, 그래서 접속 대상이 씬 파일
        /// 안에 굽혀 있었다 — 이 프로젝트의 규칙대로 `.cs` 의 기본값을 고쳐도 저장된
        /// 씬은 옛 주소를 유지하므로, 환경 애셋과 씬이 조용히 어긋날 수 있었다. 지금은
        /// 부팅 때 <see cref="NVEnvironment"/> 에서 한 번 받고, 바꾸는 문은
        /// <see cref="Configure"/> 하나다.
        public string Host { get; private set; } = NVEnvironment.FallbackHost;

        /// <summary>`wss` / `https` 를 쓰는가.</summary>
        public bool Secure { get; private set; }

        public SessionState State { get; private set; } = SessionState.Idle;

        public NetworkClient Client { get; private set; }

        /// <summary>지금 들어가 있거나 들어가려는 방의 코드.</summary>
        public string Code { get; private set; } = string.Empty;

        /// <summary>방장 토큰. 방을 만든 경우에만 있다.</summary>
        public string HostToken { get; private set; } = string.Empty;

        /// <summary>참가 전 조회로 받은 방 정보. 맵 이름과 정원이 여기 있다.</summary>
        public RoomInfo Room { get; private set; }

        public SessionFailure Failure { get; private set; } = SessionFailure.None;

        /// <summary>프리플라이트 왕복 시간(초). 서버가 얼마나 먼지 보여주는 값이다.</summary>
        public float ProbeSeconds { get; private set; }

        public bool IsHost => Client != null && Client.IsLocalHost;

        /// <summary>방장이 지금 시작을 누를 수 있는가. 화면이 버튼을 이 값으로 켠다.</summary>
        ///
        /// 서버의 `Room.Start` 와 같은 조건을 본다. **판정이 아니라 친절이다** — 서버가 다시
        /// 보므로 이 값이 틀려도 규칙은 지켜지지만, 틀리면 화면이 거짓말을 한다: 켜져 있는데
        /// 눌러도 아무 일이 없거나, 꺼져 있는데 실은 시작할 수 있는 상태가 된다.
        public bool CanStart =>
            State == SessionState.InLobby
            && IsHost
            && Client != null
            && Client.RosterCount >= MinPlayers
            && EveryoneElseIsReady;

        /// <summary>나 자신이 준비를 눌렀는가. 명단 전문에서 읽는다.</summary>
        ///
        /// 사본을 두지 않는다. 눌린 상태를 여기 적어 두면 서버가 받아들이지 않은 토글이
        /// 화면에만 남고, 그 차이는 눈으로 잡을 수 없다.
        public bool IsLocalReady
        {
            get
            {
                if (Client == null || !Client.HasWelcome)
                {
                    return false;
                }

                for (var index = 0; index < Client.RosterCount; index++)
                {
                    var entry = Client.RosterEntry(index);

                    if (entry.PlayerId == Client.LocalPlayerId)
                    {
                        return entry.IsReady;
                    }
                }

                return false;
            }
        }

        /// <summary>준비하지 않은 사람 수. 봇과 나 자신은 세지 않는다.</summary>
        ///
        /// 화면이 "몇 명을 기다리는지" 를 쓰는 데 쓴다. 0 이면 시작할 수 있다.
        public int NotReadyCount
        {
            get
            {
                if (Client == null)
                {
                    return 0;
                }

                var localId = Client.HasWelcome ? Client.LocalPlayerId : (byte)0xFF;
                var count = 0;

                for (var index = 0; index < Client.RosterCount; index++)
                {
                    var entry = Client.RosterEntry(index);

                    if (entry.PlayerId == localId || entry.IsBot || entry.IsReady)
                    {
                        continue;
                    }

                    count++;
                }

                return count;
            }
        }

        /// <summary>나를 뺀 모든 사람이 준비했는가. 서버의 `EveryoneElseIsReady` 와 같은 규칙.</summary>
        ///
        /// **정적 룸 예외가 여기에도 있었고 함께 없앴다.** 서버가 그 룸에서 준비를 보지
        /// 않았으므로 버튼도 켜 두는 것이 맞았는데, 그 룸은 공개라서 방 목록과 빠른 참가가
        /// 보통 사람을 그리로 넣었다 — 아무도 준비하지 않은 채 시작되는 방이 그렇게 생겼다.
        ///
        /// 두 곳이 같은 규칙을 들고 있다는 것 자체는 그대로다. 서버가 판정이고 이쪽은 버튼의
        /// 모양이며, 어긋나면 증상은 **"눌렀는데 아무 일도 없다"** 다.
        public bool EveryoneElseIsReady => NotReadyCount == 0;

        /// <summary>시작에 필요한 최소 인원. 서버가 알려 준 값이며 클라이언트가 정하지 않는다.</summary>
        public int MinPlayers => Room.MinPlayers > 0 ? Room.MinPlayers : 2;

        public event Action StateChanged;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                // 씬마다 하나씩 놓아 두면 두 번째가 첫 번째의 접속을 끊는다.
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            // 이 빌드가 붙기로 되어 있는 서버. 로비가 저장된 프로필을 적용하면
            // (`LobbyService.ApplyStoredProfile`) 허용된 환경에서만 그 값이 덮는다.
            var environment = NVEnvironment.Active;
            Host = environment.Host;
            Secure = environment.Secure;

            Client = GetComponent<NetworkClient>();
            if (Client == null)
            {
                Client = gameObject.AddComponent<NetworkClient>();
            }

            Client.WelcomeReceived += OnWelcome;
            Client.RoomStateChanged += OnRoomStateChanged;
            Client.Ended += OnClientEnded;
        }

        private void OnDestroy()
        {
            if (Client != null)
            {
                Client.WelcomeReceived -= OnWelcome;
                Client.RoomStateChanged -= OnRoomStateChanged;
                Client.Ended -= OnClientEnded;
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>지금 서버 주소와 이름을 바꿀 수 있는가.</summary>
        public bool CanConfigure => State == SessionState.Idle;

        // ==================================================== 사용자 행위

        /// 서버 주소와 표시 이름을 바꾼다.
        ///
        /// `Idle` 일 때만 받는다. 접속 중에 주소를 바꾸면 자동 재시도(0.5·1·2·4초)가
        /// 방이 있는 서버가 아니라 새 주소를 두드리고, 그 실패는 화면에서 "방이
        /// 사라졌다" 로 보인다 — 원인이 설정 변경이라는 단서가 아무 데도 남지 않는다.
        ///
        /// 인스펙터 필드를 직접 쓰지 않고 이 문을 통하게 하는 이유가 그 판정 한 줄이다.
        public bool Configure(string newHost, bool newSecure, string newDisplayName)
        {
            if (!CanConfigure)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(newHost))
            {
                Host = newHost.Trim();
            }

            Secure = newSecure;
            displayName = (newDisplayName ?? string.Empty).Trim();

            StateChanged?.Invoke();
            return true;
        }

        /// 방을 만들고 바로 들어간다.
        ///
        /// 만든 직후에는 조회하지 않는다. 방금 받은 응답이 그 방의 상태이고,
        /// 한 번 더 물어보면 왕복 하나가 늘어난다.
        /// <param name="isPublic">
        /// 공개 목록에 실을 것인가. 기본은 비공개다 — 노출은 만든 사람이 선택했을 때만
        /// 일어나야 하고, 인자를 생략한 호출이 방을 공개해 버리면 그 선택이 무의미해진다.
        /// </param>
        public void CreateAndJoin(string mapId, bool isPublic = false)
        {
            if (Busy())
            {
                return;
            }

            ClearFailure();
            SetState(SessionState.Creating);

            _api = new RoomApi(Host, Secure);
            _pending = StartCoroutine(_api.Create(mapId, isPublic, OnCreated));
        }

        /// 코드로 참가한다. 형식은 보내기 전에 여기서 거른다.
        public void JoinByCode(string rawCode)
        {
            if (Busy())
            {
                return;
            }

            var code = InviteCodeText.Normalize(rawCode);

            if (!InviteCodeText.IsValid(code))
            {
                // 형식이 어긋난 코드를 서버까지 보내면 브라우저에서는 실패 사유가
                // 닫힘 코드 하나로 뭉쳐 오타와 없는 방을 구분할 수 없다.
                Fail(SessionFailureKind.InvalidCode);
                return;
            }

            Code = code;
            HostToken = string.Empty;

            BeginResolve();
        }

        /// 서버가 알려 준 방에 참가한다(목록에서 고른 방, 빠른 참가).
        ///
        /// `JoinByCode` 와 갈라 놓은 이유는 **초대 코드 형식 검사 때문이다.** 그 검사는
        /// 사람이 받아 적은 코드의 오타를 요청 전에 잡으려고 있다. 그런데 서버가 목록으로
        /// 내려준 방 id 는 오타일 수 없고, 그중 정적 개발 룸(`test`)은 4자라 **초대 코드
        /// 형식을 만족하지 않는다** — 서버는 `Game:StaticRooms` 의 id 를 코드 규칙으로
        /// 검사하지 않기 때문이다.
        ///
        /// 그래서 같은 문으로 보내면 목록에 보이는 방을 눌러도 `InvalidCode` 로 거부된다.
        /// 그 증상은 "개발용 방에만 못 들어간다" 로 나타나 원인을 찾기 어렵다.
        ///
        /// 검사를 없애지는 않는다. 서버의 룸 id 규칙(소문자·숫자·하이픈, 32자)을 그대로
        /// 본다 — 목록 응답이 망가졌을 때 그것을 그대로 URL 에 실어 보내지 않기 위한 것이다.
        public void JoinRoomId(string roomId)
        {
            if (Busy())
            {
                return;
            }

            var id = (roomId ?? string.Empty).Trim().ToLowerInvariant();

            if (!IsValidRoomId(id))
            {
                Fail(SessionFailureKind.InvalidCode);
                return;
            }

            Code = id;
            HostToken = string.Empty;

            BeginResolve();
        }

        /// 서버의 `RoomRegistry.IsValidRoomId` 와 같은 규칙.
        ///
        /// 초대 코드 규칙(`InviteCodeFormat`)보다 넓다. 룸 id 는 초대 코드일 수도 있고
        /// 설정으로 열어 둔 정적 룸 id 일 수도 있으며, 후자는 코드 알파벳을 만족하지 않는다.
        private static bool IsValidRoomId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 32)
            {
                return false;
            }

            for (var index = 0; index < id.Length; index++)
            {
                var c = id[index];

                if ((c < 'a' || c > 'z') && (c < '0' || c > '9') && c != '-')
                {
                    return false;
                }
            }

            return true;
        }

        /// 준비를 켜거나 끈다.
        ///
        /// 로컬 상태를 바꾸지 않는다. 눌린 모양은 다음 명단 전문(2Hz)이 만든다 — 반 박자
        /// 늦지만, 서버가 거부한 토글이 화면에 남는 것보다 낫다.
        public bool SetReady(bool ready)
        {
            return Client != null && Client.SendControl(ControlKind.SetReady, ready ? (byte)1 : (byte)0);
        }

        /// 캐릭터를 고른다. 범위와 중복은 서버가 본다.
        ///
        /// 결과를 기다리지 않는다. 거부되면 다음 명단 전문이 여전히 전에 입던 것을 말해 주고,
        /// 화면은 그것을 그린다 — 실패 알림을 따로 만들면 그것은 놓칠 수 있는 한 번짜리
        /// 메시지가 된다.
        public bool SetCharacter(byte characterId)
        {
            return Client != null && Client.SendControl(ControlKind.SetCharacter, characterId);
        }

        /// 방장으로서 누군가를 내보낸다. 자격은 서버가 다시 본다.
        ///
        /// 대상은 **슬롯 번호**다. 클라이언트가 아는 것이 그것뿐이고(명단 전문이 싣는 값),
        /// 세션 id 는 서버 안의 값이다.
        public bool KickPlayer(byte playerId)
        {
            return Client != null && Client.SendControl(ControlKind.KickPlayer, playerId);
        }

        /// 방장을 넘긴다. 넘긴 뒤 이 클라이언트는 시작 권한을 잃는다.
        public bool TransferHost(byte playerId)
        {
            return Client != null && Client.SendControl(ControlKind.TransferHost, playerId);
        }

        /// 방장으로서 매치 시작을 요청한다. 자격과 인원은 서버가 다시 본다.
        public bool RequestStart()
        {
            return Client != null && Client.SendControl(ControlKind.StartMatch);
        }

        /// 매치 결과를 서버에 보고한다. 규칙 판정이 클라이언트에 있는 동안의 경로다.
        public bool ReportMatchEnd(byte outcome)
        {
            return Client != null && Client.SendControl(ControlKind.EndMatch, outcome);
        }

        public bool RequestReturnToLobby()
        {
            return Client != null && Client.SendControl(ControlKind.ReturnToLobby);
        }

        /// 스스로 나간다.
        ///
        /// 퇴장을 제어 메시지로 보내지 않는다. 전송이 WebSocket 정상 종료 프레임을
        /// 보내므로 서버 로그에서 이미 회선 절단과 구분되고, 둘을 다 보내면 같은
        /// 소켓에 두 송신이 겹친다 — WebSocket 은 동시 송신을 허용하지 않는다.
        public void Leave()
        {
            CancelPending();
            CancelRetry();

            if (Client != null && Client.IsConnected)
            {
                SetState(SessionState.Leaving);
            }

            Code = string.Empty;
            HostToken = string.Empty;
            Room = default;

            if (Client != null)
            {
                Client.Disconnect();
            }

            ClearFailure();
            SetState(SessionState.Idle);
        }

        /// 씬이 맵 해시 불일치를 발견했을 때 세션에 알린다.
        ///
        /// 세션은 지형을 모른다 — 맵을 만드는 것도 해시를 계산하는 것도 씬의 일이다.
        /// 그래도 이 실패는 세션 화면에 떠야 한다. 서버는 export 된 예전 맵으로
        /// 판정하고 클라이언트는 새 씨드로 만든 지형을 그리는 상태이며, 증상은
        /// "특정 위치에서만 캐릭터가 튐" 으로만 나타나 추적이 어렵다.
        public void ReportMapHashMismatch(string detail)
        {
            Failure = SessionFailure.Of(SessionFailureKind.MapHashMismatch, detail);
            StateChanged?.Invoke();
        }

        /// 실패한 접속을 다시 시도한다. 재시도해도 결과가 같은 사유는 거른다.
        public void Retry()
        {
            if (State != SessionState.Failed || !Failure.Retryable)
            {
                return;
            }

            _retryIndex = 0;

            if (string.IsNullOrEmpty(Code))
            {
                ClearFailure();
                SetState(SessionState.Idle);
                return;
            }

            BeginResolve();
        }

        // ==================================================== 흐름

        private void BeginResolve()
        {
            ClearFailure();
            SetState(SessionState.Resolving);

            _api = new RoomApi(Host, Secure);
            _pending = StartCoroutine(_api.Probe(Code, OnProbed));
        }

        private void OnCreated(RoomCreateResult result)
        {
            _pending = null;

            if (!result.Ok)
            {
                Fail(result.Failure);
                return;
            }

            Code = result.Code;
            HostToken = result.HostToken;

            Room = new RoomInfo(
                result.Code,
                result.MapName,
                result.MapDisplayName,
                result.MapHash,
                RoomPhase.Waiting,
                0,
                result.Capacity,
                RoomStateHeader.NoPlayer,
                result.MinPlayers,
                result.IsPublic);

            Connect();
        }

        private void OnProbed(RoomProbeResult result)
        {
            _pending = null;

            if (result.HasInfo)
            {
                // 실패해도 정보는 쓴다. "8/8 진행 중" 같은 표시가 여기서 나온다.
                Room = result.Info;
            }

            if (!result.CanJoin)
            {
                ProbeSeconds = result.RoundTripSeconds;
                Fail(result.Failure);
                return;
            }

            ProbeSeconds = result.RoundTripSeconds;
            Connect();
        }

        private void Connect()
        {
            SetState(SessionState.Connecting);
            Client.Connect(Host, Code, Secure, interpolationDelay, HostToken, displayName);
        }

        private void Update()
        {
            if (_retryScheduled && Time.unscaledTime >= _retryAt)
            {
                _retryScheduled = false;
                BeginResolve();
                return;
            }

            TrackClient();
        }

        /// 와이어 단계와 룸 단계를 세션 단계로 옮긴다.
        ///
        /// 세션 단계를 바꾸는 곳을 여기와 사용자 행위로만 제한한다. 여러 곳에서
        /// 바꾸면 어느 경로로 지금 상태가 되었는지 알 수 없게 된다.
        private void TrackClient()
        {
            if (Client == null)
            {
                return;
            }

            switch (State)
            {
                case SessionState.Connecting:
                    if (Client.State == ConnectionState.Connected)
                    {
                        SetState(SessionState.Handshaking);
                    }
                    else if (Client.State == ConnectionState.Failed)
                    {
                        Fail(SessionFailureKind.ServerUnreachable, Client.LastError);
                    }

                    break;

                case SessionState.Handshaking:
                    if (Client.State == ConnectionState.Failed)
                    {
                        // 소켓은 열렸는데 Welcome 이 오지 않은 경우다. 프리플라이트를
                        // 통과한 직후이므로 그 사이에 정원이 찼을 가능성이 가장 높다.
                        Fail(SessionFailureKind.HandshakeTimeout, Client.LastError);
                    }

                    break;

                case SessionState.InLobby:
                case SessionState.InGame:
                case SessionState.Ended:
                    if (Client.State == ConnectionState.Failed
                        || Client.State == ConnectionState.Disconnected)
                    {
                        Fail(LostReason(), Client.LastError);
                        break;
                    }

                    ApplyRoomPhase();
                    break;
            }
        }

        private void OnWelcome()
        {
            _retryIndex = 0;

            // 방에 들어왔다. 매치가 시작되었는지는 룸 상태 전문이 알려 준다.
            SetState(SessionState.InLobby);
        }

        private void OnRoomStateChanged()
        {
            if (State == SessionState.InLobby || State == SessionState.InGame || State == SessionState.Ended)
            {
                ApplyRoomPhase();
            }

            StateChanged?.Invoke();
        }

        private void ApplyRoomPhase()
        {
            if (Client == null || !Client.HasRoomState)
            {
                return;
            }

            switch (Client.RoomState.Phase)
            {
                case RoomPhase.Waiting:
                    SetState(SessionState.InLobby);
                    break;

                case RoomPhase.Playing:
                    SetState(SessionState.InGame);
                    break;

                case RoomPhase.Ended:
                    SetState(SessionState.Ended);
                    break;
            }
        }

        private void OnClientEnded()
        {
            if (State == SessionState.Leaving || State == SessionState.Idle || State == SessionState.Failed)
            {
                return;
            }

            Fail(LostReason(), Client != null ? Client.LastError : null);
        }

        /// 붙어 있다가 끊긴 이유. 강제 퇴장인가 회선 절단인가.
        ///
        /// **닫힘 코드가 유일한 단서다.** 서버는 강제 퇴장을 `4003` 으로 알린다(서버의
        /// `RealtimeConstants.Kick.CloseCode`). 그 값이 이쪽 상수로 복제되어 있다는 것이
        /// 이 판정의 약점이지만, `Shared` 에 두면 전송 계층이 게임 규칙을 알게 된다 —
        /// 대신 두 자리에 서로를 가리키는 주석을 남긴다.
        ///
        /// 코드를 잃는 경로가 있으면 회선 절단으로 읽히고, 그때는 자동 재시도가 그 방에
        /// 다시 붙는다. 계정이 없으므로 그것을 막을 방법은 없고, 이것이 강제 퇴장 기능의
        /// 실제 한계다.
        private SessionFailureKind LostReason()
        {
            return Client != null && Client.CloseCode == KickedCloseCode
                ? SessionFailureKind.Kicked
                : SessionFailureKind.ConnectionLost;
        }

        // ==================================================== 실패와 재시도

        private void Fail(SessionFailureKind kind, string detail = null)
        {
            if (State == SessionState.Failed && Failure.Kind == kind)
            {
                return;
            }

            CancelPending();

            Failure = SessionFailure.Of(kind, detail);
            SetState(SessionState.Failed);

            Debug.LogWarning("[NV] 세션 실패: " + Failure.Message);

            if (Client != null && Client.State != ConnectionState.Disconnected)
            {
                Client.Disconnect();
            }

            ScheduleRetry();
        }

        /// 끊긴 접속만 자동으로 다시 붙는다.
        ///
        /// 이것은 복구가 아니라 새 세션이다. PlayerId 가 바뀌고 스폰으로 돌아가며,
        /// 그 사이에 매치가 시작되었다면 진행 중 방이라 아예 거부된다. 화면에
        /// "복구했다" 로 쓰면 안 된다.
        private void ScheduleRetry()
        {
            if (Failure.Kind != SessionFailureKind.ConnectionLost
                || string.IsNullOrEmpty(Code)
                || _retryIndex >= RetryDelays.Length)
            {
                return;
            }

            _retryAt = Time.unscaledTime + RetryDelays[_retryIndex];
            _retryIndex++;
            _retryScheduled = true;
        }

        private void CancelRetry()
        {
            _retryScheduled = false;
            _retryIndex = 0;
        }

        private void CancelPending()
        {
            if (_pending != null)
            {
                StopCoroutine(_pending);
                _pending = null;
            }
        }

        private void ClearFailure()
        {
            Failure = SessionFailure.None;
        }

        private bool Busy()
        {
            return State == SessionState.Creating
                || State == SessionState.Resolving
                || State == SessionState.Connecting
                || State == SessionState.Handshaking;
        }

        private void SetState(SessionState next)
        {
            if (State == next)
            {
                return;
            }

            State = next;
            StateChanged?.Invoke();
        }
    }
}
