namespace NV.Client.Net
{
    /// 맵 이름 ↔ 씬 이름. **이 표가 유일한 출처다.**
    ///
    /// 두 곳이 이것을 읽는다. 하나는 세션 라우터(`SessionSceneRouter`) — 룸의 맵을 보고 어느
    /// 씬을 열지 정한다. 다른 하나는 배치 export — 등록된 맵을 전부 내보내려면 어느 씬을
    /// 열어야 하는지 알아야 한다.
    ///
    /// **표를 둘로 두면 갈린다.** 그리고 이 표가 갈리는 방식은 특히 조용하다: 라우터가 맵 A 를
    /// 보고 씬 B 를 열면 그 씬은 다른 지형을 만들고, 증상은 접속할 때마다의 맵 해시 불일치
    /// 하나다. 실제로 `MapName` 하나가 이 표와 어긋나 있었고 그 증상만 나타났다.
    ///
    /// 표를 코드에 두는 이유는 맵을 하나 늘리는 것이 이미 코드(레벨 생성)와 서버 설정을 함께
    /// 건드리는 일이기 때문이다 — 여기에 한 줄이 더 붙는 것이 에셋으로 흩어지는 것보다 낫다.
    ///
    /// 맵 이름은 `INetworkMapSource.MapName` 이고, 그것이 곧 export 파일명이며 서버의
    /// `Game:Maps` 키다. 셋이 같아야 한다.
    public static class MapSceneTable
    {
        /// 로비 씬. 매치가 끝나거나 나가면 여기로 돌아온다.
        ///
        /// `SceneManager.LoadScene` 이 이름으로 찾으므로 이 씬은 **Build Settings 에 등록되어
        /// 있어야 한다.** 등록을 빠뜨리면 에디터 플레이 중에도 복귀가 실패하고, 증상은 매치가
        /// 끝난 뒤 아무 일도 일어나지 않는 것으로만 나타난다.
        /// `Tools ▸ NV ▸ Scene ▸ Create Main Lobby Scene` 이 등록까지 한다.
        public const string LobbyScene = "MainLobby";

        /// 맵 이름 → 씬 이름. 짝을 늘릴 때 여기만 고친다.
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
