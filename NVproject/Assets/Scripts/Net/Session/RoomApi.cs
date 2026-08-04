using System;
using System.Collections;
using System.Text;
using NV.Shared.Contracts.Messages;
using UnityEngine;
using UnityEngine.Networking;

namespace NV.Client.Net.Session
{
    /// 방 만들기와 참가 전 조회. 서버의 HTTP 계약을 다루는 유일한 지점이다.
    ///
    /// 조회가 있는 이유는 브라우저다. WebSocket 핸드셰이크가 거부되면 브라우저는
    /// 닫힘 코드 1006 하나만 JS 에 주므로, 서버 미기동·버전 불일치·없는 방·정원
    /// 초과가 전부 같은 실패로 보인다. 접속 전에 HTTP 로 한 번 물어보는 것이
    /// 그것들을 갈라내는 유일한 방법이다.
    ///
    /// 코루틴으로만 동작한다. WebGL 은 단일 스레드이고 `UnityWebRequest` 는
    /// 브라우저 XHR 위에 있어 기다릴 수 없다.
    public sealed class RoomApi
    {
        /// 응답이 이 시간을 넘으면 서버에 닿지 못한 것으로 본다.
        private const int TimeoutSeconds = 6;

        private readonly string _baseUrl;

        public RoomApi(string host, bool secure)
        {
            _baseUrl = (secure ? "https://" : "http://") + host;
        }

        public string BaseUrl => _baseUrl;

        /// 방을 만든다. 성공하면 코드와 방장 토큰이 담긴다.
        ///
        /// <param name="isPublic">
        /// 공개 목록(`GET /rooms`)에 실을 것인가. 서버도 이 필드가 없으면 비공개로
        /// 해석하므로, 두 쪽 모두에서 노출은 선택이지 기본값이 아니다.
        /// </param>
        public IEnumerator Create(string mapId, bool isPublic, Action<RoomCreateResult> done)
        {
            var body = JsonUtility.ToJson(new CreateRoomRequestDto
            {
                map = mapId ?? string.Empty,
                isPublic = isPublic,
            });

            using var request = new UnityWebRequest(_baseUrl + "/rooms", "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSeconds,
            };

            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (!Reached(request))
            {
                done(new RoomCreateResult(SessionFailureKind.ServerUnreachable));
                yield break;
            }

            if (request.responseCode == 201)
            {
                var payload = JsonUtility.FromJson<CreateRoomResponseDto>(request.downloadHandler.text);

                if (payload == null || string.IsNullOrEmpty(payload.code))
                {
                    done(new RoomCreateResult(SessionFailureKind.ServerUnreachable));
                    yield break;
                }

                done(new RoomCreateResult(
                    payload.code,
                    payload.hostToken,
                    payload.mapName,
                    payload.mapDisplayName,
                    unchecked((uint)payload.mapHash),
                    payload.capacity,
                    payload.minPlayers,
                    payload.isPublic));

                yield break;
            }

            done(new RoomCreateResult(CreateFailure(request)));
        }

        /// 코드로 방 상태를 확인한다.
        ///
        /// 상태코드가 "들어갈 수 있는가" 를, 본문이 "지금 어떤 상태인가" 를 답한다.
        /// 정원 초과와 진행 중은 둘 다 온다 — 그래서 실패인데도 정보가 있다.
        public IEnumerator Probe(string normalizedCode, Action<RoomProbeResult> done)
        {
            var url = _baseUrl + "/rooms/" + normalizedCode
                + "?" + ProtocolInfo.VersionQueryKey + "=" + ProtocolInfo.Version;

            var startedAt = Time.realtimeSinceStartup;

            using var request = UnityWebRequest.Get(url);
            request.timeout = TimeoutSeconds;

            yield return request.SendWebRequest();

            var elapsed = Time.realtimeSinceStartup - startedAt;

            if (!Reached(request))
            {
                done(new RoomProbeResult(default, SessionFailureKind.ServerUnreachable, elapsed));
                yield break;
            }

            var info = ReadRoomInfo(request);

            switch (request.responseCode)
            {
                case 200:
                    done(new RoomProbeResult(info, SessionFailureKind.None, elapsed));
                    break;

                case 409:
                    done(new RoomProbeResult(info, SessionFailureKind.RoomInProgress, elapsed));
                    break;

                case 503:
                    done(new RoomProbeResult(info, SessionFailureKind.RoomFull, elapsed));
                    break;

                case 426:
                    done(new RoomProbeResult(default, SessionFailureKind.VersionMismatch, elapsed));
                    break;

                case 400:
                    done(new RoomProbeResult(default, SessionFailureKind.InvalidCode, elapsed));
                    break;

                case 429:
                    done(new RoomProbeResult(default, SessionFailureKind.TooManyRequests, elapsed));
                    break;

                default:
                    done(new RoomProbeResult(default, SessionFailureKind.UnknownCode, elapsed));
                    break;
            }
        }

        /// 공개된 방 목록을 받는다.
        ///
        /// 이 엔드포인트는 서버 설정(`Realtime:AllowRoomListing`)이 켜져 있을 때만
        /// 답한다. 꺼져 있으면 **404 에 빈 본문**이며, 그것은 오류가 아니라 "이 서버는
        /// 목록을 공개하지 않는다" 는 정상 응답이다 — 초대 코드 모델에서는 그쪽이
        /// 기본값이다. 그래서 404 만 `NotPublished` 로 따로 뺀다.
        ///
        /// 서버 쪽에 이 경로만 레이트리밋이 걸려 있지 않다. 자동 폴링을 붙이면 무방비로
        /// 맞으므로 호출자가 주기를 만들지 않는다 — 화면 진입 1회와 수동 새로고침뿐이다.
        public IEnumerator List(Action<RoomListResult> done)
        {
            using var request = UnityWebRequest.Get(_baseUrl + "/rooms");
            request.timeout = TimeoutSeconds;

            yield return request.SendWebRequest();

            if (!Reached(request))
            {
                done(RoomListResult.Failed(SessionFailureKind.ServerUnreachable));
                yield break;
            }

            if (request.responseCode == 404)
            {
                done(RoomListResult.NotPublished());
                yield break;
            }

            if (request.responseCode == 429)
            {
                done(RoomListResult.Failed(SessionFailureKind.TooManyRequests));
                yield break;
            }

            if (request.responseCode != 200)
            {
                done(RoomListResult.Failed(SessionFailureKind.ServerUnreachable));
                yield break;
            }

            done(new RoomListResult(ReadRoomList(request)));
        }

        /// 서버가 아는 맵의 목록.
        ///
        /// **404 는 실패가 아니다.** 이 엔드포인트가 없는 옛 서버가 그렇게 답하며, 그때 로비는
        /// 이 빌드가 아는 맵만으로 목록을 만든다. 그 구분이 없으면 옛 서버에 붙은 클라이언트가
        /// 방을 아예 만들 수 없다.
        ///
        /// 프로토콜 버전을 붙이지 않는다. 서버도 이 경로에서는 그것을 요구하지 않는다 —
        /// 접속 전 화면을 그리는 값이고, 버전 불일치는 접속 시점에 426 으로 정확히 갈린다.
        ///
        /// **방 목록과 분당 예산(`RateLimit:ListPerMinute`, 30)을 나눠 쓴다.** 그래서 이것을
        /// 주기적으로 부르지 않는다 — 한 세션에 한 번이고(`MapChoiceService` 가 캐시한다) 서버가
        /// 바뀔 때만 다시 받는다. 팝업을 열 때마다 받으면 새로고침 예산을 팝업이 쓴다.
        public IEnumerator Maps(Action<MapListResult> done)
        {
            using var request = UnityWebRequest.Get(_baseUrl + "/maps");
            request.timeout = TimeoutSeconds;

            yield return request.SendWebRequest();

            if (!Reached(request))
            {
                done(MapListResult.Failed(SessionFailureKind.ServerUnreachable));
                yield break;
            }

            if (request.responseCode == 404)
            {
                done(MapListResult.NotPublished());
                yield break;
            }

            if (request.responseCode == 429)
            {
                done(MapListResult.Failed(SessionFailureKind.TooManyRequests));
                yield break;
            }

            if (request.responseCode != 200)
            {
                done(MapListResult.Failed(SessionFailureKind.ServerUnreachable));
                yield break;
            }

            done(MapListResult.Ok(ReadMapList(request)));
        }

        /// 서버가 살아 있는가.
        ///
        /// `GET /health` 는 JSON 이 아니라 `text/plain` 으로 `ok` 한 줄을 준다. 파싱하려
        /// 들면 실패한다. 여기서 보는 것은 본문이 아니라 "200 이 왔는가" 뿐이다.
        public IEnumerator Health(Action<bool> done)
        {
            using var request = UnityWebRequest.Get(_baseUrl + "/health");
            request.timeout = TimeoutSeconds;

            yield return request.SendWebRequest();

            done(Reached(request) && request.responseCode == 200);
        }

        /// 서버까지 닿았는가.
        ///
        /// `result` 만 보면 안 된다. `ProtocolError` 는 서버가 4xx·5xx 로 답한
        /// 경우이며 그것은 닿은 것이다 — 그 둘을 묶으면 버전 불일치와 서버
        /// 미기동이 다시 같은 실패가 된다.
        private static bool Reached(UnityWebRequest request)
        {
            return request.result != UnityWebRequest.Result.ConnectionError
                && request.result != UnityWebRequest.Result.DataProcessingError;
        }

        private static RoomInfo ReadRoomInfo(UnityWebRequest request)
        {
            var text = request.downloadHandler?.text;
            if (string.IsNullOrEmpty(text) || text[0] != '{')
            {
                return default;
            }

            var payload = JsonUtility.FromJson<RoomInfoResponseDto>(text);
            return payload == null ? default : payload.ToRoomInfo();
        }

        /// 배열 응답을 읽는다.
        ///
        /// `JsonUtility` 는 최상위 배열을 파싱하지 못하고 **예외 대신 null 을 돌려준다**.
        /// 감싸지 않으면 파싱 실패가 "방이 0개" 로 조용히 둔갑해, 목록이 비어 보이는
        /// 원인이 서버인지 클라이언트인지 화면에서 구분할 수 없게 된다.
        private static RoomInfo[] ReadRoomList(UnityWebRequest request)
        {
            var text = request.downloadHandler?.text;

            if (string.IsNullOrEmpty(text) || text[0] != '[')
            {
                return Array.Empty<RoomInfo>();
            }

            var wrapped = RoomListResponseDto.WrapperPrefix + text + RoomListResponseDto.WrapperSuffix;
            var payload = JsonUtility.FromJson<RoomListResponseDto>(wrapped);

            if (payload?.items == null)
            {
                return Array.Empty<RoomInfo>();
            }

            var rooms = new RoomInfo[payload.items.Length];

            for (var index = 0; index < payload.items.Length; index++)
            {
                rooms[index] = payload.items[index].ToRoomInfo();
            }

            return rooms;
        }

        /// 맵 목록도 최상위 배열이다. 감싸는 이유는 `ReadRoomList` 와 같다 —
        /// 파싱 실패가 "0개" 로 둔갑하면 원인이 서버인지 클라이언트인지 알 수 없다.
        private static ServerMapInfo[] ReadMapList(UnityWebRequest request)
        {
            var text = request.downloadHandler?.text;

            if (string.IsNullOrEmpty(text) || text[0] != '[')
            {
                return Array.Empty<ServerMapInfo>();
            }

            var wrapped = MapListResponseDto.WrapperPrefix + text + MapListResponseDto.WrapperSuffix;
            var payload = JsonUtility.FromJson<MapListResponseDto>(wrapped);

            if (payload?.items == null)
            {
                return Array.Empty<ServerMapInfo>();
            }

            var maps = new ServerMapInfo[payload.items.Length];

            for (var index = 0; index < payload.items.Length; index++)
            {
                maps[index] = payload.items[index].ToMapInfo();
            }

            return maps;
        }

        private static SessionFailureKind CreateFailure(UnityWebRequest request)
        {
            switch (request.responseCode)
            {
                case 400:
                    return SessionFailureKind.UnknownMap;

                // 서버가 요청 속도를 제한한다. 동시 룸 수 상한을 없앤 자리를 그것이
                // 대신하므로, 옛 "방을 더 만들 수 없다" 는 이 응답으로 온다.
                case 429:
                    return SessionFailureKind.TooManyRequests;

                case 503:
                    return SessionFailureKind.RoomCreateFailed;

                default:
                    return SessionFailureKind.ServerUnreachable;
            }
        }
    }
}
