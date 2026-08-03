using System;
using System.Collections.Generic;

namespace NV.Realtime.Contracts
{
    /// 설정으로 미리 열어 두는 룸. 룸 id → 맵 id.
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
        public static readonly StaticRooms Empty = new StaticRooms(null);

        private readonly Dictionary<string, string> _mapByRoom;

        public StaticRooms(IReadOnlyDictionary<string, string>? mapByRoom)
        {
            _mapByRoom = new Dictionary<string, string>(StringComparer.Ordinal);

            if (mapByRoom == null)
            {
                return;
            }

            foreach (var pair in mapByRoom)
            {
                _mapByRoom[pair.Key] = pair.Value;
            }
        }

        public IReadOnlyDictionary<string, string> MapByRoom => _mapByRoom;

        public bool Contains(string roomId)
        {
            return !string.IsNullOrEmpty(roomId) && _mapByRoom.ContainsKey(roomId);
        }
    }
}
