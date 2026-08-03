namespace NV.Shared.Contracts.Enums
{
    /// 바이너리 프레임의 첫 바이트. 상위 비트가 서 있으면 서버 발신이다.
    public enum MessageOpcode : byte
    {
        None = 0x00,

        /// C -> S. 최근 여러 틱치 입력을 중복 전송한다.
        Input = 0x01,

        /// C -> S. 룸에 대한 요청(시작, 퇴장, 로비 복귀). 종류는 `ControlKind` 다.
        Control = 0x02,

        /// S -> C. 매 틱 풀 스냅샷. 델타 압축하지 않는다.
        Snapshot = 0x81,

        /// S -> C. 룸 상태 전문, 킬 피드. 종류는 `EventKind` 다.
        Event = 0x82,

        /// S -> C. 자기 ID, 서버 틱, 맵 해시.
        Welcome = 0x83,
    }
}
