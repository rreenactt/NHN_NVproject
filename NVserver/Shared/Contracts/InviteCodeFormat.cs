using System;

namespace NV.Shared.Contracts
{
    /// 초대 코드의 형식. 서버가 만들고 클라이언트가 입력받는다.
    ///
    /// 형식을 `Shared` 에 두는 이유는 클라이언트가 입력 칸에서 바로 걸러야 하기
    /// 때문이다. 형식이 어긋난 코드를 서버까지 보내면 브라우저에서는 실패 사유가
    /// 닫힘 코드 하나로 뭉쳐 오타와 없는 방을 구분할 수 없다.
    ///
    /// 형식 검사는 여기서 하고, 그 코드의 방이 실제로 있는지는 서버만 안다.
    /// 값을 공유하는 것과 판단을 넘기는 것은 다르다.
    ///
    /// **길이가 고정이 아니다.** 서버는 지금 열려 있는 룸 수에 맞춰 길이를 늘린다.
    /// 그래서 클라이언트는 범위만 검사하고, 몇 자인지는 받은 코드를 따른다 —
    /// 6자로 못박으면 서버가 늘린 코드를 클라이언트가 "형식 오류" 로 거부한다.
    public static class InviteCodeFormat
    {
        /// 사람이 받아 적을 수 있는 최소 길이. 31^6 ≈ 8.9억.
        public const int MinLength = 6;

        /// 상한. 31^12 ≈ 7.9×10^17 이며 이보다 길게 만들 이유가 생기지 않는다.
        /// 룸 id 규칙의 32자 상한 안에 있어야 하고, 길이 계산이 long 을 넘지 않아야 한다.
        public const int MaxLength = 12;

        /// 소문자와 숫자에서 받아쓰기로 갈리는 문자를 뺐다 — `i` `l` `o` `0` `1`.
        /// 코드는 사람이 읽어서 옮기는 값이고, 그 자리에서 한 글자만 틀려도
        /// "없는 방" 과 구분되지 않는 실패가 된다.
        public const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";

        /// 무작위 바이트를 알파벳으로 접을 때 버릴 경계.
        ///
        /// 256 은 31 로 나누어떨어지지 않는다. 나머지를 그대로 쓰면 앞쪽 8개 문자가
        /// 다른 문자보다 자주 나오고, 그만큼 코드 공간이 줄어든다. 이 값 이상인
        /// 바이트를 버리면(거부 표집) 분포가 고르게 된다.
        ///
        /// 룸 수 상한이 있던 동안에는 이 편향이 실질적으로 무해했다. 상한이 사라진
        /// 지금은 코드 자체가 남의 방에 들어오지 못하게 하는 유일한 수단이므로,
        /// 공간을 깎는 요소를 남겨 둘 이유가 없다.
        public const int SamplingLimit = 256 - (256 % 31);

        public static bool IsValid(string code)
        {
            if (code == null || code.Length < MinLength || code.Length > MaxLength)
            {
                return false;
            }

            for (var index = 0; index < code.Length; index++)
            {
                if (Alphabet.IndexOf(code[index]) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// 사람이 옮겨 적은 코드를 내부 표현으로 만든다.
        ///
        /// 화면에는 대문자로 보여주고 내부는 소문자다 — 그래야 룸 id 규칙
        /// (소문자·숫자·하이픈)을 그대로 만족해 검증을 두 벌로 두지 않는다.
        /// 공백과 하이픈은 버린다. 붙여넣기에 섞여 들어오는 것들이다.
        ///
        /// 제외된 문자를 비슷한 문자로 바꿔 주지 않는다. `l` 을 `1` 로 고쳐 주면
        /// 사용자가 옮겨 적은 것과 다른 방에 들어가고, 그때 화면에는 아무 설명이 없다.
        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            var buffer = new char[raw.Length];
            var count = 0;

            for (var index = 0; index < raw.Length; index++)
            {
                var character = raw[index];

                if (character == ' ' || character == '-' || character == '\t')
                {
                    continue;
                }

                buffer[count] = char.ToLowerInvariant(character);
                count++;
            }

            return new string(buffer, 0, count);
        }
    }
}
