using NV.Shared.Contracts.Messages;

namespace NV.Realtime.Transport
{
    /// 수신 펌프가 큐에 넣는 단위. 틱 시작 시 전부 드레인된다.
    internal readonly struct InboundInput
    {
        public InboundInput(int sessionId, uint tick, uint releaseTick, InputFrame frame)
        {
            SessionId = sessionId;
            Tick = tick;
            ReleaseTick = releaseTick;
            Frame = frame;
        }

        public int SessionId { get; }

        /// 클라이언트가 이 입력에 붙인 틱 번호.
        public uint Tick { get; }

        /// 이 서버 틱 이후에 처리한다. 네트워크 조건 주입기가 지연을 넣는 지점이며,
        /// 주입기가 꺼져 있으면 도착한 틱과 같다.
        public uint ReleaseTick { get; }

        public InputFrame Frame { get; }
    }
}
