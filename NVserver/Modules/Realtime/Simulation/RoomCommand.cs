namespace NV.Realtime.Simulation
{
    internal enum RoomCommandKind : byte
    {
        Join = 0,
        Leave = 1,
    }

    /// HTTP·WebSocket 스레드가 룸 상태를 직접 바꾸지 않고 큐에 넣는 단위.
    /// 틱 루프가 순회하는 컬렉션을 다른 스레드가 변경하면 안 된다.
    ///
    /// 세션 객체가 아니라 식별자만 싣는다. 룸은 전송 계층을 알지 않아도 되고,
    /// 그래야 소켓 없이 룸을 테스트할 수 있다.
    internal readonly struct RoomCommand
    {
        private RoomCommand(RoomCommandKind kind, int sessionId, byte playerId)
        {
            Kind = kind;
            SessionId = sessionId;
            PlayerId = playerId;
        }

        public RoomCommandKind Kind { get; }

        public int SessionId { get; }

        public byte PlayerId { get; }

        public static RoomCommand Join(int sessionId, byte playerId)
        {
            return new RoomCommand(RoomCommandKind.Join, sessionId, playerId);
        }

        public static RoomCommand Leave(int sessionId, byte playerId)
        {
            return new RoomCommand(RoomCommandKind.Leave, sessionId, playerId);
        }
    }
}
