namespace NV.Shared.Simulation
{
    /// 매치 규칙의 고정 파라미터. 이 값들이 유일한 출처다.
    ///
    /// 규칙의 출처는 기획서(`docs/asymmetric_tag_shooter_game_design.md`)와 룰셋
    /// (`NVproject/.claude/skills/game-rules/references/ruleset.md`)이다. 수치를 바꿀 때는
    /// 그쪽을 먼저 고치고 여기를 맞춘다 — 반대로 하면 문서가 코드를 따라가지 못한다.
    ///
    /// **`SimConstants` 와 나누는 기준은 "무엇을 계산하는가" 다.** 그쪽은 이동·충돌처럼
    /// 매 틱 돌아가는 시뮬레이션 파라미터이고, 여기는 매치 규칙이다. 둘을 한 파일에
    /// 두면 "이동 속도를 바꾸려다 매치 길이를 건드렸다" 가 가능해진다.
    ///
    /// **`RealtimeConstants.Match` 와 나누는 기준은 "클라이언트가 이 값으로 화면을 그리는가" 다.**
    /// 여기 있는 값은 클라이언트가 안다고 가정한다 — HUD 가 남은 시간과 탄약을 세고,
    /// 상호작용 프롬프트가 반경을 알아야 뜨고, 쿨다운 게이지가 길이를 알아야 찬다.
    /// WebGL 빌드는 디컴파일되지만 이 값들은 알려져도 무해하다. 판정은 서버가 다시 한다.
    ///
    /// 반대로 **판정에만 쓰이고 화면에 나오지 않는 값은 여기 두지 않는다** — 무적 창,
    /// 장치 파괴 탄수, 배치 간격이 그렇다. 그것들은 해당 판정이 서버로 옮겨갈 때
    /// `RealtimeConstants.Match` 로 간다.
    public static class MatchConstants
    {
        // ================================================================ 매치 진행

        /// 매치 길이(초). 기획서 §8 은 "시간 종료" 만 정하고 값을 주지 않으며,
        /// 룰셋의 기본값이 8:00 이다.
        public const float MatchDuration = 480f;

        /// 역할 공개 화면을 유지하는 시간(초). 이 동안 이동이 잠긴다.
        public const float RoleRevealDuration = 4f;

        /// 기획서 §3 — 이만큼의 Runner 가 탈출하면 Runner 승리.
        public const int EscapesToWin = 2;

        // ================================================================ 열쇠와 문

        /// 기획서 §3 — 문을 열려면 삽입해야 하는 열쇠 수.
        public const int KeysRequired = 10;

        /// 시작할 때 맵에 뿌리는 열쇠 수. `KeysRequired` 보다 적으면 Runner 가 이길 수
        /// 없으므로 배치 쪽에서 올려 잡는다.
        public const int KeysPlaced = 10;

        /// 한 Runner 가 들 수 있는 열쇠 수. 0 이하는 무제한이다.
        ///
        /// 무제한이 기본인 이유는 그것이 전술을 만들기 때문이다 — 한 명이 열쇠를 몰아
        /// 들면 그 사람이 죽었을 때 손실이 크다.
        public const int CarryLimit = 0;

        /// 열쇠를 집는 수평 거리(m).
        ///
        /// 수직 허용치는 여기 없다. 클라이언트가 1.6m 로 하드코딩하고 있고
        /// (`KeyPickup.Update`), 그 판정이 서버로 옮겨갈 때(IG-012) 함께 올라온다.
        /// 지금 값만 옮겨 적으면 같은 수가 두 곳에 있는 상태가 된다.
        public const float KeyPickupRadius = 1.4f;

        /// 열쇠 두 개를 연달아 넣는 사이의 간격(초).
        ///
        /// 열쇠 10개가 문 앞에서 눈에 보이는 시간이 되게 한다. 간격이 0 이면 한 번의
        /// 키 입력으로 전부 들어가고, Seeker 가 끼어들 창이 사라진다.
        public const float KeyInsertInterval = 0.6f;

        /// 문에서 열쇠를 넣고 탈출할 수 있는 거리(m).
        public const float DoorUseRadius = 2.2f;

        /// 열린 문간에 머물러야 탈출로 인정되는 시간(초).
        ///
        /// 즉시 탈출이 아닌 이유는 목표의 마지막 한 걸음을 Seeker 가 끊을 수 있는
        /// 순간으로 만들기 위해서다.
        ///
        /// 층 판정 허용치는 여기 없다 — `KeyPickupRadius` 와 같은 이유로 IG-012 에서
        /// 올라온다(현재 `MatchManager.TickEscapes` 가 2m 로 하드코딩한다).
        public const float EscapeHoldTime = 0.8f;

        // ================================================================ 전투

        /// 기획서 §4.1 — Runner 를 죽이는 피격 수. 1회는 출혈이다.
        public const int RunnerHitsToDie = 2;

        /// 기획서 §4.3 — 탄창.
        public const int SeekerMagazine = 3;

        // ================================================================ 체인 (기획서 §4.3)

        /// 제단에 끌려간 뒤 행동할 수 없는 시간(초).
        public const float ChainWait = 3f;

        /// 견인 시간의 하한(초). 제단이 이미 가까울 때 쓴다.
        public const float ChainDragTime = 0.45f;

        /// 견인 속도(m/s). 직선거리가 아니라 **걸어가는 경로 길이**로 잰다.
        public const float ChainDragSpeed = 45f;

        /// 견인 시간의 상한(초). 미로 반대편에서는 경로가 400m 가 되므로 상한이 필요하다.
        public const float ChainDragMaxTime = 3.5f;

        /// 체인이 놓아준 뒤 재장전에 걸리는 시간(초).
        public const float ChainReloadTime = 1.5f;

        // ================================================================ 장치 (기획서 §5)

        /// 기획서 §5 — 맵에 놓는 장치 수(8~9개).
        public const int DeviceCount = 9;

        /// 장치를 쓸 수 있는 거리(m).
        public const float DeviceUseRadius = 2.2f;

        /// 기획서 §5.2 — 순간이동 장치를 쓴 뒤의 전역 락아웃(초).
        public const float TeleportSharedCooldown = 12f;

        /// 다회 사용 장치가 다시 켜지기까지의 시간(초).
        ///
        /// 기획서에 없다. 이것이 없으면 한 패널을 연타해 영구적인 맵 핵으로 쓸 수 있다.
        public const float RepeatableDeviceCooldown = 8f;

        /// 기획서 §5.1 "시간 증가" 가 시계에 더하는 초. 남은 시간에 그대로 더한다.
        public const float DeviceTimeBonus = 60f;

        /// 기획서 §5.1 "전체 위치 공개" 가 유지되는 시간(초).
        public const float MapViewDuration = 6f;

        /// 기획서 §5.1 "술래 시점 보기" 가 유지되는 시간(초).
        public const float SeekerCamDuration = 6f;

        /// 기획서 §5.1 "전체 정지 + 벽 투명화" 가 유지되는 시간(초).
        public const float FreezeDuration = 5f;
    }
}
