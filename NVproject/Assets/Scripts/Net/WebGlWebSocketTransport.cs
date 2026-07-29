#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using NV.Shared.Transport;

namespace NV.Client.Net
{
    /// 브라우저 WebSocket 을 IClientTransport 로 감싼다.
    ///
    /// WebGL 은 싱글 스레드다. Task.Run, Thread, lock, System.Net.Sockets 를
    /// 쓸 수 없다. 수신은 .jslib 의 큐를 게임 루프가 폴링해서 꺼낸다.
    public sealed class WebGlWebSocketTransport : IClientTransport, IDisposable
    {
        private const int StateClosed = 0;
        private const int StateConnecting = 1;
        private const int StateOpen = 2;
        private const int StateError = 3;

        /// 스냅샷 최대 114B. 여유를 두되 조작된 크기를 그대로 받지 않는다.
        private const int ScratchBytes = 512;

        private readonly byte[] _scratch = new byte[ScratchBytes];

        private bool _disposed;

        [DllImport("__Internal")]
        private static extern void NvWsOpen(string url);

        [DllImport("__Internal")]
        private static extern int NvWsState();

        [DllImport("__Internal")]
        private static extern int NvWsCloseCode();

        [DllImport("__Internal")]
        private static extern int NvWsSend(byte[] data, int length);

        [DllImport("__Internal")]
        private static extern int NvWsPeekSize();

        [DllImport("__Internal")]
        private static extern int NvWsReceive(byte[] destination, int capacity);

        [DllImport("__Internal")]
        private static extern void NvWsClose();

        public bool IsConnected => NvWsState() == StateOpen;

        public bool IsConnecting => NvWsState() == StateConnecting;

        public bool HasError => NvWsState() == StateError;

        public int CloseCode => NvWsCloseCode();

        /// url 은 wss:// 여야 한다. HTTPS 페이지에서 ws:// 는 차단된다.
        /// 프로토콜 버전과 룸은 쿼리스트링으로 넘긴다.
        /// 브라우저는 핸드셰이크에 커스텀 헤더를 붙일 수 없다.
        public void Connect(string url)
        {
            NvWsOpen(url);
        }

        public bool TrySend(ReadOnlySpan<byte> payload, Reliability reliability)
        {
            if (payload.Length > ScratchBytes)
            {
                return false;
            }

            if (NvWsState() != StateOpen)
            {
                return false;
            }

            // DllImport 경계로 Span 을 넘길 수 없다. 재사용 버퍼를 거친다.
            payload.CopyTo(_scratch);
            return NvWsSend(_scratch, payload.Length) == 1;
        }

        public int Receive(Span<byte> destination)
        {
            var size = NvWsPeekSize();
            if (size == 0)
            {
                return 0;
            }

            if (size > destination.Length || size > ScratchBytes)
            {
                // 담을 수 없는 메시지는 버린다. 반환값 -1 이 그 뜻이다.
                NvWsReceive(_scratch, 0);
                return 0;
            }

            var read = NvWsReceive(_scratch, ScratchBytes);
            if (read <= 0)
            {
                return 0;
            }

            new ReadOnlySpan<byte>(_scratch, 0, read).CopyTo(destination);
            return read;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            NvWsClose();
        }
    }
}
#endif
