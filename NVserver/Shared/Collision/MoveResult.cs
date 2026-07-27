using System.Numerics;

namespace NV.Shared.Collision
{
    /// 충돌 해소 후의 이동 결과. 속도도 접촉면에 투사된 값으로 돌려준다.
    /// 호출자가 속도를 따로 처리하면 클라이언트와 서버에서 처리 순서가 갈릴 수 있다.
    public readonly struct MoveResult
    {
        public MoveResult(Vector3 center, Vector3 velocity, Vector3 lastNormal, bool hit, bool grounded)
        {
            Center = center;
            Velocity = velocity;
            LastNormal = lastNormal;
            Hit = hit;
            Grounded = grounded;
        }

        public Vector3 Center { get; }

        public Vector3 Velocity { get; }

        public Vector3 LastNormal { get; }

        public bool Hit { get; }

        /// 이동 중 바닥으로 인정되는 법선에 닿았는지. 착지 판정은 별도 탐침이 담당한다.
        public bool Grounded { get; }
    }
}
