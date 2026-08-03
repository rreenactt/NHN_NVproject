namespace NV.Shared.Contracts.Enums
{
    /// 기획서 §5 의 장치 효과. 놓인 장치 하나가 정확히 하나를 갖는다.
    ///
    /// 여러 장치가 같은 타입을 가질 수 있다 — 기획서는 8~9개를 놓으라 하고 효과는 6종이므로,
    /// 남는 자리는 다회 사용 효과에 준다(`RealtimeConstants.Match.DeviceMix`).
    ///
    /// 이름이 `DeviceType` 이 아닌 이유는 충돌을 미리 피하기 위해서다. 클라이언트에 이미
    /// `NV.Game.DeviceType` 이 있고, `MatchPhase` 를 `Shared` 에 넣었을 때 같은 이름이
    /// 겹쳐 호출부를 수식해야 했다. 값은 그 열거형과 같으므로 전문을 받는 쪽에서 그대로 옮긴다.
    public enum MatchDeviceType : byte
    {
        /// §5.1 시간 증가 (1회). 남은 시간에 더한다.
        AddTime = 0,

        /// §5.1 전체 위치 공개 (다회).
        FullMapView = 1,

        /// §5.1 출혈 제거 (1회).
        StopBleeding = 2,

        /// §5.1 전체 정지 + 벽 투명화 (1회).
        FreezeAndXray = 3,

        /// §5.1 술래 시점 보기 (다회).
        SeekerCameraView = 4,

        /// §5.2 1:1 순간 이동 (다회, 쿨타임 12초).
        ///
        /// **기획서는 이것을 "술래 전용 장치" 로 적는다.** 룰셋과 현재 구현은 Runner 쪽으로
        /// 서술·구현되어 있어 어긋나며, 그 판단은 OQ-1 이 기다린다. 이 열거형은 효과의
        /// 종류만 정하고 누가 쓸 수 있는지는 정하지 않는다.
        Teleport = 5,
    }
}
