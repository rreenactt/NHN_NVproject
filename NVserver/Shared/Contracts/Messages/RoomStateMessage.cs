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
        /// + startTick(4) + placementSeed(4) + playerCount(1)
        public const int WireSize = 15;

        /// 방장이나 Seeker 가 정해지지 않은 상태. 0 은 유효한 PlayerId 라 쓸 수 없다.
        public const byte NoPlayer = 0xFF;

        public RoomStateHeader(
            RoomPhase phase,
            byte hostPlayerId,
            byte seekerPlayerId,
            byte outcome,
            uint startTick,
            int placementSeed,
            byte playerCount)
        {
            Phase = phase;
            HostPlayerId = hostPlayerId;
            SeekerPlayerId = seekerPlayerId;
            Outcome = outcome;
            StartTick = startTick;
            PlacementSeed = placementSeed;
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

        /// 목표물 배치 난수의 씨드.
        ///
        /// 이 값이 이 메시지에 있는 이유가 이 기능의 핵심이다. 문·열쇠·장치의 위치는
        /// 클라이언트가 이 씨드로 계산하므로, 씨드가 다르면 플레이어마다 문이 다른 곳에
        /// 생긴다. 증상은 "남이 없는 문에 열쇠를 꽂는다" 로 나타나 네트워크 문제로 보이지 않는다.
        public int PlacementSeed { get; }

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
