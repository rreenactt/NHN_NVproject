using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Microsoft.Extensions.Logging;
using NV.Realtime.Contracts;
using NV.Realtime.Simulation.Bots;
using NV.Realtime.Transport;
using NV.Shared.Collision;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using NV.Shared.Simulation;
using NV.Shared.Transport;

namespace NV.Realtime.Simulation
{
    /// 룸 하나. 상태 소유자는 틱 루프다.
    ///
    /// 단계가 셋이다. `Waiting` 은 명단을 모으는 중이고, `Playing` 만 시뮬레이션하며,
    /// `Ended` 는 결과 화면이다. 대기 중에 시뮬레이션하지 않는 이유는 절약이 아니라
    /// 의미다 — 아직 매치가 아닌 시간에 서버가 이동을 판정하면 로비에서 서로를 밀 수 있다.
    ///
    /// 틱은 단계와 무관하게 계속 올린다. Welcome 이 이 틱을 싣고 클라이언트가 그것을
    /// 기준으로 입력 틱을 잡으므로, 대기 중에 시계를 멈추면 시작 순간에 기준이 어긋난다.
    ///
    /// 스레드 경계
    /// - 틱 루프: _players, Tick, 단계, 방장, 스냅샷 버퍼, 슬롯 해제, 시뮬레이션
    /// - 다른 스레드: PostCommand, PostInput, TryReserveSlot, Summarize 만 호출
    internal sealed class Room
    {
        private readonly ConcurrentQueue<RoomCommand> _commands = new();
        private readonly ConcurrentQueue<InboundInput> _inputs = new();
        private readonly Dictionary<int, PlayerEntity> _players = new();

        /// 도착 시점이 아직 안 된 입력. 틱 루프만 만진다.
        private readonly List<InboundInput> _heldInputs = new();

        /// 지울 참가자의 세션 id 를 모아 두는 자리. 순회 중에 딕셔너리를 바꿀 수 없다.
        private readonly List<int> _removalBuffer = new();

        private readonly EntityState[] _entityBuffer = new EntityState[RealtimeConstants.Rooms.MaxPlayers];
        private readonly byte[] _sendBuffer = new byte[MessageCodec.SnapshotWireSize(RealtimeConstants.Rooms.MaxPlayers)];

        private readonly RoomPlayerEntry[] _rosterBuffer = new RoomPlayerEntry[RealtimeConstants.Rooms.MaxPlayers];
        private readonly byte[] _stateBuffer = new byte[MessageCodec.RoomStateMaxWireSize(RealtimeConstants.Rooms.MaxPlayers)];

        private readonly MatchParticipant[] _participantBuffer = new MatchParticipant[RealtimeConstants.Rooms.MaxPlayers];
        private readonly byte[] _matchStateBuffer = new byte[MessageCodec.MatchStateMaxWireSize(RealtimeConstants.Rooms.MaxPlayers)];

        /// 목표물 전문 버퍼. 열쇠는 사망 시 흘려질 수 있어 배치 수보다 늘어날 수 있으므로
        /// 정원만큼 여유를 둔다 — 8명이 각자 열쇠를 들고 죽어도 담긴다.
        private readonly ObjectivePoint[] _keyBuffer =
            new ObjectivePoint[MatchConstants.KeysPlaced + RealtimeConstants.Rooms.MaxPlayers];

        private readonly ObjectiveDevice[] _deviceBuffer =
            new ObjectiveDevice[MatchConstants.DeviceMix.Length];

        private readonly byte[] _objectiveStateBuffer = new byte[MessageCodec.ObjectiveStateMaxWireSize(
            MatchConstants.KeysPlaced + RealtimeConstants.Rooms.MaxPlayers,
            MatchConstants.DeviceMix.Length)];

        private readonly bool[] _slots = new bool[RealtimeConstants.Rooms.MaxPlayers];
        private readonly object _slotGate = new();

        /// 매치의 단계와 시계. 틱 루프만 만진다.
        ///
        /// 룸 단계(`_phase`)와 다른 축이다. 룸이 `Playing` 인 동안 매치는 역할 공개
        /// 중일 수 있고, 그때는 이동만 잠긴다 — 시뮬레이션은 계속 돌아야 한다.
        private readonly Match _match = new();

        /// 이 매치의 목표물 — 제단·문·열쇠·장치. 틱 루프만 만진다.
        ///
        /// 서버가 배치한다. 지금까지는 배치 씨드를 내려보내 **모든 클라이언트가 같은 씨드로
        /// 계산**했고, 그래서 문의 좌표가 Seeker 의 메모리에도 있었다. 좌표를 역할별로 걸러
        /// 내려보내는 것은 다음 태스크(IG-011b)이고, 여기서는 서버가 자기 배치를 갖는다.
        private readonly Objectives _objectives = new();

        /// 날아가는 총알. 슬롯을 재사용하므로 할당이 없다.
        ///
        /// 32 는 규칙이 아니라 상한이다. 탄창 3발에 재장전이 없으므로 지금 실제로 도달할 수 있는
        /// 수는 3 이고, 재장전(IG-016)이 들어와도 수명 90틱 / 간격 5틱 = 18 이 한 사람의 최대다.
        private readonly Projectile[] _projectiles = new Projectile[32];

        /// 피격 순간이동의 착지점을 뽑는 난수. **배치와 같은 씨드를 쓰지 않는다** — 같은
        /// 수열을 두 용도가 나눠 쓰면 배치가 한 번 더 뽑는 변경이 순간이동 착지점을 바꾼다.
        private DeterministicSequence _hitRandom;

        /// 이 틱에 나갈 발사 알림. **반복하지 않으므로 보낸 뒤 비운다**(ADR 0003).
        private readonly FireEventMessage[] _pendingFires = new FireEventMessage[32];

        private readonly byte[] _fireEventBuffer = new byte[FireEventMessage.WireSize];

        private int _pendingFireCount;

        private readonly WorldMap _map;
        private readonly NetworkConditionSimulator _network;
        private readonly ILogger _logger;
        private readonly bool _isStatic;

        /// 봇 참가자 설정. 꺼져 있거나 정적 룸이 아니면 아무 일도 하지 않는다.
        private readonly BotOptions _bots;

        /// 이 방을 `GET /rooms` 목록에 실을 것인가.
        ///
        /// 방을 만든 사람이 정하고 바꿀 수 없다. 만든 뒤에 공개로 돌릴 수 있게 하면,
        /// 비공개인 줄 알고 코드를 나눈 사람들의 방이 나중에 목록에 뜬다.
        ///
        /// 비공개 방도 코드로는 그대로 들어온다. 목록에서 빠질 뿐 접근이 막히는 것이
        /// 아니며, `GET /rooms/{code}` 와 `/ws` 는 이 값을 보지 않는다 — 그쪽을 막으면
        /// 초대 코드 자체가 동작하지 않는다.
        private readonly bool _isPublic;

        private uint _tick;
        private int _playerCount;

        /// int 로 둔다. 조회 스레드가 Volatile 로 읽으며, 정렬된 int 읽기는 원자적이라
        /// 찢어진 값을 보지 않는다. `_playerCount` 와 같은 규칙이다.
        private int _phase = (int)RoomPhase.Waiting;
        private int _hostPlayerId = RoomStateHeader.NoPlayer;

        private int _hostSessionId;

        /// 다음 봇에게 줄 세션 id. **-1 부터 내려간다.**
        ///
        /// 소켓이 없으므로 `SessionRegistry` 에서 받을 수 없고, 받아도 의미가 없다 —
        /// 그 표는 소켓을 찾는 표다. 음수를 쓰는 이유는 겹치지 않는 것 말고 하나 더
        /// 있다: 부호가 곧 "봇인가" 라서 로그와 판정이 그것을 유도할 수 있고,
        /// `IsAuthorized` 가 요구하는 방장 세션 id 는 항상 양수이므로 봇이 방장
        /// 자격을 얻는 경로가 산술적으로 없다.
        private int _nextBotSessionId = -1;

        private byte _seekerPlayerId = RoomStateHeader.NoPlayer;
        private int _placementSeed;
        private uint _startTick;
        private byte _outcome;

        /// 상태가 바뀐 틱에는 간격을 무시하고 즉시 보낸다.
        private bool _stateDirty = true;
        private uint _lastStateTick;

        /// 매치 전문도 같은 규칙이지만 게이트를 따로 둔다.
        ///
        /// `_stateDirty` 를 공유하면 룸 상태를 보내는 쪽이 그 깃발을 내려버려서, 같은
        /// 틱에 매치 전문이 "바뀐 것 없음" 으로 판단하고 즉시 전송을 건너뛴다. 두 전문은
        /// 서로 다른 이유로 바뀌므로 깃발도 둘이어야 한다.
        private bool _matchStateDirty;
        private uint _lastMatchStateTick;

        /// 목표물 전문도 게이트를 따로 둔다. 주기가 다르고(5초) 바뀌는 이유도 다르다 —
        /// 열쇠가 주워지거나 장치가 부서질 때만이다.
        private bool _objectiveStateDirty;
        private uint _lastObjectiveStateTick;

        public Room(
            string roomId,
            WorldMap map,
            NetworkConditionSimulator network,
            ILogger logger,
            bool isStatic = false,
            bool isPublic = false,
            BotOptions? bots = null)
        {
            RoomId = roomId;
            _map = map;
            _network = network;
            _logger = logger;
            _isStatic = isStatic;
            _isPublic = isPublic;

            // 넘기지 않았으면 꺼진 설정이다. null 을 그대로 들고 있으면 봇을 보는 자리마다
            // null 검사가 하나씩 붙고, 그중 하나를 빼먹는 것이 곧 NRE 다.
            _bots = bots ?? new BotOptions();
        }

        public string RoomId { get; }

        public uint MapHash => _map.Hash;

        public string MapName => _map.Name;

        /// uint 정렬 읽기는 원자적이라 조회 스레드가 찢어진 값을 보지 않는다.
        public uint Tick => _tick;

        public RoomPhase Phase => (RoomPhase)Volatile.Read(ref _phase);

        public int PlayerCount => Volatile.Read(ref _playerCount);

        /// 설정으로 미리 열어 둔 룸. 방장이 없고 비어도 회수되지 않는다.
        public bool IsStatic => _isStatic;

        /// 매치의 진행 단계. 틱 루프가 소유하므로 조회는 같은 스레드에서만 한다.
        ///
        /// 아직 와이어에 실리지 않는다 — 이 값을 클라이언트에 알리는 전문은 IG-008 이다.
        /// 지금은 서버가 매치를 진행시키는 것까지이고, 화면에는 변화가 없다.
        public MatchPhase MatchPhase => _match.Phase;

        public float MatchSecondsRemaining => _match.MatchSecondsRemaining;

        public float RevealSecondsRemaining => _match.RevealSecondsRemaining;

        /// 진행 중인 매치. **걸러지지 않은 실제 값을 들고 있다** — 세션으로 나가는 값은
        /// `BroadcastMatchState` 가 역할별로 인코딩한다.
        ///
        /// `Objectives` 와 같은 이유로 `internal` 이다. 모듈 밖으로는 나가지 않고, 테스트가
        /// 룸을 돌려 놓고 판정 결과를 확인하는 창구다.
        internal Match Match => _match;

        /// 날아가는 총알. 슬롯 배열이므로 `Active` 를 보고 걸러야 한다.
        internal Projectile[] Projectiles => _projectiles;

        /// 룸 안의 몸들. `Objectives`·`Match` 와 같은 이유로 `internal` 이다 — 판정을 검사하려면
        /// 몸을 특정 자리에 세울 수 있어야 하고, 그것을 위해 프로덕션에 테스트 전용 메서드를
        /// 만드는 것보다 소유한 객체를 모듈 안에서 여는 편이 표면이 작다.
        internal IEnumerable<PlayerEntity> Players => _players.Values;

        /// 이 매치의 목표물 배치. 틱 루프에서만 조회한다.
        ///
        /// 아직 와이어에 실리지 않는다 — 좌표를 역할별로 걸러 내려보내는 전문은 IG-011b 다.
        /// 지금은 서버가 배치를 갖는 것까지이고, 클라이언트는 여전히 씨드로 자기 배치를 만든다.
        internal Objectives Objectives => _objectives;

        /// 목록에 실리는 방인가. <see cref="_isPublic"/> 의 설명을 참고한다.
        public bool IsPublic => _isPublic;

        /// 정원이 찼으면 false. 슬롯은 접속 스레드가 예약하고 틱 루프가 반납한다.
        /// 반납을 접속 스레드에서 하면 퇴장 커맨드가 적용되기 전에 같은 PlayerId 가
        /// 재사용되어 한 스냅샷에 같은 id 가 두 번 실린다.
        public bool TryReserveSlot(out byte playerId)
        {
            lock (_slotGate)
            {
                for (var index = 0; index < _slots.Length; index++)
                {
                    if (!_slots[index])
                    {
                        _slots[index] = true;
                        playerId = (byte)index;
                        return true;
                    }
                }
            }

            playerId = 0;
            return false;
        }

        public void ReleaseSlot(byte playerId)
        {
            if (playerId >= _slots.Length)
            {
                return;
            }

            lock (_slotGate)
            {
                _slots[playerId] = false;
            }
        }

        public void PostCommand(in RoomCommand command)
        {
            _commands.Enqueue(command);
        }

        /// 수신 펌프가 호출한다. 네트워크 조건 주입기가 여기서 손실과 지연을 만든다.
        public void PostInput(int sessionId, uint tick, in InputFrame frame)
        {
            if (_network.Enabled && _network.ShouldDrop())
            {
                return;
            }

            var releaseTick = _tick + _network.DelayTicks();
            _inputs.Enqueue(new InboundInput(sessionId, tick, releaseTick, frame));
        }

        public RoomSummary Summarize()
        {
            return new RoomSummary(
                RoomId,
                _tick,
                Volatile.Read(ref _playerCount),
                RealtimeConstants.Rooms.MaxPlayers,
                (RoomPhase)Volatile.Read(ref _phase),
                (byte)Volatile.Read(ref _hostPlayerId),
                _map.Name,
                _map.Hash,
                _isPublic);
        }

        /// 틱 루프에서만 호출한다.
        public void Advance()
        {
            DrainCommands();

            _tick++;

            if (Phase == RoomPhase.Playing)
            {
                DrainInputs();

                foreach (var player in _players.Values)
                {
                    StepPlayer(player);
                }

                // 목표물 판정은 이동 **뒤**다. 앞에 두면 이번 틱에 열쇠 위로 걸어간
                // 플레이어가 다음 틱까지 줍지 못한다.
                PickUpKeys();
                InsertKeys();
                TickEscapes();

                // **이 순서가 규칙을 하나 정하고 있고, 그것은 기획서가 정한 것이 아니다.**
                // 탈출 판정이 총알보다 앞이므로 **유지 시간의 마지막 틱에 도착한 총알은 탈출을
                // 끊지 못한다** — `EscapeHoldTime` 의 목적이 "Seeker 가 끊을 수 있는 순간" 인데
                // 그 순간의 마지막 33ms 는 끊을 수 없다. `EscapesToWin` 이 2 이므로 그 한 틱이
                // 매치 결과를 바꿀 수 있다.
                //
                // 순서를 바꾸는 것(전투를 목표물 앞으로)이 의도에 맞아 보이지만 그것은 규칙
                // 변경이므로 §6.4 에 따라 추측하지 않는다 → **OQ-8**.
                // 현재 동작은 `TieBreakTests` 가 고정하고 있고, 답이 오면 그 테스트가 무엇이
                // 뒤집히는지 보여 준다.

                // 발사는 이동 뒤다 — 이번 틱의 시선으로 쏜다. 총알을 **같은 틱에 진행시키지
                // 않는다**: 발사한 틱에 4m 를 날아가면 눈앞의 벽이 총구보다 뒤에 있는 경우가
                // 생기고, 클라이언트의 예광탄은 총구에서 시작한다.
                StepProjectiles();
                FireWeapons();

                // **와이어 상태는 판정 뒤에 만든다.** 이동 안에서 만들면 이 틱의 판정이 세운
                // 플래그가 다음 틱 스냅샷에나 나간다 — 탈출은 33ms 늦게 사라지고, 출혈
                // (IG-014)도 같은 만큼 늦는다. 틱 N 의 스냅샷은 틱 N 이 끝난 상태여야 한다.
                foreach (var player in _players.Values)
                {
                    ProjectWire(player);
                }

                // 매치 시계는 이동을 처리한 뒤에 올린다. 먼저 올리면 시간이 0 이 된 틱의
                // 입력이 버려지고, 그 한 틱이 마지막 탈출을 판정하는 틱일 수 있다.
                var phaseBefore = _match.Phase;

                if (_match.Advance())
                {
                    EndMatchByServer();
                }

                // 단계가 바뀐 틱에는 전문을 즉시 보낸다. 간격만으로 보내면 리빌이 끝나고
                // 최대 0.5초 동안 클라이언트가 아직 잠긴 화면을 그린다.
                if (_match.Phase != phaseBefore)
                {
                    _matchStateDirty = true;
                }
            }
            else
            {
                // 대기·종료 단계에서는 입력을 처리하지 않는다. 그렇다고 두면 큐가
                // 무한히 자란다 — 클라이언트가 보내지 않기로 되어 있어도 서버가 그것에
                // 기대면 안 된다.
                DiscardInputs();

                // 봇 채우기는 **대기 단계에서만** 돈다. `/ws` 가 사람에게 진행 중 합류를
                // 막는 것과 같은 이유이고(역할도 배치도 이미 정해져 있어 규칙이 성립하지
                // 않는다), 이 검사가 여기 있는 것이 그 규칙의 전부다 — `Broadcast` 나
                // `Sweep` 쪽에 두면 단계가 시야에서 사라진다.
                if (Phase == RoomPhase.Waiting)
                {
                    TopUpBots();
                }
            }

            Volatile.Write(ref _playerCount, _players.Count);
        }

        /// 틱 루프에서만 호출한다.
        ///
        /// 룸 상태 전문은 모든 단계에서 보내고, 스냅샷은 `Playing` 에서만 보낸다.
        public void Broadcast(IServerTransport transport)
        {
            if (_players.Count == 0)
            {
                return;
            }

            BroadcastRoomState(transport);
            BroadcastMatchState(transport);
            BroadcastObjectiveState(transport);

            // 발사 알림은 전문이 아니다 — 쌓인 것을 내보내고 비운다. 이 틱에 아무도 쏘지
            // 않았으면 아무것도 나가지 않는다.
            BroadcastFireEvents(transport);

            if (Phase != RoomPhase.Playing)
            {
                return;
            }

            BroadcastSnapshot(transport);
        }

        /// 매 틱 풀 스냅샷을 보낸다.
        /// AckedInputTick 이 수신자마다 다르므로 세션별로 인코딩한다.
        private void BroadcastSnapshot(IServerTransport transport)
        {
            var count = 0;
            foreach (var player in _players.Values)
            {
                _entityBuffer[count] = player.Wire;
                count++;
            }

            foreach (var player in _players.Values)
            {
                // 봇에게는 보내지 않는다. 위의 엔티티 목록에는 **들어간다** — 사람들이
                // 봇의 몸을 봐야 한다. 걸러지는 것은 수신자 쪽뿐이고, 그래서 이 함수의
                // 두 순회가 서로 다른 목록을 돈다.
                if (player.IsBot)
                {
                    continue;
                }

                var header = new SnapshotHeader(_tick, player.LastProcessedInputTick, (byte)count);
                var length = MessageCodec.WriteSnapshot(
                    _sendBuffer,
                    header,
                    new ReadOnlySpan<EntityState>(_entityBuffer, 0, count));

                transport.TrySend(
                    player.SessionId,
                    new ReadOnlySpan<byte>(_sendBuffer, 0, length),
                    Reliability.Unreliable);
            }
        }

        /// 상태가 바뀌었거나 간격이 지났을 때 명단 전문을 보낸다.
        ///
        /// 본문이 수신자와 무관하므로 한 번 인코딩해 전원에게 보낸다.
        /// 스냅샷과 다른 점이며, 그 차이는 `AckedInputTick` 하나에서 온다.
        private void BroadcastRoomState(IServerTransport transport)
        {
            var due = _stateDirty
                || _tick - _lastStateTick >= (uint)RealtimeConstants.Rooms.RoomStateIntervalTicks;

            if (!due)
            {
                return;
            }

            _stateDirty = false;
            _lastStateTick = _tick;

            var count = 0;
            foreach (var player in _players.Values)
            {
                _rosterBuffer[count] = new RoomPlayerEntry(player.PlayerId, player.Name);
                count++;
            }

            // 배치 씨드는 싣지 않는다. 서버는 그것을 계속 갖고 있지만(`_placementSeed`,
            // 배치를 재현하는 데 쓴다) 클라이언트에 보내면 Seeker 가 문의 좌표를 계산할 수
            // 있다 — 그것이 이 이관 작업이 닫으려던 구멍이다.
            var header = new RoomStateHeader(
                Phase,
                (byte)Volatile.Read(ref _hostPlayerId),
                _seekerPlayerId,
                _outcome,
                _startTick,
                (byte)count);

            var length = MessageCodec.WriteRoomState(
                _stateBuffer,
                header,
                new ReadOnlySpan<RoomPlayerEntry>(_rosterBuffer, 0, count));

            foreach (var player in _players.Values)
            {
                if (player.IsBot)
                {
                    continue;
                }

                transport.TrySend(
                    player.SessionId,
                    new ReadOnlySpan<byte>(_stateBuffer, 0, length),
                    Reliability.Reliable);
            }
        }

        /// 매치 상태 전문을 보낸다. **세션별로 인코딩한다.**
        ///
        /// 스냅샷이 `AckedInputTick` 때문에 세션별로 인코딩하는 것과 이유가 다르다.
        /// 여기서는 **본문 자체가 수신자의 역할에 따라 달라진다** — 룰셋은 Seeker 에게
        /// 열쇠 진행도를 알리지 않으므로 그 사본에서는 삽입 열쇠와 소지 열쇠가 0 이다.
        /// 필터는 `MessageCodec.WriteMatchState` 안에 있어 우회할 자리가 없다.
        ///
        /// 로비 단계에서는 보내지 않는다. 매치가 없는데 전문을 보내면 클라이언트가
        /// 시작하지 않은 매치의 시계를 그린다.
        private void BroadcastMatchState(IServerTransport transport)
        {
            if (_match.Phase == MatchPhase.Lobby)
            {
                return;
            }

            var due = _matchStateDirty
                || _tick - _lastMatchStateTick >= (uint)RealtimeConstants.Rooms.MatchStateIntervalTicks;

            if (!due)
            {
                return;
            }

            _matchStateDirty = false;
            _lastMatchStateTick = _tick;

            var count = 0;
            foreach (var player in _players.Values)
            {
                // 출혈·탈출·쓰러짐은 여기 싣지 않는다 — **스냅샷의 `EntityFlags`** 로 매 틱
                // 나가므로 2Hz 전문에 같은 정보를 두 번 실으면 두 경로가 어긋날 자리만 생긴다.
                // 그 자리에 있던 `flags` 바이트가 그래서 영구히 0 이었고, 이제 탄약이 쓴다.
                //
                // 세 값 모두 여기서 바이트로 좁힌다. 상한을 두는 이유는 형식이지 규칙이
                // 아니다 — 무제한 소지(`CarryLimit` 0)에서도 맵의 열쇠 수가 10 이므로
                // 넘을 수 없고, 넘었다면 습득 판정이 같은 열쇠를 두 번 센 것이다.
                _participantBuffer[count] = new MatchParticipant(
                    player.PlayerId,
                    RoleOf(player.PlayerId),
                    (byte)Math.Min(player.Ammo, byte.MaxValue),
                    (byte)Math.Min(player.Hits, byte.MaxValue),
                    (byte)Math.Min(player.CarriedKeys, byte.MaxValue));

                count++;
            }

            var header = new MatchStateHeader(
                _match.Phase,
                MatchStateHeader.ToTenths(_match.MatchSecondsRemaining),

                // Seeker 사본에서는 코덱이 0 으로 만든다. 여기서 거르지 않는 이유는 필터가
                // 나가는 길목에 한 번만 있어야 하기 때문이다 — 두 곳에 있으면 한 곳을 고칠 때
                // 다른 곳이 남는다.
                (byte)_match.KeysInserted,

                // 탈출 수는 **Seeker 도 받는다.** 자기가 막아야 하는 수이므로 코덱이 거르지
                // 않는다 — 숨기는 것은 목표의 위치와 진행도다(기획서 §2.1).
                (byte)_match.Escapes,
                _outcome,
                (byte)count);

            var participants = new ReadOnlySpan<MatchParticipant>(_participantBuffer, 0, count);

            foreach (var player in _players.Values)
            {
                if (player.IsBot)
                {
                    continue;
                }

                var length = MessageCodec.WriteMatchState(
                    _matchStateBuffer,
                    header,
                    participants,
                    RoleOf(player.PlayerId));

                transport.TrySend(
                    player.SessionId,
                    new ReadOnlySpan<byte>(_matchStateBuffer, 0, length),
                    Reliability.Reliable);
            }
        }

        /// 목표물 전문을 보낸다. **세션별로 인코딩하고, Seeker 사본에서는 문이 빠진다.**
        ///
        /// 이 필터가 이 이관 작업의 원래 목적이다. 씨드를 공유해 양쪽이 같은 배치를 계산하는
        /// 방식으로는 문 좌표가 Seeker 의 프로세스에 도달하는 것을 막을 수 없었다 — 컬링
        /// 레이어는 화면에서 가릴 뿐이고 WebGL 빌드는 디컴파일된다.
        ///
        /// 배치되지 않았으면 보내지 않는다. 격자가 없는 맵이 그 경우이고, 빈 전문을 보내면
        /// 클라이언트가 "목표물이 전부 사라졌다" 로 읽는다.
        private void BroadcastObjectiveState(IServerTransport transport)
        {
            if (!_objectives.Placed)
            {
                return;
            }

            var due = _objectiveStateDirty
                || _tick - _lastObjectiveStateTick >= (uint)RealtimeConstants.Match.ObjectiveStateIntervalTicks;

            if (!due)
            {
                return;
            }

            _objectiveStateDirty = false;
            _lastObjectiveStateTick = _tick;

            var keyCount = 0;
            for (var index = 0; index < _objectives.Keys.Count && keyCount < _keyBuffer.Length; index++)
            {
                _keyBuffer[keyCount] = ToPoint(_objectives.Keys[index]);
                keyCount++;
            }

            var deviceCount = 0;
            for (var index = 0; index < _objectives.Devices.Count && deviceCount < _deviceBuffer.Length; index++)
            {
                var device = _objectives.Devices[index];

                _deviceBuffer[deviceCount] = new ObjectiveDevice(
                    device.Type,
                    Quantization.ToFixedPosition(device.Position.X),
                    Quantization.ToFixedPosition(device.Position.Y),
                    Quantization.ToFixedPosition(device.Position.Z),
                    Quantization.ToFixedYaw(device.Yaw),

                    // 소진·파괴·쿨다운은 아직 서버가 판정하지 않는다 → IG-013·IG-015.
                    0);

                deviceCount++;
            }

            var header = new ObjectiveStateHeader(
                ObjectiveFlags.HasAltar | ObjectiveFlags.HasDoor,
                (byte)keyCount,
                (byte)deviceCount);

            var keys = new ReadOnlySpan<ObjectivePoint>(_keyBuffer, 0, keyCount);
            var devices = new ReadOnlySpan<ObjectiveDevice>(_deviceBuffer, 0, deviceCount);

            foreach (var player in _players.Values)
            {
                if (player.IsBot)
                {
                    continue;
                }

                var length = MessageCodec.WriteObjectiveState(
                    _objectiveStateBuffer,
                    header,
                    ToPoint(_objectives.AltarPosition),
                    ToPoint(_objectives.AltarDragPoint),
                    ToPoint(_objectives.DoorPosition),
                    Quantization.ToFixedYaw(_objectives.DoorYaw),

                    // 문 개방. **Seeker 사본에는 문 블록 자체가 없으므로 이 값도 실리지
                    // 않는다** — 별도의 필터가 필요하지 않은 것이 블록을 통째로 빼는 설계의
                    // 부수 효과다.
                    _match.DoorOpen,
                    keys,
                    devices,
                    RoleOf(player.PlayerId));

                transport.TrySend(
                    player.SessionId,
                    new ReadOnlySpan<byte>(_objectiveStateBuffer, 0, length),
                    Reliability.Reliable);
            }
        }

        private static ObjectivePoint ToPoint(Vector3 position)
        {
            return new ObjectivePoint(
                Quantization.ToFixedPosition(position.X),
                Quantization.ToFixedPosition(position.Y),
                Quantization.ToFixedPosition(position.Z));
        }

        /// 이 플레이어가 어느 편인가.
        ///
        /// Seeker 가 정해지기 전에는 아무도 배정되지 않았다. 그때 전원을 Runner 로 두면
        /// 클라이언트가 로비에서 무기 없는 몸을 만들고, 역할이 정해진 뒤 다시 만들어야 한다.
        private MatchRole RoleOf(byte playerId)
        {
            if (_seekerPlayerId == RoomStateHeader.NoPlayer)
            {
                return MatchRole.Unassigned;
            }

            return playerId == _seekerPlayerId ? MatchRole.Seeker : MatchRole.Runner;
        }

        /// 한 플레이어를 한 틱 진행한다.
        ///
        /// 입력이 여러 개 쌓여 있으면 상한까지 따라잡고, 하나도 없으면
        /// 마지막 입력을 제한된 횟수만 반복한다. 그 뒤에는 시선만 유지하고 멈춘다.
        /// 반복을 무제한 허용하면 입력을 끊은 클라이언트가 계속 달린다.
        private void StepPlayer(PlayerEntity player)
        {
            // 봇은 입력 버퍼를 지나지 않는다. 매 틱 정확히 한 프레임을 만들고 그것이
            // 유실될 경로가 없으므로, 따라잡기 상한·반복·중립화 같은 지터 흡수 장치를
            // 지날 이유가 없다. **적용은 사람과 같은 함수를 부른다**(`ApplyFrame`) —
            // 그것을 갈라 두면 이동 잠금 규칙이 두 곳에 생기고, 증상은 "리빌 중에
            // 봇만 움직인다" 가 된다.
            if (player.IsBot)
            {
                StepBot(player);
                return;
            }

            var applied = 0;

            while (applied < RealtimeConstants.Rooms.MaxInputsPerTick && player.TryTakeNext(out var input))
            {
                var frame = ApplyFrame(player, input.Frame);

                // 엣지 버튼을 지운 프레임을 저장한다. `LastInput` 은 **반복 적용될 값**이므로
                // 한 번만 발동해야 하는 비트가 남아 있으면 안 된다.
                //
                // 지금 이것이 없어도 삽입은 반복되지 않는다 — 요청을 세우는 곳이 위의 새 입력
                // 갈래뿐이고, 반복 갈래는 `Simulate` 만 부른다(테스트로 확인했다: 이 줄을
                // `= frame` 으로 되돌려도 13개가 그대로 통과한다). 남겨 두는 이유는 그 불변식이
                // 두 곳의 협조에 의존하기 때문이다. 반복 갈래가 언젠가 버튼을 보게 되면 그때
                // 조용히 깨지고, 증상은 "열쇠가 저절로 들어간다" 가 된다.
                player.LastInput = InputValidator.WithoutEdgeButtons(frame);
                player.RepeatCount = 0;
                applied++;
            }

            if (applied == 0)
            {
                // 잠금 중에는 반복도 비운다. 이 갈래를 빼면 잠금이 걸린 첫 틱에 새 입력이
                // 없는 플레이어가 **직전 프레임의 이동을 그대로 반복**해, 리빌 중에 혼자
                // 계속 달린다.
                if (_match.MovementLocked)
                {
                    var locked = InputValidator.Neutral(player.LastInput);
                    Simulate(player, locked);
                    player.LastInput = locked;
                }
                else if (player.RepeatCount < RealtimeConstants.Rooms.MaxInputRepeatTicks)
                {
                    Simulate(player, player.LastInput);
                    player.RepeatCount++;
                }
                else
                {
                    var neutral = InputValidator.Neutral(player.LastInput);
                    Simulate(player, neutral);
                    player.LastInput = neutral;
                }
            }

        }

        /// 봇을 한 틱 진행한다. 두뇌가 만든 프레임을 사람과 같은 경로로 적용한다.
        ///
        /// `LastInput` 을 저장하는 이유가 사람과 다르다. 사람에게는 새 입력이 없을 때
        /// 반복할 값이지만, 봇에게는 **다음 틱 두뇌의 입력**이다 — 시선을 여기에 담아
        /// 두고 두뇌가 그것을 기준으로 다음 시선을 만든다.
        private void StepBot(PlayerEntity bot)
        {
            var frame = ApplyFrame(bot, BotBrain.Think(bot.LastInput));

            bot.LastInput = InputValidator.WithoutEdgeButtons(frame);
            bot.RepeatCount = 0;
        }

        /// 프레임 하나를 검증하고 적용한다. **사람과 봇이 공유하는 유일한 경로다.**
        ///
        /// 돌려주는 것은 검증·잠금을 지난 프레임이다. 호출자가 그것을 `LastInput` 에
        /// 저장해야 하며, 원본을 저장하면 잠금 중에 지운 이동 성분이 되살아난다.
        private InputFrame ApplyFrame(PlayerEntity player, in InputFrame raw)
        {
            var frame = InputValidator.Sanitize(raw);

            // 역할 공개와 결과 화면에서는 이동을 비운다. 입력은 그래도 **소비한다** —
            // 버리기만 하면 잠금이 풀리는 순간 쌓인 입력이 한꺼번에 적용되어
            // 플레이어가 순간이동한다. 시선은 남기므로 리빌 중에도 둘러볼 수 있다.
            if (_match.MovementLocked)
            {
                frame = InputValidator.Neutral(frame);
            }

            // 상호작용 요청을 여기서 걷는다. 판정은 이동이 끝난 뒤에 한 번 돌므로
            // (`InsertKeys`) 같은 틱에 여러 프레임을 따라잡아도 요청은 한 번이다 —
            // 그것이 맞다. 클라이언트는 키를 한 번 눌렀다.
            if ((frame.Buttons & ButtonFlags.Interact) != 0)
            {
                player.InteractRequested = true;
            }

            Simulate(player, frame);

            return frame;
        }

        /// 이 몸의 와이어 표현을 다시 만든다. **틱의 마지막 단계다.**
        ///
        /// 이동과 나누어 둔 이유는 순서다 — 목표물 판정(`TickEscapes` 등)이 세우는 플래그가
        /// 같은 틱의 스냅샷에 실려야 한다.
        private void ProjectWire(PlayerEntity player)
        {
            player.MatchFlags = MatchFlagsFor(player);
            player.Wire = StateProjection.ToEntityState(player.PlayerId, player.State, player.MatchFlags);
        }

        /// 이 몸에 얹을 매치 판정 비트.
        ///
        /// 지금 채울 수 있는 것은 둘이다 — 역할과 이동 잠금. 출혈(`Bleeding`)과
        /// 탈출(`Escaped`)은 그 판정이 서버로 오는 태스크(IG-014·IG-012)에서 붙는다.
        ///
        /// 매 틱 다시 계산한다. 상태로 들고 있다가 갱신을 잊는 것보다, 근거가 되는
        /// 값(Seeker id, 잠금 여부)에서 매번 유도하는 편이 어긋날 자리가 없다.
        private EntityFlags MatchFlagsFor(PlayerEntity player)
        {
            var flags = EntityFlags.None;

            if (_seekerPlayerId != RoomStateHeader.NoPlayer && player.PlayerId == _seekerPlayerId)
            {
                flags |= EntityFlags.Seeker;
            }

            if (_match.MovementLocked)
            {
                flags |= EntityFlags.Frozen;
            }

            if (player.Escaped)
            {
                flags |= EntityFlags.Escaped;
            }

            if (player.Bleeding)
            {
                flags |= EntityFlags.Bleeding;
            }

            if (player.Downed)
            {
                flags |= EntityFlags.Downed;
            }

            return flags;
        }

        /// 열쇠 습득을 판정한다. 기획서 §3 — Runner 가 열쇠에 다가가면 줍는다.
        ///
        /// **상호작용 키가 없는 것이 의도다.** 기획서는 습득 방식을 정하지 않고, 클라이언트가
        /// 쓰던 방식이 거리 폴링이었다(`KeyPickup.Update`). 열쇠를 지나쳤는데 줍지 못하는
        /// 편보다 걸어서 줍는 편이 미로에서 덜 답답하다. 삽입은 다르다 — 그쪽은 되돌릴 수
        /// 없으므로 명시적인 입력을 받는다(IG-012b).
        ///
        /// 뒤에서부터 훑는다. `RemoveKeyAt` 이 리스트를 당기므로 앞에서부터 지우면 지운
        /// 자리의 다음 열쇠를 한 틱 건너뛴다 — 한 틱 뒤에 주워지므로 증상이 거의 없고,
        /// 그래서 찾기 어려운 종류의 버그다.
        ///
        /// 한 틱에 여러 개를 줍는 것을 막지 않는다. 열쇠 간격(`KeySpacing` 4m)이 습득
        /// 반경(1.4m)보다 넓으므로 겹쳐 놓인 경우에만 일어나고, 그때는 둘 다 주워지는
        /// 것이 옳다.
        private void PickUpKeys()
        {
            if (!_objectives.Placed || _objectives.Keys.Count == 0)
            {
                return;
            }

            if (_match.Phase != MatchPhase.Playing)
            {
                return;
            }

            for (var index = _objectives.Keys.Count - 1; index >= 0; index--)
            {
                var key = _objectives.Keys[index];

                foreach (var player in _players.Values)
                {
                    // 빠져나간 사람은 판정에서 빠진다. 몸은 남아 있으므로(승리 조건이 명단을
                    // 세어야 한다) 좌표만으로는 걸러지지 않는다.
                    if (RoleOf(player.PlayerId) != MatchRole.Runner || player.Escaped || player.Downed)
                    {
                        continue;
                    }

                    if (!IsWithinPickupRange(player.State.Position, key))
                    {
                        continue;
                    }

                    if (MatchConstants.CarryLimit > 0 && player.CarriedKeys >= MatchConstants.CarryLimit)
                    {
                        continue;
                    }

                    player.CarriedKeys++;
                    _objectives.RemoveKeyAt(index);

                    // 두 전문이 모두 바뀐다 — 맵에서 열쇠가 사라졌고(목표물), 그 사람이
                    // 든 수가 늘었다(매치). 하나만 즉시 보내면 다른 쪽이 최대 5초 늦는다.
                    _objectiveStateDirty = true;
                    _matchStateDirty = true;

                    _logger.LogDebug(
                        "룸 {RoomId} 플레이어 {PlayerId}: 열쇠를 주웠다. 소지 {Carried}, 남은 {Remaining}.",
                        RoomId,
                        player.PlayerId,
                        player.CarriedKeys,
                        _objectives.Keys.Count);

                    // 이 열쇠는 사라졌다. 같은 자리의 다른 사람을 볼 필요가 없다.
                    break;
                }
            }
        }

        /// 열쇠 삽입을 판정한다. 기획서 §3 — 열쇠 `KeysRequired` 개가 들어가면 문이 열린다.
        ///
        /// 습득과 달리 **명시적인 입력을 받는다.** 문 앞을 지나가는 것만으로 들고 있던 열쇠가
        /// 들어가면, 열쇠를 모아 두는 전술(`CarryLimit` 무제한이 만드는 것)이 성립하지 않는다.
        ///
        /// **삽입이 한 곳에서 직렬화되는 것이 "두 Runner 가 동시에 10번째 열쇠를 넣는" 경우의
        /// 답이다.** 먼저 도는 쪽이 문턱을 넘고, 다음 쪽은 `_match.DoorOpen` 에서 걸린다 —
        /// 열쇠는 소비되지 않는다. 순서는 사전 순이 아니라 딕셔너리 순회 순이지만, 어느 쪽이
        /// 먼저든 결과가 같으므로(문이 열리고 열쇠 하나가 쓰인다) 판정이 갈리지 않는다.
        private void InsertKeys()
        {
            if (_match.Phase != MatchPhase.Playing || !_objectives.Placed)
            {
                return;
            }

            foreach (var player in _players.Values)
            {
                if (!player.InteractRequested)
                {
                    continue;
                }

                // **자격을 보기 전에 지운다.** 엣지는 한 틱만 살아야 하고, 거부된 요청이
                // 남아 있으면 문 앞에 도착한 순간 예전에 누른 키가 발동한다.
                player.InteractRequested = false;

                if (_match.DoorOpen)
                {
                    continue;
                }

                if (RoleOf(player.PlayerId) != MatchRole.Runner || player.Escaped || player.Downed)
                {
                    continue;
                }

                if (player.CarriedKeys <= 0)
                {
                    continue;
                }

                if (_tick < player.NextInsertTick)
                {
                    continue;
                }

                if (!IsWithinDoorRange(player.State.Position))
                {
                    continue;
                }

                player.CarriedKeys--;
                player.NextInsertTick = _tick + (uint)Match.InsertIntervalTicks;

                var opened = _match.InsertKey();

                // 소지 수와 삽입 수가 함께 바뀌므로 매치 전문은 항상 즉시 보낸다.
                _matchStateDirty = true;

                if (opened)
                {
                    // 목표물 전문의 문 블록에 개방 여부가 실려 있다. 열린 틱에 보내지 않으면
                    // 최대 5초 동안 Runner 가 열린 문을 잠긴 것으로 본다.
                    _objectiveStateDirty = true;

                    _logger.LogInformation(
                        "룸 {RoomId}: 열쇠 {Required} 개가 들어가 문이 열렸다.",
                        RoomId,
                        MatchConstants.KeysRequired);
                }
                else
                {
                    _logger.LogDebug(
                        "룸 {RoomId} 플레이어 {PlayerId}: 열쇠를 넣었다. {Inserted}/{Required}.",
                        RoomId,
                        player.PlayerId,
                        _match.KeysInserted,
                        MatchConstants.KeysRequired);
                }
            }
        }

        /// 발사를 판정한다. 기획서 §4.3 — Seeker 의 3발.
        ///
        /// **`Fire` 는 엣지가 아니라 누르고 있는 상태다**(`FirstPersonController.FireHeld`).
        /// 연사 간격이 그것을 받아 준다 — `NextFireTick` 이 없으면 트리거를 누르고 있는 동안
        /// 초당 30발이 나간다.
        ///
        /// Runner 는 쏘지 않는다. 기획서 §4 의 총은 술래의 것이고, `RunnerHitsToDie` 가 있는
        /// 것도 맞는 쪽이 Runner 뿐이기 때문이다.
        private void FireWeapons()
        {
            if (_match.Phase != MatchPhase.Playing)
            {
                return;
            }

            foreach (var player in _players.Values)
            {
                if ((player.LastInput.Buttons & ButtonFlags.Fire) == 0)
                {
                    continue;
                }

                if (RoleOf(player.PlayerId) != MatchRole.Seeker)
                {
                    continue;
                }

                if (player.Ammo <= 0 || _tick < player.NextFireTick)
                {
                    continue;
                }

                if (!TrySpawnProjectile(player, out var origin))
                {
                    // 슬롯이 없다. 탄을 소비하지 않는 것이 맞다 — 서버 쪽 상한이지
                    // 규칙이 아니다.
                    _logger.LogWarning(
                        "룸 {RoomId}: 총알 슬롯 {Max} 개가 모두 찼다. 발사를 버렸다.",
                        RoomId,
                        _projectiles.Length);
                    continue;
                }

                player.Ammo--;
                player.NextFireTick = _tick + (uint)Match.FireIntervalTicks;

                // 발사 알림을 쌓아 둔다. `Broadcast` 가 이 틱에 내보내고 반복하지 않는다 —
                // 이 프로젝트의 유일한 알림이고 근거는 ADR 0003 이다.
                QueueFireEvent(player, origin);

                _logger.LogDebug(
                    "룸 {RoomId} 플레이어 {PlayerId}: 발사. 남은 탄 {Ammo}/{Magazine}.",
                    RoomId,
                    player.PlayerId,
                    player.Ammo,
                    MatchConstants.SeekerMagazine);
            }
        }

        /// 이 틱에 나갈 발사 알림을 쌓는다.
        ///
        /// **총알의 시작점을 인자로 받는다.** 눈높이를 두 곳에서 따로 계산하면 예광탄이 총알과
        /// 다른 데서 출발하고, 그 어긋남은 총구 오프셋처럼 보여 원인을 찾기 어렵다.
        private void QueueFireEvent(PlayerEntity player, Vector3 origin)
        {
            if (_pendingFireCount >= _pendingFires.Length)
            {
                // 한 틱에 32발이 나가는 경로는 없다(연사 간격이 5틱이다). 넘으면 알림만 잃고
                // 판정은 그대로 진행된다 — 알림이 버려져도 되는 것이 ADR 0003 의 전제다.
                return;
            }

            _pendingFires[_pendingFireCount++] = new FireEventMessage(
                player.PlayerId,
                Quantization.ToFixedPosition(origin.X),
                Quantization.ToFixedPosition(origin.Y),
                Quantization.ToFixedPosition(origin.Z),
                Quantization.ToFixedYaw(player.State.Yaw),
                Quantization.ToFixedPitch(player.State.Pitch),
                _tick);
        }

        /// 쌓인 발사 알림을 내보내고 비운다. **반복하지 않는다.**
        ///
        /// 역할별 필터가 없다 — 총성이 이미 술래의 위치를 알려 주므로 예광탄을 숨기면
        /// "소리는 들리는데 궤적이 없는" 상태가 되어 소리의 정보만 줄어든다.
        private void BroadcastFireEvents(IServerTransport transport)
        {
            for (var index = 0; index < _pendingFireCount; index++)
            {
                var length = MessageCodec.WriteFireEvent(_fireEventBuffer, _pendingFires[index]);

                foreach (var player in _players.Values)
                {
                    if (player.IsBot)
                    {
                        continue;
                    }

                    transport.TrySend(
                        player.SessionId,
                        new ReadOnlySpan<byte>(_fireEventBuffer, 0, length),
                        Reliability.Reliable);
                }
            }

            _pendingFireCount = 0;
        }

        /// 눈높이에서 시선 방향으로 총알 하나를 만든다.
        ///
        /// 눈높이에서 쏘는 것이 맞다. 클라이언트는 총구에서 쏘지만 **조준점을 향해** 쏘고
        /// (`WeaponController.Fire`), 명중 판정은 화면 중심에서 온다 — 즉 실제 판정선은 눈에서
        /// 나가는 직선이다. 총구 오프셋은 연출이다.
        private bool TrySpawnProjectile(PlayerEntity player, out Vector3 origin)
        {
            origin = default;

            for (var index = 0; index < _projectiles.Length; index++)
            {
                if (_projectiles[index].Active)
                {
                    continue;
                }

                var eye = player.State.Position
                    + new Vector3(0f, SimConstants.PlayerHeight * SimConstants.EyeHeightRatio, 0f);

                origin = eye;

                _projectiles[index] = new Projectile
                {
                    OwnerId = player.PlayerId,
                    Position = eye,
                    Direction = PlayerMovement.Forward(player.State.Yaw, player.State.Pitch),
                    TicksLived = 0,
                    Active = true,
                };

                return true;
            }

            return false;
        }

        /// 총알을 한 틱 진행한다.
        ///
        /// **한 틱에 지나간 선분 전체를 검사한다.** 120m/s 는 한 틱에 4m 를 지나므로 도착 지점만
        /// 보면 벽을 통과한다 — 클라이언트의 `Bullet` 이 스윕 레이캐스트를 쓰는 이유와 같고,
        /// 그쪽은 그것을 10만 m/s 로 검증했다.
        ///
        /// 플레이어 판정은 아직 없다(IG-014b). 지오메트리에 맞으면 사라지는 것까지가 여기다.
        private void StepProjectiles()
        {
            var step = MatchConstants.BulletSpeed * SimConstants.TickDelta;

            for (var index = 0; index < _projectiles.Length; index++)
            {
                if (!_projectiles[index].Active)
                {
                    continue;
                }

                ref var projectile = ref _projectiles[index];

                // **지오메트리와 사람을 같은 선분에서 함께 본다.** 따로 검사하면 벽 뒤의
                // 사람이 맞는다 — 벽이 더 가까우면 벽이 이겨야 한다.
                var blocked = _map.Collision.Raycast(projectile.Position, projectile.Direction, step, out var mapHit);
                var reach = blocked ? mapHit.Distance : step;

                if (TryFindVictim(projectile, reach, out var victim))
                {
                    ApplyHit(victim, projectile.OwnerId);
                    projectile.Active = false;
                    continue;
                }

                if (blocked)
                {
                    projectile.Active = false;
                    continue;
                }

                projectile.Position += projectile.Direction * step;
                projectile.TicksLived++;

                if (projectile.TicksLived >= Match.BulletLifetimeTicks)
                {
                    // 콜리전 틈으로 빠져나간 총알을 영원히 시뮬레이션하지 않는다.
                    projectile.Active = false;
                }
            }
        }

        /// 이 선분에서 맞는 사람을 찾는다. 여럿이면 **가장 가까운 사람**이다.
        ///
        /// 쏜 사람은 제외한다. 총구가 자기 몸 안에서 시작하므로 그러지 않으면 발사한 틱에
        /// 자기가 맞는다 — 클라이언트도 `hitMask` 로 자기 레이어를 뺀다.
        ///
        /// 기획서 §4 — 맞는 쪽은 Runner 뿐이다. 술래는 총을 맞지 않고 다른 누구도 무장하지 않는다.
        private bool TryFindVictim(in Projectile projectile, float reach, out PlayerEntity victim)
        {
            victim = null!;
            var closest = reach;

            foreach (var player in _players.Values)
            {
                if (player.PlayerId == projectile.OwnerId)
                {
                    continue;
                }

                if (RoleOf(player.PlayerId) != MatchRole.Runner || player.Escaped || player.Downed)
                {
                    continue;
                }

                if (!Raycaster.RayAabb(
                        projectile.Position,
                        projectile.Direction,
                        BodyOf(player),
                        out var enter,
                        out var exit,
                        out _))
                {
                    continue;
                }

                // 시작점이 몸 안이면 거리 0 이다(`Raycaster.Raycast` 와 같은 규칙).
                var distance = enter < 0f ? 0f : enter;

                if (exit < 0f || distance > closest)
                {
                    continue;
                }

                closest = distance;
                victim = player;
            }

            return victim != null;
        }

        /// 몸의 판정 박스. 이동이 쓰는 것과 같은 치수여야 한다 — 다르면 눈에 보이는 몸과
        /// 맞는 몸이 어긋난다.
        private static Aabb BodyOf(PlayerEntity player)
        {
            var height = (player.State.Flags & EntityFlags.Crouching) != 0
                ? SimConstants.PlayerCrouchHeight
                : SimConstants.PlayerHeight;

            var half = new Vector3(SimConstants.PlayerRadius, height * 0.5f, SimConstants.PlayerRadius);

            return Aabb.FromCenter(player.State.Position + new Vector3(0f, half.Y, 0f), half);
        }

        /// 피격 하나를 적용한다. 기획서 §4.1 — 1방은 출혈과 순간이동, 2방은 쓰러짐.
        ///
        /// 무적 창이 여기 있는 이유는 `PlayerEntity.ImmuneUntilTick` 에 적혀 있다 — 3연사가
        /// 순간이동을 관통해 죽이는 것을 막는다.
        private void ApplyHit(PlayerEntity victim, byte shooterId)
        {
            if (_tick < victim.ImmuneUntilTick)
            {
                return;
            }

            victim.Hits++;
            victim.ImmuneUntilTick = _tick + (uint)Match.HitImmunityTicks;
            _matchStateDirty = true;

            if (victim.Hits >= MatchConstants.RunnerHitsToDie)
            {
                DownRunner(victim, shooterId);
                return;
            }

            // 살아남은 피격: 출혈이 시작되고(유도값) 다른 곳으로 던져진다. **순간이동이 벌칙의
            // 무게다** — 하던 일이 끝나고, 자기가 어디 있는지 모르게 된다.
            TeleportToRandomFreeFloor(victim);

            _logger.LogDebug(
                "룸 {RoomId} 플레이어 {PlayerId}: 피격 {Hits}/{Fatal}. 출혈 시작.",
                RoomId,
                victim.PlayerId,
                victim.Hits,
                MatchConstants.RunnerHitsToDie);
        }

        /// 쓰러뜨린다. 들고 있던 열쇠는 사망 지점 주위에 떨어진다.
        private void DownRunner(PlayerEntity victim, byte shooterId)
        {
            victim.Downed = true;

            var dropped = victim.CarriedKeys;

            if (dropped > 0 && _objectives.Placed)
            {
                ScatterKeys(dropped, victim.State.Position);
                _objectiveStateDirty = true;
            }

            victim.CarriedKeys = 0;

            _logger.LogInformation(
                "룸 {RoomId}: 플레이어 {PlayerId} 가 플레이어 {ShooterId} 에게 쓰러졌다. 열쇠 {Dropped} 개를 흘렸다.",
                RoomId,
                victim.PlayerId,
                shooterId,
                dropped);
        }

        /// 흘린 열쇠를 사망 지점 주위에 퍼뜨린다(룰셋 — 목표가 되돌아온다).
        ///
        /// **원 위에 균등 배분하고 시작 각도만 무작위로 뽑는다.** 클라이언트의 `ScatterKeys` 는
        /// 각 열쇠의 각도를 따로 뽑는데, 그러면 각도가 겹쳐 두 열쇠가 같은 자리에 놓일 수 있다 —
        /// 흩뿌리는 목적이 "한 무더기가 한 틱에 전부 주워지는 것" 을 피하는 것이므로 겹침이 곧
        /// 실패다. 균등 배분은 그것을 불가능하게 만들고 난수 draw 도 하나로 줄인다.
        ///
        /// **격자에 스냅하지 않는다.** `TryNearestFreeFloor` 는 셀 중심을 돌려주므로(AS-7) 반경
        /// 0.7m 안의 후보들이 전부 같은 셀 중심으로 모여 다시 한 점이 된다. 스냅이 없어도 안전한
        /// 이유는 **습득 반경(1.4m)이 이 반경(0.7m)보다 크다는 것**이다 — 벽 쪽으로 밀린 열쇠도
        /// 사망 지점에 서서 그대로 주울 수 있다.
        ///
        /// 난수는 피격 순간이동과 같은 수열을 쓴다. 둘 다 같은 판정(`ApplyHit`)의 결과이고
        /// 서로 독립적으로 재현되어야 할 이유가 없다.
        private void ScatterKeys(int count, Vector3 deathPoint)
        {
            const float TwoPi = 2f * 3.14159265f;

            // 0~359도. 정수로 뽑는 이유는 `NextUnitFloat` 보다 분포를 읽기 쉽고, 1도 단위면
            // 시작 각도가 눈에 띄게 반복되지 않기 때문이다.
            var start = _hitRandom.NextInt(360) * (TwoPi / 360f);

            for (var index = 0; index < count; index++)
            {
                var angle = start + (index * TwoPi / count);

                DeterministicMath.SinCos(angle, out var sin, out var cos);

                _objectives.AddKey(deathPoint + new Vector3(
                    cos * MatchConstants.KeyDropRadius,
                    0f,
                    sin * MatchConstants.KeyDropRadius));
            }
        }

        /// 무작위 통행 가능 셀로 옮긴다. 격자가 없으면 아무것도 하지 않는다.
        ///
        /// **속도를 0 으로 만든다.** 남겨 두면 옮겨진 자리에서 원래 달리던 방향으로 계속 미끄러지고,
        /// 그 방향은 이제 벽일 수 있다.
        ///
        /// 입력은 비우지 않는다 — 키를 누르고 있으면 새 자리에서 계속 달리는 것이 맞다.
        private void TeleportToRandomFreeFloor(PlayerEntity victim)
        {
            if (!_map.HasGrid)
            {
                _logger.LogWarning(
                    "룸 {RoomId}: 맵 {MapName} 에 격자가 없어 피격 순간이동을 건너뛴다.",
                    RoomId,
                    _map.Name);
                return;
            }

            if (!_map.Grid.TryRandomFreeFloor(ref _hitRandom, out var point))
            {
                return;
            }

            victim.State.Position = point;
            victim.State.Velocity = Vector3.Zero;
        }

        /// 탈출을 판정한다. 기획서 §3 — 열린 문간에 `EscapeHoldTime` 동안 머물면 빠져나간다.
        ///
        /// **문을 만지는 것이 아니라 서 있는 것이다.** 유지 시간이 목표의 마지막 한 걸음을
        /// Seeker 가 끊을 수 있는 순간으로 만든다 — 즉시 탈출이면 문이 열리는 순간 매치가 끝난다.
        ///
        /// **거리 판정을 삽입과 같은 함수로 한다**(`IsWithinDoorRange`). 클라이언트는 두 값을
        /// 달리 썼는데(삽입 프롬프트 2.5m, 탈출 판정 2.0m) 그것은 **"서 있으라고 표시된 자리에
        /// 서 있는데 아무 일도 안 일어나는" 구간**을 0.5m 만들어 둔 것이었다. 같은 질문에는
        /// 같은 답을 쓴다(AS-11).
        private void TickEscapes()
        {
            // 문이 닫혀 있으면 아무도 나갈 수 없다. 열려 있어야 문간이 존재한다.
            if (_match.Phase != MatchPhase.Playing || !_objectives.Placed || !_match.DoorOpen)
            {
                return;
            }

            foreach (var player in _players.Values)
            {
                // 이미 나간 사람과 쓰러진 사람은 세지 않는다. 쓰러진 몸이 문간에 있으면
                // 유지 시간이 계속 쌓여 시체가 탈출한다.
                if (player.Escaped || player.Downed)
                {
                    continue;
                }

                if (RoleOf(player.PlayerId) != MatchRole.Runner
                    || !IsWithinDoorRange(player.State.Position))
                {
                    // 벗어나면 처음부터다. 누적이면 문 앞을 스쳐 지나가는 것만으로 탈출한다.
                    player.EscapeHoldTicks = 0;
                    continue;
                }

                player.EscapeHoldTicks++;

                if (player.EscapeHoldTicks < Match.EscapeHoldTicks)
                {
                    continue;
                }

                player.Escaped = true;

                // 들고 있던 열쇠는 함께 나간다. 맵에 되돌리지 않는다 — 되돌리면 문이 이미
                // 열린 뒤이므로 아무도 쓸 수 없는 열쇠가 복도에 생긴다.
                player.CarriedKeys = 0;

                _match.RegisterEscape();
                _matchStateDirty = true;

                _logger.LogInformation(
                    "룸 {RoomId} 플레이어 {PlayerId}: 탈출했다. {Escapes}/{Needed}.",
                    RoomId,
                    player.PlayerId,
                    _match.Escapes,
                    MatchConstants.EscapesToWin);
            }
        }

        /// 문에 닿는가. 수평은 `DoorUseRadius`, 수직은 `InteractHeight` 다.
        ///
        /// 수직을 보지 않으면 **위층에서 아래층 문에 열쇠를 넣을 수 있다.** 문은 Runner 에게만
        /// 보이지만 좌표는 그 클라이언트가 알고 있으므로, 층을 안 보면 벽을 통과해 목표를
        /// 달성하는 경로가 된다.
        private bool IsWithinDoorRange(Vector3 feet)
        {
            var door = _objectives.DoorPosition;

            if (MathF.Abs(feet.Y - door.Y) > MatchConstants.InteractHeight)
            {
                return false;
            }

            var dx = feet.X - door.X;
            var dz = feet.Z - door.Z;

            return (dx * dx) + (dz * dz) <= MatchConstants.DoorUseRadius * MatchConstants.DoorUseRadius;
        }

        /// 발밑끼리 잰다. 수직은 별도 허용치이고 수평보다 크다 —
        /// 이유는 `MatchConstants.KeyPickupHeight` 에 있다.
        private static bool IsWithinPickupRange(Vector3 feet, Vector3 key)
        {
            var dy = feet.Y - key.Y;

            if (MathF.Abs(dy) > MatchConstants.KeyPickupHeight)
            {
                return false;
            }

            var dx = feet.X - key.X;
            var dz = feet.Z - key.Z;

            return (dx * dx) + (dz * dz) <= MatchConstants.KeyPickupRadius * MatchConstants.KeyPickupRadius;
        }

        private void Simulate(PlayerEntity player, in InputFrame frame)
        {
            player.State = PlayerMovement.Step(player.State, frame, _map.Collision);

            // Shared 의 이동 함수가 이미 상한을 두지만 그것은 계산 규칙이다.
            // 여기서 걸리면 Shared 와 판정 중 하나가 어긋났다는 신호다.
            if (InputValidator.TryClampSpeed(ref player.State, out var speed))
            {
                _logger.LogWarning(
                    "룸 {RoomId} 플레이어 {PlayerId}: 수평 속도 {Speed} 가 상한을 넘어 잘렸다.",
                    RoomId,
                    player.PlayerId,
                    speed);
            }
        }

        private void DrainCommands()
        {
            while (_commands.TryDequeue(out var command))
            {
                switch (command.Kind)
                {
                    case RoomCommandKind.Join:
                        Join(command.SessionId, command.PlayerId, command.Name, command.IsHost);
                        break;

                    case RoomCommandKind.Leave:
                        Leave(command.SessionId, command.PlayerId);
                        break;

                    case RoomCommandKind.Start:
                        Start(command.SessionId);
                        break;

                    case RoomCommandKind.EndMatch:
                        EndMatch(command.SessionId, command.Value);
                        break;

                    case RoomCommandKind.ReturnToLobby:
                        ReturnToLobby(command.SessionId);
                        break;

                    case RoomCommandKind.AddBot:
                        JoinBot();
                        break;
                }
            }
        }

        private void Join(int sessionId, byte playerId, string name, bool isHost)
        {
            if (_players.ContainsKey(sessionId) || _players.Count >= RealtimeConstants.Rooms.MaxPlayers)
            {
                return;
            }

            // 어느 스폰을 고를지는 판정이다. PlayerId 로 갈라 같은 룸에서 겹치지 않게 한다.
            _players[sessionId] = new PlayerEntity(
                sessionId,
                playerId,
                _map.SpawnPosition(playerId),
                _map.SpawnYaw(playerId),
                name);

            // 방장 자리는 먼저 주장한 세션이 갖는다. 이미 방장이 있으면 무시한다 —
            // 같은 토큰으로 두 번 붙는 경우이며, 나중 접속에 자리를 넘기면
            // 먼저 붙은 쪽이 조용히 권한을 잃는다.
            if (isHost && _hostSessionId == 0)
            {
                _hostSessionId = sessionId;
            }

            RefreshHostPlayerId();
            _stateDirty = true;
        }

        /// 사람인 참가자 수. 틱 루프에서만 부른다.
        ///
        /// 봇을 빼고 세는 자리가 셋이다 — 채우기 조건, 마지막 사람이 나갔는지, 방장 승계.
        /// 셋 다 "사람이 있는가" 를 묻고 있고, `_players.Count` 로는 답이 되지 않는다.
        private int HumanCount
        {
            get
            {
                var count = 0;
                foreach (var player in _players.Values)
                {
                    if (!player.IsBot)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// 참가자를 몇 명까지 채우는가.
        ///
        /// 설정의 0 은 "매치 시작 최소 인원까지" 로 읽는다. 상한을 여기서 자르는 이유는
        /// 정원 상수가 `internal` 이라는 것이다 — 설정 타입이 정원을 알면 용량 상수가
        /// 공개 표면으로 새어 나간다.
        private int BotFillTarget => _bots.FillTo <= 0
            ? RealtimeConstants.Rooms.MinPlayersToStart
            : Math.Min(_bots.FillTo, RealtimeConstants.Rooms.MaxPlayers);

        /// 부족한 만큼 봇을 넣도록 커맨드를 붙인다. 대기 단계에서만 호출된다.
        ///
        /// **사람이 하나도 없으면 채우지 않는다.** 그러지 않으면 서버가 기동하자마자 빈
        /// 방에서 봇끼리 매치가 돌고, 로그가 그것으로 덮인다. 이 조건은 마지막 사람이
        /// 나갈 때 봇을 지우는 것(`Leave`)과 짝이다 — 지우지 않으면 조건이 계속 참이다.
        ///
        /// 커맨드는 다음 틱에 적용되므로 이 함수가 같은 부족분을 두 번 붙이지 않는다.
        /// `DrainCommands` 가 `Advance` 의 첫 단계이고 이 함수는 마지막 단계다.
        private void TopUpBots()
        {
            if (!_bots.Enabled || !_isStatic || HumanCount == 0)
            {
                return;
            }

            var deficit = BotFillTarget - _players.Count;

            for (var index = 0; index < deficit; index++)
            {
                PostCommand(RoomCommand.AddBot());
            }
        }

        /// 봇 하나를 명단에 넣는다.
        ///
        /// **자격을 여기서 다시 본다.** 커맨드를 붙인 틱과 적용되는 틱이 다르므로 그 사이에
        /// 매치가 시작되었을 수 있고, 그러면 봇이 진행 중인 매치에 합류한다.
        private void JoinBot()
        {
            if (!_bots.Enabled || !_isStatic || Phase != RoomPhase.Waiting)
            {
                return;
            }

            if (!TryReserveSlot(out var playerId))
            {
                // 붙인 뒤에 사람이 들어와 정원이 찼다. 다음 틱의 채우기가 다시 판단한다.
                _logger.LogDebug("룸 {RoomId}: 정원이 차 봇 추가를 건너뛴다.", RoomId);
                return;
            }

            var sessionId = _nextBotSessionId--;

            // 이름을 슬롯에서 만든다. 슬롯은 어느 순간에도 룸 안에서 유일하므로 이름이
            // 겹치지 않고, 스냅샷의 엔티티 id 와 같은 수라 화면의 몸과 명단의 줄을
            // 눈으로 맞출 수 있다. 1 을 더하는 것은 표시용이다 — 0번 봇이라고 부르지 않는다.
            var name = RealtimeConstants.Bots.NamePrefix + (playerId + 1);

            _players[sessionId] = new PlayerEntity(
                sessionId,
                playerId,
                _map.SpawnPosition(playerId),
                _map.SpawnYaw(playerId),
                name);

            _stateDirty = true;

            _logger.LogInformation(
                "룸 {RoomId}: 봇 {Name} 추가. 세션 {SessionId}, 플레이어 {PlayerId}, 인원 {Count}/{Target}",
                RoomId,
                name,
                sessionId,
                playerId,
                _players.Count,
                BotFillTarget);
        }

        /// 봇을 전부 지운다. 사람이 다 나갔을 때만 부른다.
        ///
        /// 남겨 두면 `Leave` 의 빈 룸 판정(`_players.Count == 0`)이 성립하지 않아 단계가
        /// 되돌아가지 않고, 정적이 아닌 룸이라면 `RoomRegistry.Sweep` 이 참가자가 있다고
        /// 보아 그 룸을 **영구히 회수하지 못한다.**
        private void RemoveAllBots()
        {
            _removalBuffer.Clear();

            foreach (var pair in _players)
            {
                if (pair.Value.IsBot)
                {
                    _removalBuffer.Add(pair.Key);
                }
            }

            if (_removalBuffer.Count == 0)
            {
                return;
            }

            foreach (var sessionId in _removalBuffer)
            {
                if (_players.Remove(sessionId, out var bot))
                {
                    ReleaseSlot(bot.PlayerId);
                }
            }

            _stateDirty = true;

            _logger.LogInformation(
                "룸 {RoomId}: 사람이 모두 나가 봇 {Count} 명을 지웠다.",
                RoomId,
                _removalBuffer.Count);
        }

        private void Leave(int sessionId, byte playerId)
        {
            _players.Remove(sessionId);
            ReleaseSlot(playerId);

            for (var index = _heldInputs.Count - 1; index >= 0; index--)
            {
                if (_heldInputs[index].SessionId == sessionId)
                {
                    _heldInputs.RemoveAt(index);
                }
            }

            // 사람이 다 나갔으면 봇도 지운다. **아래의 빈 룸 판정보다 앞이어야 한다** —
            // 봇이 남아 있으면 그 판정이 성립하지 않아 단계가 되돌아가지 않는다.
            if (HumanCount == 0)
            {
                RemoveAllBots();
            }

            // 방장 승계는 여기서 한다. 접속 스레드에서 하면 퇴장 커맨드가 적용되기 전이라
            // 이미 나간 세션을 방장으로 만든다.
            if (sessionId == _hostSessionId)
            {
                _hostSessionId = LowestRemainingSessionId();

                if (_hostSessionId != 0)
                {
                    _logger.LogInformation(
                        "룸 {RoomId}: 방장이 나가 세션 {SessionId} 가 승계했다.",
                        RoomId,
                        _hostSessionId);
                }
            }

            // 아무도 없는 룸이 진행 중으로 남으면, 다음에 들어온 사람이 이미
            // 시작된 매치에 갇힌다. 룸 회수는 별개이고 여기서는 단계를 되돌린다.
            if (_players.Count == 0 && Phase != RoomPhase.Waiting)
            {
                ResetToWaiting();
            }

            RefreshHostPlayerId();
            _stateDirty = true;
        }

        private void Start(int sessionId)
        {
            if (Phase != RoomPhase.Waiting)
            {
                return;
            }

            if (!IsAuthorized(sessionId))
            {
                _logger.LogInformation("룸 {RoomId}: 방장이 아닌 세션 {SessionId} 의 시작 요청을 무시했다.", RoomId, sessionId);
                return;
            }

            if (_players.Count < RealtimeConstants.Rooms.MinPlayersToStart)
            {
                _logger.LogInformation(
                    "룸 {RoomId}: 인원 {Count} 명으로는 시작할 수 없다. 최소 {Min} 명.",
                    RoomId,
                    _players.Count,
                    RealtimeConstants.Rooms.MinPlayersToStart);
                return;
            }

            _seekerPlayerId = PickSeeker();
            _placementSeed = NextPlacementSeed();
            _outcome = 0;

            // 지난 매치에서 날아가던 총알을 지운다. 남겨 두면 새 매치의 첫 틱에 아무도
            // 쏘지 않은 총알이 벽에 맞는다.
            for (var index = 0; index < _projectiles.Length; index++)
            {
                _projectiles[index] = default;
            }

            // 아직 나가지 않은 발사 알림도 버린다. 보통 한 틱 안에 나가지만, 남아 있으면
            // 새 매치의 첫 프레임에 지난 매치의 예광탄이 그려진다.
            _pendingFireCount = 0;

            // 매치는 역할 공개부터 시작한다. 이 시점부터 시계는 서버의 것이다.
            _match.Begin();
            _matchStateDirty = true;

            // 목표물을 서버가 배치한다. 씨드는 서버 안에만 있고 와이어에 없다 — 좌표를
            // 역할별로 걸러 내려보내므로(IG-011b) Seeker 는 문을 받지도, 계산하지도 못한다.
            var placement = new DeterministicSequence(_placementSeed);
            ObjectivePlacement.PlaceObjectives(_objectives, _map.Grid, ref placement);

            // 피격 순간이동은 배치와 다른 수열을 쓴다. 골든 비율 상수로 씨드를 흩어 두 용도가
            // 같은 값에서 출발하지 않게 한다 — 그러면 첫 순간이동이 제단 자리가 된다.
            _hitRandom = new DeterministicSequence(_placementSeed ^ unchecked((int)0x9E3779B9));

            _objectiveStateDirty = true;

            if (!_objectives.Placed)
            {
                // 격자가 없는 맵이다. 이동과 전투는 되지만 목표가 없으므로 Runner 가 이길
                // 방법이 없다 — 조용히 지나가면 "왜 열쇠가 없는지" 를 찾는 데 시간이 걸린다.
                _logger.LogWarning(
                    "룸 {RoomId}: 맵 {MapName} 에 격자가 없어 목표물을 배치하지 못했다. " +
                    "Export 에 격자가 포함되었는지 확인한다.",
                    RoomId,
                    _map.Name);
            }

            // 커맨드는 틱을 올리기 전에 드레인된다. 그래서 +1 이 실제로 시뮬레이션되는
            // 첫 틱이며, 이 값과 스냅샷의 틱이 같은 기준이 된다.
            _startTick = _tick + 1u;

            // 배치는 서버가 한다. 이동이 서버 권위이므로 클라이언트가 자기 몸을
            // 옮겨 놓아도 다음 스냅샷이 되돌린다.
            foreach (var player in _players.Values)
            {
                player.RespawnAt(_map.SpawnPosition(player.PlayerId), _map.SpawnYaw(player.PlayerId));

                // 지난 매치의 소지 열쇠를 지운다. `RespawnAt` 에 넣지 않는 것은 그 함수가
                // 피격 순간이동에도 쓰일 예정이기 때문이다(IG-014) — 맞았다고 들고 있던
                // 열쇠가 사라지면 그것은 배치가 아니라 규칙이다.
                player.CarriedKeys = 0;

                // 로비에서 누른 E 가 매치 첫 틱에 발동하지 않게 한다. `NextInsertTick` 은
                // 되돌리지 않는다 — 틱 카운터가 이어지므로 지난 매치의 값은 항상 과거다.
                player.InteractRequested = false;

                // 지난 매치에 빠져나간 사람이 이번 매치를 탈출한 상태로 시작하지 않게 한다.
                player.Escaped = false;
                player.EscapeHoldTicks = 0;

                // 탄창은 역할과 무관하게 채운다. 역할은 발사 판정에서 본다 — 여기서 Seeker 만
                // 채우면 매치 중 역할이 바뀌는 경로가 생길 때 빈 탄창을 든 Seeker 가 나온다.
                player.Ammo = MatchConstants.SeekerMagazine;

                // 지난 매치의 피격을 지운다. `ImmuneUntilTick` 은 절대 틱이라 언제나 과거다.
                player.Hits = 0;
                player.Downed = false;
            }

            Volatile.Write(ref _phase, (int)RoomPhase.Playing);
            _stateDirty = true;

            _logger.LogInformation(
                "룸 {RoomId} 매치 시작. 틱 {Tick}, 인원 {Count} 명, Seeker {Seeker}, 배치 씨드 {Seed}",
                RoomId,
                _tick,
                _players.Count,
                _seekerPlayerId,
                _placementSeed);
        }

        /// 결과를 판정한 것은 방장 클라이언트다. 매치 규칙이 아직 클라이언트에 있는
        /// 동안의 한시적 경로이며, 서버는 단계 전이와 중계만 한다.
        private void EndMatch(int sessionId, byte outcome)
        {
            if (Phase != RoomPhase.Playing || !IsAuthorized(sessionId))
            {
                return;
            }

            _outcome = outcome;
            _match.ForceEnd();
            Volatile.Write(ref _phase, (int)RoomPhase.Ended);
            _stateDirty = true;
            _matchStateDirty = true;

            _logger.LogInformation("룸 {RoomId} 매치 종료. 결과 코드 {Outcome}", RoomId, outcome);
        }

        /// 서버의 시계가 매치를 끝냈다.
        ///
        /// **결과 코드를 채우지 않는다.** 기획서 §8 은 시간 종료를 술래 승리로 정하지만,
        /// 구현과 어긋나는 지점이 남아 있어(전멸 승리 유무 OQ-2, 2인 매치에서 Runner
        /// 승리가 구조적으로 불가능 OQ-6) 승패 판정을 여기서 추측하지 않는다. 단계만
        /// 옮기고 `_outcome` 은 0(미정)으로 둔다 — IG-007 이 그 자리를 채운다.
        private void EndMatchByServer()
        {
            Volatile.Write(ref _phase, (int)RoomPhase.Ended);
            _stateDirty = true;
            _matchStateDirty = true;

            _logger.LogInformation(
                "룸 {RoomId}: 매치 시간이 끝나 서버가 종료했다. 틱 {Tick}",
                RoomId,
                _tick);
        }

        private void ReturnToLobby(int sessionId)
        {
            if (Phase == RoomPhase.Waiting || !IsAuthorized(sessionId))
            {
                return;
            }

            ResetToWaiting();
            _stateDirty = true;
        }

        private void ResetToWaiting()
        {
            Volatile.Write(ref _phase, (int)RoomPhase.Waiting);
            _seekerPlayerId = RoomStateHeader.NoPlayer;
            _placementSeed = 0;
            _startTick = 0;
            _outcome = 0;
            _match.Reset();
            _objectives.Reset();
            _matchStateDirty = true;
            _objectiveStateDirty = true;
        }

        /// 방장이 필요 없는 룸에서는 누구나 시작할 수 있다. 설정으로 미리 열어 둔
        /// 개발용 룸이 그 경우다 — 코드를 발급받는 경로가 없으므로 방장도 없다.
        private bool IsAuthorized(int sessionId)
        {
            if (_isStatic)
            {
                return true;
            }

            return sessionId != 0 && sessionId == _hostSessionId;
        }

        private void RefreshHostPlayerId()
        {
            var hostPlayerId = (int)RoomStateHeader.NoPlayer;

            if (_hostSessionId != 0 && _players.TryGetValue(_hostSessionId, out var host))
            {
                hostPlayerId = host.PlayerId;
            }

            Volatile.Write(ref _hostPlayerId, hostPlayerId);
        }

        /// 남은 사람 중 가장 작은 PlayerId 의 세션. 아무도 없으면 0 이다.
        ///
        /// 접속 순서가 아니라 PlayerId 순으로 고른다. 슬롯 번호는 룸 안에서 유일하고
        /// 모든 클라이언트가 같은 값을 보므로, 누가 승계했는지 화면에서 확인할 수 있다.
        ///
        /// **봇은 후보가 아니다.** 봇의 슬롯 번호가 남은 사람보다 작을 수 있고(사람이 나간
        /// 자리를 봇이 채운 경우), 그러면 방장 자리가 아무 요청도 보내지 않는 참가자에게
        /// 간다. 정적 룸은 전원에게 시작 권한이 있어 증상이 보이지 않지만, 그 룸이 유일하게
        /// 봇이 있는 룸이라서 증상이 없을 뿐이다 — 승계 규칙 자체는 옳아야 한다.
        private int LowestRemainingSessionId()
        {
            var bestPlayerId = int.MaxValue;
            var bestSessionId = 0;

            foreach (var player in _players.Values)
            {
                if (player.IsBot)
                {
                    continue;
                }

                if (player.PlayerId < bestPlayerId)
                {
                    bestPlayerId = player.PlayerId;
                    bestSessionId = player.SessionId;
                }
            }

            return bestSessionId;
        }

        private byte PickSeeker()
        {
            // 봇 역할 희망은 **정적 룸에서만** 지킨다. 초대 코드 룸의 역할 배정은 이 갈래를
            // 지나지 않으므로 지금까지와 완전히 같다 — 개발용 훅이 실제 매치에 닿지 않는
            // 경계가 이 한 줄이다.
            if (_isStatic && _bots.Enabled && TryPickPreferredSeeker(out var preferred))
            {
                return preferred;
            }

            Span<byte> ids = stackalloc byte[RealtimeConstants.Rooms.MaxPlayers];
            var count = 0;

            foreach (var player in _players.Values)
            {
                ids[count] = player.PlayerId;
                count++;
            }

            return ids[Random.Shared.Next(count)];
        }

        /// 봇 역할 희망이 요구하는 쪽에서 술래를 고른다. 고를 수 없으면 false.
        ///
        /// `BotOptions.Role` 은 **봇이** 맡을 역할이다. 봇이 Runner 이길 바라면 술래는
        /// 사람이어야 하므로 후보가 사람 쪽이 된다.
        ///
        /// 지킬 수 없으면 경고를 남긴다. 조용히 무작위로 떨어지면 "역할 강제가 동작하지
        /// 않는다" 를 찾는 데 시간이 걸리고, 그 원인이 "그 역할을 맡을 참가자가 없었다" 라는
        /// 것은 코드를 읽어야만 알 수 있다.
        private bool TryPickPreferredSeeker(out byte playerId)
        {
            playerId = 0;

            if (_bots.Role == BotRolePreference.Any)
            {
                return false;
            }

            var seekerIsBot = _bots.Role == BotRolePreference.Seeker;

            Span<byte> ids = stackalloc byte[RealtimeConstants.Rooms.MaxPlayers];
            var count = 0;

            foreach (var player in _players.Values)
            {
                if (player.IsBot == seekerIsBot)
                {
                    ids[count] = player.PlayerId;
                    count++;
                }
            }

            if (count == 0)
            {
                _logger.LogWarning(
                    "룸 {RoomId}: 봇 역할 희망 {Role} 을 지킬 참가자가 없어 술래를 무작위로 고른다.",
                    RoomId,
                    _bots.Role);

                return false;
            }

            playerId = ids[Random.Shared.Next(count)];
            return true;
        }

        /// 0 이 아닌 씨드를 만든다.
        ///
        /// 0 을 피하는 이유가 클라이언트에 있다. 클라이언트의 매치 설정은 씨드가 0 이면
        /// 자기 시계로 난수를 만드는데, 그러면 플레이어마다 문과 열쇠가 다른 곳에 생긴다.
        private static int NextPlacementSeed()
        {
            int seed;
            do
            {
                seed = Random.Shared.Next(int.MinValue, int.MaxValue);
            }
            while (seed == 0);

            return seed;
        }

        private void DrainInputs()
        {
            // 지연으로 보류된 입력을 먼저 검사한다.
            for (var index = _heldInputs.Count - 1; index >= 0; index--)
            {
                var held = _heldInputs[index];
                if (held.ReleaseTick > _tick)
                {
                    continue;
                }

                Buffer(held);
                _heldInputs.RemoveAt(index);
            }

            while (_inputs.TryDequeue(out var input))
            {
                if (input.ReleaseTick > _tick)
                {
                    _heldInputs.Add(input);
                    continue;
                }

                Buffer(input);
            }
        }

        private void DiscardInputs()
        {
            while (_inputs.TryDequeue(out _))
            {
            }

            _heldInputs.Clear();
        }

        private void Buffer(in InboundInput input)
        {
            if (_players.TryGetValue(input.SessionId, out var player))
            {
                player.TryBuffer(input);
            }
        }
    }
}
