using System.Collections.Generic;
using NV.Client.Map;
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
        private VisualElement _root, _scanlines, _vignette, _deadWash;
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
        }

        private void OnDestroy()
        {
            MapView.Release();
            Feed.Release();
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
            _endTitle = _root.Q<Label>("end-title");
            _endDetail = _root.Q<Label>("end-detail");

            _roleLabel.text = seeker ? "SEEKER" : "RUNNER";
            _roleChip.AddToClassList(seeker ? "chip--seeker" : "chip--runner");

            ApplyAtmosphereTextures();
            OnKeysChanged(_match.KeysInserted, _match.Config.keysRequired);
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

            Fill(_keySlots, _match.Config.keysRequired, "slot");
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
            if (phase != MatchPhase.Ended) _endCard.style.display = DisplayStyle.None;

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
            // 결과 카드는 끝난 뒤 한 박자 뒤에 올라온다. 그 사이에 마지막 장면이 보인다.
            if (_endCardDelay > 0f)
            {
                _endCardDelay -= Time.deltaTime;
                if (_endCardDelay <= 0f) _endCard.style.display = DisplayStyle.Flex;
            }

            if (_match.Phase != MatchPhase.RoleReveal) return;
            _revealCount.text = Mathf.CeilToInt(_match.RevealRemaining) + "…";
        }

        private void OnMatchEnded(MatchOutcome outcome)
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
                    _match.Config.keysRequired + " KEYS IN THE DOOR.",
                MatchOutcome.SeekerWipedRunners =>
                    "EVERY RUNNER WENT DOWN BEFORE TWO COULD LEAVE.",
                MatchOutcome.Aborted =>
                    "TOO FEW PLAYERS LEFT TO KEEP THE HUNT GOING. NOBODY WINS.",
                _ => string.Empty,
            };

            _endTitle.EnableInClassList("card__title--win", !aborted && localWon);
            _endTitle.EnableInClassList("card__title--lose", !aborted && !localWon);

            // **한 박자 두고 띄운다.** 매치가 끝나는 순간은 대개 화면에서 무슨 일이 벌어지는
            // 순간이다 — 마지막 한 명이 문으로 걸어 나가거나, 쓰러지거나, 시계가 0 이 된다.
            // 그 프레임에 카드가 덮으면 결과는 읽히는데 **왜 그렇게 됐는지를 못 본다.**
            // 카드를 켜는 것은 `UpdateCards` 다 — 여기서 함께 켜면 지연이 없는 것과 같다.
            _endCardDelay = EndCardDelaySeconds;
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
