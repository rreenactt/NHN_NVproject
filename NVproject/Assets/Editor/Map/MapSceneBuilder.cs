using System.Collections.Generic;
using System.IO;
using NV.Client.EditorTools.Generators;
using NV.Client.Map;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NV.Client.EditorTools
{
    /// blueprint 을 씬에 세운다. **판정하지 않는다** — 무엇을 세울지는 이미 정해져 있다.
    ///
    /// 되돌릴 수 있어야 한다. 레벨 하나가 오브젝트 수천 개일 수 있으므로 등록은 **루트 하나**
    /// 로만 한다. 자식마다 `Undo.RegisterCreatedObjectUndo` 를 부르면 Ctrl+Z 한 번이 벽 하나를
    /// 지우고, 되돌리는 데 수천 번이 필요하다.
    public static class MapSceneBuilder
    {
        /// 구운 머티리얼과 병합 메시가 사는 곳. 프리팹에 들어가므로 씬이 아니라 프로젝트에 있어야 한다.
        public const string MaterialDirectory = "Assets/Settings/Maps/Materials";

        public const string MeshDirectory = "Assets/Settings/Maps/Meshes";

        /// 이 도구가 만든 레벨의 루트 이름.
        public const string RootName = "__NVMap";

        /// 씬에 레벨을 세우고 그 루트를 돌려준다.
        ///
        /// 같은 이름의 기존 루트는 지운다. 지우지 않으면 다시 생성할 때마다 레벨이 한 벌씩
        /// 쌓인다 — 콜리전이 두 겹이 되고, 증상은 "아무것도 없는 곳에서 막힘" 이다.
        public static GameObject Build(MapBlueprint blueprint, MapBakedAsset asset, IMapGenerator generator)
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

            var materials = EnsureMaterials(blueprint);

            var source = root.AddComponent<BakedMapSource>();
            source.asset = asset;
            materials.TryGetValue(MapSurface.Wall, out source.wallMaterial);

            BuildSolidPieces(root.transform, blueprint, materials);
            BuildMergedPieces(root.transform, blueprint, materials);

            (generator as IMapSceneDecorator)?.Decorate(root, blueprint);

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

        /// 콜리전을 갖는 조각은 **하나씩 세운다.**
        ///
        /// 합칠 수도 있지만 합치지 않는다. 벽 하나를 손으로 옮기거나 지우는 것이 레벨을 다듬는
        /// 정상 작업이고, 병합 메시에서는 그것이 불가능하다. 개수도 문제가 아니다 — 벽은 이미
        /// 런(run) 단위로 합쳐져 나오므로 2층 35×35 가 736개다.
        private static void BuildSolidPieces(
            Transform parent, MapBlueprint blueprint, IReadOnlyDictionary<MapSurface, Material> materials)
        {
            for (var index = 0; index < blueprint.Pieces.Count; index++)
            {
                var piece = blueprint.Pieces[index];
                if (!piece.Collides) continue;

                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = piece.Name;
                box.transform.SetParent(parent, false);
                box.transform.localPosition = piece.Bounds.center;
                box.transform.localScale = piece.Bounds.size;

                if (materials.TryGetValue(piece.Surface, out var material))
                {
                    box.GetComponent<MeshRenderer>().sharedMaterial = material;
                }
            }
        }

        /// 콜리전이 없는 조각은 **표면마다 메시 하나로 합친다.**
        ///
        /// 합치지 않으면 Backrooms 가 셀마다 천장 타일 하나씩, 2층 35×35 에서 오브젝트 수천 개가
        /// 된다. 프리팹 파일이 수 MB 가 되고, git diff 를 볼 수 없고, YAML 병합이 충돌한다.
        ///
        /// **합쳐도 판정은 바뀌지 않는다.** 서버가 보는 박스는 `MapBakedAsset` 에서 나오고 이
        /// 조각들은 애초에 거기 없다 — 천장 타일과 조명 패널은 아무것도 막지 않는다(막으면 총알이
        /// 천장에 걸린다). 그래서 여기서 잃는 것은 "천장 타일 하나를 따로 고를 수 있음" 뿐이다.
        private static void BuildMergedPieces(
            Transform parent, MapBlueprint blueprint, IReadOnlyDictionary<MapSurface, Material> materials)
        {
            var bySurface = new Dictionary<MapSurface, List<CombineInstance>>();
            var cube = CubeMesh();

            for (var index = 0; index < blueprint.Pieces.Count; index++)
            {
                var piece = blueprint.Pieces[index];
                if (piece.Collides) continue;

                if (!bySurface.TryGetValue(piece.Surface, out var list))
                {
                    list = new List<CombineInstance>();
                    bySurface[piece.Surface] = list;
                }

                list.Add(new CombineInstance
                {
                    mesh = cube,
                    transform = Matrix4x4.TRS(piece.Bounds.center, Quaternion.identity, piece.Bounds.size),
                });
            }

            if (bySurface.Count > 0)
            {
                Directory.CreateDirectory(MeshDirectory);
            }

            foreach (var entry in bySurface)
            {
                var mesh = new Mesh
                {
                    name = $"{blueprint.MapName}-{entry.Key}",

                    // 큐브 하나가 24 버텍스다. 천장 타일 2400장이면 57,600 으로 16비트 인덱스의
                    // 65,535 에 붙고, 조금만 큰 맵이면 넘는다. 넘으면 메시가 조용히 잘린다.
                    indexFormat = IndexFormat.UInt32,
                };

                mesh.CombineMeshes(entry.Value.ToArray(), true, true);
                mesh.RecalculateBounds();

                var path = $"{MeshDirectory}/{blueprint.MapName}-{entry.Key}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

                if (existing == null)
                {
                    AssetDatabase.CreateAsset(mesh, path);
                }
                else
                {
                    // 덮어쓴다. 새로 만들면 이 메시를 가리키던 프리팹의 참조가 끊긴다.
                    EditorUtility.CopySerialized(mesh, existing);
                    Object.DestroyImmediate(mesh);
                    mesh = existing;
                    EditorUtility.SetDirty(mesh);
                }

                var go = new GameObject(entry.Key.ToString());
                go.transform.SetParent(parent, false);

                go.AddComponent<MeshFilter>().sharedMesh = mesh;

                var renderer = go.AddComponent<MeshRenderer>();
                if (materials.TryGetValue(entry.Key, out var material)) renderer.sharedMaterial = material;

                // 천장 타일과 조명 패널은 그림자를 드리우지 않는다 — 아래에서 보이지 않고,
                // 병합된 뒤에는 한 덩어리라 그림자 비용이 통째로 붙는다.
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            AssetDatabase.SaveAssets();
        }

        /// 병합의 재료가 될 큐브 메시. 프리미티브를 하나 만들어 그 메시만 빌린다.
        private static Mesh CubeMesh()
        {
            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = probe.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(probe);

            return mesh;
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

                // 반사는 없다. 광택은 이 레벨들의 평평하고 답답한 느낌을 통째로 깬다.
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
                material.enableInstancing = true;

                if (entry.Key == MapSurface.LightPanel)
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", entry.Value * 2.6f);
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }

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
