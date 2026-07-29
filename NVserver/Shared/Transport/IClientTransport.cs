using System;

namespace NV.Shared.Transport
{
    /// 클라이언트측 전송.
    /// WebGL 은 싱글 스레드라 콜백·스레드 기반이 아닌 폴링 형태여야 한다.
    /// Task.Run, Thread, lock 을 쓸 수 없다.
    public interface IClientTransport
    {
        bool IsConnected { get; }

        bool TrySend(ReadOnlySpan<byte> payload, Reliability reliability);

        /// 수신 대기 중인 메시지 하나를 destination 에 복사하고 길이를 반환한다.
        /// 남은 메시지가 없으면 0 을 반환한다. 블로킹하지 않는다.
        int Receive(Span<byte> destination);
    }
}
