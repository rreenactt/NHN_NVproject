using System.Collections.Generic;
using NV.Client.Lobby.Models;
using NV.Client.Net.Session;
using NV.Lobby;
using UnityEngine;
using UnityEngine.UIElements;

namespace NV.Client.Lobby.GameLobby
{
    /// 서버 연동 대기방 씬의 유일한 오브젝트.
    ///
    /// 방과 스탠드 줄은 옛 로비의 것을 그대로 세운다(`LobbyRoom`·`LobbySlot`·
    /// `LobbyMannequin`). **그 셋은 판정이 아니라 표현이므로 서버가 생겨도 바뀔 이유가
    /// 없다** — 바뀐 것은 줄에 누가 서는지를 누가 정하는가이고, 그것은 이제 서버다.
    ///
    /// 옛 씬(`Lobby.unity`)과의 차이는 세 가지다.
    ///
    /// - `LobbyManager` 가 없다. 명단·준비·캐릭터는 서버의 `RoomState` 전문에서 읽는다.
    /// - 스탠드 수가 **서버가 알려 준 정원**이다. 옛 씬은 `LobbyConfig.maxPlayers`(6)를
    ///   썼고 그것은 서버 정원(8)의 어긋난 사본이었다.
    /// - 클릭은 자리 이동이 아니라 방장의 조작이다. 스탠드 번호가 곧 서버의 `PlayerId` 이고
    ///   그것이 스폰을 고르므로, 자리를 옮기는 것은 스폰을 옮기는 것과 같은 말이 된다.
    ///
    /// 트리의 생사를 여기서 한 번만 판정한다. `VisualElement` 는 스크립트 편집이 유발하는
    /// 도메인 리로드를 넘기지 못하고 컴포넌트는 넘기므로, 뷰마다 컴포넌트를 두면 살아 있는
    /// 컴포넌트가 죽은 요소를 가리키는 반쯤 살아 있는 객체가 여러 개 생긴다.
    [DefaultExecutionOrder(-70)]
    public sealed class GameLobbyBootstrap : MonoBehaviour
    {
        [Tooltip("스탠드 사이 거리(m). 옛 로비와 같은 값이다.")]
        public float slotSpacing = 1.35f;

        [Tooltip("서버가 정원을 알려 주기 전에 세울 스탠드 수. 서버 값이 오면 그것으로 다시 세운다.")]
        [Range(2, 16)] public int fallbackCapacity = 5;

        private readonly List<LobbySlot> _slots = new List<LobbySlot>();
        private readonly RoomMember[] _members = new RoomMember[16];

        private NetSession _session;
        private LobbyRoom _room;
        private GameLobbyHud _hud;
        private GameLobbyPicker _picker;
        private UIDocument _document;
        private VisualElement _root;

        private int _builtCapacity;

        /// 트리가 살아 있는가.
        ///
        /// bool 플래그를 두지 않는다. bool 은 도메인 리로드를 넘어 살아남고 `VisualElement`
        /// 는 넘지 못하므로, 전부 null 인 트리를 "빌드됨" 으로 오인해 화면이 빈 채로 남고
        /// 프레임마다 예외가 난다. 게임 HUD 와 메인 로비가 같은 이유로 같은 패턴을 쓴다.
        private bool HudIsLive => _root != null && _root.panel != null && _hud != null;

        private void Awake()
        {
            _session = NetSession.Current;

            // 로비는 서 있는 방이므로 포인터는 플레이어의 것이다.
            //
            // 이름을 다 적는다. `UnityEngine.UIElements` 에도 `Cursor` 가 있고, UI Toolkit 을
            // 쓰는 파일에서 짧게 쓰면 어느 것인지 컴파일러가 판단하지 못한다.
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
        }

        private void OnEnable()
        {
            if (_session != null)
            {
                _session.StateChanged += Refresh;
            }
        }

        private void OnDisable()
        {
            if (_session != null)
            {
                _session.StateChanged -= Refresh;
            }
        }

        private void Update()
        {
            if (_session == null)
            {
                _session = NetSession.Current;
                return;
            }

            // 정원은 참가 전 조회로 오므로 씬이 열린 뒤에 정해질 수 있다. 바뀌면 줄을 다시 세운다.
            var capacity = Capacity();
            var rebuilt = false;

            if (_builtCapacity != capacity)
            {
                BuildRow(capacity);
                rebuilt = true;
            }

            if (!HudIsLive)
            {
                BuildHud();
                return;
            }

            // 줄을 다시 세웠으면 빈 스탠드뿐이므로 한 번 그린다.
            if (rebuilt)
            {
                Refresh();
            }

            // **프레임마다 그리지 않는다.** 그릴 것이 생기는 시점은 명단 전문이 바뀔 때이고
            // 그것은 `NetSession.StateChanged` 로 온다(`NetworkClient` 가 항목을 비교해
            // 바뀌었을 때만 올린다).
            //
            // 처음에는 여기서 매 프레임 불렀고, 그것이 인형을 굳게 만들었다 —
            // `LobbySlot.Bind` 가 부르는 `ApplyCharacter` 는 멱등이 아니라서 idle 제스처를
            // 되돌리고 머리 장식을 다시 만든다. 지금은 그쪽도 멱등이지만, 명단 줄과 캐릭터
            // 칸을 프레임마다 다시 만드는 것 자체가 쓰레기를 만든다.
        }

        private int Capacity()
        {
            var capacity = _session.Room.Capacity;

            return capacity > 0 ? capacity : fallbackCapacity;
        }

        // ==================================================== 만들기

        /// 방과 스탠드 줄. **정원이 바뀌면 통째로 다시 만든다.**
        ///
        /// 줄 폭이 스탠드 수에서 나오고 방의 크기가 줄 폭에서 나오므로, 스탠드만 더하면
        /// 방이 그것을 담지 못한다.
        private void BuildRow(int capacity)
        {
            _builtCapacity = capacity;

            for (var index = 0; index < _slots.Count; index++)
            {
                if (_slots[index] != null)
                {
                    Destroy(_slots[index].gameObject);
                }
            }

            _slots.Clear();

            if (_room != null)
            {
                Destroy(_room.gameObject);
                _room = null;
            }

            var rowWidth = (capacity - 1) * slotSpacing;

            _room = LobbyRoom.Build(transform, rowWidth);

            var root = new GameObject("__Slots").transform;
            root.SetParent(transform, false);

            // 뒷벽에 붙은 곧은 줄, 가운데 정렬, 전부 카메라를 향한다. 곧고 고르게 벌어진
            // 것이 이 화면의 전부다 — 한눈에 셀 수 있는 줄.
            for (var index = 0; index < capacity; index++)
            {
                var x = -rowWidth * 0.5f + index * slotSpacing;

                LobbySlot slot = LobbySlot.Spawn(index, new Vector3(x, 0f, 2.2f), root);
                slot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // -Z, 카메라를 향해

                _slots.Add(slot);
            }

            if (_picker != null)
            {
                _picker.lobbyCamera = _room.Camera;
            }
        }

        private void BuildHud()
        {
            var uxml = Resources.Load<VisualTreeAsset>("UI/GameLobbyHUD");
            var panel = Resources.Load<PanelSettings>("UI/GameHudPanelSettings");

            if (uxml == null || panel == null)
            {
                Debug.LogError("[GameLobby] Assets/Resources/UI 에 GameLobbyHUD 또는 GameHudPanelSettings 이 없다.");
                enabled = false;
                return;
            }

            if (_document == null)
            {
                _document = GetComponent<UIDocument>() ?? gameObject.AddComponent<UIDocument>();
            }

            _document.panelSettings = panel;
            _document.visualTreeAsset = null;
            _document.rootVisualElement.Clear();

            _root = uxml.Instantiate();
            _root.style.flexGrow = 1f;
            _document.rootVisualElement.Add(_root);

            _hud = new GameLobbyHud(_root, _session)
            {
                OnToggleReady = ready => _session.SetReady(ready),
                OnStart = () => _session.RequestStart(),
                OnLeave = () => _session.Leave(),
                OnPickCharacter = id => _session.SetCharacter(id),
                OnKick = id => _session.KickPlayer(id),
                OnTransferHost = id => _session.TransferHost(id),
            };

            _hud.Reset();

            if (_picker == null)
            {
                _picker = gameObject.AddComponent<GameLobbyPicker>();
            }

            _picker.lobbyCamera = _room != null ? _room.Camera : null;
            _picker.Bind(_session, _hud, _slots);

            Refresh();
        }

        // ==================================================== 그리기

        /// 서버가 보낸 명단으로 줄과 HUD 를 맞춘다.
        ///
        /// **스탠드 번호는 `PlayerId` 다.** 서버가 슬롯을 그 번호로 예약하고 스폰도 그것으로
        /// 고르므로, 화면의 줄과 게임 안의 몸이 같은 번호를 쓴다 — 명단의 어느 줄이 누구인지
        /// 눈으로 맞출 수 있는 이유가 그것이다.
        private void Refresh()
        {
            if (!HudIsLive)
            {
                return;
            }

            var count = RoomMember.Collect(_session, _members);

            for (var index = 0; index < _slots.Count; index++)
            {
                var member = -1;

                for (var probe = 0; probe < count; probe++)
                {
                    if (_members[probe].PlayerId == index)
                    {
                        member = probe;
                        break;
                    }
                }

                if (member < 0)
                {
                    _slots[index].Clear();
                    continue;
                }

                _slots[index].Bind(
                    _members[member].CharacterId,
                    _members[member].IsReady,
                    _members[member].IsSelf);
            }

            _hud.Refresh();
        }
    }
}
