using NV.Client.Lobby.UI;
using NV.Client.Net.Session;

namespace NV.Client.Lobby.Controllers
{
    /// 어느 페이지가 보이는지를 세션 상태에 맞춘다.
    ///
    /// 옛 `RoomController` 를 승계한다. 그것은 방 팝업을 여닫았고 이것은 페이지를 바꾼다.
    ///
    /// 페이지 전환을 버튼 클릭에 묶지 않는 이유는 방에 들어가는 경로가 넷이기 때문이다 —
    /// 방 만들기, 코드 참가, 목록에서 참가, 빠른 참가. 그 넷이 각자 화면을 바꾸게 하면
    /// 하나를 빠뜨렸을 때 방에는 들어갔는데 화면은 로비인 상태가 된다.
    ///
    /// 상태를 보고 바꾸므로 자동 재시도로 다시 붙은 경우에도 저절로 맞는다.
    public sealed class GameLobbyController
    {
        private readonly NetSession _session;
        private readonly LobbyUIController _ui;

        private bool _inRoom;

        public GameLobbyController(NetSession session, LobbyUIController ui)
        {
            _session = session;
            _ui = ui;

            _ui.GameLobby.OnStart = Start;
            _ui.GameLobby.OnToggleReady = ToggleReady;
            _ui.GameLobby.Characters.OnPick = PickCharacter;
            _ui.GameLobby.OnKick = Kick;
            _ui.GameLobby.OnTransferHost = TransferHost;
            _ui.GameLobby.OnLeave = Leave;
        }

        /// 방 안에 있다고 볼 상태인가.
        ///
        /// `InGame` 을 포함한다. 매치가 시작되면 `SessionSceneRouter` 가 게임 씬을 여는데,
        /// 씬이 바뀌기까지 몇 프레임이 걸린다. 그 사이에 로비 페이지로 돌리면 방 화면이
        /// 한순간 사라지고 목록이 번쩍인다.
        private bool ShouldBeInRoom =>
            _session.State == SessionState.InLobby
            || _session.State == SessionState.InGame
            || _session.State == SessionState.Ended;

        /// 세션 상태가 바뀔 때마다 부른다.
        public void Sync()
        {
            if (ShouldBeInRoom)
            {
                if (!_inRoom)
                {
                    Enter();
                }

                _ui.GameLobby.Refresh();
                return;
            }

            if (_inRoom)
            {
                Exit();
            }
        }

        private void Enter()
        {
            _inRoom = true;

            // 들어오는 길에 열려 있던 팝업을 걷는다. 방 만들기와 코드 참가는 스스로 닫지만,
            // 실패로 되돌아왔다가 다시 붙은 경우처럼 남아 있을 수 있는 길이 있다. 방 안에서
            // 뒤에 남은 참가 팝업은 지금 방을 조용히 버리는 버튼이다.
            _ui.Popups.CloseAll();

            _ui.GameLobby.Reset();
            _ui.ShowRoomPage(true);
        }

        private void Exit()
        {
            _inRoom = false;
            _ui.ShowRoomPage(false);
        }

        /// 방장이 매치 시작을 요청한다. 자격·인원은 서버가 다시 본다.
        private void Start()
        {
            _session.RequestStart();
        }

        /// 준비를 켜거나 끈다. 눌린 모양은 다음 명단 전문이 만든다.
        private void ToggleReady(bool ready)
        {
            _session.SetReady(ready);
        }

        /// 캐릭터를 요청한다. 범위와 중복은 서버가 본다 — 거부되면 다음 명단 전문이 여전히
        /// 전에 입던 것을 말해 준다.
        private void PickCharacter(byte characterId)
        {
            _session.SetCharacter(characterId);
        }

        /// 강제 퇴장. **확인을 지난다.**
        ///
        /// 되돌릴 수 없고 대상이 아무 잘못이 없을 수 있다. 잘못 누르면 남의 판을 끝내고,
        /// 그 사람은 이유를 알 수 없다.
        private void Kick(byte playerId, string name)
        {
            ConfirmDialog.Open(
                _ui.Popups,
                "내보내기",
                $"{name} 을(를) 방에서 내보낸다. 되돌릴 수 없다.",
                () => _session.KickPlayer(playerId));
        }

        /// 방장 위임. **확인을 지난다.**
        ///
        /// 넘긴 뒤에는 되돌릴 수 없다 — 되돌리려면 새 방장이 다시 넘겨 주어야 한다.
        private void TransferHost(byte playerId, string name)
        {
            ConfirmDialog.Open(
                _ui.Popups,
                "방장 넘기기",
                $"{name} 에게 방장을 넘긴다. 시작 권한도 함께 넘어간다.",
                () => _session.TransferHost(playerId));
        }

        private void Leave()
        {
            _session.Leave();
        }
    }
}
