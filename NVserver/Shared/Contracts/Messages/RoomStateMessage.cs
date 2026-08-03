using NV.Shared.Contracts.Enums;

namespace NV.Shared.Contracts.Messages
{
    /// 룸 상태 전문의 고정부. 가변부는 플레이어 항목이 뒤따른다.
    ///
    /// 이것은 "무엇이 바뀌었다" 는 알림이 아니라 "지금 상태는 이렇다" 는 전문이다.
    /// 서버는 이것을 2Hz 로 계속 보내고 클라이언트는 받은 값으로 자기 화면을 맞춘다.
    ///
    /// 한 번짜리 알림으로 만들면 안 된다. 세션의 송신 채널은 가득 차면 오래된 것을
    /// 버리도록(DropOldest) 되어 있고 — 스냅샷은 다음 틱이 대체하므로 그래도 되지만 —
    /// "매치가 시작됐다" 를 한 번만 보내면 그 프레임이 버려진 클라이언트는 로비 화면에
    /// 영구히 남는다. 멱등한 전문을 반복하면 ack 와 재전송 장치 없이 수렴한다.
    public readonly struct RoomStateHeader
    {
        /// opcode(1) + kind(1) + phase(1) + host(1) + seeker(1) + outcome(1)
        /// + startTick(4) + playerCount(1)
        ///
        /// 15 였다. 배치 씨드 4바이트가 빠졌다 — 그 필드가 이 게임의 정보 규칙을 어기는
        /// 경로였다. 자세한 이유는 아래 생성자 주석에 있다.
        public const int WireSize = 11;

        /// 방장이나 Seeker 가 정해지지 않은 상태. 0 은 유효한 PlayerId 라 쓸 수 없다.
        public const byte NoPlayer = 0xFF;

        /// **배치 씨드는 더 이상 여기 없다.**
        ///
        /// 있었을 때는 그것이 이 기능의 핵심이었다 — 문·열쇠·장치의 위치를 모든 클라이언트가
        /// 이 씨드로 계산했고, 씨드가 다르면 플레이어마다 문이 다른 곳에 생겨 증상이 "남이
        /// 없는 문에 열쇠를 꽂는다" 로 나타났다.
        ///
        /// 그 방식이 동시에 이 게임의 정보 규칙을 어겼다. 룰셋은 문이 Runner 에게만 보여야
        /// 한다고 정하고 클라이언트는 컬링 레이어로 그것을 지키지만, 씨드를 받은 Seeker 의
        /// 프로세스에는 문의 좌표가 **계산 가능한 형태로** 들어 있었다. WebGL 빌드는
        /// 디컴파일되므로 카메라 마스크로 막을 수 있는 종류의 정보가 아니다.
        ///
        /// 이제 서버가 배치하고 좌표를 역할별로 걸러 내려보낸다(`EventKind.ObjectiveState`).
        /// 씨드가 와이어에 없으므로, 배치 함수를 가진 클라이언트도(ADR 0002) 문을 계산할
        /// **입력이 없다.** 막아야 하는 것은 코드가 아니라 입력이었다.
        public RoomStateHeader(
            RoomPhase phase,
            byte hostPlayerId,
            byte seekerPlayerId,
            byte outcome,
            uint startTick,
            byte playerCount)
        {
            Phase = phase;
            HostPlayerId = hostPlayerId;
            SeekerPlayerId = seekerPlayerId;
            Outcome = outcome;
            StartTick = startTick;
            PlayerCount = playerCount;
        }

        public RoomPhase Phase { get; }

        /// 시작 버튼을 누를 수 있는 플레이어. 클라이언트는 자기 id 와 비교해
        /// 자기가 방장인지 안다 — 별도의 "너는 방장이다" 신호를 두지 않는다.
        public byte HostPlayerId { get; }

        /// 이 매치의 Seeker. `Waiting` 단계에서는 `NoPlayer` 다.
        public byte SeekerPlayerId { get; }

        /// 매치 결과 코드. `Ended` 단계에서만 의미가 있다.
        public byte Outcome { get; }

        /// 매치가 시작된 룸 틱. 늦게 상태를 받은 클라이언트가 얼마나 지났는지 안다.
        public uint StartTick { get; }

        public byte PlayerCount { get; }
    }

    /// 명단의 한 줄.
    public readonly struct RoomPlayerEntry
    {
        /// playerId(1) + nameLength(1)
        public const int FixedWireSize = 2;

        public RoomPlayerEntry(byte playerId, string name)
        {
            PlayerId = playerId;
            Name = name;
        }

        public byte PlayerId { get; }

        /// 표시 이름. 계정이 없으므로 세션 수명만큼만 살고 중복도 사칭도 막지 않는다.
        /// 비어 있을 수 있다 — 그때 화면은 "플레이어 {PlayerId}" 로 대신한다.
        public string Name { get; }
    }
}
