using System;
using NV.Client.Net;
using NV.Client.Net.Session;
using NV.Shared.Contracts.Messages;
using UnityEngine;
using UnityEngine.UIElements;

namespace NV.Client.Lobby.UI
{
    /// 방 안 화면. 코드, 명단, 시작, 나가기.
    ///
    /// 요구사항의 UI 트리에는 이 화면이 없다 — 로비 첫 페이지만 다룬다. 없으면 방을
    /// 만든 뒤 아무것도 할 수 없는 화면에 갇히므로, 이 로비가 대체하는 옛 로비에서
    /// 가져왔다.
    ///
    /// 모달로 연다. 방 안에 있는 동안 뒤의 목록을 눌러 다른 방에 들어가는 것은
    /// 지금 방을 조용히 버리는 일이고, 그것을 실수로 할 수 있게 두면 안 된다.
    ///
    /// 상태를 들고 있지 않다. 전부 `NetSession` 과 `NetworkClient` 에서 읽어 그린다 —
    /// 화면이 자기 사본을 들면 서버가 보낸 명단과 화면의 명단이 어긋날 수 있고,
    /// 그 차이는 눈으로 잡을 수 없다.
    public sealed class RoomView
    {
        private readonly NetSession _session;

        private readonly Label _code;
        private readonly Label _map;
        private readonly Label _visibility;
        private readonly Label _note;
        private readonly Label _copyResult;
        private readonly Label _playersCount;
        private readonly VisualElement _roster;
        private readonly Button _start;

        public RoomView(NetSession session, Action onLeave)
        {
            _session = session;

            Root = MainLobbyAssets.Clone("RoomPopup");

            if (Root == null)
            {
                return;
            }

            _code = Root.Q<Label>("room-code");
            _map = Root.Q<Label>("room-map");
            _visibility = Root.Q<Label>("room-visibility");
            _note = Root.Q<Label>("room-note");
            _copyResult = Root.Q<Label>("room-copy-result");
            _playersCount = Root.Q<Label>("room-players-count");
            _roster = Root.Q<VisualElement>("room-roster");
            _start = Root.Q<Button>("room-start");

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

                // 링크를 만들 수 없는 플랫폼에서는 버튼을 숨긴다. 눌러 봐야 실패한다.
                copyLink.style.display = InviteLink.TryBuild(_session.Code, out _)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (_start != null)
            {
                // 자격과 인원은 서버가 다시 본다. 버튼이 꺼져 있는 것은 UI 의 친절이고
                // 판정이 아니다.
                _start.clicked += () => _session.RequestStart();
            }

            if (leave != null)
            {
                leave.clicked += () => onLeave?.Invoke();
            }
        }

        public VisualElement Root { get; }

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

            RefreshRoster();

            _start.SetEnabled(_session.CanStart);
            _note.text = StartNote();
        }

        /// 인원과 정원을 함께 쓴다.
        ///
        /// **채워진 수만 보여주면 안 된다.** 명단은 서버가 보낸 줄 수만큼 그려지므로, 8칸 중
        /// 2칸인 방과 정원이 2인 방이 화면에서 같은 모습이 된다. 봇으로 최소 인원만 채운
        /// 개발용 방이 "봇이 하나뿐" 으로 읽히는 것이 그 증상이었다.
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

                var tag = new Label(Tag(entry.PlayerId, client, isSelf));
                tag.AddToClassList("roster-tag");
                row.Add(tag);

                _roster.Add(row);
            }

            AddEmptySlots(client.RosterCount);
        }

        /// 남은 자리를 몇 줄 그린다. **정원 전체를 나열하지 않는다.**
        ///
        /// 8칸을 다 그리면 팝업이 그만큼 길어지고, `.popup-frame` 은 `max-height: 84%` 에
        /// 스크롤이 없어 아래의 시작 버튼이 잘린다. 정원은 이미 제목 줄의 숫자가 말하므로
        /// 여기서 할 일은 남은 자리의 **성격**을 말하는 것뿐이다.
        ///
        /// 그래서 줄 수가 두 경우로 갈린다. 최소 인원에 못 미치면 **모자란 만큼** 그려
        /// 몇 명이 더 필요한지 세지 않고 보이게 하고, 이미 시작할 수 있으면 자리가 남았다는
        /// 사실만 한 줄로 말한다.
        private void AddEmptySlots(int filled)
        {
            var spare = _session.Room.Capacity - filled;

            if (spare <= 0)
            {
                return;
            }

            var needed = _session.MinPlayers - filled;
            var rows = needed > 0 ? Math.Min(needed, spare) : 1;

            for (var index = 0; index < rows; index++)
            {
                var row = new VisualElement();
                row.AddToClassList("roster-row");
                row.AddToClassList("roster-empty");

                // 필요한 자리와 남은 자리는 다른 말이다. 같은 문구로 쓰면 시작할 수 없는
                // 이유가 명단에 있는데도 보이지 않는다.
                var label = new Label(needed > 0 ? "─ 한 명 더 필요 ─" : "─ 빈 자리 ─");
                label.AddToClassList("roster-name");
                row.Add(label);

                _roster.Add(row);
            }
        }

        private static string Tag(byte playerId, NetworkClient client, bool isSelf)
        {
            var isHost = client.HasRoomState && client.RoomState.HostPlayerId == playerId;

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
                return HasNoHost()
                    ? "이 방에 방장이 없어 아무도 시작할 수 없다. 서버가 오래된 빌드일 수 있다."
                    : "방장이 시작하기를 기다린다.";
            }

            var count = _session.Client != null ? _session.Client.RosterCount : 0;

            return count < _session.MinPlayers
                ? $"{_session.MinPlayers}명부터 시작할 수 있다. 지금 {count}명."
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
