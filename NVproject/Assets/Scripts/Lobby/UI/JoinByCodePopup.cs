using System;
using NV.Client.Net.Session;
using UnityEngine.UIElements;

namespace NV.Client.Lobby.UI
{
    /// 초대 코드로 참가하는 팝업.
    ///
    /// 요구사항의 `RoomPasswordPopup` 자리를 대신한다. 서버에 방 비밀번호라는 개념이
    /// 없고, 이 게임에서 남의 방에 들어가는 열쇠는 초대 코드다.
    ///
    /// 형식 검사를 여기서 먼저 한다. 틀린 코드를 서버까지 보내면 두 가지를 잃는다 —
    /// 오타와 없는 방이 브라우저에서 같은 실패로 보이고, `GET /rooms/{code}` 가 `/ws` 와
    /// 공유하는 분당 60회 예산을 오타가 갉아먹는다.
    public static class JoinByCodePopup
    {
        public static void Open(PopupHost host, Action<string> onJoin)
        {
            var element = MainLobbyAssets.Clone("JoinByCodePopup");

            if (element == null || host == null)
            {
                return;
            }

            var field = element.Q<TextField>("join-code");
            var hint = element.Q<Label>("join-hint");
            var confirm = element.Q<Button>("join-confirm");
            var cancel = element.Q<Button>("join-cancel");

            void Sync()
            {
                var normalized = InviteCodeText.Normalize(field != null ? field.value : string.Empty);

                if (hint != null)
                {
                    hint.text = InviteCodeText.Hint(normalized);
                }

                confirm?.SetEnabled(InviteCodeText.IsValid(normalized));
            }

            if (field != null)
            {
                field.RegisterValueChangedCallback(change =>
                {
                    // 화면에는 대문자로 남기고 내부 표현은 소문자다. 정규화가 한 곳에만
                    // 있으므로 붙여넣기에 섞인 공백과 하이픈도 같은 자리에서 사라진다.
                    var normalized = InviteCodeText.Normalize(change.newValue);
                    var display = InviteCodeText.ToDisplay(normalized);

                    if (!string.Equals(change.newValue, display, StringComparison.Ordinal))
                    {
                        field.SetValueWithoutNotify(display);
                    }

                    Sync();
                });

                // 링크로 실행된 경우 코드를 채워 준다. 자동 참가는 하지 않는다 —
                // 잘못 눌렀을 때 되돌릴 화면이 없어진다. 쿼리스트링은 사용자가 고칠 수
                // 있는 입력이므로 형식 검사를 반드시 통과시킨다.
                var launch = InviteCodeText.Normalize(InviteLink.ReadCodeFromLaunchUrl());

                if (InviteCodeText.IsValid(launch))
                {
                    field.SetValueWithoutNotify(InviteCodeText.ToDisplay(launch));
                }
            }

            Sync();

            if (confirm != null)
            {
                confirm.clicked += () =>
                {
                    var code = field != null ? field.value : string.Empty;

                    host.CloseTop();
                    onJoin?.Invoke(code);
                };
            }

            if (cancel != null)
            {
                cancel.clicked += () => host.CloseTop();
            }

            host.Open(element);

            field?.Focus();
        }
    }
}
