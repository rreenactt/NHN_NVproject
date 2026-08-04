using System.Collections.Generic;
using NV.Shared.Collision;
using UnityEditor;
using UnityEngine;

namespace NV.Client.EditorTools
{
    /// 맵 export 창.
    ///
    /// **이 창이 있는 이유는 예전 메뉴가 클릭 한 번으로 되돌릴 수 없는 쓰기였기 때문이다.**
    /// 어느 레벨이 뽑혔는지, 어디에 쓰는지, 지금 파일과 무엇이 다른지를 쓴 뒤에야 — 콘솔
    /// 한 줄로 — 알 수 있었다. 잘못된 씬을 열어 놓고 메뉴를 누르면 커밋된 맵이 사라졌고,
    /// 그것을 받쳐 주는 것은 git 뿐이었다.
    ///
    /// 창은 판정을 하지 않는다. `MapExportPipeline` 이 계획을 세우고 창은 그것을 보여 준다.
    public sealed class MapExportWindow : EditorWindow
    {
        private MapExportPlan _plan;
        private Vector2 _scroll;

        private bool _showGrid;
        private int _previewFloor;

        // 미리보기용 선분 묶음. 셀마다 Handles 를 부르면 격자 하나에 수천 번이 되므로
        // 종류별로 한 번씩 그린다.
        private Vector3[] _standableLines;
        private Vector3[] _freeFloorLines;
        private Vector3[] _stairLines;
        private int _builtFloor = -1;

        [MenuItem("Tools/NV/Map/Map Export", priority = 60)]
        public static void Open()
        {
            var window = GetWindow<MapExportWindow>("Map Export");
            window.minSize = new Vector2(460f, 420f);
            window.Refresh();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
        }

        private void Refresh()
        {
            _plan = MapExportPipeline.Plan();
            _builtFloor = -1;
            _previewFloor = 0;
            Repaint();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("다시 읽기", GUILayout.Width(90f)))
                {
                    Refresh();
                }

                GUILayout.Label(
                    Application.isPlaying ? "Play 모드 — 런타임 콜리전 목록을 쓴다" : "Edit 모드",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.Space();

            if (_plan == null)
            {
                EditorGUILayout.HelpBox("아직 읽지 않았다. '다시 읽기' 를 누른다.", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawSources();

            if (_plan.Sources.Count == 1)
            {
                DrawBlocker();
                DrawOutput();
                DrawSummary();
                DrawFindings();
                DrawRegistration();
                DrawPreviewControls();
            }

            EditorGUILayout.EndScrollView();

            DrawActions();
        }

        private void DrawSources()
        {
            EditorGUILayout.LabelField("씬의 레벨", EditorStyles.boldLabel);

            if (_plan.Sources.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "INetworkMapSource 를 구현한 레벨이 없다.\n" +
                    "SampleScene(Backrooms) 이나 MultiplayerTest(테스트 룸) 를 열고 다시 읽는다.",
                    MessageType.Error);
                return;
            }

            for (var index = 0; index < _plan.Sources.Count; index++)
            {
                var source = _plan.Sources[index];
                var behaviour = source as MonoBehaviour;

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        $"{(behaviour == null ? "(MonoBehaviour 아님)" : behaviour.name)} / {source.GetType().Name}",
                        GUILayout.MinWidth(220f));

                    EditorGUILayout.LabelField($"\"{source.MapName}\"", GUILayout.MinWidth(120f));

                    if (behaviour != null && GUILayout.Button("선택", GUILayout.Width(46f)))
                    {
                        Selection.activeGameObject = behaviour.gameObject;
                    }
                }
            }

            if (_plan.Sources.Count <= 1)
            {
                return;
            }

            // **하나를 고르지 않는다.** 씬 스캔 순서는 규정되지 않았으므로 고르는 것은 어느
            // 파일이 쓰일지를 운에 맡기는 일이다. 둘 이상이면 씬이 잘못된 것이다.
            var duplicate = _plan.DuplicateName;

            EditorGUILayout.HelpBox(
                $"레벨이 {_plan.Sources.Count}개다. 어느 것을 export 할지 알 수 없다." +
                (duplicate == null
                    ? "\nexport 하려는 하나만 씬에 남긴다."
                    : $"\n그중 \"{duplicate}\" 이 둘 이상이다 — 같은 파일을 두고 서로 다른 내용을 " +
                      "쓰게 되므로, 한쪽이 격자를 내놓지 않으면 그 차이가 맵 파일에서 사라진 격자로만 나타난다."),
                MessageType.Error);
        }

        private void DrawBlocker()
        {
            if (_plan.Blocker == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "이 레벨은 지금 export 할 수 없다.\n\n" + _plan.Blocker,
                MessageType.Error);
        }

        private void DrawOutput()
        {
            if (_plan.Blocker != null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("출력", EditorStyles.boldLabel);

            if (_plan.PathError != null)
            {
                EditorGUILayout.HelpBox(_plan.PathError, MessageType.Error);
                return;
            }

            EditorGUILayout.SelectableLabel(_plan.OutputPath, EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));

            var state = _plan.IsNewFile
                ? "새 파일"
                : _plan.Unchanged ? "지금 파일과 같다 — 쓰지 않는다" : "지금 파일과 다르다 — 덮어쓴다";

            EditorGUILayout.LabelField("상태", state);
        }

        private void DrawSummary()
        {
            if (_plan.Data == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("내용", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_plan.Describe(), EditorStyles.wordWrappedLabel);
        }

        private void DrawFindings()
        {
            if (_plan.Data == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("검사", EditorStyles.boldLabel);

            if (_plan.Errors.Count == 0 && _plan.Warnings.Count == 0)
            {
                EditorGUILayout.HelpBox("스키마 검사와 시뮬레이션 검산을 통과했다.", MessageType.Info);
            }

            for (var index = 0; index < _plan.Errors.Count; index++)
            {
                EditorGUILayout.HelpBox(_plan.Errors[index], MessageType.Error);
            }

            for (var index = 0; index < _plan.Warnings.Count; index++)
            {
                EditorGUILayout.HelpBox(_plan.Warnings[index], MessageType.Warning);
            }
        }

        private void DrawRegistration()
        {
            if (_plan.Data == null || !_plan.RegistrationKnown || _plan.Registered)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                $"서버 설정(Game:Maps)에 \"{_plan.Data.Name}\" 이 없다. 등록하지 않으면 이 맵으로 " +
                "방을 만들 수 없다 — 등록되지 않은 맵 id 는 기본 맵으로 열리지 않고 거절된다. " +
                "(default 항목이 이 파일을 가리키고 있다면 이 경고는 무시해도 된다.)",
                MessageType.Warning);

            if (GUILayout.Button("appsettings 조각 복사"))
            {
                EditorGUIUtility.systemCopyBuffer = _plan.RegistrationSnippet;
                Debug.Log("[NV] 클립보드에 복사했다: " + _plan.RegistrationSnippet);
            }
        }

        /// 격자 미리보기.
        ///
        /// **좌표계 어긋남은 숫자로 보이지 않는다.** 격자가 반 셀 밀렸거나 축 순서가 뒤바뀌어도
        /// 셀 수는 맞고 해시도 맞는다. 실제 지형 위에 겹쳐 그려 보는 것이 가장 싸다.
        private void DrawPreviewControls()
        {
            if (_plan.Data == null || _plan.Data.Grid == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("격자 미리보기", EditorStyles.boldLabel);

            var grid = _plan.Data.Grid;

            _showGrid = EditorGUILayout.Toggle("씬에 겹쳐 그린다", _showGrid);

            if (!_showGrid)
            {
                return;
            }

            var floor = grid.Floors <= 1
                ? 0
                : EditorGUILayout.IntSlider("층", _previewFloor, 0, grid.Floors - 1);

            if (floor != _previewFloor)
            {
                _previewFloor = floor;
                _builtFloor = -1;
            }

            EditorGUILayout.LabelField(
                "회색 = 격자상 통행 가능, 초록 = 몸이 들어간다, 파랑 = 층을 잇는다",
                EditorStyles.miniLabel);

            SceneView.RepaintAll();
        }

        private void DrawActions()
        {
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_plan == null || _plan.Data == null))
                {
                    if (GUILayout.Button("검사만"))
                    {
                        Refresh();
                        Debug.Log($"[NV] 맵 검사: 오류 {_plan.Errors.Count}건, 경고 {_plan.Warnings.Count}건. " +
                                  _plan.Describe());
                    }
                }

                using (new EditorGUI.DisabledScope(_plan == null || !_plan.CanExport))
                {
                    if (GUILayout.Button("Export"))
                    {
                        Export();
                    }
                }
            }

            if (_plan != null && _plan.Data != null && !_plan.CanExport)
            {
                EditorGUILayout.LabelField(
                    "오류가 남아 있어 Export 를 누를 수 없다. 경고는 막지 않는다.",
                    EditorStyles.miniLabel);
            }
        }

        private void Export()
        {
            if (!MapExportPipeline.TryWrite(_plan, out var message))
            {
                Debug.LogError("[NV] " + message);
                EditorUtility.DisplayDialog("NV — 맵 export 실패", message, "확인");
                return;
            }

            Debug.Log("[NV] " + message);
            EditorUtility.DisplayDialog("NV — 맵 export", message, "확인");

            Refresh();
        }

        // ==================================================== 씬 오버레이

        private void OnSceneGui(SceneView view)
        {
            if (!_showGrid || _plan == null || _plan.Data == null || _plan.Data.Grid == null)
            {
                return;
            }

            if (_builtFloor != _previewFloor)
            {
                BuildPreview(_plan.Data.Grid, _previewFloor);
                _builtFloor = _previewFloor;
            }

            DrawLines(_standableLines, new Color(0.6f, 0.6f, 0.6f, 0.5f));
            DrawLines(_stairLines, new Color(0.35f, 0.6f, 1f, 0.9f));
            DrawLines(_freeFloorLines, new Color(0.3f, 1f, 0.4f, 0.9f));
        }

        private static void DrawLines(Vector3[] lines, Color color)
        {
            if (lines == null || lines.Length == 0)
            {
                return;
            }

            var previous = Handles.color;
            Handles.color = color;
            Handles.DrawLines(lines);
            Handles.color = previous;
        }

        /// 셀을 종류별로 한 묶음의 선분으로 만든다.
        ///
        /// 셀마다 `Handles` 호출을 하면 35×35 격자 하나에 1225번이 되어 씬 뷰가 눈에 보이게
        /// 느려진다. `DrawLines` 는 배열 하나를 한 번에 받으므로 종류마다 한 번이면 된다.
        /// 대신 한 묶음 안에서 색을 바꿀 수 없어 종류별로 배열을 따로 만든다.
        private void BuildPreview(MapGridData grid, int floor)
        {
            var standable = new List<Vector3>();
            var freeFloor = new List<Vector3>();
            var stair = new List<Vector3>();

            var half = grid.CellSize * 0.45f;

            for (var x = 0; x < grid.Width; x++)
            {
                for (var z = 0; z < grid.Depth; z++)
                {
                    var flags = grid.At(floor, x, z);

                    if (flags == MapCellFlags.None)
                    {
                        continue;
                    }

                    var centre = grid.CellToWorld(floor, x, z);

                    // 발밑에서 살짝 띄운다. 바닥면과 같은 높이로 그리면 z-fighting 으로 깜박인다.
                    var unity = new Vector3(centre.X, centre.Y + 0.03f, centre.Z);

                    if ((flags & MapCellFlags.FreeFloor) == MapCellFlags.FreeFloor)
                    {
                        AppendSquare(freeFloor, unity, half);
                    }
                    else if ((flags & MapCellFlags.Standable) == MapCellFlags.Standable)
                    {
                        AppendSquare(standable, unity, half);
                    }

                    if ((flags & MapCellFlags.StairLink) == MapCellFlags.StairLink)
                    {
                        AppendCross(stair, unity, half);
                    }
                }
            }

            _standableLines = standable.ToArray();
            _freeFloorLines = freeFloor.ToArray();
            _stairLines = stair.ToArray();
        }

        private static void AppendSquare(List<Vector3> into, Vector3 centre, float half)
        {
            var a = new Vector3(centre.x - half, centre.y, centre.z - half);
            var b = new Vector3(centre.x + half, centre.y, centre.z - half);
            var c = new Vector3(centre.x + half, centre.y, centre.z + half);
            var d = new Vector3(centre.x - half, centre.y, centre.z + half);

            into.Add(a); into.Add(b);
            into.Add(b); into.Add(c);
            into.Add(c); into.Add(d);
            into.Add(d); into.Add(a);
        }

        private static void AppendCross(List<Vector3> into, Vector3 centre, float half)
        {
            into.Add(new Vector3(centre.x - half, centre.y, centre.z - half));
            into.Add(new Vector3(centre.x + half, centre.y, centre.z + half));
            into.Add(new Vector3(centre.x - half, centre.y, centre.z + half));
            into.Add(new Vector3(centre.x + half, centre.y, centre.z - half));
        }
    }
}
