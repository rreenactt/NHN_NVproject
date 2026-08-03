using System.Collections.Generic;
using NV.Client.Config;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NV.Client.EditorTools
{
    /// 플랫폼·씬·환경을 골라 누르면 빌드되는 창.
    ///
    /// **이 창은 아무것도 빌드하지 않는다.** 고르기만 하고 <see cref="BuildRunner"/> 를
    /// 부른다. 배치모드에는 `EditorWindow` 를 띄울 수 없으므로, 로직이 여기 있으면
    /// 나중에 CI 경로를 붙이는 것이 원리적으로 불가능해진다.
    ///
    /// **상태를 필드로 들지 않는다.** 스크립트를 고치면 도메인 리로드가 일어나 관리
    /// 객체가 사라지는데 `VisualElement` 는 그중 하나다. 그래서 선택은 `EditorPrefs`
    /// (`BuildSelection`)와 애셋에만 있고, 창은 열릴 때와 바뀔 때 그것을 다시 읽어
    /// 트리를 새로 만든다. `bool built` 같은 플래그를 들면 그 플래그는 리로드를 넘어
    /// 살아남아 전부 null 이 된 요소들을 "만들어져 있다" 고 설명한다 —
    /// `GameHudController.TreeIsLive` 가 있는 이유와 같다.
    ///
    /// UXML 을 쓰지 않고 C# 으로 트리를 만든다. 이 창의 내용은 씬 수·환경 수·진단 수에
    /// 따라 달라져 어차피 코드가 만들고, UXML 을 두면 정적인 틀 하나를 위해 경로로 애셋을
    /// 읽는 실패 경로가 하나 생긴다(폴더 이름을 바꾸면 트리가 null 이 된다). 게임 UI 에
    /// UXML 을 쓰는 규칙은 스타일시트를 사람이 손보는 화면에 대한 것이다.
    public sealed class BuildManagerWindow : EditorWindow
    {
        private static readonly Color WarningColor = new Color(0.95f, 0.75f, 0.2f);
        private static readonly Color OkColor = new Color(0.45f, 0.75f, 0.45f);
        private static readonly Color MutedColor = new Color(0.6f, 0.6f, 0.6f);

        /// 진단은 소켓을 열어 보므로 다시 그릴 때마다 돌리지 않는다.
        private List<BuildDiagnostics.Line> _diagnostics;

        [MenuItem("Tools/NV/Build Manager…", priority = 10)]
        public static void Open()
        {
            var window = GetWindow<BuildManagerWindow>();
            window.titleContent = new GUIContent("NV Build Manager");
            window.minSize = new Vector2(460f, 520f);

            // 이미 열려 있는 창에 `GetWindow` 는 포커스만 준다. 트리는 그대로 남으므로,
            // 그 사이에 선택이나 빌드 설정이 바뀌었다면 낡은 화면을 다시 보여주게 된다.
            window._diagnostics = null;
            window.Rebuild();
        }

        private void CreateGUI()
        {
            Rebuild();
        }

        private void OnEnable()
        {
            NVEnvironmentSelection.Changed += OnSelectionChanged;
        }

        private void OnDisable()
        {
            NVEnvironmentSelection.Changed -= OnSelectionChanged;
        }

        /// 메뉴나 다른 코드가 환경을 바꿨다. 창이 포커스를 갖고 있으면 `OnFocus` 는 오지 않는다.
        private void OnSelectionChanged()
        {
            _diagnostics = null;
            Rebuild();
        }

        private void OnFocus()
        {
            // 다른 창에서 빌드 설정이나 환경 애셋을 고쳤을 수 있다.
            Rebuild();
        }

        private void Rebuild()
        {
            if (rootVisualElement == null)
            {
                return;
            }

            rootVisualElement.Clear();

            var selection = BuildSelection.Load();
            var root = new ScrollView(ScrollViewMode.Vertical);
            root.style.paddingLeft = 10f;
            root.style.paddingRight = 10f;
            root.style.paddingTop = 8f;
            root.style.paddingBottom = 8f;

            root.Add(BuildPlatformSection(selection));
            root.Add(BuildSceneSection());
            root.Add(BuildEnvironmentSection(selection));
            root.Add(BuildOptionSection(selection));
            root.Add(BuildOutputSection(selection));
            root.Add(BuildDiagnosticSection(selection));
            root.Add(BuildButtonRow(selection));

            rootVisualElement.Add(root);
        }

        // ==================================================== 플랫폼

        private VisualElement BuildPlatformSection(BuildSelection selection)
        {
            var section = Section("플랫폼");

            var group = new RadioButtonGroup(null, new List<string> { "Windows 64", "WebGL" })
            {
                value = (int)selection.Platform,
            };

            var note = Note(string.Empty);
            UpdateTargetNote(note, selection);

            group.RegisterValueChangedCallback(changed =>
            {
                selection.Platform = (BuildPlatform)changed.newValue;
                selection.Save();

                UpdateTargetNote(note, selection);
                Rebuild();
            });

            section.Add(group);
            section.Add(note);
            return section;
        }

        /// 플랫폼 전환 비용을 누르기 전에 말한다.
        ///
        /// 전환은 애셋을 전부 다시 임포트해 수 분이 걸리고, 그동안 진행바만 돌아 멈춘 것
        /// 처럼 보인다. 그 상태에서 사람이 Unity 를 강제 종료하는 것이 실제로 일어난다.
        private static void UpdateTargetNote(Label note, BuildSelection selection)
        {
            if (selection.MatchesActiveTarget)
            {
                note.text = "현재 에디터 플랫폼과 일치한다";
                note.style.color = MutedColor;
                return;
            }

            note.text = "⚠ 플랫폼 전환이 필요하다 — 애셋을 다시 임포트하므로 처음에는 수 분이 걸린다";
            note.style.color = WarningColor;
        }

        // ==================================================== 씬

        /// 씬 목록은 `EditorBuildSettings` **그 자체**다.
        ///
        /// 창이 자기 목록을 따로 들지 않는다. 두 벌이 되면 반드시 어긋나고, 그 어긋남은
        /// 빌드를 실행해야 보인다. 여기의 체크박스를 누르는 것이 곧 Build Settings 편집이다.
        private VisualElement BuildSceneSection()
        {
            var section = Section("씬  (Build Settings 를 직접 편집한다)");
            var scenes = EditorBuildSettings.scenes;

            if (scenes.Length == 0)
            {
                section.Add(Line("등록된 씬이 없다", BuildDiagnostics.Level.Warning));
                section.Add(FixEntrySceneButton());
                return section;
            }

            for (var index = 0; index < scenes.Length; index++)
            {
                section.Add(SceneRow(scenes, index));
            }

            var firstEnabled = FirstEnabledScene(scenes);

            if (firstEnabled != MainLobbySetup.ScenePath)
            {
                section.Add(Line("진입 씬(0번)이 MainLobby 가 아니다", BuildDiagnostics.Level.Warning));
                section.Add(FixEntrySceneButton());
            }

            return section;
        }

        private VisualElement SceneRow(EditorBuildSettingsScene[] scenes, int index)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var toggle = new Toggle { value = scenes[index].enabled };
            toggle.style.marginRight = 4f;

            toggle.RegisterValueChangedCallback(changed =>
            {
                var current = EditorBuildSettings.scenes;
                current[index].enabled = changed.newValue;
                EditorBuildSettings.scenes = current;

                Rebuild();
            });

            var label = new Label(index + "  " + System.IO.Path.GetFileNameWithoutExtension(scenes[index].path));
            label.style.flexGrow = 1f;

            if (!scenes[index].enabled)
            {
                label.style.color = MutedColor;
            }

            row.Add(toggle);
            row.Add(label);

            if (scenes[index].path == MainLobbySetup.ScenePath && index == 0)
            {
                var marker = new Label("진입 씬");
                marker.style.color = OkColor;
                row.Add(marker);
            }

            return row;
        }

        private Button FixEntrySceneButton()
        {
            var button = new Button(() =>
            {
                MainLobbySetup.EnsureEntryScene();
                Rebuild();
            })
            {
                text = "MainLobby 를 0번으로 되돌린다",
            };

            button.style.marginTop = 2f;
            return button;
        }

        private static string FirstEnabledScene(EditorBuildSettingsScene[] scenes)
        {
            for (var index = 0; index < scenes.Length; index++)
            {
                if (scenes[index].enabled)
                {
                    return scenes[index].path;
                }
            }

            return string.Empty;
        }

        // ==================================================== 환경

        /// 환경은 고르는 자리에서 값이 함께 보인다.
        ///
        /// 이름만 보이면 `dev` 가 어디를 가리키는지 애셋을 열어야 알 수 있고, 그 한 번의
        /// 확인을 건너뛰는 것이 잘못된 서버로 빌드하는 경로다.
        private VisualElement BuildEnvironmentSection(BuildSelection selection)
        {
            var section = Section("환경");
            var all = NVEnvironmentSelection.All();

            if (all.Count == 0)
            {
                section.Add(Line(
                    NVEnvironment.AssetFolder + " 에 환경이 없다. Assets ▸ Create ▸ NV ▸ Environment",
                    BuildDiagnostics.Level.Warning));

                return section;
            }

            var names = new List<string>(all.Count);
            var active = NVEnvironment.Active;
            var activeIndex = 0;

            for (var index = 0; index < all.Count; index++)
            {
                names.Add(all[index].Id + "  ·  " + all[index].DisplayName);

                if (all[index] == active)
                {
                    activeIndex = index;
                }
            }

            var dropdown = new DropdownField();
            dropdown.choices = names;
            dropdown.index = activeIndex;

            dropdown.RegisterValueChangedCallback(_ =>
            {
                var picked = all[dropdown.index];
                NVEnvironmentSelection.Path = AssetDatabase.GetAssetPath(picked);

                Rebuild();
            });

            section.Add(dropdown);
            section.Add(EnvironmentFields(active));

            if (active.IsInsecureRemote)
            {
                section.Add(Line(
                    "✖ 원격 호스트를 평문으로 가리킨다 — 이 환경으로는 빌드가 거부된다",
                    BuildDiagnostics.Level.Warning));
            }

            return section;
        }

        /// 환경 애셋의 값을 그 자리에서 고친다.
        ///
        /// `SerializedObject` 로 쓴다. 애셋의 값을 코드로 바꿀 때 그것이 유일하게 에디터의
        /// 더티 표시·Undo·저장과 어긋나지 않는 경로다.
        private VisualElement EnvironmentFields(NVEnvironment environment)
        {
            var box = new VisualElement();
            box.style.marginTop = 4f;

            var serialized = new SerializedObject(environment);

            box.Add(EnvironmentField(serialized, "host", "호스트"));
            box.Add(EnvironmentField(serialized, "secure", "보안 (wss / https)"));
            box.Add(EnvironmentField(serialized, "allowHostOverride", "설정에서 주소 변경 허용"));
            box.Add(EnvironmentField(serialized, "allowDebugKeys", "디버그 키 (F1/F2/F5)"));

            box.Bind(serialized);
            return box;
        }

        private VisualElement EnvironmentField(SerializedObject serialized, string propertyName, string label)
        {
            var property = serialized.FindProperty(propertyName);
            var field = new PropertyField(property, label);

            // 값이 바뀌면 이미 읽어 둔 환경을 버려야 한다. 그러지 않으면 창은 새 주소를
            // 보여 주면서 빌드는 옛 주소를 굽는다.
            field.RegisterValueChangeCallback(_ =>
            {
                serialized.ApplyModifiedProperties();
                NVEnvironment.Invalidate();

                _diagnostics = null;
                Rebuild();
            });

            return field;
        }

        // ==================================================== 옵션

        private VisualElement BuildOptionSection(BuildSelection selection)
        {
            var section = Section("옵션");

            var development = new Toggle("개발 빌드 (로그·디버깅)") { value = selection.Development };
            development.RegisterValueChangedCallback(changed =>
            {
                selection.Development = changed.newValue;
                selection.Save();
            });

            var launch = new Toggle("빌드 후 실행") { value = selection.LaunchAfterBuild };
            launch.RegisterValueChangedCallback(changed =>
            {
                selection.LaunchAfterBuild = changed.newValue;
                selection.Save();
            });

            var instances = new IntegerField("인스턴스") { value = selection.InstanceCount };
            instances.RegisterValueChangedCallback(changed =>
            {
                selection.InstanceCount = Mathf.Clamp(changed.newValue, 1, 8);
                selection.Save();

                instances.SetValueWithoutNotify(selection.InstanceCount);
            });

            var width = new IntegerField("창 너비") { value = selection.WindowWidth };
            width.RegisterValueChangedCallback(changed =>
            {
                selection.WindowWidth = Mathf.Max(320, changed.newValue);
                selection.Save();
            });

            var height = new IntegerField("창 높이") { value = selection.WindowHeight };
            height.RegisterValueChangedCallback(changed =>
            {
                selection.WindowHeight = Mathf.Max(240, changed.newValue);
                selection.Save();
            });

            section.Add(development);
            section.Add(launch);
            section.Add(instances);
            section.Add(width);
            section.Add(height);

            if (!selection.CanLaunch)
            {
                section.Add(WebGlCompressionField(selection));
                section.Add(Note("WebGL 빌드물은 브라우저와 정적 서버가 필요하다 — 여기서 띄우지 않는다"));
            }

            return section;
        }

        /// 압축은 WebGL 을 골랐을 때만 보인다. 그리고 이 창에서 가장 오해받기 쉬운 값이다 —
        /// 압축된 빌드는 서버가 `Content-Encoding` 을 붙여 주지 않으면 열리지 않고, 그
        /// 증상(검은 화면)은 빌드나 코드를 의심하게 만든다. 그래서 값 옆에 결과를 적는다.
        private VisualElement WebGlCompressionField(BuildSelection selection)
        {
            var box = new VisualElement();

            var field = new EnumField("WebGL 압축", selection.WebGlCompression);
            var note = Note(string.Empty);

            void Sync()
            {
                note.text = selection.WebGlCompression == WebGLCompressionFormat.Disabled
                    ? "평범한 정적 서버(python -m http.server)로 바로 열린다"
                    : "⚠ 서버가 Content-Encoding 을 붙여야 열린다 — 로컬 확인용이면 Disabled";
            }

            field.RegisterValueChangedCallback(changed =>
            {
                selection.WebGlCompression = (WebGLCompressionFormat)changed.newValue;
                selection.Save();

                Sync();
            });

            Sync();

            box.Add(field);
            box.Add(note);
            return box;
        }

        // ==================================================== 출력과 진단

        private VisualElement BuildOutputSection(BuildSelection selection)
        {
            var section = Section("출력");
            section.Add(Note(selection.OutputPath));

            var exists = selection.CanLaunch && System.IO.File.Exists(selection.OutputPath);
            section.Add(Note(exists ? "지난 빌드물이 있다" : "아직 빌드물이 없다"));

            return section;
        }

        private VisualElement BuildDiagnosticSection(BuildSelection selection)
        {
            var section = Section("진단");

            if (_diagnostics == null)
            {
                _diagnostics = BuildDiagnostics.Collect(selection);
            }

            for (var index = 0; index < _diagnostics.Count; index++)
            {
                section.Add(Line(_diagnostics[index].Text, _diagnostics[index].Level));
            }

            var refresh = new Button(() =>
            {
                _diagnostics = null;
                Rebuild();
            })
            {
                text = "다시 검사",
            };

            refresh.style.marginTop = 2f;
            section.Add(refresh);

            return section;
        }

        private VisualElement BuildButtonRow(BuildSelection selection)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 10f;

            var build = new Button(() =>
            {
                // 진단은 빌드가 바꿀 수 있다 — 빌드물이 생기고 맵 파일 나이가 달라진다.
                _diagnostics = null;

                if (selection.LaunchAfterBuild)
                {
                    BuildRunner.RunAndLaunch(selection);
                }
                else
                {
                    BuildRunner.Run(selection);
                }

                Rebuild();
            })
            {
                text = selection.LaunchAfterBuild ? "빌드하고 실행" : "빌드",
            };

            build.style.flexGrow = 1f;
            build.style.height = 26f;
            row.Add(build);

            if (selection.CanLaunch)
            {
                var launchOnly = new Button(() =>
                {
                    PlayerLaunchService.Launch(selection);
                })
                {
                    text = "실행만",
                };

                launchOnly.style.flexGrow = 1f;
                launchOnly.style.height = 26f;
                row.Add(launchOnly);
            }

            return row;
        }

        // ==================================================== 조각

        private static VisualElement Section(string title)
        {
            var section = new VisualElement();
            section.style.marginBottom = 10f;

            var header = new Label(title);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 2f;

            section.Add(header);
            return section;
        }

        private static Label Note(string text)
        {
            var label = new Label(text);
            label.style.color = MutedColor;
            label.style.whiteSpace = WhiteSpace.Normal;

            return label;
        }

        private static Label Line(string text, BuildDiagnostics.Level level)
        {
            var label = new Label((level == BuildDiagnostics.Level.Ok ? "● " : "▲ ") + text);
            label.style.color = level == BuildDiagnostics.Level.Ok ? OkColor : WarningColor;
            label.style.whiteSpace = WhiteSpace.Normal;

            return label;
        }
    }
}
