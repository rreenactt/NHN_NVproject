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
    public static class InviteCodeFormat
    {
        /// 6자. 31^6 ≈ 8.9억이고 동시에 열리는 룸은 16개뿐이라
        /// 코드를 찍어서 남의 방에 들어가는 것은 실질적으로 불가능하다.
        public const int Length = 6;

        /// 소문자와 숫자에서 받아쓰기로 갈리는 문자를 뺐다 — `i` `l` `o` `0` `1`.
        /// 코드는 사람이 읽어서 옮기는 값이고, 그 자리에서 한 글자만 틀려도
        /// "없는 방" 과 구분되지 않는 실패가 된다.
        public const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";

        public static bool IsValid(string code)
        {
            if (code == null || code.Length != Length)
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
