namespace NV.Client.Net.Session
{
    /// 접속이 실패한 이유.
    ///
    /// 이 열거형이 존재하는 이유가 브라우저에 있다. WebSocket 핸드셰이크가 거부되면
    /// 브라우저는 닫힘 코드 1006 하나만 주고, 그러면 서버 미기동·주소 오타·버전
    /// 불일치·없는 방·정원 초과·진행 중이 전부 같은 모습으로 화면에 나타난다.
    /// 접속 전에 HTTP 로 한 번 물어보는 것이 그 여섯 가지를 갈라내는 유일한 수단이다.
    public enum SessionFailureKind
    {
        None = 0,

        /// 서버에 닿지 못했다. 미기동, 주소 오타, 방화벽이 전부 여기다 —
        /// HTTP 요청 자체가 실패한 경우이며 그 이상은 클라이언트가 알 수 없다.
        ServerUnreachable,

        /// 서버와 클라이언트의 프로토콜 버전이 다르다.
        VersionMismatch,

        /// 코드 형식이 어긋난다. 입력 칸에서 바로 잡는다.
        InvalidCode,

        /// 그 코드의 방이 없다. 오타이거나 만료되었다.
        UnknownCode,

        /// 등록되지 않은 맵으로 방을 만들려 했다.
        UnknownMap,

        /// 방이 꽉 찼다.
        RoomFull,

        /// 이미 시작한 방이다. 비대칭 매치 중간 합류는 규칙을 깬다.
        RoomInProgress,

        /// 요청이 너무 잦아 서버가 거절했다.
        ///
        /// 동시 룸 수에 상한이 없어진 대신 서버가 생성·조회 요청의 속도를 제한한다.
        /// 사람이 방을 만들거나 코드를 넣는 속도로는 걸리지 않는 값이다 — 여기에
        /// 걸렸다면 재시도 루프가 돌고 있거나 여러 클라이언트가 한 IP 를 쓰고 있다.
        TooManyRequests,

        /// 서버가 겹치지 않는 초대 코드를 만들지 못했다.
        ///
        /// 정상적으로는 나타나지 않는다. 코드 길이나 알파벳 규칙이 줄어든 서버에서만
        /// 나오며, 그 경우 재시도해도 결과가 같다.
        RoomCreateFailed,

        /// 소켓은 열렸는데 서버가 Welcome 을 보내지 않았다.
        HandshakeTimeout,

        /// 붙어 있다가 끊겼다.
        ConnectionLost,

        /// 서버가 판정하는 지형과 클라이언트가 그리는 지형이 다르다.
        MapHashMismatch,
    }

    /// 실패 하나. 사유와 사람이 읽을 문구, 그리고 재시도해도 되는지를 함께 담는다.
    ///
    /// 재시도 가능 여부를 사유와 붙여 두는 이유: 결과가 같은 요청을 반복하는 것은
    /// 정보가 없다. 버전 불일치는 클라이언트를 다시 빌드해야 하고, 없는 코드는
    /// 다시 물어봐야 하며, 그 둘에 자동 재시도를 붙이면 화면만 계속 깜빡인다.
    public readonly struct SessionFailure
    {
        public static readonly SessionFailure None = default;

        public SessionFailure(SessionFailureKind kind, string message, string nextAction, bool retryable)
        {
            Kind = kind;
            Message = message;
            NextAction = nextAction;
            Retryable = retryable;
        }

        public SessionFailureKind Kind { get; }

        public string Message { get; }

        /// 사용자가 지금 할 수 있는 일. 문구를 요약하면 사유들이 다시 뭉친다.
        public string NextAction { get; }

        public bool Retryable { get; }

        public bool HasFailed => Kind != SessionFailureKind.None;

        public static SessionFailure Of(SessionFailureKind kind, string detail = null)
        {
            switch (kind)
            {
                case SessionFailureKind.ServerUnreachable:
                    return new SessionFailure(
                        kind,
                        "서버에 닿지 못했다." + Detail(detail),
                        "주소를 확인하거나 서버를 띄운다. 로컬은 dotnet run --project Api 다.",
                        true);

                case SessionFailureKind.VersionMismatch:
                    return new SessionFailure(
                        kind,
                        "서버와 프로토콜 버전이 다르다." + Detail(detail),
                        "클라이언트를 다시 빌드한다. 재시도해도 결과가 같다.",
                        false);

                case SessionFailureKind.InvalidCode:
                    return new SessionFailure(
                        kind,
                        "초대 코드 형식이 어긋난다. I·L·O·0·1 은 쓰지 않는다." + Detail(detail),
                        "코드를 다시 확인한다.",
                        false);

                case SessionFailureKind.UnknownCode:
                    return new SessionFailure(
                        kind,
                        "그 코드의 방이 없다. 오타이거나 방이 이미 닫혔다.",
                        "코드를 다시 확인하거나 새로 방을 만든다.",
                        false);

                case SessionFailureKind.UnknownMap:
                    return new SessionFailure(
                        kind,
                        "서버에 등록되지 않은 맵이다." + Detail(detail),
                        "서버 설정의 Game:Maps 를 확인한다.",
                        false);

                case SessionFailureKind.RoomFull:
                    return new SessionFailure(
                        kind,
                        "방이 꽉 찼다.",
                        "다른 방에 들어가거나 자리가 나기를 기다린다.",
                        true);

                case SessionFailureKind.RoomInProgress:
                    return new SessionFailure(
                        kind,
                        "이미 시작한 방이다.",
                        "다음 판을 기다린다.",
                        true);

                case SessionFailureKind.TooManyRequests:
                    return new SessionFailure(
                        kind,
                        "요청이 너무 잦아 서버가 거절했다." + Detail(detail),
                        "잠시 기다린 뒤 다시 시도한다.",
                        true);

                case SessionFailureKind.RoomCreateFailed:
                    return new SessionFailure(
                        kind,
                        "서버가 초대 코드를 만들지 못했다." + Detail(detail),
                        "서버 로그를 확인한다. 재시도해도 결과가 같다.",
                        false);

                case SessionFailureKind.HandshakeTimeout:
                    return new SessionFailure(
                        kind,
                        "소켓은 열렸지만 서버가 Welcome 을 보내지 않았다. 정원이 방금 찼을 수 있다.",
                        "다시 시도한다.",
                        true);

                case SessionFailureKind.ConnectionLost:
                    return new SessionFailure(
                        kind,
                        "연결이 끊겼다." + Detail(detail),
                        "자동으로 다시 붙는다. 실패하면 로비로 돌아간다.",
                        true);

                case SessionFailureKind.MapHashMismatch:
                    return new SessionFailure(
                        kind,
                        "서버와 다른 지형에서 시뮬레이션하고 있다." + Detail(detail),
                        "룸의 맵에 맞는 씬을 열고, 씨드를 바꿨다면 맵을 다시 export 한다.",
                        false);

                default:
                    return None;
            }
        }

        private static string Detail(string detail)
        {
            return string.IsNullOrEmpty(detail) ? string.Empty : " (" + detail + ")";
        }
    }
}
