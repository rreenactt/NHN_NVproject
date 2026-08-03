using NV.Game;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using UnityEngine;

namespace NV.Client.Net.Session
{
    /// 서버의 룸 단계를 매치 레이어에 옮긴다. 게임 씬에 하나 둔다.
    ///
    /// 옮기는 것은 넷이다 — 언제 시작하는지, 누가 Seeker 인지, 목표물을 어느 씨드로
    /// 배치하는지, 언제 끝났는지. 그 넷이 서버에서 오지 않으면 클라이언트마다 다른
    /// 게임이 된다. 특히 씨드가 그렇다: `MatchManager` 는 씨드가 없으면 자기 시계로
    /// 난수를 만들고, 그러면 플레이어마다 문과 열쇠가 다른 곳에 생긴다. 증상은
    /// "남이 없는 문에 열쇠를 꽂는다" 로 나타나 네트워크 문제로 보이지 않는다.
    ///
    /// 규칙 판정은 아직 클라이언트에 있다. 이 컴포넌트는 그 판정을 대체하지 않고,
    /// 판정이 서버로 옮겨갈 때 갈아 끼울 자리를 지금 확정해 둔다.
    ///
    /// 실행 순서를 `MatchBootstrap`(-70) 앞에 둔다. 세션이 있으면 자동 시작을 꺼야
    /// 하고, 그 결정은 부트스트랩의 Start 보다 먼저 나야 한다.
    [DefaultExecutionOrder(-75)]
    public sealed class MatchSync : MonoBehaviour
    {
        /// 명단 전원의 몸이 도착할 때까지 기다리는 시간(초).
        ///
        /// 원격 몸은 첫 스냅샷이 와야 만들어진다. 그 전에 매치를 시작하면 늦게 도착한
        /// 플레이어가 역할 없이 남는다 — `BeginMatch` 는 그 시점의 명단에만 역할을 준다.
        private const float BodyWaitSeconds = 5f;

        private NetworkBootstrap _bootstrap;
        private MatchBootstrap _matchBootstrap;
        private NetSession _session;
        private NetworkClient _client;

        private bool _pendingStart;
        private float _pendingSince;
        private uint _startedTick;
        private bool _subscribedToMatch;

        private void Awake()
        {
            _matchBootstrap = FindFirstObjectByType<MatchBootstrap>();

            // 세션이 있으면 매치는 서버의 시작 신호로만 시작한다. 자동 시작을 켜 둔
            // 채로 두면 각 클라이언트가 자기 씨드로 자기 매치를 먼저 시작한다.
            if (_matchBootstrap != null && NetSession.Exists)
            {
                _matchBootstrap.autoStart = false;

                // 디버그 키(F1 역할 교체, F2 재시작)는 로컬에서 매치를 다시 시작한다.
                // 네트워크 매치에서는 그 한 번으로 그 클라이언트만 다른 배치를 갖는다.
                _matchBootstrap.debugKeys = false;
            }
        }

        private void Start()
        {
            _bootstrap = FindFirstObjectByType<NetworkBootstrap>();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (!Bind())
            {
                return;
            }

            if (_pendingStart)
            {
                TryBeginMatch();
            }

            ApplyMatchState();
        }

        /// 서버의 매치 전문을 매치 레이어에 옮긴다.
        ///
        /// 폴링이다. 전문은 2Hz 로 오면서 시계를 싣고 있으므로 "바뀌었다" 는 신호에 정보가
        /// 없고, `NetworkClient` 도 그래서 변경 이벤트를 두지 않았다.
        ///
        /// 매치가 시작되기 전에는 적용할 것이 없다. 서버는 로비 단계에서 이 전문을 아예
        /// 보내지 않으므로 `HasMatchState` 가 false 다.
        private void ApplyMatchState()
        {
            if (_client == null || !_client.HasMatchState)
            {
                return;
            }

            var state = _client.MatchState;

            MatchManager.Instance.AcceptMatchState(
                state.Phase,
                MatchStateHeader.FromTenths(state.TimeRemainingTenths));
        }

        /// 세션·클라이언트·매치가 모두 준비됐는가.
        ///
        /// 늦게 붙는다. 세션은 씬보다 오래 살고 매치 매니저는 이 씬에서 만들어지므로,
        /// 어느 쪽이 먼저인지 순서로 보장하지 않고 매 프레임 확인한다.
        private bool Bind()
        {
            if (!NetSession.Exists)
            {
                return false;
            }

            if (_session == null)
            {
                _session = NetSession.Current;
                _client = _session.Client;

                if (_client != null)
                {
                    _client.RoomStateChanged += OnRoomStateChanged;
                }
            }

            if (!_subscribedToMatch && MatchManager.Instance != null)
            {
                MatchManager.Instance.MatchEnded += OnLocalMatchEnded;
                _subscribedToMatch = true;
            }

            return _client != null && MatchManager.Instance != null;
        }

        private void Unsubscribe()
        {
            if (_client != null)
            {
                _client.RoomStateChanged -= OnRoomStateChanged;
            }

            if (_subscribedToMatch && MatchManager.Instance != null)
            {
                MatchManager.Instance.MatchEnded -= OnLocalMatchEnded;
            }

            _subscribedToMatch = false;
        }

        private void OnRoomStateChanged()
        {
            if (_client == null || !_client.HasRoomState || MatchManager.Instance == null)
            {
                return;
            }

            var state = _client.RoomState;

            switch (state.Phase)
            {
                case RoomPhase.Playing:
                    // 같은 매치의 전문은 2Hz 로 계속 온다. 시작 틱으로 판별해야
                    // 한 매치를 초당 두 번 다시 시작하지 않는다.
                    if (_startedTick != state.StartTick)
                    {
                        _startedTick = state.StartTick;
                        _pendingStart = true;
                        _pendingSince = Time.unscaledTime;
                    }

                    break;

                case RoomPhase.Ended:
                    _pendingStart = false;
                    AcceptRemoteOutcome(state.Outcome);
                    break;

                case RoomPhase.Waiting:
                    _pendingStart = false;
                    _startedTick = 0u;

                    // NV.Game.MatchPhase 로 수식한다. 서버도 이제 자기 매치 단계를
                    // 갖고(NV.Shared.Contracts.Enums.MatchPhase) 두 이름이 겹친다.
                    // 값은 같으며, 통합은 이 컴포넌트가 전문을 받게 될 때(IG-010) 한다.
                    if (MatchManager.Instance.Phase != NV.Game.MatchPhase.Lobby)
                    {
                        MatchManager.Instance.ReturnToLobby();
                    }

                    break;
            }
        }

        /// 명단 전원의 몸이 도착했으면 매치를 시작한다.
        private void TryBeginMatch()
        {
            var match = MatchManager.Instance;
            var expectedRemotes = Mathf.Max(0, _client.RosterCount - 1);
            var waited = Time.unscaledTime - _pendingSince;
            var everyoneHere = _bootstrap != null && _bootstrap.PuppetCount >= expectedRemotes;

            if (!everyoneHere && waited < BodyWaitSeconds)
            {
                return;
            }

            _pendingStart = false;

            if (!everyoneHere)
            {
                // 몸이 오지 않은 플레이어는 역할을 받지 못한다. 조용히 시작하면
                // 그 플레이어가 화면에서 관전자처럼 보이므로 남겨 둔다.
                Debug.LogError(
                    $"[NV] 원격 몸 {_bootstrap?.PuppetCount ?? 0}/{expectedRemotes} 만 도착한 채로 매치를 시작한다. " +
                    "스냅샷이 오지 않는지 확인한다.");
            }

            var state = _client.RoomState;

            match.PlacementSeedOverride = state.PlacementSeed;
            match.ServerPlacesAgents = true;

            // 결과를 판정하는 클라이언트는 방장 하나다. 전원이 각자 판정하면 서로
            // 다른 순간에 매치를 끝내고 결과도 갈린다.
            match.ResolvesOutcome = _session.IsHost;

            var seeker = FindAgent(state.SeekerPlayerId);

            if (seeker == null)
            {
                Debug.LogError(
                    $"[NV] Seeker(플레이어 {state.SeekerPlayerId}) 의 몸을 찾지 못했다. " +
                    "그 플레이어 없이 시작한다.");
            }

            match.BeginMatch(seeker);

            Debug.Log(
                $"[NV] 매치 시작. 틱 {state.StartTick}, Seeker 플레이어 {state.SeekerPlayerId}, " +
                $"배치 씨드 {state.PlacementSeed}, 판정 {(match.ResolvesOutcome ? "이 클라이언트" : "방장")}");
        }

        /// PlayerId 로 참가자를 찾는다. 로컬은 자기 자신, 나머지는 원격 몸이다.
        private PlayerAgent FindAgent(byte playerId)
        {
            if (playerId == RoomStateHeader.NoPlayer)
            {
                return null;
            }

            if (_client.HasWelcome && playerId == _client.LocalPlayerId)
            {
                return MatchManager.Instance.LocalAgent;
            }

            return _bootstrap != null && _bootstrap.TryGetPuppet(playerId, out var puppet)
                ? puppet.Agent
                : null;
        }

        /// 방장이 판정한 결과를 받는다.
        private void AcceptRemoteOutcome(byte outcome)
        {
            var match = MatchManager.Instance;

            if (match.Phase == NV.Game.MatchPhase.Ended)
            {
                return;
            }

            match.AcceptOutcome((MatchOutcome)outcome);
        }

        /// 방장의 매치가 끝나면 서버에 보고한다. 나머지 클라이언트는 전문으로 받는다.
        ///
        /// 결과를 판정한 것은 이 클라이언트다. 규칙이 서버로 옮겨가면 이 경로가 사라진다.
        private void OnLocalMatchEnded(MatchOutcome outcome)
        {
            if (_session == null || !_session.IsHost)
            {
                return;
            }

            _session.ReportMatchEnd((byte)outcome);
        }
    }
}
