using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using NV.Realtime.Contracts;
using NV.Realtime.Transport;
using NV.Shared.Collision;
using NV.Shared.Contracts;
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

        /// 겹치지 않는 코드를 만들지 못했다.
        ///
        /// 길이를 상한까지 늘려도 실패했다는 뜻이므로 정상적으로는 도달하지 않는다.
        /// 알파벳이나 길이 규칙을 줄이는 변경이 들어왔을 때만 나타나며, 조용히
        /// 넘기지 않고 사유를 따로 두어 로그와 응답에서 구분되게 한다.
        CodeExhausted = 2,
    }

    /// 룸 목록. 룸은 명시적으로 만들어지고 초대 코드로만 참가한다.
    ///
    /// 예전에는 접속 쿼리의 룸 id 로 룸이 그 자리에서 생겼다. 초대 코드 모델에서는
    /// 그 반대여야 한다 — 코드를 모르는 접속은 거부되어야 하고, 그러려면 "없는 룸을
    /// 만들어 준다" 는 경로가 아예 없어야 한다.
    ///
    /// **동시 룸 수에 상한이 없다.** 상한은 임의의 쿼리스트링으로 룸이 무한히 생기던
    /// 시절의 방어선이었고, 룸이 명시적으로만 만들어지는 지금은 그 자리를 두 가지가
    /// 대신한다 — 빈 룸 회수(60초)와 생성 요청 제한(Api). 대신 코드 길이가 룸 수에
    /// 따라 늘어난다. 공간이 고정이면 룸이 늘수록 충돌이 잦아진다.
    internal sealed class RoomRegistry : IRoomQuery
    {
        private sealed class Entry
        {
            public Entry(Room room, string hostToken, uint createdTick)
            {
                Room = room;
                HostToken = hostToken;
                CreatedTick = createdTick;
            }

            public Room Room { get; }

            /// 방장을 주장하는 토큰. 룸이 아니라 레지스트리가 들고 있다 —
            /// 룸은 전송도 인증도 모르는 채로 남아야 소켓 없이 테스트할 수 있다.
            public string HostToken { get; }

            /// 만들어진 틱. 아직 아무도 들어오지 않은 룸의 회수 시점을 여기서 잰다.
            public uint CreatedTick { get; }

            /// 한 번이라도 사람이 있었는가.
            ///
            /// 이 값이 회수 규칙을 가른다. 참이면 비는 즉시 회수하고, 거짓이면 만든
            /// 사람이 붙을 시간을 준다. 둘을 합치면 방이 만들어지자마자 사라진다.
            public bool WasOccupied { get; set; }
        }

        private readonly ConcurrentDictionary<string, Entry> _rooms = new(StringComparer.Ordinal);
        private readonly RoomMaps _maps;
        private readonly StaticRooms _staticRooms;
        private readonly NetworkConditionSimulator _network;
        private readonly RealtimeOptions _options;
        private readonly ILogger<RoomRegistry> _logger;

        /// 틱 루프가 갱신하고 HTTP 스레드가 읽는다. 생성 시점을 찍는 데만 쓴다.
        private uint _serverTick;

        public RoomRegistry(
            RoomMaps maps,
            StaticRooms staticRooms,
            NetworkConditionSimulator network,
            RealtimeOptions options,
            ILogger<RoomRegistry> logger)
        {
            _maps = maps;
            _staticRooms = staticRooms;
            _network = network;
            _options = options;
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
        ///
        /// <param name="isPublic">
        /// 목록(`GET /rooms`)에 실을 것인가. **기본은 비공개다.** 필드를 보내지 않는
        /// 옛 클라이언트가 방을 만들면 비공개가 되어야 한다 — 반대로 두면 클라이언트를
        /// 업데이트하지 않은 사람의 방이 본인도 모르게 목록에 뜬다.
        /// </param>
        public bool TryCreate(
            string? mapId,
            bool isPublic,
            out string code,
            out string hostToken,
            out RoomCreateError error)
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

            var createdTick = Volatile.Read(ref _serverTick);

            // 길이는 지금 열려 있는 룸 수에서 나온다. 룸 수에 상한이 없으므로 공간을
            // 고정하면 룸이 늘수록 충돌이 잦아진다.
            var length = InviteCode.LengthFor(_rooms.Count);

            while (length <= InviteCodeFormat.MaxLength)
            {
                for (var attempt = 0; attempt < RealtimeConstants.Rooms.CodeGenerationAttempts; attempt++)
                {
                    var candidate = InviteCode.NewCode(length);
                    var token = InviteCode.NewHostToken();
                    // 봇 설정을 넘기지 않는다. 초대 코드 룸에는 봇이 생기지 않으며,
                    // 그 이유는 `BotOptions` 에 적혀 있다 — 참가자가 있는 룸은 회수되지
                    // 않으므로 봇이 남은 초대 코드 룸은 영구히 살아남는다.
                    var entry = new Entry(
                        new Room(candidate, map, _network, _logger, isPublic: isPublic),
                        token,
                        createdTick);

                    // 충돌은 여기서 흡수된다. `TryAdd` 가 실패했다는 것은 그 코드를
                    // 이미 다른 방이 쓰고 있다는 뜻이며, 같은 코드가 두 방에 붙는
                    // 경로는 없다 — 경쟁하는 요청 둘이 같은 코드를 뽑아도 하나만 이긴다.
                    if (!_rooms.TryAdd(candidate, entry))
                    {
                        continue;
                    }

                    _logger.LogInformation(
                        "룸 {RoomId} 생성({Visibility}). 맵 {MapId}({MapName}) 해시 {MapHash:X8} 박스 {BoxCount}개, 룸 {RoomCount}개",
                        candidate,
                        isPublic ? "공개" : "비공개",
                        resolvedMapId,
                        map.Name,
                        map.Hash,
                        map.Collision.BoxCount,
                        _rooms.Count);

                    code = candidate;
                    hostToken = token;
                    error = RoomCreateError.None;
                    return true;
                }

                // 같은 길이에서 연달아 겹혔다. 부하율 계산이 실제 상황과 어긋났다는
                // 뜻이므로 길이를 늘려 공간을 키운다. 실패로 끝내는 것보다 낫다.
                _logger.LogWarning(
                    "{Length}자 코드가 {Attempts}회 연속 겹쳤다. 한 자 늘려 다시 시도한다. 룸 {RoomCount}개",
                    length,
                    RealtimeConstants.Rooms.CodeGenerationAttempts,
                    _rooms.Count);

                length++;
            }

            // 상한까지 늘려도 실패했다. 알파벳이나 길이 규칙이 줄어든 경우다.
            _logger.LogError(
                "초대 코드를 {MaxLength}자까지 늘려도 만들지 못했다. 코드 알파벳과 길이 규칙을 확인한다.",
                InviteCodeFormat.MaxLength);

            error = RoomCreateError.CodeExhausted;
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

        /// 틱 루프가 매 틱 호출한다. 참가자가 없는 룸을 회수한다.
        ///
        /// 규칙이 둘이다.
        /// - 한 번이라도 사람이 있었던 룸은 비는 즉시 회수한다. 마지막 사람이 나간
        ///   방을 남겨 둘 이유가 없다 — 코드도 방장 토큰도 그 방과 함께 끝난다.
        /// - 아직 아무도 들어오지 않은 룸은 만든 사람이 붙을 시간을 준다. `POST /rooms`
        ///   와 WebSocket 접속 사이에는 참가자가 0이므로, 그 구간에서 즉시 회수하면
        ///   모든 방이 만든 사람이 들어오기 전에 사라진다.
        ///
        /// 설정으로 열어 둔 정적 룸은 회수하지 않는다. 사라지면 다시 만들 방법이 없다.
        public void Sweep(uint serverTick)
        {
            Volatile.Write(ref _serverTick, serverTick);

            foreach (var pair in _rooms)
            {
                var entry = pair.Value;

                if (entry.Room.PlayerCount > 0)
                {
                    entry.WasOccupied = true;
                    continue;
                }

                if (entry.Room.IsStatic)
                {
                    continue;
                }

                if (entry.WasOccupied)
                {
                    if (_rooms.TryRemove(pair.Key, out _))
                    {
                        _logger.LogInformation("룸 {RoomId} 회수. 마지막 참가자가 나갔다.", pair.Key);
                    }

                    continue;
                }

                var waited = serverTick - entry.CreatedTick;

                if (waited < RealtimeConstants.Rooms.UnjoinedExpiryTicks)
                {
                    continue;
                }

                if (_rooms.TryRemove(pair.Key, out _))
                {
                    _logger.LogInformation(
                        "룸 {RoomId} 회수. 만든 뒤 {Seconds}초 동안 아무도 들어오지 않았다.",
                        pair.Key,
                        waited / SimConstants.TickRate);
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

        /// 목록에 실을 방들. **공개로 만들어진 방만 나온다.**
        ///
        /// 거르는 자리를 레지스트리에 둔다. 엔드포인트에서 거르면 "전부 돌려주는" 메서드가
        /// 남아 있게 되고, 다음에 목록이 필요한 곳이 생겼을 때 그쪽을 부르면 비공개 방이
        /// 조용히 새어 나간다. 새지 않는 유일한 방법은 새는 메서드를 두지 않는 것이다.
        public IReadOnlyList<RoomSummary> ListPublicRooms()
        {
            var summaries = new List<RoomSummary>(_rooms.Count);
            foreach (var entry in _rooms.Values)
            {
                if (!entry.Room.IsPublic)
                {
                    continue;
                }

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
            foreach (var pair in _staticRooms.Rooms)
            {
                if (!IsValidRoomId(pair.Key))
                {
                    throw new InvalidOperationException($"정적 룸 id '{pair.Key}' 의 형식이 어긋난다.");
                }

                var profile = pair.Value;
                var map = _maps.ByMapId(profile.MapId);
                if (map is null)
                {
                    throw new InvalidOperationException($"정적 룸 '{pair.Key}' 가 등록되지 않은 맵 '{profile.MapId}' 를 가리킨다.");
                }

                // 봇 설정은 정적 룸에만 넘어간다. 그 제한이 이 호출이다. 전역 설정 위에
                // 프로필을 겹치므로, 룸마다 다른 행동·역할·채움 인원을 가질 수 있다.
                OpenStaticRoom(pair.Key, profile.MapId, map, profile.ResolveBots(_options.Bots));
            }
        }

        /// 정적 룸 하나를 연다. 명시 설정과 맵당 자동 생성이 같은 규칙을 지나야 하므로
        /// 이 자리가 하나여야 한다.
        private void OpenStaticRoom(string roomId, string mapId, WorldMap map, BotOptions bots)
        {
            // 정적 룸은 공개다. 개발용으로 미리 열어 둔 방이고, 목록에 뜨지 않으면
            // 로비에서 여기에 닿을 길이 없다 — id 가 초대 코드 형식(6자 이상)을
            // 만족하지 않아 코드 입력 칸으로도 들어갈 수 없다.
            var entry = new Entry(
                new Room(roomId, map, _network, _logger, isStatic: true, isPublic: true, bots: bots),
                string.Empty,
                0u);

            _rooms[roomId] = entry;

            _logger.LogInformation(
                "정적 룸 {RoomId} 열림. 맵 {MapId}({MapName}) 해시 {MapHash:X8}. 방장 없이 시작할 수 있다.",
                roomId,
                mapId,
                map.Name,
                map.Hash);

            if (bots.Enabled)
            {
                // 개발 전용 기능이 켜져 있다는 것을 기동 로그에 남긴다. 네트워크 조건
                // 주입기와 같은 취급이다 — 켜진 것을 모르고 관찰하면 봇의 존재가
                // 서버 버그로 보인다.
                _logger.LogWarning(
                    "정적 룸 {RoomId} 의 봇 채우기가 켜져 있다. 인원 {FillTo} 까지 채우고 봇 행동은 {Behavior}, 역할은 {Role} 다. 개발 전용 설정이다.",
                    roomId,
                    bots.FillTo <= 0 ? RealtimeConstants.Rooms.MinPlayersToStart : bots.FillTo,
                    bots.Behavior,
                    bots.Role);
            }
        }
    }
}
