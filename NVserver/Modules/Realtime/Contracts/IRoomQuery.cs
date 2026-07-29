using System.Collections.Generic;

namespace NV.Realtime.Contracts
{
    /// 조회 전용 진입점. 반환값은 전부 불변 스냅샷이다.
    /// 상태를 바꾸는 요청은 이 경로가 아니라 틱 경계에서 적용되는 커맨드 큐로 간다.
    public interface IRoomQuery
    {
        bool TryGetRoom(string roomId, out RoomSummary summary);

        IReadOnlyList<RoomSummary> ListRooms();
    }
}
