namespace NV.Shared.Contracts.Enums
{
    /// `MessageOpcode.Event` 의 두 번째 바이트. 이벤트 종류를 가른다.
    ///
    /// opcode 를 종류마다 새로 만들지 않는다. 서버 발신 opcode 는 상위 비트로
    /// 구분되는 좁은 공간이고, 이벤트는 앞으로 늘어나는 쪽이다.
    public enum EventKind : byte
    {
        None = 0,

        /// 룸의 현재 상태 전문. 단계·방장·명단·역할·배치 씨드가 전부 들어 있다.
        RoomState = 1,
    }
}
