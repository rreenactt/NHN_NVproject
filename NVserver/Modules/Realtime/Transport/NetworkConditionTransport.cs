using System;
using System.Collections.Generic;
using NV.Shared.Transport;

namespace NV.Realtime.Transport
{
    /// 송신 경로의 네트워크 조건 주입. 주입기가 꺼져 있으면 그대로 통과시킨다.
    ///
    /// 항상 이 데코레이터를 끼운다. 설정에 따라 타입이 달라지면 틱 루프가
    /// 어느 구현을 받았는지에 따라 분기해야 하고, 그 분기를 잊으면 지연이
    /// 조용히 무시된다. 꺼진 경로의 비용은 bool 검사 하나다.
    ///
    /// TrySend 와 Flush 는 틱 루프에서만 호출된다. 그래서 보류 목록에 잠금이 없다.
    internal sealed class NetworkConditionTransport : IServerTransport
    {
        private readonly IServerTransport _inner;
        private readonly NetworkConditionSimulator _simulator;
        private readonly List<PendingSend> _pending = new();

        private uint _currentTick;

        public NetworkConditionTransport(WebSocketServerTransport inner, NetworkConditionSimulator simulator)
        {
            _inner = inner;
            _simulator = simulator;
        }

        public int PendingCount => _pending.Count;

        /// 틱 루프가 룸을 돌기 전에 호출한다.
        public void BeginTick(uint tick)
        {
            _currentTick = tick;
        }

        /// 틱 루프가 룸을 다 돈 뒤 호출한다. 도착 시점이 된 패킷을 내보낸다.
        public void Flush()
        {
            if (_pending.Count == 0)
            {
                return;
            }

            for (var index = _pending.Count - 1; index >= 0; index--)
            {
                var send = _pending[index];
                if (send.ReleaseTick > _currentTick)
                {
                    continue;
                }

                _inner.TrySend(send.SessionId, send.Payload, send.Reliability);
                _pending.RemoveAt(index);
            }
        }

        public bool TrySend(int sessionId, ReadOnlySpan<byte> payload, Reliability reliability)
        {
            if (!_simulator.Enabled)
            {
                return _inner.TrySend(sessionId, payload, reliability);
            }

            // 신뢰 전송은 버리지 않는다. Welcome 이 사라지면 접속이 그대로 멈춘다.
            if (reliability == Reliability.Unreliable && _simulator.ShouldDrop())
            {
                return true;
            }

            var delay = _simulator.DelayTicks();
            if (delay == 0u)
            {
                return _inner.TrySend(sessionId, payload, reliability);
            }

            _pending.Add(new PendingSend(sessionId, _currentTick + delay, payload.ToArray(), reliability));
            return true;
        }

        public void Disconnect(int sessionId, string reason)
        {
            _inner.Disconnect(sessionId, reason);
        }

        private readonly struct PendingSend
        {
            public PendingSend(int sessionId, uint releaseTick, byte[] payload, Reliability reliability)
            {
                SessionId = sessionId;
                ReleaseTick = releaseTick;
                Payload = payload;
                Reliability = reliability;
            }

            public int SessionId { get; }

            public uint ReleaseTick { get; }

            public byte[] Payload { get; }

            public Reliability Reliability { get; }
        }
    }
}
