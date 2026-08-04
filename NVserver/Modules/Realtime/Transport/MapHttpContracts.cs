namespace NV.Realtime.Transport
{
    /// 맵 하나에 대해 로비가 알아야 하는 것.
    ///
    /// **왜 이 엔드포인트가 필요한가.** 방 만들기 화면의 맵 목록이 클라이언트 코드에 배열로
    /// 박혀 있었고, 서버 `appsettings.json` 과 손으로 맞춰야 했다. 어긋나면 고를 수 있는데
    /// 만들 수 없는 맵이 되고(`400 unknownMap`), 반대로 서버에 있는 맵을 고를 방법이 없었다.
    ///
    /// 판정에 쓰이는 값과 보여 주는 값이 섞여 있고 그것이 맞다. `hash` 와 `supportsMatch` 는
    /// 화면이 방을 만들기 **전에** 실패를 말할 수 있게 하는 값이고, `displayName` 은 사람이
    /// 읽을 값이다. 둘을 다른 엔드포인트로 나누면 화면이 두 응답을 짝지어야 한다.
    internal sealed class MapInfoResponse
    {
        /// 맵 id. **맵 이름과 같고 export 파일명과 같다.** 방을 만들 때 이 값을 보낸다.
        public string Id { get; set; } = string.Empty;

        /// 사람이 읽는 이름. 맵이 그것을 싣지 않으면 `id` 와 같다.
        public string DisplayName { get; set; } = string.Empty;

        /// 한 줄 설명. 없으면 빈 문자열이다.
        public string Description { get; set; } = string.Empty;

        /// 서버가 로드한 지형의 해시.
        ///
        /// **클라이언트가 자기 베이크 해시와 대조한다.** 지금 이 불일치는 접속한 **뒤** 경고
        /// 한 줄로만 드러나고, 그때 사람은 이미 방을 만들었다. 목록에 실리면 그 전에 말할 수 있다.
        public uint Hash { get; set; }

        /// 맵 파일의 스키마 버전. 버전 없는 파일은 1 이다.
        public int SchemaVersion { get; set; }

        /// 맵을 지정하지 않은 요청이 이 맵으로 열리는가. 화면의 기본 선택이 이것이다.
        public bool IsDefault { get; set; }

        /// 이 맵에서 매치가 성립하는가.
        ///
        /// **서버가 판정한다.** 격자가 없거나 몸이 들어가는 셀이 없으면 목표물을 배치할 수 없고
        /// (`Room.BeginMatch` 가 그것을 로그로 남긴다) 열쇠도 문도 생기지 않는다. "격자가
        /// 있는가" 를 내주고 해석을 클라이언트에 맡기면 그 해석이 두 곳에 생긴다.
        public bool SupportsMatch { get; set; }

        public int BoxCount { get; set; }

        public int SpawnCount { get; set; }

        public bool HasGrid { get; set; }

        /// 격자의 크기. 격자가 없으면 전부 0 이다. 화면이 "2층 35×35" 를 여기서 만든다.
        public int Floors { get; set; }

        public int Width { get; set; }

        public int Depth { get; set; }

        public float CellSize { get; set; }

        /// 권장 인원. 맵이 싣지 않으면 서버의 최소 시작 인원과 정원이 들어간다.
        public int RecommendedPlayersMin { get; set; }

        public int RecommendedPlayersMax { get; set; }

        public string[] Tags { get; set; } = System.Array.Empty<string>();
    }
}
