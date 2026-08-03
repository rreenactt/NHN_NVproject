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

        /// 한 번만 발동해야 하는 버튼을 지운다. **반복 적용될 프레임을 저장하기 전에 거친다.**
        ///
        /// 새 입력이 없으면 서버는 마지막 입력을 최대 `MaxInputRepeatTicks` 만큼 반복한다
        /// (`Room.StepPlayer`). 이동은 반복되어야 맞지만 상호작용은 **엣지**다.
        ///
        /// **이것은 방어이고, 지금 막고 있는 버그는 없다.** 상호작용 요청을 세우는 곳은 새 입력
        /// 갈래뿐이므로 반복은 애초에 그 비트를 읽지 않는다. 그 불변식이 두 곳의 협조에
        /// 의존한다는 것이 여기 있는 이유다 — 반복 갈래가 버튼을 보게 되는 변경이 들어오면
        /// 조용히 깨지고, 증상은 "열쇠가 저절로 들어간다" 가 된다.
        ///
        /// **`Jump` 는 일부러 건드리지 않는다.** 점프도 엣지지만 반복돼도 재점프가 되지 않는다 —
        /// 반복 구간(`MaxInputRepeatTicks` 3틱 = 0.1초)이 체공 시간(`2·JumpSpeed/Gravity` = 0.7초)
        /// 보다 짧아서 반복된 `Jump` 가 **전부 공중에서** 소비되고 접지 검사에 걸린다.
        /// `JumpRepeatTests` 가 그 관계를 못질한다.
        ///
        /// 게다가 지우면 잃는 것이 있다. 공중에서 누른 점프가 3틱 동안 재시도되므로 **착지
        /// 직전의 입력이 착지와 함께 발동한다** — 의도해서 만든 것은 아니지만 점프 버퍼로
        /// 동작하고, 그것은 조작감에서 얻는 쪽이다. 반복 상한을 체공보다 길게 올리는 변경이
        /// 들어오면 그때 `Jump` 를 여기 더해야 하고, 그 테스트가 알려 준다.
        public static InputFrame WithoutEdgeButtons(in InputFrame frame)
        {
            return new InputFrame(
                frame.Buttons & ~ButtonFlags.Interact,
                frame.MoveX,
                frame.MoveZ,
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
