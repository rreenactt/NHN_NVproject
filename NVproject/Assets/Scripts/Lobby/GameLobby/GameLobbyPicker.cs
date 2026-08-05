using System.Collections.Generic;
using NV.Client.Lobby.Models;
using NV.Client.Net.Session;
using NV.Lobby;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NV.Client.Lobby.GameLobby
{
    /// 방을 클릭한다. **방장이 사람을 지목하는 제스처다.**
    ///
    /// 옛 로비에서 클릭은 자리 이동과 교환 요청이었다. 지금은 아니다 — 스탠드 번호가 곧
    /// 서버의 `PlayerId` 이고 그것이 스폰 위치를 고르므로, 자리를 옮기는 것은 스폰을 옮기는
    /// 것과 같은 말이 된다. 그래서 클릭은 방장에게만 뜻이 있고, 남의 스탠드를 누르면 그
    /// 사람에 대한 확인을 띄운다.
    ///
    /// 방장이 아니면 아무 일도 하지 않는다. 눌러도 반응이 없는 것이 눌러서 거부되는 것보다
    /// 낫다 — 후자는 서버에 요청을 보내고 그것이 거부되는 경로를 만든다.
    public sealed class GameLobbyPicker : MonoBehaviour
    {
        public Camera lobbyCamera;

        [Tooltip("m. 방은 작고 이것은 뒷벽까지만 닿으면 된다.")]
        public float pickRange = 40f;

        private NetSession _session;
        private GameLobbyHud _hud;
        private List<LobbySlot> _slots;

        private readonly RoomMember[] _members = new RoomMember[16];

        public void Bind(NetSession session, GameLobbyHud hud, List<LobbySlot> slots)
        {
            _session = session;
            _hud = hud;
            _slots = slots;
        }

        private void Update()
        {
            if (_session == null || _hud == null || lobbyCamera == null || _slots == null)
            {
                return;
            }

            // 대기 단계에서만 받는다. 매치 중에 사람을 끊는 것은 진행 중인 판정을 흔든다.
            if (_session.State != SessionState.InLobby || !_session.IsHost)
            {
                return;
            }

            Mouse mouse = Mouse.current;

            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            {
                return;
            }

            // 버튼에 떨어진 클릭은 그 뒤의 방을 누른 것이 아니다.
            if (_hud.PointerOverUi)
            {
                return;
            }

            Ray ray = lobbyCamera.ScreenPointToRay(mouse.position.ReadValue());

            if (!Physics.Raycast(ray, out RaycastHit hit, pickRange, ~0, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            LobbySlot slot = hit.collider.GetComponentInParent<LobbySlot>();

            if (slot != null)
            {
                Click(slot.Index);
            }
        }

        /// 스탠드 번호는 `PlayerId` 다. 그 번호의 사람을 명단에서 찾는다.
        private void Click(int slotIndex)
        {
            var count = RoomMember.Collect(_session, _members);

            for (var index = 0; index < count; index++)
            {
                if (_members[index].PlayerId != slotIndex)
                {
                    continue;
                }

                // 자기 스탠드에는 할 일이 없다. 나가기는 버튼이 따로 있다.
                if (_members[index].IsSelf)
                {
                    return;
                }

                var playerId = _members[index].PlayerId;
                var name = _members[index].DisplayName;

                // 확인을 지난다. 되돌릴 수 없고, 대상은 아무 잘못이 없을 수 있다.
                _hud.Confirm(
                    $"{name} 을(를) 내보낸다",
                    () => _session.KickPlayer(playerId));

                return;
            }
        }
    }
}
