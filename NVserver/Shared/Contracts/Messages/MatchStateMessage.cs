using NV.Shared.Contracts.Enums;

namespace NV.Shared.Contracts.Messages
{
    /// 매치 상태 전문의 고정부. 가변부는 참가자 항목이 뒤따른다.
    ///
    /// **`RoomState` 와 같은 성격이다 — 알림이 아니라 전문이다.** 2Hz 로 계속 보내고,
    /// 상태가 바뀐 틱에는 즉시 보낸다. 한 번짜리로 만들면 세션의 송신 채널이
    /// `Bounded(32, DropOldest)` 라 버려질 수 있고, "매치가 시작됐다" 를 놓친
    /// 클라이언트는 영구히 옛 화면에 남는다. 멱등한 전문을 반복하면 ack 와 재전송
    /// 장치 없이 수렴한다.
    ///
    /// **`RoomState` 와 다른 점은 본문이 수신자에 따라 달라지는 것이다.** 룰셋은 Seeker
    /// 에게 열쇠 진행도를 알리지 않으므로, 이 전문은 스냅샷처럼 **세션별로 인코딩**해야
    /// 한다. 클라이언트에서 숨기는 방식은 쓸 수 없다 — WebGL 빌드는 디컴파일된다.
    ///
    /// 아직 서버가 세지 않는 값이 있다. 열쇠·탈출·피격은 그 판정이 서버로 오는
    /// 태스크(IG-012·IG-014)에서 채워지고, 그때 **와이어 포맷은 바뀌지 않는다** —
    /// 자리를 미리 잡아 두는 것이 프로토콜 버전을 한 번만 올리는 방법이다.
    public readonly struct MatchStateHeader
    {
        /// opcode(1) + kind(1) + phase(1) + timeRemaining(2) + keysInserted(1)
        /// + escapes(1) + outcome(1) + participantCount(1)
        public const int WireSize = 9;

        public MatchStateHeader(
            MatchPhase phase,
            ushort timeRemainingTenths,
            byte keysInserted,
            byte escapes,
            byte outcome,
            byte participantCount)
        {
            Phase = phase;
            TimeRemainingTenths = timeRemainingTenths;
            KeysInserted = keysInserted;
            Escapes = escapes;
            Outcome = outcome;
            ParticipantCount = participantCount;
        }

        public MatchPhase Phase { get; }

        /// 남은 시간, 0.1초 단위.
        ///
        /// u16 이면 6553.5초까지 실린다. 매치 길이 480초는 4800 이고, 장치가 시간을
        /// 더해도(기획서 §5.1) 한참 남는다. 0.1초 해상도인 이유는 전문이 2Hz 라
        /// 그보다 잘게 보낼 의미가 없기 때문이다 — 그 사이는 클라이언트가 자기 시계로
        /// 메운다.
        public ushort TimeRemainingTenths { get; }

        /// 문에 들어간 열쇠 수. **Seeker 사본에서는 0 이다.**
        public byte KeysInserted { get; }

        /// 탈출한 Runner 수. 이것은 Seeker 도 알아야 한다 — 자기가 막아야 하는 수다.
        public byte Escapes { get; }

        /// 매치 결과 코드. `Ended` 에서만 의미가 있다.
        ///
        /// 서버는 아직 이 값을 정하지 않는다. 기획서 §8 과 구현이 어긋나는 지점이 남아
        /// 있어(전멸 승리 유무, 2인 매치에서 Runner 승리 불가) 승패 판정을 미뤘다.
        public byte Outcome { get; }

        public byte ParticipantCount { get; }

        /// 초 → 0.1초 단위. 상한을 넘으면 자른다 — 넘길 값이 오면 시계가 아니라
        /// 계산이 잘못된 것이므로, 감싸 올려 6553초를 0 으로 만드는 것보다 낫다.
        public static ushort ToTenths(float seconds)
        {
            if (seconds <= 0f)
            {
                return 0;
            }

            var tenths = seconds * 10f;
            return tenths >= ushort.MaxValue ? ushort.MaxValue : (ushort)tenths;
        }

        public static float FromTenths(ushort tenths)
        {
            return tenths * 0.1f;
        }
    }

    /// 참가자 한 명의 매치 상태.
    ///
    /// 출혈과 역할은 여기 **말고** 스냅샷의 `EntityFlags` 에도 있다(IG-009). 중복이
    /// 아니라 주기가 다르다 — 원격 몸의 표현(피 흔적, 무기 유무)은 매 틱 필요하고,
    /// 이 전문은 2Hz 다. 2Hz 로 출혈을 받으면 피 흔적이 최대 0.5초 늦게 시작한다.
    public readonly struct MatchParticipant
    {
        /// playerId(1) + role(1) + flags(1) + hits(1) + carriedKeys(1)
        public const int WireSize = 5;

        public MatchParticipant(
            byte playerId,
            MatchRole role,
            byte flags,
            byte hits,
            byte carriedKeys)
        {
            PlayerId = playerId;
            Role = role;
            Flags = flags;
            Hits = hits;
            CarriedKeys = carriedKeys;
        }

        public byte PlayerId { get; }

        public MatchRole Role { get; }

        /// 매치 상태 비트. 살아 있음·탈출함 같은 것이 여기 온다.
        ///
        /// 아직 열거형을 만들지 않았다. 채울 값이 생기는 것은 탈락과 탈출이 서버 판정이
        /// 되는 태스크(IG-012)이고, 비어 있는 열거형을 미리 두면 어디까지 왔는지 읽어서
        /// 알 수 없다. 지금은 항상 0 이 나간다.
        public byte Flags { get; }

        /// 받은 피격 수. 기획서 §4.1 — 2회면 사망이다.
        public byte Hits { get; }

        /// 들고 있는 열쇠 수. **Seeker 사본에서는 0 이다.**
        public byte CarriedKeys { get; }
    }
}
