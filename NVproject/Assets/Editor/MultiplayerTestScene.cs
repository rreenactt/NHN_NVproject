using System.IO;
using NV.Client.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NV.Client.EditorTools
{
    /// 멀티플레이 확인용 씬을 만든다.
    ///
    /// **Tools ▸ NV Network ▸ Create Multiplayer Test Scene**
    ///
    /// SampleScene 을 고치지 않고 별도 씬으로 둔다. Backrooms 는 게임이고, 이 씬은
    /// 계측 도구다 — 안개를 걷고, 방을 40m 로 줄이고, 스폰을 링 위에 두어 접속하는 즉시
    /// 서로가 화면에 있게 한다. 미로에서 상대를 찾는 데 드는 1분은 네트워크 확인에
    /// 아무것도 기여하지 않는다.
    ///
    /// 씬에는 그래도 거의 아무것도 담기지 않는다 — 레벨은 <see cref="TestRoomMap"/> 이,
    /// 몸은 <see cref="BlockRig"/> 가 Awake 에서 만든다. 이 스크립트는 컴포넌트를 붙이고
    /// 참조를 잇기만 한다. 프로젝트의 나머지와 같은 방식이다.
    ///
    /// 씬 파일은 저장소에 이미 들어 있다. 이 메뉴는 그것을 처음부터 다시 만드는 경로이며,
    /// 배선을 바꿀 때 손으로 씬을 고치는 대신 여기를 고치고 다시 돌린다.
    public static class MultiplayerTestScene
    {
        private const string ScenePath = "Assets/Scenes/MultiplayerTest.unity";

        [MenuItem("Tools/NV Network/Create Multiplayer Test Scene")]
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

            BuildRoom();
            var player = BuildPlayer();
            BuildNetwork();
            BuildLight();

            // 테스트 룸이 플레이어를 스폰 링에 올린다. 서버에 붙으면 첫 스냅샷이 덮어쓴다.
            var room = Object.FindAnyObjectByType<TestRoomMap>();
            room.player = player.transform;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log(
                $"[NV] {ScenePath} 를 만들었다.\n" +
                "1. Tools ▸ NV Network ▸ Export Map Collision 으로 test-room.json 을 내보낸다.\n" +
                "2. NVserver/Api/appsettings.Development.json 의 Game:MapPath 를 " +
                "../MapData/test-room.json 으로 바꾼다.\n" +
                "3. dotnet run --project Api 로 서버를 띄우고 이 씬에서 Play.\n" +
                "4. 두 번째 클라이언트는 File ▸ Build And Run(WebGL) 또는 두 번째 에디터 인스턴스로 붙인다.");
        }

        private static void BuildRoom()
        {
            var go = new GameObject("Test Room");
            go.AddComponent<TestRoomMap>();
        }

        /// BlockPlayerSetup 과 같은 배선이다. 그 메뉴는 열린 씬의 Player 를 다시 만들도록
        /// 되어 있어 여기서 이름만 맞춰 두고 호출한다.
        private static GameObject BuildPlayer()
        {
            var player = new GameObject("Player");
            BlockPlayerSetup.BuildBlockPlayer();

            // BuildBlockPlayer 는 바닥이 없으면 Ground 평면을 하나 만든다. 테스트 룸이
            // 자기 바닥을 만들므로 그것과 겹치면 스폰이 지형에 파묻힌다.
            var ground = GameObject.Find("Ground");
            if (ground != null) Object.DestroyImmediate(ground);

            return GameObject.Find("Player") ?? player;
        }

        private static void BuildNetwork()
        {
            var go = new GameObject("Network");
            var bootstrap = go.AddComponent<NetworkBootstrap>();

            // 새로 붙인 컴포넌트는 이 시점의 기본값을 씬에 직렬화한다. 값을 여기서
            // 명시해 두지 않으면 .cs 의 기본값을 나중에 바꿔도 이 씬은 옛 값을 유지한다.
            //
            // 서버 주소와 룸은 여기 없다. 접속은 세션(NetSession)이 소유하며 씬보다
            // 오래 살고, 이 컴포넌트는 스냅샷을 몸에 적용하는 일만 한다.
            bootstrap.localSmoothing = 0.05f;
            bootstrap.localSnapDistance = 2f;
            bootstrap.showOverlay = false;

            var ui = go.AddComponent<NetworkTestUi>();
            ui.openOnStart = true;
            ui.togglePanelKey = UnityEngine.InputSystem.Key.Escape;
            ui.panelWidth = 400;
            ui.hudWidth = 320;
        }

        /// BlockPlayerSetup 이 이미 방향광을 하나 만들어 두므로 그것을 다시 쓴다.
        /// 두 개가 되면 이 씬만 유난히 밝아져 다른 씬과 비교가 되지 않는다.
        private static void BuildLight()
        {
            Light light = null;
            foreach (var candidate in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (candidate.type == LightType.Directional) { light = candidate; break; }
            }

            var go = light != null ? light.gameObject : new GameObject("Directional Light");
            if (light == null) light = go.AddComponent<Light>();

            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }
    }
}
