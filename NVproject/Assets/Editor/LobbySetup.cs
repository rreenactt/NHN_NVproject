using System.Collections.Generic;
using System.IO;
using NV.Client.Net.Session;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NV.Client.EditorTools
{
    /// 로비 씬을 만든다.
    ///
    /// 씬에 authoring 하지 않는 이 프로젝트의 규칙을 그대로 따른다 — 화면은 UXML 과
    /// USS 에서 나오고, 씬에는 그것을 띄우는 오브젝트 하나와 카메라만 있다.
    ///
    /// 카메라가 필요한 이유는 UI 가 아니다. 카메라가 없는 씬은 "No cameras rendering"
    /// 경고를 내고 배경이 그려지지 않아, 반투명 패널 뒤로 이전 씬의 잔상이 남는다.
    public static class LobbySetup
    {
        private const string ScenePath = "Assets/Scenes/Lobby.unity";
        private const string ObjectName = "Lobby";

        [MenuItem("Tools/NV Network/Create Lobby Scene", priority = 5)]
        public static void CreateLobbyScene()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[NV] 플레이 모드를 먼저 나간다 — 플레이 중의 씬 편집은 버려진다.");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Lobby Camera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;

            // 레벨의 색이다. 순수 검정은 소프트웨어로 읽히고 이 색은 꺼진 형광등으로 읽힌다.
            camera.backgroundColor = new Color(24f / 255f, 21f / 255f, 14f / 255f, 1f);
            camera.orthographic = true;
            camera.cullingMask = 0;

            var go = new GameObject(ObjectName);
            go.AddComponent<LobbyController>();

            var directory = Path.GetDirectoryName(ScenePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();

            Selection.activeGameObject = go;

            Debug.Log($"[NV] {ScenePath} 를 만들었다. 서버를 띄우고 이 씬에서 플레이한다. "
                + "게임 씬으로는 룸의 맵에 따라 자동으로 넘어간다.");
        }

        /// 빌드 설정에 로비를 넣는다.
        ///
        /// 첫 번째로 넣지 않는다. 0번 씬은 빌드가 처음 여는 씬이고, 그것을 바꾸면
        /// 개발용 2클라이언트 빌드가 기대하는 진입점이 조용히 달라진다. 배포용으로
        /// 로비를 진입점으로 삼는 것은 사람이 정할 일이다.
        private static void AddToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            for (var index = 0; index < scenes.Count; index++)
            {
                if (scenes[index].path == ScenePath)
                {
                    return;
                }
            }

            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log("[NV] 로비를 빌드 설정에 추가했다. 배포에서 로비를 진입점으로 쓰려면 "
                + "Build Settings 에서 순서를 직접 올린다.");
        }
    }
}
