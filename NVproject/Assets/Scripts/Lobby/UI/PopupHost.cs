using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace NV.Client.Lobby.UI
{
    /// 팝업을 한 곳에서 쌓고 걷는다.
    ///
    /// 스택인 이유는 겹치기 때문이다. 설정에서 확인 대화상자를 띄우면 둘이 동시에 열려
    /// 있고, `Esc` 는 맨 위 하나만 닫아야 한다. 화면마다 자기 팝업을 열게 두면 그 순서를
    /// 아무도 갖고 있지 않게 된다.
    ///
    /// 닫을 때 `display: none` 이 아니라 트리에서 뗀다. 보이지 않는 요소에 포커스가
    /// 남으면 키 입력이 화면 어디에도 없는 곳으로 흘러가고, 증상은 "가끔 Esc 가 안
    /// 먹는다" 로만 나타난다.
    public sealed class PopupHost
    {
        private readonly VisualElement _root;
        private readonly List<Layer> _layers = new List<Layer>();

        public PopupHost(VisualElement root)
        {
            _root = root;
            Sync();
        }

        public bool HasOpen => _layers.Count > 0;

        public int Count => _layers.Count;

        /// 팝업 하나를 연다.
        ///
        /// <param name="content">템플릿에서 복제한 팝업 본체.</param>
        /// <param name="onClose">닫힐 때 한 번 불린다. 취소·Esc·바깥 클릭 전부 포함이다.</param>
        /// <param name="modal">true 면 바깥을 눌러도 닫히지 않는다. 되돌릴 수 없는 작업에만.</param>
        public void Open(VisualElement content, Action onClose = null, bool modal = false)
        {
            if (content == null || _root == null)
            {
                return;
            }

            var dim = new VisualElement();
            dim.AddToClassList("popup-dim");
            dim.Add(content);

            var layer = new Layer(dim, content, onClose);

            if (!modal)
            {
                // 본체를 누른 것이 바깥을 누른 것으로 새지 않게 한다. 클릭은 위로
                // 전파되므로 딤에서만 받으면 팝업 안을 눌러도 닫힌다.
                dim.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.target == dim)
                    {
                        Close(layer);
                    }
                });
            }

            _layers.Add(layer);
            _root.Add(dim);

            Sync();

            // 열자마자 키를 받게 한다. 그러지 않으면 첫 Esc 가 무시된다.
            content.focusable = true;
            content.Focus();
        }

        /// 맨 위 하나를 닫는다. 열린 것이 있었으면 true.
        public bool CloseTop()
        {
            if (_layers.Count == 0)
            {
                return false;
            }

            Close(_layers[_layers.Count - 1]);
            return true;
        }

        public void CloseAll()
        {
            while (_layers.Count > 0)
            {
                Close(_layers[_layers.Count - 1]);
            }
        }

        private void Close(Layer layer)
        {
            if (!_layers.Remove(layer))
            {
                return;
            }

            layer.Dim.RemoveFromHierarchy();
            Sync();

            layer.OnClose?.Invoke();
        }

        /// 열린 것이 없으면 루트 자체를 치운다.
        ///
        /// 화면 전체를 덮는 빈 컨테이너를 남겨 두면 그 아래 버튼이 눌리지 않는다.
        /// 증상은 "로비 버튼이 전부 죽었다" 이고 원인은 보이지 않는다.
        private void Sync()
        {
            if (_root == null)
            {
                return;
            }

            _root.style.display = _layers.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _root.pickingMode = _layers.Count > 0 ? PickingMode.Position : PickingMode.Ignore;
        }

        private sealed class Layer
        {
            public Layer(VisualElement dim, VisualElement content, Action onClose)
            {
                Dim = dim;
                Content = content;
                OnClose = onClose;
            }

            public VisualElement Dim { get; }

            public VisualElement Content { get; }

            public Action OnClose { get; }
        }
    }
}
