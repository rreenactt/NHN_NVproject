using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using NV.Realtime.Contracts;
using NV.Realtime.Transport;
using NV.Shared.Collision;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using NV.Shared.Simulation;
using NV.Shared.Transport;

namespace NV.Realtime.Simulation
{
    /// 진행 중인 매치 하나. 상태 소유자는 틱 루프다.
    ///
    /// 스레드 경계
    /// - 틱 루프: _players, Tick, 스냅샷 버퍼, 슬롯 해제, 시뮬레이션
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

        private readonly bool[] _slots = new bool[RealtimeConstants.Rooms.MaxPlayers];
        private readonly object _slotGate = new();

        private readonly WorldMap _map;
        private readonly NetworkConditionSimulator _network;
        private readonly ILogger _logger;

        private uint _tick;
        private int _playerCount;

        public Room(string roomId, WorldMap map, NetworkConditionSimulator network, ILogger logger)
        {
            RoomId = roomId;
            _map = map;
            _network = network;
            _logger = logger;
        }

        public string RoomId { get; }

        public uint MapHash => _map.Hash;

        /// uint 정렬 읽기는 원자적이라 조회 스레드가 찢어진 값을 보지 않는다.
        public uint Tick => _tick;

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
            return new RoomSummary(RoomId, _tick, Volatile.Read(ref _playerCount), RealtimeConstants.Rooms.MaxPlayers);
        }

        /// 틱 루프에서만 호출한다.
        public void Advance()
        {
            DrainCommands();

            _tick++;

            DrainInputs();

            foreach (var player in _players.Values)
            {
                StepPlayer(player);
            }

            Volatile.Write(ref _playerCount, _players.Count);
        }

        /// 틱 루프에서만 호출한다. 매 틱 풀 스냅샷을 보낸다.
        /// AckedInputTick 이 수신자마다 다르므로 세션별로 인코딩한다.
        public void Broadcast(IServerTransport transport)
        {
            if (_players.Count == 0)
            {
                return;
            }

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
                Simulate(player, frame);

                player.LastInput = frame;
                player.RepeatCount = 0;
                applied++;
            }

            if (applied == 0)
            {
                if (player.RepeatCount < RealtimeConstants.Rooms.MaxInputRepeatTicks)
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
                        Join(command.SessionId, command.PlayerId);
                        break;

                    case RoomCommandKind.Leave:
                        Leave(command.SessionId, command.PlayerId);
                        break;
                }
            }
        }

        private void Join(int sessionId, byte playerId)
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
                _map.SpawnYaw(playerId));
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

        private void Buffer(in InboundInput input)
        {
            if (_players.TryGetValue(input.SessionId, out var player))
            {
                player.TryBuffer(input);
            }
        }
    }
}
