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

        /// 줄이 내려가는 중인가. 한 번 서면 이 씬이 끝날 때까지 내려가지 않는다.
        private bool _departing;

        /// 퇴장이 시작된 뒤 흐른 시간(초). 연출이 어긋나도 컷이 나게 하는 보증이다.
        private float _departureClock;
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

            // **나가는 문.** 라우터가 없으면 이 씬은 들어올 수는 있어도 나갈 수 없는 방이다 —
            // LEAVE 는 세션을 `Idle` 로 만들 뿐이고, 씬을 바꾸는 것은 라우터의 일이다.
            //
            // 붙이는 곳이 메인 로비뿐이던 동안, `GameLobby.unity` 를 열고 바로 Play 하면
            // 접속하지 않은 채 빈 스탠드만 서고 LEAVE 가 먹지 않았다. 대기방은 개발용 씬이
            // 아니라 로비 흐름의 일부이므로 스스로 붙이는 것이 맞다.
            SessionSceneRouter.EnsureOn(_session);

            // 게임 안내서(H). 매치가 시작되기 전이 초보자가 규칙을 물어볼 바로 그 순간이므로,
            // 대기방에도 매치 씬과 같은 오버레이를 세운다. 플레이어가 없는 씬이라 입력 인계는
            // 스스로 건너뛴다 — 붙이는 비용은 오브젝트 하나다.
            var guide = new GameObject("Guide Overlay");
            guide.transform.SetParent(transform, false);
            var overlay = guide.AddComponent<NV.Game.UI.GuideOverlayController>();

            // 처음 온 사람에게는 스스로 펼쳐진다. H 키를 아는 것도 규칙을 읽고 나서의 일이다 —
            // 한 번 직접 닫으면 그 뒤로는 구석 힌트만 남는다.
            overlay.autoOpenOnStart = true;

            // 로비는 서 있는 방이므로 포인터는 플레이어의 것이다.
            //
            // 이름을 다 적는다. `UnityEngine.UIElements` 에도 `Cursor` 가 있고, UI Toolkit 을
            // 쓰는 파일에서 짧게 쓰면 어느 것인지 컴파일러가 판단하지 못한다.
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
        }

        /// 되돌리기 요청을 이미 보냈다. 요청과 반영 사이의 몇 프레임 동안 다시 보내지
        /// 않기 위한 값이다.
        private bool _reopenSent;

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
            // `_session` 을 여기서 다시 잡지 않는다. `NetSession.Current` 는 없으면 만들므로
            // `Awake` 가 null 을 받는 길이 없고, 여기서 다시 잡는 가지는 **구독을 건너뛴다** —
            // `OnEnable` 은 이미 지나갔으므로 `StateChanged` 가 붙지 않은 채 필드만 채워지고,
            // 명단이 와도 줄이 그려지지 않는 방이 된다.

            // 매치가 시작됐으면 방을 다시 그릴 것이 없다. 줄을 내보내는 것이 남은 일이다.
            if (StepDeparture())
            {
                return;
            }

            ReopenFinishedRoom();

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

        // ==================================================== 퇴장

        /// <summary>
        /// 매치가 시작되는 순간의 방. 발밑의 판이 꺼지고, 줄에 선 사람들이 잠깐 허우적거리다
        /// 바닥 아래로 빨려 들어간 뒤에 씬이 바뀐다.
        ///
        /// **컷을 붙잡는 것이 이 함수의 절반이다.** 라우터는 세션이 `InGame` 이 되는 프레임에
        /// 씬을 바꾸므로(그것이 맞다 — 단계는 서버의 것이다) 그대로 두면 줄이 idle 자세인 채로
        /// 사라진다. `SceneTransitionHold` 로 잠깐만 미룬다. 그 값은 **시한**이라 이 컴포넌트가
        /// 죽어도 컷이 막히지 않는다.
        ///
        /// 미루는 시간은 역할 공개(`MatchConstants.RoleRevealDuration` 4초) 안에 들어간다.
        /// 그 동안 게임 씬에서는 역할만 보여 주고 있으므로 플레이 시간을 먹지 않는다.
        ///
        /// 실행 순서가 라우터(-70 대 0)보다 앞이라, 상태가 바뀐 **그 프레임에** 붙잡을 수 있다.
        /// </summary>
        /// <returns>퇴장 중이면 true — 부르는 쪽은 방을 더 그리지 않는다.</returns>
        private bool StepDeparture()
        {
            if (_session.State != SessionState.InGame)
            {
                return false;
            }

            if (!_departing)
            {
                _departing = true;
                _departureClock = 0f;

                for (var index = 0; index < _slots.Count; index++)
                {
                    if (_slots[index] != null)
                    {
                        _slots[index].BeginDeparture();
                    }
                }
            }

            _departureClock += Time.unscaledDeltaTime;

            // **끝나는 조건이 둘이다. 시계 쪽이 보증이다.**
            //
            // 인형이 다 내려갔는지 묻는 것만으로는 부족하다. 이 함수는 붙잡는 창을 매 프레임
            // 다시 채우므로, 어떤 인형 하나가 끝났다고 말하지 않으면 그 창은 영영 만료되지
            // 않는다 — `SceneTransitionHold` 의 시한은 이 컴포넌트가 죽는 경우를 막을 뿐,
            // 살아서 계속 붙잡는 경우는 막지 못한다. 그 결과는 매치는 돌아가는데 플레이어만
            // 대기방에 남는 것이고, 그것이 이 연출이 만들 수 있는 최악이다.
            //
            // 그래서 벽시계로도 끊는다. 연출이 어긋나면 컷이 조금 이를 뿐이다.
            var finished = DepartureFinished()
                || _departureClock >= LobbyMannequin.DepartureSeconds + 0.5f;

            if (finished)
            {
                // 다 내려갔다. 라우터가 다음 프레임에 컷한다.
                SceneTransitionHold.Release();
                return true;
            }

            // 한 프레임보다 조금 긴 창으로 매 프레임 다시 잡는다. 전체 길이를 한 번에 잡으면
            // 애니메이션이 일찍 끝나도 그만큼 검은 화면을 보게 된다.
            SceneTransitionHold.Hold(0.3f);
            return true;
        }

        private bool DepartureFinished()
        {
            for (var index = 0; index < _slots.Count; index++)
            {
                if (_slots[index] != null && !_slots[index].DepartureFinished)
                {
                    return false;
                }
            }

            return true;
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
        /// <summary>
        /// 끝난 매치를 대기 상태로 되돌린다. **대기방에 도착한 방장만 부른다.**
        ///
        /// 매치가 끝나면 방은 `Ended` 로 남고, 그것을 `Waiting` 으로 되돌리는 컨트롤을 보내는
        /// UI 는 **매치 씬의 ESC 메뉴 하나**였다. 결과를 닫고 여기로 걸어 나오면 그 문이 씬과
        /// 함께 사라진다 — 그리고 이 화면은 `Ended` 동안 <c>waiting</c> 이 거짓이라 START·
        /// READY·캐릭터 선택이 전부 빠지므로, 아무도 아무것도 할 수 없는 방이 된다. 서버도
        /// `Ended` 인 방의 준비 요청을 거절하므로 버튼을 보여 준다고 풀리지도 않는다.
        ///
        /// 그래서 조용히 되돌린다. 사람이 보는 것은 매치 전과 똑같은 대기방이고, 다시 READY
        /// 를 누르고 다시 시작하면 된다 — 그것이 "자연스럽게 다시 한 판" 이다.
        ///
        /// **한 번만 보낸다.** 요청과 반영 사이에 몇 프레임이 있고, 그동안 상태는 계속
        /// `Ended` 다 — 매 프레임 보내면 같은 요청이 수십 개 쌓인다.
        private void ReopenFinishedRoom()
        {
            if (_session == null || _session.State != SessionState.Ended)
            {
                _reopenSent = false;
                return;
            }

            // 방장만 되돌릴 수 있다(`Room.ReturnToLobby` 의 `IsAuthorized`). 나머지는
            // 방장이 도착할 때까지 기다린다.
            if (!_session.IsHost || _reopenSent)
            {
                return;
            }

            _reopenSent = true;
            _session.RequestReturnToLobby();
        }

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
