namespace NV.Shared.Contracts.Messages
{
    /// 와이어 포맷의 불변값. 클라이언트와 서버가 다른 시점에 빌드되므로
    /// Version 불일치는 접속 거부로 처리한다. 이 핸드셰이크가 유일한 방어선이다.
    public static class ProtocolInfo
    {
        public const ushort Version = 1;

        /// 손실 대비로 한 메시지에 실어 보내는 입력 프레임 수의 상한.
        public const int MaxInputFramesPerMessage = 3;

        /// 접속 URL 쿼리 키. 업그레이드 전에 버전을 검사하려면 쿼리여야 한다.
        public const string VersionQueryKey = "v";

        public const string RoomQueryKey = "room";
    }
}
