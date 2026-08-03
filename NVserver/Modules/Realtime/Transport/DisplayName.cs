using NV.Shared.Contracts.Messages;

namespace NV.Realtime.Transport
{
    /// 표시 이름을 와이어에 실을 수 있는 형태로 만든다.
    ///
    /// 클라이언트가 보낸 문자열을 그대로 쓰지 않는다. 길이는 세션 버퍼 상한과 맞물리고,
    /// 제어문자는 로그와 화면을 망가뜨리며, 비ASCII 는 코덱이 거부한다. 세 가지를
    /// 서버에서 한 번에 걸러야 룸과 코덱이 신뢰할 수 있는 값만 다룬다.
    ///
    /// 사칭과 중복은 막지 않는다. 계정이 없으므로 막을 근거가 없고, 막는 척하면
    /// 이름을 신뢰할 수 있다는 잘못된 인상을 준다. 이름은 표시용이며 판정에 쓰이지 않는다.
    internal static class DisplayName
    {
        public static string Sanitize(string? raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            var buffer = new char[ProtocolInfo.MaxDisplayNameBytes];
            var count = 0;

            for (var index = 0; index < raw!.Length && count < buffer.Length; index++)
            {
                var character = raw[index];

                // 출력 가능한 ASCII 만 남긴다. 공백은 이름 사이에 필요하므로 남기고,
                // 앞뒤 공백은 아래에서 자른다.
                if (character < ' ' || character > '~')
                {
                    continue;
                }

                buffer[count] = character;
                count++;
            }

            var start = 0;
            while (start < count && buffer[start] == ' ')
            {
                start++;
            }

            var end = count;
            while (end > start && buffer[end - 1] == ' ')
            {
                end--;
            }

            return end == start ? string.Empty : new string(buffer, start, end - start);
        }
    }
}
