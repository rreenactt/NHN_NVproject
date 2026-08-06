using NV.Client.Net;
using NV.Client.Net.Session;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace NV.Game.UI
{
    /// <summary>
    /// The ESC system menu — resume, return the room to the waiting phase, leave the room, quit
    /// the app. Built by <see cref="MatchBootstrap"/> next to the HUD, so every match scene has
    /// it without any scene carrying it.
    ///
    /// It is a menu over a live game, not a pause: the server keeps ticking and so does the
    /// world. What it owns is the *input handoff* — opening turns
    /// <see cref="FirstPersonController.InputEnabled"/> off, which frees the cursor and zeroes
    /// the latched input, and closing hands it back. It does not touch
    /// <c>MovementLocked</c>: that flag belongs to the chain and the freeze.
    ///
    /// It lives on its own <see cref="UIDocument"/> rather than inside <c>GameHUD.uxml</c> for
    /// three reasons: the HUD's tree is wiped and re-cloned on every role assignment (button
    /// callbacks would die with it), the HUD's whole tree is picking-mode Ignore by convention
    /// (this one exists to be clicked), and the HUD panel sorts under the uGUI crosshair (100) —
    /// the menu clones the shared panel settings and sorts above it.
    /// </summary>
    [DefaultExecutionOrder(70)]
    public sealed class EscapeMenuController : MonoBehaviour
    {
        private const string UxmlPath = "UI/EscapeMenu";
        private const string PanelPath = "UI/GameHudPanelSettings";

        /// <summary>Crosshair canvas is 100; the menu covers the whole game view.</summary>
        private const int SortingOrder = 120;

        private UIDocument _document;
        private PanelSettings _panel;
        private VisualElement _root;
        private Button _return;
        private Button _leave;
        private Button _quit;
        private FirstPersonController _player;

        public bool IsOpen { get; private set; }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.escapeKey.wasPressedThisFrame) Toggle();
        }

        public void Toggle()
        {
            SetOpen(!IsOpen);
        }

        private void SetOpen(bool open)
        {
            if (open && !EnsureTree()) return;

            IsOpen = open;
            if (_root != null) _root.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open) RefreshItems();

            // The input handoff. The controller's own ESC handler only runs while input is
            // enabled, so while the menu is up nothing fights over the cursor — and the
            // weapon checks the same flag, so the click that presses a button never fires.
            var player = ResolvePlayer();
            if (player != null) player.InputEnabled = !open;
        }

        /// <summary>
        /// Closes the visuals without handing input back. For the paths where this scene is
        /// about to be torn down (leave, quit) — re-enabling input would re-lock the cursor on
        /// the way out, and the lobby needs the pointer free.
        /// </summary>
        private void CloseVisualOnly()
        {
            IsOpen = false;
            if (_root != null) _root.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// Which entries make sense right now. Asked on every open rather than cached — the
        /// answer changes with the session state and a stored copy would disagree invisibly.
        /// </summary>
        private void RefreshItems()
        {
            var session = NetSession.Current;

            // No session, no room to leave — offline play still gets resume and quit.
            _leave.style.display = session != null ? DisplayStyle.Flex : DisplayStyle.None;

            // Returning the room to the waiting phase is a request the server only honours from
            // the host once the match has ended, so the entry only exists when it would work.
            var canReturn = session != null && session.State == SessionState.Ended && session.IsHost;
            _return.style.display = canReturn ? DisplayStyle.Flex : DisplayStyle.None;

#if UNITY_WEBGL && !UNITY_EDITOR
            // In a browser Application.Quit() does nothing. A button that does nothing is worse
            // than no button — same judgement as the main lobby's quit.
            _quit.style.display = DisplayStyle.None;
#endif
        }

        private bool EnsureTree()
        {
            // Liveness is asked, never flagged — a domain reload keeps a bool and kills the
            // elements, and the flag then describes a tree that is not there.
            if (_root != null && _root.panel != null) return true;

            var uxml = Resources.Load<VisualTreeAsset>(UxmlPath);
            var basePanel = Resources.Load<PanelSettings>(PanelPath);

            if (uxml == null || basePanel == null)
            {
                Debug.LogError("[EscapeMenu] Resources/UI/EscapeMenu.uxml 또는 GameHudPanelSettings 가 없다.");
                return false;
            }

            if (_document == null)
            {
                _document = GetComponent<UIDocument>();
                if (_document == null) _document = gameObject.AddComponent<UIDocument>();
            }

            if (_panel == null)
            {
                // A runtime clone rather than a second asset: only the sorting order differs,
                // and a copied asset is a copy that can drift.
                _panel = Instantiate(basePanel);
                _panel.name = basePanel.name + " (escape menu)";
                _panel.sortingOrder = SortingOrder;
            }

            _document.panelSettings = _panel;
            _document.visualTreeAsset = null;   // cloned by hand, same as the HUD

            var documentRoot = _document.rootVisualElement;
            documentRoot.Clear();
            uxml.CloneTree(documentRoot);

            _root = documentRoot.Q<VisualElement>("esc-root");
            _return = documentRoot.Q<Button>("esc-return");
            _leave = documentRoot.Q<Button>("esc-leave");
            _quit = documentRoot.Q<Button>("esc-quit");

            var resume = documentRoot.Q<Button>("esc-resume");
            if (resume != null) resume.clicked += () => SetOpen(false);
            if (_return != null) _return.clicked += ReturnRoomToLobby;
            if (_leave != null) _leave.clicked += LeaveRoom;
            if (_quit != null) _quit.clicked += QuitGame;

            _root.style.display = DisplayStyle.None;
            return true;
        }

        private void ReturnRoomToLobby()
        {
            NetSession.Current?.RequestReturnToLobby();

            // A request, not a transition: the room comes back through the RoomState bulletin
            // and the router changes the scene. Close and let that happen.
            SetOpen(false);
        }

        private void LeaveRoom()
        {
            CloseVisualOnly();

            // Leave() clears the code and cancels the retry, so the disconnect is not mistaken
            // for a dropped connection — the router then routes Idle to the main lobby.
            NetSession.Current?.Leave();
        }

        private void QuitGame()
        {
            CloseVisualOnly();

            // Leave first, then quit — the socket closes cleanly and the room reclaims the slot
            // now instead of waiting out a timeout. Same order as the main lobby's Shutdown.
            NetSession.Current?.Leave();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// The body whose input this menu borrows. Resolved lazily and re-resolved when gone —
        /// the local player is built at runtime and a cached reference dies with a domain
        /// reload or a respawn.
        /// </summary>
        private FirstPersonController ResolvePlayer()
        {
            if (_player != null) return _player;

            var bootstrap = FindFirstObjectByType<NetworkBootstrap>();
            if (bootstrap != null) _player = bootstrap.LocalPlayer;
            if (_player == null) _player = FindFirstObjectByType<FirstPersonController>();

            return _player;
        }
    }
}
