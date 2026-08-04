using System;
using System.Collections.Generic;
using NV.Client.Lobby.Models;
using NV.Client.Lobby.Services;
using UnityEngine.UIElements;

namespace NV.Client.Lobby.UI
{
    /// 방 만들기 팝업.
    ///
    /// **맵 목록은 서버에서 온다.** 예전에는 이 파일에 맵 id 배열과 설명 배열이 짝으로 박혀
    /// 있었고, 서버 `appsettings.json` 과 손으로 맞춰야 했다 — 어긋나면 고를 수 있는데 만들 수
    /// 없는 맵이 되고(`400 unknownMap`), 반대로 서버에 있는 맵을 고를 방법이 없었다.
    ///
    /// 드롭다운이 아니라 세로 목록인 이유는 상태가 넷이기 때문이다(`MapChoiceStatus`).
    /// 드롭다운에는 "고를 수 없는 항목과 그 이유" 를 담을 자리가 없고, 이유 없이 빠진 항목은
    /// 사람이 자기가 아는 맵이 왜 사라졌는지 알 수 없게 만든다.
    public static class CreateRoomPopup
    {
        /// <param name="onCreate">맵 id 와 공개 여부.</param>
        public static void Open(PopupHost host, MapChoiceService maps, Action<string, bool> onCreate)
        {
            var element = MainLobbyAssets.Clone("CreateRoomPopup");

            if (element == null || host == null || maps == null)
            {
                return;
            }

            var list = element.Q<VisualElement>("create-map-list");
            var listNote = element.Q<Label>("create-map-note");
            var isPublic = element.Q<Toggle>("create-public");
            var publicNote = element.Q<Label>("create-public-note");
            var confirm = element.Q<Button>("create-confirm");
            var cancel = element.Q<Button>("create-cancel");

            // 기본은 비공개다. 노출은 만든 사람이 선택했을 때만 일어나야 한다.
            isPublic?.SetValueWithoutNotify(false);

            void SyncVisibility()
            {
                if (publicNote == null)
                {
                    return;
                }

                // 어느 쪽이든 초대 코드는 나온다. 공개가 "누구나 들어온다" 로,
                // 비공개가 "아무도 못 들어온다" 로 읽히지 않게 둘 다 적는다.
                publicNote.text = isPublic != null && isPublic.value
                    ? "방 목록에 뜬다. 코드로도 들어올 수 있다."
                    : "방 목록에 뜨지 않는다. 초대 코드를 아는 사람만 들어온다.";
            }

            isPublic?.RegisterValueChangedCallback(_ => SyncVisibility());
            SyncVisibility();

            var rows = new List<MapRowView>();
            var selected = -1;

            void Select(int index)
            {
                selected = index;

                for (var row = 0; row < rows.Count; row++)
                {
                    rows[row].SetSelected(row == index);
                }

                // 만들 수 없는 줄을 고른 채로 버튼이 살아 있으면 400 을 받으러 가는 버튼이 된다.
                confirm?.SetEnabled(index >= 0 && index < maps.Choices.Count && maps.Choices[index].CanCreate);
            }

            void Rebuild()
            {
                if (list == null)
                {
                    return;
                }

                list.Clear();
                rows.Clear();

                if (maps.IsLoading)
                {
                    list.Add(new Label("맵 목록을 받는 중…") { name = "create-map-loading" });
                    confirm?.SetEnabled(false);
                    ApplyListNote(listNote, maps);
                    return;
                }

                var choices = maps.Choices;

                if (choices.Count == 0)
                {
                    // 서버도 답하지 않았고 이 빌드도 아는 맵이 없다. 만들기를 막지 않는다 —
                    // 맵을 비워 보내면 서버가 기본 맵으로 만든다.
                    list.Add(new Label("고를 수 있는 맵이 없다.") { name = "create-map-loading" });
                    confirm?.SetEnabled(true);
                    ApplyListNote(listNote, maps);
                    return;
                }

                for (var index = 0; index < choices.Count; index++)
                {
                    var row = new MapRowView(choices[index], Select, index);
                    rows.Add(row);
                    list.Add(row.Root);
                }

                ApplyListNote(listNote, maps);
                Select(MapChoiceService.PreferredIndex(choices));
            }

            Rebuild();
            maps.Ensure(Rebuild);

            if (confirm != null)
            {
                confirm.clicked += () =>
                {
                    var choices = maps.Choices;

                    // 고른 것이 없으면 맵을 비워 보낸다. 서버가 기본 맵으로 해석한다.
                    var mapId = selected >= 0 && selected < choices.Count
                        ? choices[selected].MapId
                        : string.Empty;

                    MapChoiceService.Remember(mapId);

                    host.CloseTop();
                    onCreate?.Invoke(mapId, isPublic != null && isPublic.value);
                };
            }

            if (cancel != null)
            {
                cancel.clicked += () => host.CloseTop();
            }

            host.Open(element);
        }

        /// 목록 위의 한 줄. **서버가 답하지 않았다는 사실이 화면에 있어야 한다** — 없으면
        /// 이 빌드가 아는 맵만 보이는 것이 "서버에 맵이 두 개뿐" 으로 읽힌다.
        private static void ApplyListNote(Label note, MapChoiceService maps)
        {
            if (note == null)
            {
                return;
            }

            if (maps.IsLoading)
            {
                note.text = string.Empty;
                return;
            }

            note.text = maps.ServerListUnavailable
                ? "서버의 맵 목록을 받지 못했다. 이 빌드가 아는 맵만 보인다."
                : string.Empty;
        }
    }

    /// 맵 목록의 한 줄.
    ///
    /// 이름·설명·규모·상태를 한 줄에 담는다. 고를 수 없는 줄은 눌리지 않고 그 이유를 자기
    /// 자리에 적는다 — 이유 없이 꺼진 항목은 고장으로 읽힌다(`RoomService.CanQuickJoin` 이
    /// 같은 규칙을 지킨다).
    internal sealed class MapRowView
    {
        private readonly VisualElement _root;

        public MapRowView(MapChoice choice, Action<int> onSelect, int index)
        {
            _root = MainLobbyAssets.Clone("MapItem");

            if (_root == null)
            {
                // 템플릿이 없으면 이름만이라도 보여 준다. 목록이 통째로 비면 원인이
                // 서버인지 에셋인지 화면에서 구분할 수 없다.
                _root = new Label(choice.DisplayName);
                return;
            }

            var name = _root.Q<Label>("map-item-name");
            var meta = _root.Q<Label>("map-item-meta");
            var description = _root.Q<Label>("map-item-description");
            var reason = _root.Q<Label>("map-item-reason");

            if (name != null)
            {
                name.text = choice.IsDefault
                    ? choice.DisplayName + "  (기본)"
                    : choice.DisplayName;
            }

            if (meta != null)
            {
                var parts = new List<string>(2);

                if (!string.IsNullOrEmpty(choice.SizeText)) parts.Add(choice.SizeText);
                if (!string.IsNullOrEmpty(choice.PlayersText)) parts.Add(choice.PlayersText);

                meta.text = string.Join("  ·  ", parts.ToArray());
            }

            if (description != null)
            {
                description.text = choice.Description ?? string.Empty;
                description.style.display = string.IsNullOrEmpty(description.text)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }

            if (reason != null)
            {
                reason.text = choice.Reason;
                reason.style.display = string.IsNullOrEmpty(reason.text)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }

            if (!choice.CanCreate)
            {
                _root.AddToClassList("map-item-blocked");
                _root.SetEnabled(false);
                return;
            }

            _root.RegisterCallback<ClickEvent>(_ => onSelect?.Invoke(index));
        }

        public VisualElement Root => _root;

        public void SetSelected(bool selected)
        {
            if (_root == null)
            {
                return;
            }

            if (selected)
            {
                _root.AddToClassList("map-item-selected");
            }
            else
            {
                _root.RemoveFromClassList("map-item-selected");
            }
        }
    }
}
