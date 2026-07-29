using System;
using System.Collections.Generic;
using NV.Shared.Collision;

namespace NV.Realtime.Contracts
{
    /// 룸이 어느 지형에서 판정되는지. Api 가 파일을 읽어 만들고 모듈이 조회만 한다.
    ///
    /// 맵을 서버 전체에 하나만 두면 룸마다 다른 씬을 띄울 수 없다. 그 상태에서
    /// 클라이언트가 다른 씬을 열면 증상이 맵 해시 불일치 하나로 나타나고, 고치는 방법은
    /// 서버 설정을 바꾸고 재기동하는 것뿐이다. 테스트 룸과 게임 레벨을 번갈아 확인하는
    /// 동안 그 왕복이 계속 반복된다 — 룸 id 로 맵을 고르면 한 번 띄운 서버로 둘 다 된다.
    ///
    /// 살아 있는 룸 객체가 아니라 불변 맵만 담으므로 모듈 밖으로 나가도 안전하다.
    public sealed class RoomMaps
    {
        /// 이 키의 맵이 룸 id 에 대응하는 항목이 없을 때 쓰인다. 반드시 있어야 한다.
        public const string FallbackKey = "default";

        private readonly Dictionary<string, WorldMap> _byRoom;

        public RoomMaps(WorldMap fallback, IReadOnlyDictionary<string, WorldMap>? byRoom)
        {
            Fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));

            _byRoom = new Dictionary<string, WorldMap>(StringComparer.Ordinal);

            if (byRoom == null)
            {
                return;
            }

            foreach (var pair in byRoom)
            {
                _byRoom[pair.Key] = pair.Value;
            }
        }

        /// 단일 맵으로 쓰는 경우. 룸별 분리가 필요 없는 배포와 테스트가 이 형태다.
        public RoomMaps(WorldMap fallback)
            : this(fallback, null)
        {
        }

        public WorldMap Fallback { get; }

        /// 룸 id 로 등록된 맵. 없으면 Fallback 이다.
        /// 알 수 없는 룸을 빈 콜리전으로 열지 않는다 — 그러면 플레이어가 지형을 통과한다.
        public WorldMap For(string? roomId)
        {
            if (roomId != null && _byRoom.TryGetValue(roomId, out var map))
            {
                return map;
            }

            return Fallback;
        }

        /// 기동 로그용. 어느 룸이 어느 맵을 쓰는지 한 번 찍어 두면
        /// 해시 불일치를 만났을 때 클라이언트가 어느 씬을 열었는지만 확인하면 된다.
        public IReadOnlyDictionary<string, WorldMap> ByRoom => _byRoom;
    }
}
