using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;
using NV.Shared.Simulation;

namespace NV.Realtime.Transport
{
    /// 엔드포인트는 모듈이 소유한다. Api 에 컨트롤러를 두지 않는다.
    internal static class RealtimeEndpoints
    {
        public static void Map(IEndpointRouteBuilder endpoints)
        {
            endpoints.Map("/ws", HandleAsync);
        }

        private static async Task HandleAsync(
            HttpContext context,
            RoomRegistry rooms,
            SessionRegistry sessions,
            ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger("NV.Realtime.Transport");

            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            // 버전 검사는 업그레이드 전에 한다. 클라이언트와 서버는 다른 시점에 빌드되며
            // 이 핸드셰이크가 유일한 방어선이다.
            if (!TryReadVersion(context.Request.Query, out var version) || version != ProtocolInfo.Version)
            {
                logger.LogInformation("프로토콜 버전 불일치로 거부. 서버 {Server}, 클라이언트 {Client}", ProtocolInfo.Version, version);
                context.Response.StatusCode = (int)HttpStatusCode.UpgradeRequired;
                return;
            }

            var roomId = ReadRoomId(context.Request.Query);
            var room = rooms.GetOrCreate(roomId);
            if (room is null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            if (!room.TryReserveSlot(out var playerId))
            {
                logger.LogInformation("룸 {RoomId} 정원 초과로 거부.", roomId);
                context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                return;
            }

            System.Net.WebSockets.WebSocket socket;
            try
            {
                socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 업그레이드가 실패하면 퇴장 커맨드가 발생하지 않으므로 여기서 슬롯을 되돌린다.
                room.ReleaseSlot(playerId);
                throw;
            }

            var sessionId = sessions.AllocateSessionId();
            var session = new GameSession(sessionId, playerId, roomId, socket);
            sessions.Add(session);

            logger.LogInformation("세션 {SessionId} 입장. 룸 {RoomId}, 플레이어 {PlayerId}", sessionId, roomId, playerId);

            try
            {
                SendWelcome(session, room);
                room.PostCommand(RoomCommand.Join(session.SessionId, session.PlayerId));

                var send = session.RunSendPumpAsync(context.RequestAborted);
                var receive = session.RunReceivePumpAsync(room, logger, context.RequestAborted);

                await Task.WhenAny(send, receive).ConfigureAwait(false);

                // 한쪽이 끝나면 다른 쪽도 정리한다. 송신 채널을 닫으면 송신 펌프가 빠진다.
                session.CompleteOutbound();
                await Task.WhenAll(send, receive).ConfigureAwait(false);
            }
            finally
            {
                // 슬롯 반납은 틱 루프가 퇴장 커맨드를 처리할 때 일어난다.
                room.PostCommand(RoomCommand.Leave(session.SessionId, session.PlayerId));
                sessions.Remove(sessionId);
                socket.Dispose();

                logger.LogInformation("세션 {SessionId} 퇴장. 사유 {Reason}", sessionId, session.DisconnectReason ?? "정상 종료");
            }
        }

        private static void SendWelcome(GameSession session, Room room)
        {
            var payload = new byte[WelcomeMessage.WireSize];
            MessageCodec.WriteWelcome(
                payload,
                new WelcomeMessage(
                    ProtocolInfo.Version,
                    session.PlayerId,
                    room.Tick,
                    room.MapHash,
                    (byte)SimConstants.TickRate));

            session.TryEnqueue(payload);
        }

        private static bool TryReadVersion(IQueryCollection query, out ushort version)
        {
            version = 0;
            var raw = query[ProtocolInfo.VersionQueryKey].ToString();
            return ushort.TryParse(raw, out version);
        }

        private static string ReadRoomId(IQueryCollection query)
        {
            var raw = query[ProtocolInfo.RoomQueryKey].ToString();
            return string.IsNullOrEmpty(raw) ? RealtimeConstants.Rooms.DefaultRoomId : raw;
        }
    }
}
