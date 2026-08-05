using System;
using System.Collections.Generic;

namespace NV.Realtime.Contracts
{
    /// 설정으로 미리 열어 두는 룸. 룸 id → 프로필(맵 + 봇 오버라이드).
    ///
    /// 초대 코드 전용이 되면 개발 루프가 끊긴다. 에디터와 스탠드얼론을 나란히 띄워
    /// 두 클라이언트를 붙이는 절차(Build and Launch 2 Clients)는 고정된 룸 id 에
    /// 기대고 있고, 코드를 받아 오는 경로가 없다. 그 자리를 이 목록이 채운다.
    ///
    /// 이 룸들은 방장이 없고 만료되지 않는다. 코드를 발급받는 경로가 없으니
    /// 방장을 주장할 토큰도 없고, 아무도 없을 때 사라지면 다음 테스트에서
    /// 다시 만들 방법이 없다.
    public sealed class StaticRooms
    {
        public static readonly StaticRooms Empty =
            new StaticRooms((IReadOnlyDictionary<string, TestRoomProfile>?)null);

        private readonly Dictionary<string, TestRoomProfile> _profileByRoom;

        public StaticRooms(IReadOnlyDictionary<string, TestRoomProfile>? profileByRoom)
        {
            _profileByRoom = new Dictionary<string, TestRoomProfile>(StringComparer.Ordinal);

            if (profileByRoom == null)
            {
                return;
            }

            foreach (var pair in profileByRoom)
            {
                _profileByRoom[pair.Key] = pair.Value;
            }
        }

        /// 맵 id 만 적는 옛 형태. 설정의 문자열 값이 이 모양이고, 봇 오버라이드가
        /// 필요 없는 룸도 이것으로 충분하다 — 생략한 필드는 전역 설정을 따른다.
        public StaticRooms(IReadOnlyDictionary<string, string>? mapByRoom)
            : this(ToProfiles(mapByRoom))
        {
        }

        public IReadOnlyDictionary<string, TestRoomProfile> Rooms => _profileByRoom;

        public bool Contains(string roomId)
        {
            return !string.IsNullOrEmpty(roomId) && _profileByRoom.ContainsKey(roomId);
        }

        private static IReadOnlyDictionary<string, TestRoomProfile>? ToProfiles(
            IReadOnlyDictionary<string, string>? mapByRoom)
        {
            if (mapByRoom == null)
            {
                return null;
            }

            var profiles = new Dictionary<string, TestRoomProfile>(StringComparer.Ordinal);

            foreach (var pair in mapByRoom)
            {
                profiles[pair.Key] = new TestRoomProfile(pair.Value);
            }

            return profiles;
        }
    }
}
