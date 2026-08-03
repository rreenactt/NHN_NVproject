using System;
using NV.Client.Lobby.Models;
using NV.Client.Net.Session;
using UnityEngine.UIElements;

namespace NV.Client.Lobby.UI
{
    /// 설정 팝업. 표시 이름과 서버 주소.
    ///
    /// 접속 중에는 저장이 거부된다(`NetSession.CanConfigure`). 주소를 바꾸면 자동
    /// 재시도가 방이 있는 서버가 아니라 새 주소를 두드리고, 그 실패는 화면에서
    /// "방이 사라졌다" 로 보인다. 버튼을 끄고 이유를 함께 적는다.
    public static class SettingsPopup
    {
        /// <param name="onSave">이름·주소·secure. 저장에 성공하면 true 를 돌려준다.</param>
        public static void Open(PopupHost host, Func<string, string, bool, bool> onSave)
        {
            var element = MainLobbyAssets.Clone("SettingsPopup");

            if (element == null || host == null)
            {
                return;
            }

            var nameField = element.Q<TextField>("settings-name");
            var nameNote = element.Q<Label>("settings-name-note");
            var hostField = element.Q<TextField>("settings-host");
            var secureToggle = element.Q<Toggle>("settings-secure");
            var lockNote = element.Q<Label>("settings-lock-note");
            var save = element.Q<Button>("settings-save");
            var cancel = element.Q<Button>("settings-cancel");

            nameField?.SetValueWithoutNotify(PlayerProfile.DisplayName);
            hostField?.SetValueWithoutNotify(PlayerProfile.Host);
            secureToggle?.SetValueWithoutNotify(PlayerProfile.Secure);

            void SyncName()
            {
                if (nameNote == null)
                {
                    return;
                }

                var raw = nameField != null ? nameField.value : string.Empty;

                // 서버는 출력 가능한 ASCII 만 남기고 12자로 자른다. 같은 규칙을 여기서
                // 미리 보여 주지 않으면, 한글 이름을 넣은 사람은 명단에서 자기 이름이
                // 사라진 것을 버그로 신고하게 된다.
                nameNote.text = PlayerProfile.WouldChange(raw)
                    ? $"서버에는 '{PlayerProfile.Sanitize(raw)}' 로 전달된다 (ASCII {PlayerProfile.MaxNameLength}자)"
                    : string.Empty;
            }

            nameField?.RegisterValueChangedCallback(_ => SyncName());
            SyncName();

            var canConfigure = NetSession.Current.CanConfigure;

            // 주소는 두 가지 이유로 잠긴다. 접속 중이면 잠시, 이 환경이 주소 변경을
            // 허용하지 않으면 계속. 이유가 다르므로 문구도 갈라 적는다 — "바꿀 수 없다"
            // 한 줄만 보이면 방에서 나가면 풀린다고 읽는다.
            var canChangeHost = canConfigure && PlayerProfile.CanChangeHost;

            nameField?.SetEnabled(canConfigure);
            hostField?.SetEnabled(canChangeHost);
            secureToggle?.SetEnabled(canChangeHost);
            save?.SetEnabled(canConfigure);

            if (lockNote != null)
            {
                if (!canConfigure)
                {
                    lockNote.text = "접속 중에는 바꿀 수 없다. 방에서 나간 뒤 다시 연다.";
                }
                else if (!PlayerProfile.CanChangeHost)
                {
                    lockNote.text = "이 빌드는 서버가 정해져 있다. 이름만 바꿀 수 있다.";
                }
                else
                {
                    lockNote.text = string.Empty;
                }
            }

            if (save != null)
            {
                save.clicked += () =>
                {
                    var ok = onSave != null && onSave(
                        nameField != null ? nameField.value : string.Empty,
                        hostField != null ? hostField.value : string.Empty,
                        secureToggle != null && secureToggle.value);

                    if (ok)
                    {
                        host.CloseTop();
                    }
                    else if (lockNote != null)
                    {
                        lockNote.text = "저장하지 못했다. 접속 중이 아닌지 확인한다.";
                    }
                };
            }

            if (cancel != null)
            {
                cancel.clicked += () => host.CloseTop();
            }

            host.Open(element);
        }
    }
}
