#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using NV.Shared.Transport;

namespace NV.Client.Net
{
    /// 에디터·스탠드얼론용 전송. WebGL 빌드에는 들어가지 않는다.
    ///
    /// WebGL 빌드는 수 분이 걸려 매 수정마다 돌릴 수 없다. 에디터에서 반복하려면
    /// 브라우저 WebSocket 이 없는 환경의 구현이 필요하다.
    ///
    /// 두 구현이 갈리는 것 자체가 위험이다. WebGL 에서만 나는 버그를 만들 수 있다.
    /// 그래서 이 클래스는 IClientTransport 표면만 채우고 그 위에 로직을 두지 않는다.
    /// 예측·보간은 전송 구현을 몰라야 한다.
    public sealed class EditorWebSocketTransport : IClientTransport, IDisposable
    {
        private const int ScratchBytes = 512;

        private readonly ConcurrentQueue<byte[]> _inbound = new ConcurrentQueue<byte[]>();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

        private ClientWebSocket _socket;
        private Task _receiveLoop;
        private volatile bool _connected;
        private bool _disposed;

        public bool IsConnected => _connected;

        public int QueuedMessages => _inbound.Count;

        public void Connect(string url)
        {
            if (_socket != null)
            {
                return;
            }

            _socket = new ClientWebSocket();
            _receiveLoop = RunAsync(url, _cancellation.Token);
        }

        public bool TrySend(ReadOnlySpan<byte> payload, Reliability reliability)
        {
            if (!_connected || _socket == null || payload.Length > ScratchBytes)
            {
                return false;
            }

            var copy = payload.ToArray();

            // 송신 완료를 기다리지 않는다. 게임 루프를 막으면 안 된다.
            _ = _socket.SendAsync(
                new ArraySegment<byte>(copy),
                WebSocketMessageType.Binary,
                true,
                _cancellation.Token);

            return true;
        }

        public int Receive(Span<byte> destination)
        {
            if (!_inbound.TryDequeue(out var message))
            {
                return 0;
            }

            if (message.Length > destination.Length)
            {
                return 0;
            }

            message.CopyTo(destination);
            return message.Length;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _connected = false;
            _cancellation.Cancel();

            try
            {
                _socket?.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }

            _cancellation.Dispose();
        }

        private async Task RunAsync(string url, CancellationToken cancellationToken)
        {
            var buffer = new byte[ScratchBytes];

            try
            {
                await _socket.ConnectAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
                _connected = true;

                while (!cancellationToken.IsCancellationRequested
                    && _socket.State == WebSocketState.Open)
                {
                    var result = await _socket
                        .ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                        .ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (result.MessageType != WebSocketMessageType.Binary || !result.EndOfMessage)
                    {
                        continue;
                    }

                    var message = new byte[result.Count];
                    Array.Copy(buffer, message, result.Count);
                    _inbound.Enqueue(message);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException)
            {
            }
            finally
            {
                _connected = false;
            }
        }
    }
}
#endif
