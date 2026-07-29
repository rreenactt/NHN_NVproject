using System;

namespace NV.Shared.Transport
{
    /// 서버측 전송. 구현은 서버가, IClientTransport 구현은 클라이언트가 갖는다.
    /// 이 인터페이스의 구현은 논블로킹이어야 한다. 틱 루프가 직접 호출하므로
    /// 안에서 await 하면 모든 룸이 함께 멈춘다.
    public interface IServerTransport
    {
        /// 큐가 가득 차 폐기됐으면 false. 스냅샷은 폐기해도 다음 틱이 대체한다.
        bool TrySend(int sessionId, ReadOnlySpan<byte> payload, Reliability reliability);

        void Disconnect(int sessionId, string reason);
    }
}
