using System;
using System.Collections.Generic;
using NV.Client.Lobby.Models;
using NV.Client.Lobby.Services;
using NV.Client.Net.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace NV.Client.Lobby.UI
{
    /// 활성 방 목록. ScrollView + 템플릿 복제 + 풀링.
    ///
    /// 갱신마다 행을 버리고 다시 만들지 않는다. 만든 행은 `_pool` 에 남겨 두고 다음
    /// 갱신에서 다시 쓴다 — 새로고침이 3초마다 눌릴 수 있는 화면에서 매번 8개의
    /// `VisualElement` 트리를 새로 만들면 그만큼이 매번 쓰레기가 된다.
    ///
    /// 빈 목록과 비공개를 반드시 다르게 그린다. 하나는 "방이 없다"(방을 만들면 된다),
    /// 다른 하나는 "알 수 없다"(코드를 받아야 한다)이고 다음 행동이 다르다.
    public sealed class RoomListView
    {
        private readonly ScrollView _scroll;
        private readonly VisualElement _empty;
        private readonly Label _emptyTitle;
        private readonly Label _emptyNote;
        private readonly Label _refreshed;
        private readonly Button _refresh;

        private readonly List<RoomItemView> _pool = new List<RoomItemView>();

        private Action<RoomInfo> _onJoin;

        public RoomListView(VisualElement root, Action onRefresh)
        {
            _scroll = root.Q<ScrollView>("room-scroll");
            _empty = root.Q<VisualElement>("room-empty");
            _emptyTitle = root.Q<Label>("room-empty-title");
            _emptyNote = root.Q<Label>("room-empty-note");
            _refreshed = root.Q<Label>("room-refreshed");
            _refresh = root.Q<Button>("refresh-button");

            if (_refresh != null)
            {
                _refresh.clicked += () => onRefresh?.Invoke();
            }
        }

        public void SetJoinHandler(Action<RoomInfo> onJoin)
        {
            _onJoin = onJoin;
        }

        public void Refresh(LobbyModel model, RoomService rooms)
        {
            if (_scroll == null)
            {
                return;
            }

            var count = model.ListStatus == RoomListStatus.Ready ? model.Rooms.Count : 0;

            Fill(model, count);

            var showEmpty = count == 0;
            _empty.style.display = showEmpty ? DisplayStyle.Flex : DisplayStyle.None;
            _scroll.style.display = showEmpty ? DisplayStyle.None : DisplayStyle.Flex;

            if (showEmpty)
            {
                ApplyEmptyText(model);
            }

            _refresh.SetEnabled(rooms.CanRefresh);
            _refresh.text = rooms.IsRefreshing ? "받는 중…" : "새로고침";

            _refreshed.text = RefreshedText(model, rooms);
        }

        /// 행을 필요한 만큼만 만들고, 남는 것은 트리에서만 뗀다.
        private void Fill(LobbyModel model, int count)
        {
            while (_pool.Count < count)
            {
                var item = new RoomItemView();

                if (item.Root == null)
                {
                    // 템플릿을 못 읽었다. 더 만들어도 같은 결과이므로 멈춘다.
                    break;
                }

                _pool.Add(item);
            }

            for (var index = 0; index < _pool.Count; index++)
            {
                var item = _pool[index];

                if (index < count)
                {
                    item.Bind(model.Rooms[index], _onJoin);

                    if (item.Root.parent != _scroll.contentContainer)
                    {
                        _scroll.Add(item.Root);
                    }
                }
                else
                {
                    item.Root.RemoveFromHierarchy();
                }
            }
        }

        private void ApplyEmptyText(LobbyModel model)
        {
            switch (model.ListStatus)
            {
                // 서버가 이 경로를 모른다. 방마다 공개 여부를 정하기 전의 서버는 목록
                // 전체를 설정 뒤에 숨겼고, 그 설정이 꺼져 있으면 404 로 답했다.
                case RoomListStatus.Unavailable:
                    _emptyTitle.text = "이 서버는 방 목록을 제공하지 않는다";
                    _emptyNote.text =
                        "서버가 공개 방 목록을 지원하지 않는 버전이다. "
                        + "방을 만들어 코드를 나누거나, 받은 초대 코드로 참가한다.";
                    break;

                case RoomListStatus.Failed:
                    _emptyTitle.text = "목록을 받지 못했다";
                    _emptyNote.text = SessionFailure.Of(model.ListFailure).Message;
                    break;

                // 공개 방이 없다는 뜻이지 방이 없다는 뜻이 아니다. 비공개 방은 여기
                // 실리지 않으므로, 그 사실을 적지 않으면 "아무도 게임을 안 한다" 로 읽힌다.
                case RoomListStatus.Ready:
                    _emptyTitle.text = "공개된 방이 없다";
                    _emptyNote.text =
                        "비공개 방은 목록에 뜨지 않는다. 초대 코드를 받았다면 코드로 참가하고, "
                        + "아니면 방을 만든다.";
                    break;

                default:
                    _emptyTitle.text = "목록을 확인하는 중…";
                    _emptyNote.text = string.Empty;
                    break;
            }
        }

        private static string RefreshedText(LobbyModel model, RoomService rooms)
        {
            if (rooms.IsRefreshing)
            {
                return string.Empty;
            }

            var cooldown = rooms.CooldownRemaining;

            if (cooldown > 0f)
            {
                return $"{Mathf.CeilToInt(cooldown)}초 뒤 다시";
            }

            if (model.LastRefreshAt <= 0f)
            {
                return string.Empty;
            }

            var elapsed = Mathf.FloorToInt(Time.unscaledTime - model.LastRefreshAt);
            return elapsed < 1 ? "방금" : $"{elapsed}초 전";
        }
    }
}
