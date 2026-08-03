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
        public IEnumerator Create(string mapId, Action<RoomCreateResult> done)
        {
            var body = JsonUtility.ToJson(new CreateRoomRequestDto { map = mapId ?? string.Empty });

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
                    unchecked((uint)payload.mapHash),
                    payload.capacity,
                    payload.minPlayers));

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
