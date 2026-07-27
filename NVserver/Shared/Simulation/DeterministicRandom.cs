namespace NV.Shared.Simulation
{
    /// 시뮬레이션이 쓰는 난수. 상태를 갖지 않는다.
    ///
    /// new Random() 이나 UnityEngine.Random 을 쓰면 리컨실리에이션 재적용에서
    /// 다른 값이 나온다. 같은 틱·같은 엔티티·같은 용도면 항상 같은 값이어야 한다.
    /// salt 로 용도를 구분한다.
    public static class DeterministicRandom
    {
        public static uint NextUInt(uint tick, uint entityId, uint salt)
        {
            var hash = StateHash.Seed;
            hash = StateHash.Combine(hash, tick);
            hash = StateHash.Combine(hash, entityId);
            hash = StateHash.Combine(hash, salt);

            // 하위 비트 편향을 줄이는 최종 혼합.
            hash ^= hash >> 16;
            hash *= 2246822507u;
            hash ^= hash >> 13;
            hash *= 3266489909u;
            hash ^= hash >> 16;

            return hash;
        }

        /// [0, 1). 상위 24비트만 써서 float 정밀도 안에 들어가게 한다.
        public static float NextUnitFloat(uint tick, uint entityId, uint salt)
        {
            return (NextUInt(tick, entityId, salt) >> 8) / 16777216f;
        }

        /// [-1, 1).
        public static float NextSignedFloat(uint tick, uint entityId, uint salt)
        {
            return (NextUnitFloat(tick, entityId, salt) * 2f) - 1f;
        }
    }
}
