using System;
using UnityEngine.UIElements;

namespace NV.Client.Lobby.UI
{
    /// 예/아니오 하나짜리 확인 대화상자.
    ///
    /// 되돌릴 수 없는 것에만 쓴다 — 게임 종료, 그리고 강제 퇴장. 확인을 남발하면
    /// 사용자는 읽지 않고 누르게 되고, 그러면 정작 물어야 할 때 물어도 소용이 없다.
    ///
    /// 강제 퇴장이 여기 들어오는 이유는 **대상이 아무 잘못이 없을 수 있다는 것**이다.
    /// 잘못 누르면 남의 판을 끝내고, 그 사람은 이유를 알 수 없다.
    public static class ConfirmDialog
    {
        public static void Open(PopupHost host, string title, string body, Action onConfirm)
        {
            var element = MainLobbyAssets.Clone("ConfirmDialog");

            if (element == null || host == null)
            {
                return;
            }

            var titleLabel = element.Q<Label>("confirm-title");
            var bodyLabel = element.Q<Label>("confirm-body");
            var ok = element.Q<Button>("confirm-ok");
            var cancel = element.Q<Button>("confirm-cancel");

            if (titleLabel != null)
            {
                titleLabel.text = title;
            }

            if (bodyLabel != null)
            {
                bodyLabel.text = body;
            }

            // 확인을 눌렀는지 취소·Esc 로 닫혔는지 구분한다. `onClose` 는 어느
            // 경로로든 불리므로 그것만으로는 판정할 수 없다.
            var confirmed = false;

            if (ok != null)
            {
                ok.clicked += () =>
                {
                    confirmed = true;
                    host.CloseTop();
                };
            }

            if (cancel != null)
            {
                cancel.clicked += () => host.CloseTop();
            }

            host.Open(element, () =>
            {
                if (confirmed)
                {
                    onConfirm?.Invoke();
                }
            });
        }
    }
}
