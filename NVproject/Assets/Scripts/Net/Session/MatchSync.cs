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

        /// 이미 적용한 배치의 특징. 전문은 5초마다 같은 내용으로 다시 오므로, 그때마다
        /// 목표물을 다시 만들면 초당 오브젝트를 지우고 세우는 일이 반복된다.
        ///
        /// 열쇠 수로 판별한다 — 배치가 바뀌는 실질적인 경우는 열쇠가 주워지거나 흘려지는
        /// 것이고, 그것이 곧 개수 변화다.
        private int _appliedObjectiveKeys = -1;
        private bool _appliedObjectiveDoor;
        private bool _appliedObjectivePlacement;

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

            ApplyRoomPhase();

            if (_pendingStart)
            {
                TryBeginMatch();
            }

            ApplyMatchState();
            ApplyObjectiveState();
        }

        /// 서버가 배치한 목표물을 매치 레이어에 넘긴다.
        ///
        /// 전문은 5초 주기 + 변경 즉시로 온다. 매치가 시작된 뒤에 도착할 수도 있으므로
        /// (`TryBeginMatch` 가 목표물을 만들지 않고 기다린다) 새 전문이 올 때마다 적용한다.
        ///
        /// **문이 실려 오지 않는 것이 정상인 경우가 있다.** 이 클라이언트가 Seeker 라면
        /// 서버가 그 블록을 빼고 보낸다 — `HasObjectiveDoor` 가 false 이고, 문 오브젝트를
        /// 만들지 않는 것이 맞다.
        private void ApplyObjectiveState()
        {
            if (_client == null || !_client.HasObjectiveState)
            {
                return;
            }

            // **장치 상태는 배치와 달리 매 프레임 넘긴다.** 아래의 "같은 배치면 건너뛴다" 는
            // 오브젝트를 다시 만들지 않기 위한 것이고, 소진·쿨다운·정지는 배치가 그대로인 채로
            // 바뀌는 값이라 그 조건에 걸리면 영원히 반영되지 않는다. 적용은 멱등하다.
            NV.Game.DeviceSystem.Instance?.AcceptServerStates(_client);

            // 같은 배치를 매 프레임 다시 만들지 않는다. 전문이 갱신될 때만 적용한다.
            if (_appliedObjectiveKeys == _client.Objectives.Keys.Count
                && _appliedObjectiveDoor == _client.HasObjectiveDoor
                && _appliedObjectivePlacement)
            {
                return;
            }

            _appliedObjectiveKeys = _client.Objectives.Keys.Count;
            _appliedObjectiveDoor = _client.HasObjectiveDoor;
            _appliedObjectivePlacement = true;

            MatchManager.Instance.AcceptObjectiveState(_client.Objectives, _client.HasObjectiveDoor);
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

            // 목표 진행도. 문 개방은 **목표물 전문**에서 온다 — 두 전문의 주기가 다르므로
            // (2Hz / 5초) 여기서 함께 넘기면 늦게 온 쪽이 다음 폴링에 반영된다. 두 값 모두
            // 멱등이라 매 프레임 넘겨도 무해하고, 문 오브젝트가 아직 없으면
            // `AcceptObjectiveProgress` 가 넘긴다 — 다음 폴링에 다시 온다.
            MatchManager.Instance.AcceptObjectiveProgress(
                state.KeysInserted,
                _client.ObjectiveDoorOpen);

            // 탈출 수는 역할과 무관하게 실제 값이 온다 — Seeker 가 막아야 하는 수다.
            MatchManager.Instance.AcceptEscapes(state.Escapes);

            ApplyParticipants();
        }

        /// 참가자별 상태를 몸에 적용한다 — 소지 열쇠(IG-012a)와 탈출(IG-012c2).
        ///
        /// 명단 전체를 훑는다. 로컬만 적용하면 남이 든 열쇠 수와 남의 탈출이 이 클라이언트에서
        /// 영원히 반영되지 않는다.
        ///
        /// 몸이 아직 없는 참가자는 건너뛴다. 원격 몸은 첫 스냅샷이 와야 생기므로 전문이
        /// 먼저 도착하는 것이 정상이고, 다음 전문이 0.5초 뒤에 같은 값을 다시 싣고 온다.
        private void ApplyParticipants()
        {
            var count = _client.ParticipantCount;

            // 문턱에 선 사람의 진행도. **가장 큰 값 하나만 본다** — 화면이 답해야 하는 것은
            // "누가" 가 아니라 "지금 누군가 나가는 중인가" 이고, 그것이 룰셋이 이 값을
            // 공개로 둔 이유다(끊을 수 있어야 한다).
            var escaping = 0f;

            for (var index = 0; index < count; index++)
            {
                var participant = _client.MatchParticipantAt(index);
                var agent = FindAgent(participant.PlayerId);

                if (agent == null)
                {
                    continue;
                }

                if (_client.Snapshots != null
                    && _client.Snapshots.TryLatest(participant.PlayerId, out var live))
                {
                    escaping = Mathf.Max(escaping, live.EscapeProgress / 255f);
                }

                MatchManager.Instance.AcceptCarriedKeys(agent, participant.CarriedKeys);
                MatchManager.Instance.AcceptAmmo(agent, participant.Ammo);
                ApplyBody(participant, agent);
            }

            MatchManager.Instance.AcceptEscapeProgress(escaping);
        }

        /// 몸에 실리는 서버 판정 — 탈출·출혈·쓰러짐.
        ///
        /// **수는 전문에서, 상태는 스냅샷에서 온다.** 전문(2Hz)이 피격 수를 싣고, 플래그는 매 틱
        /// 오는 스냅샷에 있다 — 출혈 흔적과 사라지는 몸은 0.5초를 기다릴 수 없기 때문이다
        /// (`EntityFlags` 주석이 그 기준을 적어 두었다).
        ///
        /// 보간하지 않은 최신 값을 읽는다(`TryLatest`). 보간은 위치를 위한 것이고, 불리언을 두
        /// 스냅샷 사이에서 섞으면 켜졌다 꺼졌다 한다.
        private void ApplyBody(MatchParticipant participant, PlayerAgent agent)
        {
            if (_client.Snapshots == null
                || !_client.Snapshots.TryLatest(participant.PlayerId, out var sample))
            {
                return;
            }

            var flags = sample.Flags;

            if (!agent.Escaped && (flags & NV.Shared.Contracts.Enums.EntityFlags.Escaped) != 0)
            {
                MatchManager.Instance.AcceptEscaped(agent);
            }

            MatchManager.Instance.AcceptCombatState(
                agent,
                participant.Hits,
                (flags & NV.Shared.Contracts.Enums.EntityFlags.Bleeding) != 0,
                (flags & NV.Shared.Contracts.Enums.EntityFlags.Downed) != 0);

            // 체인. **빈 탄창이 아니라 이 비트가 신호다** — 탄창은 서버의 것이고 전문이 매 프레임
            // 로컬 값을 덮어쓰므로, 로컬 `Fire` 가 0 을 보는 일이 없어 체인이 아예 그려지지
            // 않았다(서버는 끌고 가는데 사슬만 없었다). 서버는 체인과 매치 전체의 이동 잠금을
            // 한 비트에 함께 싣고(`Room` 의 플래그 인코딩), 잠금은 리빌과 종료 단계에만 걸린다 —
            // 그래서 단계로 가른다. 명단 전원에 적용하므로 남이 끌려가는 것도 보인다.
            //
            // **전체 정지 장치도 같은 비트를 켠다.** 그 장치가 도는 동안에는 전원이 `Frozen`
            // 이므로, 빼지 않으면 Seeker 에게 제단까지 사슬이 그려진다 — 못 움직이는 이유가
            // 체인이 아닌데도. 목표물 전문이 그 장치를 `Active` 로 싣고 있으므로 그것으로 가른다.
            MatchManager.Instance.AcceptChained(
                agent,
                MatchManager.Instance.Phase == NV.Game.MatchPhase.Playing
                && !_client.DeviceFreezeActive
                && (flags & NV.Shared.Contracts.Enums.EntityFlags.Frozen) != 0);
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
            if (_subscribedToMatch && MatchManager.Instance != null)
            {
                MatchManager.Instance.MatchEnded -= OnLocalMatchEnded;
            }

            _subscribedToMatch = false;
        }

        /// 룸 단계를 매치 레이어에 옮긴다. **전문을 그대로 읽는다. 변경 이벤트를 듣지 않는다.**
        ///
        /// 예전에는 `NetworkClient.RoomStateChanged` 로만 왔고, 그것이 네트워크 매치를 통째로
        /// 죽였다 — `Playing` 으로 바뀌는 그 전문이 **게임 씬을 여는 원인**이고, 이 컴포넌트는
        /// 그 씬에서 만들어지므로 구독하는 시점에는 이미 지나간 뒤다. 전문은 내용이 바뀔 때만
        /// 이벤트를 올리고 단계는 다시 바뀌지 않으므로, `_pendingStart` 가 영원히 서지 않고
        /// 매치가 시작되지 않는다. 증상은 서버는 매치를 돌리는데 클라이언트는 로비 단계에
        /// 머무는 것 — 역할이 없고, 서버 권위 플래그가 전부 꺼져 있고, 목표물이 없다.
        ///
        /// `RoomState` 는 알림이 아니라 **게시**다(ADR 0003). 2Hz 로 전체가 계속 오고 멱등하며,
        /// 같은 매치를 두 번 시작하는 것은 `_startedTick` 이 이미 막는다. 그러니 지금 온 것을
        /// 읽으면 된다 — 놓칠 수 있는 순간이 없다.
        private void ApplyRoomPhase()
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

                    // 다음 매치는 새 배치를 받는다. 여기서 지우지 않으면 열쇠 수가 우연히
                    // 같을 때 두 번째 매치가 첫 매치의 목표물을 그대로 쓴다.
                    _appliedObjectivePlacement = false;
                    _appliedObjectiveKeys = -1;

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

            match.ServerPlacesAgents = true;

            // 목표물은 서버의 것이다 — 놓는 것도, 그것에 대한 판정도. 이 클라이언트는
            // 전문을 기다리고, 열쇠 습득·삽입·문 개방을 스스로 판정하지 않는다.
            //
            // **씨드를 넘기지 않는다. 넘길 씨드가 이제 없다.** 그 값이 와이어에서 빠졌고
            // (`RoomStateHeader`), 그래서 이 클라이언트에는 문의 좌표를 계산할 입력이 없다 —
            // 배치 함수를 갖고 있어도(ADR 0002) 계산할 수 없다. 컬링 레이어가 화면에서 가리는
            // 것과 달리 이것은 디컴파일로 되살릴 수 없다.
            match.ServerOwnsObjectives = true;

            // 전투도 서버의 것이다 — 총알이 어디로 가고 누가 맞고 그 대가가 무엇인지.
            // 목표물과 별개의 플래그인 이유는 `MatchManager` 쪽에 적혀 있다.
            match.ServerOwnsCombat = true;

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
                $"판정 {(match.ResolvesOutcome ? "이 클라이언트" : "방장")}, 목표물은 서버 배치");
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
