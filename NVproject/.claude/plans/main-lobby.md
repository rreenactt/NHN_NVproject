# 메인 로비 실행 계획

게임을 켜면 처음 만나는 화면을 만든다. 로고·플레이어 정보·서버 상태·방 목록·방 만들기·빠른 참가·설정·종료가 한 화면에 있고, 여기서 방에 들어가면 매치로 이어진다.

기준 문서는 `NVproject/CLAUDE.md`, `.claude/skills/game-ui-generator`, `.claude/skills/aesthetic-spec`, `NVserver/docs/architecture.md`, 그리고 선행 계획인 `.claude/plans/invite-code-session.md`. 이 파일은 임시 작업 산출물이다. 실행 중 계획이 바뀌면 이 파일을 고친다.

---

## 확정된 결정 세 가지

| # | 질문 | 결정 | 대가 |
|---|---|---|---|
| 1 | UI 스택 | **UI Toolkit 유지.** 요구사항의 "Prefab 기반 재사용"은 **UXML 템플릿 + USS 클래스**로 구현한다 | 요구사항의 `Assets/Prefabs/Lobby/**` 트리는 그대로 나오지 않는다. 대응표를 아래에 적었다 |
| 2 | 서버 API | **현재 구성 그대로.** 초대 코드로 방에 붙는 설계를 유지하고 서버에 엔드포인트를 추가하지 않는다 | 방 목록·빠른 참가·온라인 인원은 개발 플래그(`Realtime:AllowRoomListing`) 뒤의 `GET /rooms` 위에 얹힌다. 플래그가 꺼진 배포에서 그 세 기능은 **동작하지 않는 것이 아니라 "비공개"로 표시**되어야 한다 |
| 3 | 기존 로비 | **MainLobby 가 대체한다.** `LobbyController`·`Lobby.uxml`·`lobby.uss`·`LobbyPanelSettings`·`LobbySetup.cs` 는 제거 | 검증된 화면 하나를 버린다. 대신 그 아래층(`NetSession`·`RoomApi`·`SessionFailure`·`InviteCodeText`·`InviteLink`·`SessionSceneRouter`)은 **한 줄도 다시 쓰지 않는다** |

### 결정 1 — "Prefab" 을 무엇으로 읽는가

이 프로젝트에는 자기 프리팹이 **하나도 없다**(서드파티 `Assets/Shady_3d/PREFAB` 제외). TextMeshPro 는 `Packages/manifest.json` 에 들어 있지도 않다. 제품 UI 는 전부 UI Toolkit 이고(`GameHUD.uxml` + `game-hud.uss`), uGUI 는 `Crosshair.cs` 한 곳뿐이다. 요구사항이 원하는 성질 — 재사용 단위, 동적 생성, 오브젝트 풀 — 은 UXML `VisualTreeAsset` 이 전부 제공한다: `CloneTree()` 가 `Instantiate` 이고, 반환된 `VisualElement` 를 free-list 에 넣으면 그것이 풀이다.

| 요구사항의 Prefab | 이 계획의 산출물 |
|---|---|
| `LobbyCanvas.prefab` | `MainLobby.uxml` (루트) + `MainLobbyPanelSettings.asset` |
| `Header/Header.prefab` | `MainLobby.uxml` 안의 `#header` 블록 |
| `Header/PlayerInfo.prefab` | `templates/PlayerInfo.uxml` |
| `Header/ConnectionStatus.prefab` | `templates/ConnectionStatus.uxml` |
| `Room/RoomList.prefab` | `MainLobby.uxml` 안의 `#room-panel` + `ScrollView` |
| **`Room/RoomItem.prefab`** | **`templates/RoomItem.uxml`** — `CloneTree()` + 풀링. 요구사항이 명시적으로 요구한 것이고 그대로 만족한다 |
| `Room/CreateRoomPopup.prefab` | `templates/CreateRoomPopup.uxml` |
| `Room/RoomPasswordPopup.prefab` | **만들지 않는다.** 서버에 방 비밀번호 개념이 없다. 그 자리를 **`JoinByCodePopup.uxml`**(초대 코드 입력)이 대신한다 — 이 게임에서 "남의 방에 들어가는 열쇠"는 비밀번호가 아니라 초대 코드다 |
| `Common/PrimaryButton.prefab` / `SecondaryButton.prefab` | `main-lobby.uss` 의 `.btn-primary` / `.btn-secondary` 클래스. 버튼 하나를 위해 템플릿을 만들면 텍스트를 바꾸는 데 두 파일을 열게 된다 |
| `Common/LoadingOverlay.prefab` | `templates/LoadingOverlay.uxml` |
| `Common/ConfirmDialog.prefab` | `templates/ConfirmDialog.uxml` |
| `Common/ToastMessage.prefab` | `templates/ToastMessage.uxml` |
| `Settings/SettingsPopup.prefab` | `templates/SettingsPopup.uxml` |
| `Popup/PopupRoot.prefab` | `MainLobby.uxml` 안의 `#popup-root` + `PopupHost` C# 클래스 |

### 결정 2 — 서버가 답하지 않는 것을 화면에서 어떻게 말하는가

조사로 확인된 사실(파일·줄 근거는 아래 "서버 접속면" 절):

| 요구 기능 | 서버 현황 |
|---|---|
| 활성 방 목록 | `GET /rooms` 가 있지만 `Realtime:AllowRoomListing` 이 `true` 일 때만. `appsettings.Development.json` 에서만 켜져 있고 소스 주석은 공개 목록을 "결함이지 기능이 아니다" 라고 적고 있다 |
| 온라인 플레이어 수 | **없다.** 전역 세션 수를 내주는 엔드포인트가 없고 `SessionRegistry` 는 `internal` |
| 빠른 참가 | **없다.** 매치메이킹 모듈 미구현 |
| 맵 목록 | **없다.** `RoomMaps.ByMap` 은 노출되지 않는다 |

그래서 이렇게 만든다.

- 방 목록·새로고침·빠른 참가·온라인 인원은 **전부 구현하되 `GET /rooms` 하나에 얹는다.** 온라인 인원은 목록의 `playerCount` 합계이고, 라벨에 근거를 적는다 — `접속 27명 · 공개된 방 기준`. 없는 확실성을 주지 않는다.
- `GET /rooms` 가 **404** 를 주면 그것은 오류가 아니라 **"이 서버는 방 목록을 공개하지 않는다"** 는 정상 응답이다. 방 패널은 빈 목록이 아니라 안내 상태로 바뀌고, 빠른 참가 버튼은 이유를 달고 비활성, 온라인 인원 칸은 사라진다. **빈 목록과 비공개를 같은 화면으로 만들면 안 된다** — 하나는 "방이 없다", 다른 하나는 "알 수 없다"이고 사용자의 다음 행동이 다르다.
- 목록이 없어도 **방 만들기와 초대 코드 참가는 항상 살아 있다.** 이것이 제품의 본선이고 목록은 편의다.
- 맵 선택은 `Game:Maps` 의 키(`default`, `test-room`)를 클라이언트가 들고 있는다. 서버가 모르는 맵 id 는 `POST /rooms` 가 `400 unknownMap` 으로 돌려주므로, 목록을 틀리게 들고 있어도 조용히 깨지지는 않는다.

---

## 서버 접속면 (조사 결과, 이 계획이 의존하는 전부)

| 무엇 | 어디 | 계약 |
|---|---|---|
| 방 생성 | `POST /rooms` | 요청 `{map}`. **201** `{code, hostToken, map, mapName, mapHash, capacity:8, minPlayers:2}`. `400 unknownMap` / `503 codeExhausted` / `429`. **버전 검사 없음** |
| 참가 전 조회 | `GET /rooms/{code}?v=2` | **`?v=` 필수.** 200·409·503 은 **본문에 `RoomInfoResponse` 전체**를 싣는다(`{code, mapName, mapHash, phase, playerCount, capacity, hostPlayerId, minPlayers}`) — 그래서 "8/8 진행 중" 을 표시할 수 있다. 400·404·426 은 `{error}` |
| 방 목록 | `GET /rooms` | `RoomInfoResponse[]`. **`AllowRoomListing` 이 false 면 404 + 빈 본문.** 레이트리밋 **없음** |
| 생존 | `GET /health` | `text/plain` `ok`. 그 이상 아무것도 없다 |
| 룸 상태 | `Event(0x82)` | 2Hz 반복 전문. `phase, hostPlayerId(0xFF=없음), seekerPlayerId, outcome, startTick, placementSeed, playerCount`, 이어서 `(playerId, nameLen≤12, ASCII name)` × N. 이름은 **빈 문자열일 수 있다** |
| 레이트리밋 | IP 별 고정 창 1분 | `POST /rooms` 10/분. `GET /rooms/{code}` **와 `/ws` 가 한 양동이** 60/분. `GET /rooms` 는 제한 없음 |
| 초대 코드 | `Shared/Contracts/InviteCodeFormat.cs` | 알파벳 31자, 길이 **6~12 범위**(고정 아님). `IsValid` / `Normalize` |
| 정원 | 8명, 시작 최소 2명 | `capacity` · `minPlayers` 는 서버가 응답에 실어 준다. 클라이언트가 다시 적지 않는다 |

**두 가지 함정이 여기서 나온다.**

1. `GET /rooms` 에는 **정적 룸(`test`)이 섞여 나온다.** 정적 룸은 회수되지 않으므로 목록에 영원히 남는다. 개발 중에는 그것이 맞고(코드 없이 붙을 수 있는 유일한 방이다), 목록을 공개하는 배포에서는 눈에 거슬린다. 필터링하지 말고 **`개발용` 배지를 붙여 구분**한다 — 숨기면 그 방으로 들어갈 방법이 화면에서 사라진다.
2. `GET /rooms/{code}` 와 `/ws` 가 **레이트리밋 양동이를 공유한다.** 빠른 참가가 후보를 여러 개 시도하면 그 시도들이 실제 접속 예산을 깎는다. 후보 시도 횟수에 상한(3)을 두는 이유가 이것이다.

---

## 아키텍처 — 무엇을 새로 만들고 무엇을 재사용하는가

세션 계층은 이미 있고 검증되어 있다. **이 계획은 그 위에 화면만 얹는다.**

```
                      ┌─────────────────────────────────────────┐
   새로 만든다  →     │ Scripts/Lobby/UI/       (View)           │
                      │ Scripts/Lobby/Controllers/              │
                      │ Scripts/Lobby/Services/  ─┐             │
                      │ Scripts/Lobby/Models/     │             │
                      │ Scripts/Lobby/Events/     │             │
                      └───────────────────────────┼─────────────┘
                                                  │ 얇은 어댑터
   그대로 쓴다  →     ┌───────────────────────────▼─────────────┐
                      │ Scripts/Net/Session/                     │
                      │   NetSession   SessionState  RoomApi     │
                      │   RoomInfo     SessionFailure            │
                      │   InviteCodeText  InviteLink             │
                      │   SessionSceneRouter                     │
                      │ Scripts/Net/ NetworkClient (RoomState)   │
                      └──────────────────────────────────────────┘
```

**재사용을 강제하는 규칙 네 가지.**

- **실패 문구를 다시 쓰지 않는다.** `SessionFailure.Of(kind)` 가 13종의 한국어 `Message` 와 `NextAction` 을 이미 들고 있다. 로비 UI 는 그 두 문자열을 표시할 뿐이다. 새 실패 원인이 생기면 `SessionFailureKind` 에 추가한다.
- **코드 형식 규칙을 다시 쓰지 않는다.** `InviteCodeFormat`(Shared) → `InviteCodeText`(표시·힌트). 길이를 6으로 못 박으면 서버가 정당하게 늘린 코드를 클라이언트가 거부한다.
- **정원·최소 인원을 다시 쓰지 않는다.** 서버 응답의 `capacity`·`minPlayers` 를 쓴다.
- **세션 상태를 복제하지 않는다.** `LobbyModel` 은 **로비 화면의 상태**(방 목록, 선택, 필터, 마지막 새로고침 시각, 서버 상태)만 가진다. 접속 단계는 `NetSession.State` 가 정본이다. 두 벌이 되면 반드시 어긋난다.

### 계층 책임

| 계층 | 하는 일 | 하지 않는 일 |
|---|---|---|
| `Models/` | 로비 화면 상태 보관. 변경 시 `LobbyEvents` 로 알린다 | 네트워크, `VisualElement` 접근 |
| `Services/` | HTTP 코루틴, `NetSession` 호출, 응답 → 모델 | 화면 갱신, 문구 생성 |
| `Controllers/` | 화면 흐름, 사용자 입력, 팝업 열고 닫기, 서비스 호출 | `VisualElement` 를 직접 만들기 |
| `UI/` | 모델을 읽어 표시. 입력은 `Action` 콜백으로 위임 | 서비스·`NetSession` 직접 호출 |
| `Events/` | 정적 이벤트 허브 하나 | 상태 보관 |

### 클래스 이름 충돌

새 `NV.Client.Lobby.LobbyController` 와 기존 `NV.Client.Net.Session.LobbyController` 는 네임스페이스가 달라 CLR 충돌은 없다. 그래도 **기존 것을 제거한 뒤에 새 것을 넣는다**(L11) — 같은 이름 두 개가 공존하는 중간 커밋은 `using` 하나로 조용히 잘못된 타입을 잡는다.

### 왜 View 를 MonoBehaviour 로 만들지 않는가

`VisualElement` 는 도메인 리로드에서 살아남지 않는다. View 를 MonoBehaviour 로 만들면 컴포넌트는 살아남고 그 안의 요소 참조만 죽어 — 반쯤 살아 있는 객체가 남는다(`NVproject/CLAUDE.md` 가 기록한 함정). **View 는 평범한 C# 클래스**로 만들고 `Build()` 에서 통째로 새로 생성한다. MonoBehaviour 는 `MainLobbyController` 하나뿐이고, 그것이 `TreeIsLive` 로 트리 전체의 생사를 판정한다.

### 확장 지점 (친구·파티·채팅·매칭)

- `LobbyEvents` 가 유일한 알림 경로다. 새 시스템은 이벤트를 하나 더 추가할 뿐 기존 View 를 고치지 않는다.
- `MainLobby.uxml` 의 본문은 **좌(사이드 레일) / 중(방 패널) / 우(액션 패널)** 3열 그리드다. 친구 목록·파티는 좌측 레일, 채팅은 하단 도크 자리를 비워 둔다 — `#side-rail`, `#dock` 을 빈 컨테이너로 미리 넣어 둔다. 나중에 열을 추가하려고 레이아웃을 다시 짜는 일이 없다.
- 팝업은 전부 `PopupHost` 를 거친다. 새 팝업은 UXML 하나 + `Open()` 호출 한 줄.
- **Addressables 대비:** 모든 `Resources.Load` 를 `MainLobbyAssets` 정적 클래스 한 곳에 가둔다. 전환은 그 파일만 고치는 일이 된다. UI 에셋은 `Assets/Resources/UI/MainLobby/` 아래 한 폴더에 모은다.

---

## 파일 배치

```
NVproject/Assets/
├── Scenes/MainLobby.unity                         L01 (에디터 메뉴가 생성)
├── Editor/MainLobbySetup.cs                       L01
├── Resources/UI/MainLobby/
│   ├── MainLobby.uxml                             L02
│   ├── main-lobby.uss                             L02
│   ├── MainLobbyPanelSettings.asset               L02
│   └── templates/
│       ├── RoomItem.uxml                          L05
│       ├── PlayerInfo.uxml                        L04
│       ├── ConnectionStatus.uxml                  L04
│       ├── CreateRoomPopup.uxml                   L07
│       ├── JoinByCodePopup.uxml                   L07
│       ├── SettingsPopup.uxml                     L09
│       ├── LoadingOverlay.uxml                    L06
│       ├── ConfirmDialog.uxml                     L06
│       └── ToastMessage.uxml                      L06
└── Scripts/Lobby/
    ├── MainLobbyAssets.cs                         L02  (Resources 접근 단일 지점)
    ├── UI/
    │   ├── LobbyUIController.cs                   L03  (루트 View 조립)
    │   ├── RoomListView.cs                        L05
    │   ├── RoomItemView.cs                        L05
    │   ├── PlayerInfoView.cs                      L04
    │   ├── ConnectionStatusView.cs                L04
    │   ├── CreateRoomPopup.cs                     L07
    │   ├── JoinByCodePopup.cs                     L07
    │   ├── SettingsPopup.cs                       L09
    │   ├── LoadingOverlay.cs                      L06
    │   ├── PopupHost.cs                           L06
    │   ├── ConfirmDialog.cs                       L06
    │   └── ToastMessage.cs                        L06
    ├── Controllers/
    │   ├── MainLobbyController.cs                 L03  (유일한 MonoBehaviour)
    │   ├── LobbyController.cs                     L03  (화면 흐름)
    │   └── RoomController.cs                      L07  (생성·참가·빠른 참가)
    ├── Services/
    │   ├── LobbyService.cs                        L04  (health, 온라인 인원, 프로필)
    │   └── RoomService.cs                         L05  (목록·생성·참가 → NetSession)
    ├── Models/
    │   ├── LobbyModel.cs                          L03
    │   └── PlayerProfile.cs                       L04
    └── Events/
        └── LobbyEvents.cs                         L03

NVproject/Assets/Scripts/Net/Session/
├── RoomApi.cs                    List() 추가                      L05
├── NetSession.cs                 host/name 런타임 설정 API 추가    L09
└── SessionSceneRouter.cs         LobbyScene "Lobby" → "MainLobby"  L11

제거 (L11):
  Assets/Scripts/Net/Session/LobbyController.cs
  Assets/Resources/UI/Lobby.uxml, lobby.uss, LobbyPanelSettings.asset
  Assets/Editor/LobbySetup.cs
```

`Models/RoomInfo.cs` 는 **만들지 않는다** — `Net/Session/RoomInfo.cs` 가 이미 그것이다. 요구사항의 파일 목록에서 유일하게 빠지는 항목이고, 이유는 중복 금지다.

---

## UI 계층 구조 (요구사항 대비)

```
MainLobby.uxml
└─ #root  .lobby-root
   ├─ #background        .bg            (USS 그라디언트 + 스캔라인 텍스처)
   ├─ #header            .header
   │   ├─ #logo          .logo
   │   ├─ #player-info   ← templates/PlayerInfo.uxml
   │   └─ #connection    ← templates/ConnectionStatus.uxml   (온라인 인원 포함)
   ├─ #content           .content-grid
   │   ├─ #side-rail     (비워 둠 — 친구·파티 확장 지점)
   │   ├─ #room-panel    .panel
   │   │   ├─ #room-panel-head   (제목 + #refresh-button + 마지막 갱신 시각)
   │   │   ├─ #room-scroll       ScrollView
   │   │   │   └─ (RoomItem.uxml × N — 풀링)
   │   │   └─ #room-empty        (빈 / 비공개 / 오류 세 상태)
   │   └─ #action-panel  .panel
   │       ├─ #create-room-button   .btn-primary
   │       ├─ #join-code-button     .btn-primary
   │       ├─ #quick-join-button    .btn-secondary
   │       ├─ #settings-button      .btn-secondary
   │       └─ #quit-button          .btn-secondary .btn-danger
   ├─ #dock              (비워 둠 — 채팅 확장 지점)
   ├─ #popup-root        .popup-root   (기본 display:none)
   ├─ #toast-root        .toast-root
   └─ #loading-overlay   ← templates/LoadingOverlay.uxml
```

요구사항의 트리에서 **`#join-code-button` 하나가 늘었다.** 초대 코드가 이 서버의 유일한 확실한 참가 수단인데 요구사항의 액션 패널에는 그 입구가 없다. 목록이 비공개인 배포에서 그 버튼이 없으면 남의 방에 들어갈 방법이 화면에서 사라진다.

**미학은 기존 두 스타일시트를 따른다** (`.claude/skills/aesthetic-spec`, `game-hud.uss`, 제거될 `lobby.uss` 에서 승계): 벽 `#C9B36B`, 트림 `#A8924E`, 형광 `#FFF6D6`, 본 `#F2E7C2`, 갈흑 `#0E0C08`, 배경 `rgb(24,21,14)`. 순백·순흑 금지, 패널은 반투명·얇은 테두리·직각, 넓은 `letter-spacing`.

---

## 마일스톤

| # | 완료 시점에 동작하는 것 | 검증 |
|---|---|---|
| M1 (L01–L03) | 게임을 켜면 MainLobby 가 뜨고, 골격과 이벤트 배선이 산다 | 에디터 Play, 콘솔 0 오류, 스크립트 수정 후 도메인 리로드에서 트리 재생성 |
| M2 (L04–L06) | 헤더(플레이어·서버 상태·인원)와 공통 부품(팝업·토스트·로딩)이 산다 | 서버 켠 상태/끈 상태에서 상태 표시가 갈린다 |
| M3 (L07–L08) | 방 목록·새로고침·방 만들기·코드 참가·빠른 참가가 실제로 방에 넣는다 | 에디터 + `curl` + 콘솔 클라이언트로 생성→참가→명단→시작 |
| M4 (L09–L10) | 설정·종료·확장 지점 정리 | 설정한 주소·이름이 재실행 후에도 남는다 |
| M5 (L11–L12) | 기존 로비 제거, 라우팅 이관, 문서 갱신 | 재현 표 중 `빌드 필요` 를 뺀 전부. 매치 종료 후 `MainLobby` 복귀 |

---

## 태스크

### L01 — `MainLobby` 씬과 생성 메뉴

| 항목 | 내용 |
|---|---|
| 목적 | 게임 진입점이 되는 씬을 코드로 재현 가능하게 만든다 |
| 변경 대상 | 신규 `Assets/Editor/MainLobbySetup.cs`, 생성물 `Assets/Scenes/MainLobby.unity` |
| 내용 | `LobbySetup.cs` 를 템플릿으로 삼는다. 빈 씬 + 직교 `Lobby Camera`(`cullingMask = 0`, solid color `rgb(24,21,14)`) + `Main Lobby` 오브젝트에 `MainLobbyController`. 메뉴 **Tools ▸ NV Network ▸ Create Main Lobby Scene**(priority 4). 저장 후 Build Settings **0번(진입 씬)** 에 넣는다 |
| 완료 조건 | 메뉴 실행이 씬을 만들고, 씬을 열어 Play 하면 카메라 배경색만 있는 빈 화면이 뜬다. Build Settings 맨 위가 `MainLobby` 다 |
| 함정 | `if (Application.isPlaying) { warn; return; }` 가드 필수 — 플레이 모드 편집은 조용히 버려진다. MCP 안에서 `Undo.*` 를 쓰지 않는다(명령이 나중에 오류 나면 롤백되어 반쯤 적용된 씬이 남는다). `AssetDatabase.DeleteAsset` 은 MCP 에서 실패하므로 덮어쓰기 대신 새 경로에 쓴다 |
| **등록과 순서를 둘 다 코드가 정한다** | 등록은 빠뜨릴 수 없다 — `SessionSceneRouter` 가 `SceneManager.LoadScene("MainLobby")` 로 이름을 찾으므로, 목록에 없으면 에디터 플레이 중에도 매치 후 복귀가 실패한다. **순서도 코드가 정한다**: 씬을 지우고 메뉴로 다시 만들 수 있다는 것이 이 프로젝트의 전제인데, 0번 자리를 사람이 기억해야 하면 다시 만든 순간 진입 씬이 조용히 `SampleScene` 으로 돌아간다. 그 증상은 빌드를 실행해야 보이고 잡아 줄 테스트가 없다. 이미 등록되어 있으면 **빼고 다시 0번에 넣는다** — 그대로 두고 앞에 하나 더 넣으면 같은 씬이 두 번 등록된다 |

### L02 — UXML·USS 골격과 에셋 로케이터

| 항목 | 내용 |
|---|---|
| 목적 | 화면 뼈대와 팔레트, 그리고 Addressables 전환 지점을 만든다 |
| 선행 | 없음 |
| 변경 대상 | `Resources/UI/MainLobby/MainLobby.uxml`·`main-lobby.uss`·`MainLobbyPanelSettings.asset`, 신규 `Scripts/Lobby/MainLobbyAssets.cs` |
| 내용 | 위 계층 구조대로 UXML 작성(템플릿 슬롯은 빈 컨테이너로). USS 는 팔레트·`.panel`·`.btn-primary`/`.btn-secondary`/`.btn-danger`·`.content-grid` 3열. `MainLobbyAssets` 는 `VisualTreeAsset`/`StyleSheet`/`PanelSettings` 로드를 전담하는 `static` 클래스 — 경로 상수도 여기에만 둔다 |
| 완료 조건 | UI Builder 에서 열리고 레이아웃이 1920×1080 과 1280×720 에서 무너지지 않는다 |
| 함정 | `MainLobbyPanelSettings` 는 `GameHudPanelSettings`(sortingOrder 0)·크로스헤어 캔버스(100)와 **별도 에셋이고 sortingOrder 가 겹치면 안 된다**. 로비는 게임 씬과 공존하지 않으므로 0 으로 두되 에셋은 분리한다 |

### L03 — 컨트롤러·모델·이벤트 골격

| 항목 | 내용 |
|---|---|
| 목적 | 화면이 갱신되는 경로를 하나로 만든다 |
| 선행 | L02 |
| 변경 대상 | `Controllers/MainLobbyController.cs`, `Controllers/LobbyController.cs`, `Models/LobbyModel.cs`, `Events/LobbyEvents.cs`, `UI/LobbyUIController.cs` |
| 내용 | `MainLobbyController` 는 `[DefaultExecutionOrder(-60)]` MonoBehaviour. `UIDocument` 를 스스로 붙이고, `TreeIsLive` 프로퍼티(요소가 있고 패널에 붙어 있는가)로 `Update()` 에서 재생성 판정. `LobbyUIController` 가 UXML 을 인스턴스화하고 각 View 를 생성해 조립. `LobbyModel` 은 방 목록·선택·마지막 갱신 시각·서버 상태·온라인 인원. `LobbyEvents` 는 `ModelChanged`, `RoomListChanged`, `ConnectionChanged`, `ProfileChanged`, `ToastRequested` 정적 이벤트 |
| 완료 조건 | Play 시 빈 골격이 그려지고, `.cs` 를 고쳐 도메인 리로드가 나도 다음 프레임에 트리가 재생성된다 |
| 함정 | **`bool _built` 플래그를 쓰지 않는다.** bool 은 리로드에서 살아남고 `VisualElement` 는 죽어서, 전부 null 인 트리를 "빌드됨"으로 오인한 채 프레임마다 예외를 던진다(`GameHudController` 가 겪은 그대로). 정적 이벤트는 씬 재진입 때 중복 구독되므로 `OnDisable` 에서 반드시 해제한다 |

### L04 — 플레이어 정보 · 서버 연결 상태 · 온라인 인원

| 항목 | 내용 |
|---|---|
| 목적 | 헤더가 "지금 서버에 붙을 수 있는가"를 말한다 |
| 선행 | L03 |
| 변경 대상 | `templates/PlayerInfo.uxml`·`ConnectionStatus.uxml`, `UI/PlayerInfoView.cs`·`ConnectionStatusView.cs`, `Services/LobbyService.cs`, `Models/PlayerProfile.cs` |
| 내용 | `PlayerProfile` 은 표시 이름(12자 ASCII 상한 — 서버가 자르는 값과 같은 상한을 화면에서도 쓴다)·서버 주소·`secure` 를 `PlayerPrefs` 에 보관. `LobbyService` 가 `GET /health` 를 5초 주기로 폴링해 `Online / Offline / Checking` 3상태를 만들고, 프로토콜 버전(`ProtocolInfo.Version`)과 주소를 함께 표시. 온라인 인원은 `RoomService` 의 목록 결과에서 `playerCount` 합계를 받아 `접속 N명 · 공개된 방 기준` 으로 표시하고, 목록이 비공개면 **칸 자체를 숨긴다** |
| 완료 조건 | 서버를 껐다 켜면 표시가 5초 안에 따라온다. 이름을 32자 입력하면 12자로 잘린다 |
| 함정 | `GET /health` 는 본문이 문자열 `ok` 다 — JSON 으로 파싱하려 들면 실패한다. `UnityWebRequest.result` 와 `responseCode` 는 다른 정보다(`RoomApi.Reached()` 가 이미 그 구분을 한다 — 같은 판정을 다시 쓰지 말고 꺼내 쓴다). WebGL 은 스레드가 없으므로 **코루틴만** |

### L05 — 방 목록 · RoomItem · 새로고침

| 항목 | 내용 |
|---|---|
| 목적 | 방 목록을 띄우고, 비공개·빈 목록·오류를 서로 다르게 보여 준다 |
| 선행 | L03 |
| 변경 대상 | `templates/RoomItem.uxml`, `UI/RoomListView.cs`·`RoomItemView.cs`, `Services/RoomService.cs`, `Net/Session/RoomApi.cs`(`List()` 추가) |
| 내용 | `RoomApi.List(Action<RoomListResult>)` — `GET /rooms`, 200 → `RoomInfo[]`, **404 → `Listing­Unavailable` 플래그(실패가 아니다)**, 그 외 → `SessionFailure`. `RoomListView` 는 `RoomItem.uxml` 을 `CloneTree()` 하고 **free-list 로 풀링**한다(스크롤 중 GC 를 만들지 않는다). 항목 표시: 코드(대문자)·맵 이름·`N/8`·단계 배지(`대기`/`진행 중`)·정적 룸이면 `개발용` 배지·참가 버튼. 진행 중·정원 초과 항목은 참가 비활성 + 이유. 새로고침 버튼은 **클라이언트 측 3초 쿨다운** |
| 완료 조건 | 개발 서버에서 방 3개를 만들면 3개가 뜨고, `AllowRoomListing` 을 끄면 안내 상태로 바뀐다 |
| 함정 | `GET /rooms` 에는 **레이트리밋이 없다** — 자동 폴링을 넣으면 서버가 무방비로 맞는다. **자동 폴링을 넣지 않는다**(수동 새로고침 + 화면 진입 시 1회). 정적 룸 `test` 는 회수되지 않아 항상 목록에 있다 — 숨기지 말고 배지로 구분. 목록의 `playerCount` 는 조회 시점의 값이고 참가 시점에는 다를 수 있다 |

### L06 — 공통 부품: PopupHost · LoadingOverlay · ConfirmDialog · Toast

| 항목 | 내용 |
|---|---|
| 목적 | 팝업을 한 곳에서 관리하고, 이후 모든 팝업이 이 위에 얹히게 한다 |
| 선행 | L03 |
| 변경 대상 | `templates/LoadingOverlay.uxml`·`ConfirmDialog.uxml`·`ToastMessage.uxml`, `UI/PopupHost.cs`·`LoadingOverlay.cs`·`ConfirmDialog.cs`·`ToastMessage.cs` |
| 내용 | `PopupHost` 는 `#popup-root` 위의 **스택**. `Open(VisualElement, options)` / `CloseTop()` / `CloseAll()`. 배경 딤 클릭과 `Esc` 로 최상단만 닫힌다(`modal` 옵션이면 딤 클릭 무시). `LoadingOverlay.Show(reason)` / `Hide()` 는 겹침 카운트로 관리 — 두 작업이 겹쳤을 때 먼저 끝난 쪽이 오버레이를 걷어 버리는 사고를 막는다. `ToastMessage` 는 `#toast-root` 에 쌓이고 3초 뒤 페이드아웃, 최대 3개 |
| 완료 조건 | 팝업 두 개를 겹쳐 열고 `Esc` 두 번으로 순서대로 닫힌다 |
| 함정 | UI Toolkit 에서 `Esc` 를 받으려면 요소가 포커스를 가져야 한다 — 루트에 `focusable=true` 를 주고 `RegisterCallback<KeyDownEvent>` 를 `TrickleDown` 으로 단다. 딤은 `display:none` 이 아니라 **`RemoveFromHierarchy()`** 로 치운다(포커스가 보이지 않는 요소에 남는 것을 막는다) |

### L07 — 방 만들기 · 코드 참가 · 빠른 참가

| 항목 | 내용 |
|---|---|
| 목적 | 화면에서 실제로 방에 들어간다 |
| 선행 | L05, L06 |
| 변경 대상 | `templates/CreateRoomPopup.uxml`·`JoinByCodePopup.uxml`, `UI/CreateRoomPopup.cs`·`JoinByCodePopup.cs`, `Controllers/RoomController.cs` |
| 내용 | **방 만들기**: 맵 선택(`default`/`test-room`) → `NetSession.CreateAndJoin(mapId)`. **코드 참가**: 입력 칸이 `InviteCodeText.Normalize` 로 정규화하고 대문자로 되돌려 표시, `Hint(raw)` 를 실시간 힌트로 — 형식이 틀리면 **요청을 보내기 전에** 막는다(레이트리밋 예산을 아낀다). 유효하면 `NetSession.JoinByCode`. 실행 URL 의 `?code=` 는 `InviteLink.ReadCodeFromLaunchUrl()` 로 읽어 **칸을 채우기만 하고 자동 참가하지 않는다**. **빠른 참가**: `GET /rooms` → `phase == Waiting && playerCount < capacity` 필터 → **가장 많이 찬 방부터** 최대 3개 시도(빨리 시작될 방이 좋은 방이다) → 각 시도는 `JoinByCode`. 목록이 비공개면 버튼 비활성 + `이 서버는 방 목록을 공개하지 않는다. 초대 코드로 참가한다.` |
| 완료 조건 | 에디터 한 클라이언트로 방을 만들면 코드가 나오고, 서버 로그에 방과 세션이 잡히고, 같은 코드로 재접속하면 붙는다. `curl` 로 만든 방의 코드를 에디터에서 입력해 참가된다 |
| 함정 | `hostToken` 은 `POST /rooms` 응답에 **한 번만** 온다 — 다시 받을 방법이 없으므로 `NetSession` 밖으로 흘리지 않는다. 빠른 참가의 후보 시도는 `GET /rooms/{code}` + `/ws` 와 **같은 60/분 양동이**를 쓴다 — 상한 3 을 지킨다. 목록 조회와 실제 참가 사이에 방이 찰 수 있다: `RoomFull`·`RoomInProgress` 는 다음 후보로 넘어가고, 후보가 떨어지면 `SessionFailure` 를 그대로 보여 준다. `Application.absoluteURL` 은 에디터에서 빈 문자열이므로 `#if UNITY_WEBGL` 이 아니라 **값 유무로 분기**한다 |

### L08 — 방 안 화면(명단·시작)의 소재 결정

| 항목 | 내용 |
|---|---|
| 목적 | 방에 들어간 뒤의 화면이 사라지지 않게 한다 |
| 선행 | L07 |
| 변경 대상 | `templates/RoomPopup.uxml`(신규), `UI/RoomView.cs`(신규), `Controllers/RoomController.cs` |
| 내용 | 기존 `Lobby.uxml` 의 `#room` 화면이 제거되면서 **명단·코드 표시·코드/링크 복사·시작·나가기가 갈 곳을 잃는다.** 이것을 MainLobby 안의 전체 화면 팝업(`PopupHost` 위, `modal`)으로 옮긴다. 명단은 `NetworkClient.RosterCount`/`RosterEntry(i)` 를 `RoomStateChanged` 에서 다시 그리고, 이름이 빈 항목은 `플레이어 {playerId}` 로 대체. 방장 표시는 `RoomState.hostPlayerId == LocalPlayerId`. 시작 버튼은 `NetSession.CanStart` 로 활성/비활성하고 비활성 이유(`최소 {MinPlayers}명 필요`)를 함께 표시. 링크 복사 버튼은 `InviteLink.TryBuild` 가 false 인 플랫폼에서 숨긴다 |
| 완료 조건 | 에디터에서 방을 만들면 명단에 자기 이름이 뜨고, 방장 표시가 붙고, 1명이라 시작 버튼이 `최소 2명 필요` 로 비활성이다. 임시 WebSocket 콘솔 클라이언트를 같은 방에 붙이면 명단이 2명으로 늘고 시작 버튼이 활성된다 |
| 함정 | **요구사항에 이 화면이 없다** — 요구사항의 트리는 로비 첫 화면만 다룬다. 여기를 빠뜨리면 방을 만든 뒤 아무것도 할 수 없는 화면에 갇힌다. 클립보드는 에디터에서 `GUIUtility.systemCopyBuffer` 로 동작한다 — 코드와 링크를 선택 가능한 텍스트로도 남기고 복사 성공/실패를 토스트로 알린다 |

### L09 — 설정 팝업

| 항목 | 내용 |
|---|---|
| 목적 | 서버 주소·이름을 화면에서 바꾼다 |
| 선행 | L06, L04 |
| 변경 대상 | `templates/SettingsPopup.uxml`, `UI/SettingsPopup.cs`, `Net/Session/NetSession.cs` |
| 내용 | 표시 이름(12자)·서버 주소·`secure` 토글·마우스 감도·음량. `PlayerPrefs` 에 저장하고 `LobbyEvents.ProfileChanged` 로 헤더를 갱신. `NetSession` 의 `host`·`displayName`·`secure` 는 지금 인스펙터 필드뿐이므로 **런타임 설정 API 를 추가**한다(`Configure(host, secure, displayName)`), 단 `State != Idle` 이면 거부하고 이유를 돌려준다 |
| 완료 조건 | 주소를 바꾸고 재실행해도 유지되고, 접속 중에는 변경이 거부된다 |
| 함정 | 접속 중 주소 변경을 허용하면 다음 재시도가 다른 서버로 간다 — `NetSession` 의 재시도 경로(`RetryDelays`)가 조용히 엉뚱한 곳을 두드린다. 반드시 `Idle` 에서만 |

### L10 — 게임 종료

| 항목 | 내용 |
|---|---|
| 목적 | 종료가 플랫폼별로 맞게 동작한다 |
| 선행 | L06 |
| 변경 대상 | `Controllers/LobbyController.cs` |
| 내용 | `ConfirmDialog` 로 확인 → 에디터는 `EditorApplication.isPlaying = false`(`#if UNITY_EDITOR`), 그 외는 `Application.Quit()`. WebGL 에서는 `Application.Quit()` 이 아무 일도 하지 않으므로 `#if UNITY_WEBGL && !UNITY_EDITOR` 로 버튼을 숨기는 분기를 **미리 넣어 둔다** — 빌드는 이번 범위 밖이지만 한 줄이고, 나중에 빌드했을 때 반응 없는 버튼이 남는 것을 막는다 |
| 완료 조건 | 에디터에서 확인 후 플레이가 멈춘다 |

### L11 — 기존 로비 제거와 라우팅 이관

| 항목 | 내용 |
|---|---|
| 목적 | 로비를 한 벌로 만든다 |
| 선행 | L07, L08 |
| 변경 대상 | 제거 `Net/Session/LobbyController.cs`·`Resources/UI/Lobby.uxml`·`lobby.uss`·`LobbyPanelSettings.asset`·`Editor/LobbySetup.cs`, 수정 `Net/Session/SessionSceneRouter.cs` |
| 내용 | `SessionSceneRouter.LobbyScene` 상수를 `"Lobby"` → `"MainLobby"`. **`SessionSceneRouter` 를 세션 오브젝트에 붙이는 일은 지금 `LobbyController.OnEnable:74` 가 한다** — 그 한 줄을 `MainLobbyController.OnEnable` 로 옮긴다. Build Settings 에서 `Lobby` 씬 항목 제거 |
| 완료 조건 | 매치가 끝나거나 실패하면 `MainLobby` 로 돌아온다 |
| 함정 | **이 부착을 옮기지 않으면 매치가 끝나고 아무 데로도 돌아가지 않는다.** 라우터가 없으면 `InGame` 전이도 일어나지 않아 시작을 눌러도 씬이 바뀌지 않는다 — 증상이 "시작 버튼이 안 먹는다"로 나타나 UI 버그처럼 보인다. 파일 삭제는 `.cs` 와 `.meta` 를 함께, 그리고 `AssetDatabase.Refresh()` (MCP 의 `DeleteAsset` 은 실패한다) |

### L12 — 문서 갱신

| 항목 | 내용 |
|---|---|
| 목적 | 이 작업이 무효화하는 서술을 고친다 |
| 선행 | L11 |
| 변경 대상 | 저장소 루트 `CLAUDE.md`, `NVproject/CLAUDE.md`, `NVserver/docs/readme.md` |
| 내용 | 무효화되는 것: 루트 `CLAUDE.md` 의 메뉴 표(`Create Lobby Scene` → `Create Main Lobby Scene`)와 "Lobby (product flow)" 절차(`Assets/Scenes/Lobby.unity` → `MainLobby.unity`), `NVproject/CLAUDE.md` 의 로비 UI 경로, `NVserver/docs/readme.md` 의 실행 절차 |
| 완료 조건 | 문서의 경로·메뉴 이름으로 따라 했을 때 실제로 로비가 뜬다 |
| 함정 | 루트 `CLAUDE.md` 는 **PowerShell `Get-Content`/`Set-Content` 로 건드리면 안 된다** — 왕복에서 em-dash 와 `▸` 가 전부 `??` 로 깨진다. Edit/Write 도구를 쓴다 |

---

## 실행 순서

```
L01 ──┐
L02 ──┴─ L03 ─┬─ L04 ──────────────┐
              ├─ L05 ─┐            │
              └─ L06 ─┴─ L07 ─ L08 ─┴─ L11 ─ L12
                       ├─ L09
                       └─ L10
```

`L04`(헤더)·`L05`(목록)·`L06`(공통 부품)은 `L03` 이 끝나면 병행 가능하다. `L11`(기존 로비 제거)은 `L08` 이 끝난 뒤에만 — 방 안 화면이 새 로비에 옮겨지기 전에 옛 로비를 지우면 방을 만든 뒤 갈 곳이 없어진다.

---

## 검증

**클라이언트에는 CLI 빌드도 테스트 스위트도 없고, 이번 작업에서 빌드는 범위 밖이다.** 증거는 에디터 콘솔, 에디터 플레이 모드, 서버 로그, `curl`, 그리고 화면이다. 각 태스크마다:

1. 스크립트 저장 → 도메인 리로드 → **콘솔 0 오류** (Unity MCP `unity-mcp-ops` 절차)
2. 플레이 모드 재진입 후 확인 — 스크립트 편집 뒤 옛 코드가 조용히 도는 것이 이 프로젝트의 상시 함정이다
3. 해당하는 실패 재현 표 행

### 빌드 없이 다중 클라이언트를 어떻게 보는가

에디터는 클라이언트 하나뿐이다. 그런데 로비의 핵심(명단·정원·시작 조건·진행 중 거부)은 **두 명 이상이 있어야만 보인다.** 빌드를 쓰지 않으면서 두 번째 참가자를 만드는 방법은 둘이다.

| 수단 | 볼 수 있는 것 | 한계 |
|---|---|---|
| **`curl`** — `POST /rooms`, `GET /rooms/{code}?v=2`, `GET /rooms` | 방 생성·조회·목록·상태코드 6종·레이트리밋. 에디터가 만든 방을 밖에서 조회해 화면과 대조 | 참가자가 되지 않는다 — `playerCount` 는 0 |
| **임시 WebSocket 콘솔 클라이언트** (선행 계획이 서버 검증에 이미 쓴 그 도구) | **명단 증가, 정원, 시작 활성화, `Control(StartMatch)` 왕복, `Event(0x82)` 전문** | 몸이 없다. 매치 진입 후의 화면은 못 본다 |

**빌드가 있어야만 볼 수 있는 것**은 이 계획에서 미검증으로 남는다: 두 개의 *실제 화면*이 같은 명단을 보이는가, 8명을 채운 `RoomFull`, WebGL 의 `?code=` 링크와 클립보드 권한, 그리고 시작 이후 세 화면의 문·역할 일치(그것은 선행 계획 M5 의 몫이고 이 계획의 범위도 아니다). **미검증인 채로 완료라고 말하지 않는다** — 아래 재현 표의 해당 행에 `빌드 필요` 를 달아 둔다.

### 실패 재현 표 (수락 기준)

각 행이 **서로 다른 화면**을 내야 한다. 하나라도 뭉치면 미완이다. `SessionFailure` 가 이미 문구를 들고 있으므로 이 표는 "그 문구가 화면에 도달하는가"를 본다.

| 재현 | 기대 표시 | 다음 행동 |
|---|---|---|
| 서버 미기동에서 로비 진입 | 연결 상태 `오프라인`, 방 목록 오류 상태 | 재시도 |
| 서버 미기동에서 방 만들기 | `ServerUnreachable` | 주소 확인 |
| `AllowRoomListing=false` 서버 | 방 패널 **`비공개`** (빈 목록 아님), 빠른 참가 비활성 + 이유, 온라인 인원 칸 없음 | 코드로 참가 |
| 방이 0개인 개발 서버 | 방 패널 **`방이 없다`** (비공개 아님) | 방 만들기 |
| 코드 칸에 `ILO0` 입력 | 요청 전 `InviteCodeText.Hint` 힌트, 참가 버튼 비활성 | 코드 재확인 |
| 없는 코드로 참가 | `UnknownCode` | 코드 확인 / 새 방 |
| 진행 중 방에 참가 | `RoomInProgress` + 본문의 `N/8 진행 중` | 다음 판 대기 |
| 분당 10회 초과 방 만들기 | `TooManyRequests` | 잠시 뒤 |
| 등록되지 않은 맵으로 생성 | `UnknownMap` | 맵 변경 |
| 빠른 참가, 후보 3개 모두 실패 | 마지막 실패 사유 그대로 | 코드로 참가 |
| 접속 중 설정에서 주소 변경 | 거부 + 이유 | 나간 뒤 변경 |
| 스크립트 편집으로 도메인 리로드 | 트리 재생성, 예외 없음 | — |
| 방 안에서 명단이 1명 | 시작 비활성 + `최소 2명 필요` | 인원 대기 |
| 콘솔 클라이언트가 붙음 | 명단 2명, 시작 활성 | 시작 |

**빌드 필요 — 이번 범위에서 미검증으로 남긴다**

| 재현 | 기대 표시 |
|---|---|
| 두 개의 실제 화면에서 명단 일치 | 같은 명단·같은 방장 |
| 8명 찬 방에 9번째 | `RoomFull` |
| WebGL 링크 복사 권한 없음 | 복사 실패 토스트 + 선택 가능한 텍스트 |
| `?code=` 를 손으로 고친 링크 | 칸만 채워지고 자동 참가 없음 |

위 4행은 코드 경로를 구현하되 **동작 확인은 하지 않는다.** 빌드가 범위에 들어오는 시점에 이 표부터 돌린다.

---

## 범위 밖

| 항목 | 이유 |
|---|---|
| 서버에 공개 방 목록·인원 통계 엔드포인트 추가 | 결정 2. 초대 코드 모델을 유지한다 |
| 매치메이킹(진짜 빠른 참가) | `Matchmaking` 모듈 미구현. 목록 기반 후보 선택이 그 자리를 잠정적으로 채운다 |
| 친구·파티·채팅 | 자리(`#side-rail`, `#dock`)와 이벤트 허브만 남긴다. 계정 계층(`Identity` 모듈)이 없어 친구는 지금 구현할 수 없다 |
| 방 비밀번호 | 서버에 개념이 없다. 초대 코드가 그 역할을 한다 |
| Addressables 전환 | `MainLobbyAssets` 한 곳으로 좁혀 두는 것까지가 이번 범위 |
| 계정·로그인 | `Identity` 모듈 미구현. 표시 이름은 세션 수명만큼만 산다 |
| uGUI/TextMeshPro 도입 | 결정 1 |
| **빌드 실행** | 이번 작업은 에디터에서 끝난다. WebGL·스탠드얼론 빌드를 만들지 않고, `TestClientBuild` 의 2클라이언트 실행 경로도 다루지 않는다. `Build and Launch 2 Clients` 는 테스트용이고 폐기 예정이므로 이 계획이 되살리지 않는다. **진입 씬 지정(Build Settings 0번)은 예외로 범위 안이다** — 씬 생성 메뉴가 함께 정한다 |
| 다중 화면 육안 검증 | 위 이유로 미검증. 재현 표의 `빌드 필요` 4행이 그 목록이다 |

---

## 실행 중 바뀐 것

계획과 다르게 한 결정들. 이유를 남긴다.

| 계획 | 실제 | 이유 |
|---|---|---|
| `LobbyEvents` 를 정적 이벤트 허브로 | **인스턴스**로. `MainLobbyController` 가 소유 | static 이벤트는 도메인 리로드를 넘어 구독자를 남기는데 `VisualElement` 는 넘지 못한다. 죽은 트리를 가리키는 핸들러가 이벤트마다 예외를 던지고, 증상은 "가끔 화면이 두 번 그려진다" 로만 나타난다. 구독자도 `LobbyUIController` 하나로 좁혀 해제 지점을 한 곳에 뒀다 |
| `templates/LoadingOverlay.uxml` | `MainLobby.uxml` 안에 인라인 | 로딩 오버레이는 화면에 하나뿐이고 복제되지 않는다. 템플릿은 "반복되는 단위" 를 위한 것이고, 단일 요소를 템플릿으로 빼면 파일만 하나 늘고 얻는 것이 없다 |
| `RoomPasswordPopup` | `JoinByCodePopup` | 서버에 방 비밀번호 개념이 없다. 계획 단계에서 정한 대로 |
| `Models/RoomInfo.cs` | 만들지 않음 | `Net/Session/RoomInfo.cs` 가 이미 그것이다 |
| `RoomApi` 에 `List()` 추가 | `List()` 와 **`Health()`** 둘 다 | 생존 확인도 서버 HTTP 계약이다. `LobbyService` 가 `UnityWebRequest` 를 직접 쓰면 "HTTP 는 `RoomApi` 한 곳" 규칙이 깨지고, `Reached()` 의 판정(닿았는가 vs 4xx 응답)이 두 벌이 된다 |
| — (계획에 없던 것) | `RoomListResponseDto` 래퍼 | `JsonUtility` 는 최상위 배열을 파싱하지 못하고 **예외 대신 null 을 돌려준다.** 감싸지 않으면 파싱 실패가 "방이 0개" 로 조용히 둔갑해, 목록이 비어 보이는 원인이 서버인지 클라이언트인지 화면에서 구분할 수 없다 |
| 빠른 참가를 후보 3개 시도 | 그대로 구현 | 다만 다음 후보로 넘어가는 조건을 `RoomFull`·`RoomInProgress` **둘로만** 한정했다. 버전 불일치·서버 미도달·레이트리밋은 후보를 더 써도 결과가 같고, 그 경우 예산만 태운다 |
| `PlayerProfile` 은 이름·주소 보관 | 서버의 이름 절단 규칙(ASCII 12자)을 **입력 시점에 미리 적용** | 서버가 조용히 자르면 사용자가 친 이름과 명단의 이름이 달라지고, 그것을 버그로 신고하게 된다. 한글 이름이 통째로 사라지는 것이 특히 그렇다 |
| Build Settings 등록만, 순서는 그대로 | **0번(진입 씬)까지 코드가 정한다** | 사용자 지시로 바뀌었다. 손으로 한 번 올려 두면 씬을 다시 만든 순간 원위치로 돌아가고, 그 증상은 빌드를 실행해야 보인다. 이미 있으면 빼고 다시 넣어 중복 등록을 막는다 |
| `TestClientBuild` 는 건드리지 않는다 | **Build Settings 순서를 그대로 쓰도록 바꿨다** | 사용자 지시. 이 도구는 `scenes = new[] { MultiplayerTest }` 로 씬 목록을 **하드코딩**해 프로젝트의 진입 씬을 무시하고 있었다. 씬 목록이 두 벌이면 반드시 어긋나고, 계측용 빌드가 제품과 다른 화면으로 뜨는 차이는 화면에 아무 단서도 남기지 않는다. 씬을 더 넣어도 빌드는 거의 무거워지지 않는다 — 이 프로젝트의 씬은 지형도 플레이어도 담고 있지 않다 |
| — (계획에 없던 것) | `NetSession.JoinRoomId` 신설 | **정적 개발 룸에 들어갈 수 없는 결함을 고친다.** `JoinByCode` 는 초대 코드 형식(6자 이상)을 요구하는데 `test` 는 4자다 — 서버는 `Game:StaticRooms` 의 id 를 코드 규칙으로 검사하지 않기 때문이다. 목록에서 고른 방과 빠른 참가는 서버가 준 id 를 쓰므로 오타일 수 없고, 서버의 룸 id 규칙(소문자·숫자·하이픈, 32자)만 본다. 형식 검사는 사람이 받아 적은 코드에만 남는다 |
| — (계획에 없던 것) | `NetworkTestUi` 의 참가도 `JoinRoomId` 로 | **이 계획 이전부터 깨져 있던 것.** 개발 계기판의 참가 버튼이 `JoinByCode("test")` 를 불러 `InvalidCode` 로 거부되고 있었다. 문서가 안내하는 개발 경로가 동작하지 않았다는 뜻이다 |

## 공개 / 비공개 방 (결정 2를 뒤집었다)

사용자 지시로 **방마다 공개 여부를 정한다.** 공개 방은 활성 방 목록에 실리고, 비공개 방은 목록에 뜨지 않고 초대 코드로만 들어온다.

이것은 처음 결정 2("현재 API 구성으로 간다 — 서버를 건드리지 않는다")를 뒤집는다. 서버에 방별 가시성이라는 개념 자체가 없었기 때문에 클라이언트만으로는 만들 수 없었다.

### 설계

| 결정 | 내용 | 이유 |
|---|---|---|
| 생성 시 확정, 변경 불가 | `POST /rooms` 의 `isPublic` 이 유일한 선택 지점 | 비공개인 줄 알고 코드를 나눈 방이 나중에 목록에 뜨는 경로를 만들지 않는다 |
| **필드가 없으면 비공개** | 서버 `request?.IsPublic ?? false`, 클라이언트 기본 인자 `false` | 노출은 선택이어야 한다. 반대로 두면 필드를 모르는 옛 클라이언트의 방이 본인도 모르게 목록에 뜬다 |
| 팝업 기본값 비공개 | `Toggle` 이 꺼진 채로 열린다 | 같은 이유. 아무것도 건드리지 않은 사용자의 방은 노출되지 않는다 |
| 목록만 거른다 | `GET /rooms/{code}` 와 `/ws` 는 가시성을 보지 않는다 | 그쪽을 막으면 초대 코드 자체가 동작하지 않는다. 비공개는 "숨김"이지 "잠금"이 아니다 |
| `ListPublicRooms()` 하나만 | 전체를 돌려주는 메서드를 두지 않는다 | 엔드포인트에서 거르면 전체를 주는 문이 남고, 다음에 목록이 필요한 곳에서 그것을 부르는 순간 비공개 방이 샌다 |
| 정적 룸은 공개 | `test` 가 목록에 `DEV` 배지로 뜬다 | 로비에서 그 방에 닿는 유일한 길이다 — id 가 4자라 코드 입력 칸으로는 들어갈 수 없다 |
| **`AllowRoomListing` 제거** | 플래그를 없앴다 | 그 플래그는 목록이 **모든** 방을 내주던 시절의 방어선이다. 방마다 동의를 받게 되면서 근거가 사라졌고, 남겨 두면 공개를 선택한 방조차 뜨지 않아 그 선택이 무의미해진다 |
| **`RateLimit:ListPerMinute` 신설(30)** | `GET /rooms` 에 제한을 붙였다 | 플래그를 없애면 이 경로가 **상시 열린다.** 지금까지 제한이 없었던 것은 개발 설정에서만 열렸기 때문이고, 그 전제가 바뀌었다. 코드 시도와 **다른** 양동이를 쓴다 — 새로고침이 방에 들어갈 예산을 깎으면 안 된다 |

### 화면에 드러나는 것

- 방 만들기 팝업에 `공개 방으로 만든다` 토글. 어느 쪽이든 초대 코드는 나오므로, 설명에 그 사실을 함께 적는다 — 공개가 "누구나", 비공개가 "아무도"로 읽히지 않게.
- 방 안 화면에 `PUBLIC` / `PRIVATE` 줄. 코드를 어디까지 흘려도 되는지가 그것으로 갈리는데, **들어온 경로는 단서가 되지 않는다.**
- 빈 목록 문구가 "열린 방이 없다" → **"공개된 방이 없다"**. 비공개 방은 여기 실리지 않으므로, 그 사실을 적지 않으면 "아무도 게임을 안 한다"로 읽힌다.
- `Unavailable`(404) 의 의미가 바뀌었다. 이제 "설정이 꺼져 있다"가 아니라 **"서버가 이 기능을 지원하지 않는 버전이다"** 다.

### 검증

`dotnet build` 경고 0 · `dotnet test` **169 통과**(Modules) + **4 통과**(Architecture). 새 테스트 둘: 비공개 방이 목록에서 빠지되 코드로는 조회·접속되는가, 정적 룸이 공개인가.

## 30분 이상 걸린 문제는

`NVserver/docs/conventions.md` 에 증상 → 원인 → 대책으로 남긴다. UI Toolkit·도메인 리로드 관련이면 `NVproject/CLAUDE.md` 에도 한 줄 남긴다.
