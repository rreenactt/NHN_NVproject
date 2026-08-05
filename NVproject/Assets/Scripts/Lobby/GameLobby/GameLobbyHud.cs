using System;
using System.Collections.Generic;
using NV.Client.Lobby.Models;
using NV.Client.Net.Session;
using NV.Shared.Contracts.Messages;
using UnityEngine;
using UnityEngine.UIElements;

namespace NV.Client.Lobby.GameLobby
{
    /// 서버 연동 대기방의 HUD. 방 정보, 명단, 캐릭터, 준비·시작·나가기.
    ///
    /// **상태를 들고 있지 않다.** 전부 `NetSession` 과 `NetworkClient` 에서 읽어 그린다.
    /// 화면이 자기 사본을 들면 서버가 보낸 명단과 화면의 명단이 어긋날 수 있고, 그 차이는
    /// 눈으로 잡을 수 없다.
    ///
    /// 트리의 생사를 판정하는 것은 이 클래스가 아니라 `GameLobbyBootstrap` 이다 —
    /// `VisualElement` 는 도메인 리로드를 넘기지 못하고 컴포넌트는 넘기므로, 살아 있는
    /// 컴포넌트가 죽은 요소를 가리키는 상태를 한 곳에서만 다룬다(게임 HUD 와 같은 규칙).
    public sealed class GameLobbyHud
    {
        private readonly NetSession _session;
        private readonly VisualElement _root;

        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly Label _code;
        private readonly Label _copyResult;
        private readonly Label _map;
        private readonly Label _visibility;
        private readonly Label _notice;
        private readonly VisualElement _roster;
        private readonly Label _rosterCount;
        private readonly Label _readyCount;
        private readonly VisualElement _characterList;
        private readonly Label _characterNote;
        private readonly Button _ready;
        private readonly Button _start;
        private readonly Button _leave;
        private readonly Button _copyLink;
        private readonly Label _hint;

        private readonly VisualElement _confirm;
        private readonly Label _confirmText;

        private readonly RoomMember[] _members = new RoomMember[SlotCapacity];
        private readonly List<CharacterItem> _characters = new List<CharacterItem>();

        private Action _pendingConfirm;

        /// 명단 버퍼 길이. 서버 정원(8)보다 넉넉하게 둔다 — 정원 상수는 서버의 것이고
        /// 클라이언트가 그것을 복제하지 않으므로, 늘어나도 여기서 잘리지 않아야 한다.
        private const int SlotCapacity = 16;

        public GameLobbyHud(VisualElement root, NetSession session)
        {
            _root = root;
            _session = session;

            _title = root.Q<Label>("room-title");
            _subtitle = root.Q<Label>("room-subtitle");
            _code = root.Q<Label>("invite-code");
            _copyResult = root.Q<Label>("invite-result");
            _map = root.Q<Label>("room-map");
            _visibility = root.Q<Label>("room-visibility");
            _notice = root.Q<Label>("notice");
            _roster = root.Q<VisualElement>("roster");
            _rosterCount = root.Q<Label>("roster-count");
            _readyCount = root.Q<Label>("ready-count");
            _characterList = root.Q<VisualElement>("character-list");
            _characterNote = root.Q<Label>("character-note");
            _ready = root.Q<Button>("ready-button");
            _start = root.Q<Button>("start-button");
            _leave = root.Q<Button>("leave-button");
            _copyLink = root.Q<Button>("copy-link");
            _hint = root.Q<Label>("hint");

            _confirm = root.Q<VisualElement>("confirm-prompt");
            _confirmText = root.Q<Label>("confirm-text");

            WireButtons(root);
            BuildCharacterGrid();
            HideConfirm();
        }

        /// 지금 포인터가 UI 위에 있는가. 스탠드 클릭이 버튼 클릭으로 새지 않게 한다.
        ///
        /// 옛 `LobbyHud` 와 같은 판정이다. 3D 를 클릭으로 집는 화면에서는 이것이 없으면
        /// 버튼을 눌렀는데 그 뒤의 사람도 함께 눌린다.
        public bool PointerOverUi { get; private set; }

        public Action<bool> OnToggleReady { get; set; }

        public Action OnStart { get; set; }

        public Action OnLeave { get; set; }

        public Action<byte> OnPickCharacter { get; set; }

        public Action<byte> OnKick { get; set; }

        public Action<byte> OnTransferHost { get; set; }

        // ==================================================== 갱신

        public void Refresh()
        {
            var waiting = _session.State == SessionState.InLobby;
            var count = RoomMember.Collect(_session, _members);

            RefreshRoom();
            RefreshRoster(count, waiting);
            RefreshCharacters(count, waiting);
            RefreshButtons(waiting);
        }

        private void RefreshRoom()
        {
            if (_code != null)
            {
                _code.text = InviteCodeText.ToDisplay(_session.Code);
            }

            if (_map != null)
            {
                _map.text = "MAP  " + (_session.Room.MapName ?? string.Empty).ToUpperInvariant();
            }

            // 공개인지 비공개인지 화면에 남긴다. 코드를 어디까지 흘려도 되는지가 그것으로
            // 갈리는데, 방에 들어온 사람은 자기가 어느 쪽에 있는지 알 방법이 이 줄밖에 없다.
            if (_visibility != null)
            {
                _visibility.text = _session.Room.IsPublic
                    ? "PUBLIC  ·  방 목록에 떠 있다"
                    : "PRIVATE  ·  코드를 아는 사람만 들어온다";
            }

            if (_subtitle != null)
            {
                _subtitle.text = SubtitleFor(_session.State);
            }

            // 링크를 만들 수 없는 플랫폼에서는 버튼을 숨긴다. 눌러 봐야 실패한다.
            if (_copyLink != null)
            {
                _copyLink.style.display = InviteLink.TryBuild(_session.Code, out _)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }
        }

        private static string SubtitleFor(SessionState state)
        {
            switch (state)
            {
                case SessionState.InGame: return "MATCH IN PROGRESS";
                case SessionState.Ended: return "MATCH OVER";
                default: return "WAIT HERE";
            }
        }

        /// 명단을 **정원 전체**로 그린다.
        ///
        /// 채워진 수만 보여주면 8칸 중 2칸인 방과 정원이 2인 방이 화면에서 같은 모습이 된다.
        /// 봇으로 최소 인원만 채운 개발용 방이 "봇이 하나뿐" 으로 읽히는 것이 그 증상이었다.
        private void RefreshRoster(int count, bool waiting)
        {
            if (_roster == null)
            {
                return;
            }

            _roster.Clear();

            for (var index = 0; index < count; index++)
            {
                _roster.Add(BuildRosterRow(_members[index], waiting));
            }

            var capacity = _session.Room.Capacity;
            var spare = capacity - count;
            var needed = Math.Max(0, _session.MinPlayers - count);

            for (var index = 0; index < spare; index++)
            {
                // 필요한 자리와 남는 자리는 다른 말이다. 같은 문구로 쓰면 시작할 수 없는
                // 이유가 명단에 있는데도 보이지 않는다.
                _roster.Add(BuildEmptyRow(index < needed));
            }

            if (_rosterCount != null)
            {
                _rosterCount.text = capacity > 0 ? count + " / " + capacity : count.ToString();
            }

            if (_readyCount != null)
            {
                var notReady = _session.NotReadyCount;

                _readyCount.style.display = waiting ? DisplayStyle.Flex : DisplayStyle.None;
                _readyCount.text = notReady > 0 ? notReady + "명 준비 안 함" : "전원 준비";
            }
        }

        private VisualElement BuildRosterRow(in RoomMember member, bool waiting)
        {
            var row = new VisualElement();
            row.AddToClassList("roster-row");

            var name = new Label(member.DisplayName);
            name.AddToClassList("roster-row__name");
            name.EnableInClassList("roster-row__name--local", member.IsSelf);
            row.Add(name);

            var character = new Label(LobbyCharacterCatalog.LabelOf(member.CharacterId));
            character.AddToClassList("roster-row__character");
            row.Add(character);

            var badge = new Label(BadgeFor(member));
            badge.AddToClassList("roster-row__badge");
            row.Add(badge);

            // 준비 상태는 대기 단계에서만 뜻이 있다. 매치 중에도 값은 참으로 남아 있으므로
            // (`ResetToWaiting` 이 지운다) 화면에서 가린다.
            var state = new Label(member.IsReady ? "READY" : string.Empty);
            state.AddToClassList("roster-row__state");
            state.EnableInClassList("roster-row__state--ready", member.IsReady);
            state.style.display = waiting ? DisplayStyle.Flex : DisplayStyle.None;
            row.Add(state);

            AddHostActions(row, member, waiting);

            return row;
        }

        private static string BadgeFor(in RoomMember member)
        {
            if (member.IsBot)
            {
                return member.IsHost ? "봇 · 방장" : "봇";
            }

            if (member.IsHost && member.IsSelf)
            {
                return "방장 · 나";
            }

            return member.IsHost ? "방장" : member.IsSelf ? "나" : string.Empty;
        }

        /// 방장에게만 붙는 줄별 버튼 — 위임과 강제 퇴장.
        ///
        /// 자기 줄에는 붙지 않는다. 자기를 내보내는 것은 나가기이고 그 버튼은 따로 있으며,
        /// 자기에게 방장을 넘기는 것은 아무 일도 아니다.
        ///
        /// 봇에게는 위임을 붙이지 않는다 — 서버가 거부한다(봇은 아무 요청도 보내지 않으므로
        /// 그 방은 시작할 수 없는 방이 된다). 눌러도 안 되는 버튼을 그리지 않는다.
        private void AddHostActions(VisualElement row, in RoomMember member, bool waiting)
        {
            if (!_session.IsHost || member.IsSelf || !waiting)
            {
                return;
            }

            var playerId = member.PlayerId;
            var name = member.DisplayName;

            var actions = new VisualElement();
            actions.AddToClassList("roster-row__actions");

            if (!member.IsBot)
            {
                var transfer = new Button(() => Confirm(
                    $"{name} 에게 방장을 넘긴다",
                    () => OnTransferHost?.Invoke(playerId)))
                {
                    text = "방장",
                };
                transfer.AddToClassList("lobby-button");
                actions.Add(transfer);
            }

            var kick = new Button(() => Confirm(
                $"{name} 을(를) 내보낸다",
                () => OnKick?.Invoke(playerId)))
            {
                text = "내보내기",
            };
            kick.AddToClassList("lobby-button");
            actions.Add(kick);

            row.Add(actions);
        }

        private static VisualElement BuildEmptyRow(bool needed)
        {
            var row = new VisualElement();
            row.AddToClassList("roster-row");
            row.AddToClassList("roster-row--empty");

            var label = new Label(needed ? "─ 한 명 더 필요 ─" : "─ 빈 자리 ─");
            label.AddToClassList("roster-row__name");
            row.Add(label);

            return row;
        }

        // ==================================================== 캐릭터

        /// 여덟 칸을 한 번만 만든다.
        ///
        /// 2Hz 로 다시 만들면 누르는 순간 요소가 교체되어 클릭이 사라지는 일이 생긴다.
        private void BuildCharacterGrid()
        {
            if (_characterList == null)
            {
                return;
            }

            _characterList.Clear();
            _characters.Clear();

            var grid = new VisualElement();
            grid.AddToClassList("character-grid");
            _characterList.Add(grid);

            for (var index = 0; index < LobbyCharacterCatalog.Count; index++)
            {
                LobbyCharacterCatalog.Character character = LobbyCharacterCatalog.All[index];
                var characterId = (byte)index;

                // `Button` 이다. 평범한 `VisualElement` + `PointerDownEvent` 로도 눌리지만,
                // 버튼은 이 프로젝트에서 이미 동작이 확인된 경로이고 눌림 상태(`:active`)를
                // 스타일시트가 그려 준다.
                var item = new Button(() => OnPickCharacter?.Invoke(characterId));
                item.AddToClassList("character");

                // **칸의 배경이 그 캐릭터의 색이다.** 이것이 칸의 내용 전부다 — `lobby.uss` 의
                // `.character__label` 은 글자색이 거의 검정이라 배경 없이는 읽히지 않는다.
                // 배경을 빼먹었더니 여덟 개의 빈 테두리가 되어, 골라도 아무 일도 없는 것처럼
                // 보였다.
                item.style.backgroundColor = character.suit;

                var label = new Label(character.label);
                label.AddToClassList("character__label");

                // 글자는 클릭을 먹지 않는다. 칸 자체가 눌린 것이 되어야 `:hover` 와 눌림
                // 상태가 칸에 걸린다.
                label.pickingMode = PickingMode.Ignore;
                item.Add(label);

                var owner = new Label(string.Empty);
                owner.AddToClassList("character__owner");
                owner.pickingMode = PickingMode.Ignore;
                item.Add(owner);

                grid.Add(item);

                // 요소를 들고 있는다. 갱신마다 `Q` 로 다시 찾으면 2Hz 로 여덟 번의 트리
                // 탐색이 돌고, 찾는 대상은 만든 순간부터 바뀌지 않는다.
                _characters.Add(new CharacterItem(item, owner));
            }
        }

        /// 누가 무엇을 입고 있는지 칸에 적는다.
        ///
        /// 이미 쓰이는 것은 **감추지 않고 흐리게 그린다.** 감추면 8종 중 몇 종이 있는지 알 수
        /// 없고, 남이 놓아 준 순간 목록이 늘어나는 것으로 보인다.
        private void RefreshCharacters(int count, bool waiting)
        {
            for (var index = 0; index < _characters.Count; index++)
            {
                CharacterItem item = _characters[index];

                var mine = false;
                var owner = string.Empty;

                for (var member = 0; member < count; member++)
                {
                    if (_members[member].CharacterId != index)
                    {
                        continue;
                    }

                    mine = _members[member].IsSelf;
                    owner = mine ? "YOU" : _members[member].DisplayName;
                    break;
                }

                var taken = owner.Length > 0 && !mine;

                item.Owner.text = owner;
                item.Root.EnableInClassList("character--mine", mine);
                item.Root.EnableInClassList("character--taken", taken);

                // **자기 것은 끄지 않는다.** 남이 입은 것만 끈다 — 자기 칸을 끄면 지금 입고
                // 있는 것이 "쓸 수 없는 것" 과 같은 모습(비활성 틴트)이 되어, 고른 결과가
                // 화면에서 실패처럼 읽힌다. 자기 것을 다시 누르는 것은 서버가 아무 일도
                // 하지 않고 넘긴다.
                item.Root.SetEnabled(waiting && !taken);
            }

            if (_characterNote != null)
            {
                _characterNote.text = waiting
                    ? "이미 쓰이는 캐릭터는 고를 수 없다."
                    : "매치 중에는 바꿀 수 없다.";
            }
        }

        // ==================================================== 조작

        /// **준비와 시작은 한 사람에게 동시에 보이지 않는다.**
        ///
        /// 방장에게는 시작 버튼이 준비이므로 준비 토글을 감추고, 방장이 아닌 사람에게는 시작
        /// 버튼을 감춘다 — 눌러도 서버가 거부하는 버튼은 고장으로 읽힌다.
        private void RefreshButtons(bool waiting)
        {
            var isHost = _session.IsHost;

            if (_start != null)
            {
                _start.style.display = isHost && waiting ? DisplayStyle.Flex : DisplayStyle.None;

                var canStart = _session.CanStart;
                _start.SetEnabled(canStart);
                _start.EnableInClassList("lobby-button--disabled", !canStart);
            }

            if (_ready != null)
            {
                _ready.style.display = !isHost && waiting ? DisplayStyle.Flex : DisplayStyle.None;

                var ready = _session.IsLocalReady;
                _ready.text = ready ? "CANCEL" : "READY";
                _ready.EnableInClassList("lobby-button--on", ready);
            }

            if (_hint != null)
            {
                _hint.text = Hint();
            }
        }

        /// 시작 버튼이 꺼져 있는 이유를 쓴다. 이유 없이 꺼진 버튼은 고장으로 읽힌다.
        private string Hint()
        {
            if (_session.State == SessionState.InGame)
            {
                return "매치 진행 중.";
            }

            if (_session.State == SessionState.Ended)
            {
                return "매치가 끝났다. 방장이 로비로 되돌릴 수 있다.";
            }

            if (!_session.IsHost)
            {
                if (HasNoHost())
                {
                    return "이 방에 방장이 없어 아무도 시작할 수 없다. 서버가 오래된 빌드일 수 있다.";
                }

                // 준비 여부에 따라 다음에 할 일이 다르다. 준비하지 않은 사람에게 "방장을
                // 기다린다" 만 쓰면 자기가 막고 있다는 사실이 화면에 없다.
                return _session.IsLocalReady
                    ? "방장이 시작하기를 기다린다."
                    : "READY 를 누르면 방장이 시작할 수 있다.";
            }

            var count = _session.Client != null ? _session.Client.RosterCount : 0;

            if (count < _session.MinPlayers)
            {
                return $"{_session.MinPlayers}명부터 시작할 수 있다. 지금 {count}명.";
            }

            var notReady = _session.NotReadyCount;

            return notReady > 0
                ? $"{notReady}명이 아직 준비하지 않았다."
                : "시작할 수 있다.";
        }

        /// 서버가 방장을 아무에게도 배정하지 않았는가.
        ///
        /// 명단 전문이 아직 오지 않은 동안은 거짓이다 — 그때는 "모른다" 이고, 모르는 것을
        /// "방장이 없다" 로 말하면 접속 직후 한순간 잘못된 문구가 뜬다.
        private bool HasNoHost()
        {
            var client = _session.Client;

            return client != null
                && client.HasRoomState
                && client.RoomState.HostPlayerId == RoomStateHeader.NoPlayer;
        }

        private void WireButtons(VisualElement root)
        {
            if (_ready != null)
            {
                // 지금 상태의 반대를 요청한다. 로컬 사본을 두지 않으므로 여기서 읽는 값도
                // 서버가 보낸 명단이다.
                _ready.clicked += () => OnToggleReady?.Invoke(!_session.IsLocalReady);
            }

            if (_start != null)
            {
                _start.clicked += () => OnStart?.Invoke();
            }

            if (_leave != null)
            {
                _leave.clicked += () => OnLeave?.Invoke();
            }

            var copyCode = root.Q<Button>("copy-code");

            if (copyCode != null)
            {
                copyCode.clicked += () =>
                {
                    GUIUtility.systemCopyBuffer = InviteCodeText.ToDisplay(_session.Code);
                    Show(_copyResult, "코드를 복사했다.");
                };
            }

            if (_copyLink != null)
            {
                _copyLink.clicked += CopyLink;
            }

            var ok = root.Q<Button>("confirm-ok");
            var cancel = root.Q<Button>("confirm-cancel");

            if (ok != null)
            {
                ok.clicked += () =>
                {
                    var pending = _pendingConfirm;
                    HideConfirm();
                    pending?.Invoke();
                };
            }

            if (cancel != null)
            {
                cancel.clicked += HideConfirm;
            }

            // 포인터가 UI 위에 있는 동안은 스탠드 클릭을 받지 않는다. 루트 전체에 걸어 두면
            // `picking-mode="Ignore"` 인 요소는 애초에 이벤트를 만들지 않으므로, 실제로
            // 눌리는 것들만 이 판정에 들어온다.
            root.RegisterCallback<PointerEnterEvent>(_ => PointerOverUi = true);
            root.RegisterCallback<PointerLeaveEvent>(_ => PointerOverUi = false);
        }

        /// 링크는 클라이언트가 자기 실행 위치에서 조립한다. 서버는 배포 URL 을 모른다.
        private void CopyLink()
        {
            if (!InviteLink.TryBuild(_session.Code, out var link))
            {
                // 조용히 실패하면 사용자는 복사됐다고 믿고 빈 것을 붙여넣는다.
                Show(_copyResult, "이 빌드에서는 링크를 만들 수 없다. 코드를 전달한다.");
                return;
            }

            GUIUtility.systemCopyBuffer = link;
            Show(_copyResult, link);
        }

        // ==================================================== 확인과 알림

        /// 되돌릴 수 없는 것만 지난다 — 강제 퇴장과 방장 위임.
        ///
        /// 강제 퇴장은 대상이 아무 잘못이 없을 수 있다. 잘못 누르면 남의 판을 끝내고,
        /// 그 사람은 이유를 알 수 없다.
        public void Confirm(string text, Action onConfirm)
        {
            _pendingConfirm = onConfirm;

            if (_confirmText != null)
            {
                _confirmText.text = text;
            }

            if (_confirm != null)
            {
                _confirm.style.display = DisplayStyle.Flex;
            }
        }

        private void HideConfirm()
        {
            _pendingConfirm = null;

            if (_confirm != null)
            {
                _confirm.style.display = DisplayStyle.None;
            }
        }

        /// 화면 가운데 한 줄. 옛 로비의 `notice` 자리를 그대로 쓴다.
        public void Notify(string message)
        {
            Show(_notice, message);
        }

        /// 방에 들어올 때 한 번. 지난 방의 흔적을 지운다.
        public void Reset()
        {
            Show(_copyResult, string.Empty);
            Show(_notice, string.Empty);
            HideConfirm();
        }

        private static void Show(Label label, string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }

        /// 캐릭터 칸 하나. 만든 순간부터 바뀌지 않는 두 요소를 들고 있는다.
        private readonly struct CharacterItem
        {
            public CharacterItem(VisualElement root, Label owner)
            {
                Root = root;
                Owner = owner;
            }

            public VisualElement Root { get; }

            public Label Owner { get; }
        }
    }
}
