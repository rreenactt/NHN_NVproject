using System;
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
    /// `RoomApi`, 화면은 `LobbyController` 다. 그 경계를 넘기면 WebGL 에서만 나는
    /// 버그가 세션 로직 안으로 들어온다.
    [DefaultExecutionOrder(-120)]
    public sealed class NetSession : MonoBehaviour
    {
        /// 백오프 간격(초). 이 배열 길이가 재시도 횟수 상한이다.
        private static readonly float[] RetryDelays = { 0.5f, 1f, 2f, 4f };

        private static NetSession _instance;

        [Header("Server")]
        [Tooltip("host:port. 로컬 개발 서버는 dotnet run --project Api 의 5202 포트다.")]
        public string host = "localhost:5202";

        [Tooltip("배포 환경에서는 반드시 켠다. HTTPS 페이지의 ws:// 는 mixed content 로 차단된다.")]
        public bool secure;

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
        public bool CanStart =>
            State == SessionState.InLobby
            && IsHost
            && Client != null
            && Client.RosterCount >= MinPlayers;

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

        // ==================================================== 사용자 행위

        /// 방을 만들고 바로 들어간다.
        ///
        /// 만든 직후에는 조회하지 않는다. 방금 받은 응답이 그 방의 상태이고,
        /// 한 번 더 물어보면 왕복 하나가 늘어난다.
        public void CreateAndJoin(string mapId)
        {
            if (Busy())
            {
                return;
            }

            ClearFailure();
            SetState(SessionState.Creating);

            _api = new RoomApi(host, secure);
            _pending = StartCoroutine(_api.Create(mapId, OnCreated));
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

            _api = new RoomApi(host, secure);
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
                result.MapHash,
                RoomPhase.Waiting,
                0,
                result.Capacity,
                RoomStateHeader.NoPlayer,
                result.MinPlayers);

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
            Client.Connect(host, Code, secure, interpolationDelay, HostToken, displayName);
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
                        Fail(SessionFailureKind.ConnectionLost, Client.LastError);
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

            Fail(SessionFailureKind.ConnectionLost, Client != null ? Client.LastError : null);
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
