using NV.Client.Net;
using NV.Client.Net.Session;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace NV.Game.UI
{
    /// <summary>
    /// 게임 안내서 — 목적, 역할, 조작, 오브젝트, 진행을 언제든 다시 펼쳐 볼 수 있는 오버레이.
    /// H 로 열고 닫으며, 닫혀 있는 동안은 구석의 "H — 게임 방법" 한 줄만 남는다.
    ///
    /// 내용은 전부 <see cref="GuideCatalog"/>에 있고 이 컴포넌트는 목록을 그릴 뿐이다 —
    /// 새 규칙이 생기면 카탈로그에 줄을 더하지, 여기를 고치지 않는다.
    ///
    /// 구조는 <see cref="EscapeMenuController"/>를 그대로 따른다: 자기 <see cref="UIDocument"/>,
    /// 공용 패널 설정의 런타임 클론, HUD 위·ESC 메뉴 아래의 정렬 순서. 매치 씬에서는
    /// <see cref="MatchBootstrap"/>이, 대기방에서는 <c>GameLobbyBootstrap</c>이 만들어 주므로
    /// 어떤 씬도 이 오브젝트를 들고 다니지 않는다.
    /// </summary>
    [DefaultExecutionOrder(60)] // ESC 메뉴(70)보다 먼저 — 안내서가 닫은 ESC 를 메뉴가 다시 열지 않도록.
    public sealed class GuideOverlayController : MonoBehaviour
    {
        private const string UxmlPath = "UI/GuideOverlay";
        private const string PanelPath = "UI/GameHudPanelSettings";

        /// <summary>
        /// 한 번이라도 직접 닫아 본 사람인가. 처음 온 사람은 H 키의 존재조차 모르므로
        /// 안내서는 스스로 열려야 하고, 규칙을 아는 사람에게 매번 다시 여는 것은 방해다 —
        /// "직접 닫았다" 가 그 둘을 가르는 선이다. 환경별로 나누지 않는다: 규칙은 서버가
        /// 어디든 같다.
        /// </summary>
        private const string DismissedKey = "nv.guide.dismissed";

        /// <summary>HUD 위, ESC 메뉴(120) 아래.</summary>
        private const int SortingOrder = 110;

        private UIDocument _document;
        private PanelSettings _panel;
        private VisualElement _root;
        private Label _hint;
        private VisualElement _tabs;
        private ScrollView _content;
        private VisualElement _brief;
        private FirstPersonController _player;
        private MatchManager _match;
        private float _briefTimer;
        private bool _briefArmed;

        public bool IsOpen { get; private set; }

        /// <summary>ESC 메뉴가 커서를 되돌릴지 판단할 때 물어본다 — 참조 없이 물을 수 있게 정적.</summary>
        public static bool AnyOpen { get; private set; }

        /// <summary>
        /// 이번 프레임에 ESC 가 안내서를 닫는 데 쓰였는가. 같은 프레임에 ESC 메뉴의 Update 가
        /// 그 키를 또 읽고 메뉴를 열면, 안내서를 닫은 손이 메뉴를 연 셈이 된다 — 실행 순서상
        /// 이 컴포넌트가 먼저 닫고, 메뉴는 이 값을 보고 그 프레임을 건너뛴다.
        /// </summary>
        public static bool ConsumedEscThisFrame => _escConsumedFrame == Time.frameCount;

        private static int _escConsumedFrame = -1;

        /// 씬이 바뀌면 오버레이도 함께 사라지지만 정적 값은 남는다 — ESC 메뉴와 같은 이유.
        private void OnDisable() => AnyOpen = false;

        [Tooltip("이 씬에 들어오면 안내서를 저절로 편다 — 단, 한 번이라도 직접 닫아 본 사람에게는 " +
                 "구석 힌트만 남긴다. 대기방이 켠다: 매치가 시작되기 전이 규칙을 읽을 시간이다.")]
        public bool autoOpenOnStart;

        [Tooltip("매치 시작 브리핑이 떠 있는 시간(초). 역할이 정해진 직후, 입력을 잠그지 않고 " +
                 "왼쪽 가장자리에 요약만 보여 준다. 시야를 막지 않는 자리라 넉넉히 두어도 된다.")]
        public float briefSeconds = 15f;

        private void Start()
        {
            // ESC 메뉴와 달리 닫힌 상태에도 보여 줄 것(구석 힌트)이 있으므로 트리를 미리 세운다.
            EnsureTree();

            if (autoOpenOnStart && PlayerPrefs.GetInt(DismissedKey, 0) == 0) SetOpen(true);
        }

        private void Update()
        {
            UpdateBrief();

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.hKey.wasPressedThisFrame)
            {
                // ESC 메뉴가 위에 떠 있으면 열지 않는다 — 메뉴의 "게임 방법" 버튼이 그 길이다.
                if (!IsOpen && EscapeMenuController.AnyOpen) return;
                Toggle();
            }
            else if (IsOpen && keyboard.escapeKey.wasPressedThisFrame)
            {
                _escConsumedFrame = Time.frameCount;
                SetOpen(false);
            }
        }

        /// <summary>
        /// 매치 시작 브리핑. 게임 씬의 규칙대로 전문을 읽지, 알려 주기를 기다리지 않는다 —
        /// 페이즈가 Playing 이 되고 역할이 정해진 것을 매 프레임 확인해 한 번 무장하고,
        /// Playing 을 벗어나면 풀린다. 그래서 디버그 재시작(F1/F2)도 새 역할로 다시 뜬다.
        /// </summary>
        private void UpdateBrief()
        {
            if (_brief == null) return;

            var match = ResolveMatch();
            var phase = match != null ? match.Phase : MatchPhase.Lobby;

            if (phase != MatchPhase.Playing)
            {
                _briefArmed = false;
                _briefTimer = 0f;
            }
            else if (!_briefArmed)
            {
                var role = match.LocalAgent != null ? match.LocalAgent.Role : Role.Unassigned;
                var lines = GuideCatalog.BriefFor(role);

                // 역할이 아직 안 왔으면 다음 프레임에 다시 본다 — 무장은 역할과 함께다.
                if (lines.Length > 0)
                {
                    PopulateBrief(lines);
                    _briefArmed = true;
                    _briefTimer = briefSeconds;
                }
            }

            if (_briefTimer > 0f) _briefTimer -= Time.deltaTime;

            // 전체 안내서나 ESC 메뉴가 떠 있는 동안은 비킨다 — 같은 말이 두 겹일 이유가 없다.
            bool visible = _briefArmed && _briefTimer > 0f && !IsOpen && !EscapeMenuController.AnyOpen;
            _brief.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void PopulateBrief(string[] lines)
        {
            _brief.Clear();

            for (int i = 0; i < lines.Length; i++)
            {
                var label = new Label(lines[i]);
                label.AddToClassList(i == 0 ? "guide-brief-title" : "guide-brief-line");
                label.pickingMode = PickingMode.Ignore;
                _brief.Add(label);
            }
        }

        /// <summary>매치 층은 매치 씬에만 있다 — 없으면 없는 대로, 브리핑이 안 뜰 뿐이다.</summary>
        private MatchManager ResolveMatch()
        {
            if (_match != null) return _match;

            _match = FindFirstObjectByType<MatchManager>();
            return _match;
        }

        public void Toggle() => SetOpen(!IsOpen);

        /// <summary>안내서를 연다. 매치 중이면 자기 역할의 탭이 먼저 펼쳐진다.</summary>
        public void Open() => SetOpen(true);

        private void SetOpen(bool open)
        {
            if (!EnsureTree()) return;

            // 열려 있던 것을 닫는 것이 "읽었다" 는 신호다. 여는 쪽에 찍으면 자동으로 열린 채
            // 매치로 끌려간 사람 — 한 글자도 못 읽은 사람 — 이 아는 사람으로 기록된다.
            if (IsOpen && !open) PlayerPrefs.SetInt(DismissedKey, 1);

            IsOpen = open;
            AnyOpen = open;
            _root.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            _hint.style.display = open ? DisplayStyle.None : DisplayStyle.Flex;

            if (open) ShowTopic(InitialTopic());

            // 입력 인계는 ESC 메뉴와 같은 규칙. 결과 화면이 서 있거나 ESC 메뉴가 열려 있으면
            // 되돌리지 않는다 — 그 화면들도 UI 이고, UI 가 겹쳐 있을 뿐이다.
            var player = ResolvePlayer();
            if (player != null)
                player.InputEnabled = !open && !MatchResultGate.Standing && !EscapeMenuController.AnyOpen;
        }

        /// <summary>
        /// 처음 펼칠 탭. 역할은 열 때마다 다시 묻는다 — 매치 씬에서는 자기 역할, 대기방처럼
        /// 매치 층이 없는 씬에서는 소개 탭이 된다.
        /// </summary>
        private string InitialTopic()
        {
            var match = FindFirstObjectByType<MatchManager>();
            var role = match != null && match.LocalAgent != null ? match.LocalAgent.Role : Role.Unassigned;
            return GuideCatalog.TopicFor(role);
        }

        private bool EnsureTree()
        {
            // 살아 있는지는 묻는다, 플래그로 남기지 않는다 — 도메인 리로드는 bool 을 살리고
            // 트리를 죽인다 (ESC 메뉴와 같은 패턴).
            if (_root != null && _root.panel != null) return true;

            var uxml = Resources.Load<VisualTreeAsset>(UxmlPath);
            var basePanel = Resources.Load<PanelSettings>(PanelPath);

            if (uxml == null || basePanel == null)
            {
                Debug.LogError("[Guide] Resources/UI/GuideOverlay.uxml 또는 GameHudPanelSettings 가 없다.");
                return false;
            }

            if (_document == null)
            {
                _document = GetComponent<UIDocument>();
                if (_document == null) _document = gameObject.AddComponent<UIDocument>();
            }

            if (_panel == null)
            {
                _panel = Instantiate(basePanel);
                _panel.name = basePanel.name + " (guide)";
                _panel.sortingOrder = SortingOrder;
            }

            _document.panelSettings = _panel;
            _document.visualTreeAsset = null;

            var documentRoot = _document.rootVisualElement;
            documentRoot.Clear();
            uxml.CloneTree(documentRoot);

            // 닫힌 동안 이 문서가 화면을 가리면 안 된다 — 힌트도 읽는 것이지 누르는 것이 아니다.
            documentRoot.pickingMode = PickingMode.Ignore;

            _root = documentRoot.Q<VisualElement>("guide-root");
            _hint = documentRoot.Q<Label>("guide-hint");
            _tabs = documentRoot.Q<VisualElement>("guide-tabs");
            _content = documentRoot.Q<ScrollView>("guide-content");
            _brief = documentRoot.Q<VisualElement>("guide-brief");
            _hint.pickingMode = PickingMode.Ignore;
            _brief.pickingMode = PickingMode.Ignore;
            _brief.style.display = DisplayStyle.None;

            BuildTabs();

            _root.style.display = DisplayStyle.None;
            _hint.style.display = DisplayStyle.Flex;
            IsOpen = false;

            var close = documentRoot.Q<Button>("guide-close");
            if (close != null) close.clicked += () => SetOpen(false);

            return true;
        }

        /// <summary>탭 버튼 줄. 카탈로그의 주제 순서 그대로다.</summary>
        private void BuildTabs()
        {
            _tabs.Clear();

            foreach (var topic in GuideCatalog.Topics)
            {
                var id = topic.Id;
                var button = new Button(() => ShowTopic(id)) { text = topic.Title, name = "guide-tab-" + id };
                button.AddToClassList("guide-tab");
                _tabs.Add(button);
            }
        }

        private void ShowTopic(string id)
        {
            foreach (var child in _tabs.Children())
                child.EnableInClassList("guide-tab--active", child.name == "guide-tab-" + id);

            _content.Clear();

            foreach (var topic in GuideCatalog.Topics)
            {
                if (topic.Id != id) continue;

                foreach (var section in topic.Sections)
                {
                    var heading = new Label(section.Heading);
                    heading.AddToClassList("guide-heading");
                    _content.Add(heading);

                    foreach (var line in section.Lines)
                    {
                        var label = new Label(line);
                        label.AddToClassList("guide-line");
                        _content.Add(label);
                    }
                }

                break;
            }

            _content.scrollOffset = Vector2.zero;
        }

        /// <summary>ESC 메뉴와 같은 지연 해석 — 대기방처럼 플레이어가 없는 씬에서는 null 로 남는다.</summary>
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
