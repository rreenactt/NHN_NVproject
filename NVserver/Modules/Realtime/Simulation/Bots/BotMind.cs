using System.Numerics;
using NV.Shared.Collision;
using NV.Shared.Simulation;

namespace NV.Realtime.Simulation.Bots
{
    /// 봇 하나가 틱 사이에 들고 가는 것. 목표점과 자기 난수다.
    ///
    /// `PlayerEntity` 에 두지 않는다. 그러면 "사람인데 목표점이 있다" 가 표현 가능한
    /// 상태가 되고, 룸은 이미 봇을 세션 id 의 부호로 가르고 있다. 마음은 봇이 명단에
    /// 들어올 때 생기고 나갈 때 사라진다 — 그 두 자리가 룸에 각각 한 곳뿐이다.
    ///
    /// **난수를 봇마다 따로 둔다.** 하나를 공유하면 여러 봇이 뽑는 순서에 서로 끼어들어,
    /// 봇 수가 바뀌면 같은 씨드로도 다른 궤적이 나온다. 배치·피격 수열과도 분리되어
    /// 있어(룸이 씨드를 흩어 준다) 봇이 한 번 더 뽑는 변경이 열쇠 배치를 바꾸지 않는다.
    internal sealed class BotMind
    {
        private DeterministicSequence _sequence;

        /// 직전 틱의 발밑. 나아가고 있는지를 이 값과 비교해서 안다.
        private Vector3 _lastFeet;

        private int _stuckTicks;

        public BotMind(int seed)
        {
            _sequence = new DeterministicSequence(seed);
        }

        /// 걸어가는 목표점. `HasGoal` 이 참일 때만 의미가 있다.
        public Vector3 Goal { get; private set; }

        public bool HasGoal { get; private set; }

        /// 새 목표점을 뽑는다. 격자에 후보가 없으면 false.
        ///
        /// 셀 중심이 나온다(`MapGrid` 의 규약). 도착 판정 반경이 셀 크기보다 작아도
        /// 되는 이유는 그 중심까지 실제로 걸어가기 때문이다 — 벽에 막히면 도착하지
        /// 못하고, 그 경우는 `_stuckTicks` 가 답한다.
        public bool TryRetarget(MapGrid grid)
        {
            if (!grid.TryRandomFreeFloor(ref _sequence, out var goal))
            {
                return false;
            }

            Goal = goal;
            HasGoal = true;
            _stuckTicks = 0;

            return true;
        }

        /// 목표를 다시 뽑아야 하는가. **매 틱 한 번만 부른다** — 나아간 거리를 여기서 잰다.
        ///
        /// 셋 중 하나면 참이다. 목표가 없거나, 도착했거나, 나아가지 못하고 있거나.
        ///
        /// 세 번째가 경로 탐색을 대신한다. 봇은 목표를 향해 직선으로 걸으므로 미로에서는
        /// 벽에 붙어 멈춘다. 그때 다른 목표를 뽑으면 결국 돌아서 나아가고, 이것이 A\* 없이
        /// 배회가 성립하는 이유다 — 효율은 나쁘지만 검증하려는 것은 봇의 영리함이 아니라
        /// 서버 판정이 도는지다.
        public bool NeedsNewGoal(Vector3 feet)
        {
            if (!HasGoal)
            {
                return true;
            }

            var moved = HorizontalDistanceSquared(feet, _lastFeet);
            _lastFeet = feet;

            if (moved < RealtimeConstants.Bots.MinStepSquared)
            {
                _stuckTicks++;
            }
            else
            {
                _stuckTicks = 0;
            }

            if (_stuckTicks >= RealtimeConstants.Bots.StuckTicks)
            {
                return true;
            }

            return HorizontalDistanceSquared(feet, Goal)
                <= RealtimeConstants.Bots.GoalReachRadius * RealtimeConstants.Bots.GoalReachRadius;
        }

        /// 수평 거리만 잰다. 층이 다른 목표는 계단으로 이어져야 도달하고, 그 판단은
        /// 격자 경로 탐색이 들어올 때의 일이다 — 지금은 도달하지 못하면 다시 뽑는다.
        private static float HorizontalDistanceSquared(Vector3 a, Vector3 b)
        {
            var dx = a.X - b.X;
            var dz = a.Z - b.Z;

            return (dx * dx) + (dz * dz);
        }
    }
}
