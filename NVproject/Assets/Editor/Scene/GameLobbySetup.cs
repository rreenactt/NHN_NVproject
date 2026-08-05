using System.Collections.Generic;
using System.IO;
using NV.Client.Lobby.GameLobby;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NV.Client.EditorTools
{
    /// 서버 연동 대기방 씬을 만든다.
    ///
    /// 씬에 authoring 하지 않는 규칙을 그대로 따른다 — 방·스탠드 줄·마네킹·카메라는
    /// `GameLobbyBootstrap` 이 런타임에 세우고, 씬에는 그 오브젝트 하나만 있다. 그래서 이
    /// 씬은 언제든 지우고 이 메뉴로 다시 만들 수 있다.
    ///
    /// **카메라를 넣지 않는다.** 로비 카메라는 `LobbyRoom.Build` 가 방과 함께 만든다 —
    /// 줄의 폭에서 거리와 화각이 나오므로 씬에 미리 놓아 둘 수 없다. 씬을 열어 둔 상태에서는
    /// "No cameras rendering" 이 뜨는데, 플레이를 누르면 그 프레임에 사라진다.
    ///
    /// 옛 `Tools ▸ Backrooms ▸ Create Lobby Scene`(오프라인 프로토타입)과 나란히 존재한다.
    /// 그쪽은 `LobbyManager` 로 혼자 도는 씬이고 이쪽은 서버가 판정하는 씬이며, 연동 확인이
    /// 끝나면 그쪽을 지운다.
    public static class GameLobbySetup
    {
        public const string ScenePath = "Assets/Scenes/GameLobby.unity";
        private const string ObjectName = "Game Lobby";

        [MenuItem("Tools/NV/Scene/Create Game Lobby Scene", priority = 41)]
        public static void CreateGameLobbyScene()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[NV] 플레이 모드를 먼저 나간다 — 플레이 중의 씬 편집은 버려진다.");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject(ObjectName);
            go.AddComponent<GameLobbyBootstrap>();

            var directory = Path.GetDirectoryName(ScenePath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();

            Selection.activeGameObject = go;

            Debug.Log($"[NV] {ScenePath} 를 만들었다. 메인 로비에서 방을 만들거나 참가하면 "
                + "`SessionSceneRouter` 가 이 씬을 연다.");
        }

        /// 빌드 설정에 넣는다. **순서는 상관없다** — 0번은 메인 로비의 자리다.
        ///
        /// 등록 자체는 빠뜨릴 수 없다. `SessionSceneRouter` 가 씬을 **이름으로** 찾으므로,
        /// 목록에 없으면 방을 만든 뒤 아무 일도 일어나지 않는다. 그 증상은 로그 한 줄도 남기지
        /// 않는다 — `LoadScene` 이 없는 씬에 대해 조용히 실패한다.
        private static void AddToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            for (var index = 0; index < scenes.Count; index++)
            {
                if (scenes[index].path == ScenePath)
                {
                    // 이미 있다. 활성 상태만 확인한다 — 꺼져 있으면 없는 것과 같다.
                    scenes[index] = new EditorBuildSettingsScene(ScenePath, true);
                    EditorBuildSettings.scenes = scenes.ToArray();
                    return;
                }
            }

            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log("[NV] 대기방 씬을 빌드 설정에 넣었다.");
        }
    }
}
