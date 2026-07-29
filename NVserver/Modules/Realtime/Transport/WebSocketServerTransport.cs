using System;
using NV.Shared.Transport;

namespace NV.Realtime.Transport
{
    /// 틱 루프가 직접 호출한다. 안에서 await 하지 않는다.
    /// 실제 전송은 세션별 송신 펌프가 수행한다.
    internal sealed class WebSocketServerTransport : IServerTransport
    {
        private readonly SessionRegistry _sessions;

        public WebSocketServerTransport(SessionRegistry sessions)
        {
            _sessions = sessions;
        }

        public bool TrySend(int sessionId, ReadOnlySpan<byte> payload, Reliability reliability)
        {
            // WebSocket 은 전부 신뢰·순서 보장이라 reliability 로 경로가 갈리지 않는다.
            // 전송 계층을 교체할 때 이 분기가 생긴다.
            if (!_sessions.TryGet(sessionId, out var session) || session is null)
            {
                return false;
            }

            // 채널이 byte[] 를 받으므로 여기서 복사가 발생한다.
            // 8인 기준 틱당 8 * 114B 이며, 부하 테스트에서 문제가 되면 풀링으로 바꾼다.
            return session.TryEnqueue(payload.ToArray());
        }

        public void Disconnect(int sessionId, string reason)
        {
            if (_sessions.TryGet(sessionId, out var session) && session is not null)
            {
                session.RequestDisconnect(reason);
            }
        }
    }
}
