using NV.Client.EditorTools.Generators;
using NV.Client.Map;
using UnityEditor;
using UnityEngine;

namespace NV.Client.EditorTools
{
    /// 맵을 만드는 창.
    ///
    /// **누르는 것이 곧 덮어쓰기가 되지 않는다.** `Generate Preview` 는 아무것도 쓰지 않고
    /// blueprint 만 만들어 숫자와 거절 사유를 보여 준다. 이것은 `MapExportWindow` 가 이미
    /// 지키는 규칙이고, 그 창이 그렇게 된 이유는 예전의 export 메뉴가 판정·대화상자·쓰기를 한
    /// 함수에서 해서 "무엇을 쓸 것인지" 를 쓰지 않고 확인할 방법이 없었기 때문이다.
    ///
    /// IMGUI 다. 옆에 있는 `MapExportWindow` 가 IMGUI 이고, 창 두 개가 서로 다른 UI 체계를
    /// 쓰면 고치는 사람이 배울 것만 늘어난다. (게임 HUD 만 UI Toolkit 인 것은 스타일시트가
    /// 필요해서다.)
    public sealed class MapGeneratorWindow : EditorWindow
    {
        [MenuItem("Tools/NV/Map/Map Generator", priority = 60)]
        public static void Open()
        {
            var window = GetWindow<MapGeneratorWindow>("Map Generator");
            window.minSize = new Vector2(420f, 380f);
            window.Show();
        }

        /// 고른 생성기. **인덱스가 아니라 이름으로 기억한다** — 생성기를 하나 추가하면
        /// 목록이 정렬되며 인덱스가 밀리고, 창은 다른 생성기를 고른 채로 열린다.
        [SerializeField] private string _generatorName;

        /// 지금 만지고 있는 설정. 에셋일 수도, 창이 만든 임시 인스턴스일 수도 있다.
        [SerializeField] private MapGeneratorSettings _settings;

        [SerializeField] private bool _writePrefab = true;

        [SerializeField] private Vector2 _scroll;

        private Editor _settingsEditor;

        private MapBlueprint _preview;

        /// <summary>
        /// The grid picture. Rebuilt with the preview and destroyed with it — a Texture2D created
        /// per repaint would leak one per frame, which is slow to notice and ugly to find.
        /// </summary>
        private Texture2D _previewTexture;

        private string _status;

        private bool _statusIsError;

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawGeneratorPicker();

            if (_settings == null)
            {
                EditorGUILayout.HelpBox("생성기를 고르면 설정이 만들어진다.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawSettings();
            DrawPreview();
            DrawActions();
            DrawStatus();

            EditorGUILayout.EndScrollView();
        }

        // ==================================================== 생성기와 설정

        private void DrawGeneratorPicker()
        {
            EditorGUILayout.LabelField("Generator", EditorStyles.boldLabel);

            var generators = MapGeneratorRegistry.All;

            if (generators.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "IMapGenerator 를 구현한 타입이 없다. Editor/Map/Generators 를 본다.",
                    MessageType.Error);
                return;
            }

            var names = MapGeneratorRegistry.DisplayNames();
            var current = IndexOf(names, _generatorName);

            var picked = EditorGUILayout.Popup("Type", Mathf.Max(0, current), names);

            if (picked != current || _settings == null)
            {
                SelectGenerator(generators[picked]);
            }

            var chosen = EditorGUILayout.ObjectField(
                "Preset", _settings, typeof(MapGeneratorSettings), false) as MapGeneratorSettings;

            if (chosen != _settings && chosen != null)
            {
                AdoptSettings(chosen);
            }

            if (GUILayout.Button("Save Preset As Asset"))
            {
                SavePreset();
            }
        }

        private void SelectGenerator(IMapGenerator generator)
        {
            _generatorName = generator.DisplayName;
            _settings = MapGeneratorRegistry.CreateSettings(generator);
            DiscardEditor();
            ClearPreview();
        }

        /// 고른 에셋에 맞는 생성기로 갈아탄다.
        ///
        /// 설정 에셋이 곧 어느 생성기인지를 말하므로, 사람이 드롭다운과 에셋을 따로 맞출 일이
        /// 없어야 한다 — 어긋나면 Backrooms 설정을 테스트 룸 생성기에 먹이게 되고, 그것은
        /// 예외로만 드러난다.
        private void AdoptSettings(MapGeneratorSettings settings)
        {
            var generator = MapGeneratorRegistry.ForSettings(settings);

            if (generator == null)
            {
                _status = $"{settings.GetType().Name} 을 읽는 생성기가 없다.";
                _statusIsError = true;
                return;
            }

            _settings = settings;
            _generatorName = generator.DisplayName;
            DiscardEditor();
            ClearPreview();
        }

        private void DrawSettings()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);

            if (_settingsEditor == null || _settingsEditor.target != _settings)
            {
                DiscardEditor();
                _settingsEditor = Editor.CreateEditor(_settings);
            }

            EditorGUI.BeginChangeCheck();
            _settingsEditor.OnInspectorGUI();

            if (EditorGUI.EndChangeCheck())
            {
                // 미리보기는 지금 화면의 값에 대한 것이다. 값이 바뀌었는데 옛 숫자를 계속
                // 보여 주면 그것이 곧 거짓말이다.
                ClearPreview();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Roll Seed"))
                {
                    _settings.seed = new System.Random().Next();
                    ClearPreview();
                }
            }
        }

        // ==================================================== 미리보기

        private void DrawPreview()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            if (_preview == null)
            {
                EditorGUILayout.HelpBox("Generate Preview 를 누르면 무엇이 만들어질지 나온다. " +
                                        "씬에는 아무것도 쓰지 않는다.", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField(Describe(_preview));

            DrawGridPreview();

            if (_preview.Blocker != null)
            {
                EditorGUILayout.HelpBox(_preview.Blocker, MessageType.Error);
            }

            var drift = MapSceneBuilder.DescribeDrift(
                GameObject.Find(MapSceneBuilder.RootName), FindSceneAsset());

            if (drift != null)
            {
                EditorGUILayout.HelpBox(drift, MessageType.Error);
            }
        }

        /// 씬에 서 있는 레벨이 물고 있는 에셋. 없으면 <c>null</c>.
        private static MapBakedAsset FindSceneAsset()
        {
            var root = GameObject.Find(MapSceneBuilder.RootName);
            var source = root == null ? null : root.GetComponent<BakedMapSource>();

            return source == null ? null : source.asset;
        }

        /// 격자를 그린다. **씬에 아무것도 만들지 않는다** — 파라미터를 만지는 동안 씬에
        /// 오브젝트 수천 개가 생겼다 지워졌다 하면 도구를 쓸 수 없다.
        ///
        /// 층을 가로로 이어 붙인다. 층마다 창을 하나씩 두는 것보다 두 층의 계단 위치가 맞는지를
        /// 눈으로 확인하기 쉽다 — 그것이 이 레벨에서 가장 자주 어긋나는 곳이다.
        private void DrawGridPreview()
        {
            if (_preview.Grid == null)
            {
                return;
            }

            if (_previewTexture == null)
            {
                _previewTexture = PaintGrid(_preview.Grid);
            }

            var width = Mathf.Min(_previewTexture.width, EditorGUIUtility.currentViewWidth - 40f);
            var height = width * _previewTexture.height / _previewTexture.width;

            var rect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false));
            GUI.DrawTexture(rect, _previewTexture, ScaleMode.ScaleToFit);
        }

        private static Texture2D PaintGrid(NV.Shared.Collision.MapGridData grid)
        {
            const int gap = 4;

            var stride = grid.Width + gap;
            var texture = new Texture2D(stride * grid.Floors, grid.Depth, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                name = "Map Grid Preview",
            };

            var wall = new Color32(22, 20, 15, 255);
            var open = new Color32(126, 113, 70, 255);
            var stair = new Color32(210, 160, 60, 255);

            var pixels = new Color32[texture.width * texture.height];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = wall;

            for (var f = 0; f < grid.Floors; f++)
            for (var x = 0; x < grid.Width; x++)
            for (var z = 0; z < grid.Depth; z++)
            {
                if (!grid.Has(f, x, z, NV.Shared.Collision.MapCellFlags.Standable)) continue;

                // The stairwell in a different colour, because "do the two floors' stairs line up"
                // is the question this preview is actually for.
                var colour = grid.Has(f, x, z, NV.Shared.Collision.MapCellFlags.StairLink) ? stair : open;
                pixels[z * texture.width + f * stride + x] = colour;
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return texture;
        }

        private static string Describe(MapBlueprint blueprint)
        {
            var grid = blueprint.Grid == null
                ? "격자 없음"
                : $"격자 {blueprint.Grid.Floors}층 {blueprint.Grid.Width}×{blueprint.Grid.Depth}";

            return $"조각 {blueprint.Pieces.Count}개 (콜리전 {blueprint.CollidingPieceCount}개), " +
                   $"스폰 {blueprint.Spawns.Count}개, {grid}, 씨드 {blueprint.UsedSeed}";
        }

        // ==================================================== 버튼

        private void DrawActions()
        {
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate Preview"))
                {
                    GeneratePreview();
                }

                using (new EditorGUI.DisabledScope(_preview == null || _preview.Blocker != null))
                {
                    if (GUILayout.Button("Bake"))
                    {
                        Bake();
                    }
                }
            }

            _writePrefab = EditorGUILayout.ToggleLeft(
                "Bake 할 때 프리팹도 쓴다 (Assets/Prefabs/Maps)", _writePrefab);

            EditorGUILayout.HelpBox(
                "Bake 는 콜리전·스폰·격자를 Assets/Settings/Maps 의 에셋에 굳히고 씬에 레벨을 " +
                "세운다. 서버로 보내는 것은 그 다음이다 — Tools ▸ NV ▸ Map ▸ Export Map Collision.",
                MessageType.None);

            if (GUILayout.Button("Clear Level From Scene"))
            {
                MapSceneBuilder.Clear();
                _status = "씬에서 지웠다.";
                _statusIsError = false;
            }
        }

        private void GeneratePreview()
        {
            var generator = MapGeneratorRegistry.ForSettings(_settings);

            if (generator == null)
            {
                _status = $"{_settings.GetType().Name} 을 읽는 생성기가 없다.";
                _statusIsError = true;
                return;
            }

            _preview = generator.Generate(_settings);
            _status = "미리보기를 만들었다. 아무것도 쓰지 않았다.";
            _statusIsError = false;
        }

        private void Bake()
        {
            var generator = MapGeneratorRegistry.ForSettings(_settings);
            var result = MapBakePipeline.Bake(_preview, generator, _writePrefab);

            _statusIsError = !result.Ok;

            if (!result.Ok)
            {
                _status = result.Error;
                Debug.LogError("[NV] 맵을 굽지 않았다. " + result.Error);
                return;
            }

            _status = $"구웠다.\n  {result.AssetPath}" +
                      (result.PrefabPath == null ? string.Empty : $"\n  {result.PrefabPath}");
            Debug.Log("[NV] " + _status);
        }

        private void SavePreset()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "설정 프리셋 저장", _settings.GetType().Name, "asset", string.Empty, "Assets/Settings");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            // 만지고 있던 인스턴스를 그대로 저장한다. 사본을 저장하면 창은 계속 저장되지 않은
            // 쪽을 만지게 되고, 다음 편집이 조용히 사라진다.
            var saved = Instantiate(_settings);
            AssetDatabase.CreateAsset(saved, path);
            AssetDatabase.SaveAssets();

            AdoptSettings(saved);

            _status = "프리셋을 저장했다: " + path;
            _statusIsError = false;
        }

        // ==================================================== 잡동사니

        private void DrawStatus()
        {
            if (string.IsNullOrEmpty(_status))
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(_status, _statusIsError ? MessageType.Error : MessageType.Info);
        }

        private static int IndexOf(string[] names, string name)
        {
            for (var index = 0; index < names.Length; index++)
                if (string.Equals(names[index], name, System.StringComparison.Ordinal)) return index;

            return -1;
        }

        /// 미리보기를 버린다. **텍스처까지 버린다** — 그것이 미리보기의 일부다.
        private void ClearPreview()
        {
            _preview = null;

            if (_previewTexture == null) return;

            DestroyImmediate(_previewTexture);
            _previewTexture = null;
        }

        private void DiscardEditor()
        {
            if (_settingsEditor == null) return;

            DestroyImmediate(_settingsEditor);
            _settingsEditor = null;
        }

        private void OnDisable()
        {
            DiscardEditor();
            ClearPreview();
        }
    }
}
