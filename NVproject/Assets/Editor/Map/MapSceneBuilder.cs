using System.Collections.Generic;
using System.IO;
using NV.Client.Map;
using UnityEditor;
using UnityEngine;

namespace NV.Client.EditorTools
{
    /// blueprint 을 씬에 세운다. **판정하지 않는다** — 무엇을 세울지는 이미 정해져 있다.
    ///
    /// 되돌릴 수 있어야 한다. 레벨 하나가 오브젝트 수천 개일 수 있으므로 등록은 **루트 하나**
    /// 로만 한다. 자식마다 `Undo.RegisterCreatedObjectUndo` 를 부르면 Ctrl+Z 한 번이 벽 하나를
    /// 지우고, 되돌리는 데 수천 번이 필요하다.
    public static class MapSceneBuilder
    {
        /// 구운 머티리얼이 사는 곳. 프리팹에 들어가므로 씬이 아니라 프로젝트에 있어야 한다.
        public const string MaterialDirectory = "Assets/Settings/Maps/Materials";

        /// 이 도구가 만든 레벨의 루트 이름.
        public const string RootName = "__NVMap";

        /// 씬에 레벨을 세우고 그 루트를 돌려준다.
        ///
        /// 같은 이름의 기존 루트는 지운다. 지우지 않으면 다시 생성할 때마다 레벨이 한 벌씩
        /// 쌓인다 — 콜리전이 두 겹이 되고, 증상은 "아무것도 없는 곳에서 막힘" 이다.
        public static GameObject Build(MapBlueprint blueprint, MapBakedAsset asset)
        {
            if (blueprint == null)
            {
                return null;
            }

            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("NV Map Generate");

            Clear();

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "NV Map Generate");

            var source = root.AddComponent<BakedMapSource>();
            source.asset = asset;

            var materials = EnsureMaterials(blueprint);

            for (var index = 0; index < blueprint.Pieces.Count; index++)
            {
                AddPiece(root.transform, blueprint.Pieces[index], materials);
            }

            // 자식은 등록하지 않았지만 루트를 지우는 것으로 전부 사라진다. 한 번의 Ctrl+Z.
            Undo.CollapseUndoOperations(group);

            return root;
        }

        /// 이 도구가 만든 레벨을 씬에서 지운다. 없으면 아무것도 하지 않는다.
        public static void Clear()
        {
            var existing = GameObject.Find(RootName);

            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }
        }

        private static void AddPiece(Transform parent, MapPiece piece, IReadOnlyDictionary<MapSurface, Material> materials)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = piece.Name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = piece.Bounds.center;
            box.transform.localScale = piece.Bounds.size;

            if (!piece.Collides)
            {
                // 콜라이더를 붙인 채 두면 천장 타일과 조명 패널이 총알을 막는다. 그리고 셀마다
                // 하나씩이라 콜라이더 수천 개가 된다.
                Object.DestroyImmediate(box.GetComponent<Collider>());
            }

            if (materials.TryGetValue(piece.Surface, out var material))
            {
                box.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
        }

        /// 표면마다 머티리얼 **에셋**을 마련한다.
        ///
        /// 런타임 생성기처럼 `new Material(...)` 로 만들면 프리팹으로 구울 때 씬에만 있는
        /// 객체를 가리키게 되고, 프리팹은 저장된 순간부터 분홍색이 된다. 에셋이어야 한다.
        ///
        /// 맵 이름으로 파일을 나눈다. 표면 이름만으로 나누면 Backrooms 의 노란 벽과 테스트 룸의
        /// 회색 벽이 같은 파일을 두고 다투고, 나중에 구운 쪽이 먼저 구운 레벨의 색을 바꾼다.
        private static Dictionary<MapSurface, Material> EnsureMaterials(MapBlueprint blueprint)
        {
            var materials = new Dictionary<MapSurface, Material>();

            if (blueprint.Palette.Count == 0)
            {
                return materials;
            }

            Directory.CreateDirectory(MaterialDirectory);

            foreach (var entry in blueprint.Palette)
            {
                var path = $"{MaterialDirectory}/{blueprint.MapName}-{entry.Key}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (material == null)
                {
                    material = new Material(FindShader());
                    AssetDatabase.CreateAsset(material, path);
                }

                material.color = entry.Value;

                // 반사는 없다. 두 레벨 모두 광택이 없는 것이 의도다.
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
                material.enableInstancing = true;

                EditorUtility.SetDirty(material);
                materials[entry.Key] = material;
            }

            AssetDatabase.SaveAssets();

            return materials;
        }

        private static Shader FindShader()
        {
            return Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        }
    }
}
