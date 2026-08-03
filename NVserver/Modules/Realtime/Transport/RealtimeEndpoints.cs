using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using NV.Infrastructure.Json;
using NV.Realtime.Contracts;
using NV.Realtime.Simulation;
using NV.Shared.Contracts;
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
            // 요청 제한을 붙인다. 동시 룸 수 상한을 없앤 자리를 이것이 대신한다 —
            // 룸은 `POST /rooms` 로만 생기므로 막아야 하는 것은 룸의 개수가 아니라
            // 요청의 속도다.
            //
            // 접속과 조회가 같은 양동이를 쓴다. 조회만 막으면 조회를 건너뛰고 접속으로
            // 코드를 찍을 수 있다 — `/ws` 도 없는 코드와 있는 코드에 다르게 답한다.
            endpoints.Map("/ws", HandleAsync).RequireRateLimiting(RateLimitPolicies.CodeAttempt);

            endpoints.MapPost("/rooms", CreateRoom).RequireRateLimiting(RateLimitPolicies.RoomCreate);
            endpoints.MapGet("/rooms/{code}", GetRoom).RequireRateLimiting(RateLimitPolicies.CodeAttempt);
            endpoints.MapGet("/rooms", ListRooms);
        }

        /// 방을 만든다. 코드와 방장 토큰을 돌려준다.
        ///
        /// 룸 생성은 레지스트리 수준이라 HTTP 스레드에서 해도 된다. 룸 *상태* 를 바꾸는
        /// 것과 다르다 — 그쪽은 틱 루프가 소유하므로 `RoomCommand` 를 거친다.
        private static IResult CreateRoom(CreateRoomRequest? request, RoomRegistry rooms, RoomMaps maps)
        {
            var mapId = string.IsNullOrWhiteSpace(request?.Map) ? RoomMaps.DefaultMapId : request!.Map!.Trim();

            if (!rooms.TryCreate(mapId, out var code, out var hostToken, out var error))
            {
                return error switch
                {
                    RoomCreateError.UnknownMap => Results.Json(
                        new ErrorResponse("unknownMap"),
                        JsonDefaults.Options,
                        statusCode: (int)HttpStatusCode.BadRequest),

                    // 코드를 만들지 못한 경우다. 서버 쪽 결함이므로 사유를 따로 둔다 —
                    // 요청이 많아서 거절된 것(429)과 섞이면 화면에서 구분되지 않는다.
                    _ => Results.Json(
                        new ErrorResponse("codeExhausted"),
                        JsonDefaults.Options,
                        statusCode: (int)HttpStatusCode.ServiceUnavailable),
                };
            }

            var map = maps.ByMapId(mapId)!;

            return Results.Json(
                new CreateRoomResponse
                {
                    Code = code,
                    HostToken = hostToken,
                    Map = mapId,
                    MapName = map.Name,
                    MapHash = map.Hash,
                    Capacity = RealtimeConstants.Rooms.MaxPlayers,
                    MinPlayers = RealtimeConstants.Rooms.MinPlayersToStart,
                },
                JsonDefaults.Options,
                statusCode: (int)HttpStatusCode.Created);
        }

        /// 참가 전 조회. 상태코드가 접속 가능 여부를, 본문이 현재 상태를 답한다.
        ///
        /// 이 엔드포인트가 있는 이유는 브라우저다. WebSocket 핸드셰이크가 거부되면
        /// 브라우저는 닫힘 코드 1006 하나만 JS 에 주고, 그러면 서버 미기동·버전
        /// 불일치·없는 방·정원 초과가 화면에서 전부 같은 모습이 된다. 여기서 미리
        /// 갈라낸다.
        ///
        /// 판정은 아니다. `/ws` 가 같은 검사를 다시 하며, 이 응답과 실제 접속 사이에
        /// 정원이 찰 수 있다.
        private static IResult GetRoom(string code, HttpRequest request, RoomRegistry rooms)
        {
            if (!TryReadVersion(request.Query, out var version) || version != ProtocolInfo.Version)
            {
                return Results.Json(
                    new ErrorResponse("versionMismatch"),
                    JsonDefaults.Options,
                    statusCode: (int)HttpStatusCode.UpgradeRequired);
            }

            var normalized = InviteCodeFormat.Normalize(code);

            // 룸 id 규칙으로 검사한다. 초대 코드 형식(`InviteCodeFormat`)이 아니다.
            // 정적 룸 id 는 코드 형식을 만족하지 않으므로(`test` 는 4자다) 여기서
            // 코드 형식을 요구하면 그 룸들을 조회할 수 없다.
            //
            // 그래서 코드 오타 중 일부 — 길이가 맞고 제외된 문자만 섞인 경우 —
            // 는 여기서 404 로 온다. 그 구분은 클라이언트가 입력 칸에서 하며,
            // 같은 판단을 서버에도 두면 정적 룸 id 에 쓸 수 있는 글자가 조용히 줄어든다.
            if (!RoomRegistry.IsValidRoomId(normalized))
            {
                return Results.Json(
                    new ErrorResponse("invalidCode"),
                    JsonDefaults.Options,
                    statusCode: (int)HttpStatusCode.BadRequest);
            }

            if (!rooms.TryGetRoom(normalized, out var summary))
            {
                // 없는 코드와 만료된 코드를 구분하지 않는다. 서버는 만료된 룸의 흔적을
                // 남기지 않으며, 남기면 어떤 코드가 존재했는지 알려 주는 창구가 된다.
                return Results.Json(
                    new ErrorResponse("unknownCode"),
                    JsonDefaults.Options,
                    statusCode: (int)HttpStatusCode.NotFound);
            }

            var body = new RoomInfoResponse
            {
                Code = summary.RoomId,
                MapName = summary.MapName,
                MapHash = summary.MapHash,
                Phase = (byte)summary.Phase,
                PlayerCount = summary.PlayerCount,
                Capacity = summary.Capacity,
                HostPlayerId = summary.HostPlayerId,
                MinPlayers = RealtimeConstants.Rooms.MinPlayersToStart,
            };

            if (summary.Phase != RoomPhase.Waiting)
            {
                return Results.Json(body, JsonDefaults.Options, statusCode: (int)HttpStatusCode.Conflict);
            }

            if (summary.IsFull)
            {
                return Results.Json(body, JsonDefaults.Options, statusCode: (int)HttpStatusCode.ServiceUnavailable);
            }

            return Results.Json(body, JsonDefaults.Options);
        }

        /// 열려 있는 룸 목록. 개발 설정에서만 응답한다.
        ///
        /// 초대 코드 모델에서 공개 목록은 기능이 아니라 결함이다. 코드를 아는 사람만
        /// 들어올 수 있다는 전제가 목록 하나로 사라진다. 서버 상태를 눈으로 볼 수단은
        /// 개발 중에 필요하므로 설정 뒤에 둔다.
        private static IResult ListRooms(RoomRegistry rooms, RealtimeOptions options)
        {
            if (!options.AllowRoomListing)
            {
                return Results.NotFound();
            }

            var summaries = rooms.ListRooms();
            var body = new RoomInfoResponse[summaries.Count];

            for (var index = 0; index < summaries.Count; index++)
            {
                var summary = summaries[index];
                body[index] = new RoomInfoResponse
                {
                    Code = summary.RoomId,
                    MapName = summary.MapName,
                    MapHash = summary.MapHash,
                    Phase = (byte)summary.Phase,
                    PlayerCount = summary.PlayerCount,
                    Capacity = summary.Capacity,
                    HostPlayerId = summary.HostPlayerId,
                    MinPlayers = RealtimeConstants.Rooms.MinPlayersToStart,
                };
            }

            return Results.Json(body, JsonDefaults.Options);
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
