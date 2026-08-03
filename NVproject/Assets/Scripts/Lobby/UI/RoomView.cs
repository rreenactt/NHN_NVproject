using System;
using NV.Client.Net;
using NV.Client.Net.Session;
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

        private void RefreshRoster()
        {
            _roster.Clear();

            var client = _session.Client;

            if (client == null)
            {
                return;
            }

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
                return "방장이 시작하기를 기다린다.";
            }

            var count = _session.Client != null ? _session.Client.RosterCount : 0;

            return count < _session.MinPlayers
                ? $"{_session.MinPlayers}명부터 시작할 수 있다. 지금 {count}명."
                : "시작할 수 있다.";
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
