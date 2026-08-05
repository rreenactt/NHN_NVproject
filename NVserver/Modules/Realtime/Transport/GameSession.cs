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
        private readonly WebSocket _socket;
        private readonly Channel<byte[]> _outbound;

        public GameSession(int sessionId, byte playerId, string roomId, WebSocket socket)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            RoomId = roomId;
            _socket = socket;

            _outbound = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(RealtimeConstants.Sessions.OutboundCapacity)
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

        /// 닫힘 코드를 실어 소켓을 닫는다. 강제 퇴장에만 쓴다.
        ///
        /// **이 코드가 강제 퇴장의 유일한 신호다.** 그냥 소켓을 버리면 브라우저는 1006
        /// (비정상 종료)을 보고, 클라이언트는 회선 절단으로 읽어 자동 재시도가 방금 내보낸
        /// 사람을 다시 데려온다.
        ///
        /// 실패를 삼킨다. 이미 닫힌 소켓에 닫기를 보내면 예외가 나고, 그 시점에 할 일은
        /// 아무것도 없다 — 상대는 이미 떠났다.
        public async Task CloseWithReasonAsync(CancellationToken cancellationToken)
        {
            if (DisconnectReason != RealtimeConstants.Kick.Reason)
            {
                return;
            }

            if (_socket.State != WebSocketState.Open && _socket.State != WebSocketState.CloseReceived)
            {
                return;
            }

            try
            {
                await _socket
                    .CloseOutputAsync(
                        (WebSocketCloseStatus)RealtimeConstants.Kick.CloseCode,
                        RealtimeConstants.Kick.Reason,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException
                or ObjectDisposedException)
            {
            }
        }

        public async Task RunReceivePumpAsync(Room room, ILogger logger, CancellationToken cancellationToken)
        {
            var buffer = new byte[RealtimeConstants.Sessions.ReceiveBufferBytes];

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

            switch (MessageCodec.ReadOpcode(payload))
            {
                case MessageOpcode.Input:
                    DispatchInput(payload, room, logger);
                    break;

                case MessageOpcode.Control:
                    DispatchControl(payload, room, logger);
                    break;

                default:
                    // 서버 발신 opcode 나 미정의 값. 조작된 프레임일 수 있으므로 조용히 버린다.
                    break;
            }
        }

        /// 제어 요청을 룸 커맨드로 옮긴다.
        ///
        /// 자격은 여기서 보지 않는다. 방장인지, 지금 단계에서 가능한 전이인지는
        /// 룸이 틱 경계에서 판단한다 — 그 판단에 필요한 상태를 틱 루프만 소유하므로
        /// 여기서 미리 걸러 봐야 한 틱 뒤의 진실과 어긋날 수 있다.
        /// 제어 요청을 룸 커맨드로 옮긴다. 변환은 `ControlRouter` 가 한다.
        ///
        /// **여기에 두지 않는 이유는 시험할 수 없기 때문이다.** 이 클래스는 `WebSocket` 을 들고
        /// 있어 소켓 없이 만들 수 없고, 그래서 변환이 여기 있던 동안 테스트가 닿지 않았다 —
        /// 그 사이에 대기방 제어 넷이 조용히 버려지고 있었다.
        ///
        /// **거부를 `LogWarning` 으로 올린다.** `LogDebug` 였고, 그것이 넷이 죽은 것을 몇 번의
        /// 세션 동안 숨겼다. 정상 동작에서는 나오지 않는 줄이므로 시끄러워질 이유가 없다 —
        /// 나온다면 클라이언트와 서버의 버전이 어긋났다는 뜻이고, 그것은 알아야 하는 사실이다.
        private void DispatchControl(ReadOnlySpan<byte> payload, Room room, ILogger logger)
        {
            if (!ControlRouter.TryRoute(payload, SessionId, out var command, out var error))
            {
                logger.LogWarning("세션 {SessionId}: 제어 메시지를 버렸다. {Error}", SessionId, error);
                return;
            }

            room.PostCommand(command);
        }

        private void DispatchInput(ReadOnlySpan<byte> payload, Room room, ILogger logger)
        {
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
