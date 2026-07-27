using System;
using System.Numerics;

namespace NV.Shared.Simulation
{
    /// 결정적 해시. FNV-1a 32비트.
    ///
    /// 용도는 둘이다. 클라이언트와 서버의 시뮬레이션 결과 대조,
    /// 그리고 리컨실리에이션 재적용이 같은 상태를 만드는지 검증.
    ///
    /// float 은 비트 패턴을 해싱한다. -0.0 과 +0.0 은 비트가 다르므로 0 으로 정규화한다.
    /// 그러지 않으면 부호만 다른 0 이 다른 해시를 만들고, 원인 추적이 어렵다.
    public static class StateHash
    {
        public const uint Seed = 2166136261u;

        private const uint Prime = 16777619u;

        public static uint Combine(uint hash, uint value)
        {
            hash ^= value & 0xFFu;
            hash *= Prime;
            hash ^= (value >> 8) & 0xFFu;
            hash *= Prime;
            hash ^= (value >> 16) & 0xFFu;
            hash *= Prime;
            hash ^= (value >> 24) & 0xFFu;
            hash *= Prime;
            return hash;
        }

        public static uint Combine(uint hash, int value)
        {
            return Combine(hash, unchecked((uint)value));
        }

        public static uint Combine(uint hash, byte value)
        {
            hash ^= value;
            hash *= Prime;
            return hash;
        }

        public static uint Combine(uint hash, float value)
        {
            // 부호만 다른 0 을 같은 값으로 본다.
            if (value == 0f)
            {
                return Combine(hash, 0u);
            }

            return Combine(hash, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        }

        public static uint Combine(uint hash, Vector3 value)
        {
            hash = Combine(hash, value.X);
            hash = Combine(hash, value.Y);
            hash = Combine(hash, value.Z);
            return hash;
        }

        public static uint Combine(uint hash, string value)
        {
            if (value == null)
            {
                return Combine(hash, 0u);
            }

            for (var index = 0; index < value.Length; index++)
            {
                hash = Combine(hash, (uint)value[index]);
            }

            return hash;
        }

        public static uint Combine(uint hash, in PlayerState state)
        {
            hash = Combine(hash, state.Position);
            hash = Combine(hash, state.Velocity);
            hash = Combine(hash, state.Yaw);
            hash = Combine(hash, state.Pitch);
            hash = Combine(hash, (byte)state.Flags);
            hash = Combine(hash, state.Health);
            return hash;
        }

        public static uint Of(in PlayerState state)
        {
            return Combine(Seed, state);
        }
    }
}
