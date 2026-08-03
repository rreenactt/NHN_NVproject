namespace NV.Realtime.Contracts
{
    /// 모듈 설정. Api 가 구성 파일에서 바인딩한다.
    ///
    /// 네트워크 조건 주입은 개발용이다. 프로덕션 설정에서는 켜지 않는다.
    /// 지연 없이 개발하면 예측·보정 결함이 배포 후에야 드러난다.
    public sealed class RealtimeOptions
    {
        /// 구성 파일의 절 이름.
        public const string SectionName = "Realtime";

        public bool NetworkConditionsEnabled { get; set; }

        /// 편도 지연(ms). 왕복은 이 값의 두 배가 된다.
        public int LatencyMilliseconds { get; set; }

        /// 지연에 더해지는 흔들림 폭(ms). 실제 지연은 지연 ± 이 값이다.
        public int JitterMilliseconds { get; set; }

        /// 0.0 ~ 1.0. 0.02 면 2% 손실.
        public double PacketLoss { get; set; }

        /// 재현 가능한 조건을 위한 시드. 시뮬레이션 난수와 무관하다.
        public uint RandomSeed { get; set; } = 0x5EED1234u;

        /// `GET /rooms` 로 열려 있는 룸 목록을 공개한다. 개발용이다.
        ///
        /// 초대 코드 모델에서 공개 목록은 기능이 아니라 결함이다 — 코드를 아는 사람만
        /// 들어올 수 있다는 전제가 목록 하나로 사라진다. 기본값이 꺼짐이어야 하고,
        /// 프로덕션 설정에서 켜는 일이 없어야 한다.
        public bool AllowRoomListing { get; set; }
    }
}
