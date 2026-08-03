namespace NV.Client.Net.Session
{
    /// 세션이 지금 무엇을 하고 있는가. UI 가 이 값 하나로 화면을 고른다.
    ///
    /// `ConnectionState` 와 역할이 다르다. 그쪽은 소켓과 핸드셰이크의
    /// 단계이고 여기는 사용자가 보는 흐름의 단계다. 둘을 하나로 합치면 "방을 만드는
    /// 중" 과 "소켓을 여는 중" 이 같은 상태가 되어, 화면에 무엇을 기다리는지 쓸 수 없다.
    public enum SessionState
    {
        /// 아무것도 하지 않는다. 첫 화면이다.
        Idle,

        /// 방 만들기 요청을 보냈다. 코드를 기다린다.
        Creating,

        /// 코드로 방 상태를 확인하는 중. 여기서 실패 사유가 갈린다.
        Resolving,

        /// 소켓을 여는 중.
        Connecting,

        /// 소켓은 열렸고 Welcome 을 기다린다.
        Handshaking,

        /// 방에 들어와 명단을 보고 있다. 아직 매치가 아니다.
        InLobby,

        /// 매치 진행 중.
        InGame,

        /// 결과 화면. 방장이 로비로 되돌릴 수 있다.
        Ended,

        /// 스스로 나가는 중.
        Leaving,

        /// 실패했다. 사유는 `SessionFailure` 가 들고 있다.
        Failed,
    }
}
