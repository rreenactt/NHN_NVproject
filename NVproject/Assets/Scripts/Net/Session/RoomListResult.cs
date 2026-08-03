using System;
using System.Collections.Generic;

namespace NV.Client.Net.Session
{
    /// `GET /rooms` 결과.
    ///
    /// 실패가 두 종류다. **서버가 목록을 공개하지 않는 것(404)은 실패가 아니다** —
    /// `Realtime:AllowRoomListing` 이 꺼진 것이고, 초대 코드 모델에서는 그쪽이 기본값이며
    /// 소스 주석은 공개 목록을 "기능이 아니라 결함" 이라고 적고 있다. 그것을 오류로
    /// 다루면 정상 배포에서 로비가 고장난 것처럼 보인다.
    ///
    /// 그래서 `Unavailable` 과 `Failure` 를 나눈다. 앞은 "이 서버는 안 알려 준다",
    /// 뒤는 "물어봤는데 답을 못 받았다" 이고 화면 문구가 달라야 한다.
    public readonly struct RoomListResult
    {
        public RoomListResult(IReadOnlyList<RoomInfo> rooms)
        {
            Rooms = rooms ?? Array.Empty<RoomInfo>();
            Unavailable = false;
            Failure = SessionFailureKind.None;
        }

        private RoomListResult(bool unavailable, SessionFailureKind failure)
        {
            Rooms = Array.Empty<RoomInfo>();
            Unavailable = unavailable;
            Failure = failure;
        }

        public IReadOnlyList<RoomInfo> Rooms { get; }

        /// 서버가 목록을 공개하지 않는다. 오류가 아니다.
        public bool Unavailable { get; }

        public SessionFailureKind Failure { get; }

        public bool Ok => !Unavailable && Failure == SessionFailureKind.None;

        public static RoomListResult NotPublished()
        {
            return new RoomListResult(true, SessionFailureKind.None);
        }

        public static RoomListResult Failed(SessionFailureKind failure)
        {
            return new RoomListResult(false, failure);
        }
    }

    /// `JsonUtility` 는 최상위 배열을 읽지 못한다.
    ///
    /// `GET /rooms` 는 `[ {...}, {...} ]` 를 준다. 그대로 넘기면 예외가 아니라 **null 을
    /// 돌려주므로**, 파싱 실패가 "방이 0개" 로 조용히 둔갑한다. 응답을 객체 하나로 감싸
    /// 넣는 것이 이 직렬화기에서 배열을 읽는 유일한 방법이다.
    [Serializable]
    internal sealed class RoomListResponseDto
    {
        public RoomInfoResponseDto[] items;

        public const string WrapperPrefix = "{\"items\":";
        public const string WrapperSuffix = "}";
    }
}
