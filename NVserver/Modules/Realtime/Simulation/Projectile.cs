using System.Numerics;

namespace NV.Realtime.Simulation
{
    /// 날아가는 총알 하나. 룸이 배열로 들고 틱 루프만 만진다.
    ///
    /// **히트스캔이 아니라 실제로 날아간다**(`MatchConstants.BulletSpeed`). 클라이언트가 그렇게
    /// 만들어져 있고, 그것이 이 게임의 설계다 — 근거리에서도 피할 창이 있고 예광탄이 보인다.
    /// 서버가 히트스캔으로 판정하면 클라이언트가 그리는 궤적과 판정 시점이 어긋난다.
    ///
    /// 구조체다. 총알은 매치 중 수십 개가 나고 죽으므로 힙에 두면 그만큼의 쓰레기가 생기고,
    /// 틱 루프는 GC 를 부르지 않는 것이 원칙이다. 배열 슬롯을 재사용한다.
    internal struct Projectile
    {
        /// 쏜 사람. 자기 총알에 맞지 않게 하고(IG-014b), 누가 맞췄는지 판정할 때 쓴다.
        public byte OwnerId;

        /// 이번 틱 시작 지점.
        public Vector3 Position;

        /// 단위 방향. 발사 시점의 시선이고 이후 바뀌지 않는다 — 중력도 없다
        /// (`Bullet.bulletGravity` 기본값이 0 인 이유와 같다: 조준점에 맞아야 한다).
        public Vector3 Direction;

        /// 살아 있는 틱 수. `Match.BulletLifetimeTicks` 를 넘으면 사라진다.
        public int TicksLived;

        /// 이 슬롯이 쓰이는 중인가.
        public bool Active;
    }
}
