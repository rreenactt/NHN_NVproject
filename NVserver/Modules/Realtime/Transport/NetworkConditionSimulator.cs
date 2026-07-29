using System;
using NV.Realtime.Contracts;
using NV.Shared.Simulation;

namespace NV.Realtime.Transport
{
    /// 개발용 네트워크 조건 주입기. 지연·지터·손실을 만든다.
    ///
    /// 지연은 틱 단위로 환산한다. 30Hz 에서 한 틱은 33.3ms 이므로
    /// 그보다 작은 지연은 표현되지 않는다. 예측·보정 검증에는 충분한 해상도다.
    ///
    /// 시뮬레이션 난수(DeterministicRandom)를 쓰지 않는다. 이 값은 시뮬레이션
    /// 결과에 들어가면 안 되고, 클라이언트가 같은 값을 재현할 필요도 없다.
    internal sealed class NetworkConditionSimulator
    {
        private readonly object _gate = new();
        private readonly int _baseDelayTicks;
        private readonly int _jitterTicks;
        private readonly uint _lossThreshold;

        private uint _state;

        public NetworkConditionSimulator(RealtimeOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            Enabled = options.NetworkConditionsEnabled;

            if (!Enabled)
            {
                return;
            }

            var tickMilliseconds = 1000.0 / SimConstants.TickRate;

            _baseDelayTicks = ToTicks(options.LatencyMilliseconds, tickMilliseconds);
            _jitterTicks = ToTicks(options.JitterMilliseconds, tickMilliseconds);

            var loss = options.PacketLoss;
            if (loss < 0.0)
            {
                loss = 0.0;
            }
            else if (loss > 1.0)
            {
                loss = 1.0;
            }

            _lossThreshold = (uint)(loss * uint.MaxValue);
            _state = options.RandomSeed == 0u ? 1u : options.RandomSeed;
        }

        public bool Enabled { get; }

        public int BaseDelayTicks => _baseDelayTicks;

        public int JitterTicks => _jitterTicks;

        public bool ShouldDrop()
        {
            if (!Enabled || _lossThreshold == 0u)
            {
                return false;
            }

            return Next() < _lossThreshold;
        }

        /// 이 패킷에 적용할 지연(틱). 꺼져 있으면 0.
        public uint DelayTicks()
        {
            if (!Enabled)
            {
                return 0u;
            }

            var delay = _baseDelayTicks;

            if (_jitterTicks > 0)
            {
                var span = (_jitterTicks * 2) + 1;
                delay += (int)(Next() % (uint)span) - _jitterTicks;
            }

            return delay <= 0 ? 0u : (uint)delay;
        }

        /// 절삭하지 않고 반올림한다. 30Hz 에서 한 틱은 33.3ms 이므로
        /// 절삭하면 30ms 지터가 0틱이 되어 설정이 조용히 무효가 된다.
        /// 반올림해도 한 틱의 절반 미만은 표현되지 않는다. 그 해상도가 필요하면
        /// 틱 기반이 아닌 실시간 큐로 바꿔야 한다.
        private static int ToTicks(int milliseconds, double tickMilliseconds)
        {
            if (milliseconds <= 0)
            {
                return 0;
            }

            return (int)Math.Round(milliseconds / tickMilliseconds, MidpointRounding.AwayFromZero);
        }

        /// xorshift32. 수신 스레드와 틱 루프가 함께 호출하므로 잠근다.
        private uint Next()
        {
            lock (_gate)
            {
                var value = _state;
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                _state = value;
                return value;
            }
        }
    }
}
