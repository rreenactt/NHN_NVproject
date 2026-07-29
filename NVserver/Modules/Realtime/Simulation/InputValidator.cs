using System.Numerics;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Simulation;

namespace NV.Realtime.Simulation
{
    /// 입력 판정. 계산은 Shared 가, 정당성 판단은 여기가 한다.
    ///
    /// WebGL 빌드는 디컴파일되므로 클라이언트가 보내는 값은 전부 조작 가능하다고 본다.
    /// Shared 의 이동 함수가 상한을 두더라도 그것은 계산 규칙이지 방어선이 아니다.
    /// 여기서 독립적으로 다시 검사한다.
    internal static class InputValidator
    {
        /// 조작된 입력을 사용 가능한 형태로 자른다. 연결을 끊지는 않는다.
        /// 손상된 프레임 하나로 끊으면 정상 클라이언트가 패킷 손상으로 튕긴다.
        public static InputFrame Sanitize(in InputFrame frame)
        {
            return new InputFrame(
                frame.Buttons & ButtonFlags.All,
                ClampAxis(frame.MoveX),
                ClampAxis(frame.MoveZ),
                frame.Yaw,
                frame.Pitch);
        }

        /// 마지막 입력의 시선만 유지하고 이동을 비운다.
        /// 입력이 끊긴 뒤에도 계속 달리는 것을 막는다.
        public static InputFrame Neutral(in InputFrame last)
        {
            return new InputFrame(ButtonFlags.None, 0, 0, last.Yaw, last.Pitch);
        }

        /// 이동 결과의 수평 속도가 상한을 넘으면 잘라낸다.
        /// 정상 경로에서는 걸리지 않는다. 걸렸다면 Shared 와 여기 중 하나가 어긋났다는 뜻이다.
        public static bool TryClampSpeed(ref PlayerState state, out float speed)
        {
            var velocity = state.Velocity;
            speed = DeterministicMath.Sqrt((velocity.X * velocity.X) + (velocity.Z * velocity.Z));

            var limit = RealtimeConstants.Validation.MaxHorizontalSpeed * RealtimeConstants.Validation.SpeedTolerance;
            if (speed <= limit)
            {
                return false;
            }

            var scale = limit / speed;
            state.Velocity = new Vector3(velocity.X * scale, velocity.Y, velocity.Z * scale);
            return true;
        }

        /// sbyte.MinValue 는 역양자화하면 -1.008 이 되어 단위 구간을 넘는다.
        private static sbyte ClampAxis(sbyte axis)
        {
            return axis == sbyte.MinValue ? (sbyte)-127 : axis;
        }
    }
}
