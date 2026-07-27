using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Serialization;

namespace NV.Shared.Simulation
{
    /// 이동 계산에 들어가는 입력. InputFrame 을 역양자화한 결과다.
    ///
    /// 서버는 반드시 이 변환을 거쳐야 한다. 클라이언트가 예측할 때 쓰는 값은
    /// 양자화를 통과한 값이므로, 서버가 원본 부동소수점을 쓰면 양쪽 결과가 갈린다.
    public readonly struct MoveIntent
    {
        public MoveIntent(float moveX, float moveZ, float yaw, float pitch, ButtonFlags buttons)
        {
            MoveX = moveX;
            MoveZ = moveZ;
            Yaw = yaw;
            Pitch = pitch;
            Buttons = buttons;
        }

        /// -1..1. 좌우.
        public float MoveX { get; }

        /// -1..1. 전후.
        public float MoveZ { get; }

        public float Yaw { get; }

        public float Pitch { get; }

        public ButtonFlags Buttons { get; }

        public bool Jump => (Buttons & ButtonFlags.Jump) != 0;

        public bool Crouch => (Buttons & ButtonFlags.Crouch) != 0;

        public bool Sprint => (Buttons & ButtonFlags.Sprint) != 0;

        public static MoveIntent FromInput(in InputFrame frame)
        {
            return new MoveIntent(
                Quantization.ToMoveAxis(frame.MoveX),
                Quantization.ToMoveAxis(frame.MoveZ),
                Quantization.ToYawRadians(frame.Yaw),
                Quantization.ToPitchRadians(frame.Pitch),
                frame.Buttons);
        }
    }
}
