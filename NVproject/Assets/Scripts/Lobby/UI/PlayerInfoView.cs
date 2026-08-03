using NV.Client.Lobby.Models;
using UnityEngine.UIElements;

namespace NV.Client.Lobby.UI
{
    /// 헤더의 플레이어 칸. 표시만 한다.
    public sealed class PlayerInfoView
    {
        private readonly Label _name;
        private readonly Label _note;

        public PlayerInfoView(VisualElement slot)
        {
            var element = MainLobbyAssets.Clone("PlayerInfo");

            if (element == null || slot == null)
            {
                return;
            }

            slot.Add(element);

            _name = element.Q<Label>("player-name");
            _note = element.Q<Label>("player-note");
        }

        public void Refresh()
        {
            if (_name == null)
            {
                return;
            }

            var stored = PlayerProfile.DisplayName;

            // 이름이 없으면 서버가 명단에서 `플레이어 N` 으로 보여 준다. 화면에서도
            // 같은 말을 써서, 이름을 비운 것이 설정되지 않은 상태라는 것을 드러낸다.
            _name.text = string.IsNullOrEmpty(stored) ? "이름 없음" : stored;

            // 계정이 없다는 사실을 감추지 않는다. 이름은 세션 수명만큼만 살고
            // 중복도 사칭도 막지 않는다 — 그것을 모르면 같은 이름 둘을 버그로 읽는다.
            _note.text = string.IsNullOrEmpty(stored)
                ? "설정에서 이름을 정한다"
                : "계정 없음 · 이름은 이번 접속에만 유효";
        }
    }
}
