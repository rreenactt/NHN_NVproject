using System;

namespace NV.Shared.Contracts.Enums
{
    /// InputFrame 의 buttons 필드. 8비트를 넘기지 않는다.
    [Flags]
    public enum ButtonFlags : byte
    {
        None = 0,
        Jump = 1 << 0,
        Fire = 1 << 1,
        Crouch = 1 << 2,
        Sprint = 1 << 3,

        /// 상호작용 — 열쇠 삽입(IG-012b2)과 장치 사용(IG-013).
        ///
        /// **누른 순간의 엣지다. 누르고 있는 상태가 아니다.** 클라이언트가 프레임에서 래치하고
        /// 틱마다 소비한다(`FirstPersonController.ConsumeInteract`) — 점프와 같은 이유이고,
        /// 30Hz 틱 사이에 눌린 키를 그냥 읽으면 절반쯤 사라진다.
        ///
        /// **무엇에 대한 상호작용인지는 실리지 않는다.** 대상을 클라이언트가 지정하면 "나는
        /// 저 문을 쓴다" 를 클라이언트가 주장하는 구조가 되고, 사거리 밖의 문도 지목할 수 있다.
        /// 서버가 자기 좌표로 대상을 고른다.
        Interact = 1 << 4,

        /// 정의된 비트 전부. 서버가 미정의 비트를 걸러내는 마스크다.
        /// 버튼을 추가하면 여기도 함께 고친다.
        All = Jump | Fire | Crouch | Sprint | Interact,
    }
}
