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

        /// `DeterministicRandom` 을 쓰지 않는다.
        ///
        /// 그쪽은 클라이언트와 서버가 같은 값을 내야 하는 시뮬레이션용이고, 씨드를
        /// 알면 다음 값이 나온다. 초대 코드와 방장 토큰은 그 반대여야 한다 — 코드를
        /// 찍어 남의 방에 들어가거나 토큰을 추측해 방장을 가로채는 것을 막는 값이다.
        public static string NewCode()
        {
            Span<byte> bytes = stackalloc byte[InviteCodeFormat.Length];
            RandomNumberGenerator.Fill(bytes);

            Span<char> code = stackalloc char[InviteCodeFormat.Length];
            for (var index = 0; index < code.Length; index++)
            {
                // 알파벳 길이가 256의 약수가 아니므로 나머지 연산에 편향이 남는다.
                // 여기서 문제되지 않는다 — 코드는 비밀이 아니라 식별자이고, 실제
                // 방어선은 동시에 열리는 룸이 16개뿐이라는 사실이다.
                code[index] = InviteCodeFormat.Alphabet[bytes[index] % InviteCodeFormat.Alphabet.Length];
            }

            return new string(code);
        }

        public static string NewHostToken()
        {
            Span<byte> bytes = stackalloc byte[TokenBytes];
            RandomNumberGenerator.Fill(bytes);

            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
