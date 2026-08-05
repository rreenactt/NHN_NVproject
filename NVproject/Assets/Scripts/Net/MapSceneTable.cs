namespace NV.Client.Net
{
    /// 맵 이름 ↔ **전용 씬**. 짝이 있는 맵만 여기 있다.
    ///
    /// **모든 맵이 여기 있어야 하는 것은 아니다.** 예전에는 그랬고, 그래서 맵을 하나 늘리는
    /// 일이 씬 하나 + 이 표 한 줄 + Build Settings 한 줄이었다. 이제 굽힌 맵은 프리팹으로
    /// 나오고 공용 런타임 씬(`MapRuntimeLoader`)이 그것을 세우므로, 이 표에 남는 것은
    /// **맵 말고도 다른 것을 담은 씬** 뿐이다 — `SampleScene` 은 아직 런타임 생성기로 지형을
    /// 만들고, `MultiplayerTest` 는 계측 도구를 담고 있다.
    ///
    /// 세 곳이 이것을 읽는다. 세션 라우터(`SessionSceneRouter`)는 룸의 맵을 보고 어느 씬을
    /// 열지 정하고, 배치 export 는 등록된 맵을 전부 내보내려면 어느 씬을 열어야 하는지
    /// 알아야 하며, 베이크(`MapCatalogWriter`)는 구운 맵에 전용 씬이 있는지 이 표에 묻는다.
    ///
    /// **표를 둘로 두면 갈린다.** 그리고 이 표가 갈리는 방식은 특히 조용하다: 라우터가 맵 A 를
    /// 보고 씬 B 를 열면 그 씬은 다른 지형을 만들고, 증상은 접속할 때마다의 맵 해시 불일치
    /// 하나다. 실제로 `MapName` 하나가 이 표와 어긋나 있었고 그 증상만 나타났다.
    ///
    /// 표를 코드에 두는 이유는 여기 남는 것이 **씬** 이기 때문이다. 씬은 프로젝트에 하나씩
    /// 존재하는 물건이고 그 짝을 아는 것은 코드뿐이다 — 반면 맵 자체는 이제 파일을 놓으면
    /// 등록되므로 이 표를 지나지 않는다.
    ///
    /// 맵 이름은 `INetworkMapSource.MapName` 이고, 그것이 곧 export 파일명이며 서버의 맵
    /// id 다. 셋이 같아야 하고, 서버가 기동할 때 파일명과 `name` 이 같은지 검사한다.
    public static class MapSceneTable
    {
        /// 로비 씬. 매치가 끝나거나 나가면 여기로 돌아온다.
        ///
        /// `SceneManager.LoadScene` 이 이름으로 찾으므로 이 씬은 **Build Settings 에 등록되어
        /// 있어야 한다.** 등록을 빠뜨리면 에디터 플레이 중에도 복귀가 실패하고, 증상은 매치가
        /// 끝난 뒤 아무 일도 일어나지 않는 것으로만 나타난다.
        /// `Tools ▸ NV ▸ Scene ▸ Create Main Lobby Scene` 이 등록까지 한다.
        public const string LobbyScene = "MainLobby";

        /// 대기방 씬. 방에 들어가 있고 매치가 아직 시작되지 않았을 때 여기 서 있는다.
        ///
        /// 로비 씬과 갈라 두는 이유는 **화면이 아니라 방**이라는 것이다. 메인 로비는 방 목록과
        /// 메뉴이고, 대기방은 줄에 서 있는 사람들이 보이는 3D 공간이다. 그래서 씬이 둘이다.
        ///
        /// `SceneManager.LoadScene` 이 이름으로 찾으므로 **Build Settings 에 등록되어 있어야
        /// 한다.** 등록을 빠뜨리면 방을 만든 뒤 아무 일도 일어나지 않는다.
        /// `Tools ▸ NV ▸ Scene ▸ Create Game Lobby Scene` 이 등록까지 한다.
        public const string GameLobbyScene = "GameLobby";

        /// 굽힌 맵을 어떤 씬이든 담을 수 있는 공용 씬.
        ///
        /// 전용 씬이 없는 맵이 이것으로 열린다. **Build Settings 에 한 번만** 등록되며,
        /// 그것이 맵을 늘리는 비용에서 씬을 없앤 자리다.
        /// `Tools ▸ NV ▸ Scene ▸ Create Map Runtime Scene` 이 등록까지 한다.
        public const string RuntimeScene = "MapRuntime";

        /// 맵 이름 → 전용 씬. **새 맵은 여기 붙이지 않는다** — 공용 씬이 그것을 담는다.
        private static readonly string[,] Pairs =
        {
            { "backrooms", "SampleScene" },
            { "test-room", "MultiplayerTest" },
        };

        public static int Count => Pairs.GetLength(0);

        public static string MapNameAt(int index) => Pairs[index, 0];

        public static string SceneNameAt(int index) => Pairs[index, 1];

        /// 이 맵의 씬. 표에 없으면 빈 문자열.
        ///
        /// 빈 문자열을 돌려주고 예외를 던지지 않는다 — 서버가 클라이언트가 모르는 맵을 물린
        /// 룸을 열 수 있고, 그때는 "그 맵의 씬을 모른다" 를 사람에게 말해야 한다.
        public static string SceneFor(string mapName)
        {
            if (string.IsNullOrEmpty(mapName))
            {
                return string.Empty;
            }

            for (var index = 0; index < Count; index++)
            {
                if (string.Equals(MapNameAt(index), mapName, System.StringComparison.Ordinal))
                {
                    return SceneNameAt(index);
                }
            }

            return string.Empty;
        }
    }
}
