namespace NV.Shared.Transport
{
    /// WebSocket 은 전부 신뢰·순서 보장이라 현재 두 값의 동작이 같다.
    /// 전송 계층을 바꿀 때 호출 지점을 다시 훑지 않으려면 의도를 지금 남겨야 한다.
    public enum Reliability : byte
    {
        /// 유실되면 다음 것으로 대체되는 데이터. 스냅샷.
        Unreliable = 0,

        /// 유실되면 안 되는 데이터. Welcome, 이벤트.
        Reliable = 1,
    }
}
