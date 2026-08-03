using System;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using NV.Shared.Simulation;
using NV.Shared.Transport;
using UnityEngine;

namespace NV.Client.Net
{
    /// 한 틱의 입력을 만들어 주는 쪽. 로컬 플레이어가 구현한다.
    public interface IInputSource
    {
        InputFrame Sample();
    }

    /// 접속의 진행 단계. UI 가 이 값 하나로 화면을 고른다.
    ///
    /// `Connected` 와 `Playing` 을 나눈 이유가 있다. WebSocket 이 열린 것과 서버가
    /// 룸 슬롯을 주고 Welcome 을 보낸 것은 다르다. 그 사이에 정원 초과로 끊길 수 있고,
    /// 둘을 합치면 "접속했다는데 아무것도 안 보임" 상태가 정상처럼 보인다.
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Playing,
        Failed,
    }

    /// 서버와의 연결 하나. 접속, 수신 디코드, 30Hz 입력 송신까지가 전부다.
    ///
    /// 여기에 게임 로직을 두지 않는다. 스냅샷을 씬에 적용하는 것은
    /// <see cref="NetworkBootstrap"/> 의 몫이고, 이 클래스는 와이어만 다룬다.
    /// 전송 구현(에디터·WebGL)이 갈리는 지점 위에 로직을 얹으면 WebGL 에서만
    /// 나는 버그를 만들게 된다.
    ///
    /// 실행 순서를 앞으로 당겨 둔다. 같은 프레임에 수신한 스냅샷이
    /// 그 프레임의 애니메이션에 반영되어야 한다.
    [DefaultExecutionOrder(-100)]
    public sealed class NetworkClient : MonoBehaviour
    {
        /// 스냅샷 최대 114B. 조작된 크기를 그대로 받지 않기 위해 상한을 둔다.
        private const int ReceiveBytes = 512;

        /// 한 프레임에 꺼낼 메시지 수 상한. 밀린 큐를 따라잡되 프레임을 잡아먹지 않는다.
        private const int MaxReceivesPerFrame = 16;

        private readonly byte[] _receive = new byte[ReceiveBytes];
        private readonly byte[] _send = new byte[MessageCodec.InputWireSize(ProtocolInfo.MaxInputFramesPerMessage)];
        private readonly byte[] _control = new byte[ControlMessage.WireSize];
        private readonly EntityState[] _entities = new EntityState[SnapshotBuffer.MaxEntities];

        /// 룸 명단. 서버가 2Hz 로 보내는 전문을 그대로 담는다.
        private readonly RoomPlayerEntry[] _roster = new RoomPlayerEntry[SnapshotBuffer.MaxEntities];
        private readonly RoomPlayerEntry[] _rosterIncoming = new RoomPlayerEntry[SnapshotBuffer.MaxEntities];
        private int _rosterCount;

        /// 최근 입력 프레임. 손실 대비로 매 메시지에 여러 틱치를 함께 싣는다.
        private readonly InputFrame[] _history = new InputFrame[ProtocolInfo.MaxInputFramesPerMessage];
        private int _historyCount;

        /// 이 시간 안에 소켓이 열리지 않으면 실패로 본다. 서버를 띄우지 않은 채
        /// 접속하면 브라우저는 한참 매달려 있고, 그동안 화면에 아무 설명이 없다.
        private const float ConnectTimeoutSeconds = 8f;

        /// Welcome 이 이 시간 안에 오지 않으면 실패로 본다. 소켓은 열렸는데
        /// 서버가 룸을 주지 않은 상태이며, 정원 초과가 대표적이다.
        private const float WelcomeTimeoutSeconds = 5f;

        private IClientTransport _transport;
        private float _tickAccumulator;
        private uint _inputTick;
        private float _stateElapsed;

        /// <summary>보간 버퍼. 씬에 적용할 상태는 전부 여기서 꺼낸다.</summary>
        public SnapshotBuffer Snapshots { get; private set; }

        /// <summary>접속 단계. UI 와 씬 적용이 함께 보는 유일한 값이다.</summary>
        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

        /// <summary>지금 접속해 있는 주소. UI 표시용이다.</summary>
        public string Endpoint { get; private set; } = string.Empty;

        /// <summary>현재 단계에 머문 시간(초). 접속 대기 표시에 쓴다.</summary>
        public float StateElapsed => _stateElapsed;

        public bool IsConnected => _transport != null && _transport.IsConnected;

        public bool HasWelcome { get; private set; }

        /// <summary>서버가 배정한 우리 엔티티 id.</summary>
        public byte LocalPlayerId { get; private set; }

        /// <summary>서버가 로드한 맵의 해시. 클라이언트 계산값과 다르면 다른 지형에서 시뮬레이션하고 있다.</summary>
        public uint ServerMapHash { get; private set; }

        public byte ServerTickRate { get; private set; }

        /// <summary>서버가 마지막으로 적용한 우리 입력의 틱. 리컨실리에이션(M5)이 쓸 값이다.</summary>
        public uint AckedInputTick { get; private set; }

        /// <summary>보낸 입력 틱과 확인된 틱의 차이. 왕복 지연을 틱으로 본 값이다.</summary>
        public uint InputLag => HasWelcome && _inputTick > AckedInputTick ? _inputTick - AckedInputTick : 0u;

        public string LastError { get; private set; }

        public IInputSource InputSource { get; set; }

        /// <summary>마지막 스냅샷을 받은 시각(unscaled). 0 이면 아직 하나도 받지 않았다.</summary>
        public float LastSnapshotAt { get; private set; }

        /// <summary>스냅샷 도착 간격의 이동 평균(초). 30Hz 면 0.033 근처여야 한다.</summary>
        public float SnapshotInterval { get; private set; }

        /// <summary>관측된 최대 간격(초). 손실과 지터가 여기에 남는다.</summary>
        public float SnapshotIntervalMax { get; private set; }

        /// <summary>마지막 수신 이후 지난 시간(초). 끊김을 눈으로 보는 값이다.</summary>
        public float SinceLastSnapshot =>
            LastSnapshotAt <= 0f ? 0f : Time.unscaledTime - LastSnapshotAt;

        /// <summary>서버가 보낸 마지막 룸 상태 전문. 로비 화면과 매치 시작이 이것만 본다.</summary>
        public RoomStateHeader RoomState { get; private set; }

        public bool HasRoomState { get; private set; }

        /// <summary>명단 길이. 항목은 <see cref="RosterEntry"/> 로 꺼낸다.</summary>
        public int RosterCount => _rosterCount;

        /// <summary>룸의 단계. 전문을 받기 전에는 대기로 본다 — 아직 매치가 아니다.</summary>
        public RoomPhase Phase => HasRoomState ? RoomState.Phase : RoomPhase.Waiting;

        /// <summary>내가 방장인가. 서버는 "너는 방장이다" 를 따로 보내지 않는다.</summary>
        public bool IsLocalHost => HasRoomState && HasWelcome && RoomState.HostPlayerId == LocalPlayerId;

        public RoomPlayerEntry RosterEntry(int index)
        {
            return _roster[index];
        }

        public event Action WelcomeReceived;

        /// 룸 상태가 실제로 바뀌었을 때만 부른다. 전문은 2Hz 로 계속 오지만
        /// 그때마다 UI 를 다시 만들면 로비에서 초당 두 번 트리를 새로 짓는다.
        public event Action RoomStateChanged;

        /// 접속이 끊기거나 실패했을 때. 씬은 이 신호로 원격 몸을 지운다.
        public event Action Ended;

        /// 접속을 시작한다. 핸드셰이크 완료를 기다리지 않는다 — WebGL 은 블로킹할 수 없다.
        public void Connect(
            string host,
            string room,
            bool secure,
            float interpolationDelay,
            string hostToken = null,
            string displayName = null)
        {
            if (_transport != null)
            {
                return;
            }

            Snapshots = new SnapshotBuffer(interpolationDelay);
            _transport = ClientTransportFactory.Create();

            var url = ClientTransportFactory.BuildUrl(host, room, secure, hostToken, displayName);
            Endpoint = url;
            LastError = null;
            State = ConnectionState.Connecting;
            _stateElapsed = 0f;

            ClientTransportFactory.Connect(_transport, url);

            Debug.Log($"[NV] 접속 시도: {url}");
        }

        public void Disconnect()
        {
            var wasActive = _transport != null;

            if (_transport is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _transport = null;
            HasWelcome = false;
            HasRoomState = false;
            _rosterCount = 0;
            _historyCount = 0;
            _tickAccumulator = 0f;
            _stateElapsed = 0f;
            LastSnapshotAt = 0f;
            SnapshotInterval = 0f;
            SnapshotIntervalMax = 0f;
            State = ConnectionState.Disconnected;

            if (wasActive)
            {
                Ended?.Invoke();
            }
        }

        private void Update()
        {
            if (_transport == null)
            {
                return;
            }

            _stateElapsed += Time.unscaledDeltaTime;

            Receive();
            Advance();

            // 룸이 진행 단계일 때만 입력을 보낸다. 대기 중에 보내면 서버가 버리므로
            // 동작은 같지만, 로비 화면에서 30Hz 로 프레임을 밀어 넣을 이유가 없다.
            if (State == ConnectionState.Playing && Phase == RoomPhase.Playing)
            {
                SendInput();
            }
        }

        /// 룸에 요청을 보낸다. 자격 판정은 서버가 한다 — 방장이 아닌 클라이언트가
        /// 시작을 눌러도 조용히 무시되며, 그 판단을 여기서 미리 하지 않는다.
        public bool SendControl(ControlKind kind, byte value = 0)
        {
            if (_transport == null || !_transport.IsConnected)
            {
                return false;
            }

            var length = MessageCodec.WriteControl(_control, new ControlMessage(kind, value));
            return _transport.TrySend(new ReadOnlySpan<byte>(_control, 0, length), Reliability.Reliable);
        }

        /// 단계 전이와 실패 판정. 여기 한 곳에서만 State 를 바꾼다 —
        /// 여러 곳에서 바꾸면 어느 경로로 Failed 가 되었는지 알 수 없게 된다.
        private void Advance()
        {
            if (ClientTransportFactory.HasFailed(_transport))
            {
                Fail(ClientTransportFactory.FailureReason(_transport));
                return;
            }

            switch (State)
            {
                case ConnectionState.Connecting:
                    if (_transport.IsConnected)
                    {
                        State = ConnectionState.Connected;
                        _stateElapsed = 0f;
                    }
                    else if (_stateElapsed > ConnectTimeoutSeconds)
                    {
                        Fail($"{ConnectTimeoutSeconds:F0}초 안에 접속되지 않았다. 서버가 떠 있는지 확인한다.");
                    }

                    break;

                case ConnectionState.Connected:
                    if (HasWelcome)
                    {
                        State = ConnectionState.Playing;
                        _stateElapsed = 0f;
                    }
                    else if (_stateElapsed > WelcomeTimeoutSeconds)
                    {
                        Fail("소켓은 열렸지만 서버가 Welcome 을 보내지 않았다. 룸 정원이 찼을 수 있다.");
                    }

                    break;

                case ConnectionState.Playing:
                    if (!_transport.IsConnected)
                    {
                        Fail("연결이 끊겼다.");
                    }

                    break;
            }
        }

        private void Fail(string reason)
        {
            if (State == ConnectionState.Failed)
            {
                return;
            }

            LastError = string.IsNullOrEmpty(reason) ? "알 수 없는 오류" : reason;
            Debug.LogWarning("[NV] 접속 실패: " + LastError);

            if (_transport is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _transport = null;
            HasWelcome = false;
            _historyCount = 0;
            State = ConnectionState.Failed;
            _stateElapsed = 0f;

            Ended?.Invoke();
        }

        private void OnDestroy()
        {
            Disconnect();
        }

        private void Receive()
        {
            for (var pass = 0; pass < MaxReceivesPerFrame; pass++)
            {
                var length = _transport.Receive(_receive);
                if (length <= 0)
                {
                    return;
                }

                Dispatch(length);
            }
        }

        private void Dispatch(int length)
        {
            var payload = new ReadOnlySpan<byte>(_receive, 0, length);

            switch (MessageCodec.ReadOpcode(payload))
            {
                case MessageOpcode.Welcome:
                    ReadWelcome(payload);
                    break;

                case MessageOpcode.Snapshot:
                    ReadSnapshot(payload);
                    break;

                case MessageOpcode.Event:
                    DispatchEvent(payload);
                    break;

                default:
                    // 정의되지 않은 값. 손상되었거나 조작된 프레임이다.
                    break;
            }
        }

        /// `Event` 프레임을 종류별로 가른다.
        ///
        /// **종류를 먼저 보지 않으면 새 전문이 추가된 순간 매 프레임 파싱 예외가 난다.**
        /// 예전에는 `Event` 를 무조건 룸 상태로 파싱했고, `ReadRoomState` 가 종류 불일치를
        /// 예외로 던지므로 서버가 다른 전문을 보내기 시작하면 2Hz 로 `LastError` 가
        /// 덮여 화면에 네트워크 오류가 뜬다.
        private void DispatchEvent(ReadOnlySpan<byte> payload)
        {
            switch (MessageCodec.ReadEventKind(payload))
            {
                case EventKind.RoomState:
                    ReadRoomState(payload);
                    break;

                case EventKind.MatchState:
                    // 서버는 이미 매치 단계와 시계를 보내고 있다. 이 클라이언트는 아직
                    // 자기 시계로 매치를 돌리므로 지금은 받아만 두고 버린다 — 적용은
                    // MatchManager 가 심판에서 뷰로 바뀔 때(IG-010) 붙는다.
                    break;

                default:
                    // 모르는 종류. 서버가 앞서 나갔거나 프레임이 손상되었다.
                    break;
            }
        }

        /// 룸 상태 전문을 받는다.
        ///
        /// 서버는 이것을 2Hz 로 계속 보낸다. 한 번짜리 "시작했다" 알림이 아니라
        /// 멱등한 전문이므로, 프레임 하나를 놓쳐도 다음 것으로 따라잡는다.
        private void ReadRoomState(ReadOnlySpan<byte> payload)
        {
            RoomStateHeader header;
            int count;

            // 들어오는 전문을 별도 버퍼로 읽는다. 바로 _roster 에 읽으면 비교할 이전
            // 명단이 사라지고, 그러면 한 명이 나가고 다른 한 명이 들어온 전문을
            // "바뀐 것 없음" 으로 판단해 화면에 나간 사람의 이름이 남는다.
            try
            {
                count = MessageCodec.ReadRoomState(payload, out header, _rosterIncoming);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException)
            {
                LastError = exception.Message;
                return;
            }

            var changed = !HasRoomState || Differs(header, count);

            RoomState = header;
            for (var index = 0; index < count; index++)
            {
                _roster[index] = _rosterIncoming[index];
            }

            _rosterCount = count;
            HasRoomState = true;

            if (changed)
            {
                RoomStateChanged?.Invoke();
            }
        }

        /// 새 전문이 지금 들고 있는 것과 다른가.
        private bool Differs(in RoomStateHeader header, int count)
        {
            if (RoomState.Phase != header.Phase
                || RoomState.HostPlayerId != header.HostPlayerId
                || RoomState.SeekerPlayerId != header.SeekerPlayerId
                || RoomState.Outcome != header.Outcome
                || RoomState.StartTick != header.StartTick
                || RoomState.PlacementSeed != header.PlacementSeed
                || _rosterCount != count)
            {
                return true;
            }

            for (var index = 0; index < count; index++)
            {
                if (_roster[index].PlayerId != _rosterIncoming[index].PlayerId
                    || !string.Equals(_roster[index].Name, _rosterIncoming[index].Name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void ReadWelcome(ReadOnlySpan<byte> payload)
        {
            WelcomeMessage welcome;
            try
            {
                welcome = MessageCodec.ReadWelcome(payload);
            }
            catch (InvalidOperationException exception)
            {
                LastError = exception.Message;
                return;
            }

            if (welcome.ProtocolVersion != ProtocolInfo.Version)
            {
                // 서버는 업그레이드 전에 이미 버전을 검사한다. 여기까지 온 불일치는
                // 쿼리스트링과 실제 빌드가 어긋난 경우다.
                LastError = $"프로토콜 버전 불일치: 서버 {welcome.ProtocolVersion}, 클라이언트 {ProtocolInfo.Version}";
                Debug.LogError("[NV] " + LastError);
                return;
            }

            LocalPlayerId = welcome.PlayerId;
            ServerMapHash = welcome.MapHash;
            ServerTickRate = welcome.TickRate;

            // 입력 틱은 서버 틱보다 조금 앞선 값에서 시작한다. 도착이 늦은 입력은
            // 서버가 이미 지나간 틱이라 버려지므로, 약간 앞서 보내는 편이 안전하다.
            _inputTick = welcome.ServerTick + 2u;
            AckedInputTick = welcome.ServerTick;
            HasWelcome = true;

            Debug.Log($"[NV] Welcome: 플레이어 {LocalPlayerId}, 서버 틱 {welcome.ServerTick}, " +
                      $"{welcome.TickRate}Hz, 맵 해시 {welcome.MapHash:X8}");

            WelcomeReceived?.Invoke();
        }

        private void ReadSnapshot(ReadOnlySpan<byte> payload)
        {
            SnapshotHeader header;
            int count;

            try
            {
                count = MessageCodec.ReadSnapshot(payload, out header, _entities);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException)
            {
                LastError = exception.Message;
                return;
            }

            if (header.AckedInputTick > AckedInputTick)
            {
                AckedInputTick = header.AckedInputTick;
            }

            var now = Time.unscaledTime;

            // 도착 간격을 재 둔다. "안 되는데요" 를 수치로 갈라내는 첫 값이며,
            // 평균이 33ms 근처인데 최대가 크면 지터, 평균 자체가 크면 손실이다.
            if (LastSnapshotAt > 0f)
            {
                var gap = now - LastSnapshotAt;

                SnapshotInterval = SnapshotInterval <= 0f
                    ? gap
                    : (SnapshotInterval * 0.9f) + (gap * 0.1f);

                if (gap > SnapshotIntervalMax)
                {
                    SnapshotIntervalMax = gap;
                }
            }

            LastSnapshotAt = now;

            Snapshots.Add(header.Tick, _entities, count, now);
        }

        /// 서버와 같은 고정 델타로 입력을 만든다. 렌더 프레임레이트에 묶으면
        /// 프레임이 높은 클라이언트가 더 많은 입력을 보내 이동 거리가 달라진다.
        private void SendInput()
        {
            if (!HasWelcome || InputSource == null || !_transport.IsConnected)
            {
                return;
            }

            _tickAccumulator += Time.unscaledDeltaTime;

            // 한 프레임에 여러 틱을 몰아 보내되 상한을 둔다. 탭이 백그라운드로 갔다
            // 돌아오면 누적이 몇 초가 되는데, 그것을 전부 보내면 서버가 도약으로 보고 막는다.
            var budget = ProtocolInfo.MaxInputFramesPerMessage;

            while (_tickAccumulator >= SimConstants.TickDelta && budget-- > 0)
            {
                _tickAccumulator -= SimConstants.TickDelta;
                PushAndSend(InputSource.Sample());
            }

            if (_tickAccumulator > SimConstants.TickDelta)
            {
                _tickAccumulator = 0f;
            }
        }

        private void PushAndSend(in InputFrame frame)
        {
            // history[0] 이 최신이고 뒤로 갈수록 과거다. 와이어 규약과 같은 순서다.
            for (var index = _history.Length - 1; index > 0; index--)
            {
                _history[index] = _history[index - 1];
            }

            _history[0] = frame;
            if (_historyCount < _history.Length)
            {
                _historyCount++;
            }

            var length = MessageCodec.WriteInput(
                _send,
                _inputTick,
                new ReadOnlySpan<InputFrame>(_history, 0, _historyCount));

            _transport.TrySend(new ReadOnlySpan<byte>(_send, 0, length), Reliability.Unreliable);
            _inputTick++;
        }
    }
}
