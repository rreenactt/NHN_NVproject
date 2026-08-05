using NV.Client.Lobby.Events;
using NV.Client.Lobby.Services;
using NV.Client.Lobby.UI;
using NV.Client.Net.Session;
using UnityEngine;

namespace NV.Client.Lobby.Controllers
{
    /// 화면 흐름. 버튼이 눌렸을 때 무엇이 일어나는지가 여기 모여 있다.
    ///
    /// 뷰가 서비스를 직접 부르지 않게 하는 것이 이 클래스의 목적이다. 뷰마다 서비스를
    /// 쥐고 있으면 "방에 들어가는 경로" 가 네 파일에 흩어지고, 그중 하나만 로딩
    /// 오버레이를 안 걷는 식의 어긋남이 생긴다.
    public sealed class LobbyController
    {
        private readonly NetSession _session;
        private readonly LobbyEvents _events;
        private readonly LobbyService _lobby;
        private readonly RoomService _rooms;
        private readonly MapChoiceService _maps;
        private readonly LobbyUIController _ui;

        public LobbyController(
            NetSession session,
            LobbyEvents events,
            LobbyService lobby,
            RoomService rooms,
            MapChoiceService maps,
            LobbyUIController ui)
        {
            _session = session;
            _events = events;
            _lobby = lobby;
            _rooms = rooms;
            _maps = maps;
            _ui = ui;

            ui.OnCreateRoom = OpenCreateRoom;
            ui.OnJoinByCode = OpenJoinByCode;
            ui.OnQuickJoin = QuickJoin;
            ui.OnSettings = OpenSettings;
            ui.OnQuit = Quit;
            ui.OnRefresh = RefreshRooms;
            ui.OnRetry = Retry;
            ui.OnJoinRoom = JoinRoom;
        }

        /// 세션 상태가 바뀌었다. 상태 줄과 목록을 맞춘다.
        ///
        /// **방 안의 화면을 여기서 열지 않는다.** 대기방은 별개의 씬이고 그 전환은
        /// `SessionSceneRouter` 가 세션 단계를 보고 한다 — 방에 들어가는 길이 넷이므로
        /// (만들기·코드·목록·빠른 참가) 각자 화면을 바꾸게 하면 하나를 빠뜨렸을 때 방에는
        /// 들어갔는데 화면은 로비인 상태가 된다.
        public void OnSessionChanged()
        {
            _ui.RefreshStatus();
            _ui.RefreshRooms();
        }

        // ==================================================== 버튼

        private void OpenCreateRoom()
        {
            CreateRoomPopup.Open(_ui.Popups, _maps, (mapId, isPublic) => _rooms.Create(mapId, isPublic));
        }

        private void OpenJoinByCode()
        {
            JoinByCodePopup.Open(_ui.Popups, code => _rooms.JoinByCode(code));
        }

        private void JoinRoom(RoomInfo room)
        {
            _rooms.Join(room);
        }

        private void QuickJoin()
        {
            if (!_rooms.CanQuickJoin(out var reason))
            {
                _events.Toast(reason, true);
                return;
            }

            _rooms.QuickJoin();
            _ui.RefreshRooms();
        }

        private void RefreshRooms()
        {
            _rooms.Refresh();
        }

        private void OpenSettings()
        {
            SettingsPopup.Open(_ui.Popups, (name, host, secure) =>
            {
                if (!_lobby.SaveProfile(name, host, secure))
                {
                    return false;
                }

                _events.Toast("설정을 저장했다.");
                return true;
            });
        }

        private void Retry()
        {
            _session.Retry();
        }

        /// 게임을 끝낸다.
        ///
        /// 확인을 붙이는 이유는 되돌릴 수 없기 때문이다. 방에 들어가 있으면 나가는
        /// 것까지 함께 일어나므로 그 사실을 문구에 적는다.
        private void Quit()
        {
            var inRoom = _session.State == SessionState.InLobby
                || _session.State == SessionState.InGame
                || _session.State == SessionState.Ended;

            ConfirmDialog.Open(
                _ui.Popups,
                "게임 종료",
                inRoom ? "방에서 나가고 게임을 종료한다." : "게임을 종료한다.",
                Shutdown);
        }

        private void Shutdown()
        {
            _session.Leave();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
