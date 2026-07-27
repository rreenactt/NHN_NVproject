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

        /// 정의된 비트 전부. 서버가 미정의 비트를 걸러내는 마스크다.
        /// 버튼을 추가하면 여기도 함께 고친다.
        All = Jump | Fire | Crouch | Sprint,
    }
}
