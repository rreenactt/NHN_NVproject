using NV.Shared.Contracts.Enums;

namespace NV.Shared.Contracts.Messages
{
    /// 한 틱의 입력. 7B.
    /// 프레임 자체에 틱 번호가 없다. 메시지 헤더의 틱이 첫 프레임의 틱이고
    /// 이후 프레임은 하나씩 과거다.
    /// 클라이언트는 위치를 보내지 않는다. 보내는 즉시 클라이언트 권위가 된다.
    public readonly struct InputFrame
    {
        public const int WireSize = 7;

        public InputFrame(ButtonFlags buttons, sbyte moveX, sbyte moveZ, ushort yaw, short pitch)
        {
            Buttons = buttons;
            MoveX = moveX;
            MoveZ = moveZ;
            Yaw = yaw;
            Pitch = pitch;
        }

        public ButtonFlags Buttons { get; }

        /// -127..127 로 정규화된 좌우 이동 입력.
        public sbyte MoveX { get; }

        /// -127..127 로 정규화된 전후 이동 입력.
        public sbyte MoveZ { get; }

        /// Quantization.ToFixedYaw 로 변환된 값.
        public ushort Yaw { get; }

        /// Quantization.ToFixedPitch 로 변환된 값.
        public short Pitch { get; }
    }
}
