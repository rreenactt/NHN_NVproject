using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace NV.Lobby
{
    /// <summary>
    /// The lobby's interface: roster, appearance, ready button, countdown, swap prompt.
    ///
    /// It is UI Toolkit and it imports the in-game HUD's stylesheet directly, so the lobby is
    /// visibly the same interface as the match — the player never leaves the building, and a clean
    /// menu here would break that.
    ///
    /// It decides nothing. Every button posts a <c>Request*</c> to <see cref="LobbyManager"/> and
    /// then waits to be told what happened, exactly like a networked client would.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public sealed class LobbyHud : MonoBehaviour
    {
        private const string UxmlPath = "UI/LobbyHUD";
        private const string PanelPath = "UI/GameHudPanelSettings";

        private UIDocument _document;
        private VisualTreeAsset _uxml;
        private LobbyManager _lobby;

        private VisualElement _root;
        private Label _notice, _countdown, _countdownCaption, _rosterCount, _hint, _swapText, _title;
        private VisualElement _roster, _customization, _swapPrompt;
        private Button _readyButton, _swapAccept, _swapDecline;

        private float _noticeTimer;
        private int _pendingSwapId = -1;
        private readonly List<string> _notices = new List<string>();

        /// <summary>True while the pointer is over an interactive panel, so slot clicks are not double-handled.</summary>
        public bool PointerOverUi { get; private set; }

        private bool TreeIsLive => _root != null && _readyButton != null && _root.panel != null;

        private void Awake() => EnsureAssets();

        private bool EnsureAssets()
        {
            if (_uxml == null) _uxml = Resources.Load<VisualTreeAsset>(UxmlPath);

            if (_document == null)
            {
                _document = GetComponent<UIDocument>();
                if (_document == null) _document = gameObject.AddComponent<UIDocument>();
            }

            if (_document.panelSettings == null)
                _document.panelSettings = Resources.Load<PanelSettings>(PanelPath);

            if (_uxml == null || _document.panelSettings == null)
            {
                Debug.LogError("[Lobby] Missing " + UxmlPath + " or " + PanelPath + " under Assets/Resources.");
                return false;
            }

            _document.visualTreeAsset = null;   // cloned by hand, like the match HUD
            return true;
        }

        private void OnEnable()
        {
            _lobby = LobbyManager.Instance;
            if (_lobby == null) return;

            _lobby.RosterChanged += Rebuild;
            _lobby.StateChanged += OnStateChanged;
            _lobby.CountdownChanged += OnCountdown;
            _lobby.Notified += OnNotified;
            _lobby.SwapRequestReceived += OnSwapRequested;
        }

        private void OnDisable()
        {
            if (_lobby == null) return;

            _lobby.RosterChanged -= Rebuild;
            _lobby.StateChanged -= OnStateChanged;
            _lobby.CountdownChanged -= OnCountdown;
            _lobby.Notified -= OnNotified;
            _lobby.SwapRequestReceived -= OnSwapRequested;
        }

        private void Update()
        {
            if (_lobby == null) _lobby = LobbyManager.Instance;
            if (_lobby == null) return;

            if (!TreeIsLive)
            {
                Build();
                if (!TreeIsLive) return;
            }

            UpdateNotice();
        }

        // ============================================================ build

        /// <summary>
        /// Clones the tree. Asked for by liveness rather than by a "built" flag, because a domain
        /// reload during play keeps a bool and wipes every VisualElement reference — the match HUD
        /// learned that the hard way.
        /// </summary>
        private void Build()
        {
            if (!EnsureAssets()) return;

            VisualElement documentRoot = _document.rootVisualElement;
            if (documentRoot == null) return;

            documentRoot.Clear();
            _uxml.CloneTree(documentRoot);

            _root = documentRoot.Q<VisualElement>("lobby-root");
            if (_root == null) return;

            _title = _root.Q<Label>("room-title");
            _notice = _root.Q<Label>("notice");
            _countdown = _root.Q<Label>("countdown");
            _countdownCaption = _root.Q<Label>("countdown-caption");
            _roster = _root.Q<VisualElement>("roster");
            _rosterCount = _root.Q<Label>("roster-count");
            _customization = _root.Q<VisualElement>("customization");
            _hint = _root.Q<Label>("hint");
            _swapPrompt = _root.Q<VisualElement>("swap-prompt");
            _swapText = _root.Q<Label>("swap-text");

            _readyButton = _root.Q<Button>("ready-button");
            _swapAccept = _root.Q<Button>("swap-accept");
            _swapDecline = _root.Q<Button>("swap-decline");

            _readyButton.clicked += ToggleReady;
            _swapAccept.clicked += () => ResolveSwap(true);
            _swapDecline.clicked += () => ResolveSwap(false);

            // Pointer tracking, so a click on a button is not *also* read as a click on the stand
            // behind it. Cheaper and more reliable than picking the panel from a screen position.
            _root.RegisterCallback<PointerOverEvent>(_ => PointerOverUi = true);
            _root.RegisterCallback<PointerOutEvent>(_ => PointerOverUi = false);
            foreach (VisualElement interactive in new[]
                     { (VisualElement)_readyButton, _swapPrompt, _root.Q<VisualElement>("customization-panel") })
            {
                if (interactive == null) continue;
                interactive.RegisterCallback<PointerEnterEvent>(_ => PointerOverUi = true);
                interactive.RegisterCallback<PointerLeaveEvent>(_ => PointerOverUi = false);
            }

            BuildCharacterPicker();
            Rebuild();
            OnStateChanged(_lobby.State);
            OnCountdown(_lobby.Countdown);
        }

        /// <summary>
        /// The eight characters, straight from the catalog. One row of tiles; a tile shows the
        /// character's own colours so the list reads as the people in front of you rather than as
        /// eight words.
        /// </summary>
        private void BuildCharacterPicker()
        {
            _customization.Clear();

            var row = new VisualElement();
            row.AddToClassList("character-grid");
            row.name = "character-grid";
            _customization.Add(row);

            foreach (LobbyCharacterCatalog.Character character in LobbyCharacterCatalog.All)
            {
                string id = character.id;

                var tile = new Button(() => _lobby.RequestCharacter(id));
                tile.AddToClassList("character");
                tile.name = "character-" + id;
                tile.style.backgroundColor = character.suit;

                var label = new Label(character.label);
                label.AddToClassList("character__label");
                label.pickingMode = PickingMode.Ignore;
                tile.Add(label);

                var owner = new Label(string.Empty);
                owner.AddToClassList("character__owner");
                owner.name = "owner-" + id;
                owner.pickingMode = PickingMode.Ignore;
                tile.Add(owner);

                row.Add(tile);
            }
        }

        /// <summary>
        /// Marks who has what. A character somebody else is wearing is shown as theirs and refuses
        /// the click — the authority would reject it anyway, but a button that visibly cannot be
        /// pressed is better than one that silently does nothing.
        /// </summary>
        private void MarkCharacters(LobbyPlayer local)
        {
            foreach (LobbyCharacterCatalog.Character character in LobbyCharacterCatalog.All)
            {
                VisualElement tile = _root.Q<VisualElement>("character-" + character.id);
                var owner = _root.Q<Label>("owner-" + character.id);
                if (tile == null) continue;

                LobbyPlayer wearer = _lobby.WearerOf(character.id);
                bool mine = wearer != null && local != null && wearer.id == local.id;
                bool taken = wearer != null && !mine;

                tile.EnableInClassList("character--mine", mine);
                tile.EnableInClassList("character--taken", taken);
                tile.SetEnabled(!taken);

                if (owner != null)
                    owner.text = mine ? "YOU" : taken ? wearer.displayName : string.Empty;
            }
        }

        // ============================================================ state

        private void Rebuild()
        {
            if (!TreeIsLive) return;

            _roster.Clear();
            IReadOnlyList<LobbyPlayer> players = _lobby.Players;

            for (int slot = 0; slot < _lobby.Config.maxPlayers; slot++)
            {
                LobbyPlayer player = _lobby.Occupant(slot);
                if (player == null) continue;

                var row = new VisualElement();
                row.AddToClassList("roster-row");

                var name = new Label((slot + 1).ToString("00") + "  " + player.displayName);
                name.AddToClassList("roster-row__name");
                if (player.isLocal) name.AddToClassList("roster-row__name--local");

                var state = new Label(player.isReady ? "READY" : "WAITING");
                state.AddToClassList("roster-row__state");
                if (player.isReady) state.AddToClassList("roster-row__state--ready");

                row.Add(name);
                row.Add(state);
                _roster.Add(row);
            }

            _rosterCount.text = players.Count + " / " + _lobby.Config.maxPlayers
                + "   ·   " + _lobby.ReadyCount() + " READY";

            LobbyPlayer local = _lobby.Local;
            _readyButton.text = local != null && local.isReady ? "UNREADY" : "READY";
            _readyButton.EnableInClassList("lobby-button--on", local != null && local.isReady);

            MarkCharacters(local);
        }

        private void OnStateChanged(LobbyState state)
        {
            if (!TreeIsLive) return;

            bool locked = state == LobbyState.Locked || state == LobbyState.Starting;

            _readyButton.EnableInClassList("lobby-button--disabled", locked);
            _customization.SetEnabled(!locked);
            _hint.style.display = locked ? DisplayStyle.None : DisplayStyle.Flex;

            if (locked) HideSwapPrompt();

            _countdownCaption.text = state switch
            {
                LobbyState.CountingDown => "EVERYONE IS READY",
                LobbyState.Locked => "LOCKED IN",
                LobbyState.Starting => "GO",
                _ => string.Empty,
            };

            _title.text = state == LobbyState.Waiting
                ? "STAGING ROOM — SUBLEVEL 0"
                : "STAGING ROOM — DOOR OPENING";
        }

        private void OnCountdown(float remaining)
        {
            if (!TreeIsLive) return;

            bool running = _lobby.State == LobbyState.CountingDown || _lobby.State == LobbyState.Locked;
            _countdown.text = running ? Mathf.CeilToInt(remaining).ToString() : string.Empty;
            _countdown.EnableInClassList("lobby-countdown__value--locked", _lobby.State == LobbyState.Locked);
        }

        private void ToggleReady()
        {
            LobbyPlayer local = _lobby.Local;
            if (local == null) return;
            _lobby.RequestReady(!local.isReady);
        }

        // ============================================================ swap prompt

        private void OnSwapRequested(int requestId, int fromPlayerId)
        {
            if (!TreeIsLive) return;

            LobbyPlayer from = _lobby.Find(fromPlayerId);
            _pendingSwapId = requestId;
            _swapText.text = (from != null ? from.displayName : "SOMEONE") + " WANTS YOUR STAND";
            _swapPrompt.style.display = DisplayStyle.Flex;
        }

        private void ResolveSwap(bool accept)
        {
            if (_pendingSwapId < 0) return;
            _lobby.RespondToSwap(_pendingSwapId, accept);
            HideSwapPrompt();
        }

        private void HideSwapPrompt()
        {
            _pendingSwapId = -1;
            if (_swapPrompt != null) _swapPrompt.style.display = DisplayStyle.None;
        }

        // ============================================================ notices

        private void OnNotified(string message) => _notices.Add(message);

        private void UpdateNotice()
        {
            if (_notices.Count > 0 && _noticeTimer <= 0.7f)
            {
                _notice.text = _notices[0];
                _notices.RemoveAt(0);
                _noticeTimer = 2.2f;
            }

            if (_noticeTimer <= 0f) return;

            _noticeTimer -= Time.deltaTime;
            _notice.style.opacity = Mathf.Clamp01(_noticeTimer * 1.6f);
            if (_noticeTimer <= 0f) _notice.text = string.Empty;
        }
    }
}
