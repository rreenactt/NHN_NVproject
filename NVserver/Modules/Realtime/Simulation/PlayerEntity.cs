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

        /// 매치 판정이 소유하는 플래그 — 출혈·역할·탈출·잠금.
        ///
        /// `State.Flags` 와 나누어 둔다. 그쪽은 이동 시뮬레이션의 것이고 클라이언트도
        /// 예측하지만, 이것은 서버만 아는 판정 결과다. 한 곳에 담으면 `StateHash` 에
        /// 예측 불가능한 비트가 섞여 리컨실리에이션 비교가 영구히 어긋난다.
        ///
        /// 스냅샷을 인코딩할 때 합쳐진다(`StateProjection.ToEntityState` 3인자 오버로드).
        public EntityFlags MatchFlags { get; set; }

        /// 이 플레이어가 들고 있는 열쇠 수. 서버가 습득 판정으로 올린다.
        ///
        /// `Objectives.Keys` 에서 빠진 열쇠는 반드시 누군가의 이 값으로 들어간다 —
        /// 둘의 합이 `MatchConstants.KeysPlaced` 에서 삽입된 수를 뺀 값이어야 한다.
        /// 죽으면 떨어뜨려 다시 맵으로 돌아간다(IG-014).
        public int CarriedKeys { get; set; }

        /// 이번 틱에 상호작용(E)을 요청했는가.
        ///
        /// **엣지다. 한 틱만 산다** — 목표물 판정이 읽고 즉시 지운다. 눌린 상태로 들고 있으면
        /// 한 번의 키 입력이 삽입 간격마다 계속 발동한다.
        public bool InteractRequested { get; set; }

        /// 다음 삽입이 가능한 틱. 기획서에 없는 간격이지만 없으면 한 번의 입력으로 열쇠
        /// 10개가 다 들어간다(`MatchConstants.KeyInsertInterval` 참조).
        ///
        /// 매치가 다시 시작될 때 되돌리지 않는다. 틱 카운터는 접속 내내 이어지므로 지난
        /// 매치에 적힌 값은 언제나 과거다.
        public uint NextInsertTick { get; set; }

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
