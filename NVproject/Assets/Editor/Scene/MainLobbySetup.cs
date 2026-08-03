using System.Collections.Generic;
using System.IO;
using NV.Client.Lobby.Controllers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NV.Client.EditorTools
{
    /// 메인 로비 씬을 만든다.
    ///
    /// 씬에 authoring 하지 않는 이 프로젝트의 규칙을 그대로 따른다 — 화면은 UXML 과
    /// USS 에서 나오고, 씬에는 그것을 띄우는 오브젝트 하나와 카메라만 있다. 그래서 이
    /// 씬은 언제든 지우고 이 메뉴로 다시 만들 수 있다.
    ///
    /// 카메라가 필요한 이유는 UI 가 아니다. 카메라가 없는 씬은 "No cameras rendering"
    /// 경고를 내고 배경이 그려지지 않아, 반투명 패널 뒤로 이전 씬의 잔상이 남는다.
    public static class MainLobbySetup
    {
        /// <summary>제품의 진입 씬. Build Manager 창도 이 값으로 0번 자리를 판정한다.</summary>
        public const string ScenePath = "Assets/Scenes/MainLobby.unity";
        private const string ObjectName = "Main Lobby";

        [MenuItem("Tools/NV/Scene/Create Main Lobby Scene", priority = 40)]
        public static void CreateMainLobbyScene()
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
            go.AddComponent<MainLobbyController>();

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

        /// 빌드 설정에 메인 로비를 **0번으로** 넣는다.
        ///
        /// 0번 씬은 빌드가 처음 여는 씬이다. 메인 로비는 게임을 켜면 처음 만나는 화면이므로
        /// 그 자리가 정의상 이 씬의 자리다.
        ///
        /// 손으로 한 번 올려 두는 것으로 끝내지 않는다. 이 프로젝트의 씬은 언제든 지우고
        /// 이 메뉴로 다시 만들 수 있다는 것이 전제인데, 순서를 사람이 기억해야 하면 다시
        /// 만든 순간 진입 씬이 조용히 `SampleScene` 으로 돌아간다. 그 증상은 빌드를 실행해야
        /// 보이고, 이 저장소에는 그것을 잡아 줄 테스트가 없다.
        ///
        /// 등록 자체도 빠뜨릴 수 없다 — `SessionSceneRouter` 가 씬을 **이름으로** 찾으므로,
        /// 목록에 없으면 에디터 플레이 중에도 매치가 끝난 뒤 로비로 돌아오지 못한다.
        /// <summary>메인 로비를 0번으로 되돌린다. 창의 "고치기" 버튼이 이것을 부른다.</summary>
        ///
        /// 창이 같은 일을 자기 코드로 하지 않게 하려고 공개한다. 두 곳에 두면 한쪽만
        /// 고쳐지고, 그 어긋남은 빌드를 실행해야 보인다.
        public static void EnsureEntryScene()
        {
            AddToBuildSettings();
        }

        private static void AddToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            // 이미 있으면 빼낸다. 그대로 두고 앞에 하나 더 넣으면 같은 씬이 두 번 등록된다.
            for (var index = scenes.Count - 1; index >= 0; index--)
            {
                if (scenes[index].path == ScenePath)
                {
                    scenes.RemoveAt(index);
                }
            }

            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log("[NV] 메인 로비를 빌드 설정 0번(진입 씬)에 넣었다.");
        }
    }
}
