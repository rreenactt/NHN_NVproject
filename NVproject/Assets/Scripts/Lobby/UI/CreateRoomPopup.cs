using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace NV.Client.Lobby.UI
{
    /// 방 만들기 팝업.
    ///
    /// 맵 목록을 클라이언트가 들고 있다. 서버는 `RoomMaps.ByMap` 을 갖고 있지만 그것을
    /// 내주는 엔드포인트가 없다. 이것이 조용히 틀어지지는 않는다 — 등록되지 않은 맵 id 로
    /// 만들면 `POST /rooms` 가 `400 unknownMap` 을 주고 세션이 `UnknownMap` 으로
    /// 분류하므로, 표가 낡으면 화면에 그렇게 뜬다.
    public static class CreateRoomPopup
    {
        /// 서버 `appsettings.json` 의 `Game:Maps` 키와 같아야 한다.
        ///
        /// `MapData/` 에는 `arena.json` 과 `backrooms2f.json` 도 있지만 둘 다 등록되어
        /// 있지 않아 지금은 만들 수 없다. 여기에 넣으면 고를 수는 있으나 400 이 난다.
        private static readonly string[] MapIds = { "default", "test-room" };

        /// 맵 id → 사람이 읽는 설명. 표시에만 쓴다.
        private static readonly string[] MapNotes =
        {
            "Backrooms — 실제 매치용 맵",
            "Test Room — 개발용 작은 맵",
        };

        public static string DefaultMapId => MapIds[0];

        /// <param name="onCreate">맵 id 와 공개 여부.</param>
        public static void Open(PopupHost host, Action<string, bool> onCreate)
        {
            var element = MainLobbyAssets.Clone("CreateRoomPopup");

            if (element == null || host == null)
            {
                return;
            }

            var dropdown = element.Q<DropdownField>("create-map");
            var note = element.Q<Label>("create-map-note");
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

            if (dropdown != null)
            {
                dropdown.choices = new List<string>(MapIds);
                dropdown.index = 0;

                dropdown.RegisterValueChangedCallback(_ => ApplyNote(dropdown, note));
            }

            ApplyNote(dropdown, note);

            if (confirm != null)
            {
                confirm.clicked += () =>
                {
                    var mapId = dropdown != null && dropdown.index >= 0
                        ? MapIds[dropdown.index]
                        : DefaultMapId;

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

        private static void ApplyNote(DropdownField dropdown, Label note)
        {
            if (note == null)
            {
                return;
            }

            var index = dropdown != null ? dropdown.index : 0;

            note.text = index >= 0 && index < MapNotes.Length ? MapNotes[index] : string.Empty;
        }
    }
}
