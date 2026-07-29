using NV.Shared.Contracts.Enums;

namespace NV.Shared.Contracts.Messages
{
    /// 스냅샷에 실리는 엔티티 하나. 13B.
    /// 위치는 Quantization 으로 양자화된 고정소수점이며 미터가 아니다.
    public readonly struct EntityState
    {
        public const int WireSize = 13;

        public EntityState(
            byte id,
            short x,
            short y,
            short z,
            ushort yaw,
            short pitch,
            EntityFlags flags,
            byte health)
        {
            Id = id;
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
            Pitch = pitch;
            Flags = flags;
            Health = health;
        }

        public byte Id { get; }

        public short X { get; }

        public short Y { get; }

        public short Z { get; }

        public ushort Yaw { get; }

        public short Pitch { get; }

        public EntityFlags Flags { get; }

        public byte Health { get; }
    }
}
