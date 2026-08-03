using NV.Shared.Contracts.Enums;

namespace NV.Shared.Contracts.Messages
{
    /// 클라이언트 → 서버 제어 요청. 3B.
    ///
    /// 입력과 달리 재전송하지 않는다. 입력은 매 틱 새로 만들어지므로 하나를 잃어도
    /// 다음 것이 오지만, 제어는 사용자가 버튼을 누른 한 번뿐이다. 그래서 이것은
    /// WebSocket(TCP)의 순서·도달 보장에 기댄다 — 잃어버릴 수 있는 경로에 두려면
    /// 요청 id 와 ack 가 필요하고, 버튼 하나에 그만한 장치를 붙일 이유가 없다.
    public readonly struct ControlMessage
    {
        /// opcode(1) + kind(1) + value(1)
        public const int WireSize = 3;

        public ControlMessage(ControlKind kind, byte value)
        {
            Kind = kind;
            Value = value;
        }

        public ControlKind Kind { get; }

        /// 종류에 딸린 값. `EndMatch` 는 결과 코드, 나머지는 0 이다.
        public byte Value { get; }
    }
}
