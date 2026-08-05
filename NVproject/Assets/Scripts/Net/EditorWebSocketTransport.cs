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
        private volatile bool _failed;
        private volatile string _failure;
        private volatile int _closeCode;
        private bool _disposed;

        public bool IsConnected => _connected;

        /// 접속이 끝내 성립하지 않았거나 도중에 끊겼다. UI 가 이 값으로 재시도를 제안한다.
        /// 예외를 조용히 먹으면 화면에는 "연결 중" 만 영원히 남는다.
        public bool HasError => _failed;

        public string Failure => _failure;

        /// 서버가 실은 닫힘 코드. 아직 닫히지 않았거나 코드가 없으면 0 이다.
        ///
        /// WebGL 쪽과 같은 표면을 만든다. 서버는 강제 퇴장을 4003 으로 알리고, 이것이
        /// 없으면 에디터에서는 강제 퇴장과 회선 절단을 구분할 수 없다 — 그러면 그 경로를
        /// WebGL 빌드로만 시험할 수 있게 되고, 그 빌드는 수 분이 걸린다.
        public int CloseCode => _closeCode;

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

            // 정상 종료 프레임을 먼저 보낸다. 곧바로 Dispose 하면 프레임이 나가지
            // 못하고, 서버 로그에서 자발적 퇴장과 회선 절단이 같은 모습이 된다.
            // 그 구분이 없으면 "왜 갑자기 튕기지" 를 로그로 추적할 수 없다.
            //
            // 기다리지 않는다 — Dispose 는 게임 루프 안에서 불린다. 대신 소켓 정리를
            // 그 작업 뒤로 미룬다. 여기서 바로 Dispose 하면 보내는 중인 프레임이 끊긴다.
            var socket = _socket;
            if (socket != null && socket.State == WebSocketState.Open)
            {
                socket
                    .CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "클라이언트 퇴장", CancellationToken.None)
                    .ContinueWith(_ => DisposeSocket(socket), TaskScheduler.Default);
            }
            else
            {
                DisposeSocket(socket);
            }

            _cancellation.Cancel();
            _cancellation.Dispose();
        }

        private static void DisposeSocket(WebSocket socket)
        {
            try
            {
                socket?.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
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
                        // 코드를 여기서 집는다. `finally` 까지 가면 소켓이 이미 Closed 로
                        // 넘어가 `CloseStatus` 가 남아 있기는 하지만, 읽는 자리를 하나로
                        // 두면 나중에 소켓을 즉시 버리도록 바뀌어도 값이 살아 있다.
                        _closeCode = (int)(result.CloseStatus ?? 0);
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
            catch (WebSocketException exception)
            {
                _failed = true;
                _failure = exception.Message;
            }
            catch (Exception exception)
            {
                // 잘못된 URL 은 UriFormatException 으로 온다. WebSocketException 만 잡으면
                // 그 경우가 조용히 사라진다.
                _failed = true;
                _failure = exception.Message;
            }
            finally
            {
                _connected = false;

                // 정상 종료도 UI 관점에서는 연결이 사라진 것이다. 서버가 정원 초과로
                // 끊었을 때 예외 없이 여기로 온다.
                if (!_failed && !cancellationToken.IsCancellationRequested)
                {
                    _failed = true;
                    _failure = _failure ?? "연결이 닫혔다.";
                }
            }
        }
    }
}
#endif
