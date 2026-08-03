using System.Numerics;
using NV.Realtime.Transport;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using NV.Shared.Simulation;

namespace NV.Realtime.Simulation
{
    /// 룸 안의 플레이어. 틱 루프만 이 객체를 만지며 잠금이 없다.
    /// 예외는 TryBuffer 로, 수신 펌프가 호출한다 — Room 이 큐를 통해 직렬화한다.
    internal sealed class PlayerEntity
    {
        private readonly InboundInput[] _inputs = new InboundInput[RealtimeConstants.Players.InputBufferCapacity];
        private int _inputCount;

        public PlayerEntity(int sessionId, byte playerId, Vector3 spawnPosition, float spawnYaw, string name = "")
        {
            SessionId = sessionId;
            PlayerId = playerId;
            Name = name ?? string.Empty;
            State = PlayerState.Spawn(spawnPosition, spawnYaw, RealtimeConstants.Players.MaxHealth);
            LastInput = new InputFrame(ButtonFlags.None, 0, 0, Quantization.ToFixedYaw(spawnYaw), 0);
            Wire = StateProjection.ToEntityState(playerId, State);
        }

        public int SessionId { get; }

        public byte PlayerId { get; }

        /// 표시 이름. 명단에만 쓰이며 판정에 관여하지 않는다.
        public string Name { get; }

        /// 필드로 둔다. 프로퍼티면 매 접근마다 구조체가 복사되어 제자리 수정이 안 된다.
        public PlayerState State;

        /// 마지막으로 인코딩한 와이어 상태. 스냅샷은 이 값을 그대로 보낸다.
        public EntityState Wire { get; set; }

        public uint LastProcessedInputTick { get; private set; }

        public bool HasProcessedInput { get; private set; }

        public uint HighestInputTick { get; private set; }

        public bool HasInputBaseline { get; private set; }

        public InputFrame LastInput { get; set; }

        /// 새 입력 없이 마지막 입력을 반복한 횟수.
        public int RepeatCount { get; set; }

        public int BufferedInputCount => _inputCount;

        /// 매치 시작 시 스폰으로 되돌린다.
        ///
        /// 배치를 서버가 한다. 이동이 서버 권위이므로 클라이언트가 자기 몸을 옮겨도
        /// 다음 스냅샷이 되돌리고, 증상은 시작 직후의 순간이동 한 번으로만 보인다.
        ///
        /// 입력 기록은 지우지 않는다. 틱 카운터는 접속 내내 이어지므로 여기서 0 으로
        /// 되돌리면 이미 처리한 틱 번호의 입력이 다시 유효해진다.
        public void RespawnAt(Vector3 position, float yaw)
        {
            State = PlayerState.Spawn(position, yaw, RealtimeConstants.Players.MaxHealth);
            LastInput = new InputFrame(ButtonFlags.None, 0, 0, Quantization.ToFixedYaw(yaw), 0);
            RepeatCount = 0;
            Wire = StateProjection.ToEntityState(PlayerId, State);

            for (var index = 0; index < _inputCount; index++)
            {
                _inputs[index] = default;
            }

            _inputCount = 0;
        }

        /// 버렸으면 false. 버린 이유는 호출자가 로그로 남긴다.
        public bool TryBuffer(in InboundInput input)
        {
            if (HasProcessedInput && input.Tick <= LastProcessedInputTick)
            {
                // 최근 여러 틱치를 중복 전송하므로 이미 적용한 틱이 계속 들어온다.
                return false;
            }

            if (HasInputBaseline && input.Tick > HighestInputTick + RealtimeConstants.Players.MaxInputLead)
            {
                return false;
            }

            for (var index = 0; index < _inputCount; index++)
            {
                if (_inputs[index].Tick == input.Tick)
                {
                    return false;
                }
            }

            if (_inputCount == RealtimeConstants.Players.InputBufferCapacity)
            {
                RemoveAt(0);
            }

            InsertSorted(input);

            if (!HasInputBaseline || input.Tick > HighestInputTick)
            {
                HighestInputTick = input.Tick;
            }

            HasInputBaseline = true;
            return true;
        }

        /// 가장 오래된 미처리 입력을 꺼낸다. 꺼낸 시점에 처리 기록이 갱신된다.
        public bool TryTakeNext(out InboundInput input)
        {
            if (_inputCount == 0)
            {
                input = default;
                return false;
            }

            input = _inputs[0];
            RemoveAt(0);

            LastProcessedInputTick = input.Tick;
            HasProcessedInput = true;
            return true;
        }

        private void InsertSorted(in InboundInput input)
        {
            var position = _inputCount;
            while (position > 0 && _inputs[position - 1].Tick > input.Tick)
            {
                _inputs[position] = _inputs[position - 1];
                position--;
            }

            _inputs[position] = input;
            _inputCount++;
        }

        private void RemoveAt(int index)
        {
            for (var shift = index; shift < _inputCount - 1; shift++)
            {
                _inputs[shift] = _inputs[shift + 1];
            }

            _inputCount--;
            _inputs[_inputCount] = default;
        }
    }
}
