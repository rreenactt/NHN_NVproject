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
        private void DispatchControl(ReadOnlySpan<byte> payload, Room room, ILogger logger)
        {
            ControlMessage message;

            try
            {
                message = MessageCodec.ReadControl(payload);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                logger.LogDebug(exception, "세션 {SessionId}: 손상된 제어 메시지.", SessionId);
                return;
            }

            switch (message.Kind)
            {
                case ControlKind.StartMatch:
                    room.PostCommand(RoomCommand.Start(SessionId));
                    break;

                case ControlKind.EndMatch:
                    room.PostCommand(RoomCommand.EndMatch(SessionId, message.Value));
                    break;

                case ControlKind.ReturnToLobby:
                    room.PostCommand(RoomCommand.ReturnToLobby(SessionId));
                    break;

                case ControlKind.SetReady:
                    // 0 이 아니면 참으로 읽는다. 손상된 값을 거부하기보다 받아들이는 쪽이
                    // 안전하다 — 준비는 되돌릴 수 있고, 거부하면 화면이 눌린 채로 남는다.
                    room.PostCommand(RoomCommand.SetReady(SessionId, message.Value != 0));
                    break;

                case ControlKind.SetCharacter:
                    // 범위와 중복은 룸이 본다. 여기서 미리 거르면 한 틱 뒤의 진실과 어긋난다 —
                    // 그 사이에 다른 사람이 같은 캐릭터를 집을 수 있다.
                    room.PostCommand(RoomCommand.SetCharacter(SessionId, message.Value));
                    break;
            }
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
