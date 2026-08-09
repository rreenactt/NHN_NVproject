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
        //
        // **클라이언트의 설계값이다.** 걷기 2.5, 달리기 5.0. 이 두 값은 취향이 아니라
        // 다른 튜닝의 기준점이다 — 발소리 가청 거리(1.6m 에서 100%, 18m 에서 0), 살금
        // 걸음 1.1, 3m 격자의 미로 폭, 걸음 애니메이션의 보폭이 전부 여기에 맞춰져 있다.
        //
        // 6.5/9.4 였고, 그것이 오프라인 씬(2.5/5.0)과 어긋나 있었다. 증상은 로비를 지나
        // 매치에 들어가는 순간 캐릭터가 2.6배 빨라지는 것 — 같은 맵이 갑자기 좁아지고
        // 발소리 거리가 무의미해진다.
        public const float MoveSpeed = 2.5f;
        public const float SprintMultiplier = 2f;

        /// 술래가 달릴 때의 배수. **사람이 달리는 속도의 1.6배**이고, 그래서 여기 적힌 값은
        /// `SprintMultiplier * 1.6` 이다 — 곱을 풀어 쓰면 사람의 속도를 바꿀 때 한쪽만 바뀐다.
        ///
        /// 술래는 이 속도를 계속 쓰지 못한다. 게이지가 있고 가득 차도 4초다
        /// (`MatchConstants.SeekerSprintSeconds`) — 쫓는 쪽이 항상 빠르면 도망이 성립하지
        /// 않고, 한 번도 빠르지 않으면 잡히는 순간이 오지 않는다.
        public const float SeekerSprintMultiplier = SprintMultiplier * 1.6f;
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

        /// 스폰 시 체력. **이 게임은 체력을 깎지 않는다** — 피격 수로 판정한다
        /// (`MatchConstants.RunnerHitsToDie`). `PlayerState` 가 그 필드를 들고 있고
        /// `StateHash` 가 그것을 해시하므로 값이 있어야 하며, 언제나 이 값이다.
        public const byte SpawnHealth = 100;

        /// 걸어서 올라갈 수 있는 턱의 높이(m).
        ///
        /// **없으면 계단을 올라갈 수 없다.** 박스 스윕은 수직면을 만나면 그 평면으로
        /// 미끄러질 뿐이라, 계단의 챌면(backrooms 는 3.2m/16단 = 0.2m)이 벽과 똑같이
        /// 몸을 세운다. 증상은 "계단에서 한 칸씩 점프해야 올라가진다" 이고, 서버가
        /// 위치를 정하므로 클라이언트에서는 **올라가려다 끌려 내려오는** 것으로 보인다.
        ///
        /// 0.3 은 임의의 값이 아니라 클라이언트 `CharacterController` 의
        /// `m_StepOffset` 이다. 오프라인 플레이는 그쪽이 몸을 움직이므로, 두 값이
        /// 다르면 같은 턱이 오프라인에서는 올라가지고 네트워크에서는 안 올라간다.
        /// 계단 한 단(0.2m)보다 크고 제단 기단(0.5m)보다 작다 — 올라가야 할 것과
        /// 올라가면 안 되는 것 사이에 있다.
        public const float StepHeight = 0.3f;

        /// 겹침 해소 반복 상한.
        public const int MaxDepenetrationIterations = 4;
    }
}
