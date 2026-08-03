namespace NV.Realtime.Simulation
{
    internal enum RoomCommandKind : byte
    {
        Join = 0,
        Leave = 1,

        /// 방장이 매치 시작을 요청했다. 자격과 단계는 틱 루프가 다시 본다.
        Start = 2,

        /// 방장이 매치 결과를 보고했다. 규칙 판정이 클라이언트에 있는 동안의 경로다.
        EndMatch = 3,

        /// 결과 화면에서 대기 단계로 되돌린다.
        ReturnToLobby = 4,
    }

    /// HTTP·WebSocket 스레드가 룸 상태를 직접 바꾸지 않고 큐에 넣는 단위.
    /// 틱 루프가 순회하는 컬렉션을 다른 스레드가 변경하면 안 된다.
    ///
    /// 세션 객체가 아니라 식별자와 값만 싣는다. 룸은 전송 계층을 알지 않아도 되고,
    /// 그래야 소켓 없이 룸을 테스트할 수 있다.
    internal readonly struct RoomCommand
    {
        private RoomCommand(
            RoomCommandKind kind,
            int sessionId,
            byte playerId,
            byte value,
            string name,
            bool isHost)
        {
            Kind = kind;
            SessionId = sessionId;
            PlayerId = playerId;
            Value = value;
            Name = name;
            IsHost = isHost;
        }

        public RoomCommandKind Kind { get; }

        public int SessionId { get; }

        public byte PlayerId { get; }

        /// 종류에 딸린 값. `EndMatch` 는 결과 코드, 나머지는 0 이다.
        public byte Value { get; }

        /// 표시 이름. 이름을 세션에서 룸으로 옮기는 유일한 경로다.
        public string Name { get; }

        /// 이 세션이 방장 토큰을 제시했는가. 판단은 접속 경로에서 끝났고
        /// 여기서는 결과만 옮긴다 — 룸이 토큰을 알 필요는 없다.
        public bool IsHost { get; }

        public static RoomCommand Join(int sessionId, byte playerId, string name = "", bool isHost = false)
        {
            return new RoomCommand(RoomCommandKind.Join, sessionId, playerId, 0, name ?? string.Empty, isHost);
        }

        public static RoomCommand Leave(int sessionId, byte playerId)
        {
            return new RoomCommand(RoomCommandKind.Leave, sessionId, playerId, 0, string.Empty, false);
        }

        public static RoomCommand Start(int sessionId)
        {
            return new RoomCommand(RoomCommandKind.Start, sessionId, 0, 0, string.Empty, false);
        }

        public static RoomCommand EndMatch(int sessionId, byte outcome)
        {
            return new RoomCommand(RoomCommandKind.EndMatch, sessionId, 0, outcome, string.Empty, false);
        }

        public static RoomCommand ReturnToLobby(int sessionId)
        {
            return new RoomCommand(RoomCommandKind.ReturnToLobby, sessionId, 0, 0, string.Empty, false);
        }
    }
}
