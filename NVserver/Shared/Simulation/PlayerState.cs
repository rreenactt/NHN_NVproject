using System.Numerics;
using NV.Shared.Contracts.Enums;

namespace NV.Shared.Simulation
{
    /// 시뮬레이션이 다루는 플레이어 상태. 와이어 포맷(EntityState)과 다르다.
    /// 와이어는 양자화된 정수, 여기는 원본 부동소수점이다.
    ///
    /// Position 은 발밑 기준이다. 충돌 박스는 여기서 위로 자란다.
    public struct PlayerState
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float Yaw;
        public float Pitch;
        public EntityFlags Flags;
        public byte Health;

        public static PlayerState Spawn(Vector3 position, float yaw, byte health)
        {
            var state = default(PlayerState);
            state.Position = position;
            state.Velocity = new Vector3(0f, 0f, 0f);
            state.Yaw = yaw;
            state.Pitch = 0f;
            state.Flags = EntityFlags.Alive;
            state.Health = health;
            return state;
        }

        public bool IsGrounded => (Flags & EntityFlags.OnGround) != 0;

        public bool IsCrouching => (Flags & EntityFlags.Crouching) != 0;

        public float Height => IsCrouching ? SimConstants.PlayerCrouchHeight : SimConstants.PlayerHeight;

        /// 충돌 박스의 중심. 발밑 위치에서 키의 절반만큼 위다.
        public Vector3 BoxCenter => new Vector3(Position.X, Position.Y + (Height * 0.5f), Position.Z);

        public Vector3 BoxHalfExtents =>
            new Vector3(SimConstants.PlayerRadius, Height * 0.5f, SimConstants.PlayerRadius);

        public Vector3 EyePosition =>
            new Vector3(Position.X, Position.Y + (Height * SimConstants.EyeHeightRatio), Position.Z);
    }
}
