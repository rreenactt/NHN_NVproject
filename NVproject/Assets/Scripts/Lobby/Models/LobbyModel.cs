using System.Collections.Generic;
using NV.Client.Net.Session;
using NV.Shared.Contracts.Enums;

namespace NV.Client.Lobby.Models
{
    /// 방 목록을 지금 알 수 있는가.
    ///
    /// `Ready` 와 `Unavailable` 을 반드시 나눈다. 목록이 비어 있는 것("방이 없다")과
    /// 서버가 목록을 공개하지 않는 것("알 수 없다")은 사용자가 다음에 할 일이 다르다 —
    /// 앞은 방을 만들면 되고, 뒤는 코드를 받아야 한다. 둘을 같은 빈 화면으로 만들면
    /// 목록을 끈 서버에서 이 로비는 고장난 것으로 보인다.
    public enum RoomListStatus
    {
        /// 아직 한 번도 조회하지 않았다.
        Unknown = 0,

        /// 조회에 성공했다. `Rooms` 가 그 결과이며 0개일 수 있다.
        Ready = 1,

        /// 서버가 목록을 공개하지 않는다(`Realtime:AllowRoomListing` 이 꺼져 있다).
        /// 오류가 아니다.
        Unavailable = 2,

        /// 조회가 실패했다. `ListFailure` 에 사유가 있다.
        Failed = 3,
    }

    /// 서버에 닿는가.
    public enum ServerStatus
    {
        Unknown = 0,
        Checking = 1,
        Online = 2,
        Offline = 3,
    }

    /// 로비 화면의 상태.
    ///
    /// 접속 단계는 여기 없다. 그것은 `NetSession.State` 가 정본이고, 사본을 두면 반드시
    /// 어긋나는데 그 차이는 화면만 봐서는 잡히지 않는다. 이 모델이 갖는 것은 세션이
    /// 모르는 것 — 방 목록, 서버 생존, 온라인 인원, 마지막 갱신 시각 — 뿐이다.
    public sealed class LobbyModel
    {
        private readonly List<RoomInfo> _rooms = new List<RoomInfo>();

        public IReadOnlyList<RoomInfo> Rooms => _rooms;

        public RoomListStatus ListStatus { get; private set; } = RoomListStatus.Unknown;

        public SessionFailureKind ListFailure { get; private set; } = SessionFailureKind.None;

        /// 마지막으로 목록을 받은 시각(`Time.unscaledTime`). 0 이면 아직 없다.
        public float LastRefreshAt { get; private set; }

        public ServerStatus Server { get; private set; } = ServerStatus.Unknown;

        /// 공개된 방들의 인원 합계.
        ///
        /// 서버가 알려 주는 값이 아니다 — 전역 세션 수를 내주는 엔드포인트가 없다.
        /// 그래서 목록이 없으면 이 값도 없고, 화면은 0 이 아니라 아무것도 보이지 않아야
        /// 한다. 0 은 "아무도 없다" 라는 거짓말이다.
        public int OnlinePlayers { get; private set; }

        public bool HasOnlineCount => ListStatus == RoomListStatus.Ready;

        /// 들어갈 수 있는 방이 하나라도 있는가. 빠른 참가 버튼이 이것으로 켜진다.
        public bool HasJoinableRoom
        {
            get
            {
                for (var index = 0; index < _rooms.Count; index++)
                {
                    if (IsJoinable(_rooms[index]))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public void SetRooms(IReadOnlyList<RoomInfo> rooms, float now)
        {
            _rooms.Clear();

            var total = 0;

            if (rooms != null)
            {
                for (var index = 0; index < rooms.Count; index++)
                {
                    _rooms.Add(rooms[index]);
                    total += rooms[index].PlayerCount;
                }
            }

            OnlinePlayers = total;
            ListStatus = RoomListStatus.Ready;
            ListFailure = SessionFailureKind.None;
            LastRefreshAt = now;
        }

        public void SetListUnavailable(float now)
        {
            _rooms.Clear();
            OnlinePlayers = 0;
            ListStatus = RoomListStatus.Unavailable;
            ListFailure = SessionFailureKind.None;
            LastRefreshAt = now;
        }

        public void SetListFailed(SessionFailureKind failure, float now)
        {
            _rooms.Clear();
            OnlinePlayers = 0;
            ListStatus = RoomListStatus.Failed;
            ListFailure = failure;
            LastRefreshAt = now;
        }

        public void SetServer(ServerStatus status)
        {
            Server = status;
        }

        /// 지금 들어갈 수 있는 방인가.
        ///
        /// 조회 시점의 값이다. 참가할 때 이미 차 있거나 시작되었을 수 있고, 그때는
        /// 서버가 503·409 로 답한다 — 이 판정은 화면의 친절이지 보증이 아니다.
        public static bool IsJoinable(RoomInfo room)
        {
            return room.Phase == RoomPhase.Waiting && room.PlayerCount < room.Capacity;
        }
    }
}
