using System;
using System.Collections.Generic;
using NV.Shared.Collision;

namespace NV.Realtime.Contracts
{
    /// 등록된 맵 목록. Api 가 파일을 읽어 만들고 모듈이 조회만 한다.
    ///
    /// 키가 맵 id 다. 예전에는 룸 id 였는데, 초대 코드 룸은 코드가 만들어지는
    /// 시점에 설정 파일이 알 수 없으므로 룸 id 로는 맵을 찾을 수 없다 — 그 구조에서는
    /// 모든 초대 코드 방이 조용히 기본 맵으로 열린다. 이제 룸을 만들 때 맵 id 를 받고,
    /// 클라이언트는 참가 전 조회로 그 맵 이름을 받아 어느 씬을 열지 정한다.
    ///
    /// 살아 있는 룸 객체가 아니라 불변 맵만 담으므로 모듈 밖으로 나가도 안전하다.
    public sealed class RoomMaps
    {
        /// 맵을 지정하지 않은 요청에 쓰이는 id. 설정에 반드시 있어야 한다.
        public const string DefaultMapId = "default";

        private readonly Dictionary<string, WorldMap> _byMapId;

        public RoomMaps(IReadOnlyDictionary<string, WorldMap> byMapId)
        {
            if (byMapId == null)
            {
                throw new ArgumentNullException(nameof(byMapId));
            }

            _byMapId = new Dictionary<string, WorldMap>(StringComparer.Ordinal);

            foreach (var pair in byMapId)
            {
                _byMapId[pair.Key] = pair.Value;
            }

            if (!_byMapId.ContainsKey(DefaultMapId))
            {
                // 없이 올라가면 맵을 지정하지 않은 요청이 전부 실패하고,
                // 증상은 방 만들기가 안 되는 것으로만 나타난다.
                throw new ArgumentException($"맵 목록에 '{DefaultMapId}' 항목이 없다.", nameof(byMapId));
            }
        }

        /// 단일 맵으로 쓰는 경우. 룸별 분리가 필요 없는 배포와 테스트가 이 형태다.
        public RoomMaps(WorldMap fallback)
            : this(new Dictionary<string, WorldMap>(StringComparer.Ordinal)
            {
                [DefaultMapId] = fallback ?? throw new ArgumentNullException(nameof(fallback)),
            })
        {
        }

        public WorldMap Default => _byMapId[DefaultMapId];

        /// 등록되지 않은 맵 id 는 null 이다.
        ///
        /// 기본 맵으로 대신 열지 않는다. 요청한 맵과 다른 지형으로 방이 열리면
        /// 증상이 맵 해시 불일치 하나로 나타나고, 방을 만든 사람은 자기가 무엇을
        /// 잘못 골랐는지 알 수 없다. 알 수 없는 id 는 거절이 맞다.
        public WorldMap? ByMapId(string? mapId)
        {
            if (string.IsNullOrEmpty(mapId))
            {
                return Default;
            }

            return _byMapId.TryGetValue(mapId!, out var map) ? map : null;
        }

        public bool IsRegistered(string? mapId)
        {
            return !string.IsNullOrEmpty(mapId) && _byMapId.ContainsKey(mapId);
        }

        /// 기동 로그와 맵 선택 화면용.
        public IReadOnlyDictionary<string, WorldMap> ByMap => _byMapId;
    }
}
