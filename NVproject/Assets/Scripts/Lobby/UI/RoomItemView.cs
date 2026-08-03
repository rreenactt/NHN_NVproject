using System;
using NV.Client.Lobby.Models;
using NV.Client.Net.Session;
using NV.Shared.Contracts.Enums;
using UnityEngine.UIElements;

namespace NV.Client.Lobby.UI
{
    /// 방 목록의 한 줄.
    ///
    /// 요구사항이 프리팹으로 만들라고 지정한 단위다. 이 프로젝트에서 그 역할을 하는 것은
    /// `templates/RoomItem.uxml` 이고, 이 클래스가 복제본 하나를 감싸 재사용한다 —
    /// 목록이 갱신될 때마다 버리고 다시 만들지 않고 `Bind` 로 내용만 갈아 끼운다.
    ///
    /// 정적 개발 룸을 숨기지 않는다. 그 방은 회수되지 않아 목록에 영원히 남지만,
    /// 숨기면 코드 없이 들어갈 수 있는 유일한 방으로 가는 길이 화면에서 사라진다.
    /// 배지로 구분만 한다.
    public sealed class RoomItemView
    {
        private readonly Label _code;
        private readonly Label _map;
        private readonly Label _badge;
        private readonly Label _count;
        private readonly Button _join;

        private RoomInfo _room;
        private Action<RoomInfo> _onJoin;

        public RoomItemView()
        {
            Root = MainLobbyAssets.Clone("RoomItem");

            if (Root == null)
            {
                return;
            }

            _code = Root.Q<Label>("item-code");
            _map = Root.Q<Label>("item-map");
            _badge = Root.Q<Label>("item-badge");
            _count = Root.Q<Label>("item-count");
            _join = Root.Q<Button>("item-join");

            if (_join != null)
            {
                // 핸들러를 한 번만 단다. 재사용할 때마다 붙이면 같은 클릭이 여러 번
                // 처리되고, 그 증상은 목록을 오래 쓴 뒤에야 나타난다.
                _join.clicked += () => _onJoin?.Invoke(_room);
            }
        }

        public VisualElement Root { get; }

        public void Bind(RoomInfo room, Action<RoomInfo> onJoin)
        {
            _room = room;
            _onJoin = onJoin;

            if (Root == null)
            {
                return;
            }

            _code.text = InviteCodeText.ToDisplay(room.Code);
            _map.text = string.IsNullOrEmpty(room.MapName) ? "MAP —" : "MAP " + room.MapName.ToUpperInvariant();
            _count.text = $"{room.PlayerCount}/{room.Capacity}";

            var joinable = LobbyModel.IsJoinable(room);
            _join.SetEnabled(joinable);
            _join.text = joinable ? "참가" : room.Phase != RoomPhase.Waiting ? "진행 중" : "정원";

            ApplyBadge(room);
        }

        /// 상태코드가 말하지 못하는 두 가지를 배지가 말한다 — 이미 시작했는가,
        /// 그리고 이것이 회수되지 않는 개발용 방인가.
        private void ApplyBadge(RoomInfo room)
        {
            _badge.RemoveFromClassList("room-item-badge-playing");
            _badge.RemoveFromClassList("room-item-badge-dev");

            if (room.Phase == RoomPhase.Playing)
            {
                _badge.style.display = DisplayStyle.Flex;
                _badge.text = "PLAYING";
                _badge.AddToClassList("room-item-badge-playing");
                return;
            }

            if (room.Phase == RoomPhase.Ended)
            {
                _badge.style.display = DisplayStyle.Flex;
                _badge.text = "ENDED";
                _badge.AddToClassList("room-item-badge-playing");
                return;
            }

            if (IsStaticRoom(room.Code))
            {
                _badge.style.display = DisplayStyle.Flex;
                _badge.text = "DEV";
                _badge.AddToClassList("room-item-badge-dev");
                return;
            }

            _badge.style.display = DisplayStyle.None;
        }

        /// 초대 코드 형식을 만족하지 않는 방 id 는 설정으로 미리 열어 둔 정적 룸이다.
        ///
        /// 서버는 `Game:StaticRooms` 의 id 를 코드 규칙으로 검사하지 않는다 —
        /// `test` 는 4자라 초대 코드가 될 수 없다. 그 차이가 곧 구분법이다.
        private static bool IsStaticRoom(string code)
        {
            return !string.IsNullOrEmpty(code) && !InviteCodeText.IsValid(code);
        }
    }
}
