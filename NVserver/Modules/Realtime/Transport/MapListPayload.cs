using System;
using System.Collections.Generic;
using System.Text.Json;
using NV.Infrastructure.Json;
using NV.Realtime.Contracts;
using NV.Shared.Collision;
using NV.Shared.Simulation;

namespace NV.Realtime.Transport
{
    /// `GET /maps` 의 응답. **기동 때 한 번 만들고 그대로 내준다.**
    ///
    /// 맵은 로드 후 변하지 않는다 — 그 전제가 룸이 `WorldMap` 참조를 들고 사는 것에도 이미
    /// 깔려 있다. 그래서 요청마다 직렬화할 이유가 없고, 값이 불변이므로 ETag 를 붙일 수 있다.
    /// 로비는 화면에 들어올 때마다 이 목록을 부르고, 두 번째부터는 304 로 끝난다.
    ///
    /// 싱글턴으로 등록된다(`RealtimeModule`). 정적 필드에 캐시하지 않는 이유는 테스트다 —
    /// 한 프로세스에서 서로 다른 맵 목록으로 두 번 조립할 수 있어야 한다.
    internal sealed class MapListPayload
    {
        public MapListPayload(RoomMaps maps)
        {
            if (maps == null)
            {
                throw new ArgumentNullException(nameof(maps));
            }

            Maps = Build(maps);
            Json = JsonSerializer.Serialize(Maps, JsonDefaults.Options);
            ETag = MakeETag(Json);
        }

        /// 직렬화 전의 목록. 테스트가 문자열을 다시 파싱하지 않아도 되게 남긴다.
        public MapInfoResponse[] Maps { get; }

        public string Json { get; }

        /// 약한 검증자가 아니다 — 같은 바이트에 같은 값이고, 내용이 바뀌면 서버가 다시 뜬다.
        public string ETag { get; }

        /// 맵 **id 순**으로 만든다.
        ///
        /// 사전의 순회 순서에 맡기면 같은 설정이 기계마다 다른 순서로 답할 수 있고, 그러면
        /// ETag 도 흔들려 캐시가 뜻을 잃는다. 기본 맵을 앞으로 끌어오지도 않는다 — 어느 것이
        /// 기본인지는 `isDefault` 가 말하고, 순서로도 말하면 둘이 어긋날 자리가 생긴다.
        private static MapInfoResponse[] Build(RoomMaps maps)
        {
            var ids = new List<string>(maps.ByMap.Keys);
            ids.Sort(StringComparer.Ordinal);

            var list = new MapInfoResponse[ids.Count];

            for (var index = 0; index < ids.Count; index++)
            {
                list[index] = Describe(maps, ids[index]);
            }

            return list;
        }

        private static MapInfoResponse Describe(RoomMaps maps, string id)
        {
            var map = maps.ByMap[id];
            var grid = map.Data?.Grid;

            var info = new MapInfoResponse
            {
                Id = id,
                DisplayName = id,
                Description = string.Empty,
                Hash = map.Hash,
                SchemaVersion = MapSchema.Effective(map.Data == null ? 0 : map.Data.Version),
                IsDefault = string.Equals(maps.DefaultId, id, StringComparison.Ordinal),

                // 격자가 있고 몸이 들어가는 셀이 있어야 목표물을 배치할 수 있다. 둘 중
                // 하나만 보면 "격자는 있는데 좌표계가 어긋난 맵" 이 통과한다.
                SupportsMatch = map.HasGrid && map.Grid.FreeFloorCount > 0,

                BoxCount = map.Collision.BoxCount,
                SpawnCount = map.SpawnCount,
                HasGrid = map.HasGrid,

                RecommendedPlayersMin = RealtimeConstants.Rooms.MinPlayersToStart,
                RecommendedPlayersMax = RealtimeConstants.Rooms.MaxPlayers,
            };

            if (grid != null)
            {
                info.Floors = grid.Floors;
                info.Width = grid.Width;
                info.Depth = grid.Depth;
                info.CellSize = grid.CellSize;
            }

            return info;
        }

        /// 본문에서 만든다. 맵 해시를 모아 만들지 않는다 — 그러면 표시용 필드가 바뀌었을 때
        /// ETag 가 그대로 남아, 새 필드를 붙인 서버가 옛 응답을 캐시한 클라이언트에게 아무것도
        /// 알려 주지 못한다.
        private static string MakeETag(string json)
        {
            var hash = StateHash.Combine(StateHash.Seed, json);

            return $"\"{hash:x8}\"";
        }
    }
}
