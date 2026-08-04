# Map Collision Export 개선 계획

이 문서는 구현 계획이다. 코드는 아직 고치지 않았다. 아래의 모든 진단은 현재 리포지터리의
코드와 실제 맵 파일을 읽어서 확인한 것이며, 근거를 `파일:줄` 로 적었다.

대상은 `Tools ▸ NV ▸ Map ▸ Export Map Collision` 과 그것이 만드는 `NVserver/MapData/*.json`,
그리고 서버가 그 파일을 쓰는 경로 전체다.

---

## 1. 지금 어떻게 도는가

메뉴 한 번이 아래 여섯 단계를 순서대로 밟고, **중간에 사람이 개입할 지점이 없다.**

| # | 하는 일 | 어디 |
|---|---|---|
| 1 | 메뉴 진입 | `MapCollisionExporter.cs:31` `[MenuItem("Tools/NV/Map/Export Map Collision")]` |
| 2 | 씬에서 맵 소스 찾기 | `MapExport.cs:104` — `FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)` 를 전수 스캔해 **처음 만난** `INetworkMapSource` 하나를 돌려준다 |
| 3 | 박스 목록 얻기 | `MapExport.cs:18` — `CollisionBoxes` 가 비면 `ComputeCollision()`. 후자는 지오메트리를 만들지 않고 레이아웃만 다시 풀어 `_collisionBoxes` 를 채운다 (`BackroomsMapGenerator.cs:169`, `AddBox` 가 `_collisionOnly` 일 때 GameObject 생성을 건너뛴다 — `BackroomsMapGenerator.cs:924`) |
| 4 | 스폰 얻기 | `MapExport.cs:24` → `GetSpawns`. 제너레이터는 스폰 룸의 링에서 최대 8개를 뽑고 방 중심을 바라보게 yaw 를 계산한다 (`BackroomsMapGenerator.cs:185`) |
| 5 | 격자 붙이기 | `MapExport.cs:79` `AttachGrid` — `BuildGrid()` → `TryValidate()` → `MapGridBuilder.MarkFreeFloor(grid, data.ToCollisionWorld())` |
| 6 | 파일 쓰기 | `MapCollisionExporter.cs:46` — 경로는 `Application.dataPath + "../../NVserver/MapData" + MapName + ".json"`, `Directory.CreateDirectory` 후 `File.WriteAllText` (UTF-8 no BOM), 결과는 `Debug.Log` 한 줄 |

직렬화는 손으로 쓴 `StringBuilder` 다 (`MapCollisionExporter.cs:65`). `Shared` 가 NuGet 을 참조할 수
없어 `System.Text.Json` 을 쓸 수 없기 때문이며, float 은 왕복 보존 형식 `"R"`, 격자 셀은 base64 다.

**역할 분담은 깔끔하다.** 레벨 생성기가 `Standable`/`StairLink` 만 채우고, `FreeFloor` 는
`MapExport` 가 서버의 플레이어 박스로 계산해 덧붙인다 (`MapGridBuilder.cs:38`, `INetworkMapSource.cs:35`).
그 플래그의 뜻이 "서버가 여기에 플레이어를 놓아도 밀려나지 않는다" 이므로 판정이 `Shared` 에 있어야
한다는 논리다.

---

## 2. Export 데이터(JSON) 구조

`Shared/Collision/MapData.cs` 와 `MapGridData.cs` 가 스키마이고, JSON 키는 camelCase
(`JsonDefaults.cs:22` 의 `JsonNamingPolicy.CamelCase`).

```
{
  "name":   string
  "boxes":  [ { minX, minY, minZ, maxX, maxY, maxZ } ]     // float, 형식 "R"
  "spawns": [ { x, y, z, yaw } ]                            // y 는 발밑, yaw 는 라디안 0=+Z
  "grid": {                                                 // 없을 수 있다 (필드 자체를 생략)
    "floors", "width", "depth":     int
    "cellSize", "floorHeight":      float
    "originX", "originZ":           float                   // 셀 (0,0) 의 바깥 모서리
    "cells":                        base64                  // 셀당 MapCellFlags 1바이트
  }
}
```

`MapCellFlags` 는 8비트 중 3개를 쓴다 (`MapGridData.cs:13`): `Standable`(1), `FreeFloor`(2),
`StairLink`(4).

**실제 파일 (`NVserver/MapData/`, 실측):**

| 파일 | `name` | 박스 | 스폰 | 격자 | 크기 | `Game:Maps` 등록 |
|---|---|---|---|---|---|---|
| `backrooms.json` | `backrooms` | 736 | 8 | 2층 35×35 = 2450셀 | 74 KB | ✅ `default` |
| `test-room.json` | `test-room` | 10 | 8 | 없음 (정상) | 1.4 KB | ✅ `test-room` |
| `backrooms2f.json` | `backrooms2f` | 735 | 8 | — | 69 KB | ❌ **고아** |
| `arena.json` | `arena` | 10 | — | 없음 | 1.4 KB | ❌ **고아** |

`arena.json` 은 손으로 쓴 파일이다 (박스가 논리 그룹별로 빈 줄로 나뉘어 있고 float 이 `-20.0`
형식이다). 어느 export 경로도 이 파일을 만들지 않는다.

---

## 3. 서버가 Collision 데이터를 쓰는 방식

```
appsettings.json  Game:Maps: { "default": "../MapData/backrooms.json", "test-room": "..." }
        │
        ▼  기동 시 1회, 실패하면 기동 중단
ModuleRegistration.LoadMaps          ModuleRegistration.cs:213
        │  맵 id → 경로 → MapLoader.Load
        ▼
MapLoader.Load → Validate            MapLoader.cs:15,51
        │  박스 0개 / min>max / 스폰 0개 / 격자 불일치 → 예외
        ▼
WorldMap                             WorldMap.cs:14
        ├── Collision : CollisionWorld(Aabb[])     — 이동·사격 판정
        ├── Hash      : uint (ComputeHash)         — Welcome 에 실려 나간다
        └── Grid      : MapGrid | null             — FreeFloor 셀 좌표를 로드 시 1회 사전 계산
                                                     (MapGrid.cs:21)
```

- **이동/사격**: `CollisionWorld.MoveBox` / `Depenetrate` / `SweepEarliest` / `Raycast` 가 전부
  `Aabb[]` 를 **선형으로 훑는다.** 브로드페이즈가 없다 (`CollisionWorld.cs:7`).
- **격자를 요구하는 곳**: 목표물 배치와 피격 순간이동. `MapGrid.TryRandomFreeFloor`,
  `TryNearestFreeFloor`. 격자가 없으면 `WorldMap.Grid` 가 `null` 이고 그쪽에서 거절한다
  (`WorldMap.cs:31`). 봇도 격자를 쓴다 (`Modules/Realtime/Simulation/Bots/BotMind.cs`).
- **해시 대조**: 서버가 `Welcome` 에 `MapHash` 를 싣고, 클라이언트가
  `MapExport.BuildMapData(_map).ComputeHash()` 로 자기 값을 계산해 비교한다
  (`NetworkBootstrap.cs:395`). 불일치는 `Debug.LogError` + `NetSession.ReportMapHashMismatch`.
- **테스트**: `tests/Modules.Tests/Realtime/ExportedMapTests.cs` 가 실제 파일을 로드해 스폰 매몰,
  착지, 전진 60틱 관통, 격자 정합, `FreeFloor ⊆ Standable`, 층별 `FreeFloor` 존재,
  무작위 질의 500회를 서버의 충돌 코드로 검산한다.

---

## 4. 지금 구조에서 지켜야 할 것

개선안이 이것들을 깨지 않아야 한다.

1. **export 와 런타임 해시 검증이 같은 함수를 지난다** (`MapExport.BuildMapData`). 두 경로가
   서로 다른 계산을 하면 검증이 아무것도 잡지 못한다 — `MapExport.cs:9` 가 이유를 적어 두었다.
2. **`FreeFloor` 판정이 `Shared` 에 있고 서버의 플레이어 박스로 계산된다.** 원래
   `Physics.CheckCapsule`(반지름 0.32) 이었는데, 서버 박스(0.4)보다 작아서 서버가 밀어낼 자리를
   통과시켰다 (`MapGridBuilder.cs:14-25`).
3. **격자 없음이 정상 표현이고, 해시에는 있을 때만 들어간다** (`MapData.cs:56`). 격자를 도입한
   커밋에서 기존 맵 전부를 재export 하지 않아도 됐던 이유다.
4. **base64 셀 + `"R"` float.** 크기와 비트 정확성이 둘 다 해결돼 있다 (`conventions.md:198`).
5. **못 읽는 맵으로는 기동하지 않는다** (`MapLoader.cs:11`).
6. **`ExportedMapTests` 가 실제 파일을 시뮬레이션으로 검산한다.** 특히
   `모든_층에_몸이_들어가는_셀이_있다` 는 float 왕복 오차 회귀를 잡는 테스트다.

---

## 5. 문제점

### P1 — 씬 스캔이 비결정적이고, 이름이 충돌한다 (심각)

`MapExport.FindInScene()` 은 `FindObjectsSortMode.None` 으로 훑어 **처음 만난 하나**를 쓴다
(`MapExport.cs:106`). 그 순서는 Unity 가 규정하지 않는다.

그런데 `INetworkMapSource` 구현체가 **`MapName` 이 같은 채로 둘** 있다.

| 구현체 | `MapName` | `BuildGrid()` |
|---|---|---|
| `BackroomsMapGenerator` (`SampleScene` 이 실제로 쓰는 것, guid `9d287251…`) | `"backrooms"` (`:130`) | 격자를 만든다 (`:313`) |
| `BackroomsMap` (레거시, 어느 씬도 참조하지 않음) | `"backrooms"` (`:114`) | **`=> null`** (`:203`) |

두 컴포넌트가 한 씬에 있으면 export 는 **격자 없는 `backrooms.json` 을 쓸 수 있다.** 격자 없는
맵도 서버가 정상 로드하므로(`MapData.cs:22`) 기동은 성공하고, 증상은 "열쇠도 문도 없는 매치" 로만
나타난다. `SampleScene.unity` 는 지금 제너레이터만 참조하므로 현재는 사고가 나지 않지만, 그것을
막고 있는 것은 **코드가 아니라 씬 파일의 현재 상태**다.

### P2 — 고아 맵 파일이 살아 있는 파일과 구별되지 않는다

`backrooms2f.json`(735박스) 과 `arena.json`(10박스) 은 `Game:Maps` 에 없고, 어느 씬도
`backrooms2f` 를 만들지 않는다. `SessionSceneRouter.SceneByMap` 에도 없다. `arena` 는
`RoomRegistryTests.cs:340`, `CollisionTests.cs:199` 가 **이름 문자열만** 쓰고 파일은 읽지 않는다
(합성 `MapData` 를 만든다).

파일만 보고 어느 것이 살아 있는지 알 수 없다. `docs/match-authority-plan.md` §3·§9 가 이미 삭제를
계획해 두었으므로 이 작업과 합쳐야 한다.

### P3 — export 가 서버 등록으로 이어지지 않는다

새 맵을 export 해도 `Game:Maps` 에 손으로 넣어야 한다. 넣지 않으면 방 생성이 거절되고
(`conventions.md:216` — 등록 안 된 맵 id 는 기본 맵으로 열지 않고 거절한다),
export 를 한 사람은 자기 파일이 왜 안 먹는지 알 수 없다. `backrooms2f` 가 정확히 이 경로로 죽었다.

또한 **서버는 기동 시 로드한 맵을 로그에 남기지 않는다.** `LoadMaps`(`ModuleRegistration.cs:213`)
에도 `Program.cs` 에도 로그가 없다. `conventions.md:218` 이 그것을 요구하는데 구현되어 있지 않다.

### P4 — 검증이 비대칭이다: 서버가 하는 검사를 export 는 하지 않는다

`MapLoader.Validate` 는 네 가지를 검사한다(박스 0개, `min>max`, 스폰 0개, 격자 불일치).
export 쪽은 그중 **격자 `TryValidate` 하나만** 한다 (`MapExport.cs:90`). 나머지는 통과해서 파일이
되고, 서버 기동 실패나 `dotnet test` 실패로 한참 뒤에 드러난다.

특히 아까운 것: `MarkFreeFloor` 는 표시한 셀 수를 돌려주고, 그 반환값의 존재 이유를
`MapGridBuilder.cs:37` 이 "0 이면 격자나 콜리전이 어긋났다는 신호이고, export 쪽에서 그것을 보고
멈출 수 있다" 로 적어 두었다. **호출자가 그 값을 버린다** (`MapExport.cs:97`).

`ExportedMapTests` 가 서버 쪽에서 하는 검사(스폰 매몰, 착지, 층별 `FreeFloor`) 는 전부 `Shared`
코드만 쓰므로 **Unity 에서도 그대로 돌 수 있는데** export 시점에 돌지 않는다.

### P5 — 되돌릴 수 없는 덮어쓰기

`File.WriteAllText` 가 무조건 덮어쓴다. 기존 파일의 해시와 비교하지 않고, 무엇이 바뀌는지 보여주지
않고, 백업도 없다. 잘못된 씬을 열어 놓고 메뉴를 한 번 누르면 커밋된 좋은 맵이 사라진다(git 이
받쳐 주기는 하지만, 그것이 유일한 안전망이다).

내용이 같아도 다시 쓴다 → 파일 mtime 이 흔들려 git 이 조용해도 Unity/서버 재빌드가 돈다.

### P6 — Editor Tool 의 UI 가 없다

메뉴 클릭 = 즉시 쓰기. 어느 소스가 선택됐는지, 어디에 쓰는지, 박스/스폰/격자가 어떻게 되는지 **쓴
뒤에야** 알 수 있다. 여러 맵을 export 하려면 씬을 하나씩 열어야 한다(배치 export 없음).

### P7 — 결과 보고가 한쪽으로 기울어 있다

실패(소스를 찾지 못함)만 `EditorUtility.DisplayDialog` 로 알리고, **성공은 `Debug.Log` 한 줄**이다
(`MapCollisionExporter.cs:60`). 그 줄에는 필요한 정보가 다 들어 있는데(경로, 박스, 스폰, 격자,
해시) 콘솔에만 있어서 사람이 놓친다.

### P8 — 같은 무거운 계산이 런타임에도 돈다

`MarkFreeFloor` 는 2450셀 각각에 `Depenetrate` 를 부르고, `Depenetrate` 는 최대 4회 반복 ×
736박스를 선형으로 훑는다 (`CollisionWorld.cs:94`, `SimConstants.MaxDepenetrationIterations = 4`).
상한 **2450 × 4 × 736 ≈ 720만 회** AABB 겹침 검사다. export 에서 1회면 문제가 없다.

문제는 **같은 경로가 런타임에 두 번 더 돈다**는 것이다.

- `NetworkBootstrap.OnWelcome` → `MapExport.BuildMapData(_map).ComputeHash()`
  (`NetworkBootstrap.cs:395`). 접속 프레임에 돈다.
- `MatchManager.OfflineGrid()` → `MapExport.BuildMapData(map)` (`MatchManager.cs:912`).
  오프라인 매치 시작마다 돈다.

둘 다 `AttachGrid` 를 통째로 다시 밟는다. WebGL 단일 스레드에서 접속 순간의 프레임 스파이크다.
캐시가 없다.

### P9 — 서버 충돌 구조의 전제가 이미 틀어졌다

`CollisionWorld.cs:7` 이 "브로드페이즈가 없다 — 맵 1개, 박스 수십 개 규모다" 라고 적어 두었는데,
`backrooms` 는 **736박스**다. 이동 한 번은 `Depenetrate`(≤4회 스캔) + `SweepEarliest`(≤4회 스캔) 로
박스를 8번쯤 훑는다. 30Hz × 8명이면 초당 대략 **17만 회** 박스 순회, 사격 레이캐스트와 지연 보상
되감기는 그 위에 얹힌다. 지금 돌아가고는 있지만, 박스 수를 늘리는 방향(층 추가, 프랍 포함)의
개선은 이 구조를 먼저 손대야 가능하다.

### P10 — 스키마 버전이 없다

맵 파일에 `version` 이 없다. `ProtocolInfo.Version` 은 와이어 프로토콜용이고 파일과 무관하다.
스키마를 늘렸을 때 옛 파일과 새 서버를 구별할 방법이 없다 — 해시는 내용 대조용이라 호환성을 말하지
못한다. 새 필드를 옵셔널로만 늘려 가면 조용히 기본값으로 로드되고, 증상은 그 기능이 안 도는 것이다.

### P11 — 직렬화가 손으로 쓴 문자열이라 스키마와 갈릴 수 있다

`MapCollisionExporter.Serialize`(`:65`) 와 `AppendGrid`(`:115`) 가 키 이름을 문자열 리터럴로 쓴다.
`MapData` 에 프로퍼티를 하나 추가하고 이쪽을 잊으면 **컴파일러가 아무 말도 하지 않고** 서버는 그
필드를 기본값으로 읽는다. camelCase 규약도 여기서는 리터럴로만 유지된다.

### P12 — 씬에 손으로 놓은 Collider 는 서버가 전혀 모른다

export 대상은 `INetworkMapSource` 구현체 하나뿐이고, 그 구현체는 **코드가 `AddBox` 로 등록한
박스만** 내놓는다 (`BackroomsMapGenerator.cs:924`, `TestRoomMap.cs:190`). 씬에 프랍이나 기물을
직접 놓는 순간 클라이언트에는 벽이 있고 서버에는 없다. 증상은 "아무것도 없는데 막힘" /
"벽을 통과함" 이며, 맵 해시는 **일치한다** — 해시는 export 된 목록만 보므로 export 되지 않은
지형을 잡을 수 없다.

### P13 — Play 모드 가드와 `randomizeSeed` 가드가 없다

Play 중에 메뉴를 누르면 `CollisionBoxes`(런타임 목록) 를 쓴다. `randomizeSeed` 가 켜져 있으면 그
층은 그 세션 한정이다. 기본값은 `false` 이고 그 이유가 필드 툴팁에 적혀 있지만
(`BackroomsMapGenerator.cs:45`), **씬에 직렬화된 값이 코드 기본값을 이긴다.** 게다가
`ComputeCollision()` 자신이 그 플래그를 다시 읽어 씨드를 새로 뽑는다 (`:169-172`) — edit 모드
export 에서도 `randomizeSeed` 가 켜져 있으면 **매번 다른 지형**이 나온다.

### P14 — 테스트의 맵 목록이 하드코딩이다

`ExportedMapTests.Maps`(`:23`) 와 `GriddedMaps`(`:120`) 가 파일명을 직접 적는다. 새 맵을 export
해도 검사되지 않고, `Game:Maps` 와도 연동되지 않는다. `MapData/` 디렉터리를 훑지 않으므로 P2 의
고아 파일도 테스트가 알려주지 않는다.

---

## 6. 개선안

### 6.1 Export Pipeline

**A1. 파이프라인을 단계로 쪼갠다.** `MapCollisionExporter` 는 UI 껍데기로 남기고,
찾기 → 만들기 → 검증 → 쓰기 → 보고를 `MapExportPipeline`(`Assets/Editor/Map/`) 의 순수 함수로
분리한다. 이유는 테스트다 — `Assets/Editor/Tests/` 의 EditMode 스위트가 검증 단계만 골라 부를 수
있어야 한다(그 폴더는 별도 asmdef 없이 `Assembly-CSharp-Editor` 안에서 돌므로 접근이 된다).

**A2. 소스 선택을 명시적으로 실패시킨다.** `FindInScene` 을 `FindAllInScene(List<>)` 로 바꾸고,

- 0개 → 지금처럼 거절 (문구 유지)
- 2개 이상 → **거절**하고 각 컴포넌트의 GameObject 경로와 `MapName` 을 나열한다
- `MapName` 이 같은 둘이 있으면 그 사실을 따로 지목한다

P1 을 코드로 막는 최소 변경이다. "첫 번째를 쓴다" 는 규정되지 않은 순서에 기대는 것이므로 유지할
가치가 없다.

**A3. 쓰기를 원자적으로, 그리고 내용이 같으면 쓰지 않는다.** `.tmp` 에 쓴 뒤 `File.Replace`.
쓰기 전에 기존 파일을 로드해 `ComputeHash()` 를 비교하고 같으면 "변경 없음" 으로 끝낸다. mtime 이
흔들리지 않아 재빌드와 git 이 조용해진다.

**A4. 출력 경로를 확인한다.** `../../NVserver/MapData` 는 리포지터리 배치를 가정한 하드코딩이고,
없으면 `Directory.CreateDirectory` 로 **조용히 새로 만든다** — 배치가 다르면 엉뚱한 곳에 맵이
생긴다. 부모에 `NVserver/Api/appsettings.json` 이 있는지 확인하고 없으면 거절한다.

**A5. `Game:Maps` 등록 여부를 읽어서 알려준다.** export 후 `NVserver/Api/appsettings.json` 을
읽어 그 맵 id 가 있는지 확인하고, 없으면 경고 + 붙여 넣을 JSON 조각을 콘솔과 창에 띄운다.
**자동 편집은 하지 않는다** — 에디터가 서버 설정을 고치는 것은 되돌리기 어렵고, `SceneByMap`·
`Game:Maps`·`MapName` 세 곳이 맞아야 한다는 사실은 사람이 알고 있어야 한다.

**A6. (선택) 배치 export.** `Tools ▸ NV ▸ Map ▸ Export All Maps` 가 (씬, 맵 id) 표를 돌며 씬을 열고
export 한다. 표를 새로 만들지 않는다 — `SessionSceneRouter.SceneByMap`(`:29`) 이 이미 그 표이므로,
그것을 한 곳(전용 `ScriptableObject` 또는 `Shared` 상수)으로 옮기고 라우터와 export 가 같은 것을
읽게 한다. 표가 둘이면 갈린다.

### 6.2 Export 대상 및 데이터 구조

**B1. 스키마 버전.** `"version": 1` 을 최상위에 추가한다. `MapLoader` 는 필드가 없으면 1로 보고,
자기가 아는 최대 버전보다 크면 **기동을 실패시킨다.** 해시에는 넣지 않는다 — 넣으면 이 커밋에서
모든 맵을 재export 해야 하고, 그 재export 는 아무 정보도 늘리지 않는다(격자를 해시에 조건부로 넣은
것과 같은 논리, `MapData.cs:56`).

**B2. 출처 메타데이터.** `sourceScene`, `mapSourceType`, `seed`, `exportedAtUtc`, `exporterVersion`.
**해시에 넣지 않는다** — 넣으면 재export 마다 해시가 바뀌어 맵 해시가 "같은 지형인가" 를 말하는
기능을 잃는다. 목적은 파일만 보고 "이게 어디서 왔나" 를 아는 것이고, 그것만으로 P2 의 절반이
해결된다.

**B3. 씬 Collider 를 대상에 포함한다 (P12).** 전용 마커 컴포넌트(`NVCollisionVolume`) 가 붙은
`BoxCollider` 를 모아 박스 목록에 더한다. 마커를 요구하는 이유는 명시성이다 — 씬의 모든
Collider 를 자동으로 긁으면 뷰모델·트리거·프랍 콜라이더가 지형이 된다.

**회전한 박스는 거절한다.** 스키마가 AABB 이므로 OBB 를 표현할 수 없고, 회전한 콜라이더를 AABB 로
감싸면 클라이언트가 안 막는 곳을 서버가 막는다. export 단계에서 "축 정렬만 지원한다" 를 말해야
하고, 그 사실을 나중에 "왜 여기서 걸리지" 로 만나게 두면 안 된다.

**B4. (선택) 박스 목록 정규화.** `(minX, minY, minZ)` 순으로 정렬하면 해시가 생성 순서에 흔들리지
않고, 6.5의 브로드페이즈가 인덱스 구간으로 버킷을 표현할 수 있다. **해시가 바뀌므로 전 맵
재export 와 같은 커밋에서만** 한다. `MapGrid._freeFloor` 의 순서 의존(`MapGrid.cs:51`) 은 격자
순회 순서이므로 영향받지 않는다.

**B5. 격자 플래그는 늘리지 않는다.** 8비트 중 5비트가 남아 있지만 셀당 1바이트 전제
(`MapGridData.cs:12`) 는 지킨다. 지금 필요한 플래그가 없으므로 추가하지 않는다.

### 6.3 Unity Editor Tool UI/UX

**C1. `EditorWindow` 하나** (`Tools ▸ NV ▸ Map ▸ Map Export). 메뉴 항목은 창을 연다.

```
┌ Map Export ───────────────────────────────────────────────┐
│ 씬: SampleScene                                            │
│ 맵 소스: Backrooms / BackroomsMapGenerator   [1개 발견]    │
│   MapName  backrooms      seed 0   randomizeSeed  off      │
│                                                            │
│ 출력  …/NVserver/MapData/backrooms.json          [열기]   │
│ 기존 해시  3F2A9C41      새 해시  3F2A9C41  → 변경 없음   │
│ 박스 736   스폰 8   격자 2층 35×35  Standable 1204        │
│                                     FreeFloor  1130        │
│                                                            │
│ 검증                                                       │
│  ✅ 박스·스폰·격자 정합       ✅ 모든 스폰이 지형 밖       │
│  ✅ 층마다 FreeFloor 있음     ⚠️ Game:Maps 에 등록 없음    │
│                                                            │
│              [ 검증만 ]   [ Export ]   [ 조각 복사 ]      │
└────────────────────────────────────────────────────────────┘
```

- `Export` 는 **오류 0개일 때만** 활성. 경고는 확인 후 진행.
- 기존 해시 vs 새 해시를 쓰기 전에 보여준다 — P5 가 여기서 사라진다.

**C2. Play 모드와 `randomizeSeed` 가드 (P13).** Play 중이면 "런타임 목록으로 export 한다" 를
명시하고, `randomizeSeed` 가 켜져 있으면 **거절**한다. 이 조합에서 나온 파일은 재현되지 않는다.

**C3. 결과를 양쪽에.** 성공도 창에 남기고 콘솔에도 남긴다. 지금의 `Debug.Log` 한 줄은 내용이
좋으므로 문구를 그대로 쓴다.

**C4. 격자 미리보기.** Scene view `Handles` 로 `Standable`/`FreeFloor`/`StairLink` 를 층별로 색칠해
그린다. 좌표계 어긋남(반 셀 밀림, `CellIndex` 축 뒤바뀜)은 숫자로는 보이지 않는데
`MapGridData.cs:70` 과 `:110` 이 그 둘을 실제 함정으로 기록해 두었다. 눈으로 한 번 보는 것이
가장 싸다.

### 6.4 Validation

**D1. 검증을 `Shared` 로 옮겨 한 곳에 둔다.** `MapDataValidator.TryValidate(MapData, out
IReadOnlyList<string> errors)`. `MapLoader.Validate`(`:51`) 는 이것을 부르는 껍데기가 되고,
export 는 쓰기 전에 같은 함수를 부른다. 검사를 두 곳에 쓰면 갈리고, 갈리면 export 가 통과시킨
파일이 기동을 멈춘다 — 그게 지금 상태다.

**D2. export 전용 추가 검사.** 아래는 전부 `Shared` 코드만 쓰므로 Unity 에서 돈다.

| 검사 | 근거 | 지금 어디서 잡히나 |
|---|---|---|
| 모든 스폰이 `MapGridBuilder.IsFree` 통과 | 스폰 매몰 = "스폰 직후 벽에 끼임" | `ExportedMapTests.cs:43` (서버 테스트) |
| 스폰에서 중립 입력 10틱 후 `IsGrounded`, \|y\| < 0.05 | 바닥 없음 = "끝없이 떨어짐" | `ExportedMapTests.cs:61` |
| `MarkFreeFloor` 반환값 > 0 | `MapGridBuilder.cs:37` 이 이 용도로 반환한다 | **아무도 안 잡는다** (`MapExport.cs:97` 이 버린다) |
| **층마다** `FreeFloor` > 0 | float 왕복 오차 회귀 — 증상이 "위층에만 목표물이 안 생김" | `ExportedMapTests.cs:212` |
| `FreeFloor ⊆ Standable` | 서지 못하는 칸에 배치 | `ExportedMapTests.cs:149` |
| 스폰 개수 == 8 | `Room.MaxPlayers` 와 짝 | `ExportedMapTests.cs:38` |
| `MapName` == 파일명 == `SceneByMap` 키 == `Game:Maps` 키 | P1·P3 이 여기서 생겼다 | 아무도 안 잡는다 |
| 박스 수가 임계 초과면 **경고** | 브로드페이즈 없음(P9) 을 사람이 보게 | 아무도 안 잡는다 |

이 표의 요점은 **새 검사를 발명하는 게 아니라, 서버 테스트가 이미 하는 검사를 export 시점으로
앞당기는 것**이다. `dotnet test` 는 맵을 커밋한 뒤에 도는데, export 는 커밋 전이다.

**D3. 회귀 테스트를 양쪽에 둔다.**

- Unity: `Assets/Editor/Tests/MapExportValidationTests.cs` — 합성 `INetworkMapSource`(정상 /
  스폰 매몰 / 격자 크기 불일치 / 격자 없음)로 검증 단계를 검사.
- 서버: `ExportedMapTests.Maps` 의 하드코딩을 버리고 **`MapData/*.json` 을 훑는다.** 새 맵이
  자동으로 검사 대상이 되고, 등록되지 않은 고아 파일도 그때 드러난다(P2, P14). 격자 유무는
  파일에서 읽어 분기하면 되므로 `GriddedMaps` 목록도 없어진다.
- 스키마 드리프트 방어(P11): 리플렉션으로 `MapData`/`MapGridData` 의 프로퍼티 목록을 읽어
  export 된 JSON 에 그 camelCase 키가 **모두** 있는지 검사하는 EditMode 테스트. 필드를 늘리고
  쓰기 코드를 잊으면 여기서 걸린다.

**D4. 서버 기동 로그 (P3).** `LoadMaps` 가 로드한 맵을 전부 남긴다 — 맵 id, 파일 경로, 박스 수,
스폰 수, 격자 유무, 해시. `conventions.md:218` 이 이미 요구하는 항목이고 구현되어 있지 않다.
클라이언트가 보고한 해시를 어느 맵과 비교할지 알려면 이 로그가 필요하다.

### 6.5 서버 충돌 계산을 고려한 데이터 구조

**E1. 런타임 중복 계산 제거 (P8) — 먼저 할 것.** `MapData` 를 씬 수명 동안 한 번만 만들어
캐시한다(`MapExportCache` 또는 `INetworkMapSource` 쪽 지연 필드). `NetworkBootstrap.OnWelcome`
(`:395`) 과 `MatchManager.OfflineGrid()`(`:912`) 가 같은 인스턴스를 쓴다. 코드 변경이 작고
위험이 낮으며, 접속 프레임의 스파이크가 사라진다.

**E2. `CollisionWorld` 브로드페이즈 (P9).** 맵 로드 시 균일 격자로 박스 버킷을 만들고,
`SweepEarliest`/`Depenetrate` 가 대상 AABB 가 닿는 버킷만 훑는다. 셀 크기는 격자의 `cellSize`(3m)
를 그대로 쓰면 될 후보다.

**결정성이 이 작업의 전부다.** 클라이언트 예측이 비트 동일을 요구하므로:

- `Depenetrate`(`CollisionWorld.cs:98`) 는 박스를 **순차적으로** 밀어내므로 순회 순서가 결과를
  바꾼다. `SweepEarliest` 도 `tEnter` 동률에서 순서 의존이다.
- 따라서 버킷 안에서는 **박스 인덱스 오름차순**으로 순회해야 지금과 같은 결과가 나온다.
- 착수 순서: **먼저** "기존 맵에서 이동 결과가 브로드페이즈 전후로 비트 동일" 을 확인하는 테스트를
  쓰고, 그 다음에 브로드페이즈를 넣는다. 반대로 하면 미세한 차이가 "특정 위치에서만 캐릭터가 튐"
  으로만 나타난다.

**E3. `MarkFreeFloor` 도 같이 빨라진다.** E2 를 넣으면 export 의 720만 회 검사가 버킷 크기에 비례해
줄어든다. 별도 작업이 아니다.

**E4. 파생 가능한 값은 파일에 넣지 않는다.** 맵 경계 AABB, 층별 바닥 y, 박스별 층 인덱스는
전부 로더가 계산할 수 있다. 리포지터리 규약(`CLAUDE.md`: "다른 값에서 도출할 수 있는 값을 다시
적지 않는다")을 따르고, 적어 두면 그 값이 박스 목록과 갈릴 수 있다.

### 6.6 성능 및 유지보수

| 항목 | 지금 | 개선 |
|---|---|---|
| export 시 `MarkFreeFloor` | 최대 720만 AABB 검사 | E2 의 브로드페이즈 재사용 |
| 접속 시 해시 계산 | 접속 프레임에 전체 재계산 | E1 캐시 |
| 오프라인 매치 시작 | 매 시작 재계산 | E1 캐시 |
| 파일 쓰기 | 항상 덮어씀 | A3 해시 비교 + 원자적 교체 |
| 직렬화 | 손으로 쓴 문자열, 컴파일러 검사 없음 | D3 의 리플렉션 테스트로 드리프트 방어 |
| 출력 경로 | 하드코딩, 없으면 새로 만듦 | A4 리포지터리 확인 후 거절 |
| 고아 파일/레거시 코드 | `backrooms2f.json`, `arena.json`, `BackroomsMap.cs` | F1 정리 |
| 문서 | — | 확정된 규칙을 `conventions.md` 에 증상→원인→대응으로 기록 |

**F1. 정리 대상.** `BackroomsMap.cs`(+`.meta`) 는 어느 씬도 참조하지 않으면서 P1 의 절반을
만들고 있다. `backrooms2f.json` 은 이제 존재하지 않는 레벨의 export 다. `arena.json` 은 어느
경로도 만들지 않고 어느 테스트도 읽지 않는다. 삭제 판단은 `docs/match-authority-plan.md` §3·§9 와
합쳐서 한 번에 한다.

---

## 7. 단계별 구현 계획

**해시가 바뀌는 단계와 안 바뀌는 단계를 나눈 것이 이 순서의 핵심이다.** 해시가 바뀌면 그 커밋에서
모든 맵을 재export 해야 하고, 재export 는 Unity 에디터를 열어야 하는 수동 단계다.

### Phase 0 — 사고 경로 차단 (해시 불변, 반나절)

1. `FindAllInScene` + 2개 이상/이름 중복 거절 (A2) — P1
2. Play 모드·`randomizeSeed` 가드 (C2) — P13
3. `MarkFreeFloor` 반환값 확인, 0이면 export 중단 (D2) — P4
4. 출력 경로 확인 (A4)
5. 고아 파일·레거시 코드 정리 (F1) — P2

전부 "실패시키기" 와 "삭제" 뿐이다. 스키마도 계산도 건드리지 않으므로 재export 가 필요 없다.

### Phase 1 — 검증 통합 (해시 불변, 하루)

1. `MapDataValidator` 를 `Shared` 로, `MapLoader.Validate` 가 그것을 부르게 (D1)
2. export 전 검증 + D2 표의 추가 검사
3. `ExportedMapTests` 를 `MapData/*.json` 디렉터리 훑기로 (D3) — P14
4. `Assets/Editor/Tests/` 에 검증 단계 테스트 (D3)
5. 서버 기동 시 맵 로그 (D4) — P3

### Phase 2 — Editor Tool (해시 불변, 하루)

1. `EditorWindow` (C1) — P6, P7
2. 해시 비교 + 원자적 쓰기 + 변경 없으면 쓰지 않기 (A3) — P5
3. `Game:Maps` 등록 확인 + 조각 출력 (A5) — P3
4. 격자 Scene view 미리보기 (C4)

### Phase 3 — 스키마 (해시 불변, 반나절)

1. `"version": 1` + 로더의 미래 버전 거절 (B1) — P10
2. 출처 메타데이터 (B2) — P2
3. 스키마 드리프트 리플렉션 테스트 (D3) — P11

세 항목 모두 **해시에 들어가지 않도록** 설계했으므로 재export 없이 들어간다. 다만 기존 파일에는
새 필드가 없으므로, 한 번은 전 맵을 재export 해 메타데이터를 채우는 것이 좋다(선택).

### Phase 4 — 성능 (하루~)

1. **E1 런타임 캐시 먼저.** 작고, 위험이 낮고, 접속 스파이크가 사라진다 — P8
2. 브로드페이즈 전후 비트 동일 테스트를 **먼저** 쓴다
3. `CollisionWorld` 균일 격자 브로드페이즈 (E2) — P9

### Phase 5 — 선택 (해시가 바뀐다 → 전 맵 재export 필수)

1. 씬 Collider 포함 + 회전 거절 (B3) — P12
2. 박스 목록 정렬 (B4)
3. 배치 export + `SceneByMap` 표 단일화 (A6)

### 우선순위 근거

| 우선 | 항목 | 왜 지금 | 비용 | 위험 |
|---|---|---|---|---|
| 1 | P1 소스 중복 (Phase 0) | 격자 없는 맵을 조용히 쓸 수 있고, 지금 막고 있는 것은 씬 파일의 상태뿐 | 작음 | 낮음 |
| 1 | P4 검증 비대칭 (Phase 0·1) | export 가 통과시킨 파일이 서버 기동을 멈춘다. 잡을 코드가 이미 `Shared` 에 있다 | 중간 | 낮음 |
| 2 | P5 덮어쓰기 / P6 UI (Phase 2) | 사람이 실수하는 지점이고, 실수 비용이 커밋된 맵이다 | 중간 | 낮음 |
| 2 | P8 런타임 중복 (Phase 4-1) | 접속 프레임 스파이크. 캐시 하나로 끝난다 | 작음 | 낮음 |
| 3 | P3 등록 누락 / P14 테스트 (Phase 1) | `backrooms2f` 가 이 경로로 죽었다 | 작음 | 낮음 |
| 3 | P10 버전 / P11 드리프트 (Phase 3) | 스키마를 늘리기 전에 넣어야 값이 있다 | 작음 | 낮음 |
| 4 | P9 브로드페이즈 (Phase 4-3) | 지금 돌아가고는 있다. 결정성 위험이 실질적이다 | 큼 | **높음** |
| 4 | P12 씬 Collider (Phase 5) | 지금 씬은 프랍을 놓지 않는다. 놓기 시작하면 1순위로 올라간다 | 중간 | 중간 |

---

## 8. 하지 않을 것

해커톤 규모에서 비용이 이득을 넘는 것들. 각각 왜 아닌지를 적어 둔다.

- **메시/삼각형 수준 export** — 서버가 AABB 스윕으로 판정하는 전제 자체를 바꾼다. 이 프로젝트의
  레벨은 박스로 만들어지므로 얻는 것이 없다.
- **OBB(회전 박스) 지원** — 스키마, 스윕, 되감기, 클라이언트 예측이 전부 바뀐다. 회전한 콜라이더를
  **거절**하는 것(B3)이 같은 문제의 1% 비용 해결책이다.
- **BVH / 옥트리** — 균일 격자로 충분하고, 결정적 순회를 보장하기가 훨씬 쉽다.
- **증분/델타 export, 바이너리 맵 포맷** — 74 KB 파일이다. 사람이 읽을 수 있는 것이 디버깅에서 더
  값어치가 크고, `MapLoader` 는 주석과 꼬리 콤마까지 허용해 손 편집을 지원한다
  (`JsonDefaults.cs:24`).
- **`appsettings.json` 자동 편집** — 에디터가 서버 설정을 고치는 것은 되돌리기 어렵다. 조각을
  출력해 사람이 붙이게 한다.
- **서버가 레벨을 생성** — 씨드를 바꿀 때마다 두 구현이 갈리고 증상은 "가끔 열쇠가 벽 안에 생김"
  이다 (`MapGridData.cs:39` 가 이미 이 결정을 기록해 두었다).
- **`Shared` 에 NuGet/`System.Text.Json` 도입** — Unity(IL2CPP, netstandard2.1) 가 그 어셈블리를
  갖지 않는다. 손으로 쓴 직렬화는 유지하고 **테스트로** 드리프트를 막는다(D3).

---

## 9. 착수 전에 확인이 필요한 것

1. **고아 파일 삭제 여부** — `backrooms2f.json`, `arena.json`, `BackroomsMap.cs`.
   `docs/match-authority-plan.md` §3·§9 가 이미 삭제를 계획하고 있어 그 작업과 겹친다. 어느 쪽에서
   처리할지 정해야 두 번 하지 않는다.
2. **Phase 5 의 재export 타이밍** — 해시가 바뀌는 변경은 Unity 에디터를 열어 모든 맵을 다시
   내보내야 한다. 맵을 늘릴 계획(층 추가, 새 레벨)이 있으면 그때 묶어서 하는 것이 싸다.
3. **씬에 프랍을 놓을 계획이 있는가** — 있으면 P12(B3) 의 우선순위가 1순위로 올라간다. 없으면
   Phase 5 에 남겨 둔다.
4. **브로드페이즈를 이번 범위에 넣을지** — Phase 4-3 은 이 계획에서 유일하게 위험이 높은 항목이고,
   지금 성능이 실제로 문제인지(30Hz 틱 예산 대비 측정값)를 먼저 봐야 한다. 측정 없이 착수하면
   결정성 위험만 사서 얻는 것이 없을 수 있다.
