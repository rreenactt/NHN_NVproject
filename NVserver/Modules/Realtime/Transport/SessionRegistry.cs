using System.Collections.Concurrent;
using System.Threading;

namespace NV.Realtime.Transport
{
    /// 세션 조회표. Kestrel 스레드와 틱 루프가 함께 읽는다.
    internal sealed class SessionRegistry
    {
        private readonly ConcurrentDictionary<int, GameSession> _sessions = new();
        private int _nextSessionId;

        public int AllocateSessionId()
        {
            return Interlocked.Increment(ref _nextSessionId);
        }

        public void Add(GameSession session)
        {
            _sessions[session.SessionId] = session;
        }

        public void Remove(int sessionId)
        {
            _sessions.TryRemove(sessionId, out _);
        }

        public bool TryGet(int sessionId, out GameSession? session)
        {
            return _sessions.TryGetValue(sessionId, out session);
        }
    }
}
