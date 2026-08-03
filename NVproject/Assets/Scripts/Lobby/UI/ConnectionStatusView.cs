using NV.Client.Lobby.Models;
using NV.Shared.Contracts.Messages;
using UnityEngine.UIElements;

namespace NV.Client.Lobby.UI
{
    /// 헤더의 서버 상태 칸. 연결 여부, 주소·프로토콜 버전, 온라인 인원.
    ///
    /// 온라인 인원은 서버가 알려 주는 값이 아니다 — 전역 세션 수를 내주는 엔드포인트가
    /// 없고, 여기 뜨는 숫자는 **공개된 방들의 인원 합계**다. 그래서 라벨이 그 근거를
    /// 같이 적고, 목록을 공개하지 않는 서버에서는 줄 자체를 숨긴다. 0 을 띄우면
    /// "아무도 없다" 라는 거짓말이 된다.
    public sealed class ConnectionStatusView
    {
        private readonly VisualElement _root;
        private readonly Label _label;
        private readonly Label _meta;
        private readonly Label _players;

        public ConnectionStatusView(VisualElement slot)
        {
            var element = MainLobbyAssets.Clone("ConnectionStatus");

            if (element == null || slot == null)
            {
                return;
            }

            slot.Add(element);

            _root = element;
            _label = element.Q<Label>("connection-label");
            _meta = element.Q<Label>("connection-meta");
            _players = element.Q<Label>("connection-players");
        }

        public void Refresh(LobbyModel model, string host)
        {
            if (_root == null)
            {
                return;
            }

            _root.EnableInClassList("connection-online", model.Server == ServerStatus.Online);
            _root.EnableInClassList("connection-offline", model.Server == ServerStatus.Offline);

            _label.text = StatusText(model.Server);
            _meta.text = host + "  ·  PROTOCOL v" + ProtocolInfo.Version;

            var show = model.HasOnlineCount;
            _players.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;

            if (show)
            {
                _players.text = $"접속 {model.OnlinePlayers}명 · 공개된 방 기준";
            }
        }

        private static string StatusText(ServerStatus status)
        {
            switch (status)
            {
                case ServerStatus.Online: return "온라인";
                case ServerStatus.Offline: return "오프라인";
                case ServerStatus.Checking: return "확인 중…";
                default: return "확인 전";
            }
        }
    }
}
