using System;
using System.Security.Cryptography;
using NV.Shared.Contracts;

namespace NV.Realtime.Simulation
{
    /// 초대 코드와 방장 토큰을 만든다. 형식은 `InviteCodeFormat` 이 정하고
    /// 만드는 것은 서버만 한다.
    internal static class InviteCode
    {
        /// 방장 토큰 바이트 수. 16바이트를 16진수로 적어 32자가 된다.
        /// 쿼리스트링으로 오가므로 URL 인코딩이 필요 없는 표현을 쓴다.
        private const int TokenBytes = 16;

        /// 지금 열려 있는 룸 수에 맞는 코드 길이.
        ///
        /// 룸 수 상한이 없으므로 코드 공간이 고정이면 룸이 늘수록 충돌이 잦아진다.
        /// 충돌 자체는 재시도로 흡수되지만(같은 코드가 두 방에 붙는 일은 없다),
        /// 재시도가 잦아지는 것은 공간이 부족하다는 신호다. 그래서 부하율
        /// (룸 수 / 코드 공간)이 `CodeSpaceMargin` 의 역수를 넘지 않도록 길이를 늘린다.
        ///
        /// 여유율 10만이면 6자는 약 8,800 룸까지, 7자는 약 27만 룸까지 쓴다.
        /// 현실적인 배포에서는 코드가 6자에 머무르며, 그것이 사람이 받아 적기 좋은 길이다.
        ///
        /// 길이를 줄이지는 않는다 — 룸이 줄었다고 짧은 코드를 다시 쓰면, 방금 사라진
        /// 방의 코드를 다른 방이 물려받을 확률만 올라간다.
        public static int LengthFor(int liveRooms)
        {
            var length = InviteCodeFormat.MinLength;
            var space = Space(length);

            while (length < InviteCodeFormat.MaxLength
                && liveRooms > space / RealtimeConstants.Rooms.CodeSpaceMargin)
            {
                length++;
                space *= InviteCodeFormat.Alphabet.Length;
            }

            return length;
        }

        /// `DeterministicRandom` 을 쓰지 않는다.
        ///
        /// 그쪽은 클라이언트와 서버가 같은 값을 내야 하는 시뮬레이션용이고, 씨드를
        /// 알면 다음 값이 나온다. 초대 코드와 방장 토큰은 그 반대여야 한다 — 코드를
        /// 찍어 남의 방에 들어가거나 토큰을 추측해 방장을 가로채는 것을 막는 값이다.
        ///
        /// 거부 표집을 쓴다. 바이트를 알파벳 길이로 나눈 나머지를 그대로 쓰면 앞쪽
        /// 8개 문자가 더 자주 나와 코드 공간이 실질적으로 줄어든다.
        public static string NewCode(int length)
        {
            if (length < InviteCodeFormat.MinLength || length > InviteCodeFormat.MaxLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(length),
                    length,
                    $"코드 길이는 {InviteCodeFormat.MinLength}..{InviteCodeFormat.MaxLength} 여야 한다.");
            }

            Span<char> code = stackalloc char[length];

            // 버려지는 바이트가 있으니 한 번에 넉넉히 뽑는다. 31 알파벳에서 한 바이트가
            // 버려질 확률은 8/256 이므로 두 배면 사실상 한 번으로 끝난다.
            Span<byte> bytes = stackalloc byte[length * 2];
            RandomNumberGenerator.Fill(bytes);

            var filled = 0;
            var cursor = 0;

            while (filled < length)
            {
                if (cursor == bytes.Length)
                {
                    RandomNumberGenerator.Fill(bytes);
                    cursor = 0;
                }

                var sample = bytes[cursor];
                cursor++;

                if (sample >= InviteCodeFormat.SamplingLimit)
                {
                    continue;
                }

                code[filled] = InviteCodeFormat.Alphabet[sample % InviteCodeFormat.Alphabet.Length];
                filled++;
            }

            return new string(code);
        }

        public static string NewHostToken()
        {
            Span<byte> bytes = stackalloc byte[TokenBytes];
            RandomNumberGenerator.Fill(bytes);

            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        /// 알파벳^길이. `MaxLength` 12 에서 7.9×10^17 이라 long 안에 들어온다.
        private static long Space(int length)
        {
            var space = 1L;
            for (var index = 0; index < length; index++)
            {
                space *= InviteCodeFormat.Alphabet.Length;
            }

            return space;
        }
    }
}
