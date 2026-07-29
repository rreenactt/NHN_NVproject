using NV.Shared.Contracts.Messages;
using NV.Shared.Transport;

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
        public static string BuildUrl(string host, string room, bool secure)
        {
            var scheme = secure ? "wss" : "ws";
            var roomPart = string.IsNullOrEmpty(room)
                ? string.Empty
                : "&" + ProtocolInfo.RoomQueryKey + "=" + room;

            return scheme + "://" + host + "/ws?"
                + ProtocolInfo.VersionQueryKey + "=" + ProtocolInfo.Version
                + roomPart;
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
