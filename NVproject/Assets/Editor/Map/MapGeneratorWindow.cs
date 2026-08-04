using System.Collections.Generic;
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

        /// <summary>
        /// 무엇을 서버로 쓸 것인가. `Check Export` 가 채우고 `Write To Server` 가 그것만 쓴다 —
        /// 검사한 것과 쓰는 것이 같은 물건이어야 "본 대로 쓴다" 가 참이 된다.
        /// </summary>
        private MapExportPlan _exportPlan;

        /// 파이프라인이 물을 수 없는 경고들. 이 창만 아는 것들이다.
        private readonly List<string> _exportNotes = new List<string>();

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
            DrawExport();
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

        // ==================================================== 서버로 보내기

        /// 지금 화면의 설정으로 서버 맵 파일을 만든다.
        ///
        /// **왜 이 창에 있어야 하는가.** `Tools ▸ NV ▸ Map ▸ Export Map Collision` 은 **씬을
        /// 훑어** 레벨을 찾고, 둘 이상이면 (옳게) 거절한다. 그런데 이 도구로 세운 레벨이 서 있는
        /// 씬에는 예전 런타임 생성기도 아직 함께 있는 것이 정상이다 — 갈아타는 중이므로. 그래서
        /// 그 메뉴로는 방금 만든 것을 내보낼 수 없다.
        ///
        /// 판정은 그 메뉴와 **한 글자도 다르지 않다.** 같은 `MapExportPipeline` 을 지나고, 갈리는
        /// 것은 레벨을 씬에서 찾느냐 손에 든 것을 쓰느냐 한 걸음뿐이다.
        private void DrawExport()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("서버로 보내기", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(_preview == null || _preview.Blocker != null))
            {
                if (GUILayout.Button("Check Export (쓰지 않는다)"))
                {
                    CheckExport();
                }
            }

            if (_preview == null)
            {
                EditorGUILayout.HelpBox("먼저 Generate Preview 를 누른다.", MessageType.None);
                return;
            }

            if (_exportPlan == null)
            {
                return;
            }

            DrawExportPlan();
        }

        private void DrawExportPlan()
        {
            if (_exportPlan.PathError != null)
            {
                EditorGUILayout.HelpBox(_exportPlan.PathError, MessageType.Error);
                return;
            }

            EditorGUILayout.SelectableLabel(_exportPlan.OutputPath, EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));

            EditorGUILayout.LabelField("상태", _exportPlan.IsNewFile
                ? "새 파일"
                : _exportPlan.Unchanged ? "지금 파일과 같다 — 쓰지 않는다" : "지금 파일과 다르다 — 덮어쓴다");

            EditorGUILayout.LabelField(_exportPlan.Describe(), EditorStyles.wordWrappedLabel);

            for (var index = 0; index < _exportPlan.Errors.Count; index++)
            {
                EditorGUILayout.HelpBox(_exportPlan.Errors[index], MessageType.Error);
            }

            for (var index = 0; index < _exportPlan.Warnings.Count; index++)
            {
                EditorGUILayout.HelpBox(_exportPlan.Warnings[index], MessageType.Warning);
            }

            for (var index = 0; index < _exportNotes.Count; index++)
            {
                EditorGUILayout.HelpBox(_exportNotes[index], MessageType.Warning);
            }

            if (_exportPlan.RegistrationKnown && !_exportPlan.Registered)
            {
                EditorGUILayout.HelpBox(
                    "이 맵 id 가 서버의 Game:Maps 에 없다. 등록하지 않으면 이 맵으로 방을 만들 수 " +
                    "없고, export 한 사람은 자기 파일이 왜 안 먹는지 알 수 없다.\n\n" +
                    "NVserver/Api/appsettings.json 의 Game:Maps 에 넣는다:\n  " +
                    _exportPlan.RegistrationSnippet,
                    MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(!_exportPlan.CanExport || _exportPlan.Unchanged))
            {
                if (GUILayout.Button("Write To Server"))
                {
                    WriteExport();
                }
            }
        }

        /// 무엇을 쓸지 정하고 **아무것도 쓰지 않는다.**
        private void CheckExport()
        {
            _exportNotes.Clear();
            _exportPlan = null;

            var generator = MapGeneratorRegistry.ForSettings(_settings);
            if (generator == null)
            {
                _status = $"{_settings.GetType().Name} 을 읽는 생성기가 없다.";
                _statusIsError = true;
                return;
            }

            // 임시 소스. 굽지 않고도 무엇이 나갈지 볼 수 있어야 하므로 프로젝트에 아무것도
            // 만들지 않는다 — HideAndDontSave 라 씬이 더러워지지도, 저장되지도 않는다.
            var asset = ScriptableObject.CreateInstance<MapBakedAsset>();
            asset.hideFlags = HideFlags.HideAndDontSave;
            asset.Fill(_preview, generator.DisplayName, string.Empty);

            var host = new GameObject("__NVMapExportProbe") { hideFlags = HideFlags.HideAndDontSave };
            var source = host.AddComponent<BakedMapSource>();
            source.asset = asset;

            try
            {
                _exportPlan = MapExportPipeline.PlanFor(source);
                StampToolProvenance(generator);
                CollectExportNotes();
            }
            finally
            {
                DestroyImmediate(host);
                DestroyImmediate(asset);
            }

            _status = _exportPlan.CanExport
                ? "검사했다. 아직 쓰지 않았다."
                : "지금은 쓸 수 없다. 위의 빨간 줄을 본다.";
            _statusIsError = !_exportPlan.CanExport;
        }

        /// 출처를 이 도구의 것으로 고쳐 적는다. 딸린 값을 함께 고치는 일은 파이프라인이 한다.
        private void StampToolProvenance(IMapGenerator generator)
        {
            MapExportPipeline.Restamp(
                _exportPlan,
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().name,
                "MapGenerator/" + generator.DisplayName);
        }

        /// 파이프라인이 물을 수 없는 것들. **전부 경고이지 관문이 아니다.**
        ///
        /// 파이프라인은 넘겨받은 레벨이 옳은지만 본다. "그 레벨이 이 씬의 레벨이 맞는가" 는
        /// 이 창만 알 수 있는 질문이고, 틀렸을 때의 증상이 특히 조용해서 물어 둘 값이 있다.
        private void CollectExportNotes()
        {
            if (_exportPlan?.Data == null) return;

            var mapName = _exportPlan.Data.Name;
            var expected = NV.Client.Net.MapSceneTable.SceneFor(mapName);
            var active = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().name;

            if (string.IsNullOrEmpty(expected))
            {
                _exportNotes.Add(
                    $"\"{mapName}\" 이 MapSceneTable 에 없다. 파일은 쓸 수 있지만, 로비에서 이 맵으로 " +
                    "방을 만들어도 클라이언트가 어느 씬을 열어야 하는지 모른다.");
            }
            else if (!string.Equals(expected, active, System.StringComparison.Ordinal))
            {
                // 씬 볼륨이 이 판정에 걸린다. `MapExport` 는 열려 있는 씬의 NVCollisionVolume 을
                // 박스 목록에 더하므로, 남의 씬을 열어 놓고 내보내면 그 씬의 프랍이 이 맵에 실린다.
                _exportNotes.Add(
                    $"\"{mapName}\" 의 씬은 {expected} 인데 지금 열린 씬은 " +
                    $"{(string.IsNullOrEmpty(active) ? "(이름 없음)" : active)} 다. " +
                    "export 는 열린 씬의 NVCollisionVolume 을 함께 싣는다 — 지금 쓰면 남의 씬 프랍이 " +
                    "이 맵의 지형이 된다.");
            }

            DescribeBakeMismatch(mapName);
        }

        /// 지금 내보내려는 지형이 구워 둔 에셋과 같은가.
        ///
        /// 다르면 **서버는 지금 화면의 값을 받고 프로젝트에는 그 지형이 없다.** 클라이언트가 여는
        /// 씬은 구운 프리팹이므로 둘이 갈리고, 증상은 접속할 때마다의 맵 해시 불일치 하나다.
        private void DescribeBakeMismatch(string mapName)
        {
            var path = $"{MapBakePipeline.AssetDirectory}/{mapName}.asset";
            var baked = AssetDatabase.LoadAssetAtPath<MapBakedAsset>(path);

            if (baked == null)
            {
                _exportNotes.Add(
                    $"이 맵을 아직 굽지 않았다({path} 가 없다). 파일은 쓸 수 있지만 그 지형을 " +
                    "만드는 것이 프로젝트에 없다 — 먼저 Bake 를 누르는 편이 맞다.");
                return;
            }

            var boxes = new List<Bounds>(_preview.Pieces.Count);
            _preview.CollectCollisionBoxes(boxes);

            if (boxes.Count != baked.Boxes.Count)
            {
                _exportNotes.Add(
                    $"구운 에셋과 지형이 다르다(박스 {baked.Boxes.Count} → {boxes.Count}). " +
                    "설정을 바꾸고 다시 굽지 않았다 — 먼저 Bake 를 누른다.");
                return;
            }

            for (var index = 0; index < boxes.Count; index++)
            {
                if (boxes[index] == baked.Boxes[index]) continue;

                _exportNotes.Add(
                    "구운 에셋과 지형이 다르다(박스 수는 같고 값이 다르다). 설정을 바꾸고 다시 " +
                    "굽지 않았다 — 먼저 Bake 를 누른다.");
                return;
            }
        }

        private void WriteExport()
        {
            if (!MapExportPipeline.TryWrite(_exportPlan, out var message))
            {
                _status = message;
                _statusIsError = true;
                Debug.LogError("[NV] 맵 export 를 하지 않았다. " + message);
                return;
            }

            _status = message;
            _statusIsError = false;
            Debug.Log("[NV] " + message);

            // 쓴 뒤의 계획은 "쓰기 전" 을 말하고 있다. 다시 검사해 상태를 지금 것으로 바꾼다.
            CheckExport();
        }

        /// 미리보기를 버린다. **텍스처까지 버린다** — 그것이 미리보기의 일부다.
        private void ClearPreview()
        {
            _preview = null;

            // 계획은 그 미리보기에 대한 것이었다. 남겨 두면 화면의 값과 다른 지형을 두고
            // "쓰기" 를 누를 수 있게 된다.
            _exportPlan = null;
            _exportNotes.Clear();

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
