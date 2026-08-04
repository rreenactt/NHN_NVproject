namespace NV.Shared.Collision
{
    /// 맵 파일 스키마의 버전.
    ///
    /// **왜 해시로는 대신할 수 없는가.** 맵 해시는 "같은 지형인가" 를 말하고 스키마 버전은
    /// "이 파일을 읽을 수 있는가" 를 말한다. 필드를 늘리면 해시는 그대로이고(새 필드는 해시에
    /// 들어가지 않는다) 옛 서버는 새 파일을 조용히 기본값으로 읽는다 — 증상은 그 기능이 그냥
    /// 안 도는 것이다.
    ///
    /// **`ProtocolInfo.Version` 과 다른 값이다.** 그쪽은 와이어 프로토콜이고 접속 전에
    /// 확인된다. 맵 파일은 저장소에 있고 서버 기동 때 읽힌다. 하나로 묶으면 프로토콜을 고칠
    /// 때마다 모든 맵을 재-export 해야 한다.
    public static class MapSchema
    {
        /// 지금 export 가 쓰는 버전.
        ///
        /// 2 = `meta`(표시용 이름·설명·권장 인원)가 실리기 시작한 버전.
        public const int Current = 2;

        /// 버전 필드가 없는 파일. 버전을 도입하기 전의 파일이 전부 이것이다.
        ///
        /// 이것을 거절하지 않는다. 거절하면 버전을 도입하는 커밋에서 기존 맵 전부를
        /// 재-export 해야 하는데, 그 재-export 는 아무 정보도 늘리지 않는다 — 격자를 해시에
        /// 조건부로 넣은 것과 같은 논리다.
        public const int Unversioned = 0;

        /// 선언된 버전을 실제 버전으로 읽는다. 없으면 1 이다.
        public static int Effective(int declared)
        {
            return declared <= Unversioned ? 1 : declared;
        }

        /// 이 서버가 읽을 수 있는 버전인가.
        ///
        /// **미래 버전은 거절한다.** 모르는 필드를 무시하고 읽으면 그 필드가 필요한 기능이
        /// 조용히 꺼진 채로 돌아가고, 잘못은 한참 뒤 그 기능이 안 되는 것으로만 드러난다.
        public static bool IsReadable(int declared)
        {
            return Effective(declared) <= Current;
        }
    }

    /// 이 맵 파일이 어디서 나왔는가.
    ///
    /// **해시에 들어가지 않는다.** 넣으면 재-export 마다 해시가 바뀌어, 맵 해시가 "클라이언트와
    /// 서버가 같은 지형을 보고 있는가" 를 말하는 기능을 잃는다. 그래서 이 값들은 사람이 읽는
    /// 용도이고 판정에 쓰이지 않는다.
    ///
    /// **씨드를 여기 적지 않는다.** 씨드는 레벨마다 있는 개념이 아니고(테스트 룸에는 없다)
    /// `INetworkMapSource` 에 그것을 위한 멤버를 두면 구현이 하나뿐인 인터페이스 멤버가 된다.
    /// 대신 어느 씬의 어느 컴포넌트였는지를 적는다 — 재현되지 않는 export 는 이제 거절되므로
    /// 씨드는 그 컴포넌트의 직렬화된 필드에 있고, 거기서 읽을 수 있다.
    public sealed class MapSourceInfo
    {
        /// export 를 돌린 씬.
        public string Scene { get; set; }

        /// 콜리전을 내놓은 컴포넌트의 타입 이름.
        public string Component { get; set; }

        /// export 시각(UTC, ISO 8601). 어느 쪽이 최신인지 파일만 보고 알기 위한 값이다.
        public string ExportedAtUtc { get; set; }

        /// export 를 돌린 도구의 버전. 도구가 바뀌어 바이트가 달라졌을 때 그것을 구별한다.
        public int ExporterVersion { get; set; }
    }

    /// 사람에게 보여 줄 값. **판정에 쓰이지 않는다.**
    ///
    /// 이것이 맵 파일 안에 있는 이유는 맵 목록 화면의 출처가 하나여야 하기 때문이다. 예전에는
    /// 표시용 이름과 설명이 **클라이언트 코드의 배열**에 있었고(`CreateRoomPopup`), 서버의
    /// 등록 목록과 손으로 맞춰야 했다 — 어긋나면 고를 수 있는데 만들 수 없는 맵이 된다.
    /// 지형과 같은 파일에 실으면 그 둘이 갈릴 자리가 없다.
    ///
    /// **사이드카 파일(`{name}.meta.json`)로 두지 않는다.** export 는 한 파일을 원자적으로
    /// 쓰므로 파일이 하나면 반쯤 갱신된 상태가 생기지 않는다. 둘이면 한쪽만 오래된 상태가
    /// 생기고, 그것을 감지할 방법이 없다.
    ///
    /// **해시에 들어가지 않는다.** `Source` 와 같은 이유다 — 맵 해시는 "클라이언트와 서버가 같은
    /// 지형을 보고 있는가" 를 답해야 하고 표시용 문장은 지형이 아니다. 그래서 이 블록을 도입하는
    /// 것만으로 기존 맵의 해시가 바뀌지 않고, 재-export 가 강제되지 않는다.
    ///
    /// **없을 수 있다.** 스키마 1 로 만들어진 파일에는 없고, 그것을 거절하지 않는다 — 서버가
    /// 맵 자체에서 합성한다(이름은 맵 id, 크기는 격자). `MapSchema.Unversioned` 를 받아 주는
    /// 것과 같은 논리다.
    public sealed class MapMetaInfo
    {
        /// 사람이 읽는 이름. 비어 있으면 맵 id 로 대신한다.
        public string DisplayName { get; set; }

        /// 한 줄 설명. 비어 있을 수 있다.
        public string Description { get; set; }

        /// 권장 인원. **0 은 "적지 않았다" 이고 서버의 기본값으로 대신한다.**
        ///
        /// 맵이 정원을 정하지 않는다. 정원과 최소 시작 인원은 서버가 판정하는 값이고
        /// (`RealtimeConstants.Rooms`), 맵 파일이 그것을 말하면 클라이언트가 편집할 수 있는
        /// 파일이 규칙을 정하게 된다. 이 값은 화면에 "2–8명" 을 적기 위한 조언이다.
        public int RecommendedPlayersMin { get; set; }

        public int RecommendedPlayersMax { get; set; }

        /// 분류용 꼬리표. `match`, `dev` 같은 것이고 화면이 걸러 보여 줄 때 쓴다.
        public string[] Tags { get; set; }
    }
}
