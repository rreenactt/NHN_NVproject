# Build Manager 개선 계획

**창 하나에서 플랫폼(Windows/WebGL)·씬·환경을 시각적으로 골라 누르면 빌드되는 도구.** 그 이상은 만들지 않는다. 지금 `Tools ▸ NV Network` 에 흩어져 있는 초기 테스트용 빌드 코드는 그 과정에서 정리한다.

기준 문서는 `NVproject/CLAUDE.md`, 저장소 루트 `CLAUDE.md` 의 커맨드 표, `NVserver/docs/readme.md`. 이 파일은 임시 작업 산출물이다. 실행 중 계획이 바뀌면 이 파일을 고친다.

---

## 1. 지금 상태 — 셋 다 고를 수 없다

`Assets/Editor/` 7개 파일 중 빌드를 하는 것은 `TestClientBuild.cs` 하나이고, 세 축이 전부 코드에 박혀 있다.

| 축 | 지금 | 근거 |
|---|---|---|
| **플랫폼** | `StandaloneWindows64` 고정. **WebGL 빌드 경로가 저장소에 한 줄도 없다** | `TestClientBuild.cs:109`. 전송 계층은 이미 갈라져 있다(`Assets/Plugins/WebGL`, `WebGlWebSocketTransport.cs`) |
| **씬** | Build Settings 를 그대로 읽는다 — **이 축만 이미 옳다** | `TestClientBuild.cs:145` `ScenesToBuild()`. 다만 창에서 볼 수도, 고를 수도 없다 |
| **환경** | `localhost:5202` 평문 고정. 바꾸려면 **상수를 고쳐 재컴파일** | `LobbyService.cs:95` → `PlayerProfile.cs:32` → `PlayerProfile.cs:43` `const DefaultHost = "localhost:5202"` |

환경 축에 세 겹의 함정이 더 있다. 이게 이 계획에서 가장 손이 많이 가는 부분이다.

1. **접속 대상이 세 곳에 산다.** `PlayerProfile.DefaultHost` 상수, **씬에 직렬화된 `NetSession.host`/`secure`**(`NetSession.cs:28`), `PlayerPrefs`. 이 프로젝트의 규칙대로 `.cs` 기본값을 고쳐도 저장된 씬은 옛 값을 유지하므로 셋이 조용히 어긋난다.
2. **`PlayerPrefs` 는 빌드를 갈아도 기기에 남는다.** 로컬 서버에 한 번 붙어 본 기기에 배포 빌드를 깔면 `nv.lobby.host` 에 `localhost:5202` 가 남아 **배포 빌드가 로컬을 가리킨다.** 증상은 "서버 응답 없음" 이고 단서가 없다.
3. **`secure` 를 잊은 WebGL 배포는 아예 접속하지 못한다.** HTTPS 페이지의 `ws://` 는 mixed content 로 차단된다(`ClientTransportFactory.cs:24` 의 주석이 이미 경고한다). 로컬에서 재현되지 않는 종류의 실패다.

그 외: 출력 경로·exe 이름·`Development|AllowDebugging`·인스턴스 2개·1280×720 이 전부 `const` 이고, **맵 콜리전 export 를 잊은 빌드는 조용히 망가진다**(컴파일·실행 다 되고 접속 후 맵 해시 불일치만 남는다).

---

## 2. 현재 코드 처분

| 파일 | 메뉴 | 처분 |
|---|---|---|
| `TestClientBuild.cs` | `Build Test Client (Windows)` / `Launch Test Client` / `Build and Launch 2 Clients` | **삭제.** 기능은 창 + `PlayerLaunchService` 로 이관 |
| `NetworkSetup.cs` | `Setup Networking` / `Remove Networking` | **삭제.** 씬 생성기가 `NetworkBootstrap` 을 직접 붙이고 `MainLobby` 는 쓰지 않는다. "연동 스위치" 는 접속이 세션으로 옮겨간 시점에 의미를 잃었다 |
| `MapCollisionExporter.cs` | `Export Map Collision` | **유지 + 함수 분리.** 창이 빌드 전에 호출할 수 있게 메뉴와 순수 export 함수를 나눈다 |
| `MainLobbySetup.cs` | `Create Main Lobby Scene` | **유지.** 씬 0번 고정 규칙을 창이 읽어 표시한다 |
| `MultiplayerTestScene.cs` | `Create Multiplayer Test Scene` | **유지, 메뉴만 이동** |
| `BlockPlayerSetup.cs`, `MatchSetup.cs` | `Block Player/*`, `Backrooms/*` | 관여하지 않음 |

`TestClientBuild.cs` 주석의 세 함정(Run In Background, Fullscreen, `forceSingleInstance`)과 "씬 목록은 Build Settings 를 그대로 따른다"는 규칙은 **주석째로 새 코드에 옮긴다.** 옮긴 이유를 잃으면 다음 사람이 되돌린다.

`[Obsolete]` 유예는 두지 않는다. 외부 사용자가 없고, 남겨 둔 메뉴는 어느 쪽이 진짜인지 알려주지 않는다.

---

## 3. 창

메뉴 하나 — **`Tools ▸ NV ▸ Build Manager…`**. UI Toolkit 으로 만든다(이 프로젝트의 UI 는 전부 UXML+USS 이고, 에디터 창에 IMGUI 를 쓰면 스타일이 두 벌 생긴다).

```
┌ NV Build Manager ──────────────────────────────────┐
│ 플랫폼    ( ● ) Windows 64      (   ) WebGL         │
│           현재 플랫폼과 일치                        │
├────────────────────────────────────────────────────┤
│ 씬        ✔ 0  MainLobby           ← 진입 씬        │
│           ✔ 1  MultiplayerTest                     │
│           ✔ 2  SampleScene                         │
│           ⚠ 0번이 MainLobby 가 아니다  [ 고치기 ]   │
├────────────────────────────────────────────────────┤
│ 환경      [ Local ▾ ]                              │
│           호스트  [ localhost:5202          ]      │
│           보안    ☐ wss / https                    │
│           ☐ 서버 주소 변경 허용 (로비 설정)         │
│           ☐ 디버그 키 (F1/F2/F5)                   │
├────────────────────────────────────────────────────┤
│ 옵션      ✔ 개발 빌드 (로그·디버깅)                 │
│           ✔ 빌드 후 실행   인스턴스 [2]  1280×720   │
├────────────────────────────────────────────────────┤
│ 출력      Builds/local/Windows64/NVClient.exe       │
│ 진단      ● 서버 응답 없음 (localhost:5202)         │
│           ● test-room.json 이 씬보다 오래됐다        │
├────────────────────────────────────────────────────┤
│                     [ 빌드 ]   [ 실행만 ]           │
└────────────────────────────────────────────────────┘
```

네 가지가 이 레이아웃의 의도다.

**씬 목록은 `EditorBuildSettings.scenes` 를 직접 편집한다.** 창이 자기 씬 목록을 따로 들지 않는다 — 목록을 두 벌로 두면 반드시 어긋나고, 그 어긋남은 빌드를 실행해야 보인다. 체크박스와 순서 변경이 곧 Build Settings 편집이고, 0번이 `MainLobby` 가 아니면 경고와 함께 한 번에 고치는 버튼을 둔다(`MainLobbySetup` 이 이미 하는 일).

**환경은 드롭다운으로 고르고 그 자리에서 값이 보인다.** 이름만 보이면 `Dev` 가 어디를 가리키는지 애셋을 열어야 알고, 그 한 번의 확인을 건너뛰는 것이 잘못된 서버로 빌드하는 경로다. 호스트·보안 필드는 인라인 편집 가능하며, 편집은 선택된 환경 애셋에 저장된다.

**진단 두 줄이 창의 실질적 값이다.** 지금 두 클라이언트가 안 붙는 원인 대부분이 (a) 서버가 안 떠 있음 (b) 맵 export 안 함 (c) 씬 0번 밀림 이고, 셋 다 화면에 단서를 남기지 않고 네트워크 결함처럼 보인다.

**한 가지만 빌드를 막는다: 보안이 꺼진 채로 원격 호스트를 가리키는 조합.** `localhost` 가 아닌 호스트 + `secure` 꺼짐 = 접속이 원리적으로 불가능한 빌드다. 다른 조합은 경고만 하고 통과시킨다 — 도구가 사람을 가로막기 시작하면 사람이 도구를 우회한다.

---

## 4. 환경 값이 런타임에 닿는 경로

이게 계획에서 가장 중요한 부분이다. **애셋만 만들어도 아무것도 달라지지 않는다.** `PlayerProfile.DefaultHost` 상수를 없애고 결정 순서를 하나로 고정한다.

```
1. URL 쿼리 (WebGL 초대 링크)          이미 InviteLink 가 코드·이름에 쓰는 경로
2. PlayerPrefs — 저장된 환경 id 가 지금 빌드의 id 와 같을 때만
3. 빌드에 구워진 NVEnvironment          ← 새로 생기는 층. 여기가 기본값이다
4. 없음 → 로컬 폴백 + 경고 로그          에디터에서만 도달 가능
```

- **2번의 "같은 환경일 때만" 조건이 §1 의 PlayerPrefs 누출을 막는다.** `PlayerPrefs` 키를 `nv.lobby.host` 에서 `nv.{envId}.lobby.host` 로 바꾸는 것으로 끝난다. 환경마다 다른 서랍을 쓰면 다른 환경의 값이 새어 나올 수 없다.
- **구워 넣는 방법은 `Resources/` 의 ScriptableObject 하나.** 빌드 직전에 선택된 환경을 `Assets/Resources/NVEnvironment.asset` 으로 복사하고 런타임은 `Resources.Load` 로 읽는다. `StreamingAssets` JSON 은 WebGL 에서 비동기 읽기가 되어 부팅 순서를 건드리므로 쓰지 않는다 — 이 프로젝트가 `Resources/UI/` 를 쓰는 것과 같은 이유다.
- **`NVEnvironment.cs` 는 `Assets/Editor/` 밖에 둔다.** 런타임이 읽는 타입이므로 에디터 어셈블리에 있으면 빌드에서 사라진다.
- **`NetSession.host` / `secure` 직렬화 필드는 없앤다.** 씬이 접속 대상을 들고 있는 한 씬 파일과 환경 애셋이 어긋날 수 있고, 그 어긋남은 실행해야 보인다. 세션은 부팅 시 결정 순서를 한 번 물어 값을 받는다.
- `Resources/NVEnvironment.asset` 은 **커밋하지 않는다**(`.gitignore` 추가). 빌드 산출물이고, 커밋되면 마지막으로 빌드한 사람의 환경이 남의 에디터 기본값이 된다. 에디터에서는 3번 대신 창이 `EditorPrefs` 에 남긴 선택을 읽는다.

### `NVEnvironment` 필드 — 창에 보이는 것이 전부다

| 필드 | 용도 |
|---|---|
| `id` | `local` / `dev` / `prod`. 출력 경로와 `PlayerPrefs` 키의 접두어 |
| `displayName` | 드롭다운에 보이는 이름 |
| `host` | `host:port` |
| `secure` | `wss` / `https` |
| `allowHostOverride` | 로비 설정 팝업의 서버 주소 입력칸을 켜고 끈다 (`SettingsPopup.cs:34`) |
| `allowDebugKeys` | `GameConfig.debugKeys`(F1/F2/F5)를 빌드 시점에 끈다 |

필드를 여기서 멈춘다. 로그 레벨·환경 배너·`expectsRoomListing` 같은 것은 지금 필요하지 않고, 필요해지면 필드 하나와 창의 한 줄을 더하는 일이다. 애셋은 `Assets/Settings/Environments/{id}.asset` 에 두고 **커밋한다.**

기본 환경은 **`local` 과 `dev` 둘로 시작한다.** `staging`/`prod` 의 실제 호스트가 아직 없으므로, 축과 결정 순서만 먼저 세우고 환경 추가는 애셋 하나 만드는 일로 남긴다.

---

## 5. 코드 구조

파일 9개. 이 이상 쪼개면 도구보다 구조가 커진다.

```
Assets/Editor/BuildManager/          ← 폴더 이름이 Build 가 아닌 이유는 아래
  NVEnvironmentSelection.cs  환경 선택 저장소(EditorPrefs) + 전환 메뉴
  BuildManagerWindow.cs      EditorWindow — 선택만 한다. 빌드 로직 없음
  BuildSelection.cs          창의 선택 상태. EditorPrefs 로 읽고 쓴다
  BuildRunner.cs             ★ 실제 빌드. 창은 이것만 호출한다
  BuildDiagnostics.cs        진단 (서버 응답 / 씬 0번 / 맵 파일 나이)
  PlayerLaunchService.cs     빌드물 N개 실행, 인스턴스별 로그 파일
  BuildMenu.cs               창을 열지 않는 지름길 3개
Assets/Editor/Scene/         MainLobbySetup.cs, MultiplayerTestScene.cs
Assets/Editor/Map/           MapCollisionExporter.cs  (메뉴 + 순수 함수)
Assets/Scripts/Config/
  NVEnvironment.cs           ★ 런타임 타입 + 결정 순서. Editor 폴더 밖
```

**폴더를 `Assets/Editor/Build/` 로 만들면 안 된다.** `.gitignore` 의 표준 Unity 줄
`[Bb]uild/` 는 트리 어디에 있든 그 이름의 폴더를 잡으므로 그 안의 스크립트는 커밋되지
않는다. 폴더의 `.meta` 는 파일이라 살아남기 때문에 증상이 더 나쁘다 — 남의 작업
폴더에는 내용 없는 폴더 등록만 들어간다. 그 줄은 빌드 출력물을 막는 것이므로 약화시키지
않고 이름을 피한다. (Phase 1 에서 실제로 걸렸다.)

`NVEnvironment.cs` 는 `Net/Session/` 이 아니라 **`Config/`** 에 둔다. 접속 대상만 담는
것이 아니라 디버그 키 같은 게임 쪽 값도 담고, `MatchBootstrap`(게임)과
`PlayerProfile`(로비)과 `NetSession`(네트워크)이 모두 읽는다 — 그중 한 층에 두면
나머지 두 층이 그 층을 참조하게 된다.

**의존 방향은 한 쪽이다.** `BuildManagerWindow` → `BuildRunner` → (`MapCollisionExporter`, Unity API). `BuildRunner` 는 `EditorWindow` 타입을 참조하지 않는다. 창이 로직을 들고 있지 않은 것이 확장의 유일한 조건이다 — 나중에 배치모드 진입점이나 여러 환경 일괄 빌드가 필요해지면 `BuildRunner` 를 다른 곳에서 부르는 것으로 끝난다. 지금은 만들지 않는다.

`BuildRunner.Run(BuildSelection selection)` 이 하는 일, 순서대로:

```
1. 사전 조건   플레이 모드 / 컴파일 중
2. 검증       원격 호스트 + secure 꺼짐 → 중단
3. 씬 수집     EditorBuildSettings 에서. 비어 있으면 중단
4. 경고       저장 안 된 씬, 0번이 MainLobby 가 아님 (막지 않는다)
5. 환경 굽기   선택된 환경 → Resources/NVEnvironment.asset
6. 플랫폼 전환  필요하면. 비용을 먼저 로그에 적는다
7. BuildPlayer BuildPlayerOptions 조립 → BuildPipeline.BuildPlayer
8. 실행       RunAndLaunch 로 들어온 경우만 PlayerLaunchService
```

**되돌리는 것은 하나뿐이다: WebGL 압축.** `GameConfig` 도 씬도 만지지 않고, 구워진 환경은
매번 덮어쓰므로 남아도 해가 없다(에디터는 `EditorPrefs` 의 선택을 먼저 읽고 `.gitignore` 가
그 파일을 무시한다). `PlayerSettings` 중 **압축 형식만** 빌려 쓰고 `try/finally` 로 갚는다 —
그 값은 프로젝트 설정이라 갚지 않으면 한 번의 빌드가 남의 빌드를 조용히 바꾼다. 나머지
WebGL 설정(스트리핑·예외 처리·템플릿)은 손대지 않는다. 되돌릴 값을 하나로 유지할 만큼만
만진다.

출력 경로는 `Builds/{envId}/{platform}/`. 환경이 경로에 들어가야 두 빌드물을 나란히 두고 비교할 수 있다. `.gitignore` 가 `[Bb]uilds/` 를 이미 무시하므로 추가 조치는 없다.

### 메뉴 재편

```
Tools/NV/
  Build Manager…                      10   ← 창. 아래 둘은 창을 안 열고 가는 단축
  Build and Launch 2 Clients          11   ← 가장 많이 쓰는 경로라 남긴다
  ---
  Scene ▸ Create Main Lobby Scene     40
  Scene ▸ Create Multiplayer Test…    41
  Map ▸ Export Map Collision          60
```

---

## 6. 실행 순서

| Phase | 하는 일 | 끝났다는 증거 |
|---|---|---|
| **1. 환경 축** ✅ | `NVEnvironment` 런타임 타입, 결정 순서, `PlayerPrefs` 키에 envId 삽입, `PlayerProfile.DefaultHost` 상수와 `NetSession.host`/`secure` 필드 제거, `local`·`dev` 애셋 | 에디터에서 환경을 바꿔 Play 하면 붙는 서버가 바뀐다. 다른 환경의 PlayerPrefs 값이 새어 오지 않는다 |
| **2. `BuildRunner`** ✅ | 창 없이 `BuildSelection` + `BuildRunner`. Windows 만 | 메뉴 하나로 빌드한 exe 가 지정한 환경을 가리킨다 |
| **3. 이관·폐기** ✅ | `TestClientBuild.cs`·`NetworkSetup.cs` 삭제, `PlayerLaunchService` 이관, 메뉴 재편, 파일 이동 | 두 클라이언트가 예전과 같이 뜬다. `Tools/NV Network` 가 사라진다 |
| **4. 창** ✅ | `BuildManagerWindow`, 씬 목록 편집, 환경 드롭다운 + 인라인 편집, `BuildDiagnostics` | 창에서 플랫폼·씬·환경을 골라 빌드가 되고, 서버가 꺼져 있으면 진단에 뜬다 |
| **5. WebGL** ◐ | WebGL 플랫폼 지원, 압축 선택 + 원복, 플랫폼 전환 비용 경고, 빌드 후 정적 서버 안내 | 코드는 준비됐다. **실제 WebGL 빌드는 아직 뽑지 않았다** — 플랫폼 전환이 수 분이고 에디터 상태를 바꾸므로 사용자가 편할 때 창에서 누른다 |
| **6. 문서** ✅ | 루트 `CLAUDE.md`(메뉴 표 + 환경 단락)·`NVproject/CLAUDE.md`(컴파일 검증, MCP 경고 함정)·`NVserver/docs/readme.md`·`structure.md` | 문서의 메뉴 경로가 실제와 일치한다. `NV Network` 를 가리키는 문서·코드가 남아 있지 않다 |

**Phase 1 이 먼저인 이유:** 환경 축이 없으면 그 뒤는 "Windows 개발 빌드를 좀 더 예쁘게 뽑는 도구" 로 끝난다. 이 단계만 런타임 코드(`NetSession`, `PlayerProfile`, `LobbyService`, `SettingsPopup`)를 건드리고 되돌리기가 가장 비싸므로, 결정 순서를 확정하고 이 문서에 적은 뒤 진행한다.

**Phase 5 를 뒤에 둔 것은 순서상의 편의일 뿐, 미룰 일은 아니다.** 최종 타깃을 한 번도 빌드해 본 적 없는 상태가 이 프로젝트의 가장 큰 미지의 위험이다. Phase 3 직후에 탐색만이라도 해 두는 편이 낫다.

**만들지 않는 것:** 여러 환경 일괄 빌드(`BuildSet`), 빌드 히스토리, 배치모드/CI 진입점, Unity native Build Profile 연동. 넷 다 `BuildRunner` 를 다른 데서 호출하는 것으로 나중에 붙는다 — 지금 넣으면 "간단한 도구" 가 아니게 된다.

---

## 6-1. 진행 기록

### Phase 1 — 환경 축 (완료, 에디터 확인 대기)

만든 것:

| 파일 | 내용 |
|---|---|
| `Assets/Scripts/Config/NVEnvironment.cs` | 런타임 타입 + `Active` 결정 순서 + `IsInsecureRemote` 판정 |
| `Assets/Settings/Environments/local.asset` | `localhost:5202`, 평문, 주소 변경 허용, 디버그 키 켬 |
| `Assets/Settings/Environments/dev.asset` | 같은 호스트, **디버그 키 끔** — 환경으로 무엇이 갈리는지 보이는 최소한의 두 번째 환경 |
| `Assets/Editor/BuildManager/NVEnvironmentSelection.cs` | `EditorPrefs` 저장소 + `Tools ▸ NV ▸ Environment ▸ Switch / Show`. Phase 4 의 창이 이 클래스를 그대로 쓴다 |

고친 것: `PlayerProfile`(환경별 키, `DefaultHost` 제거, `CanChangeHost`), `NetSession`(`host`/`secure` 직렬화 필드 → 프로퍼티, `Awake` 에서 환경 수령), `LobbyService`, `RoomService`, `LobbyUIController`, `NetworkTestUi`(`session.host` 직접 대입 → `Configure` 경유), `SettingsPopup`(잠금 사유 두 가지를 갈라 표시), `MatchBootstrap`(디버그 키 환경 게이트), `.gitignore`.

### 계획에서 벗어난 두 가지

1. **디버그 키를 빌드 단계로 만들지 않았다.** 계획은 빌드 전에 애셋을 고치고 원복하는 절차(`BuildRunner` 5·7단계)를 뒀지만, `debugKeys` 는 `GameConfig` 가 아니라 `MatchBootstrap` 의 필드이고 `MatchSync` 가 이미 세션이 있을 때 끈다. `MatchBootstrap.Update` 가 환경을 읽어 AND 하는 것으로 끝나므로 **단계 하나와 §7 의 "GameConfig 원복" 함정이 함께 사라졌다.** 애셋을 만지지 않는 쪽이 항상 싸다.
2. **환경 전환 메뉴를 Phase 1 에 앞당겼다.** 창(Phase 4) 없이는 에디터에서 환경을 바꿀 방법이 없어 Phase 1 의 완료 증거를 확인할 수 없었다. 메뉴 항목을 환경마다 두지 않고 **순환**시키는 것은 환경 추가가 애셋 하나 만드는 일로 남아야 하기 때문이다 — `MenuItem` 은 항목 이름을 실행 시점에 만들 수 없다.

### Phase 2·3 — BuildRunner 와 이관·폐기 (완료, 에디터에서 확인됨)

만든 것: `BuildSelection.cs`(플랫폼·옵션·출력 경로, `EditorPrefs`) · `BuildRunner.cs`(8단계) ·
`PlayerLaunchService.cs` · `BuildMenu.cs`(지름길 3개).

지운 것: `TestClientBuild.cs`, `NetworkSetup.cs`.
옮긴 것: `MainLobbySetup.cs`·`MultiplayerTestScene.cs` → `Assets/Editor/Scene/`,
`MapCollisionExporter.cs` → `Assets/Editor/Map/`. 메뉴는 `Tools/NV/` 아래로 재편했고,
메뉴 경로를 문자열로 들고 있던 코드 4곳(`MultiplayerTestScene`, `NetworkBootstrap`,
`SessionSceneRouter`, 서버의 `ModuleRegistration`)과 문서 3개를 함께 고쳤다.

**에디터에서 확인한 것:**

| 확인 | 결과 |
|---|---|
| 새 메뉴 8개 등록, 옛 `Tools/NV Network/*` 소멸 | 8개 모두 있음, 옛 경로 `false` |
| Windows 빌드 | `Builds/local/Windows64/NVClient.exe` 생성 (199MB) |
| 환경이 빌드에 구워졌는가 | `id=local url=http://localhost:5202 debugKeys=True`, 애셋 이름 `NVEnvironment` 로 복구됨 |
| 빌드 산출물이 커밋되지 않는가 | exe·구워진 애셋·그 `.meta` 모두 ignore |
| **원격 + 평문 차단** | `nv.example:443` + `secure=off` → 빌드 거부. `secure` 만 켜면 같은 호스트가 통과. `dev.asset` 원상 복구 확인 |

### Phase 4 — 창 (완료, 에디터에서 확인됨)

만든 것: `BuildManagerWindow.cs`(`Tools ▸ NV ▸ Build Manager…`), `BuildDiagnostics.cs`.
`MainLobbySetup.EnsureEntryScene()` 를 공개해 창의 "0번으로 되돌린다" 버튼이 같은 코드를 쓴다.

**에디터에서 확인한 것:**

| 확인 | 결과 |
|---|---|
| 창이 열리고 트리가 만들어지는가 | Label 23 · Toggle 5 · Button 3 · Dropdown 1 · RadioGroup 1 · IntegerField 3 |
| 진단이 통과를 말하는가 | 서버 응답함 / 진입 씬 MainLobby / 맵 데이터 새로움 |
| 진단이 **경고도** 말하는가 | 닫힌 포트 → "서버 응답 없음", 포트 없는 주소 → "주소에 포트가 없다". 한쪽만 되는 진단은 쓸모가 없으므로 양쪽을 다 확인했다 |

### Phase 5 — WebGL (코드만, 빌드는 사용자 몫)

WebGL 에서 실제로 사람을 잡는 설정은 **압축 하나**다. Unity 기본값은 Brotli 이고, 그렇게 뽑은
빌드는 서버가 `Content-Encoding: br` 를 붙여야만 열린다. `python -m http.server` 는 붙이지
않으므로 브라우저는 압축된 바이트를 스크립트로 해석하려다 실패한다 — **증상은 검은 화면이고
빌드나 코드를 의심하게 만든다.** 그래서 이 값만 창에 올리고 기본값을 `Disabled`(지금 바로 열어
볼 수 있는 쪽)로 두었으며, 값 옆에 결과를 한 줄로 적는다. 빌드가 끝나면 프로젝트 설정을 되돌린다.

`file://` 로 여는 것도 로딩바에서 멈추는 실패라 빌드 후 안내에 미리 적었다.

확인한 것: WebGL 을 고르면 압축 칸이 나타나고 Windows 로 돌리면 사라진다(1 → 0), 출력 경로가
폴더가 된다(`Builds/local/WebGL`), `CanLaunch=False` 로 실행 버튼이 빠진다, 프로젝트의 WebGL
압축 설정(`Brotli`)이 그대로다. **실제 WebGL 빌드는 뽑지 않았다.**

### 검증 중에 잡은 버그 하나 — 열린 창이 낡은 값을 보여줬다

`Tools ▸ NV ▸ Environment ▸ Switch` 로 환경을 바꿔도 **이미 열려 있는 Build Manager 창은
갱신되지 않았다.** 창이 화면을 다시 만드는 계기가 포커스 획득뿐이었고, 메뉴를 누르는 동안
창은 이미 포커스를 갖고 있다. 결과는 **창이 `dev` 를 보여주면서 빌드는 `local` 을 굽는 상태** —
이 도구가 없애려던 종류의 어긋남이 도구 안에 생긴 것이다.

고친 방법은 `NVEnvironmentSelection.Changed` 이벤트이고, 방향은 **선택 → 창**이다. 창이 그것을
구독하고, 선택 쪽은 창의 타입을 모른다 — 알게 하면 배치모드에서 쓸 수 없어진다. `GetWindow` 가
이미 열린 창에 포커스만 주는 것도 같은 문제였으므로 `Open()` 이 트리를 다시 만들게 했다.

### 계획에서 벗어난 넷째 — UXML 을 쓰지 않았다

계획은 `BuildManager.uxml` + `.uss` 를 뒀지만 창을 C# 으로 만들었다. 창의 내용은 씬 수·환경
수·진단 수에 따라 달라져 어차피 코드가 만들고, UXML 을 두면 정적인 틀 하나를 위해 **경로로
애셋을 읽는 실패 경로가 하나 생긴다** — 폴더 이름을 바꾸면 트리가 조용히 null 이 된다. 이
저장소가 이미 `[Bb]uild/` 로 그 종류의 사고를 한 번 겪었다. UXML 규칙은 스타일시트를 사람이
손보는 게임 화면에 대한 것이고, 에디터 창은 그 대상이 아니다.

### 계획에서 벗어난 셋째 — 맵 export 를 빌드 단계에서 뺐다

계획은 `exportMapCollision` 체크박스와 빌드 전 export 단계를 뒀지만 **만들지 않았다.**

레벨은 진입 씬에 없다. `backrooms` 는 `SampleScene`, `test-room` 은 `MultiplayerTest` 에 있고
export 는 **열린 씬**에서만 동작한다(`MapExport.FindInScene`). 그래서 "빌드 전에 export" 는
실제로는 "빌드 도중에 다른 씬 두 개를 열고 원래 씬으로 되돌린다" 는 뜻이 되는데, 그것은
이 도구가 감당할 크기가 아니고 실패했을 때 사람의 씬 편집을 잃을 수 있다.

같은 실수를 같은 순간에 잡는 더 싼 방법이 이미 계획에 있다 — **진단 줄**(`BuildDiagnostics`,
Phase 4)이 `MapData/*.json` 의 mtime 을 씬 파일과 비교해 "낡았다" 고 말하고, 고치는 것은
`Tools ▸ NV ▸ Map ▸ Export Map Collision` 한 번이다. 막지 않고 알려 주는 쪽을 택한다.

### 검증한 것과 못 한 것

**컴파일: 에러 0개.** `NVproject/CLAUDE.md` 는 "There is no CLI build" 라고 적고 있지만, Unity 가 생성해 둔 `.csproj` 로 **스크립트 컴파일만은 에디터 없이 확인할 수 있다.** 새 파일은 그 `Compile` 목록에 없어 `CS0234` 로 먼저 걸리므로 목록에 넣고 다시 돌려야 한다(그 파일은 gitignore 대상이고 Unity 가 다시 만든다).

```
dotnet build Assembly-CSharp.csproj          → 오류 0개 (경고 18개, 17개는 기존 DTO 의 CS0649)
dotnet build Assembly-CSharp-Editor.csproj   → 오류 0개
```

**에디터에서 확인했다.** 손으로 쓴 `local.asset`/`dev.asset` 은 정상 import 되고(한글 표시명 포함),
전환이 `NVEnvironment.Active` 를 실제로 바꾸며 `allowDebugKeys` 가 따라온다. 환경별
`PlayerPrefs` 격리는 실제로 넣고 확인했다:

```
local 에 덮어쓴 뒤 = probe.example:9999
dev 에서 읽은 값   = localhost:5202      ← 새어 오지 않는다
local 로 돌아와서  = probe.example:9999  ← 되돌아온다
```

**아직 사람이 봐야 하는 것 하나.** 빌드된 클라이언트 두 개를 실제로 띄워 서로 붙는지는
`Tools ▸ NV ▸ Build and Launch 2 Clients` 를 눌러야 한다 — 그것은 사용자 화면에 창을
두 개 띄우는 일이라 이 세션에서 대신 하지 않았다. 서버(`dotnet run --project Api`)가 떠
있어야 한다.

**MCP 래퍼의 함정 하나.** `Unity_RunCommand` 는 실행 중 **경고 하나만 올라와도** 호출을
`UNEXPECTED_ERROR` 로 표시한다. 빌드는 Sentis 패키지의 셰이더 경고를 수백 줄 뱉으므로,
빌드를 MCP 로 돌리면 **성공해도 실패로 보인다.** 판단은 응답 맨 끝의 `[Log]` 줄로 한다 —
`result.Log` 로 결론을 마지막에 한 번 찍어 두면 그것이 유일하게 믿을 수 있는 신호다.
같은 이유로 `LogError` 를 쓰는 정상 경로(빌드 거부)도 실패처럼 보인다.

---

## 7. 미리 아는 함정

| 함정 | 증상 | 대응 |
|---|---|---|
| `NVEnvironment` 가 에디터 어셈블리에 들어감 | 빌드에서 타입이 사라져 런타임이 환경을 못 읽는다 | `Assets/Scripts/Config/` 에 둔다. `Assets/Editor/` 밖이다 |
| 에디터 스크립트를 `Assets/Editor/Build/` 에 둠 | **`.gitignore` 의 `[Bb]uild/` 가 잡아 커밋되지 않는다.** 폴더의 `.meta` 만 살아남아 남의 작업 폴더에는 빈 폴더 등록만 들어간다 | 폴더 이름을 `BuildManager` 로 둔다. ignore 규칙을 예외로 뚫지 않는다 — 다음 폴더가 같은 함정에 빠진다 |
| `Resources/NVEnvironment.asset` 커밋 | 마지막으로 빌드한 사람의 환경이 남의 에디터 기본값이 된다 | `.gitignore` 추가. 에디터는 창의 `EditorPrefs` 선택을 읽는다 |
| `GameConfig` 를 끄고 원복 안 함 | 디버그 키가 꺼진 채로 커밋되어 오프라인 테스트가 안 된다. **ScriptableObject 변경은 에디터에 영구히 남는다** | `try/finally` 원복. 이 프로젝트가 `PlacementSeedOverride` 를 프로퍼티로 둔 것과 같은 이유다 |
| 씬 목록을 창이 따로 들기 | 창의 목록과 Build Settings 가 어긋나고 빌드해야 보인다 | 창은 `EditorBuildSettings.scenes` 를 직접 편집한다. 사본을 만들지 않는다 |
| 스크립트 편집 → 도메인 리로드 | 창의 필드가 날아간다 | 창은 상태를 애셋과 `EditorPrefs` 에서 다시 읽는다. `bool built` 같은 플래그를 들지 않는다(`GameHudController.TreeIsLive` 와 같은 이유) |
| 플레이 모드 중 빌드 | 에디터 편집이 조용히 버려진다 | `BuildRunner` 첫 줄에서 `Application.isPlaying` / `EditorApplication.isCompiling` 을 막는다 |
| `BuildProfile` 이름 충돌 | Unity 6 에 `UnityEditor.Build.Profile.BuildProfile` 이 있다. 같은 이름은 `using UnityEditor` 가 있는 파일에서 애매한 참조가 된다 | 자체 타입에 그 이름을 쓰지 않는다 (`BuildSelection`, `NVEnvironment`) |
| `AssetDatabase.CreateAsset` 를 MCP 커맨드에서 호출 | 사용자 상호작용 오류로 커맨드가 중간에 죽는다(`NVproject/CLAUDE.md`) | 환경 애셋은 **에디터 메뉴에서** 만든다. MCP 로는 새 경로에 쓰기만 |
| 플랫폼 전환 비용 | 진행바가 몇 분 멈춘 것처럼 보여 사람이 창을 강제 종료한다 | 플랫폼 라디오 옆에 "현재 플랫폼과 일치 / ⚠ 전환 필요 — 첫 빌드에 수 분" 을 표시 |

---

## 8. 열린 질문

1. **WebGL 릴리스에 `MultiplayerTest`·`SampleScene` 이 들어가야 하는가.** 창이 Build Settings 를 직접 편집하므로 "릴리스용 씬 조합" 을 저장할 곳이 없다. 배포 시점에 손으로 체크를 끄는 것으로 충분한지, 조합을 기억해야 하는지 Phase 5 이전에 답해야 한다.
2. **`dev` 의 실제 호스트.** 서버 배포처가 정해지지 않았다면 `local`(평문 localhost)과 `dev`(같은 호스트, 디버그 키 꺼짐) 두 개로 시작한다. 환경 추가는 애셋 하나를 만드는 일이다.
3. **WebGL 빌드물을 어디서 띄우는가.** 로컬 `python -m http.server` 로 끝낼 것인지, `NVserver/Api` 가 정적 파일을 서빙할 것인지. 후자면 같은 오리진이 되어 CORS 와 `wss` 주소 문제가 사라지지만 서버 변경이 생긴다.
