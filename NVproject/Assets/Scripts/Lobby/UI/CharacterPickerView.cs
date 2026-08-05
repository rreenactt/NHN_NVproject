using System;
using System.Collections.Generic;
using NV.Client.Net.Session;
using NV.Shared.Contracts.Messages;
using UnityEngine;
using UnityEngine.UIElements;

namespace NV.Client.Lobby.UI
{
    /// 캐릭터를 고르는 칸. 목록과 미리보기.
    ///
    /// **누른다고 입는 것이 아니다.** 요청을 보내고 서버가 판정하며(`Room.SetCharacter`),
    /// 결과는 다음 명단 전문으로 온다 — 두 사람이 같은 틱에 같은 캐릭터를 고를 수 있고
    /// 하나만 입을 수 있다. 화면이 먼저 갈아입어 두면 거부된 선택이 화면에만 남는다.
    ///
    /// 이미 쓰이는 캐릭터는 **감추지 않고 흐리게 그린다.** 감추면 8종 중 몇 종이 있는지
    /// 알 수 없고, 남이 놓아 준 순간 목록이 늘어나는 것으로 보인다.
    public sealed class CharacterPickerView
    {
        private readonly VisualElement _list;
        private readonly VisualElement _preview;
        private readonly Label _note;

        private readonly List<Row> _rows = new List<Row>();

        private CharacterPreview _stage;
        private bool _stageTried;

        public CharacterPickerView(VisualElement root)
        {
            if (root == null)
            {
                return;
            }

            _list = root.Q<VisualElement>("character-list");
            _preview = root.Q<VisualElement>("character-preview");
            _note = root.Q<Label>("character-note");

            BuildRows();
        }

        /// 캐릭터 번호를 요청한다. `GameLobbyController` 가 채운다.
        public Action<byte> OnPick { get; set; }

        /// 목록의 상태를 지금 명단에 맞춘다.
        ///
        /// 줄을 다시 만들지 않는다. 2Hz 로 여덟 줄을 새로 만들면 눌리는 순간 요소가 교체되어
        /// 클릭이 사라지는 일이 생기고, 스크롤 위치도 매번 처음으로 돌아간다.
        public void Refresh(NetSession session)
        {
            if (_list == null)
            {
                return;
            }

            var client = session.Client;
            var waiting = session.State == SessionState.InLobby;

            var mine = RoomPlayerEntry.NoCharacter;

            for (var index = 0; index < _rows.Count; index++)
            {
                var row = _rows[index];
                var owner = OwnerOf(session, (byte)index, out var ownerIsSelf);

                if (ownerIsSelf)
                {
                    mine = (byte)index;
                }

                // 남이 입고 있으면 고를 수 없다. 대기 단계가 아니면 아무것도 고를 수 없다.
                var takenByOther = owner.Length > 0 && !ownerIsSelf;

                row.State.text = ownerIsSelf ? "착용 중" : takenByOther ? owner : string.Empty;

                row.Element.EnableInClassList("character-item-mine", ownerIsSelf);
                row.Element.EnableInClassList("character-item-taken", takenByOther);
                row.Element.SetEnabled(waiting && !takenByOther && !ownerIsSelf);
            }

            if (_note != null)
            {
                _note.text = waiting
                    ? "이미 쓰이는 캐릭터는 고를 수 없다."
                    : "매치 중에는 바꿀 수 없다.";
            }

            RefreshPreview(mine, client != null);
        }

        /// 무대를 치운다. 화면을 다시 만들기 전에 반드시 부른다 — 카메라와 렌더 텍스처는
        /// `VisualElement` 와 달리 도메인 리로드를 넘어 씬에 남는다.
        public void Dispose()
        {
            _stage?.Dispose();
            _stage = null;
            _stageTried = false;
        }

        private void BuildRows()
        {
            _list.Clear();
            _rows.Clear();

            for (var index = 0; index < LobbyCharacterCatalog.Count; index++)
            {
                LobbyCharacterCatalog.Character character = LobbyCharacterCatalog.All[index];
                var characterId = (byte)index;

                var element = new VisualElement();
                element.AddToClassList("character-item");

                // 색 조각. 이 캐릭터가 무엇인지 이름보다 이것이 먼저 말한다.
                var chip = new VisualElement();
                chip.AddToClassList("character-chip");
                chip.style.backgroundColor = character.suit;
                chip.style.borderTopColor = character.accent;
                chip.style.borderBottomColor = character.accent;
                chip.style.borderLeftColor = character.trim;
                chip.style.borderRightColor = character.trim;
                element.Add(chip);

                var label = new Label(character.label);
                label.AddToClassList("character-name");
                element.Add(label);

                var state = new Label(string.Empty);
                state.AddToClassList("character-state");
                element.Add(state);

                element.RegisterCallback<PointerDownEvent>(_ => OnPick?.Invoke(characterId));

                _list.Add(element);
                _rows.Add(new Row(element, state));
            }
        }

        /// 이 캐릭터를 입고 있는 사람의 표시 이름. 아무도 없으면 빈 문자열.
        private static string OwnerOf(NetSession session, byte characterId, out bool isSelf)
        {
            isSelf = false;

            var client = session.Client;

            if (client == null)
            {
                return string.Empty;
            }

            for (var index = 0; index < client.RosterCount; index++)
            {
                var entry = client.RosterEntry(index);

                if (entry.CharacterId != characterId)
                {
                    continue;
                }

                isSelf = client.HasWelcome && entry.PlayerId == client.LocalPlayerId;

                return string.IsNullOrEmpty(entry.Name) ? "플레이어 " + entry.PlayerId : entry.Name;
            }

            return string.Empty;
        }

        /// 미리보기에 내가 입은 것을 세운다.
        ///
        /// 무대는 **한 번만** 만들고 실패하면 다시 시도하지 않는다. 만들지 못하는 이유는
        /// 셰이더가 없는 것처럼 프레임마다 달라지지 않는 것들이고, 2Hz 로 다시 시도하면
        /// 실패 로그가 화면을 덮는다.
        private void RefreshPreview(byte characterId, bool connected)
        {
            if (_preview == null || !connected)
            {
                return;
            }

            if (!LobbyCharacterCatalog.IsValidId(characterId))
            {
                return;
            }

            if (_stage == null && !_stageTried)
            {
                _stageTried = true;
                _stage = CharacterPreview.Create();

                if (_stage?.Texture != null)
                {
                    _preview.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_stage.Texture));
                }
            }

            _stage?.Show(characterId);
        }

        private readonly struct Row
        {
            public Row(VisualElement element, Label state)
            {
                Element = element;
                State = state;
            }

            public VisualElement Element { get; }

            public Label State { get; }
        }
    }
}
