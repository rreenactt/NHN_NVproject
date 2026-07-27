using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;

namespace NV.Realtime.Transport
{
    /// 한 접속의 송수신. 송신은 채널 하나와 펌프 하나로 직렬화한다.
    /// WebSocket 은 동시 SendAsync 를 허용하지 않는다. SendAsync 호출 지점은
    /// RunSendPumpAsync 한 곳뿐이어야 한다.
    internal sealed class GameSession
    {
        /// 밀리면 오래된 스냅샷을 버린다. 다음 틱이 대체하므로 유실이 문제되지 않는다.
        private const int OutboundCapacity = 32;

        /// 입력 메시지 최대 크기보다 크게 잡되, 이보다 큰 프레임은 끊는다.
        private const int ReceiveBufferBytes = 256;

        private readonly WebSocket _socket;
        private readonly Channel<byte[]> _outbound;

        public GameSession(int sessionId, byte playerId, string roomId, WebSocket socket)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            RoomId = roomId;
            _socket = socket;

            _outbound = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(OutboundCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
        }

        public int SessionId { get; }

        public byte PlayerId { get; }

        public string RoomId { get; }

        public string? DisconnectReason { get; private set; }

        public bool TryEnqueue(byte[] payload)
        {
            return _outbound.Writer.TryWrite(payload);
        }

        public void CompleteOutbound()
        {
            _outbound.Writer.TryComplete();
        }

        public void RequestDisconnect(string reason)
        {
            DisconnectReason = reason;
            CompleteOutbound();
        }

        public async Task RunSendPumpAsync(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var payload in _outbound.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (_socket.State != WebSocketState.Open)
                    {
                        break;
                    }

                    await _socket
                        .SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Binary, true, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException)
            {
            }
        }

        public async Task RunReceivePumpAsync(Room room, ILogger logger, CancellationToken cancellationToken)
        {
            var buffer = new byte[ReceiveBufferBytes];

            try
            {
                while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
                {
                    var result = await _socket
                        .ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                        .ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (result.MessageType != WebSocketMessageType.Binary)
                    {
                        continue;
                    }

                    if (!result.EndOfMessage)
                    {
                        logger.LogWarning("세션 {SessionId}: 수신 버퍼보다 큰 프레임을 받아 연결을 끊는다.", SessionId);
                        break;
                    }

                    Dispatch(buffer, result.Count, room, logger);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException exception)
            {
                logger.LogDebug(exception, "세션 {SessionId} 수신 종료.", SessionId);
            }
        }

        private void Dispatch(byte[] buffer, int length, Room room, ILogger logger)
        {
            var payload = new ReadOnlySpan<byte>(buffer, 0, length);

            if (MessageCodec.ReadOpcode(payload) != MessageOpcode.Input)
            {
                // 서버 발신 opcode 나 미정의 값. 조작된 프레임일 수 있으므로 조용히 버린다.
                return;
            }

            Span<InputFrame> frames = stackalloc InputFrame[ProtocolInfo.MaxInputFramesPerMessage];
            uint tick;
            int count;

            try
            {
                count = MessageCodec.ReadInput(payload, out tick, frames);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                logger.LogDebug(exception, "세션 {SessionId}: 손상된 입력 메시지.", SessionId);
                return;
            }

            // frames[0] 이 tick, 이후 프레임은 하나씩 과거다.
            for (var index = 0; index < count; index++)
            {
                if (tick < (uint)index)
                {
                    break;
                }

                room.PostInput(SessionId, tick - (uint)index, frames[index]);
            }
        }
    }
}
