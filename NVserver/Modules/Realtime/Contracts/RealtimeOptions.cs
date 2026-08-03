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

        // `AllowRoomListing` 이 있었다. 목록이 **모든** 방을 내주던 시절, 통째로 막는 것
        // 말고 할 수 있는 일이 없어 둔 플래그다.
        //
        // 방마다 공개 여부를 정하게 되면서 근거가 사라졌다. 목록에 실리는 방은 만든
        // 사람이 실리기로 선택한 방뿐이고 — 노출이 사고가 아니라 동의다 — 플래그를 남기면
        // 공개를 선택한 방조차 뜨지 않아 그 선택이 무의미해진다.
        //
        // 상시 열린 경로가 된 자리는 `RateLimit:ListPerMinute` 가 대신한다.
    }
}
