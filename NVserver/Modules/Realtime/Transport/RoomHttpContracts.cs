namespace NV.Realtime.Transport
{
    /// 방 만들기 요청. 맵을 비우면 기본 맵으로 만든다.
    internal sealed class CreateRoomRequest
    {
        public string? Map { get; set; }

        /// 이 방을 `GET /rooms` 목록에 실을 것인가.
        ///
        /// `bool?` 이다. **보내지 않으면 비공개**로 만든다 — 필드를 모르는 옛 클라이언트가
        /// 만든 방이 본인도 모르게 목록에 뜨는 일이 없어야 한다. 노출은 기본값이 아니라
        /// 선택이어야 하고, 그 선택을 하지 않은 요청은 노출하지 않는 쪽으로 해석한다.
        public bool? IsPublic { get; set; }
    }

    /// 방 만들기 응답. `hostToken` 은 이 응답에만 실린다.
    ///
    /// 코드를 아는 사람은 참가만 할 수 있고 시작은 못 한다. 그 차이가 이 토큰이다.
    /// 다시 받아 볼 수 있는 경로를 두지 않는다 — 조회로 토큰을 얻을 수 있으면
    /// 코드를 아는 누구나 방장이 된다.
    internal sealed class CreateRoomResponse
    {
        public string Code { get; set; } = string.Empty;

        public string HostToken { get; set; } = string.Empty;

        /// 요청한 맵 id.
        public string Map { get; set; } = string.Empty;

        /// 서버가 로드한 맵의 이름. 클라이언트가 어느 씬을 열지 이것으로 정한다.
        public string MapName { get; set; } = string.Empty;

        public uint MapHash { get; set; }

        public int Capacity { get; set; }

        public int MinPlayers { get; set; }

        /// 서버가 실제로 적용한 공개 여부. 요청한 값을 그대로 돌려주는 것이 아니라
        /// 방에 적힌 값이다 — 요청이 무시되었는지 화면이 알 수 있어야 한다.
        public bool IsPublic { get; set; }
    }

    /// 참가 전 조회 응답.
    ///
    /// 정원 초과·진행 중에도 같은 본문을 돌려준다. 상태코드가 "들어갈 수 있는가" 를
    /// 답하고 본문이 "지금 어떤 상태인가" 를 답한다. 본문을 빼면 로비가 "8/8 진행 중"
    /// 같은 표시를 할 수 없고, 상태코드를 빼면 브라우저에서 실패 사유가 뭉친다.
    internal sealed class RoomInfoResponse
    {
        public string Code { get; set; } = string.Empty;

        public string MapName { get; set; } = string.Empty;

        public uint MapHash { get; set; }

        /// `RoomPhase` 의 정수값. 문자열 표현을 함께 싣지 않는다 —
        /// 한 값을 두 형태로 보내면 다음에 한쪽만 고치게 된다.
        public byte Phase { get; set; }

        public int PlayerCount { get; set; }

        public int Capacity { get; set; }

        /// 방장의 PlayerId. 없으면 `RoomStateHeader.NoPlayer`(255) 다.
        public byte HostPlayerId { get; set; }

        public int MinPlayers { get; set; }

        /// 목록에 실리는 방인가.
        ///
        /// 조회 응답에도 싣는다. 코드로 들어온 사람이 자기가 지금 비공개 방에 있다는
        /// 것을 알아야 코드를 함부로 흘리지 않는다.
        public bool IsPublic { get; set; }
    }

    /// 실패 응답. 상태코드로도 갈리지만, 로그와 `curl` 에서 사유가 바로 보이는 편이
    /// 서버·클라이언트를 나란히 놓고 볼 때 훨씬 빠르다.
    internal sealed class ErrorResponse
    {
        public ErrorResponse(string error)
        {
            Error = error;
        }

        public string Error { get; set; }
    }
}
