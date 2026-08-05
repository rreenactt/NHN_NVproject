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
            // 목록에도 제한을 붙인다. 예전에는 개발 설정 뒤에 있어 제한이 없었지만
            // 이제 상시 열려 있다. 코드 시도와 양동이를 나눈 이유는 반대다 — 목록을
            // 자주 새로고침하는 것이 정작 방에 들어갈 예산을 깎으면 안 된다.
            endpoints.MapGet("/rooms", ListRooms).RequireRateLimiting(RateLimitPolicies.RoomList);

            // 방 목록과 양동이를 나눠 쓴다. 맵 목록은 로비 화면에 들어올 때 한 번 부르는
            // 것이고 성질이 방 목록과 같다 — 코드를 찍어 보는 경로가 아니다. 새 정책을
            // 만들면 설정 키가 하나 늘고 얻는 것이 없다.
            endpoints.MapGet("/maps", ListMaps).RequireRateLimiting(RateLimitPolicies.RoomList);
        }

        /// 등록된 맵 전부. 방 만들기 화면이 이것으로 목록을 만든다.
        ///
        /// **프로토콜 버전을 요구하지 않는다.** `GET /rooms/{code}` 는 접속 직전 조회라 버전이
        /// 다르면 426 으로 끊는 것이 맞지만, 맵 목록은 접속 전 화면을 그리는 값이다. 버전이 다른
        /// 클라이언트에게도 답해 주는 편이 낫고, 그쪽의 실패는 접속 시점에 정확히 갈린다.
        ///
        /// 본문은 기동 때 만들어져 있다(`MapListPayload`). 맵은 로드 후 변하지 않으므로
        /// 요청마다 직렬화할 이유가 없고, 불변이므로 ETag 로 두 번째 조회를 304 로 끝낼 수 있다.
        private static IResult ListMaps(HttpContext context, MapListPayload payload)
        {
            if (MatchesETag(context.Request, payload.ETag))
            {
                return Results.StatusCode((int)HttpStatusCode.NotModified);
            }

            context.Response.Headers.ETag = payload.ETag;

            // 짧게 잡는다. 서버를 다시 띄우면 목록이 바뀔 수 있고(맵 파일을 놓는 것이 등록이다)
            // 그때 오래된 캐시를 들고 있는 클라이언트는 없는 맵으로 방을 만들려 한다.
            context.Response.Headers.CacheControl = "public, max-age=60";

            return Results.Text(payload.Json, "application/json");
        }

        /// 클라이언트가 들고 있는 것이 지금 본문과 같은가.
        ///
        /// `If-None-Match` 는 값이 여러 개일 수 있고 `*` 도 올 수 있다. 문자열 하나와
        /// 그대로 비교하면 여러 개를 보낸 클라이언트가 매번 전체 본문을 받는다.
        private static bool MatchesETag(HttpRequest request, string etag)
        {
            var candidates = request.Headers.IfNoneMatch;

            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];

                if (candidate == null)
                {
                    continue;
                }

                if (candidate == "*" || candidate.Trim() == etag)
                {
                    return true;
                }
            }

            return false;
        }

        /// 방을 만든다. 코드와 방장 토큰을 돌려준다.
        ///
        /// 룸 생성은 레지스트리 수준이라 HTTP 스레드에서 해도 된다. 룸 *상태* 를 바꾸는
        /// 것과 다르다 — 그쪽은 틱 루프가 소유하므로 `RoomCommand` 를 거친다.
        private static IResult CreateRoom(
            CreateRoomRequest? request,
            RoomRegistry rooms,
            RoomMaps maps,
            MapListPayload catalog)
        {
            var mapId = string.IsNullOrWhiteSpace(request?.Map) ? RoomMaps.DefaultMapId : request!.Map!.Trim();

            // 보내지 않았으면 비공개다. 노출은 선택이어야 하고, 선택하지 않은 요청을
            // 노출하는 쪽으로 해석하면 필드를 모르는 클라이언트의 방이 목록에 뜬다.
            var isPublic = request?.IsPublic ?? false;

            if (!rooms.TryCreate(mapId, isPublic, out var code, out var hostToken, out var error))
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

            // **요청한 id 가 아니라 해석된 id 를 돌려준다.** `default` 로 만든 클라이언트도
            // 자기 방의 맵이 `backrooms` 라는 것을 알아야 한다 — 그것이 곧 씬을 정하는 이름이고,
            // 맵 id 는 맵 이름과 같으므로(`MapCatalogLoader`) `map.Name` 이 그 값이다.
            return Results.Json(
                new CreateRoomResponse
                {
                    Code = code,
                    HostToken = hostToken,
                    Map = map.Name,
                    MapName = map.Name,
                    MapDisplayName = catalog.DisplayNameOf(map.Name),
                    MapHash = map.Hash,
                    Capacity = RealtimeConstants.Rooms.MaxPlayers,
                    MinPlayers = RealtimeConstants.Rooms.MinPlayersToStart,
                    IsPublic = isPublic,
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
        private static IResult GetRoom(
            string code,
            HttpRequest request,
            RoomRegistry rooms,
            MapListPayload maps)
        {
            if (!TryReadVersion(request.Query, out var version) || version != ProtocolInfo.Version)
            {
                return Results.Json(
                    new ErrorResponse("versionMismatch"),
                    JsonDefaults.Options,
                    statusCode: (int)HttpStatusCode.UpgradeRequired);
            }

            // 룸 id 규칙으로 검사한다. 초대 코드 형식(`InviteCodeFormat`)이 아니다.
            // 정적 룸 id 는 코드 형식을 만족하지 않으므로(`test` 는 4자다) 여기서
            // 코드 형식을 요구하면 그 룸들을 조회할 수 없다.
            //
            // 그래서 코드 오타 중 일부 — 길이가 맞고 제외된 문자만 섞인 경우 —
            // 는 여기서 404 로 온다. 그 구분은 클라이언트가 입력 칸에서 하며,
            // 같은 판단을 서버에도 두면 정적 룸 id 에 쓸 수 있는 글자가 조용히 줄어든다.
            var raw = (code ?? string.Empty).Trim().ToLowerInvariant();
            var normalized = InviteCodeFormat.Normalize(code);

            if (!RoomRegistry.IsValidRoomId(raw) && !RoomRegistry.IsValidRoomId(normalized))
            {
                return Results.Json(
                    new ErrorResponse("invalidCode"),
                    JsonDefaults.Options,
                    statusCode: (int)HttpStatusCode.BadRequest);
            }

            if (!TryFindRoom(rooms, raw, normalized, out var summary))
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
                MapDisplayName = maps.DisplayNameOf(summary.MapName),
                MapHash = summary.MapHash,
                Phase = (byte)summary.Phase,
                PlayerCount = summary.PlayerCount,
                BotCount = summary.BotCount,
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

        /// 조회에 쓸 룸을 찾는다. **원문 id(소문자화)가 먼저, 코드 정돈본이 다음이다.**
        ///
        /// `InviteCodeFormat.Normalize` 는 하이픈을 버린다 — 사람이 붙여넣은 코드의
        /// 구분선이기 때문이다. 그런데 룸 id 에는 하이픈이 정당하게 들어간다
        /// (`test-backrooms`). 정돈본만으로 찾으면 그 룸들이 **목록에는 실리는데
        /// 조회는 404** 가 되고, 프리플라이트가 조회이므로 참가가 전부 막힌다.
        ///
        /// 두 해석이 서로 다른 방을 가리킬 수는 없다. 초대 코드 알파벳에 하이픈이
        /// 없으므로, 하이픈이 든 원문과 일치하는 방은 코드가 아니라 정적 룸뿐이다.
        /// 테스트가 접근할 수 있도록 `internal` 이다.
        internal static bool TryFindRoom(
            RoomRegistry rooms,
            string rawId,
            string normalizedCode,
            out RoomSummary summary)
        {
            if (RoomRegistry.IsValidRoomId(rawId) && rooms.TryGetRoom(rawId, out summary))
            {
                return true;
            }

            summary = default;

            return RoomRegistry.IsValidRoomId(normalizedCode)
                && rooms.TryGetRoom(normalizedCode, out summary);
        }

        /// 공개로 만들어진 룸의 목록.
        ///
        /// 예전에는 설정(`Realtime:AllowRoomListing`) 뒤에 있었고 기본값이 꺼짐이었다.
        /// 그 플래그는 목록이 **모든** 방을 내주던 시절의 방어선이다 — 코드를 아는 사람만
        /// 들어올 수 있다는 전제가 목록 하나로 사라지므로, 통째로 막는 것 말고 할 수 있는
        /// 일이 없었다.
        ///
        /// 방마다 공개 여부를 정하게 되면서 그 근거가 없어졌다. 여기 실리는 방은 만든
        /// 사람이 실리기로 선택한 방뿐이고, 노출은 이제 사고가 아니라 동의다. 플래그를
        /// 남겨 두면 공개를 선택한 방조차 목록에 뜨지 않아 기능이 죽는다.
        ///
        /// 대신 **요청 속도 제한이 붙었다.** 플래그가 사라지면 이 경로는 상시 열린 공개
        /// 엔드포인트가 되는데, 지금까지 제한이 없었던 것은 개발 설정에서만 열렸기
        /// 때문이다. 그 전제가 바뀌었으므로 제한이 그 자리를 대신한다.
        private static IResult ListRooms(RoomRegistry rooms, MapListPayload maps)
        {
            var summaries = rooms.ListPublicRooms();
            var body = new RoomInfoResponse[summaries.Count];

            for (var index = 0; index < summaries.Count; index++)
            {
                var summary = summaries[index];
                body[index] = new RoomInfoResponse
                {
                    Code = summary.RoomId,
                    MapName = summary.MapName,
                    MapDisplayName = maps.DisplayNameOf(summary.MapName),
                    MapHash = summary.MapHash,
                    Phase = (byte)summary.Phase,
                    PlayerCount = summary.PlayerCount,
                    BotCount = summary.BotCount,
                    Capacity = summary.Capacity,
                    HostPlayerId = summary.HostPlayerId,
                    MinPlayers = RealtimeConstants.Rooms.MinPlayersToStart,
                    IsPublic = summary.IsPublic,
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

                // 강제 퇴장은 닫힘 코드를 실어 보낸다. **펌프가 끝난 뒤에 한다** — 송신
                // 펌프가 아직 돌고 있으면 같은 소켓에 두 개의 송신이 겹치고, WebSocket 은
                // 동시 송신을 허용하지 않는다.
                await session.CloseWithReasonAsync(context.RequestAborted).ConfigureAwait(false);
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
