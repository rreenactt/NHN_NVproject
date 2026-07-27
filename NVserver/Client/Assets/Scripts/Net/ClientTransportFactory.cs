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
    }
}
