using NV.Client.Config;
using NV.Shared.Contracts.Messages;
using UnityEngine;

namespace NV.Client.Lobby.Models
{
    /// 이 기기에 남는 플레이어 설정.
    ///
    /// 계정이 아니다. `Identity` 모듈이 없어 서버는 이름을 검증하지도 기억하지도 않고,
    /// 이름은 세션 수명만큼만 산다. 여기 저장하는 것은 "다음에 켤 때 다시 타이핑하지
    /// 않게" 하기 위한 것뿐이며, 화면은 그 사실을 감추지 않는다.
    ///
    /// **서버 주소는 환경마다 서랍이 다르다.** `PlayerPrefs` 는 빌드를 갈아도 그 기기에
    /// 남으므로, 키가 하나면 로컬 서버에 한 번 붙어 본 기기에 배포 빌드를 깔았을 때
    /// 배포 빌드가 `localhost` 를 가리킨다. 증상은 "서버 응답 없음" 뿐이고 그것이 옛
    /// 설정 때문이라는 단서가 아무 데도 없다. 자세한 결정 순서는 <see cref="NVEnvironment"/>.
    public static class PlayerProfile
    {
        /// 표시 이름에는 환경 접두어를 붙이지 않는다.
        ///
        /// 이름은 사람의 것이고 서버의 것이 아니다. 환경마다 서랍을 나누면 개발 서버로
        /// 한 번 옮겼다는 이유로 이름을 다시 타이핑하게 되는데, 그것이 이 클래스가
        /// 없애려는 바로 그 수고다.
        private const string NameKey = "nv.lobby.name";

        private static string HostKey => "nv." + NVEnvironment.Active.Id + ".lobby.host";

        private static string SecureKey => "nv." + NVEnvironment.Active.Id + ".lobby.secure";

        /// 서버가 이름을 자르는 상한과 같은 값을 쓴다.
        ///
        /// 서버는 `ProtocolInfo.MaxDisplayNameBytes` 로 절단한다. 화면이 더 긴 입력을
        /// 허용하면 사용자가 친 이름과 명단에 뜨는 이름이 달라지고, 그것을 버그로
        /// 신고하게 된다. 상수를 다시 적지 않고 계약에서 가져온다.
        public static int MaxNameLength => ProtocolInfo.MaxDisplayNameBytes;

        public static string DisplayName
        {
            get => PlayerPrefs.GetString(NameKey, string.Empty);
            set => PlayerPrefs.SetString(NameKey, Sanitize(value));
        }

        /// 지금 붙을 서버. 환경이 정한 값이 기본이고, 허용된 환경에서만 사람이 덮는다.
        ///
        /// `allowHostOverride` 가 꺼진 환경에서는 저장된 값을 **읽지도 않는다.** 끄기
        /// 전에 그 기기에 남아 있던 주소가 있을 수 있고, 입력칸만 잠그면 그 옛 값이
        /// 계속 쓰인다 — 잠긴 화면이 환경과 다른 서버를 가리키는 상태가 된다.
        public static string Host
        {
            get
            {
                var environment = NVEnvironment.Active;

                return environment.AllowHostOverride
                    ? PlayerPrefs.GetString(HostKey, environment.Host)
                    : environment.Host;
            }

            set
            {
                if (!NVEnvironment.Active.AllowHostOverride)
                {
                    return;
                }

                PlayerPrefs.SetString(HostKey, (value ?? string.Empty).Trim());
            }
        }

        public static bool Secure
        {
            get
            {
                var environment = NVEnvironment.Active;

                return environment.AllowHostOverride
                    ? PlayerPrefs.GetInt(SecureKey, environment.Secure ? 1 : 0) != 0
                    : environment.Secure;
            }

            set
            {
                if (!NVEnvironment.Active.AllowHostOverride)
                {
                    return;
                }

                PlayerPrefs.SetInt(SecureKey, value ? 1 : 0);
            }
        }

        /// <summary>사람이 서버 주소를 바꿀 수 있는 환경인가. 설정 화면이 이 값으로 입력칸을 켠다.</summary>
        public static bool CanChangeHost => NVEnvironment.Active.AllowHostOverride;

        public static void Save()
        {
            PlayerPrefs.Save();
        }

        /// 이름을 서버가 받아들이는 모양으로 줄인다.
        ///
        /// 서버는 출력 가능한 ASCII(0x20~0x7E)만 남기고 자른다. 같은 규칙을 여기서 먼저
        /// 적용해, 한글을 넣었을 때 명단에서 이름이 통째로 사라지는 일이 입력 시점에
        /// 보이게 한다.
        public static string Sanitize(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            var trimmed = raw.Trim();
            var buffer = new System.Text.StringBuilder(trimmed.Length);

            for (var index = 0; index < trimmed.Length; index++)
            {
                var c = trimmed[index];

                if (c >= 0x20 && c <= 0x7E)
                {
                    buffer.Append(c);
                }

                if (buffer.Length >= MaxNameLength)
                {
                    break;
                }
            }

            return buffer.ToString();
        }

        /// 입력한 이름 중 서버에 닿지 못하는 부분이 있는가. 화면이 그 사실을 알린다.
        public static bool WouldChange(string raw)
        {
            return !string.Equals(raw ?? string.Empty, Sanitize(raw), System.StringComparison.Ordinal);
        }
    }
}
