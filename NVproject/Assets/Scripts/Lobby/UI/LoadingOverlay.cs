using UnityEngine.UIElements;

namespace NV.Client.Lobby.UI
{
    /// 화면을 덮는 진행 표시.
    ///
    /// 겹침을 센다. 두 작업이 동시에 진행 중일 때 먼저 끝난 쪽이 `Hide()` 를 부르면
    /// 아직 일하고 있는 쪽의 오버레이까지 걷혀, 화면이 조작 가능해 보이는 채로 실제로는
    /// 응답하지 않는 상태가 된다. bool 하나로는 이 경우를 표현할 수 없다.
    public sealed class LoadingOverlay
    {
        private readonly VisualElement _root;
        private readonly Label _text;

        private int _depth;

        public LoadingOverlay(VisualElement root)
        {
            _root = root;
            _text = root?.Q<Label>("loading-text");

            Sync();
        }

        public bool IsVisible => _depth > 0;

        public void Show(string reason)
        {
            _depth++;

            if (_text != null && !string.IsNullOrEmpty(reason))
            {
                _text.text = reason;
            }

            Sync();
        }

        public void Hide()
        {
            if (_depth > 0)
            {
                _depth--;
            }

            Sync();
        }

        /// 세어 둔 것을 버리고 즉시 걷는다. 화면을 다시 만들 때만 쓴다.
        public void Reset()
        {
            _depth = 0;
            Sync();
        }

        private void Sync()
        {
            if (_root == null)
            {
                return;
            }

            _root.style.display = _depth > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _root.pickingMode = _depth > 0 ? PickingMode.Position : PickingMode.Ignore;
        }
    }
}
