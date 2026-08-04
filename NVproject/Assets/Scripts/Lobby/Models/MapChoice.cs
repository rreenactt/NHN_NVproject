using System;
using System.Collections.Generic;
using NV.Client.Map;
using NV.Client.Net.Session;

namespace NV.Client.Lobby.Models
{
    /// 이 맵으로 방을 만들 수 있는가, 없으면 왜.
    public enum MapChoiceStatus
    {
        /// 서버에 있고 이 빌드가 그릴 수 있고 지형도 같다.
        Ready = 0,

        /// 서버에는 있는데 이 빌드에 에셋이 없다. **고칠 수 있는 버그가 아니라 구조다** —
        /// WebGL 빌드는 에셋을 구워서 나가므로 서버에 맵을 추가하는 것만으로는 이미 배포된
        /// 클라이언트가 그것을 그릴 수 없다.
        MissingLocally = 1,

        /// 이 빌드는 아는데 서버가 모른다. export 를 `MapData/` 에 쓰지 않았을 때의 모습이다.
        MissingOnServer = 2,

        /// 양쪽에 있는데 지형이 다르다. **덤으로 얻은 검사다** — 지금 이 상황은 접속한 뒤
        /// 맵 해시 불일치 경고 한 줄로만 드러나고, 그때 사람은 이미 방을 만들었다.
        HashMismatch = 3,
    }

    /// 방 만들기 화면의 맵 한 줄.
    ///
    /// **서버의 목록과 이 빌드의 카탈로그를 합친 결과다.** 둘 중 하나만 보면 안 되는 이유가
    /// 각각 있다: 서버만 보면 그릴 수 없는 맵을 고르게 되고(접속 후 "씬이 없다" 로 끝난다),
    /// 카탈로그만 보면 서버가 새로 등록한 맵이 화면에 나오지 않는다.
    ///
    /// 고를 수 없는 줄도 **이유와 함께** 남긴다. 목록에서 빼면 사람은 자기가 아는 맵이 왜
    /// 사라졌는지 알 수 없고, 이유 없이 꺼진 줄은 고장으로 읽힌다.
    public readonly struct MapChoice
    {
        public MapChoice(
            string mapId,
            string displayName,
            string description,
            MapChoiceStatus status,
            bool isDefault,
            bool supportsMatch,
            string sizeText,
            string playersText)
        {
            MapId = mapId;
            DisplayName = displayName;
            Description = description;
            Status = status;
            IsDefault = isDefault;
            SupportsMatch = supportsMatch;
            SizeText = sizeText;
            PlayersText = playersText;
        }

        public string MapId { get; }

        public string DisplayName { get; }

        public string Description { get; }

        public MapChoiceStatus Status { get; }

        public bool IsDefault { get; }

        public bool SupportsMatch { get; }

        /// "2층 35×35" 같은 문장. 서버가 크기를 모르면 빈 문자열.
        public string SizeText { get; }

        /// "2–8명".
        public string PlayersText { get; }

        public bool CanCreate => Status == MapChoiceStatus.Ready;

        /// 고를 수 없는 이유. 고를 수 있으면 매치 지원 여부만 알린다.
        public string Reason
        {
            get
            {
                switch (Status)
                {
                    case MapChoiceStatus.MissingLocally:
                        return "이 빌드에는 이 맵이 없다. 클라이언트를 업데이트한다.";

                    case MapChoiceStatus.MissingOnServer:
                        return "서버에 등록되지 않았다. NVserver/MapData/ 에 export 했는지 확인한다.";

                    case MapChoiceStatus.HashMismatch:
                        return "서버의 맵이 이 빌드의 것과 다르다. 다시 export 하거나 다시 빌드한다.";

                    default:
                        return SupportsMatch
                            ? string.Empty
                            : "격자가 없어 매치가 성립하지 않는다 — 열쇠도 문도 생기지 않는다.";
                }
            }
        }
    }

    /// 서버의 목록과 이 빌드의 카탈로그를 합친다.
    ///
    /// 합치는 규칙을 한 곳에 둔다. 화면에서 하면 상태 판정이 UI 코드에 섞이고, 테스트가
    /// 화면을 띄워야 답을 볼 수 있게 된다.
    public static class MapChoices
    {
        /// <param name="serverMaps">
        /// `GET /maps` 의 결과. 서버가 목록을 주지 않았으면(옛 서버, 미도달) 비어 있다.
        /// </param>
        /// <param name="serverAnswered">
        /// 서버의 목록을 받았는가. **받지 못한 것과 "서버에 맵이 없다" 는 다르다** — 구분하지
        /// 않으면 목록을 못 받은 순간 이 빌드의 모든 맵이 `MissingOnServer` 로 뜬다.
        /// </param>
        public static List<MapChoice> Merge(
            IReadOnlyList<ServerMapInfo> serverMaps,
            bool serverAnswered,
            MapCatalog catalog)
        {
            var choices = new List<MapChoice>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // **카탈로그가 없는 빌드는 "아무 맵도 못 그린다" 가 아니다.** 그것은 이 빌드에
            // 지역 지식이 없다는 뜻이고, 서버가 답하지 않은 경우의 반대편이다. 없다고 해서
            // 모든 줄을 막으면 카탈로그를 굽기 전의 빌드가 방을 아예 만들 수 없게 되는데,
            // 씬 표(`MapSceneTable`)만으로 열리는 맵이 지금도 있다. 판정은 카탈로그가
            // **있을 때만** 한다.
            var catalogKnown = catalog != null && catalog.Entries.Length > 0;

            for (var index = 0; serverMaps != null && index < serverMaps.Count; index++)
            {
                var map = serverMaps[index];
                var entry = catalog == null ? null : catalog.Find(map.Id);

                seen.Add(map.Id);
                choices.Add(FromServer(map, entry, catalogKnown));
            }

            var rows = catalog == null ? new MapCatalogEntry[0] : catalog.Entries;

            for (var index = 0; index < rows.Length; index++)
            {
                var entry = rows[index];

                if (entry == null || string.IsNullOrEmpty(entry.mapId) || seen.Contains(entry.mapId))
                {
                    continue;
                }

                choices.Add(FromCatalogOnly(entry, serverAnswered));
            }

            // 만들 수 있는 것을 앞으로, 그 안에서는 기본 맵을 먼저. 정렬을 여기서 하는 이유는
            // 뷰가 정렬하면 같은 목록이 화면마다 다른 순서로 보이기 때문이다 —
            // `RoomService.Sorted` 가 같은 판단을 한다.
            choices.Sort(Compare);

            return choices;
        }

        private static MapChoice FromServer(ServerMapInfo map, MapCatalogEntry entry, bool catalogKnown)
        {
            var status = MapChoiceStatus.Ready;

            if (!catalogKnown)
            {
                // 지역 지식이 없다. 막지 않는다 — 틀리면 접속 시점에 갈린다.
                status = MapChoiceStatus.Ready;
            }
            else if (entry == null || !entry.IsPlayable)
            {
                status = MapChoiceStatus.MissingLocally;
            }
            else if (entry.BakedHash != map.Hash)
            {
                // **에셋이 있어도 지형이 같다는 뜻은 아니다.** 해시가 다르면 서버는 다른 지형에서
                // 판정하며, 그 증상은 걸어 다니다 특정 위치에서 튀는 것이다.
                status = MapChoiceStatus.HashMismatch;
            }

            return new MapChoice(
                map.Id,

                // 표시용 이름은 **서버 것을 먼저 쓴다.** 맵 파일이 그 값의 출처이고, 카탈로그의
                // 사본은 마지막으로 구웠을 때의 것이다.
                string.IsNullOrEmpty(map.DisplayName) ? map.Id : map.DisplayName,
                string.IsNullOrEmpty(map.Description) && entry != null ? entry.description : map.Description,
                status,
                map.IsDefault,
                map.SupportsMatch,
                SizeOf(map),
                PlayersOf(map));
        }

        private static MapChoice FromCatalogOnly(MapCatalogEntry entry, bool serverAnswered)
        {
            return new MapChoice(
                entry.mapId,
                entry.DisplayNameOrId,
                entry.description ?? string.Empty,

                // 서버가 답하지 않았을 때는 `MissingOnServer` 라고 말할 근거가 없다. 그때는
                // 만들 수 있게 두고, 틀리면 `400 unknownMap` 이 그것을 정확히 말한다.
                serverAnswered ? MapChoiceStatus.MissingOnServer : MapChoiceStatus.Ready,
                false,

                // 서버가 판정하는 값이다. 모르면 지원한다고 가정한다 — 여기서 경고를 띄우면
                // 목록을 못 받은 것이 맵의 결함처럼 보인다.
                true,
                string.Empty,
                string.Empty);
        }

        private static string SizeOf(ServerMapInfo map)
        {
            return map.HasSize ? $"{map.Floors}층 {map.Width}×{map.Depth}" : string.Empty;
        }

        private static string PlayersOf(ServerMapInfo map)
        {
            if (map.RecommendedPlayersMin <= 0 || map.RecommendedPlayersMax <= 0)
            {
                return string.Empty;
            }

            return $"{map.RecommendedPlayersMin}–{map.RecommendedPlayersMax}명";
        }

        private static int Compare(MapChoice left, MapChoice right)
        {
            if (left.CanCreate != right.CanCreate)
            {
                return left.CanCreate ? -1 : 1;
            }

            if (left.IsDefault != right.IsDefault)
            {
                return left.IsDefault ? -1 : 1;
            }

            return string.CompareOrdinal(left.MapId, right.MapId);
        }
    }
}
