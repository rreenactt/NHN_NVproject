using System.Collections.Generic;
using System.IO;
using NV.Client.Map;
using NV.Client.Net;
using NV.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NV.Client.EditorTools
{
    /// 어떤 굽힌 맵이든 담는 공용 씬을 만든다.
    ///
    /// **Tools ▸ NV ▸ Scene ▸ Create Map Runtime Scene**
    ///
    /// **이 씬이 있는 이유.** 맵마다 씬이 하나씩 필요했고, 그 씬은 Build Settings 에 있어야 했고,
    /// 맵↔씬 짝은 코드에 있었다 — 맵을 하나 늘리는 데 세 곳을 고쳐야 하고 그중 하나는 diff 로
    /// 검토할 수 없는 씬 파일이다. 굽힌 레벨은 프리팹과 에셋이므로 이름으로 찾아 세울 수 있고,
    /// 그러면 씬은 하나로 족하다.
    ///
    /// 씬에는 거의 아무것도 담기지 않는다 — 레벨은 <see cref="MapRuntimeLoader"/> 가 룸의 맵을
    /// 보고 세우고, 몸은 <c>BlockRig</c> 가, 규칙은 <c>MatchBootstrap</c> 이 만든다. 이 프로젝트의
    /// 다른 씬들과 같은 방식이다.
    ///
    /// `SampleScene` 과 `MultiplayerTest` 를 대신하지 않는다. 그 둘은 맵 말고도 다른 것을 담고
    /// 있고(런타임 생성기, 계측 도구) `MapSceneTable` 이 그 짝을 계속 들고 있다.
    public static class MapRuntimeScene
    {
        private const string ScenePath = "Assets/Scenes/MapRuntime.unity";

        [MenuItem("Tools/NV/Scene/Create Map Runtime Scene", priority = 42)]
        public static void Create()
        {
            if (File.Exists(ScenePath)
                && !EditorUtility.DisplayDialog(
                    "NV",
                    $"{ScenePath} 가 이미 있다. 새로 만들어 덮어쓸까?",
                    "덮어쓴다",
                    "취소"))
            {
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLoader();
            BuildPlayer();
            BuildNetwork();
            BuildMatch();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Register();

            Debug.Log(
                $"[NV] {ScenePath} 를 만들고 Build Settings 에 등록했다.\n" +
                "이 씬은 룸의 맵을 보고 그 프리팹을 세운다. 에디터에서 바로 열어 볼 때는 " +
                "Map Runtime 오브젝트의 editorFallbackMapId 에 맵 id 를 적는다.");
        }

        /// 이 씬을 Build Settings 에 넣는다. **한 번만 들어가고 그 뒤로는 맵이 늘어도 늘지 않는다.**
        ///
        /// 등록을 사람에게 맡기지 않는 이유는 `SceneManager.LoadScene` 이 이름으로 찾기 때문이다 —
        /// 빠뜨리면 매치가 시작된 뒤 아무 일도 일어나지 않고, 그 증상은 서버 쪽을 의심하게 만든다.
        /// 메인 로비 씬이 자기 자리(index 0)를 다시 못 박는 것과 같은 판단이다.
        private static void Register()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            for (var index = 0; index < scenes.Count; index++)
            {
                if (scenes[index].path == ScenePath)
                {
                    scenes[index].enabled = true;
                    EditorBuildSettings.scenes = scenes.ToArray();
                    return;
                }
            }

            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void BuildLoader()
        {
            var go = new GameObject("Map Runtime");
            go.AddComponent<MapRuntimeLoader>();
        }

        /// `BlockPlayerSetup` 과 같은 배선이다. 그 메뉴가 열린 씬의 `Player` 를 다시 만들도록
        /// 되어 있어 이름만 맞춰 두고 호출한다.
        private static void BuildPlayer()
        {
            var player = new GameObject("Player");
            BlockPlayerSetup.BuildBlockPlayer();

            // 그 메뉴는 바닥이 없으면 Ground 평면을 하나 만든다. 레벨이 자기 바닥을 세우므로
            // 남겨 두면 스폰이 지형에 파묻힌다.
            var ground = GameObject.Find("Ground");
            if (ground != null) Object.DestroyImmediate(ground);

            if (player != null && GameObject.Find("Player") != player) Object.DestroyImmediate(player);
        }

        private static void BuildNetwork()
        {
            var go = new GameObject("Network");
            var bootstrap = go.AddComponent<NetworkBootstrap>();

            // 새로 붙인 컴포넌트는 이 시점의 기본값을 씬에 직렬화한다. 값을 여기서 명시하지
            // 않으면 `.cs` 의 기본값을 나중에 바꿔도 이 씬은 옛 값을 유지한다.
            bootstrap.localSmoothing = 0.05f;
            bootstrap.localSnapDistance = 2f;
            bootstrap.showOverlay = false;
        }

        /// 규칙 레이어. 나머지는 `MatchBootstrap` 이 런타임에 만든다.
        ///
        /// `autoStart` 는 끈다 — 이 씬은 세션을 지나 열리고, 그때 매치를 시작하는 것은 서버의
        /// 신호다. 켜 두면 로컬 매치가 그 옆에서 하나 더 돈다.
        private static void BuildMatch()
        {
            var go = new GameObject("Match");
            var bootstrap = go.AddComponent<MatchBootstrap>();

            bootstrap.autoStart = false;
            bootstrap.debugKeys = false;
            bootstrap.config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Settings/GameConfig.asset");
        }
    }
}
