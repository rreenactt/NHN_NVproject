using System.Text;
using NV.Shared.Contracts.Messages;
using NV.Shared.Transport;
using UnityEngine.Networking;

namespace NV.Client.Net
{
    /// 플랫폼에 맞는 전송을 고르고 접속 URL 을 만든다.
    ///
    /// 브라우저는 WebSocket 핸드셰이크에 커스텀 헤더를 붙일 수 없다.
    /// 프로토콜 버전과 룸은 쿼리스트링으로만 전달한다. 서버가 업그레이드 전에
    /// 버전을 검사하고 불일치면 426 으로 거부한다.
    public static class ClientTransportFactory
    {
        public static IClientTransport Create()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new WebGlWebSocketTransport();
#else
            return new EditorWebSocketTransport();
#endif
        }

        /// scheme 은 배포 환경에서 반드시 wss 다. HTTPS 페이지의 ws:// 는
        /// mixed content 로 차단되고, 증상은 접속 실패 로그 하나로만 나타난다.
        ///
        /// 방장 토큰은 접속할 때 한 번만 실린다. 서버는 이 토큰으로 방장 세션을
        /// 표시하고 그 뒤로는 다시 보지 않는다 — 매 요청에 토큰을 요구하면 방장이
        /// 나갔을 때 남은 사람에게 줄 토큰이 없어 승계가 막힌다.
        public static string BuildUrl(string host, string room, bool secure, string hostToken, string displayName)
        {
            var url = new StringBuilder(128);

            url.Append(secure ? "wss" : "ws").Append("://").Append(host).Append("/ws?");
            url.Append(ProtocolInfo.VersionQueryKey).Append('=').Append(ProtocolInfo.Version);

            if (!string.IsNullOrEmpty(room))
            {
                url.Append('&').Append(ProtocolInfo.RoomQueryKey).Append('=').Append(room);
            }

            if (!string.IsNullOrEmpty(hostToken))
            {
                url.Append('&').Append(ProtocolInfo.TokenQueryKey).Append('=').Append(hostToken);
            }

            if (!string.IsNullOrEmpty(displayName))
            {
                // 이름은 사용자 입력이다. 인코딩하지 않으면 공백이나 & 가 쿼리를 쪼갠다.
                // 서버도 다시 걸러내지만, 여기서 깨지면 걸러낼 값 자체가 달라진다.
                url.Append('&').Append(ProtocolInfo.NameQueryKey).Append('=')
                    .Append(UnityWebRequest.EscapeURL(displayName));
            }

            return url.ToString();
        }

        public static void Connect(IClientTransport transport, string url)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ((WebGlWebSocketTransport)transport).Connect(url);
#else
            ((EditorWebSocketTransport)transport).Connect(url);
#endif
        }

        /// 접속이 실패했는지. 실패를 표면에 올리지 않으면 화면에는 "연결 중" 만
        /// 영원히 남고, 서버를 띄우지 않은 것과 URL 이 틀린 것을 구분할 수 없다.
        public static bool HasFailed(IClientTransport transport)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ((WebGlWebSocketTransport)transport).HasError;
#else
            return ((EditorWebSocketTransport)transport).HasError;
#endif
        }

        /// 서버가 실은 닫힘 코드. 없으면 0.
        ///
        /// 4000~4999 는 애플리케이션 용도이며 이 프로젝트는 강제 퇴장에 하나를 쓴다. 그
        /// 코드가 없으면 클라이언트가 강제 퇴장을 회선 절단으로 읽고, 자동 재시도가 방금
        /// 내보내진 사람을 다시 그 방에 데려간다.
        public static int CloseCode(IClientTransport transport)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ((WebGlWebSocketTransport)transport).CloseCode;
#else
            return ((EditorWebSocketTransport)transport).CloseCode;
#endif
        }

        public static string FailureReason(IClientTransport transport)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var webgl = (WebGlWebSocketTransport)transport;

            // 브라우저는 보안상 실패 사유를 JS 에 주지 않는다. 닫힘 코드가 유일한 단서다.
            // 1006 은 핸드셰이크 자체가 성립하지 않은 경우이며, 서버 미기동·mixed
            // content 차단·프로토콜 버전 거부(426)가 모두 여기로 뭉쳐서 온다.
            return "WebSocket 닫힘 코드 " + webgl.CloseCode;
#else
            return ((EditorWebSocketTransport)transport).Failure;
#endif
        }
    }
}
