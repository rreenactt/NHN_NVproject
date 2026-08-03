using NV.Shared.Contracts.Enums;

namespace NV.Realtime.Contracts
{
    /// 룸의 불변 스냅샷. 살아 있는 룸 객체는 모듈 밖으로 나가지 않는다.
    /// 룸 상태는 틱 루프가 소유하므로 외부에 참조를 넘기면 경합이 생긴다.
    ///
    /// 참가 전 조회(HTTP)가 이 값으로 답한다. 그래서 정원과 단계가 함께 있어야 한다 —
    /// 둘 중 하나만 보면 "자리는 있는데 이미 시작한 방" 을 구분할 수 없다.
    public readonly struct RoomSummary
    {
        public RoomSummary(
            string roomId,
            uint tick,
            int playerCount,
            int capacity,
            RoomPhase phase,
            byte hostPlayerId,
            string mapName,
            uint mapHash,
            bool isPublic)
        {
            RoomId = roomId;
            Tick = tick;
            PlayerCount = playerCount;
            Capacity = capacity;
            Phase = phase;
            HostPlayerId = hostPlayerId;
            MapName = mapName;
            MapHash = mapHash;
            IsPublic = isPublic;
        }

        public string RoomId { get; }

        public uint Tick { get; }

        public int PlayerCount { get; }

        public int Capacity { get; }

        public RoomPhase Phase { get; }

        /// 방장이 아직 붙지 않았거나 방장이 없는 룸은 `RoomStateHeader.NoPlayer` 다.
        public byte HostPlayerId { get; }

        /// 이 룸이 판정에 쓰는 맵. 클라이언트가 어느 씬을 열어야 하는지가 이것으로 갈린다.
        public string MapName { get; }

        /// 서버가 로드한 지형의 해시. 클라이언트 계산값과 다르면 다른 지형에서 시뮬레이션한다.
        public uint MapHash { get; }

        /// 방을 만든 사람이 목록 공개를 선택했는가.
        ///
        /// 목록에 실릴지만 정한다. 비공개 방도 코드를 아는 사람은 그대로 들어온다 —
        /// 참가 전 조회와 접속은 이 값을 보지 않는다.
        public bool IsPublic { get; }

        public bool IsFull => PlayerCount >= Capacity;
    }
}
