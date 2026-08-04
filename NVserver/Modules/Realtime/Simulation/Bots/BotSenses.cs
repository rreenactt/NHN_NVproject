using NV.Shared.Collision;
using NV.Shared.Contracts.Messages;
using NV.Shared.Simulation;

namespace NV.Realtime.Simulation.Bots
{
    /// 봇이 이번 틱에 볼 수 있는 것. 룸이 채워 넘긴다.
    ///
    /// **읽기 전용 묶음이고 `PlayerEntity` 를 담지 않는다.** 두뇌가 몸을 쥘 수 있으면
    /// 언젠가 위치를 직접 옮기고, 그 순간 봇은 서버 판정을 우회하는 존재가 된다.
    /// 두뇌가 돌려줄 수 있는 것은 `InputFrame` 하나뿐이어야 한다.
    internal readonly struct BotSenses
    {
        public BotSenses(in PlayerState state, in InputFrame lastInput, WorldMap map)
        {
            State = state;
            LastInput = lastInput;
            Map = map;
        }

        public PlayerState State { get; }

        /// 직전에 만든 프레임. 시선을 이어 가는 기준이다.
        public InputFrame LastInput { get; }

        /// 이 룸의 지형. 격자가 없는 맵도 있으므로 `HasGrid` 를 먼저 본다.
        public WorldMap Map { get; }
    }
}
