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

        /// 소켓 없는 봇 참가자를 하나 넣는다. 정적 룸에서만 받아들인다.
        ///
        /// 지금 이것을 붙이는 것은 틱 루프 자신(`Room.TopUpBots`)뿐이라 커맨드를 거치지
        /// 않아도 동작한다. 그래도 거치는 이유는 명단이 늘어나는 자리를 한 곳으로
        /// 유지하는 것이다 — `DrainCommands` 를 읽으면 참가자가 늘어나는 모든 경로가
        /// 보여야 하고, 나중에 다른 스레드에서 봇을 넣는 경로가 붙을 때 `_players` 를
        /// 직접 만지는 코드가 생기지 않는다.
        AddBot = 5,

        /// 참가자가 준비를 켜거나 껐다. `Value` 가 0 또는 1 이다.
        SetReady = 6,

        /// 참가자가 캐릭터를 골랐다. `Value` 가 캐릭터 번호다.
        SetCharacter = 7,
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

        public static RoomCommand SetReady(int sessionId, bool ready)
        {
            return new RoomCommand(
                RoomCommandKind.SetReady,
                sessionId,
                0,
                ready ? (byte)1 : (byte)0,
                string.Empty,
                false);
        }

        public static RoomCommand SetCharacter(int sessionId, byte characterId)
        {
            return new RoomCommand(
                RoomCommandKind.SetCharacter,
                sessionId,
                0,
                characterId,
                string.Empty,
                false);
        }

        /// 봇 하나를 넣는다. 세션 id 와 슬롯은 룸이 적용 시점에 발급한다 —
        /// 붙일 때 정하면 그 사이에 사람이 들어와 슬롯이 겹친다.
        public static RoomCommand AddBot()
        {
            return new RoomCommand(RoomCommandKind.AddBot, 0, 0, 0, string.Empty, false);
        }
    }
}
