using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using NV.Realtime.Contracts;
using NV.Realtime.Transport;
using NV.Shared.Contracts.Enums;
using NV.Shared.Simulation;

namespace NV.Realtime.Simulation
{
    /// 룸을 만들 수 없는 이유. 엔드포인트가 상태코드로 옮긴다.
    internal enum RoomCreateError
    {
        None = 0,

        /// 등록되지 않은 맵 id. 기본 맵으로 대신 열지 않는다.
        UnknownMap = 1,

        /// 동시에 열어 두는 룸 수 상한.
        RoomLimit = 2,
    }

    /// 룸 목록. 룸은 명시적으로 만들어지고 초대 코드로만 참가한다.
    ///
    /// 예전에는 접속 쿼리의 룸 id 로 룸이 그 자리에서 생겼다. 초대 코드 모델에서는
    /// 그 반대여야 한다 — 코드를 모르는 접속은 거부되어야 하고, 그러려면 "없는 룸을
    /// 만들어 준다" 는 경로가 아예 없어야 한다.
    internal sealed class RoomRegistry : IRoomQuery
    {
        private sealed class Entry
        {
            public Entry(Room room, string hostToken, uint createdTick)
            {
                Room = room;
                HostToken = hostToken;
                LastOccupiedTick = createdTick;
            }

            public Room Room { get; }

            /// 방장을 주장하는 토큰. 룸이 아니라 레지스트리가 들고 있다 —
            /// 룸은 전송도 인증도 모르는 채로 남아야 소켓 없이 테스트할 수 있다.
            public string HostToken { get; }

            /// 마지막으로 사람이 있던 틱. 회수 판단이 이 값 하나로 끝난다.
            public uint LastOccupiedTick { get; set; }
        }

        private readonly ConcurrentDictionary<string, Entry> _rooms = new(StringComparer.Ordinal);
        private readonly RoomMaps _maps;
        private readonly StaticRooms _staticRooms;
        private readonly NetworkConditionSimulator _network;
        private readonly ILogger<RoomRegistry> _logger;

        /// 틱 루프가 갱신하고 HTTP 스레드가 읽는다. 생성 시점을 찍는 데만 쓴다.
        private uint _serverTick;

        public RoomRegistry(
            RoomMaps maps,
            StaticRooms staticRooms,
            NetworkConditionSimulator network,
            ILogger<RoomRegistry> logger)
        {
            _maps = maps;
            _staticRooms = staticRooms;
            _network = network;
            _logger = logger;

            CreateStaticRooms();
        }

        /// 틱 루프가 순회한다. 룸 수가 16 이하라 스냅샷 비용은 문제되지 않는다.
        public IEnumerable<Room> All
        {
            get
            {
                foreach (var entry in _rooms.Values)
                {
                    yield return entry.Room;
                }
            }
        }

        /// 초대 코드 룸을 만든다. 코드와 방장 토큰은 만든 사람에게만 돌아간다.
        public bool TryCreate(string? mapId, out string code, out string hostToken, out RoomCreateError error)
        {
            code = string.Empty;
            hostToken = string.Empty;

            var resolvedMapId = string.IsNullOrEmpty(mapId) ? RoomMaps.DefaultMapId : mapId!;
            var map = _maps.ByMapId(resolvedMapId);

            if (map is null)
            {
                error = RoomCreateError.UnknownMap;
                return false;
            }

            if (_rooms.Count >= RealtimeConstants.Rooms.MaxRooms)
            {
                _logger.LogWarning("룸 수 상한 {MaxRooms} 에 도달해 새 룸을 만들지 않는다.", RealtimeConstants.Rooms.MaxRooms);
                error = RoomCreateError.RoomLimit;
                return false;
            }

            var createdTick = Volatile.Read(ref _serverTick);

            for (var attempt = 0; attempt < RealtimeConstants.Rooms.CodeGenerationAttempts; attempt++)
            {
                var candidate = InviteCode.NewCode();
                var token = InviteCode.NewHostToken();
                var entry = new Entry(
                    new Room(candidate, map, _network, _logger),
                    token,
                    createdTick);

                if (!_rooms.TryAdd(candidate, entry))
                {
                    continue;
                }

                _logger.LogInformation(
                    "룸 {RoomId} 생성. 맵 {MapId}({MapName}) 해시 {MapHash:X8} 박스 {BoxCount}개",
                    candidate,
                    resolvedMapId,
                    map.Name,
                    map.Hash,
                    map.Collision.BoxCount);

                code = candidate;
                hostToken = token;
                error = RoomCreateError.None;
                return true;
            }

            // 여기까지 오면 코드 공간이나 알파벳이 줄어든 것이다. 룸 상한과 같은
            // 취급을 하되 로그는 따로 남긴다 — 원인이 전혀 다르다.
            _logger.LogError(
                "초대 코드를 {Attempts} 번 시도해 만들지 못했다. 코드 길이나 알파벳을 확인한다.",
                RealtimeConstants.Rooms.CodeGenerationAttempts);

            error = RoomCreateError.RoomLimit;
            return false;
        }

        public bool TryGet(string? roomId, out Room room)
        {
            if (roomId != null && _rooms.TryGetValue(roomId, out var entry))
            {
                room = entry.Room;
                return true;
            }

            room = null!;
            return false;
        }

        /// 이 토큰이 그 룸의 방장 토큰인가.
        ///
        /// 고정 시간 비교를 쓴다. 토큰은 16바이트 무작위값이고 이 경로는 접속마다
        /// 한 번뿐이라 실질적 위험은 낮지만, 비교 방식을 여기 한 곳에 고정해 두면
        /// 다음에 인증이 늘어날 때 같은 자리를 쓰게 된다.
        public bool IsHostToken(string? roomId, string? token)
        {
            if (roomId == null || string.IsNullOrEmpty(token))
            {
                return false;
            }

            if (!_rooms.TryGetValue(roomId, out var entry) || entry.HostToken.Length != token!.Length)
            {
                return false;
            }

            var difference = 0;
            for (var index = 0; index < token.Length; index++)
            {
                difference |= entry.HostToken[index] ^ token[index];
            }

            return difference == 0;
        }

        /// 틱 루프가 매 틱 호출한다. 아무도 없는 룸을 회수한다.
        public void Sweep(uint serverTick)
        {
            Volatile.Write(ref _serverTick, serverTick);

            foreach (var pair in _rooms)
            {
                var entry = pair.Value;

                if (entry.Room.PlayerCount > 0)
                {
                    entry.LastOccupiedTick = serverTick;
                    continue;
                }

                if (entry.Room.IsStatic)
                {
                    continue;
                }

                var idle = serverTick - entry.LastOccupiedTick;

                if (idle < RealtimeConstants.Rooms.EmptyExpiryTicks)
                {
                    continue;
                }

                if (_rooms.TryRemove(pair.Key, out _))
                {
                    _logger.LogInformation(
                        "룸 {RoomId} 회수. {Seconds}초 동안 아무도 없었다.",
                        pair.Key,
                        idle / SimConstants.TickRate);
                }
            }
        }

        public bool TryGetRoom(string? roomId, out RoomSummary summary)
        {
            if (roomId != null && _rooms.TryGetValue(roomId, out var entry))
            {
                summary = entry.Room.Summarize();
                return true;
            }

            summary = default;
            return false;
        }

        public IReadOnlyList<RoomSummary> ListRooms()
        {
            var summaries = new List<RoomSummary>(_rooms.Count);
            foreach (var entry in _rooms.Values)
            {
                summaries.Add(entry.Room.Summarize());
            }

            return summaries;
        }

        /// 룸 id 의 형식. 초대 코드는 이 규칙의 부분집합이고, 정적 룸 id 도 같은 규칙을 쓴다.
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

        /// 설정에 적힌 룸을 미리 열어 둔다. 하나라도 형식이나 맵이 어긋나면 기동을 멈춘다.
        ///
        /// 조용히 건너뛰면 개발용 룸이 없는 채로 서버가 올라가고, 증상은 접속이
        /// "없는 방" 으로 거부되는 것뿐이라 설정 오타를 찾는 데 시간이 걸린다.
        private void CreateStaticRooms()
        {
            foreach (var pair in _staticRooms.MapByRoom)
            {
                if (!IsValidRoomId(pair.Key))
                {
                    throw new InvalidOperationException($"정적 룸 id '{pair.Key}' 의 형식이 어긋난다.");
                }

                var map = _maps.ByMapId(pair.Value);
                if (map is null)
                {
                    throw new InvalidOperationException($"정적 룸 '{pair.Key}' 가 등록되지 않은 맵 '{pair.Value}' 를 가리킨다.");
                }

                var entry = new Entry(
                    new Room(pair.Key, map, _network, _logger, isStatic: true),
                    string.Empty,
                    0u);

                _rooms[pair.Key] = entry;

                _logger.LogInformation(
                    "정적 룸 {RoomId} 열림. 맵 {MapId}({MapName}) 해시 {MapHash:X8}. 방장 없이 시작할 수 있다.",
                    pair.Key,
                    pair.Value,
                    map.Name,
                    map.Hash);
            }
        }
    }
}
