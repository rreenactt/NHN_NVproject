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
        public const string DefaultRoomId = "default";

        private const int MaxRoomIdLength = 32;
        private const int MaxRooms = 16;

        private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.Ordinal);
        private readonly WorldMap _map;
        private readonly NetworkConditionSimulator _network;
        private readonly ILogger<RoomRegistry> _logger;

        public RoomRegistry(WorldMap map, NetworkConditionSimulator network, ILogger<RoomRegistry> logger)
        {
            _map = map;
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

            if (_rooms.Count >= MaxRooms)
            {
                _logger.LogWarning("룸 수 상한 {MaxRooms} 에 도달해 {RoomId} 를 만들지 않는다.", MaxRooms, roomId);
                return null;
            }

            return _rooms.GetOrAdd(roomId, id => new Room(id, _map, _network, _logger));
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
            if (string.IsNullOrEmpty(roomId) || roomId.Length > MaxRoomIdLength)
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
