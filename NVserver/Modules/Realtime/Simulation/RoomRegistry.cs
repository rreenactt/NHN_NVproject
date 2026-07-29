using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using NV.Realtime.Contracts;
using NV.Realtime.Transport;
using NV.Shared.Collision;

namespace NV.Realtime.Simulation
{
    /// 룸 목록. 매치메이킹이 없는 동안 접속 쿼리스트링이 룸을 지정한다.
    /// 임의의 문자열로 룸이 무한히 생기지 않도록 형식과 개수를 제한한다.
    internal sealed class RoomRegistry : IRoomQuery
    {
        private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.Ordinal);
        private readonly RoomMaps _maps;
        private readonly NetworkConditionSimulator _network;
        private readonly ILogger<RoomRegistry> _logger;

        public RoomRegistry(RoomMaps maps, NetworkConditionSimulator network, ILogger<RoomRegistry> logger)
        {
            _maps = maps;
            _network = network;
            _logger = logger;
        }

        public IEnumerable<Room> All => _rooms.Values;

        /// 형식이 어긋나거나 룸 수 상한을 넘으면 null.
        public Room? GetOrCreate(string roomId)
        {
            if (!IsValidRoomId(roomId))
            {
                return null;
            }

            if (_rooms.TryGetValue(roomId, out var existing))
            {
                return existing;
            }

            if (_rooms.Count >= RealtimeConstants.Rooms.MaxRooms)
            {
                _logger.LogWarning("룸 수 상한 {MaxRooms} 에 도달해 {RoomId} 를 만들지 않는다.", RealtimeConstants.Rooms.MaxRooms, roomId);
                return null;
            }

            // 맵은 룸 id 로 고른다. 등록되지 않은 id 는 기본 맵으로 열린다 —
            // 빈 콜리전으로 열면 플레이어가 지형을 통과하고 증상이 로직 버그처럼 보인다.
            return _rooms.GetOrAdd(roomId, id =>
            {
                var map = _maps.For(id);

                // 어느 룸이 어느 맵을 물었는지 남긴다. 해시 불일치를 만났을 때
                // 클라이언트가 어느 씬을 열었는지만 확인하면 원인이 갈린다.
                _logger.LogInformation(
                    "룸 {RoomId} 생성. 맵 {MapName} 해시 {MapHash:X8} 박스 {BoxCount}개",
                    id,
                    map.Name,
                    map.Hash,
                    map.Collision.BoxCount);

                return new Room(id, map, _network, _logger);
            });
        }

        public bool TryGetRoom(string? roomId, out RoomSummary summary)
        {
            if (roomId != null && _rooms.TryGetValue(roomId, out var room))
            {
                summary = room.Summarize();
                return true;
            }

            summary = default;
            return false;
        }

        public IReadOnlyList<RoomSummary> ListRooms()
        {
            var summaries = new List<RoomSummary>(_rooms.Count);
            foreach (var room in _rooms.Values)
            {
                summaries.Add(room.Summarize());
            }

            return summaries;
        }

        public static bool IsValidRoomId(string roomId)
        {
            if (string.IsNullOrEmpty(roomId) || roomId.Length > RealtimeConstants.Rooms.MaxRoomIdLength)
            {
                return false;
            }

            foreach (var character in roomId)
            {
                var allowed = (character >= 'a' && character <= 'z')
                    || (character >= '0' && character <= '9')
                    || character == '-';

                if (!allowed)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
