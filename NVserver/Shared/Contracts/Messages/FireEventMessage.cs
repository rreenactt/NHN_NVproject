namespace NV.Shared.Contracts.Messages
{
    /// 총알 한 발이 발사됐다는 알림. 근거는 ADR 0003.
    ///
    /// **발사체의 상태가 아니라 발사의 초기 조건을 싣는다.** 클라이언트가 그것으로 비행을
    /// 재현한다 — 총알은 등속 직선이고 중력이 0 이므로(`MatchConstants.BulletSpeed`,
    /// `Bullet.bulletGravity` 기본값 0) 재현이 정확하다. 상태를 매 틱 싣는 것보다 훨씬 싸고,
    /// 스냅샷에 총알을 넣으면 id 공간을 나눠야 한다.
    ///
    /// **방향을 벡터가 아니라 요·피치로 싣는다.** 수신 측이 `PlayerMovement.Forward` 로 같은
    /// 벡터를 만들므로 요 규약이 한 곳에만 남고(전방 = `(sin, 0, cos)`), 6바이트가 4바이트가 된다.
    /// 그 규약을 두 곳에 두면 한쪽을 고칠 때 다른 쪽이 남아 예광탄이 총알과 다른 데로 날아간다.
    public readonly struct FireEventMessage
    {
        /// opcode(1) + kind(1) + shooterId(1) + x·y·z(2×3) + yaw(2) + pitch(2) + tick(4)
        public const int WireSize = 17;

        public FireEventMessage(
            byte shooterId,
            short x,
            short y,
            short z,
            ushort yaw,
            short pitch,
            uint tick)
        {
            ShooterId = shooterId;
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
            Pitch = pitch;
            Tick = tick;
        }

        /// 쏜 사람. 클라이언트가 자기 발사와 남의 발사를 구별하는 데 쓴다 — 자기 것은 이미
        /// 로컬에서 그렸으므로 두 번 그리지 않는다.
        public byte ShooterId { get; }

        /// 총알이 출발한 지점(양자화). **사수의 눈높이이고 보간된 위치가 아니다.**
        ///
        /// 클라이언트도 사수의 위치를 알지만 그것은 보간 버퍼의 값이라 100ms 과거다. 예광탄은
        /// 총알이 실제로 출발한 곳에서 나가야 한다.
        public short X { get; }

        public short Y { get; }

        public short Z { get; }

        public ushort Yaw { get; }

        public short Pitch { get; }

        /// 발사된 서버 틱.
        ///
        /// 이벤트는 한 RTT 늦게 도착하므로 클라이언트는 그 사이 총알이 간 거리를 건너뛰고
        /// 그려야 한다. 틱이 없으면 예광탄이 항상 총구에서 시작해 실제 탄도보다 뒤처진다.
        public uint Tick { get; }
    }
}
