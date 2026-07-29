using System.Numerics;
using NV.Realtime;
using NV.Realtime.Simulation;
using NV.Shared.Contracts.Enums;
using NV.Shared.Contracts.Messages;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Realtime
{
    public class InputValidatorTests
    {
        [Fact]
        public void 미정의_버튼_비트는_제거된다()
        {
            var frame = new InputFrame((ButtonFlags)0xFF, 0, 0, 0, 0);

            var sanitized = InputValidator.Sanitize(frame);

            Assert.Equal(ButtonFlags.All, sanitized.Buttons);
        }

        [Fact]
        public void 이동축_최소값은_단위_구간_안으로_잘린다()
        {
            // sbyte.MinValue 를 그대로 역양자화하면 -1.008 이 되어 상한을 넘는다.
            var frame = new InputFrame(ButtonFlags.None, sbyte.MinValue, sbyte.MinValue, 0, 0);

            var sanitized = InputValidator.Sanitize(frame);

            Assert.Equal(-127, sanitized.MoveX);
            Assert.Equal(-127, sanitized.MoveZ);

            var intent = MoveIntent.FromInput(sanitized);
            Assert.Equal(-1f, intent.MoveX, 5);
        }

        [Fact]
        public void 정상_입력은_바뀌지_않는다()
        {
            var frame = new InputFrame(ButtonFlags.Jump | ButtonFlags.Sprint, 60, -60, 30000, -5000);

            var sanitized = InputValidator.Sanitize(frame);

            Assert.Equal(frame.Buttons, sanitized.Buttons);
            Assert.Equal(frame.MoveX, sanitized.MoveX);
            Assert.Equal(frame.MoveZ, sanitized.MoveZ);
            Assert.Equal(frame.Yaw, sanitized.Yaw);
            Assert.Equal(frame.Pitch, sanitized.Pitch);
        }

        [Fact]
        public void 시선만_남기는_중립_입력을_만든다()
        {
            var last = new InputFrame(ButtonFlags.Sprint, 127, 127, 12345, -678);

            var neutral = InputValidator.Neutral(last);

            Assert.Equal(ButtonFlags.None, neutral.Buttons);
            Assert.Equal(0, neutral.MoveX);
            Assert.Equal(0, neutral.MoveZ);
            Assert.Equal(last.Yaw, neutral.Yaw);
            Assert.Equal(last.Pitch, neutral.Pitch);
        }

        [Fact]
        public void 상한_이하의_속도는_그대로_둔다()
        {
            var state = PlayerState.Spawn(new Vector3(0f, 0f, 0f), 0f, 100);
            state.Velocity = new Vector3(SimConstants.MoveSpeed, 0f, 0f);

            Assert.False(InputValidator.TryClampSpeed(ref state, out _));
            Assert.Equal(SimConstants.MoveSpeed, state.Velocity.X);
        }

        [Fact]
        public void 상한을_넘는_속도는_잘리고_수직_성분은_유지된다()
        {
            var state = PlayerState.Spawn(new Vector3(0f, 0f, 0f), 0f, 100);
            state.Velocity = new Vector3(500f, -12f, 500f);

            Assert.True(InputValidator.TryClampSpeed(ref state, out var speed));
            Assert.True(speed > RealtimeConstants.Validation.MaxHorizontalSpeed);

            var clamped = System.MathF.Sqrt(
                (state.Velocity.X * state.Velocity.X) + (state.Velocity.Z * state.Velocity.Z));

            Assert.True(
                clamped <= RealtimeConstants.Validation.MaxHorizontalSpeed * RealtimeConstants.Validation.SpeedTolerance + 0.001f,
                $"clamped = {clamped}");

            Assert.Equal(-12f, state.Velocity.Y);
        }
    }
}
