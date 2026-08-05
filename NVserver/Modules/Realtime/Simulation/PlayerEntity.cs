using System.Collections.Generic;
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

        /// 소켓 없는 참가자인가. **세션 id 의 부호에서 유도한다.**
        ///
        /// 별도 필드를 두지 않는 이유는 `Bleeding` 과 같다 — "세션이 있는데 봇" 이
        /// 표현 가능한 상태가 되면 그 상태에 빠지는 경로를 찾는 일이 남는다.
        ///
        /// 부호로 가를 수 있는 근거는 `SessionRegistry.AllocateSessionId` 가
        /// `Interlocked.Increment` 로 1 이상만 낸다는 것이다. 룸이 봇에게 -1 부터
        /// 내려가며 발급하므로 두 공간이 겹치지 않는다.
        public bool IsBot => SessionId < 0;

        /// 표시 이름. 명단에만 쓰이며 판정에 관여하지 않는다.
        public string Name { get; }

        /// 대기방에서 준비를 눌렀는가. **대기 단계에서만 의미가 있다.**
        ///
        /// 매치 시작 조건이 이 값을 본다(`Room.Start`). 매치가 끝나고 로비로 돌아올 때
        /// 전원 내려간다(`Room.ResetToWaiting`) — 남겨 두면 자리를 비운 사람을 데리고
        /// 다음 매치가 시작된다.
        ///
        /// 봇은 항상 거짓이다. 봇은 준비 요청을 보내지 않으므로 조건에서 빼야 하고,
        /// 그 예외가 시작 판정 쪽에 있다.
        public bool Ready { get; set; }

        /// 입고 있는 캐릭터 번호. 방 안에서 유일하다.
        ///
        /// 서버는 **번호만** 안다. 무엇처럼 생겼는지는 클라이언트의 표이고, 서버가 판정하는
        /// 것은 범위(`ProtocolInfo.LobbyCharacterCount`)와 중복 둘뿐이다.
        ///
        /// 판정에 관여하지 않는다 — 이동·전투·목표물 어디도 이 값을 읽지 않는다. 읽기
        /// 시작하면 외형 선택이 실력 차이가 되고, 그 순간 이 값은 `Shared` 의 시뮬레이션
        /// 상수 쪽으로 옮겨야 한다.
        public byte CharacterId { get; set; } = RoomPlayerEntry.NoCharacter;

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

        /// 받은 피격 수. 기획서 §4.1 — `RunnerHitsToDie` 회면 쓰러진다.
        public int Hits { get; set; }

        /// 쓰러졌는가. 기획서 §4.1 의 2회 피격이다.
        ///
        /// 탈출과 같이 몸을 지우지 않는다 — 전멸 판정이 명단을 세어야 하고(IG-007), 스냅샷의
        /// `EntityFlags.Downed` 가 클라이언트에게 감추라고 말한다.
        public bool Downed { get; set; }

        /// 출혈 중인가. **피격 수에서 유도한다.**
        ///
        /// 따로 필드를 두면 "1방 맞았는데 피가 안 난다" 가 표현 가능한 상태가 된다.
        /// 쓰러진 뒤에는 출혈이 의미가 없으므로 함께 내린다(기획서 §4.2 의 흔적은 도망치는
        /// Runner 를 쫓는 장치다).
        public bool Bleeding => Hits > 0 && !Downed;

        /// 이 틱까지는 피격이 무시된다. 3연사가 순간이동을 관통해 죽이는 것을 막는다.
        public uint ImmuneUntilTick { get; set; }

        /// 탄창에 남은 탄. 기획서 §4.3 — Seeker 만 쓴다.
        ///
        /// **재장전하는 것은 체인이다.** 탄창이 비면 체인이 걸리고(<see cref="ChainReleaseTick"/>),
        /// 제단으로 끌려가 3초를 기다린 뒤에야 이 값이 다시 찬다. 그래서 이 필드를 채우는
        /// 곳은 매치 시작과 체인이 놓아주는 순간 둘뿐이다 — 그 밖에서 채우면 기획서 §4.3 의
        /// 벌칙이 사라진다.
        public int Ammo { get; set; }

        /// 다음 발사가 가능한 틱. `NextInsertTick` 과 같은 이유로 매치 시작에 되돌리지 않는다.
        public uint NextFireTick { get; set; }

        /// 체인이 끌고 가는 경로. 시작 지점에서 제단 옆자리까지 **걸어갈 수 있는** 길이다.
        ///
        /// 직선이 아니다. 직선으로 끌면 벽을 뚫고 지나가고, 벌칙의 값어치가 "걸어서 온 거리를
        /// 되돌린다" 에서 "지도상의 간격만큼 되돌린다" 로 바뀐다 — 멀리 돌아온 사람일수록 덜
        /// 손해를 보게 된다. `GridRoute` 가 격자 위에서 찾는다.
        public List<Vector3> ChainRoute { get; } = new();

        /// <see cref="ChainRoute"/> 의 총 길이(m). 견인 시간이 여기서 나온다.
        public float ChainRouteLength { get; set; }

        /// 견인이 시작된 틱. <see cref="ChainFrom"/> 과 짝이며 보간의 분모를 만든다.
        public uint ChainStartTick { get; set; }

        /// 견인이 끝나는 틱. 이 틱까지는 제단 쪽으로 끌려가는 중이다.
        public uint ChainDragUntilTick { get; set; }

        /// 체인이 놓아주는 틱. **이 틱에 탄창이 찬다.** 0 이면 체인에 걸려 있지 않다.
        public uint ChainReleaseTick { get; set; }

        /// 지금 체인에 걸려 있는가. 걸려 있는 동안 이동 입력이 무시되고 위치는 체인이 정한다.
        public bool Chained => ChainReleaseTick != 0u;

        /// 열린 문간을 빠져나갔는가. 기획서 §3 의 탈출이다.
        ///
        /// 몸을 지우지 않는다. 스냅샷에 `EntityFlags.Escaped` 로 실려 클라이언트가 감추고
        /// (`PlayerAgent.SetPresent(false)`), 승리 조건이 아직 명단을 세어야 한다 —
        /// 서버에서 빼면 전멸 판정이 탈출을 사망으로 셀 수 있다.
        public bool Escaped { get; set; }

        /// 문간에 연달아 머문 틱 수. 문에서 벗어나면 0 으로 돌아간다.
        ///
        /// **누적이 아니라 연속이어야 한다.** 누적이면 문 앞을 여러 번 스쳐 지나가는 것으로도
        /// 탈출이 성립하고, Seeker 가 끊을 수 있는 순간을 만들려던 `EscapeHoldTime` 의 의미가
        /// 사라진다.
        public int EscapeHoldTicks { get; set; }

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
