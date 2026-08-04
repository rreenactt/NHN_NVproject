using System;

namespace NV.Client.Net.Session
{
    /// 서버가 아는 맵 하나. `GET /maps` 본문을 그대로 옮긴 값이다.
    public readonly struct ServerMapInfo
    {
        public ServerMapInfo(
            string id,
            string displayName,
            string description,
            uint hash,
            bool isDefault,
            bool supportsMatch,
            int floors,
            int width,
            int depth,
            int recommendedPlayersMin,
            int recommendedPlayersMax)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            Hash = hash;
            IsDefault = isDefault;
            SupportsMatch = supportsMatch;
            Floors = floors;
            Width = width;
            Depth = depth;
            RecommendedPlayersMin = recommendedPlayersMin;
            RecommendedPlayersMax = recommendedPlayersMax;
        }

        /// 맵 id. 방을 만들 때 보내는 값이고, 맵 이름·export 파일명과 같다.
        public string Id { get; }

        public string DisplayName { get; }

        public string Description { get; }

        /// 서버가 로드한 지형의 해시. **이 빌드가 구운 해시와 대조한다** — 다르면 접속 후
        /// 맵 해시 불일치가 될 방을 만들기 전에 말할 수 있다.
        public uint Hash { get; }

        /// 맵을 지정하지 않은 요청이 이 맵으로 열린다. 화면의 기본 선택이다.
        public bool IsDefault { get; }

        /// 이 맵에서 매치가 성립하는가. **서버의 판정이다** — 격자가 없으면 열쇠도 문도 생기지 않는다.
        public bool SupportsMatch { get; }

        public int Floors { get; }

        public int Width { get; }

        public int Depth { get; }

        public int RecommendedPlayersMin { get; }

        public int RecommendedPlayersMax { get; }

        public bool HasSize => Floors > 0 && Width > 0 && Depth > 0;
    }

    /// 맵 목록 조회 결과.
    ///
    /// **실패와 "이 서버는 목록을 안 준다" 를 구분한다.** `GET /maps` 가 없는 옛 서버는 404 로
    /// 답하고, 그것은 오류가 아니라 정상 응답이다 — 그 경우 로비는 이 빌드가 아는 맵만으로
    /// 목록을 만들고, 만들기를 막지 않는다. 틀리면 `400 unknownMap` 이 정확히 그것을 말한다.
    /// 그 구분이 없으면 옛 서버에 붙은 클라이언트가 방을 아예 만들 수 없게 된다.
    public readonly struct MapListResult
    {
        private MapListResult(ServerMapInfo[] maps, bool unavailable, SessionFailureKind failure)
        {
            Maps = maps ?? Array.Empty<ServerMapInfo>();
            Unavailable = unavailable;
            Failure = failure;
        }

        public static MapListResult Ok(ServerMapInfo[] maps)
        {
            return new MapListResult(maps, false, SessionFailureKind.None);
        }

        /// 이 서버에는 맵 목록 엔드포인트가 없다(404).
        public static MapListResult NotPublished()
        {
            return new MapListResult(null, true, SessionFailureKind.None);
        }

        public static MapListResult Failed(SessionFailureKind failure)
        {
            return new MapListResult(null, false, failure);
        }

        public ServerMapInfo[] Maps { get; }

        public bool Unavailable { get; }

        public SessionFailureKind Failure { get; }

        public bool Succeeded => !Unavailable && Failure == SessionFailureKind.None;
    }

    /// `JsonUtility` 용 전송 형식.
    ///
    /// `hash` 를 long 으로 받는다. 서버는 uint 를 보내며 큰 값은 int 범위를 넘는데,
    /// JsonUtility 의 부호 없는 정수 처리를 신뢰하지 않는 편이 안전하다 — `mapHash` 가
    /// 같은 이유로 그렇게 되어 있다.
    [Serializable]
    internal sealed class MapInfoResponseDto
    {
        public string id;
        public string displayName;
        public string description;
        public long hash;
        public int schemaVersion;
        public bool isDefault;
        public bool supportsMatch;
        public int boxCount;
        public int spawnCount;
        public bool hasGrid;
        public int floors;
        public int width;
        public int depth;
        public float cellSize;
        public int recommendedPlayersMin;
        public int recommendedPlayersMax;
        public string[] tags;

        public ServerMapInfo ToMapInfo()
        {
            return new ServerMapInfo(
                id ?? string.Empty,
                string.IsNullOrEmpty(displayName) ? id ?? string.Empty : displayName,
                description ?? string.Empty,
                unchecked((uint)hash),
                isDefault,
                supportsMatch,
                floors,
                width,
                depth,
                recommendedPlayersMin,
                recommendedPlayersMax);
        }
    }

    /// 최상위 배열을 감싸는 껍데기.
    ///
    /// `JsonUtility` 는 최상위 배열을 파싱하지 못하고 **예외 대신 null 을 돌려준다**.
    /// 감싸지 않으면 파싱 실패가 "맵이 0개" 로 조용히 둔갑한다 — 방 목록이 이미 같은
    /// 이유로 같은 방식을 쓴다(`RoomListResponseDto`).
    [Serializable]
    internal sealed class MapListResponseDto
    {
        public const string WrapperPrefix = "{\"items\":";
        public const string WrapperSuffix = "}";

        public MapInfoResponseDto[] items;
    }
}
