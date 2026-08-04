using UnityEngine;
using UnityEngine.InputSystem;

namespace NV.Lobby
{
    /// <summary>
    /// Clicking the room. An empty stand is a move; somebody else's stand is a request to trade.
    ///
    /// Both go through <see cref="LobbyManager"/>'s <c>Request*</c> methods and neither moves
    /// anything locally — the figure only walks over once the authority says so. That is what
    /// stops two players from both believing they own stand 3.
    /// </summary>
    public sealed class LobbySlotPicker : MonoBehaviour
    {
        public Camera lobbyCamera;
        public LobbyHud hud;

        [Tooltip("Metres. The room is small; this only has to reach the back wall.")]
        public float pickRange = 40f;

        private LobbySlot _hovered;

        private void Update()
        {
            LobbyManager lobby = LobbyManager.Instance;
            Mouse mouse = Mouse.current;
            if (lobby == null || mouse == null || lobbyCamera == null) return;

            // A click that landed on a button is not also a click on the room behind it.
            if (hud != null && hud.PointerOverUi) { _hovered = null; return; }

            Vector2 screen = mouse.position.ReadValue();
            Ray ray = lobbyCamera.ScreenPointToRay(screen);

            _hovered = Physics.Raycast(ray, out RaycastHit hit, pickRange, ~0, QueryTriggerInteraction.Ignore)
                ? hit.collider.GetComponentInParent<LobbySlot>()
                : null;

            if (!mouse.leftButton.wasPressedThisFrame || _hovered == null) return;

            Click(lobby, _hovered);
        }

        private static void Click(LobbyManager lobby, LobbySlot slot)
        {
            if (lobby.InputLocked) return;

            LobbyPlayer local = lobby.Local;
            if (local == null) return;

            LobbyPlayer occupant = slot.Player;

            if (occupant == null)
            {
                lobby.RequestMoveToSlot(slot.Index);
                return;
            }

            if (occupant.id == local.id) return;               // your own stand; nothing to do

            if (lobby.Config.swapMode == SlotSwapMode.SwapRequest)
                lobby.RequestSwapWith(occupant.id);
        }
    }
}
