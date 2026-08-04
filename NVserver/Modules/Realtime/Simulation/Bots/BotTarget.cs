using System.Numerics;

namespace NV.Realtime.Simulation.Bots
{
    /// 봇이 보는 다른 몸 하나. **`PlayerEntity` 를 넘기지 않기 위한 것이다.**
    ///
    /// 값만 담는다. 두뇌가 산 몸을 쥐면 위치를 직접 옮길 수 있고, 그 순간 봇이 서버
    /// 판정을 우회하는 존재가 된다 — 그것을 막는 것이 `BotSenses` 전체의 목적이다.
    internal readonly struct BotTarget
    {
        public BotTarget(byte playerId, Vector3 feet, bool isRunner, bool isActive)
        {
            PlayerId = playerId;
            Feet = feet;
            IsRunner = isRunner;
            IsActive = isActive;
        }

        public byte PlayerId { get; }

        public Vector3 Feet { get; }

        public bool IsRunner { get; }

        /// 아직 판정에 들어가는 몸인가. 쓰러졌거나 빠져나간 몸은 거짓이다 —
        /// 그쪽을 쫓으면 술래가 시체 앞에서 탄창을 비운다.
        public bool IsActive { get; }
    }
}
