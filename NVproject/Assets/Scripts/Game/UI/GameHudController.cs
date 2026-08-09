using System.Collections.Generic;
using NV.Client.Map;
using NV.Client.Net.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace NV.Game.UI
{
    /// <summary>
    /// The match HUD: UI Toolkit, built from <c>Assets/Resources/UI/GameHUD.uxml</c> and styled by
    /// <c>game-hud.uss</c>, bound to the real <see cref="MatchManager"/> state — there is no mock
    /// anywhere in here.
    ///
    /// **Role gating is structural.** When the roles are handed out the tree is rebuilt from the
    /// UXML and the panel belonging to the other side is *removed from the hierarchy*, not hidden.
    /// A Runner's HUD therefore contains no ammo counter and a Seeker's contains no door marker and
    /// no key count — not off-screen, not transparent, not present. In an asymmetric game a hidden
    /// element is a leak waiting for a bug, and the door is the Runners' only secret.
    ///
    /// Everything reads from events plus a per-frame poll of the handful of values that change
    /// continuously (clock, ammo, prompt, compass). Nothing here decides anything about the match.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public sealed class GameHudController : MonoBehaviour
    {
        private const string UxmlPath = "UI/GameHUD";
        private const string PanelPath = "UI/GameHudPanelSettings";

        private UIDocument _document;
        private VisualTreeAsset _uxml;

        // --- shared
        private VisualElement _root, _scanlines, _vignette, _deadWash, _spectatorBar;
        private Label _spectatorName;
        private Label _clock, _roleLabel, _escapeLabel, _notice, _prompt;

        // 칩에 함께 그리는 값들. 탈출 수는 이벤트로, 진행도는 매 프레임 온다.
        private int _escapedCount;
        private int _escapesNeeded;
        private VisualElement _roleChip;

        // --- runner
        private VisualElement _runnerPanel, _keySlots, _healthPips, _effectList, _compass;
        private Label _carriedLabel, _bleedLabel, _teleportLabel, _compassArrow, _compassDistance;

        // --- seeker
        private VisualElement _seekerPanel, _shells, _destroyBlock, _destroyPips, _chainBanner;
        private Label _ammoLine, _chainLine, _trailLabel;

        // --- overlays and cards
        private VisualElement _mapOverlay, _mapImage, _feedOverlay, _feedImage, _freezeBanner;
        private Label _mapCaption, _feedCaption;
        private VisualElement _revealCard, _endCard;
        private Button _endExit;

        /// 이 매치의 결과를 사람이 이미 닫았다. 닫은 카드가 다시 올라오지 않게 하고,
        /// 씬을 붙잡는 것도 여기서 끝난다.
        private bool _resultClosed;

        /// 카드가 올라와 있다. 커서를 한 번만 푸는 데 쓴다.
        private bool _resultShown;
        private Label _revealRole, _revealFlavor, _revealCount, _endTitle, _endDetail;

        // --- state
        private MatchManager _match;
        private PlayerAgent _local;
        private PlayerInteractor _interactor;
        private WeaponController _weapon;
        private ChainDrag _chain;
        private Camera _viewCamera;

        private readonly List<string> _pendingNotices = new List<string>();
        private readonly List<ActiveEffect> _effects = new List<ActiveEffect>();
        /// 매치가 끝나고 결과 카드가 올라오기까지의 시간(초).
        private const float EndCardDelaySeconds = 1.6f;

        /// 쓰러진 뒤 화면에 얹히는 무채색 막의 진하기.
        ///
        /// "살짝" 이다 — 방이 무엇인지는 계속 보여야 한다. 색을 완전히 뽑으면 관전이 아니라
        /// 화면이 꺼진 것이 되고, 남은 사람들이 어디서 무엇을 하는지 볼 수 없다.
        private const float DeadWashOpacity = 0.45f;

        private float _endCardDelay;
        private float _noticeTimer;
        private float _mapTimer, _feedTimer, _freezeTimer;
        private int _scanlineHeight;
        private Texture2D _scanlineTexture, _vignetteTexture;

        /// <summary>
        /// Is there a live tree to write into? This is asked instead of keeping a "built" flag,
        /// and the difference is not stylistic.
        ///
        /// A domain reload during play preserves a private <c>bool</c> — it is a serializable
        /// field — but wipes every <see cref="VisualElement"/> reference, because those are plain
        /// managed objects. A flag therefore comes back saying "already built" while every element
        /// it was describing is null, and the HUD throws once per frame at an empty screen for the
        /// rest of the session. Checking the thing itself cannot lie: a wiped reference or a root
        /// the panel has since replaced both read as "not built", and the next frame rebuilds it.
        /// </summary>
        private bool TreeIsLive => _clock != null && _root != null && _root.panel != null;

        private MatchMapView _mapViewCache;
        private SeekerFeed _feedCache;

        /// 관전 카메라. 죽은 Runner 가 남의 눈을 빌리는 동안만 산다.
        private readonly MatchSpectator _spectator = new MatchSpectator();

        // Lazily created, never in a field initialiser: a domain reload during play wipes plain
        // managed objects without re-running Awake, and the HUD would then throw every frame.
        private MatchMapView MapView => _mapViewCache ??= new MatchMapView();
        private SeekerFeed Feed => _feedCache ??= new SeekerFeed();

        private struct ActiveEffect
        {
            public string label;
            public float remaining;
        }

        // ============================================================ setup

        private void Awake() => EnsureAssets();

        /// <summary>
        /// Finds the document and the UXML, re-loading them if they have gone.
        ///
        /// They *do* go: these are private fields, which Unity does not carry through the domain
        /// reload a script edit triggers mid-play, and Awake does not get a second turn. The
        /// symptom is not an exception — it is a HUD that silently stops rebuilding and leaves an
        /// empty screen for the rest of the session, which is exactly how it was found.
        /// </summary>
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
                Debug.LogError("[HUD] Missing " + UxmlPath + " or " + PanelPath +
                               " under Assets/Resources. Run Tools ▸ Backrooms ▸ Set Up Match.");
                return false;
            }

            // The tree is cloned by hand rather than assigned to the document, because the role
            // rebuild has to be able to throw half of it away and start again.
            _document.visualTreeAsset = null;
            return true;
        }

        private void OnEnable()
        {
            _match = MatchManager.Instance;
            if (_match != null)
            {
                _match.RolesAssigned += OnRolesAssigned;
                _match.KeysChanged += OnKeysChanged;
                _match.EscapesChanged += OnEscapesChanged;
                _match.PhaseChanged += OnPhaseChanged;
                _match.MatchEnded += OnMatchEnded;
                _match.AgentHit += OnAgentHit;
                _match.Notified += OnNotified;
            }
            if (DeviceSystem.Instance != null) DeviceSystem.Instance.EffectFired += OnEffectFired;
        }

        private void OnDisable()
        {
            if (_match != null)
            {
                _match.RolesAssigned -= OnRolesAssigned;
                _match.KeysChanged -= OnKeysChanged;
                _match.EscapesChanged -= OnEscapesChanged;
                _match.PhaseChanged -= OnPhaseChanged;
                _match.MatchEnded -= OnMatchEnded;
                _match.AgentHit -= OnAgentHit;
                _match.Notified -= OnNotified;
            }
            if (DeviceSystem.Instance != null) DeviceSystem.Instance.EffectFired -= OnEffectFired;

            Feed.Release();

            // 카메라를 돌려주지 않고 사라지면 로컬 카메라가 꺼진 채로 남는다 — 씬은 살아
            // 있고 화면만 검다.
            _spectator.Release();
        }

        private void OnDestroy()
        {
            MapView.Release();
            Feed.Release();
            _spectator.Release();
            if (_scanlineTexture != null) Destroy(_scanlineTexture);
            if (_vignetteTexture != null) Destroy(_vignetteTexture);
        }

        private void OnRolesAssigned() => Rebuild();

        /// <summary>
        /// Rebuilds the tree for the local player's side and deletes the other side's panel.
        /// Called on every role assignment, including the debug side-swap.
        /// </summary>
        private void Rebuild()
        {
            _match = MatchManager.Instance;
            _local = _match != null ? _match.LocalAgent : null;
            if (_local == null || !EnsureAssets()) return;

            _interactor = _local.GetComponent<PlayerInteractor>();
            _weapon = _local.GetComponent<WeaponController>();
            _chain = _local.GetComponent<ChainDrag>();
            _viewCamera = _local.head != null ? _local.head.GetComponent<Camera>() : Camera.main;

            VisualElement documentRoot = _document.rootVisualElement;
            if (documentRoot == null) return;   // the panel is not up yet; Update retries

            documentRoot.Clear();
            _uxml.CloneTree(documentRoot);

            _root = documentRoot.Q<VisualElement>("hud-root");
            _scanlines = _root.Q<VisualElement>("scanlines");
            _vignette = _root.Q<VisualElement>("vignette");
            _deadWash = _root.Q<VisualElement>("dead-wash");
            _spectatorBar = _root.Q<VisualElement>("spectator-bar");
            _spectatorName = _root.Q<Label>("spectator-name");

            _roleChip = _root.Q<VisualElement>("role-chip");
            _roleLabel = _root.Q<Label>("role-label");
            _clock = _root.Q<Label>("clock");
            _escapeLabel = _root.Q<Label>("escape-label");
            _notice = _root.Q<Label>("notice");
            _prompt = _root.Q<Label>("prompt");

            _runnerPanel = _root.Q<VisualElement>("runner-panel");
            _seekerPanel = _root.Q<VisualElement>("seeker-panel");

            bool seeker = _local.Role == Role.Seeker;

            // The structural gate. Everything role-exclusive lives inside one of these two panels
            // precisely so that this single line can make it not exist.
            if (seeker) _runnerPanel.RemoveFromHierarchy();
            else _seekerPanel.RemoveFromHierarchy();

            if (seeker) CacheSeeker(); else CacheRunner();

            _mapOverlay = _root.Q<VisualElement>("map-overlay");
            _mapImage = _root.Q<VisualElement>("map-image");
            _mapCaption = _root.Q<Label>("map-caption");
            _feedOverlay = _root.Q<VisualElement>("feed-overlay");
            _feedImage = _root.Q<VisualElement>("feed-image");
            _feedCaption = _root.Q<Label>("feed-caption");
            _freezeBanner = _root.Q<VisualElement>("freeze-banner");

            _revealCard = _root.Q<VisualElement>("reveal-card");
            _revealRole = _root.Q<Label>("reveal-role");
            _revealFlavor = _root.Q<Label>("reveal-flavor");
            _revealCount = _root.Q<Label>("reveal-count");
            _endCard = _root.Q<VisualElement>("end-card");
            _endExit = _root.Q<Button>("end-exit");
            if (_endExit != null) _endExit.clicked += LeaveResult;
            _endTitle = _root.Q<Label>("end-title");
            _endDetail = _root.Q<Label>("end-detail");

            _roleLabel.text = seeker ? "SEEKER" : "RUNNER";
            _roleChip.AddToClassList(seeker ? "chip--seeker" : "chip--runner");

            ApplyAtmosphereTextures();
            OnKeysChanged(_match.KeysInserted, _match.KeysRequired);
            OnEscapesChanged(_match.Escapes, _match.EscapesNeeded);

            _effects.Clear();
        }

        private void CacheRunner()
        {
            _keySlots = _root.Q<VisualElement>("key-slots");
            _carriedLabel = _root.Q<Label>("carried-label");
            _healthPips = _root.Q<VisualElement>("health-pips");
            _bleedLabel = _root.Q<Label>("bleed-label");
            _teleportLabel = _root.Q<Label>("teleport-label");
            _effectList = _root.Q<VisualElement>("effect-list");
            _compass = _root.Q<VisualElement>("door-compass");
            _compassArrow = _root.Q<Label>("compass-arrow");
            _compassDistance = _root.Q<Label>("compass-distance");

            Fill(_keySlots, _match.KeysRequired, "slot");
            Fill(_healthPips, _match.Config.runnerHitsToDie, "pip");
        }

        private void CacheSeeker()
        {
            _shells = _root.Q<VisualElement>("shells");
            _ammoLine = _root.Q<Label>("ammo-line");
            _trailLabel = _root.Q<Label>("trail-label");
            _destroyBlock = _root.Q<VisualElement>("destroy-block");
            _destroyPips = _root.Q<VisualElement>("destroy-pips");
            _chainBanner = _root.Q<VisualElement>("chain-banner");
            _chainLine = _root.Q<Label>("chain-line");

            Fill(_shells, _match.Config.seekerMagazine, "shell");
            Fill(_destroyPips, _match.Config.deviceDestroyHits, "pip");
        }

        private static void Fill(VisualElement row, int count, string className)
        {
            if (row == null) return;
            row.Clear();
            for (int i = 0; i < count; i++)
            {
                var cell = new VisualElement();
                cell.AddToClassList(className);
                cell.pickingMode = PickingMode.Ignore;
                row.Add(cell);
            }
        }

        // ============================================================ per frame

        private void Update()
        {
            if (!TreeIsLive || _match == null)
            {
                if (MatchManager.Instance != null && MatchManager.Instance.LocalAgent != null) Rebuild();
                if (!TreeIsLive || _match == null) return;
            }

            UpdateClock();
            UpdateDeadWash();
            UpdateSpectator();

            // 탈출 진행도는 매 틱 바뀌므로 이벤트가 아니라 여기서 그린다. 유지 시간이 0.8초라
            // 이벤트로 받으면 바가 두 번 튀고 끝난다.
            DrawEscapeChip();

            UpdatePrompt();
            UpdateNotice();
            UpdateCards();
            UpdateOverlays();

            if (_local == null) return;

            if (_local.Role == Role.Seeker) UpdateSeeker();
            else UpdateRunner();
        }

        /// <summary>
        /// 쓰러진 뒤 화면에서 색이 빠진다.
        ///
        /// **역할을 가리지 않고 공유 구간에서 돈다.** 상처 비네트는 Runner 패널 안에서 그려지는데,
        /// 그 패널은 역할에 따라 트리에서 아예 빠진다 — 여기 두면 그 갈림과 무관하게 산다.
        ///
        /// 탈출은 죽음이 아니므로 걸러지지 않는다. 나간 사람의 화면까지 회색으로 만들면 이긴
        /// 것과 진 것이 같은 그림이 된다.
        ///
        /// 0.8초에 걸쳐 든다. 즉시 갈아 끼우면 화면이 깜빡인 것으로 보이고, 쓰러진 순간의
        /// 마지막 장면을 덮어 버린다.
        /// </summary>
        private void UpdateDeadWash()
        {
            if (_deadWash == null) return;

            bool down = _local != null && !_local.Alive;

            _deadWash.style.opacity = Mathf.MoveTowards(
                _deadWash.resolvedStyle.opacity,
                down ? DeadWashOpacity : 0f,
                Time.deltaTime * (down ? DeadWashOpacity / 0.8f : 1.5f));
        }

        /// <summary>
        /// 관전 대상을 고르고 화면에 이름을 적는다.
        ///
        /// **공유 구간에서 돈다.** Runner 패널은 역할에 따라 트리에서 빠지므로 거기 두면
        /// 갈림에 걸리고, 무엇보다 관전은 카메라를 건드리는 일이라 트리보다 오래 산다.
        /// </summary>
        private void UpdateSpectator()
        {
            // **매치가 도는 동안만 관전한다.** 결과가 뜬 뒤에도 남의 눈에 붙어 있으면,
            // 나가기를 눌러야 하는 화면을 남의 어깨 너머로 보게 된다.
            bool playing = _match != null && _match.Phase == MatchPhase.Playing;

            _spectator.Tick(playing ? _local : null, _match != null ? _match.Agents : null, _viewCamera);

            if (_spectatorBar == null || _spectatorName == null) return;

            PlayerAgent target = _spectator.Target;
            bool watching = _spectator.Watching && target != null;

            _spectatorBar.style.display = watching ? DisplayStyle.Flex : DisplayStyle.None;

            if (watching) _spectatorName.text = target.displayName;
        }

        /// <summary>
        /// 관전 카메라를 대상에 붙인다. **원격 몸은 Update 에서 놓이므로** 여기서 따라가야
        /// 한 프레임 뒤처지지 않는다 — 뒤처지면 보고 있는 대상에 대해서만 화면이 떤다.
        /// </summary>
        private void LateUpdate() => _spectator.LateTick();

        /// 결과를 화면에 올리고, 사람이 닫을 때까지 씬을 붙잡기 시작한다.
        ///
        /// 입력을 UI 로 넘긴다. **커서를 직접 푸는 것으로는 안 된다.**
        ///
        /// `FirstPersonController.HandleCursor` 는 입력이 켜져 있는 동안 매 프레임 돌면서,
        /// 커서가 풀린 상태의 좌클릭을 보면 **커서를 다시 잠근다** — 화면 아무 데나 눌러
        /// 조작으로 돌아가는 동작이다. 그래서 커서만 풀어 두면 나가기를 누르려는 클릭이
        /// 버튼에 닿기 전에 그 재잠금에 쓰이고, 버튼은 영원히 눌리지 않는다.
        ///
        /// ESC 메뉴가 `InputEnabled` 를 내리는 것이 정확히 이 이유다. 같은 손잡이를 쓴다 —
        /// 커서가 풀리고, 컨트롤러의 커서·시선·이동 처리가 통째로 서고, 무기도 같은 값을
        /// 보므로 버튼을 누른 클릭이 총으로 새지 않는다.
        private void ShowResult()
        {
            _endCard.style.display = DisplayStyle.Flex;
            _resultShown = true;

            SetPlayerInput(false);
        }

        /// 나가기를 눌렀다. 붙잡음을 놓으면 다음 프레임에 라우터가 대기방으로 컷한다.
        /// 나가기를 눌렀다. 붙잡음을 놓으면 다음 프레임에 라우터가 대기방으로 컷한다.
        ///
        /// **입력을 되돌려주지 않는다.** 되돌리면 그 순간 커서가 다시 잠기는데, 지금 가는
        /// 곳은 버튼으로 된 대기방이다(ESC 메뉴의 `CloseVisualOnly` 가 같은 이유로 같은
        /// 선택을 한다). 다음 매치가 시작될 때 `OnPhaseChanged` 가 되돌린다.
        private void LeaveResult()
        {
            _resultClosed = true;
            _resultShown = false;
            _endCard.style.display = DisplayStyle.None;

            // **씬을 놓아 주는 것은 이 한 줄이다.** 라우터가 이 래치를 읽는다
            // (`SessionSceneRouter.ResultStillOnScreen`) — 붙잡기를 여기서 걸지 않는 이유는
            // 실행 순서에 있고, `MatchResultGate` 에 적혀 있다.
            MatchResultGate.Dismiss();
        }

        /// <summary>
        /// 술래가 이긴 매치의 끝에서, 아직 서 있는 Runner 들의 몸이 터진다.
        ///
        /// **`Kill` 이 아니라 연출이다.** `Kill` 은 매치 판정에 쓰이는 상태를 건드리고, 여기는
        /// 이미 끝난 매치다 — 그 상태를 지금 바꾸면 결과가 이미 나온 뒤에 명단이 흔들린다.
        /// 씨드가 이름에서 오므로 두 사람이 같은 파편을 본다.
        /// </summary>
        private void ShatterTheLost()
        {
            IReadOnlyList<PlayerAgent> agents = _match.Agents;

            for (int i = 0; i < agents.Count; i++)
            {
                PlayerAgent agent = agents[i];
                if (agent == null || agent.Role != Role.Runner) continue;

                // 이미 쓰러졌거나 빠져나간 몸은 건드리지 않는다. 탈출한 사람은 나갔으므로
                // 진 것이 아니고, 쓰러진 사람은 그때 이미 터졌다.
                if (!agent.InPlay) continue;

                agent.Shatter();
            }
        }

        private void UpdateClock()
        {
            float time = Mathf.Max(0f, _match.TimeRemaining);
            int minutes = Mathf.FloorToInt(time / 60f);
            int seconds = Mathf.FloorToInt(time % 60f);
            _clock.text = minutes.ToString("00") + ":" + seconds.ToString("00");

            // The clock is the Seeker's entire win condition, so the last half-minute flickers like
            // the tubes overhead rather than merely turning red.
            bool urgent = time <= 30f && _match.Phase == MatchPhase.Playing;
            if (urgent) _clock.AddToClassList("clock--urgent");
            else _clock.RemoveFromClassList("clock--urgent");

            _clock.style.opacity = urgent
                ? 0.72f + 0.28f * Mathf.PerlinNoise(Time.time * 11f, 0f)
                : 0.88f + 0.12f * Mathf.PerlinNoise(Time.time * 1.7f, 4f);
        }

        private void UpdatePrompt()
        {
            _prompt.text = _interactor != null && _interactor.Prompt != null ? _interactor.Prompt : string.Empty;
        }

        private void OnNotified(string message) => _pendingNotices.Add(message);

        private void UpdateNotice()
        {
            if (_pendingNotices.Count > 0 && _noticeTimer <= 0.7f)
            {
                _notice.text = _pendingNotices[0];
                _pendingNotices.RemoveAt(0);
                _noticeTimer = 2.4f;
            }

            if (_noticeTimer <= 0f) return;

            _noticeTimer -= Time.deltaTime;
            _notice.style.opacity = Mathf.Clamp01(_noticeTimer * 1.6f);
            if (_noticeTimer <= 0f) _notice.text = string.Empty;
        }

        // ============================================================ runner HUD

        private void UpdateRunner()
        {
            GameConfig config = _match.Config;

            // Key slots: inserted are solid, the ones in your hands are outlined. A Runner has to
            // be able to tell "we are three keys along" from "I am carrying three keys" at a glance,
            // because only one of those survives being shot.
            int inserted = _match.KeysInserted;
            int carried = _local.CarriedKeys;
            for (int i = 0; i < _keySlots.childCount; i++)
            {
                VisualElement slot = _keySlots[i];
                bool isInserted = i < inserted;
                bool isCarried = !isInserted && i < inserted + carried;

                slot.EnableInClassList("slot--filled", isInserted);
                slot.EnableInClassList("slot--carried", isCarried);
            }
            _carriedLabel.text = carried > 0 ? "CARRYING " + carried : "CARRYING NOTHING";

            for (int i = 0; i < _healthPips.childCount; i++)
            {
                bool lost = i >= config.runnerHitsToDie - _local.Hits;
                _healthPips[i].EnableInClassList("pip--full", !lost);
                _healthPips[i].EnableInClassList("pip--hurt", lost);
            }

            _bleedLabel.text = _local.Bleeding ? "BLEEDING — YOU ARE LEAVING A TRAIL" : string.Empty;

            float wound = _local.Bleeding ? 0.30f + 0.12f * Mathf.Sin(Time.time * 3.4f) : 0f;
            _vignette.style.opacity = Mathf.MoveTowards(_vignette.resolvedStyle.opacity, wound, Time.deltaTime * 0.9f);

            float teleportCd = DeviceSystem.Instance != null ? DeviceSystem.Instance.TeleportCooldownRemaining : 0f;
            _teleportLabel.text = teleportCd > 0f
                ? "TELEPORT LOCKED " + teleportCd.ToString("0.0") + "s"
                : "TELEPORT READY";
            _teleportLabel.EnableInClassList("panel__line--alert", teleportCd > 0f);

            UpdateEffectList();
            UpdateCompass();
        }

        private void UpdateEffectList()
        {
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                ActiveEffect effect = _effects[i];
                effect.remaining -= Time.deltaTime;
                if (effect.remaining <= 0f) _effects.RemoveAt(i);
                else _effects[i] = effect;
            }

            _effectList.Clear();
            for (int i = 0; i < _effects.Count; i++)
            {
                var label = new Label(_effects[i].label + "  " + _effects[i].remaining.ToString("0.0") + "s");
                label.AddToClassList("effect");
                label.pickingMode = PickingMode.Ignore;
                _effectList.Add(label);
            }
        }

        /// <summary>
        /// The door marker. It sits on a ring around the screen centre and always points at the
        /// door, on-screen or behind you — a marker that only appears when the door is already in
        /// view would be useless in a maze where you cannot see ten metres.
        /// </summary>
        private void UpdateCompass()
        {
            EscapeDoor door = _match.Door;
            bool show = _match.Config.showDoorCompass
                        && door != null && _viewCamera != null
                        && _match.Phase == MatchPhase.Playing && _local.InPlay;

            if (!show) { _compass.style.opacity = 0f; return; }

            Vector3 delta = door.Position - _viewCamera.transform.position;
            var flat = new Vector3(delta.x, 0f, delta.z);
            if (flat.sqrMagnitude < 0.01f) { _compass.style.opacity = 0f; return; }

            Vector3 forward = _viewCamera.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;

            float angle = Vector3.SignedAngle(forward.normalized, flat.normalized, Vector3.up);

            float width = _root.resolvedStyle.width;
            float height = _root.resolvedStyle.height;
            if (width <= 1f || height <= 1f) return;

            float radius = Mathf.Min(width, height) * 0.32f;
            float radians = angle * Mathf.Deg2Rad;

            float x = width * 0.5f + Mathf.Sin(radians) * radius;
            float y = height * 0.5f - Mathf.Cos(radians) * radius;

            _compass.style.left = x - 37f;
            _compass.style.top = y - 37f;
            _compass.style.opacity = 0.9f;
            _compassArrow.style.rotate = new StyleRotate(new Rotate(angle));

            // Storey arrow by storey *index*, not by metres of height difference. The door's origin
            // sits on its floor while the eye is 1.62 m up, so a raw comparison called every
            // same-floor door "one storey down".
            string storey = string.Empty;
            ILevelQuery map = _match.Map;
            if (map != null)
            {
                int mine = map.FloorIndexAt(_local.FeetPosition.y);
                int theirs = map.FloorIndexAt(door.Position.y);
                if (theirs > mine) storey = " ↑";
                else if (theirs < mine) storey = " ↓";
            }

            _compassDistance.text = Mathf.RoundToInt(flat.magnitude) + "m" + storey;
        }

        // ============================================================ seeker HUD

        private void UpdateSeeker()
        {
            int ammo = _weapon != null ? _weapon.Ammo : 0;
            for (int i = 0; i < _shells.childCount; i++)
                _shells[i].EnableInClassList("shell--loaded", i < ammo);

            bool chained = _chain != null && _chain.Active;
            _chainBanner.style.opacity = chained ? 1f : 0f;
            if (chained)
                _chainLine.text = "HELD " + _chain.Remaining.ToString("0.0") + "s  ·  RELOAD AFTER";

            _ammoLine.text = chained
                ? "DRAGGED"
                : _weapon != null && _weapon.IsReloading ? "RELOADING" : string.Empty;

            // The trail sense is world-space VFX; this only confirms it is on, and pulses so it
            // does not read as a dead label.
            _trailLabel.style.opacity = 0.62f + 0.38f * (0.5f + 0.5f * Mathf.Sin(Time.time * 1.6f));

            UpdateDestroyMeter();
        }

        /// <summary>
        /// Four pips of device integrity, shown only while the Seeker is actually looking at a
        /// device — this is the one piece of counter-play against the whole device system and it
        /// needs to be legible mid-fight.
        /// </summary>
        private void UpdateDestroyMeter()
        {
            MapDevice aimed = null;

            if (_viewCamera != null
                && Physics.Raycast(_viewCamera.transform.position, _viewCamera.transform.forward,
                                   out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore))
            {
                aimed = hit.collider.GetComponentInParent<MapDevice>();
            }

            if (aimed == null || aimed.Destroyed)
            {
                _destroyBlock.style.opacity = Mathf.MoveTowards(
                    _destroyBlock.resolvedStyle.opacity, 0f, Time.deltaTime * 4f);
                return;
            }

            _destroyBlock.style.opacity = 1f;
            int needed = _match.Config.deviceDestroyHits;
            for (int i = 0; i < _destroyPips.childCount; i++)
            {
                bool gone = i < aimed.ShotsTaken;
                _destroyPips[i].EnableInClassList("pip--hurt", gone);
                _destroyPips[i].EnableInClassList("pip--full", !gone && i < needed);
            }
        }

        // ============================================================ overlays

        private void OnEffectFired(DeviceType type, PlayerAgent user, float duration)
        {
            if (user == null || !user.isLocalPlayer) return;

            _effects.Add(new ActiveEffect
            {
                label = MapDevice.Label(type),
                remaining = duration > 0f ? duration : 2f,
            });

            switch (type)
            {
                case DeviceType.FullMapView:
                    _mapTimer = duration;
                    if (MapView.EnsureBuilt(_match.Map))
                    {
                        _mapImage.style.backgroundImage = new StyleBackground(MapView.Texture);
                        _mapOverlay.style.display = DisplayStyle.Flex;
                    }
                    break;

                case DeviceType.SeekerCameraView:
                    _feedTimer = duration;
                    Feed.Ensure(transform);
                    _feedImage.style.backgroundImage =
                        new StyleBackground(Background.FromRenderTexture(Feed.Target));
                    _feedOverlay.style.display = DisplayStyle.Flex;
                    break;

                case DeviceType.FreezeAndXray:
                    _freezeTimer = duration;
                    break;
            }
        }

        private void UpdateOverlays()
        {
            if (_mapTimer > 0f)
            {
                _mapTimer -= Time.deltaTime;
                MapView.Refresh(_match, _local != null ? _local.Role : Role.Unassigned);
                _mapCaption.text = "SIGNAL HOLDS FOR " + _mapTimer.ToString("0.0") + "s";
                if (_mapTimer <= 0f) _mapOverlay.style.display = DisplayStyle.None;
            }

            if (_feedTimer > 0f)
            {
                _feedTimer -= Time.deltaTime;
                bool live = Feed.Follow(_match.Seeker);
                _feedCaption.text = live
                    ? "SIGNAL HOLDS FOR " + _feedTimer.ToString("0.0") + "s"
                    : "NO SIGNAL";

                if (_feedTimer <= 0f)
                {
                    _feedOverlay.style.display = DisplayStyle.None;
                    Feed.Release();
                }
            }

            // The freeze banner fades in and out rather than blinking on: it is meant to be
            // disorienting, and a hard cut reads as a bug in a game where the walls just vanished.
            if (_freezeTimer > 0f) _freezeTimer -= Time.deltaTime;
            float freezeTarget = _freezeTimer > 0f ? 1f : 0f;
            _freezeBanner.style.opacity = Mathf.MoveTowards(
                _freezeBanner.resolvedStyle.opacity, freezeTarget, Time.deltaTime * 2.2f);
        }

        // ============================================================ cards

        private void OnPhaseChanged(MatchPhase phase)
        {
            if (!TreeIsLive) return;

            _revealCard.style.display = phase == MatchPhase.RoleReveal ? DisplayStyle.Flex : DisplayStyle.None;
            if (phase != MatchPhase.Ended)
            {
                _endCard.style.display = DisplayStyle.None;

                // 방이 결과를 떠났다. 아직 읽고 있었더라도 놓아 준다 — 여기서 계속 붙잡으면
                // 방은 다음 매치를 시작했는데 이 클라이언트만 지난 결과 화면에 남는다.
                _resultClosed = false;
                _resultShown = false;
            }

            // 매치가 시작되면 조작을 되돌려준다. 결과 화면이 내려놓은 것을 되돌리는 자리다 —
            // 없으면 한 번 결과를 본 사람은 다음 매치를 **움직일 수 없는 채로** 시작한다.
            if (phase == MatchPhase.RoleReveal || phase == MatchPhase.Playing)
            {
                SetPlayerInput(true);
            }

            if (phase != MatchPhase.RoleReveal) return;

            bool seeker = _local != null && _local.Role == Role.Seeker;
            _revealRole.text = seeker ? "SEEKER" : "RUNNER";
            _revealRole.EnableInClassList("card__title--seeker", seeker);
            _revealRole.EnableInClassList("card__title--runner", !seeker);

            _revealFlavor.text = seeker
                ? "THREE ROUNDS. NO MORE. SPEND THE THIRD AND THE CHAIN COMES FOR YOU."
                : "TEN KEYS. ONE DOOR. IT IS NOT WHERE YOU LEFT IT LAST TIME.";
        }

        private void UpdateCards()
        {
            // **결과 카드는 게시지 알림이 아니다.** `MatchEnded` 한 번에 기대면 그 순간
            // 트리가 살아 있지 않은 클라이언트는 카드를 영영 못 본다 — 그리고 그것이 곧
            // "승패 없이 로비로" 다. 단계에서 유도하면 늦게 올라온 트리도 따라잡는다.
            //
            // 씬을 붙잡는 것은 여기가 아니다. 라우터가 `MatchManager.Phase` 를 직접 읽어
            // 스스로 멈춘다(`SessionSceneRouter.ResultStillOnScreen`) — 이 컴포넌트가 세우는
            // 무엇이든 라우터보다 늦게 세워지기 때문이다(실행 순서 60 대 0).
            if (_match.Phase == MatchPhase.Ended && !_resultClosed)
            {
                // 결과 카드는 끝난 뒤 한 박자 뒤에 올라온다. 그 사이에 마지막 장면이 보인다.
                if (_endCardDelay > 0f)
                {
                    _endCardDelay -= Time.deltaTime;
                }
                else
                {
                    if (!_resultShown)
                    {
                        RenderResult(_match.Outcome);
                        ShowResult();
                    }

                    // 카드를 매 프레임 다시 세운다. 역할 재배정 등으로 트리가 다시 그려지면
                    // 카드가 사라지고, 그때 사람은 **누를 것이 없는 채로** 붙잡힌 방에 남는다.
                    _endCard.style.display = DisplayStyle.Flex;
                }
            }

            if (_match.Phase != MatchPhase.RoleReveal) return;
            _revealCount.text = Mathf.CeilToInt(_match.RevealRemaining) + "…";
        }

        private void OnMatchEnded(MatchOutcome outcome)
        {
            // **한 박자 두고 띄운다.** 매치가 끝나는 순간은 대개 화면에서 무슨 일이 벌어지는
            // 순간이다 — 마지막 한 명이 문으로 걸어 나가거나, 쓰러지거나, 시계가 0 이 된다.
            // 그 프레임에 카드가 덮으면 결과는 읽히는데 **왜 그렇게 됐는지를 못 본다.**
            //
            // 트리가 살아 있지 않아도 여기까지는 온다. 카드를 그리는 것은 `UpdateCards` 이고,
            // 그쪽은 단계에서 유도하므로 이 지연이 그냥 지나가도 결과는 뜬다.
            _endCardDelay = EndCardDelaySeconds;
            _resultClosed = false;
            _resultShown = false;

            // 진 쪽의 몸이 터진다. 시간 초과는 아무도 쓰러지지 않은 채 끝나므로, 그것이
            // 없으면 화면에서 벌어지는 일이 하나도 없이 숫자만 0 이 된다.
            // 술래가 이긴 두 경우만이다. 중단은 승패가 아니고, 결과가 미정인 매치에서
            // 한쪽 몸만 터뜨리면 화면이 아직 나오지도 않은 판정을 먼저 말한다.
            if (outcome == MatchOutcome.SeekerTimeout || outcome == MatchOutcome.SeekerWipedRunners)
            {
                ShatterTheLost();
            }

            if (!TreeIsLive) return;

            RenderResult(outcome);
        }

        /// 로컬 플레이어의 조작을 켜고 끈다. ESC 메뉴와 같은 손잡이다.
        ///
        /// **ESC 메뉴가 열려 있으면 되돌려주지 않는다.** 결과 화면 위에서 ESC 를 눌렀다가
        /// 다음 매치가 시작되면, 이 함수가 메뉴 뒤에서 조작을 켜서 커서를 잠가 버린다 —
        /// 메뉴는 떠 있는데 아무것도 누를 수 없는 상태가 된다.
        private void SetPlayerInput(bool enabled)
        {
            if (enabled && EscapeMenuController.AnyOpen) return;

            // `_local` 은 `Rebuild` 가 채운다. 아직 비어 있으면 명단에서 직접 찾는다 —
            // 여기서 조용히 넘어가면 조작이 켜진 채로 결과 화면이 뜨고, 그러면 나가기를
            // 누르려는 클릭이 컨트롤러의 커서 재잠금에 쓰여 버튼이 눌리지 않는다.
            PlayerAgent local = _local != null ? _local : MatchManager.Instance?.LocalAgent;

            if (local == null || local.controller == null) return;

            local.controller.InputEnabled = enabled;
        }

        /// 결과 카드의 글자와 색. **여러 번 불려도 같은 값을 쓴다** — 이벤트로도 오고
        /// 단계 폴링으로도 오기 때문이다.
        private void RenderResult(MatchOutcome outcome)
        {
            if (!TreeIsLive) return;

            bool seeker = _local != null && _local.Role == Role.Seeker;
            bool runnersWon = outcome == MatchOutcome.RunnersEscaped;
            bool localWon = seeker ? !runnersWon : runnersWon;

            // An abandoned match has no winner — counting a walkout as a victory would make
            // leaving a weapon, so neither the win nor the lose styling applies.
            bool aborted = outcome == MatchOutcome.Aborted;

            _endTitle.text = outcome switch
            {
                MatchOutcome.RunnersEscaped => "THEY GOT OUT",
                MatchOutcome.SeekerTimeout => "THE HOUR CLOSED",
                MatchOutcome.SeekerWipedRunners => "NOBODY LEFT TO RUN",
                MatchOutcome.Aborted => "MATCH ABANDONED",
                _ => "MATCH OVER",
            };

            _endDetail.text = outcome switch
            {
                MatchOutcome.RunnersEscaped =>
                    _match.Escapes + " RUNNERS REACHED THE DOOR. THE REST ARE STILL IN HERE.",
                MatchOutcome.SeekerTimeout =>
                    "THE CLOCK RAN OUT WITH " + _match.KeysInserted + " OF " +
                    _match.KeysRequired + " KEYS IN THE DOOR.",
                MatchOutcome.SeekerWipedRunners =>
                    "EVERY RUNNER WENT DOWN BEFORE TWO COULD LEAVE.",
                MatchOutcome.Aborted =>
                    "TOO FEW PLAYERS LEFT TO KEEP THE HUNT GOING. NOBODY WINS.",
                _ => string.Empty,
            };

            _endTitle.EnableInClassList("card__title--win", !aborted && localWon);
            _endTitle.EnableInClassList("card__title--lose", !aborted && !localWon);

            _revealCard.style.display = DisplayStyle.None;

            _mapOverlay.style.display = DisplayStyle.None;
            _feedOverlay.style.display = DisplayStyle.None;
            Feed.Release();
        }

        private void OnKeysChanged(int inserted, int required)
        {
            // Drawn in UpdateRunner. The Seeker's tree has no key slots at all, so there is
            // deliberately nothing to update here for them.
        }

        private void OnEscapesChanged(int escaped, int needed)
        {
            _escapedCount = escaped;
            _escapesNeeded = needed;
            DrawEscapeChip();
        }

        /// <summary>
        /// The escape chip: how many are out, and — while somebody is standing in the doorway —
        /// how far along they are.
        ///
        /// **Both roles see the progress.** The ruleset makes it public: the door's position stays
        /// hidden from the Seeker, but the hold exists so the last step can be interrupted, and a
        /// hold nobody can see is a delay rather than a rule. It is drawn on the chip that already
        /// carries the escape count, so it needed no new element and lands in a panel both roles
        /// already have.
        /// </summary>
        private void DrawEscapeChip()
        {
            if (_escapeLabel == null) return;

            var text = "ESCAPED " + _escapedCount + " / " + _escapesNeeded;

            float progress = _match != null ? _match.EscapeProgress : 0f;

            // 0 은 "아무도 안 하고 있다" 이므로 아예 적지 않는다. 늘 붙어 있는 0% 는 눈이
            // 걸러 내게 되고, 그러면 진짜로 누가 나갈 때도 함께 걸러진다.
            if (progress > 0.01f)
            {
                text += "  ·  ESCAPING " + Mathf.RoundToInt(progress * 100f) + "%";
            }

            _escapeLabel.text = text;
        }

        private void OnAgentHit(PlayerAgent victim, bool fatal)
        {
            if (!TreeIsLive || victim == null || !victim.isLocalPlayer) return;
            _vignette.style.opacity = fatal ? 0.85f : 0.55f;
        }

        // ============================================================ atmosphere textures

        /// <summary>
        /// Scanlines and vignette are generated, not authored. The scanline texture is rebuilt at
        /// the screen's height so the lines land one pixel apart instead of being stretched into a
        /// haze — the whole point is that it looks like a failing tube, not a grey filter.
        /// </summary>
        private void ApplyAtmosphereTextures()
        {
            int height = Mathf.Max(64, Screen.height);
            if (_scanlineTexture == null || _scanlineHeight != height)
            {
                if (_scanlineTexture != null) Destroy(_scanlineTexture);
                _scanlineHeight = height;
                _scanlineTexture = new Texture2D(4, height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    name = "Scanlines",
                };

                var pixels = new Color32[4 * height];
                for (int y = 0; y < height; y++)
                {
                    byte alpha = (byte)(y % 3 == 0 ? 190 : 0);
                    for (int x = 0; x < 4; x++) pixels[y * 4 + x] = new Color32(8, 7, 5, alpha);
                }
                _scanlineTexture.SetPixels32(pixels);
                _scanlineTexture.Apply(false);
            }

            if (_vignetteTexture == null) _vignetteTexture = BuildVignette();

            _scanlines.style.backgroundImage = new StyleBackground(_scanlineTexture);
            _vignette.style.backgroundImage = new StyleBackground(_vignetteTexture);
        }

        private static Texture2D BuildVignette()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Vignette" };
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)(size - 1) * 2f - 1f;
                float ny = y / (float)(size - 1) * 2f - 1f;
                float radius = Mathf.Sqrt(nx * nx + ny * ny) / 1.41421356f;

                // Clear in the middle, heavy at the corners, and eased so the edge of the effect
                // is never a visible ring.
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((radius - 0.35f) / 0.65f));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }
    }
}
