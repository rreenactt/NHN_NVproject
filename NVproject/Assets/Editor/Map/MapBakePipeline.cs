using System;
using System.Globalization;
using System.IO;
using NV.Client.Map;
using UnityEditor;
using UnityEngine;

namespace NV.Client.EditorTools
{
    /// blueprint 을 프로젝트에 굳힌다. **UI 를 갖지 않는다** — `MapExportPipeline` 과 같은 규칙이다.
    ///
    /// 두 가지가 나온다. `MapBakedAsset` 은 **서버에 무엇을 말할지의 출처**이고, 프리팹은
    /// 그것이 어떻게 보이는지다. 둘이 같은 blueprint 에서 같은 순간에 나오는 것이 요점이다 —
    /// 프리팹을 훑어 콜리전을 되찾는 설계였다면 박스 순서가 계층 구조에 달리고, 그 순서는 맵
    /// 해시에 그대로 들어간다.
    public static class MapBakePipeline
    {
        public const string AssetDirectory = "Assets/Settings/Maps";

        public const string PrefabDirectory = "Assets/Prefabs/Maps";

        /// 구운 결과. 무엇이 어디에 쓰였는지를 사람이 볼 수 있어야 한다.
        public sealed class BakeResult
        {
            public MapBakedAsset Asset;

            public GameObject SceneRoot;

            public string AssetPath;

            public string PrefabPath;

            /// 실패 이유. 없으면 <c>null</c>.
            public string Error;

            public bool Ok => Error == null;
        }

        /// 씬에 세우고 에셋으로 굳힌다. 프리팹까지 쓸지는 호출자가 정한다.
        ///
        /// **재현되지 않는 레벨은 굽지 않는다.** 구운 에셋은 그때부터 서버 판정의 출처가 되므로,
        /// 다음에 같은 설정으로 다시 만들 수 없는 지형을 굳히면 에셋과 생성기가 영구히 갈린다.
        /// <param name="settings">
        /// 로비에 보여 줄 값이 여기 있다. blueprint 에 담지 않는 이유는 그것이 *풀린 지오메트리*
        /// 이기 때문이다 — 표시용 이름을 그쪽에 태우면 생성기마다 읽지도 않는 값을 옮겨 적어야 한다.
        /// </param>
        public static BakeResult Bake(
            MapBlueprint blueprint,
            MapGeneratorSettings settings,
            Generators.IMapGenerator generator,
            bool writePrefab)
        {
            var result = new BakeResult();

            if (blueprint == null)
            {
                result.Error = "blueprint 이 없다.";
                return result;
            }

            if (blueprint.Blocker != null)
            {
                result.Error = "이 설정으로는 구울 수 없다.\n\n" + blueprint.Blocker;
                return result;
            }

            if (string.IsNullOrEmpty(blueprint.MapName))
            {
                result.Error = "맵 이름이 비어 있다.";
                return result;
            }

            Directory.CreateDirectory(AssetDirectory);

            result.AssetPath = $"{AssetDirectory}/{blueprint.MapName}.asset";

            // 있으면 덮어쓴다 — 새로 만들면 이 에셋을 가리키는 프리팹과 씬의 참조가 전부 끊긴다.
            var asset = AssetDatabase.LoadAssetAtPath<MapBakedAsset>(result.AssetPath);
            var created = asset == null;

            if (created)
            {
                asset = ScriptableObject.CreateInstance<MapBakedAsset>();
            }

            asset.Fill(blueprint, settings, generator?.DisplayName ?? "unknown", DateTime.UtcNow.ToString(
                "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

            if (created)
            {
                AssetDatabase.CreateAsset(asset, result.AssetPath);
            }
            else
            {
                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();

            result.Asset = asset;
            result.SceneRoot = MapSceneBuilder.Build(blueprint, asset, generator);

            if (writePrefab)
            {
                result.PrefabPath = WritePrefab(result.SceneRoot, blueprint.MapName);
            }

            // 카탈로그에 등록한다. **이것이 "맵을 늘릴 때 코드를 고치지 않는다" 의 절반이다** —
            // 나머지 절반은 서버가 `MapData/` 를 훑는 것이고, 둘 다 사람이 표를 고치던 자리다.
            MapCatalogWriter.Register(result.Asset, result.PrefabPath);

            return result;
        }

        /// 씬 루트를 프리팹으로 저장하고 씬의 것을 그 인스턴스로 잇는다.
        ///
        /// **잇는 것이 중요하다.** 잇지 않으면 씬에 프리팹과 무관한 사본이 남고, 다음에 프리팹을
        /// 고쳐도 씬은 옛 지형인 채로 export 된다.
        private static string WritePrefab(GameObject root, string mapName)
        {
            if (root == null)
            {
                return null;
            }

            Directory.CreateDirectory(PrefabDirectory);

            var path = $"{PrefabDirectory}/{mapName}.prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, path, InteractionMode.UserAction);

            return path;
        }
    }
}
