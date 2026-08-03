using NV.Client.Lobby.UI;
using NV.Client.Net.Session;

namespace NV.Client.Lobby.Controllers
{
    /// 방 안 화면의 수명을 세션 상태에 맞춘다.
    ///
    /// 방에 들어가면 열고, 나가거나 실패하면 닫는다. 여는 시점을 버튼 클릭에 묶지
    /// 않는 이유는 들어가는 경로가 넷이기 때문이다 — 방 만들기, 코드 참가, 목록에서
    /// 참가, 빠른 참가. 그 넷이 각자 팝업을 열게 하면 하나를 빠뜨렸을 때 방에는
    /// 들어갔는데 화면은 로비인 상태가 된다.
    ///
    /// 상태를 보고 여닫으므로 자동 재시도로 다시 붙은 경우에도 저절로 맞는다.
    public sealed class RoomController
    {
        private readonly NetSession _session;
        private readonly LobbyUIController _ui;

        private RoomView _view;
        private bool _open;

        public RoomController(NetSession session, LobbyUIController ui)
        {
            _session = session;
            _ui = ui;
        }

        /// 방 안에 있다고 볼 상태인가.
        ///
        /// `InGame` 을 포함한다. 매치가 시작되면 `SessionSceneRouter` 가 게임 씬을 여는데,
        /// 씬이 바뀌기까지 몇 프레임이 걸린다. 그 사이에 이 화면을 닫으면 로비가
        /// 한순간 비어 보인다.
        private bool ShouldBeOpen =>
            _session.State == SessionState.InLobby
            || _session.State == SessionState.InGame
            || _session.State == SessionState.Ended;

        /// 세션 상태가 바뀔 때마다 부른다.
        public void Sync()
        {
            if (ShouldBeOpen)
            {
                if (!_open)
                {
                    Open();
                }

                _view?.Refresh();
                return;
            }

            if (_open)
            {
                Close();
            }
        }

        private void Open()
        {
            _view = new RoomView(_session, Leave);

            if (_view.Root == null)
            {
                return;
            }

            _open = true;

            // 모달이다. 방 안에 있는 동안 뒤의 목록을 눌러 다른 방으로 새는 것은
            // 지금 방을 조용히 버리는 일이다.
            _ui.Popups.Open(_view.Root, OnClosed, modal: true);
        }

        private void Close()
        {
            _open = false;
            _view = null;

            // 방 화면은 항상 맨 위에 있다. 모달이라 그 위에 열릴 수 있는 것은
            // 확인 대화상자뿐이고, 그것은 자기가 닫고 나서 이 경로로 온다.
            _ui.Popups.CloseAll();
        }

        /// 사용자가 팝업을 닫았을 때. 모달이므로 실제로는 나가기 버튼뿐이다.
        private void OnClosed()
        {
            _open = false;
            _view = null;
        }

        private void Leave()
        {
            _session.Leave();
        }
    }
}
