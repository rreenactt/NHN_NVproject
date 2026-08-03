using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
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

            var roomId = context.Request.Query[ProtocolInfo.RoomQueryKey].ToString();

            if (!RoomRegistry.IsValidRoomId(roomId))
            {
                // 형식이 어긋난 코드는 없는 방과 구분해서 알려 준다. 오타와 만료를
                // 같은 응답으로 묶으면 화면에서 다시 갈라낼 방법이 없다.
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            // 없는 코드로는 방이 생기지 않는다. 예전에는 이 자리에서 룸이 만들어졌고,
            // 그것이 초대 코드 모델과 정면으로 어긋나는 지점이었다.
            if (!rooms.TryGet(roomId, out var room))
            {
                logger.LogInformation("없는 룸 {RoomId} 으로의 접속을 거부했다.", roomId);
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            // 진행 중 합류는 거부한다. 비대칭 매치 중간에 들어오면 역할도 목표물 배치도
            // 이미 정해져 있어 규칙이 성립하지 않는다.
            if (room.Phase != RoomPhase.Waiting)
            {
                logger.LogInformation("룸 {RoomId} 이 {Phase} 단계라 접속을 거부했다.", roomId, room.Phase);
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                return;
            }

            if (!room.TryReserveSlot(out var playerId))
            {
                logger.LogInformation("룸 {RoomId} 정원 초과로 거부.", roomId);
                context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                return;
            }

            // 방장 자격은 업그레이드 전에 확인한다. 이 뒤로는 토큰을 다시 보지 않으며,
            // 시작 권한은 "그 세션이 방장 세션인가" 로만 판정한다 — 매 요청에 토큰을
            // 요구하면 방장이 나갔을 때 남은 사람에게 줄 토큰이 없어 승계가 막힌다.
            var isHost = rooms.IsHostToken(roomId, context.Request.Query[ProtocolInfo.TokenQueryKey].ToString());
            var displayName = DisplayName.Sanitize(context.Request.Query[ProtocolInfo.NameQueryKey].ToString());

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

            logger.LogInformation(
                "세션 {SessionId} 입장. 룸 {RoomId}, 플레이어 {PlayerId}, 방장 {IsHost}",
                sessionId,
                roomId,
                playerId,
                isHost);

            try
            {
                SendWelcome(session, room);
                room.PostCommand(RoomCommand.Join(session.SessionId, session.PlayerId, displayName, isHost));

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
    }
}
