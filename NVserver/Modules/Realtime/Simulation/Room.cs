using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using NV.Realtime.Contracts;
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

        private readonly EntityState[] _entityBuffer = new EntityState[RealtimeConstants.Rooms.MaxPlayers];
        private readonly byte[] _sendBuffer = new byte[MessageCodec.SnapshotWireSize(RealtimeConstants.Rooms.MaxPlayers)];

        private readonly RoomPlayerEntry[] _rosterBuffer = new RoomPlayerEntry[RealtimeConstants.Rooms.MaxPlayers];
        private readonly byte[] _stateBuffer = new byte[MessageCodec.RoomStateMaxWireSize(RealtimeConstants.Rooms.MaxPlayers)];

        private readonly MatchParticipant[] _participantBuffer = new MatchParticipant[RealtimeConstants.Rooms.MaxPlayers];
        private readonly byte[] _matchStateBuffer = new byte[MessageCodec.MatchStateMaxWireSize(RealtimeConstants.Rooms.MaxPlayers)];

        private readonly bool[] _slots = new bool[RealtimeConstants.Rooms.MaxPlayers];
        private readonly object _slotGate = new();

        /// 매치의 단계와 시계. 틱 루프만 만진다.
        ///
        /// 룸 단계(`_phase`)와 다른 축이다. 룸이 `Playing` 인 동안 매치는 역할 공개
        /// 중일 수 있고, 그때는 이동만 잠긴다 — 시뮬레이션은 계속 돌아야 한다.
        private readonly Match _match = new();

        private readonly WorldMap _map;
        private readonly NetworkConditionSimulator _network;
        private readonly ILogger _logger;
        private readonly bool _isStatic;

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

        public Room(
            string roomId,
            WorldMap map,
            NetworkConditionSimulator network,
            ILogger logger,
            bool isStatic = false,
            bool isPublic = false)
        {
            RoomId = roomId;
            _map = map;
            _network = network;
            _logger = logger;
            _isStatic = isStatic;
            _isPublic = isPublic;
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

            var header = new RoomStateHeader(
                Phase,
                (byte)Volatile.Read(ref _hostPlayerId),
                _seekerPlayerId,
                _outcome,
                _startTick,
                _placementSeed,
                (byte)count);

            var length = MessageCodec.WriteRoomState(
                _stateBuffer,
                header,
                new ReadOnlySpan<RoomPlayerEntry>(_rosterBuffer, 0, count));

            foreach (var player in _players.Values)
            {
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
                // 열쇠·피격·상태 플래그는 아직 서버가 세지 않는다. 자리를 잡아 두었으므로
                // 그 판정이 올 때(IG-012·IG-014) 와이어 포맷은 바뀌지 않는다.
                _participantBuffer[count] = new MatchParticipant(
                    player.PlayerId,
                    RoleOf(player.PlayerId),
                    0,
                    0,
                    0);

                count++;
            }

            var header = new MatchStateHeader(
                _match.Phase,
                MatchStateHeader.ToTenths(_match.MatchSecondsRemaining),
                0,
                0,
                _outcome,
                (byte)count);

            var participants = new ReadOnlySpan<MatchParticipant>(_participantBuffer, 0, count);

            foreach (var player in _players.Values)
            {
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
            var applied = 0;

            while (applied < RealtimeConstants.Rooms.MaxInputsPerTick && player.TryTakeNext(out var input))
            {
                var frame = InputValidator.Sanitize(input.Frame);

                // 역할 공개와 결과 화면에서는 이동을 비운다. 입력은 그래도 **소비한다** —
                // 버리기만 하면 잠금이 풀리는 순간 쌓인 입력이 한꺼번에 적용되어
                // 플레이어가 순간이동한다. 시선은 남기므로 리빌 중에도 둘러볼 수 있다.
                if (_match.MovementLocked)
                {
                    frame = InputValidator.Neutral(frame);
                }

                Simulate(player, frame);

                player.LastInput = frame;
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

            player.Wire = StateProjection.ToEntityState(player.PlayerId, player.State);
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

            // 매치는 역할 공개부터 시작한다. 이 시점부터 시계는 서버의 것이다.
            _match.Begin();
            _matchStateDirty = true;

            // 커맨드는 틱을 올리기 전에 드레인된다. 그래서 +1 이 실제로 시뮬레이션되는
            // 첫 틱이며, 이 값과 스냅샷의 틱이 같은 기준이 된다.
            _startTick = _tick + 1u;

            // 배치는 서버가 한다. 이동이 서버 권위이므로 클라이언트가 자기 몸을
            // 옮겨 놓아도 다음 스냅샷이 되돌린다.
            foreach (var player in _players.Values)
            {
                player.RespawnAt(_map.SpawnPosition(player.PlayerId), _map.SpawnYaw(player.PlayerId));
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
            _matchStateDirty = true;
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
        private int LowestRemainingSessionId()
        {
            var bestPlayerId = int.MaxValue;
            var bestSessionId = 0;

            foreach (var player in _players.Values)
            {
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
            Span<byte> ids = stackalloc byte[RealtimeConstants.Rooms.MaxPlayers];
            var count = 0;

            foreach (var player in _players.Values)
            {
                ids[count] = player.PlayerId;
                count++;
            }

            return ids[Random.Shared.Next(count)];
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
