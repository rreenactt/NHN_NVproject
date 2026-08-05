using System;
using NV.Client.Net;
using NV.Client.Net.Session;
using NV.Shared.Contracts.Messages;
using UnityEngine;
using UnityEngine.UIElements;

namespace NV.Client.Lobby.UI
{
    /// 대기방 페이지. 코드, 명단, 시작, 나가기.
    ///
    /// 옛 `RoomView` 를 승계한다. 그것은 팝업이었고 이것은 페이지다 — 바뀐 이유는
    /// `GameLobbyPage.uxml` 의 머리 주석에 있다.
    ///
    /// **상태를 들고 있지 않다.** 전부 `NetSession` 과 `NetworkClient` 에서 읽어 그린다.
    /// 화면이 자기 사본을 들면 서버가 보낸 명단과 화면의 명단이 어긋날 수 있고, 그 차이는
    /// 눈으로 잡을 수 없다.
    ///
    /// 페이지의 수명은 화면 트리와 같다. 방에 들어갈 때마다 만들지 않는다 — 들어가고
    /// 나가는 것은 `display` 하나이고, 그 판정은 `GameLobbyController` 가 한다.
    public sealed class GameLobbyView
    {
        private readonly NetSession _session;

        private readonly Label _code;
        private readonly Label _map;
        private readonly Label _visibility;
        private readonly Label _note;
        private readonly Label _copyResult;
        private readonly Label _playersCount;
        private readonly Label _readyCount;
        private readonly VisualElement _roster;
        private readonly Button _start;
        private readonly Button _ready;
        private readonly Button _copyLink;

        /// <param name="slot">`MainLobby.uxml` 의 `#page-room`. 여기에 페이지 본체를 넣는다.</param>
        public GameLobbyView(VisualElement slot, NetSession session)
        {
            _session = session;

            if (slot == null)
            {
                return;
            }

            Root = MainLobbyAssets.Clone("GameLobbyPage");

            if (Root == null)
            {
                return;
            }

            slot.Add(Root);

            _code = Root.Q<Label>("room-code");
            _map = Root.Q<Label>("room-map");
            _visibility = Root.Q<Label>("room-visibility");
            _note = Root.Q<Label>("room-note");
            _copyResult = Root.Q<Label>("room-copy-result");
            _playersCount = Root.Q<Label>("room-players-count");
            _readyCount = Root.Q<Label>("room-ready-count");
            _roster = Root.Q<VisualElement>("room-roster");
            _start = Root.Q<Button>("room-start");
            _ready = Root.Q<Button>("room-ready");

            Characters = new CharacterPickerView(Root);

            var copyCode = Root.Q<Button>("room-copy-code");
            var copyLink = Root.Q<Button>("room-copy-link");
            var leave = Root.Q<Button>("room-leave");

            if (copyCode != null)
            {
                copyCode.clicked += CopyCode;
            }

            if (copyLink != null)
            {
                copyLink.clicked += CopyLink;
            }

            _copyLink = copyLink;

            if (_start != null)
            {
                // 자격과 인원은 서버가 다시 본다. 버튼이 꺼져 있는 것은 UI 의 친절이고
                // 판정이 아니다.
                _start.clicked += () => OnStart?.Invoke();
            }

            if (_ready != null)
            {
                // 지금 상태의 반대를 요청한다. 로컬 사본을 두지 않으므로 여기서 읽는 값도
                // 서버가 보낸 명단이다 — 두 번 빠르게 누르면 두 번째가 첫 번째의 결과를
                // 보기 전에 나갈 수 있고, 그때는 같은 값을 두 번 보내 서버가 무시한다.
                _ready.clicked += () => OnToggleReady?.Invoke(!_session.IsLocalReady);
            }

            if (leave != null)
            {
                leave.clicked += () => OnLeave?.Invoke();
            }
        }

        public VisualElement Root { get; }

        /// 캐릭터 칸. 페이지의 한 칸이므로 이 뷰가 갱신을 함께 돌린다.
        public CharacterPickerView Characters { get; }

        /// 버튼이 무엇을 하는지는 뷰가 정하지 않는다. `GameLobbyController` 가 채운다 —
        /// 뷰가 세션을 직접 부르기 시작하면 화면 흐름이 뷰 수만큼 흩어진다.
        public Action OnStart { get; set; }

        public Action<bool> OnToggleReady { get; set; }

        public Action OnLeave { get; set; }

        /// 방에 들어갈 때 한 번. 지난 방의 흔적을 지운다.
        ///
        /// 페이지가 트리에 살아 있으므로 복사 결과 같은 한 번짜리 문구가 남는다. 다음 방에서
        /// "코드를 복사했다" 가 먼저 떠 있으면 그것은 거짓말이다.
        public void Reset()
        {
            if (_copyResult != null)
            {
                _copyResult.text = string.Empty;
            }
        }

        public void Refresh()
        {
            if (Root == null)
            {
                return;
            }

            _code.text = InviteCodeText.ToDisplay(_session.Code);
            _map.text = "MAP  " + (_session.Room.MapName ?? string.Empty).ToUpperInvariant();

            // 공개인지 비공개인지 화면에 남긴다. 코드를 어디까지 흘려도 되는지가
            // 그것으로 갈리는데, 방에 들어온 사람은 자기가 어느 쪽에 있는지 알 방법이
            // 이 줄밖에 없다 — 목록으로 들어왔는지 코드로 들어왔는지는 단서가 아니다.
            _visibility.text = _session.Room.IsPublic
                ? "PUBLIC  ·  방 목록에 떠 있다"
                : "PRIVATE  ·  코드를 아는 사람만 들어온다";

            // 링크를 만들 수 없는 플랫폼에서는 버튼을 숨긴다. 눌러 봐야 실패한다.
            // 페이지는 방보다 오래 살므로 코드가 바뀔 때마다 다시 판정한다.
            if (_copyLink != null)
            {
                _copyLink.style.display = InviteLink.TryBuild(_session.Code, out _)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            RefreshRoster();
            RefreshButtons();
            Characters?.Refresh(_session);

            _note.text = StartNote();
        }

        /// 화면 트리를 버리기 전에 부른다.
        ///
        /// 캐릭터 미리보기의 카메라와 렌더 텍스처는 `VisualElement` 와 달리 도메인 리로드를
        /// 넘어 씬에 남는다. 치우지 않으면 리로드마다 무대가 하나씩 늘어난다.
        public void Dispose()
        {
            Characters?.Dispose();
        }

        /// 시작·준비 버튼의 모양.
        ///
        /// **한 사람에게 둘 다 보이지 않는다.** 방장에게는 시작 버튼이 준비이므로 준비 토글을
        /// 감추고, 방장이 아닌 사람에게는 시작 버튼을 감춘다 — 눌러도 서버가 거부하는 버튼을
        /// 보여 주면 그것이 고장으로 읽힌다.
        ///
        /// 대기 단계에서만 준비를 받는다(`Room.SetReady`). 매치 중·결과 화면에서는 토글을
        /// 감춘다.
        private void RefreshButtons()
        {
            var waiting = _session.State == SessionState.InLobby;
            var isHost = _session.IsHost;

            if (_start != null)
            {
                _start.style.display = isHost ? DisplayStyle.Flex : DisplayStyle.None;
                _start.SetEnabled(_session.CanStart);
            }

            if (_ready != null)
            {
                _ready.style.display = !isHost && waiting ? DisplayStyle.Flex : DisplayStyle.None;

                var ready = _session.IsLocalReady;

                _ready.text = ready ? "준비 취소" : "준비";

                // 켜진 것과 꺼진 것이 글자만으로 갈리면 읽지 않고 지나간다.
                _ready.EnableInClassList("button-primary", !ready);
            }

            if (_readyCount != null)
            {
                var waitingFor = _session.NotReadyCount;

                _readyCount.style.display = waiting ? DisplayStyle.Flex : DisplayStyle.None;
                _readyCount.text = waitingFor > 0 ? waitingFor + "명 준비 안 함" : "전원 준비";
            }
        }

        /// 인원과 정원을 함께 쓴다.
        ///
        /// **채워진 수만 보여주면 안 된다.** 8칸 중 2칸인 방과 정원이 2인 방이 화면에서 같은
        /// 모습이 된다. 봇으로 최소 인원만 채운 개발용 방이 "봇이 하나뿐" 으로 읽히는 것이
        /// 그 증상이었다.
        ///
        /// 채워진 수는 **명단(`RosterCount`)에서 센다.** `Room.PlayerCount` 는 접속 전 조회의
        /// 값이라 방에 들어온 뒤로 갱신되지 않는다 — 그 값을 쓰면 남이 들어오고 나가는 것이
        /// 화면에 반영되지 않는다. 정원은 반대로 방이 사는 동안 바뀌지 않으므로 조회 값을 쓴다.
        private void RefreshPlayersCount(int filled, int capacity)
        {
            if (_playersCount == null)
            {
                return;
            }

            _playersCount.text = capacity > 0 ? filled + " / " + capacity : filled.ToString();

            // 정원이 찼다는 것은 색으로 먼저 말한다. 두 숫자를 비교하게 하지 않는다.
            _playersCount.EnableInClassList("roster-count-full", capacity > 0 && filled >= capacity);
        }

        private void RefreshRoster()
        {
            _roster.Clear();

            var client = _session.Client;

            if (client == null)
            {
                RefreshPlayersCount(0, _session.Room.Capacity);
                return;
            }

            RefreshPlayersCount(client.RosterCount, _session.Room.Capacity);

            for (var index = 0; index < client.RosterCount; index++)
            {
                var entry = client.RosterEntry(index);
                var isSelf = client.HasWelcome && entry.PlayerId == client.LocalPlayerId;

                var row = new VisualElement();
                row.AddToClassList("roster-row");

                if (isSelf)
                {
                    row.AddToClassList("roster-self");
                }

                // 이름은 비어 있을 수 있다. 와이어가 길이 0 을 허용하고, 이름을 넣지
                // 않고 붙은 클라이언트가 그렇게 온다.
                var name = new Label(string.IsNullOrEmpty(entry.Name)
                    ? "플레이어 " + entry.PlayerId
                    : entry.Name);
                name.AddToClassList("roster-name");
                row.Add(name);

                // 어떤 캐릭터를 입었는지 명단에 쓴다. 미리보기는 자기 것 하나뿐이므로,
                // 남이 무엇을 골랐는지 알 수 있는 자리가 이 줄밖에 없다.
                var character = new Label(LobbyCharacterCatalog.LabelOf(entry.CharacterId));
                character.AddToClassList("roster-character");
                row.Add(character);

                var tag = new Label(Tag(entry, client, isSelf));
                tag.AddToClassList("roster-tag");
                row.Add(tag);

                // 준비 도장. **대기 단계에서만 그린다** — 준비는 매치가 끝나 로비로 돌아올 때
                // 내려가므로 매치 중에는 참인 값이지만, 화면에 두면 매치 중에 준비를 기다리는
                // 것처럼 읽힌다.
                var stamp = new Label(entry.IsReady ? "READY" : string.Empty);
                stamp.AddToClassList("roster-ready");
                stamp.EnableInClassList("roster-ready-on", entry.IsReady);
                stamp.style.display = _session.State == SessionState.InLobby
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                row.Add(stamp);

                _roster.Add(row);
            }

            AddEmptySlots(client.RosterCount);
        }

        /// 남은 자리를 **정원까지 전부** 그린다.
        ///
        /// 팝업이었을 때는 그럴 수 없었다. `.popup-frame` 은 `max-height: 84%` 에 스크롤이
        /// 없어 8칸을 그리면 아래의 시작 버튼이 잘렸고, 그래서 "모자란 만큼만" 그리는 규칙이
        /// 따로 있었다. 페이지에는 높이가 있으므로 그 타협을 버린다 — 정원이 몇인지, 지금
        /// 몇 자리가 비었는지가 줄 수로 바로 읽힌다.
        ///
        /// 문구는 두 갈래를 유지한다. **필요한 자리와 남는 자리는 다른 말이다** — 같은 문구로
        /// 쓰면 시작할 수 없는 이유가 명단에 있는데도 보이지 않는다.
        private void AddEmptySlots(int filled)
        {
            var spare = _session.Room.Capacity - filled;

            if (spare <= 0)
            {
                return;
            }

            var needed = Math.Max(0, _session.MinPlayers - filled);

            for (var index = 0; index < spare; index++)
            {
                var row = new VisualElement();
                row.AddToClassList("roster-row");
                row.AddToClassList("roster-empty");

                var label = new Label(index < needed ? "─ 한 명 더 필요 ─" : "─ 빈 자리 ─");
                label.AddToClassList("roster-name");
                row.Add(label);

                _roster.Add(row);
            }
        }

        /// 한 줄의 꼬리표. 방장·나·봇을 한 문자열로 합친다.
        ///
        /// 봇은 서버가 알려 준다(`RoomPlayerFlags.Bot`). 이름으로 짐작하게 두면 `BOT 1` 이라고
        /// 스스로 이름을 지은 사람과 구분되지 않는다.
        private static string Tag(in RoomPlayerEntry entry, NetworkClient client, bool isSelf)
        {
            var isHost = client.HasRoomState && client.RoomState.HostPlayerId == entry.PlayerId;

            if (entry.IsBot)
            {
                return isHost ? "봇 · 방장" : "봇";
            }

            if (isHost && isSelf)
            {
                return "방장 · 나";
            }

            return isHost ? "방장" : isSelf ? "나" : string.Empty;
        }

        /// 시작 버튼이 꺼져 있는 이유를 쓴다. 이유 없이 꺼진 버튼은 고장으로 읽힌다.
        private string StartNote()
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
                // **"방장을 기다린다" 는 방장이 있을 때만 참이다.** 방장 자리가 비어 있으면
                // 아무리 기다려도 시작되지 않으므로, 그 경우를 같은 문구로 덮으면 화면이
                // 거짓말을 한다. 실제로 그랬다 — 정적 룸(`test`)이 방장 없이 열려 있던
                // 서버에서 이 줄이 "기다린다" 를 띄웠고, 기다릴 대상이 없었다.
                //
                // 서버는 이제 정적 룸에서 받는 사람 자신을 방장으로 싣는다. 이 갈래는
                // 그보다 오래된 서버에 붙었을 때를 위한 것이며, 그 사실을 말해 준다.
                if (HasNoHost())
                {
                    return "이 방에 방장이 없어 아무도 시작할 수 없다. 서버가 오래된 빌드일 수 있다.";
                }

                // 준비 여부에 따라 다음에 할 일이 다르다. 준비하지 않은 사람에게 "방장을
                // 기다린다" 만 쓰면 자기가 막고 있다는 사실이 화면에 없다.
                return _session.IsLocalReady
                    ? "방장이 시작하기를 기다린다."
                    : "준비를 누르면 방장이 시작할 수 있다.";
            }

            var count = _session.Client != null ? _session.Client.RosterCount : 0;

            if (count < _session.MinPlayers)
            {
                return $"{_session.MinPlayers}명부터 시작할 수 있다. 지금 {count}명.";
            }

            var waitingFor = _session.NotReadyCount;

            // **꺼진 이유를 인원으로 말한다.** "준비를 기다린다" 만 쓰면 몇 명이 남았는지
            // 명단을 세어 알아야 한다.
            return waitingFor > 0
                ? $"{waitingFor}명이 아직 준비하지 않았다."
                : "시작할 수 있다.";
        }

        /// 서버가 방장을 아무에게도 배정하지 않았는가.
        ///
        /// 명단 전문이 아직 오지 않은 동안은 거짓이다 — 그때는 "모른다" 이고,
        /// 모르는 것을 "방장이 없다" 로 말하면 접속 직후 한순간 잘못된 문구가 뜬다.
        private bool HasNoHost()
        {
            var client = _session.Client;

            return client != null
                && client.HasRoomState
                && client.RoomState.HostPlayerId == RoomStateHeader.NoPlayer;
        }

        private void CopyCode()
        {
            GUIUtility.systemCopyBuffer = InviteCodeText.ToDisplay(_session.Code);
            _copyResult.text = "코드를 복사했다.";
        }

        /// 링크는 클라이언트가 자기 실행 위치에서 조립한다. 서버는 배포 URL 을 모른다.
        private void CopyLink()
        {
            if (!InviteLink.TryBuild(_session.Code, out var link))
            {
                // 조용히 실패하면 사용자는 복사됐다고 믿고 빈 것을 붙여넣는다.
                _copyResult.text = "이 빌드에서는 링크를 만들 수 없다. 코드를 전달한다.";
                return;
            }

            GUIUtility.systemCopyBuffer = link;
            _copyResult.text = link;
        }
    }
}
