using System;
using System.Collections.Generic;
using NV.Shared.Collision;

namespace NV.Realtime.Contracts
{
    /// 등록된 맵 목록. Api 가 파일을 읽어 만들고 모듈이 조회만 한다.
    ///
    /// **키가 맵 id 이고, 맵 id 는 맵 이름이다.** 예전에는 룸 id 였는데, 초대 코드 룸은 코드가
    /// 만들어지는 시점에 설정 파일이 알 수 없으므로 룸 id 로는 맵을 찾을 수 없다 — 그 구조에서는
    /// 모든 초대 코드 방이 조용히 기본 맵으로 열린다. 이제 룸을 만들 때 맵 id 를 받고,
    /// 클라이언트는 참가 전 조회로 그 맵 이름을 받아 어느 씬을 열지 정한다.
    ///
    /// **id 와 이름이 같아야 하는 이유.** 그 둘이 다른 공간이던 동안(`default` 라는 id 가
    /// `backrooms` 라는 이름의 맵을 가리켰다) 방 만들기 화면은 id 로 말하고 씬 라우터는 이름으로
    /// 말했다. 두 표를 손으로 맞춰야 했고, 어긋나면 증상이 맵 해시 불일치 하나였다. 이제 파일명 =
    /// `name` = 맵 id 이고 그것을 로드할 때 검사한다(`MapCatalogLoader`).
    ///
    /// **별칭은 그래서 남는다.** `default` 는 지울 수 없다 — 맵을 지정하지 않은 요청,
    /// `Game:StaticRooms`, 옛 클라이언트가 그것으로 말한다. 그것을 맵 id 로 두는 대신 별칭으로
    /// 두면, 실제 맵은 언제나 자기 이름으로 등록되어 있고 `default` 는 그중 하나를 가리키는
    /// 이름표가 된다.
    ///
    /// 살아 있는 룸 객체가 아니라 불변 맵만 담으므로 모듈 밖으로 나가도 안전하다.
    public sealed class RoomMaps
    {
        /// 맵을 지정하지 않은 요청에 쓰이는 id. **별칭으로든 맵으로든 반드시 풀려야 한다.**
        public const string DefaultMapId = "default";

        private readonly Dictionary<string, WorldMap> _byMapId;

        /// 별칭 → 맵 id. 맵 id 자체는 여기 없다.
        private readonly Dictionary<string, string> _aliases;

        public RoomMaps(
            IReadOnlyDictionary<string, WorldMap> byMapId,
            IReadOnlyDictionary<string, string>? aliases = null)
        {
            if (byMapId == null)
            {
                throw new ArgumentNullException(nameof(byMapId));
            }

            _byMapId = new Dictionary<string, WorldMap>(StringComparer.Ordinal);
            _aliases = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var pair in byMapId)
            {
                _byMapId[pair.Key] = pair.Value;
            }

            if (aliases != null)
            {
                foreach (var pair in aliases)
                {
                    if (!_byMapId.ContainsKey(pair.Value))
                    {
                        // 매달린 별칭은 조용히 `unknownMap` 이 된다 — 설정은 그 맵이 있다고
                        // 말하고 서버는 없다고 답하며, 그 둘을 대조할 자리가 없다.
                        throw new ArgumentException(
                            $"별칭 '{pair.Key}' 가 등록되지 않은 맵 '{pair.Value}' 를 가리킨다.",
                            nameof(aliases));
                    }

                    if (_byMapId.ContainsKey(pair.Key))
                    {
                        throw new ArgumentException(
                            $"별칭 '{pair.Key}' 가 같은 이름의 맵을 가린다.",
                            nameof(aliases));
                    }

                    _aliases[pair.Key] = pair.Value;
                }
            }

            // `ResolveId` 는 빈 id 를 기본 맵으로 답하므로 `DefaultId` 를 읽는다. 여기서는
            // `DefaultMapId` 를 넘기므로 그 경로를 타지 않지만, 순서가 이렇게 되어 있어야
            // 나중에 `ResolveId` 를 고치는 사람이 초기화 순서에 걸리지 않는다.
            var resolvedDefault = ResolveId(DefaultMapId);

            if (resolvedDefault == null)
            {
                // 없이 올라가면 맵을 지정하지 않은 요청이 전부 실패하고,
                // 증상은 방 만들기가 안 되는 것으로만 나타난다.
                throw new ArgumentException(
                    $"'{DefaultMapId}' 로 풀리는 맵이 없다. 그 이름의 맵을 두거나 별칭을 붙인다.",
                    nameof(byMapId));
            }

            DefaultId = resolvedDefault;
        }

        /// 단일 맵으로 쓰는 경우. 룸별 분리가 필요 없는 배포와 테스트가 이 형태다.
        ///
        /// 맵은 **자기 이름으로** 등록되고 `default` 는 그것을 가리키는 별칭이 된다. 맵 자체를
        /// `default` 라는 id 로 등록하면 id 와 이름이 다시 갈린다.
        public RoomMaps(WorldMap fallback)
            : this(
                new Dictionary<string, WorldMap>(StringComparer.Ordinal)
                {
                    [NameOf(fallback)] = fallback,
                },
                AliasToDefault(NameOf(fallback)))
        {
        }

        /// `default` 가 가리키는 맵 id. 생성 시점에 확정된다.
        public string DefaultId { get; }

        public WorldMap Default => _byMapId[DefaultId];

        /// 등록되지 않은 맵 id 는 null 이다.
        ///
        /// 기본 맵으로 대신 열지 않는다. 요청한 맵과 다른 지형으로 방이 열리면
        /// 증상이 맵 해시 불일치 하나로 나타나고, 방을 만든 사람은 자기가 무엇을
        /// 잘못 골랐는지 알 수 없다. 알 수 없는 id 는 거절이 맞다.
        public WorldMap? ByMapId(string? mapId)
        {
            var resolved = ResolveId(mapId);

            return resolved == null ? null : _byMapId[resolved];
        }

        /// 별칭을 푼 맵 id. 모르는 id 는 null 이다.
        ///
        /// **비어 있으면 기본 맵이다.** 맵을 지정하지 않은 요청이 그것이고, `ByMapId` 가
        /// 예전부터 그렇게 답해 왔다.
        ///
        /// 별칭은 한 단계만 푼다. 별칭이 별칭을 가리키는 것을 허용하면 순환을 검사해야 하고,
        /// 그것을 필요로 하는 설정이 없다 — `MapCatalogLoader` 가 별칭의 대상이 맵인 것을
        /// 등록 시점에 확인한다.
        public string? ResolveId(string? mapId)
        {
            if (string.IsNullOrEmpty(mapId))
            {
                return DefaultId;
            }

            if (_byMapId.ContainsKey(mapId!))
            {
                return mapId;
            }

            return _aliases.TryGetValue(mapId!, out var target) ? target : null;
        }

        public bool IsRegistered(string? mapId)
        {
            return ResolveId(mapId) != null;
        }

        /// 기동 로그와 맵 선택 화면용.
        public IReadOnlyDictionary<string, WorldMap> ByMap => _byMapId;

        /// 별칭 → 맵 id. 기동 로그가 이것을 함께 찍는다.
        public IReadOnlyDictionary<string, string> Aliases => _aliases;

        private static string NameOf(WorldMap map)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            return string.IsNullOrEmpty(map.Name) ? DefaultMapId : map.Name;
        }

        private static IReadOnlyDictionary<string, string> AliasToDefault(string mapId)
        {
            if (string.Equals(mapId, DefaultMapId, StringComparison.Ordinal))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [DefaultMapId] = mapId,
            };
        }
    }
}
