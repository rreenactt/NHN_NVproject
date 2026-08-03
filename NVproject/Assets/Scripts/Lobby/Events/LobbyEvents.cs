using System;

namespace NV.Client.Lobby.Events
{
    /// 로비 화면의 갱신 신호.
    ///
    /// static 클래스로 만들지 않았다. 뷰는 도메인 리로드마다 통째로 새로 만들어지는데
    /// static 이벤트는 리로드를 넘어 살아남는 구독자를 남길 수 있고, 그러면 죽은
    /// `VisualElement` 를 건드리는 핸들러가 프레임마다 예외를 던진다. 인스턴스를
    /// 컨트롤러가 소유하면 컨트롤러와 함께 사라진다.
    ///
    /// 구독자는 `LobbyUIController` 하나뿐이다. 뷰까지 각자 구독하게 만들면 해제
    /// 지점이 뷰 수만큼 생기고, 하나만 빠뜨려도 증상이 "가끔 화면이 두 번 그려진다"
    /// 로만 나타난다. 여기서 받아 뷰로 직접 내려보낸다.
    public sealed class LobbyEvents
    {
        /// 모델 전반이 바뀌었다. 화면 전체를 다시 그린다.
        public event Action ModelChanged;

        /// 방 목록이 바뀌었다(목록·비공개·실패 어느 쪽이든).
        public event Action RoomListChanged;

        /// 서버 연결 상태 또는 온라인 인원이 바뀌었다.
        public event Action ConnectionChanged;

        /// 표시 이름·서버 주소가 바뀌었다.
        public event Action ProfileChanged;

        /// 화면 구석에 한 줄 띄운다.
        public event Action<string, bool> ToastRequested;

        public void RaiseModelChanged()
        {
            ModelChanged?.Invoke();
        }

        public void RaiseRoomListChanged()
        {
            RoomListChanged?.Invoke();
        }

        public void RaiseConnectionChanged()
        {
            ConnectionChanged?.Invoke();
        }

        public void RaiseProfileChanged()
        {
            ProfileChanged?.Invoke();
        }

        /// <param name="isError">붉게 띄운다. 실패가 성공과 같은 색이면 읽히지 않는다.</param>
        public void Toast(string message, bool isError = false)
        {
            if (!string.IsNullOrEmpty(message))
            {
                ToastRequested?.Invoke(message, isError);
            }
        }
    }
}
