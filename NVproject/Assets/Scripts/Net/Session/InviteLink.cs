using UnityEngine;

namespace NV.Client.Net.Session
{
    /// 공유 링크를 만들고 읽는다. 코드가 정본이고 링크는 그것을 감싼 것이다.
    ///
    /// 서버는 링크를 만들지 않는다. 배포 URL 을 서버가 알 수 없고, 알게 만들면
    /// 배포 환경마다 설정 항목이 하나 늘어난다. 링크는 클라이언트가 자기 실행
    /// 위치에서 조립한다.
    ///
    /// 조립과 해석을 같은 파일에 둔다. 갈라 놓으면 쿼리 키를 한쪽만 고치는 일이 생긴다.
    public static class InviteLink
    {
        public const string CodeQueryKey = "code";

        /// 링크를 만들 수 있는가. WebGL 빌드에서만 참이다.
        ///
        /// `#if UNITY_WEBGL` 로 가르지 않는다. 값 유무로 판단하면 에디터에서도
        /// 해석 경로를 시험할 수 있고, 플랫폼 분기 하나가 줄어든다.
        public static bool TryBuild(string code, out string link)
        {
            link = string.Empty;

            var page = PageUrl();
            if (string.IsNullOrEmpty(page) || string.IsNullOrEmpty(code))
            {
                return false;
            }

            link = page + "?" + CodeQueryKey + "=" + code;
            return true;
        }

        /// 실행 URL 에 실려 온 코드. 없으면 빈 문자열이다.
        ///
        /// 형식 검증은 호출자가 한다. 쿼리스트링은 사용자가 고칠 수 있는 입력이며,
        /// 여기서 통과시킨 값을 그대로 접속에 쓰면 손으로 고친 링크가 서버까지 간다.
        public static string ReadCodeFromLaunchUrl()
        {
            return ReadCode(Application.absoluteURL);
        }

        /// 테스트와 에디터를 위해 URL 을 직접 받는 경로. 위의 함수가 이것을 쓴다.
        public static string ReadCode(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return string.Empty;
            }

            var query = url.IndexOf('?');
            if (query < 0 || query == url.Length - 1)
            {
                return string.Empty;
            }

            // 프래그먼트(#) 뒤는 쿼리가 아니다. 자르지 않으면 마지막 항목의 값에
            // 해시가 붙어 들어온다.
            var fragment = url.IndexOf('#', query);
            var end = fragment < 0 ? url.Length : fragment;

            var pairs = url.Substring(query + 1, end - query - 1).Split('&');

            for (var index = 0; index < pairs.Length; index++)
            {
                var separator = pairs[index].IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = pairs[index].Substring(0, separator);
                if (!string.Equals(key, CodeQueryKey, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return pairs[index].Substring(separator + 1);
            }

            return string.Empty;
        }

        /// 쿼리와 프래그먼트를 뗀 페이지 주소.
        private static string PageUrl()
        {
            var url = Application.absoluteURL;
            if (string.IsNullOrEmpty(url))
            {
                return string.Empty;
            }

            var cut = url.IndexOfAny(new[] { '?', '#' });
            return cut < 0 ? url : url.Substring(0, cut);
        }
    }
}
