namespace NV.Realtime.Contracts
{
    /// 룸의 불변 스냅샷. 살아 있는 룸 객체는 모듈 밖으로 나가지 않는다.
    /// 룸 상태는 틱 루프가 소유하므로 외부에 참조를 넘기면 경합이 생긴다.
    public readonly struct RoomSummary
    {
        public RoomSummary(string roomId, uint tick, int playerCount, int capacity)
        {
            RoomId = roomId;
            Tick = tick;
            PlayerCount = playerCount;
            Capacity = capacity;
        }

        public string RoomId { get; }

        public uint Tick { get; }

        public int PlayerCount { get; }

        public int Capacity { get; }
    }
}
