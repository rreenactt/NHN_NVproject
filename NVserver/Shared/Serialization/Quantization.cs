using System;

namespace NV.Shared.Serialization
{
    /// 와이어에 올릴 값의 양자화. 클라이언트와 서버가 같은 함수를 쓴다.
    /// 양자화 오차는 예측 보정의 무보정 임계값보다 작아야 한다.
    public static class Quantization
    {
        /// 위치 1m 당 고정소수점 단위 수. int16 범위에서 ±511.98m 를 표현한다.
        public const float PositionUnitsPerMeter = 64f;

        public const float MaxPositionMeters = 511.984375f;

        private const float TwoPi = 6.2831853071795862f;
        private const float HalfPi = 1.5707963267948966f;

        public static short ToFixedPosition(float meters)
        {
            var scaled = meters * PositionUnitsPerMeter;

            if (scaled >= short.MaxValue)
            {
                return short.MaxValue;
            }

            if (scaled <= short.MinValue)
            {
                return short.MinValue;
            }

            return (short)MathF.Round(scaled);
        }

        public static float ToMeters(short fixedPosition)
        {
            return fixedPosition / PositionUnitsPerMeter;
        }

        /// 라디안을 [0, 2pi) 로 감아 uint16 전체 범위에 매핑한다.
        public static ushort ToFixedYaw(float radians)
        {
            var wrapped = radians % TwoPi;
            if (wrapped < 0f)
            {
                wrapped += TwoPi;
            }

            var scaled = MathF.Round(wrapped / TwoPi * 65536f);

            // 2pi 직전 값이 65536 으로 반올림되면 0 으로 감긴다.
            return (ushort)((uint)scaled & 0xFFFFu);
        }

        public static float ToYawRadians(ushort fixedYaw)
        {
            return fixedYaw / 65536f * TwoPi;
        }

        /// 라디안을 [-pi/2, pi/2] 로 자르고 int16 범위에 매핑한다.
        public static short ToFixedPitch(float radians)
        {
            var clamped = radians;
            if (clamped > HalfPi)
            {
                clamped = HalfPi;
            }
            else if (clamped < -HalfPi)
            {
                clamped = -HalfPi;
            }

            return (short)MathF.Round(clamped / HalfPi * short.MaxValue);
        }

        public static float ToPitchRadians(short fixedPitch)
        {
            return fixedPitch / (float)short.MaxValue * HalfPi;
        }

        /// 이동 입력을 -127..127 로 정규화한다. 128 을 쓰지 않아 부호 대칭을 유지한다.
        public static sbyte ToFixedMoveAxis(float axis)
        {
            var clamped = axis;
            if (clamped > 1f)
            {
                clamped = 1f;
            }
            else if (clamped < -1f)
            {
                clamped = -1f;
            }

            return (sbyte)MathF.Round(clamped * 127f);
        }

        public static float ToMoveAxis(sbyte fixedAxis)
        {
            return fixedAxis / 127f;
        }
    }
}
