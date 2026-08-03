using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace NV.Client.Lobby.UI
{
    /// 화면 구석에 잠깐 뜨는 한 줄.
    ///
    /// 팝업이 아니다. 사용자의 다음 행동을 요구하지 않는 알림(복사 성공, 후보 재시도)만
    /// 여기로 간다. 실패 중에서도 **다음 행동이 있는 것**은 상태 줄이 맡는다 —
    /// `SessionFailure` 가 사유와 다음 행동을 짝으로 들고 있고, 3초 뒤 사라지는 자리에
    /// 그것을 띄우면 읽기 전에 없어진다.
    ///
    /// 시간은 `Time.unscaledTime` 으로 잰다. 매치가 시작되며 `timeScale` 이 바뀌어도
    /// 로비의 알림은 같은 속도로 사라져야 한다.
    public sealed class ToastMessage
    {
        private const float LifetimeSeconds = 3f;
        private const int MaxVisible = 3;

        private readonly VisualElement _root;
        private readonly List<Entry> _entries = new List<Entry>();

        public ToastMessage(VisualElement root)
        {
            _root = root;
        }

        public void Show(string message, bool isError)
        {
            if (_root == null || string.IsNullOrEmpty(message))
            {
                return;
            }

            var element = MainLobbyAssets.Clone("ToastMessage");

            if (element == null)
            {
                return;
            }

            var label = element.Q<Label>("toast-text");
            if (label != null)
            {
                label.text = message;
            }

            if (isError)
            {
                element.AddToClassList("toast-error");
            }

            _root.Add(element);
            _entries.Add(new Entry(element, Time.unscaledTime + LifetimeSeconds));

            // 오래된 것부터 밀어낸다. 쌓이면 화면 절반을 덮는다.
            while (_entries.Count > MaxVisible)
            {
                Remove(0);
            }
        }

        /// 만료된 것을 걷는다. 컨트롤러의 `Update` 가 부른다.
        public void Tick()
        {
            var now = Time.unscaledTime;

            for (var index = _entries.Count - 1; index >= 0; index--)
            {
                if (now >= _entries[index].ExpiresAt)
                {
                    Remove(index);
                }
            }
        }

        public void Clear()
        {
            for (var index = _entries.Count - 1; index >= 0; index--)
            {
                Remove(index);
            }
        }

        private void Remove(int index)
        {
            _entries[index].Element.RemoveFromHierarchy();
            _entries.RemoveAt(index);
        }

        private readonly struct Entry
        {
            public Entry(VisualElement element, float expiresAt)
            {
                Element = element;
                ExpiresAt = expiresAt;
            }

            public VisualElement Element { get; }

            public float ExpiresAt { get; }
        }
    }
}
