using UnityEditor;
using UnityEngine;

namespace NV.Client.EditorTools
{
    /// 열린 씬의 레벨을 **이 빌드가 그릴 수 있는 맵**으로 등록한다.
    ///
    /// **export 와 짝이다. 하나만 해서는 방을 만들 수 없다.**
    /// `Export Map Collision` 은 지형을 `NVserver/MapData/{map}.json` 에 써서 **서버**에게
    /// 알리고, 이 메뉴는 같은 지형을 `MapCatalog` 에 올려 **클라이언트**에게 알린다. 로비의
    /// 방 만들기 화면은 둘을 합쳐 판정하므로(`MapChoices.Merge`), 서버에만 있는 맵은
    /// `MissingLocally` — "이 빌드에는 이 맵이 없다" — 가 되어 고를 수 없다.
    ///
    /// **왜 Map Generator 로는 안 되는가.** 그 창은 설정 → 생성기 → blueprint 를 지나므로
    /// *생성기가 있는* 맵만 굽는다. `SampleScene` 의 `backrooms` 는 아직 런타임
    /// `BackroomsMapGenerator` 가 씬에서 만드는 레벨이라 blueprint 가 나올 곳이 없고, 그래서
    /// 저장소의 맵 중 그 하나만 카탈로그에 줄이 없었다.
    ///
    /// **씬에 있는 것을 그대로 굳힌다.** 지형을 다시 생성하지 않으므로 "구웠더니 다른 맵이
    /// 나왔다" 가 구조적으로 불가능하다 — 콜리전도 스폰도 격자도 export 가 파일에 쓰는 것과
    /// 같은 호출에서 나온다.
    public static class SceneMapBaker
    {
        [MenuItem("Tools/NV/Map/Bake Current Scene To Catalog", priority = 64)]
        public static void BakeCurrentScene()
        {
            // **판정을 export 에서 빌려 온다.** 재현되지 않는 씨드, 씬에 레벨이 둘, 좌표계가
            // 어긋난 격자 — 파일에 쓰지 못하게 막는 것은 카탈로그에도 올리면 안 되는 것들이다.
            // 검사를 두 벌로 두면 한쪽이 느슨해지고, 느슨한 쪽이 올린 줄은 로비가 그대로 믿는다.
            var plan = MapExportPipeline.Plan();

            if (!plan.CanExport)
            {
                Refuse(Describe(plan));
                return;
            }

            if (Application.isPlaying)
            {
                Debug.LogWarning(
                    "[NV] Play 모드에서 굽는다. 지오메트리를 다시 계산하지 않고 지금 씬에 " +
                    "만들어져 있는 콜리전 목록을 그대로 굳힌다.");
            }

            // **씬 볼륨이 있으면 카탈로그의 해시가 씬에 딸린 값이 된다.** 볼륨은 에셋에 굳지
            // 않고 `MapExport` 가 매번 열린 씬에서 주워 붙이므로(`AppendSceneVolumes`),
            // `MapCatalogWriter` 가 해시를 계산할 때 그 씬이 열려 있어야 같은 값이 나온다.
            // 지금은 맞지만 다른 씬에서 다시 구우면 조용히 갈린다.
            if (plan.Report.SceneVolumes > 0)
            {
                Debug.LogWarning(
                    $"[NV] 이 씬에 `NVCollisionVolume` 이 {plan.Report.SceneVolumes}개 있다. " +
                    "그 박스는 에셋에 굳지 않고 export 시점의 씬에서 온다 — 이 맵을 다시 구울 때도 " +
                    "이 씬을 열고 해야 카탈로그의 해시가 맞는다.");
            }

            for (var index = 0; index < plan.Warnings.Count; index++)
            {
                Debug.LogWarning("[NV] 맵 검사 경고: " + plan.Warnings[index]);
            }

            var result = MapBakePipeline.BakeScene(plan.Source, plan);

            if (!result.Ok)
            {
                Refuse(result.Error);
                return;
            }

            // **해시는 에셋이 아니라 카탈로그에서 읽는다.** 로비가 서버와 대조하는 값이 그것이고,
            // `MapCatalogWriter` 는 굽는 것과 다른 경로(`BakedMapSource` 를 세운 탐침)로 그것을
            // 계산한다 — 여기서 계획의 해시를 그대로 찍으면 실제로 대조될 값이 아니라 이 도구가
            // 바라는 값을 보고하게 된다.
            var entry = MapCatalogWriter.LoadOrCreate().Find(plan.Data.Name);
            var catalogHash = entry == null ? 0u : entry.BakedHash;

            var agrees = catalogHash == plan.Hash;

            var message =
                $"카탈로그에 등록했다: {plan.Data.Name}\n{plan.Describe()}\n" +
                $"에셋: {result.AssetPath}\n" +
                $"카탈로그 해시 {catalogHash:X8} / 이 씬의 지형 {plan.Hash:X8}" +
                (agrees ? " — 일치" : " — **불일치**");

            if (!agrees)
            {
                // 여기까지 왔는데 갈렸다면 카탈로그가 이 씬이 아닌 무언가를 재 두었다는 뜻이다.
                // 조용히 넘기면 로비는 방을 만들 때가 되어서야 "지형이 다르다" 고 말한다.
                Refuse(
                    message + "\n\n에셋은 썼지만 카탈로그의 해시가 이 씬의 지형과 다르다. " +
                    "씬에 레벨이 둘이거나 `NVCollisionVolume` 이 섞여 있는지 본다.");
                return;
            }

            // 성공은 콘솔로만 알린다 — `Export Map Collision` 이 같은 규칙이다. 대화상자는 사람이
            // 눌러야 닫히므로, 잘 된 일에 그것을 띄우면 자동화 경로가 거기서 멈춘다.
            Debug.Log(
                "[NV] " + message +
                "\n서버 쪽은 별개다. 지형을 바꿨다면 Export Map Collision 도 돌린다.");
        }

        /// 이 씬을 카탈로그에 올릴 수 있는가. 메뉴를 회색으로 만들지는 않는다 — 이유를 말할 수
        /// 있어야 하고, 눌리지 않는 메뉴는 이유를 말할 자리가 없다.
        private static string Describe(MapExportPlan plan)
        {
            if (plan.Sources.Count == 0)
            {
                return "씬에서 INetworkMapSource 를 구현한 레벨을 찾지 못했다.\n" +
                       "SampleScene(Backrooms) 이나 MultiplayerTest(테스트 룸) 를 열고 다시 실행한다.";
            }

            if (plan.Sources.Count > 1)
            {
                var text = $"씬에 레벨이 {plan.Sources.Count}개 있다. 어느 것을 구울지 알 수 없다.";

                for (var index = 0; index < plan.Sources.Count; index++)
                {
                    var behaviour = plan.Sources[index] as MonoBehaviour;
                    var where = behaviour == null ? "(MonoBehaviour 가 아니다)" : behaviour.name;

                    text += $"\n  · {where} / {plan.Sources[index].GetType().Name}" +
                            $" → \"{plan.Sources[index].MapName}\"";
                }

                return text;
            }

            if (plan.Blocker != null)
            {
                return "이 레벨은 지금 구울 수 없다.\n\n" + plan.Blocker;
            }

            if (plan.PathError != null)
            {
                return plan.PathError;
            }

            var findings = $"맵 검사에서 {plan.Errors.Count}건이 걸렸다.";

            for (var index = 0; index < plan.Errors.Count; index++)
            {
                findings += "\n  · " + plan.Errors[index];
            }

            return findings;
        }

        private static void Refuse(string message)
        {
            Debug.LogError("[NV] 씬을 카탈로그에 올리지 않았다. " + message);
            EditorUtility.DisplayDialog("NV — 씬 굽기 거절", message, "확인");
        }
    }
}
