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

        /// 씬에 서 있는 레벨이 그 에셋과 같은 지형인가. 같으면 <c>null</c>.
        ///
        /// **왜 이 검사가 필요한가.** 서버가 보는 박스는 `MapBakedAsset` 에서 나오고 씬은 보이는
        /// 쪽일 뿐이다. 그래서 씬에서 벽 하나를 밀면 **클라이언트에는 그 지형이 있고 서버에는
        /// 없는데 맵 해시는 그때도 일치한다** — export 가 이미 알고 있는 실패 모양이고
        /// (`RejectedVolumes`), 여기서는 손으로 고친 뒤 다시 굽지 않는 것이 그 원인이다.
        ///
        /// 자동으로 맞추지 않는다. 손으로 고치는 것을 막을 이유는 없고, **다시 굽지 않은 것만**
        /// 막으면 된다.
        public static string DescribeDrift(GameObject root, MapBakedAsset asset)
        {
            if (root == null || asset == null)
            {
                return null;
            }

            var colliders = root.GetComponentsInChildren<BoxCollider>(true);
            var boxes = asset.Boxes;

            if (colliders.Length != boxes.Count)
            {
                return $"씬의 콜리전이 {colliders.Length}개인데 구운 에셋은 {boxes.Count}개다. " +
                       "씬을 고친 뒤 다시 굽지 않았다 — 지금 export 하면 씬에 보이는 지형이 아니라 " +
                       "에셋의 지형이 서버로 간다.";
            }

            for (var index = 0; index < colliders.Length; index++)
            {
                var transform = colliders[index].transform;

                // 회전한 조각은 AABB 가 같아도 다른 지형이다. 생성기는 아무것도 돌리지 않으므로
                // 돌아 있다는 것 자체가 손댄 표시다.
                if (transform.rotation != Quaternion.identity)
                {
                    return $"씬의 '{colliders[index].name}' 이 회전해 있다. 서버는 축에 정렬된 " +
                           "박스만 알므로 돌린 지형은 서버에 전달되지 않는다.";
                }

                if (Approximately(WorldBox(colliders[index]), boxes[index])) continue;

                return $"씬의 '{colliders[index].name}' 이 구운 에셋의 같은 자리 박스와 다르다. " +
                       "씬을 고친 뒤 다시 굽지 않았다.";
            }

            return null;
        }

        /// 콜라이더가 차지하는 월드 박스를 **트랜스폼에서** 계산한다.
        ///
        /// `Collider.bounds` 를 쓰지 않는다. 그 값은 물리 씬에서 나오고 물리 씬은 트랜스폼 변경을
        /// 그 프레임에 반영하지 않으므로, 방금 만든 오브젝트에 물으면 크기 1 짜리 기본 박스가
        /// 돌아온다 — 실제로 그렇게 나와서 갓 구운 레벨 전체가 "손댔다" 로 보고됐다.
        /// `Physics.SyncTransforms()` 를 부르면 고쳐지지만, 검사 하나를 위해 물리 씬을 건드리는
        /// 것보다 곱셈 두 번이 싸고 확실하다.
        private static Bounds WorldBox(BoxCollider collider)
        {
            var transform = collider.transform;

            return new Bounds(
                transform.TransformPoint(collider.center),
                Vector3.Scale(transform.lossyScale, collider.size));
        }

        /// 박스 두 개가 같은가. 밀리미터면 충분하다 — 사람이 옮긴 벽은 이것보다 훨씬 많이 움직인다.
        private static bool Approximately(Bounds left, Bounds right)
        {
            const float tolerance = 0.001f;

            return (left.min - right.min).sqrMagnitude < tolerance * tolerance
                && (left.max - right.max).sqrMagnitude < tolerance * tolerance;
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
