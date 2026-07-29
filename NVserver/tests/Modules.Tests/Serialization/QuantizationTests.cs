using System;
using NV.Shared.Serialization;
using Xunit;

namespace NV.Modules.Tests.Serialization
{
    public class QuantizationTests
    {
        [Theory]
        [InlineData(0f)]
        [InlineData(1f)]
        [InlineData(-1f)]
        [InlineData(12.5f)]
        [InlineData(-256.75f)]
        public void 위치는_1_64미터_이내로_복원된다(float meters)
        {
            var restored = Quantization.ToMeters(Quantization.ToFixedPosition(meters));

            Assert.True(
                MathF.Abs(restored - meters) <= 1f / Quantization.PositionUnitsPerMeter,
                $"{meters} -> {restored}");
        }

        [Fact]
        public void 위치는_표현_범위를_벗어나면_클램프된다()
        {
            Assert.Equal(short.MaxValue, Quantization.ToFixedPosition(10_000f));
            Assert.Equal(short.MinValue, Quantization.ToFixedPosition(-10_000f));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(1.5f)]
        [InlineData(3f)]
        [InlineData(6f)]
        public void 요는_한바퀴_안에서_복원된다(float radians)
        {
            var restored = Quantization.ToYawRadians(Quantization.ToFixedYaw(radians));

            Assert.True(MathF.Abs(restored - radians) < 0.001f, $"{radians} -> {restored}");
        }

        [Fact]
        public void 요는_음수와_한바퀴_넘는_값을_감는다()
        {
            var negative = Quantization.ToFixedYaw(-0.5f);
            var wrapped = Quantization.ToFixedYaw(-0.5f + (2f * MathF.PI));

            Assert.Equal(wrapped, negative);
        }

        [Fact]
        public void 피치는_수직_한계에서_잘린다()
        {
            Assert.Equal(short.MaxValue, Quantization.ToFixedPitch(MathF.PI));
            Assert.Equal(short.MinValue + 1, Quantization.ToFixedPitch(-MathF.PI));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(1f)]
        [InlineData(-1f)]
        [InlineData(0.5f)]
        public void 이동축은_부호_대칭으로_복원된다(float axis)
        {
            var restored = Quantization.ToMoveAxis(Quantization.ToFixedMoveAxis(axis));

            Assert.True(MathF.Abs(restored - axis) < 0.01f, $"{axis} -> {restored}");
        }
    }
}
