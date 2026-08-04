using System.Collections.Generic;
using System.IO;
using NV.Client.Map;
using UnityEditor;
using UnityEngine;

namespace NV.Client.EditorTools
{
    /// 구운 맵을 `MapCatalog` 에 등록한다. **베이크 파이프라인만 부른다.**
    ///
    /// 이 자리가 없으면 맵을 하나 늘릴 때 사람이 표를 하나 더 고쳐야 하고, 그 표는 낡는다 —
    /// 이 카탈로그가 대신하는 `CreateRoomPopup` 의 맵 배열이 정확히 그 상태였다.
    /// `MapGeneratorRegistry` 가 생성기를 *찾는* 것과 같은 판단이다.
    ///
    /// 카탈로그가 `Resources/` 에 있는 이유는 빌드가 그것을 들고 나가야 하기 때문이다.
    /// 로비는 실행 중에 이 목록으로 "이 빌드가 그릴 수 있는 맵" 을 답한다.
    public static class MapCatalogWriter
    {
        public const string CatalogPath = "Assets/Resources/MapCatalog.asset";

        /// 이 맵의 줄을 갱신한다. 없으면 만든다.
        ///
        /// <param name="prefabPath">프리팹을 쓰지 않았으면 <c>null</c>.</param>
        public static MapCatalog Register(MapBakedAsset asset, string prefabPath)
        {
            if (asset == null || string.IsNullOrEmpty(asset.MapName))
            {
                return null;
            }

            var catalog = LoadOrCreate();
            var entries = new List<MapCatalogEntry>(catalog.Entries);
            var entry = Find(entries, asset.MapName);

            if (entry == null)
            {
                entry = new MapCatalogEntry { mapId = asset.MapName };
                entries.Add(entry);
            }

            entry.asset = asset;
            entry.displayName = asset.DisplayName;
            entry.description = asset.Description;

            // **구운 순간의 해시를 적는다.** 로비가 이것을 서버의 해시와 대조해, 접속한 뒤에야
            // 드러나던 맵 해시 불일치를 방을 만들기 전에 말한다.
            entry.bakedHash = unchecked((int)ComputeHash(asset));

            if (!string.IsNullOrEmpty(prefabPath))
            {
                entry.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            }

            // 이 맵에 전용 씬이 있으면 그것을 적어 둔다.
            //
            // `SampleScene` 과 `MultiplayerTest` 가 그렇다 — 그 씬들은 맵 말고도 다른 것을
            // 담고 있어 공용 런타임 씬으로 대신할 수 없다. 짝의 출처는 `MapSceneTable` 이고,
            // 여기서 베끼는 것이 아니라 **읽는다**: 두 곳에 적으면 갈리고, 그 어긋남은 맵 해시
            // 불일치 하나로만 나타난다.
            var scene = Net.MapSceneTable.SceneFor(asset.MapName);

            if (!string.IsNullOrEmpty(scene))
            {
                entry.sceneOverride = scene;
            }

            // 줄 순서를 맵 id 로 고정한다. 굽는 순서에 따라 에셋의 diff 가 흔들리면 리뷰가
            // 무엇이 바뀌었는지 말해 주지 못한다.
            entries.Sort((left, right) => string.CompareOrdinal(left.mapId, right.mapId));

            catalog.Replace(entries.ToArray());

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            return catalog;
        }

        /// 카탈로그. 없으면 만든다.
        public static MapCatalog LoadOrCreate()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MapCatalog>(CatalogPath);

            if (catalog != null)
            {
                return catalog;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));

            catalog = ScriptableObject.CreateInstance<MapCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            AssetDatabase.SaveAssets();

            return catalog;
        }

        /// 이 에셋이 서버에 말할 지형의 해시.
        ///
        /// **`MapExport` 를 지난다.** 해시를 여기서 다시 계산하면 export 가 쓰는 값과 갈릴 수
        /// 있고, 그러면 로비의 대조가 "서버와 이 빌드" 가 아니라 "서버와 이 함수" 를 비교하게
        /// 된다. 임시 오브젝트를 쓰는 이유는 `MapExport` 가 `INetworkMapSource` 를 받기 때문이다.
        ///
        /// **그 함수는 열려 있는 씬의 `NVCollisionVolume` 도 함께 센다.** 그러므로 이 값은
        /// 씬에 따라 달라질 수 있고, 그것이 맞다 — export 도 같은 것을 세어 파일에 쓰므로,
        /// 굽는 것과 내보내는 것을 같은 씬에서 하는 한 두 값은 같다. 다른 씬에서 구우면
        /// 카탈로그의 해시가 파일과 달라지고, 로비는 그것을 "지형이 다르다" 로 보고한다 —
        /// 조용히 맞춰 주는 것보다 그 편이 낫다.
        private static uint ComputeHash(MapBakedAsset asset)
        {
            var host = new GameObject("__NVMapCatalogProbe") { hideFlags = HideFlags.HideAndDontSave };

            try
            {
                var source = host.AddComponent<BakedMapSource>();
                source.asset = asset;

                return Net.MapExport.BuildMapData(source).ComputeHash();
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static MapCatalogEntry Find(List<MapCatalogEntry> entries, string mapId)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                if (entries[index] != null
                    && string.Equals(entries[index].mapId, mapId, System.StringComparison.Ordinal))
                {
                    return entries[index];
                }
            }

            return null;
        }
    }
}
