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
            : this(moveX, moveZ, yaw, pitch, buttons, false)
        {
        }

        public MoveIntent(float moveX, float moveZ, float yaw, float pitch, ButtonFlags buttons, bool seekerLegs)
        {
            MoveX = moveX;
            MoveZ = moveZ;
            Yaw = yaw;
            Pitch = pitch;
            Buttons = buttons;
            SeekerLegs = seekerLegs;
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

        /// 이 몸이 술래의 다리인가. 달릴 때의 배수가 달라진다
        /// (<see cref="SimConstants.SeekerSprintMultiplier"/>).
        ///
        /// **버튼이 아니라 별도의 값이다.** 버튼은 클라이언트가 보내는 것이므로, 거기에
        /// 실으면 아무나 술래의 다리를 주장할 수 있다. 이것은 서버가 자기 명단을 보고
        /// 채운다.
        public bool SeekerLegs { get; }

        public static MoveIntent FromInput(in InputFrame frame)
        {
            return FromInput(frame, false);
        }

        public static MoveIntent FromInput(in InputFrame frame, bool seekerLegs)
        {
            return new MoveIntent(
                Quantization.ToMoveAxis(frame.MoveX),
                Quantization.ToMoveAxis(frame.MoveZ),
                Quantization.ToYawRadians(frame.Yaw),
                Quantization.ToPitchRadians(frame.Pitch),
                frame.Buttons,
                seekerLegs);
        }
    }
}
