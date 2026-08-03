using UnityEngine;
using UnityEngine.UIElements;

namespace NV.Client.Lobby
{
    /// 메인 로비의 UI 에셋을 읽는 유일한 지점.
    ///
    /// `Resources.Load` 호출과 경로 문자열을 여기 한 곳에 가둔다. 나중에 Addressables 로
    /// 옮기는 일이 이 파일 하나를 고치는 일이 되도록 하기 위한 것이다 — 뷰마다 경로를
    /// 들고 있으면 그때 열 파일이 열 개가 된다.
    ///
    /// 캐시하지 않는다. `Resources.Load` 는 이미 로드된 에셋을 다시 읽지 않고, 캐시를
    /// 두면 도메인 리로드에서 static 필드가 비면서 "로드했다고 믿는 null" 이 생긴다.
    public static class MainLobbyAssets
    {
        private const string Root = "UI/MainLobby/";
        private const string TemplateRoot = Root + "templates/";

        public const string ScreenName = Root + "MainLobby";
        public const string StyleName = Root + "main-lobby";
        public const string PanelName = Root + "MainLobbyPanelSettings";

        public static VisualTreeAsset Screen()
        {
            return Resources.Load<VisualTreeAsset>(ScreenName);
        }

        public static StyleSheet Style()
        {
            return Resources.Load<StyleSheet>(StyleName);
        }

        public static PanelSettings Panel()
        {
            return Resources.Load<PanelSettings>(PanelName);
        }

        /// 템플릿 하나. 이름은 확장자 없는 파일 이름이다(`RoomItem`).
        public static VisualTreeAsset Template(string name)
        {
            return Resources.Load<VisualTreeAsset>(TemplateRoot + name);
        }

        /// 템플릿을 복제해 첫 자식을 돌려준다.
        ///
        /// `Instantiate()` 는 `TemplateContainer` 로 한 겹 감싼 것을 준다. 그 껍데기가
        /// 남으면 USS 의 flex 규칙이 한 단계 어긋난 곳에 걸려 목록 행이 늘어나거나
        /// 겹친다. 실제로 쓰는 것은 안쪽 요소이므로 여기서 벗겨 낸다.
        public static VisualElement Clone(string name)
        {
            var asset = Template(name);

            if (asset == null)
            {
                Debug.LogError($"[MainLobby] 템플릿 {TemplateRoot}{name} 을 찾을 수 없다.");
                return null;
            }

            var container = asset.Instantiate();

            return container.childCount > 0 ? container[0] : container;
        }
    }
}
