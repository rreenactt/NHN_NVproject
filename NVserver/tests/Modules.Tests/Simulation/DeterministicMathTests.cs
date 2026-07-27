using System;
using System.Numerics;
using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Simulation
{
    public class DeterministicMathTests
    {
        [Theory]
        [InlineData(0f)]
        [InlineData(0.3f)]
        [InlineData(1.5707963f)]
        [InlineData(3f)]
        [InlineData(-2.2f)]
        [InlineData(6.2f)]
        [InlineData(12.5f)]
        [InlineData(-40f)]
        public void 사인은_MathF와_충분히_일치한다(float radians)
        {
            var expected = MathF.Sin(radians);
            var actual = DeterministicMath.Sin(radians);

            Assert.True(MathF.Abs(expected - actual) < 1e-5f, $"{radians}: {expected} vs {actual}");
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(0.7f)]
        [InlineData(2.5f)]
        [InlineData(-1.1f)]
        [InlineData(9.9f)]
        public void 코사인은_MathF와_충분히_일치한다(float radians)
        {
            var expected = MathF.Cos(radians);
            var actual = DeterministicMath.Cos(radians);

            Assert.True(MathF.Abs(expected - actual) < 1e-5f, $"{radians}: {expected} vs {actual}");
        }

        [Fact]
        public void 사인은_주기적이다()
        {
            for (var turn = -3; turn <= 3; turn++)
            {
                var shifted = 0.9f + (turn * DeterministicMath.TwoPi);

                Assert.True(
                    MathF.Abs(DeterministicMath.Sin(0.9f) - DeterministicMath.Sin(shifted)) < 1e-5f,
                    $"turn {turn}");
            }
        }

        [Fact]
        public void 사인_제곱_더하기_코사인_제곱은_1이다()
        {
            for (var step = 0; step < 64; step++)
            {
                var radians = (step / 64f) * DeterministicMath.TwoPi;
                DeterministicMath.SinCos(radians, out var sin, out var cos);

                Assert.True(MathF.Abs(((sin * sin) + (cos * cos)) - 1f) < 1e-5f, $"step {step}");
            }
        }

        [Fact]
        public void 같은_입력은_항상_같은_비트를_낸다()
        {
            var first = DeterministicMath.Sin(1.2345f);

            for (var repeat = 0; repeat < 100; repeat++)
            {
                Assert.Equal(
                    BitConverter.SingleToInt32Bits(first),
                    BitConverter.SingleToInt32Bits(DeterministicMath.Sin(1.2345f)));
            }
        }

        [Fact]
        public void 단위_클램프는_길이가_1_이하면_그대로다()
        {
            var value = new Vector3(0.3f, 0f, 0.4f);

            Assert.Equal(value, DeterministicMath.ClampToUnit(value));
        }

        [Fact]
        public void 단위_클램프는_대각_입력을_1로_줄인다()
        {
            var clamped = DeterministicMath.ClampToUnit(new Vector3(1f, 0f, 1f));

            Assert.True(MathF.Abs(DeterministicMath.Length(clamped) - 1f) < 1e-6f);
        }

        [Fact]
        public void 영벡터_정규화는_영벡터다()
        {
            Assert.Equal(new Vector3(0f, 0f, 0f), DeterministicMath.Normalize(new Vector3(0f, 0f, 0f)));
        }

        [Fact]
        public void 평면_투사는_법선_성분을_제거한다()
        {
            var projected = DeterministicMath.ProjectOnPlane(
                new Vector3(3f, -5f, 2f),
                new Vector3(0f, 1f, 0f));

            Assert.Equal(3f, projected.X);
            Assert.Equal(0f, projected.Y);
            Assert.Equal(2f, projected.Z);
        }
    }
}
