using NV.Shared.Contracts;

namespace NV.Client.Net.Session
{
    /// 초대 코드의 화면 표현. 규칙 자체는 `InviteCodeFormat`(Shared)이 갖고 있다.
    ///
    /// 규칙을 여기 다시 적지 않는다. 서버가 코드를 만들 때 쓰는 알파벳과 길이가
    /// 같은 파일에서 나와야 하고, 그러지 않으면 서버가 만든 코드를 클라이언트가
    /// 거부하는 상황이 생긴다 — 그때 화면에는 "형식이 어긋난다" 만 뜬다.
    public static class InviteCodeText
    {
        /// 화면용. 대문자가 읽고 옮겨 적기 쉽다. 내부 표현은 항상 소문자다.
        public static string ToDisplay(string code)
        {
            return string.IsNullOrEmpty(code) ? string.Empty : code.ToUpperInvariant();
        }

        /// 사용자 입력을 내부 표현으로. 붙여넣기에 섞이는 공백과 하이픈을 버린다.
        public static string Normalize(string raw)
        {
            return InviteCodeFormat.Normalize(raw);
        }

        public static bool IsValid(string normalized)
        {
            return InviteCodeFormat.IsValid(normalized);
        }

        /// 입력 칸 아래에 띄우는 설명. 형식을 만족하면 빈 문자열이다.
        ///
        /// 길이와 문자를 따로 말해 준다. 하나로 뭉치면 다 지운 칸과 한 글자
        /// 잘못 적은 칸이 같은 문구를 받는다.
        public static string Hint(string raw)
        {
            var normalized = Normalize(raw);

            if (normalized.Length == 0)
            {
                return "초대 코드 " + InviteCodeFormat.Length + "자";
            }

            if (normalized.Length != InviteCodeFormat.Length)
            {
                return InviteCodeFormat.Length + "자여야 한다. 지금 " + normalized.Length + "자.";
            }

            for (var index = 0; index < normalized.Length; index++)
            {
                if (InviteCodeFormat.Alphabet.IndexOf(normalized[index]) < 0)
                {
                    // 비슷한 문자로 바꿔 주지 않는다. L 을 1 로 고쳐 주면 사용자가
                    // 받아 적은 것과 다른 방에 들어가고, 그때 화면에는 설명이 없다.
                    return "쓸 수 없는 문자 '" + ToDisplay(normalized[index].ToString()) + "'. I·L·O·0·1 은 코드에 쓰이지 않는다.";
                }
            }

            return string.Empty;
        }
    }
}
