namespace NV.Shared.Simulation
{
    /// 시뮬레이션 고정 파라미터. 이 값들이 유일한 출처다.
    /// Time.deltaTime 이나 실제 경과 시간을 쓰지 않는다. 재적용 결과가 달라진다.
    ///
    /// 여기 있는 값은 클라이언트가 안다고 가정한다. WebGL 빌드는 디컴파일된다.
    /// 값 공유와 검증 위임은 다르다. 위반 판정은 모듈에서 다시 한다.
    ///
    /// 서버만 정하는 판정과 용량(룸 정원, 입력 상한, 버퍼 크기)은 여기가 아니라
    /// `Modules/Realtime/RealtimeConstants.cs` 에 모아 둔다. 클라이언트가 같은 값으로
    /// 계산해야 하는지가 두 파일을 가르는 기준이다.
    public static class SimConstants
    {
        public const int TickRate = 30;

        /// 한 틱의 고정 델타(초). 33.3ms.
        public const float TickDelta = 1f / TickRate;

        public const double TickIntervalSeconds = 1.0 / TickRate;

        // 플레이어 충돌 박스. 위치는 발밑 기준이다.
        public const float PlayerRadius = 0.4f;
        public const float PlayerHeight = 1.8f;
        public const float PlayerCrouchHeight = 1.2f;
        public const float EyeHeightRatio = 0.9f;

        // 이동
        public const float MoveSpeed = 6.5f;
        public const float SprintMultiplier = 1.45f;
        public const float CrouchMultiplier = 0.5f;
        public const float GroundAcceleration = 60f;
        public const float AirAcceleration = 12f;

        // 수직
        public const float Gravity = 20f;
        public const float JumpSpeed = 7f;
        public const float TerminalVelocity = 60f;

        // 충돌 해소
        /// 이 값 이상의 법선 Y 를 바닥으로 인정한다. 약 45도.
        public const float GroundNormalY = 0.7f;

        /// 접촉면에서 이만큼 띄워 다음 틱에 다시 파묻히는 것을 막는다.
        public const float SkinWidth = 0.001f;

        /// 미끄러짐 반복 상한. 모서리에서 무한 반복을 막는다.
        public const int MaxSlideIterations = 4;

        /// 착지 판정용 하향 탐침 거리.
        public const float GroundProbeDistance = 0.05f;

        /// 겹침 해소 반복 상한.
        public const int MaxDepenetrationIterations = 4;
    }
}
