using System;

namespace NV.Shared.Contracts.Enums
{
    /// EntityState 의 flags 필드. 8비트를 넘기지 않는다.
    [Flags]
    public enum EntityFlags : byte
    {
        None = 0,
        Alive = 1 << 0,
        OnGround = 1 << 1,
        Crouching = 1 << 2,
    }
}
