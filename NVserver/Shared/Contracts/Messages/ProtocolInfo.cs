namespace NV.Shared.Contracts.Messages
{
    /// 와이어 포맷의 불변값. 클라이언트와 서버가 다른 시점에 빌드되므로
    /// Version 불일치는 접속 거부로 처리한다. 이 핸드셰이크가 유일한 방어선이다.
    public static class ProtocolInfo
    {
        /// 2 부터 초대 코드 세션이다. `Control(0x02)` 과 `Event(0x82)` 의 룸 상태 전문이
        /// 추가되었고, 룸은 코드로만 참가할 수 있다.
        ///
        /// 올릴 때는 서버와 클라이언트를 같은 커밋에 배포해야 한다. 구버전 클라이언트는
        /// 업그레이드 전에 426 으로 전부 거부되며, WebGL 빌드는 수 분이 걸린다.
        public const ushort Version = 2;

        /// 손실 대비로 한 메시지에 실어 보내는 입력 프레임 수의 상한.
        public const int MaxInputFramesPerMessage = 3;

        /// 표시 이름의 바이트 상한. ASCII 로 걸러 저장하므로 문자 수와 같다.
        ///
        /// 상한이 필요한 이유는 화면이 아니라 와이어다. 룸 상태 전문은 8명치 이름을
        /// 한 프레임에 싣고, 그 프레임이 세션 수신 버퍼보다 커지면 접속이 끊긴다.
        public const int MaxDisplayNameBytes = 12;

        /// 접속 URL 쿼리 키. 업그레이드 전에 버전을 검사하려면 쿼리여야 한다.
        public const string VersionQueryKey = "v";

        public const string RoomQueryKey = "room";

        /// 방장임을 주장하는 토큰. 룸을 만든 응답으로만 받으며, 접속 시 한 번만 쓴다.
        /// 이후 시작 권한은 "그 세션이 방장 세션인가" 로 판정한다 — 매 요청에 토큰을
        /// 요구하면 방장이 나갔을 때 남은 사람에게 토큰을 줄 방법이 없어 승계가 막힌다.
        public const string TokenQueryKey = "token";

        /// 표시 이름. 브라우저는 핸드셰이크에 헤더를 붙일 수 없으므로 쿼리로 받는다.
        public const string NameQueryKey = "name";
    }
}
