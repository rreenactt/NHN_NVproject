using System;
using System.IO;
using System.Text;
using NV.Client.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NV.Client.EditorTools
{
    /// 열린 씬의 레벨 콜리전을 서버가 읽는 JSON 으로 export 한다.
    ///
    /// 서버는 물리 엔진을 쓰지 않고 이 박스 목록으로 이동을 판정한다. 레벨이 코드로
    /// 생성되므로 export 가 유일한 전달 경로다. 씨드나 격자 수치를 바꾸면 다시 돌려야
    /// 하고, 잊으면 접속 직후 콘솔에 맵 해시 불일치가 뜬다.
    ///
    /// 파일명은 맵 이름에서 나온다 — Backrooms 는 `backrooms.json`, 테스트 룸은
    /// `test-room.json`. 서버의 `Game:Maps` 에 그 이름으로 등록되어야 그 맵으로 방을 만들 수
    /// 있다.
    ///
    /// **판정과 쓰기는 `MapExportPipeline` 에 있고 이 파일에는 메뉴만 있다.** 예전에는 한
    /// 함수가 찾기·판정·대화상자·쓰기를 모두 했고, 그 결과 메뉴를 누르는 것이 곧 되돌릴 수
    /// 없는 덮어쓰기였다 — 무엇을 쓸 것인지 쓰지 않고 확인할 방법이 없었다.
    public static class MapCollisionExporter
    {
        /// 예전 메뉴 경로. **창을 연다.**
        ///
        /// 이름을 유지하는 이유는 손이 기억하고 있고 문서와 CLAUDE.md 가 이 경로를 가리키기
        /// 때문이다. 동작은 바뀌었다 — 즉시 쓰지 않고 무엇을 쓸지 보여 준다.
        [MenuItem("Tools/NV/Map/Export Map Collision", priority = 61)]
        public static void OpenWindow()
        {
            MapExportWindow.Open();
        }

        /// 창을 열지 않고 한 번에 돌린다. 자동화와 급할 때를 위한 경로다.
        ///
        /// **거절 조건은 창과 같다.** 같은 `MapExportPipeline.Plan()` 을 지나므로 한쪽만
        /// 느슨해질 수 없다. 다른 것은 사람에게 보여 주는 방식뿐이다.
        [MenuItem("Tools/NV/Map/Export Map Collision (창 없이)", priority = 62)]
        public static void Export()
        {
            var plan = MapExportPipeline.Plan();

            if (!plan.CanExport)
            {
                Refuse(Explain(plan));
                return;
            }

            if (Application.isPlaying)
            {
                Debug.LogWarning(
                    "[NV] Play 모드에서 export 한다. 지오메트리를 다시 계산하지 않고 지금 씬에 " +
                    "만들어져 있는 콜리전 목록을 그대로 쓴다.");
            }

            for (var index = 0; index < plan.Warnings.Count; index++)
            {
                Debug.LogWarning("[NV] 맵 검사 경고: " + plan.Warnings[index]);
            }

            if (!MapExportPipeline.TryWrite(plan, out var message))
            {
                Refuse(message);
                return;
            }

            Debug.Log("[NV] " + message);
        }

        /// 표에 있는 맵을 전부 내보낸다. 씬을 하나씩 열며 돈다.
        ///
        /// **표를 새로 만들지 않는다.** 어느 씬을 열어야 그 맵이 나오는지는 `MapSceneTable` 이
        /// 이미 알고 있고, 세션 라우터가 같은 표를 읽는다. 표가 둘이면 갈리고, 이 표가 갈리는
        /// 방식은 조용하다 — 맵 A 를 보고 씬 B 를 열면 다른 지형이 나오고 증상은 맵 해시 불일치
        /// 하나다.
        ///
        /// Play 중에는 하지 않는다. 씬을 여는 것이 플레이 세션을 끊고, 그 상태에서 무엇이
        /// export 되는지가 명확하지 않다.
        [MenuItem("Tools/NV/Map/Export All Maps", priority = 63)]
        public static void ExportAll()
        {
            if (Application.isPlaying)
            {
                Refuse("Play 중에는 배치 export 를 하지 않는다. 씬을 여는 것이 플레이 세션을 끊는다.");
                return;
            }

            // 열려 있던 씬에 저장하지 않은 변경이 있으면 먼저 묻는다. 묻지 않고 씬을 열면
            // 그 변경이 사라진다.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[NV] 배치 export 를 취소했다.");
                return;
            }

            var opened = SceneManager.GetActiveScene().path;
            var written = 0;
            var failed = 0;
            var report = new StringBuilder();

            for (var index = 0; index < MapSceneTable.Count; index++)
            {
                var mapName = MapSceneTable.MapNameAt(index);
                var sceneName = MapSceneTable.SceneNameAt(index);
                var scenePath = $"Assets/Scenes/{sceneName}.unity";

                if (!File.Exists(scenePath))
                {
                    failed++;
                    report.Append($"\n  ✗ {mapName} — 씬이 없다: {scenePath}");
                    continue;
                }

                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                var plan = MapExportPipeline.Plan();

                if (!plan.CanExport)
                {
                    failed++;
                    report.Append($"\n  ✗ {mapName} ({sceneName}) — {FirstLine(Explain(plan))}");
                    continue;
                }

                if (!string.Equals(plan.Data.Name, mapName, StringComparison.Ordinal))
                {
                    // 표가 말하는 맵과 그 씬이 내놓는 맵이 다르다. 이것을 넘기면 표가 거짓인
                    // 채로 남고, 라우터는 그 표를 믿는다.
                    failed++;
                    report.Append(
                        $"\n  ✗ {mapName} ({sceneName}) — 이 씬의 레벨은 \"{plan.Data.Name}\" 이다. " +
                        "MapSceneTable 과 MapName 이 어긋났다.");
                    continue;
                }

                if (!MapExportPipeline.TryWrite(plan, out var message))
                {
                    failed++;
                    report.Append($"\n  ✗ {mapName} — {FirstLine(message)}");
                    continue;
                }

                written++;
                report.Append($"\n  ✓ {mapName} ({sceneName}) — {plan.Describe()}");

                if (plan.Unchanged)
                {
                    report.Append(" [변경 없음]");
                }
            }

            // 시작한 씬으로 돌아온다. 배치 작업이 사람의 작업 상태를 바꿔 놓지 않는다.
            if (!string.IsNullOrEmpty(opened) && File.Exists(opened))
            {
                EditorSceneManager.OpenScene(opened, OpenSceneMode.Single);
            }

            var summary = $"[NV] 배치 export: 성공 {written}건, 실패 {failed}건.{report}";

            if (failed > 0)
            {
                Debug.LogError(summary);
                EditorUtility.DisplayDialog("NV — 배치 export", summary, "확인");
                return;
            }

            Debug.Log(summary);
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var end = text.IndexOf('\n');
            return end < 0 ? text : text.Substring(0, end);
        }

        /// 왜 쓸 수 없는지를 사람이 읽을 문장으로 만든다.
        private static string Explain(MapExportPlan plan)
        {
            if (plan.Sources.Count == 0)
            {
                return "씬에서 INetworkMapSource 를 구현한 레벨을 찾지 못했다.\n" +
                       "SampleScene(Backrooms) 이나 MultiplayerTest(테스트 룸) 를 열고 다시 실행한다.";
            }

            if (plan.Sources.Count > 1)
            {
                var text = $"씬에 레벨이 {plan.Sources.Count}개 있다. 어느 것을 export 할지 알 수 없다.";

                for (var index = 0; index < plan.Sources.Count; index++)
                {
                    var behaviour = plan.Sources[index] as MonoBehaviour;
                    var where = behaviour == null ? "(MonoBehaviour 가 아니다)" : behaviour.name;

                    text += $"\n  · {where} / {plan.Sources[index].GetType().Name}" +
                            $" → \"{plan.Sources[index].MapName}\"";
                }

                var duplicate = plan.DuplicateName;
                if (duplicate != null)
                {
                    text += $"\n\n그중 \"{duplicate}\" 이 둘 이상이다. 같은 파일을 두고 서로 다른 " +
                            "내용을 쓰게 되므로, export 하려는 하나만 씬에 남긴다.";
                }

                return text;
            }

            if (plan.Blocker != null)
            {
                return "이 레벨은 지금 export 할 수 없다.\n\n" + plan.Blocker;
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
            Debug.LogError("[NV] 맵 export 를 하지 않았다. " + message);
            EditorUtility.DisplayDialog("NV — 맵 export 거절", message + "\n\n파일을 쓰지 않았다.", "확인");
        }
    }
}
