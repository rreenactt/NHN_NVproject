using System;
using System.Numerics;

namespace NV.Shared.Simulation
{
    /// 시뮬레이션이 쓰는 수학. IEEE 754 가 결과를 규정하는 연산만 사용한다.
    ///
    /// 쓰지 않는 것과 이유
    /// - MathF.Sin / Cos / Tan: 정확도가 구현에 맡겨져 있다. Unity(IL2CPP)의 libm 과
    ///   .NET 의 구현이 마지막 비트에서 갈릴 수 있고, 그 차이는 리컨실리에이션에서
    ///   "가끔 캐릭터가 떨림" 으로만 나타난다. 다항식 근사로 대체한다.
    /// - Vector3.Normalize / Length / Dot / Distance: 구현이 SIMD·FMA 경로를 타면
    ///   라운딩이 달라진다. 스칼라로 직접 계산한다.
    /// - MathF.Sqrt: IEEE 가 정확한 반올림을 규정하므로 사용해도 된다.
    /// - MathF.Floor / Abs: 정확한 연산이므로 사용해도 된다.
    ///
    /// Vector3 는 데이터 컨테이너로만 쓴다. 필드 읽기와 생성자는 안전하다.
    public static class DeterministicMath
    {
        public const float Pi = 3.14159265358979f;
        public const float TwoPi = 6.28318530717959f;
        public const float HalfPi = 1.57079632679490f;

        private const float InverseTwoPi = 0.159154943091895f;

        /// 길이 계산에서 0 나눗셈을 피하는 하한.
        public const float Epsilon = 1e-6f;

        /// sin 을 [-pi/2, pi/2] 로 범위 축소한 뒤 테일러 급수 x^11 항까지 전개한다.
        /// float 정밀도에서 다음 항의 크기가 이미 표현 한계 아래다.
        public static float Sin(float radians)
        {
            // 범위 축소: r 을 [-pi, pi] 로 옮긴다. Floor 는 정확한 연산이다.
            var turns = MathF.Floor((radians * InverseTwoPi) + 0.5f);
            var r = radians - (turns * TwoPi);

            // [-pi, pi] -> [-pi/2, pi/2]. sin 의 대칭성을 쓴다.
            if (r > HalfPi)
            {
                r = Pi - r;
            }
            else if (r < -HalfPi)
            {
                r = -Pi - r;
            }

            var square = r * r;

            // 호너 전개. 상수 나눗셈은 컴파일 시점에 접히므로
            // 양쪽이 IL 에서 같은 float 상수를 읽는다.
            var series = -1f / 39916800f;
            series = (series * square) + (1f / 362880f);
            series = (series * square) + (-1f / 5040f);
            series = (series * square) + (1f / 120f);
            series = (series * square) + (-1f / 6f);
            series = (series * square) + 1f;

            return r * series;
        }

        public static float Cos(float radians)
        {
            return Sin(radians + HalfPi);
        }

        public static void SinCos(float radians, out float sin, out float cos)
        {
            sin = Sin(radians);
            cos = Cos(radians);
        }

        public static float Sqrt(float value)
        {
            return MathF.Sqrt(value);
        }

        public static float Abs(float value)
        {
            return MathF.Abs(value);
        }

        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        /// 현재 값을 목표로 maxDelta 만큼만 당긴다. 프레임레이트가 아니라 틱 델타 기준이다.
        public static float Converge(float current, float target, float maxDelta)
        {
            var difference = target - current;

            if (difference > maxDelta)
            {
                return current + maxDelta;
            }

            if (difference < -maxDelta)
            {
                return current - maxDelta;
            }

            return target;
        }

        public static float Dot(Vector3 left, Vector3 right)
        {
            return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
        }

        public static float LengthSquared(Vector3 value)
        {
            return (value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z);
        }

        public static float Length(Vector3 value)
        {
            return MathF.Sqrt(LengthSquared(value));
        }

        public static Vector3 Normalize(Vector3 value)
        {
            var length = Length(value);

            if (length < Epsilon)
            {
                return new Vector3(0f, 0f, 0f);
            }

            return new Vector3(value.X / length, value.Y / length, value.Z / length);
        }

        /// 길이가 1 을 넘을 때만 정규화한다. 대각 입력이 축 입력보다 빨라지는 것을 막는다.
        public static Vector3 ClampToUnit(Vector3 value)
        {
            var lengthSquared = LengthSquared(value);

            if (lengthSquared <= 1f)
            {
                return value;
            }

            var length = MathF.Sqrt(lengthSquared);
            return new Vector3(value.X / length, value.Y / length, value.Z / length);
        }

        public static Vector3 Scale(Vector3 value, float scalar)
        {
            return new Vector3(value.X * scalar, value.Y * scalar, value.Z * scalar);
        }

        public static Vector3 Add(Vector3 left, Vector3 right)
        {
            return new Vector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        public static Vector3 Subtract(Vector3 left, Vector3 right)
        {
            return new Vector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        /// 평면에 투사한다. normal 은 단위 벡터여야 한다.
        public static Vector3 ProjectOnPlane(Vector3 value, Vector3 normal)
        {
            var into = Dot(value, normal);
            return new Vector3(
                value.X - (normal.X * into),
                value.Y - (normal.Y * into),
                value.Z - (normal.Z * into));
        }
    }
}
