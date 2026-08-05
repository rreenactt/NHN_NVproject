# Backrooms V2 맵 타입 추가 계획

> **상태: 계획 (미착수).** 아래의 모든 현황 진단은 리포지터리의 코드와 실제 맵 파일을 읽어서
> 확인한 것이며, 근거를 `파일:줄` 또는 `파일 → 멤버` 로 적었다.

대상은 Unity 에디터의 Map Generator (`Tools ▸ NV ▸ Map ▸ Map Generator`) 에 세 번째 맵 타입
**Backrooms V2** 를 추가하는 작업 전체 — 생성 알고리즘, 에디터 툴, 데이터 구조, 서버 연동,
성능까지.

**전제 (요구사항).** V2 는 기존 Backrooms 의 코드·알고리즘·생성 로직을 **참조하지도
재사용하지도 않는다.** 완전히 새로운 구조로 설계하고, 생성 결과는 NVserver 의 멀티플레이
파이프라인(충돌, 워커빌리티 격자, 스폰, 직렬화, 해시 동기화)이 처리하기 쉬운 형태를
목표로 한다.

---

## 1. 격리 경계 — 무엇이 "기존 Backrooms 코드"인가

재사용 금지 대상과 재사용 **필수** 대상을 먼저 가른다. 이 경계가 흐려지면 "V1 을 참조하지
않는다"는 전제가 지켜졌는지 검증할 수 없다.

### 1a. 금지 — V2 가 참조·수정·복사하지 않는 것

| 파일 | 이유 |
|---|---|
| `NVproject/Assets/Scripts/BackroomsMapGenerator.cs` | 레거시 런타임 생성기 (1203줄, `SampleScene` 전용) |
| `NVproject/Assets/Editor/Map/Generators/BackroomsGenerator.cs` | 현행 V1 생성기 — 방 시도 + 복도 체인 + 사후 연결 복구 알고리즘 전체 |
| `NVproject/Assets/Scripts/Map/BackroomsSettings.cs` | V1 파라미터 집합 |
| `NVproject/Assets/Scripts/Map/BackroomsAmbience.cs` | V1 분위기 (안개/조명/험 사운드) |
| `.claude/skills/aesthetic-spec/aesthetic-spec.md` 의 팔레트 | V2 는 다른 장소로 읽혀야 한다 — 모노 옐로 계열을 쓰지 않는다 |
| `.claude/skills/backrooms-map-generator` 스킬 | V1 의 설계 브리프. V2 작업 중 로드하지 않는다 |

"알고리즘을 참조하지 않는다"의 실무적 의미: V1 은 **고정 앵커 방 3개를 찍고, 방 배치를
반복 시도하고, 복도로 체인 연결한 뒤, 도달 불가 지역을 사후에 뚫어서 고치는** 구조다
(`BackroomsGenerator.Solver` — `StampAnchors`/`CarveRooms`/`ConnectRooms`/`EnforceConnectivity`).
V2 는 이 네 단계 중 어느 것도 같은 방식으로 풀지 않는다 (§3).

### 1b. 필수 재사용 — 맵 파이프라인 (Backrooms 코드가 아니라 Map Generator 인프라)

아래는 "기존 Backrooms" 가 아니라 **모든 맵 타입이 공유하는 계약**이며, 이걸 우회하면
오히려 전제(서버가 처리하기 쉬운 구조)를 어긴다. V2 는 전부 그대로 탄다.

| 계층 | 파일 | V2 와의 관계 |
|---|---|---|
| 생성기 계약 | `NVproject/Assets/Editor/Map/Generators/IMapGenerator.cs` | 구현한다. `TypeCache` 자동 발견 (`MapGeneratorRegistry.cs`) — 등록 목록 수정 없음 |
| 중간 표현 | `NVproject/Assets/Scripts/Map/MapBlueprint.cs` | 채운다 (`Pieces`/`Spawns`/`Grid`/`Palette`/`Lights`) |
| 설정 베이스 | `NVproject/Assets/Scripts/Map/MapGeneratorSettings.cs` | 상속한다 (이름/시드/로비 메타 공통 필드) |
| 베이크 | `Assets/Editor/Map/MapBakePipeline.cs`, `MapSceneBuilder.cs`, `MapCatalogWriter.cs` | 수정 없이 사용 |
| 익스포트 | `Assets/Editor/Map/MapExportPipeline.cs`, `Assets/Scripts/Net/MapExport.cs` | 수정 없이 사용 |
| 공유 스키마 | `NVserver/Shared/Collision/` (`MapData`, `MapGridData`, `MapGridBuilder`, `MapDataValidator`) | 수정 없이 사용 — 서버와 같은 코드가 Unity 에서 컴파일된다 |
| 런타임 | `Assets/Scripts/Map/MapBakedAsset.cs`, `BakedMapSource.cs`, `MapRuntimeLoader.cs`, `Assets/Scripts/Net/Session/SessionSceneRouter.cs` | 수정 없이 사용 |

---

## 2. 현재 구조 (실측 요약)

### 2a. 클라이언트 — 두 세대의 맵 코드가 공존한다

씬에 들어 있는 것은 레거시 런타임 생성기(`BackroomsMapGenerator`, `TestRoomMap`)지만,
에디터에는 세대교체된 데이터 파이프라인이 **완성된 채 아직 어느 씬에도 쓰이지 않고** 있다:

```
IMapGenerator.Generate(settings) → MapBlueprint          (순수 데이터, UnityEngine.Object 생성 금지)
  → MapBakePipeline.Bake                                  (에디터 버튼)
      → MapBakedAsset   Assets/Settings/Maps/{name}.asset (동결된 레벨 = 서버에 말해줄 전부)
      → 프리팹           Assets/Prefabs/Maps/{name}.prefab (충돌 조각 = 개별 큐브, 비충돌 = 표면별 병합 메시)
      → MapCatalog 행    Assets/Resources/MapCatalog.asset
  → MapExportPipeline.TryWrite                             (에디터 버튼)
      → NVserver/MapData/{name}.json                       (원자적 쓰기, 내용 동일 시 스킵)
```

- 생성기는 `MapGeneratorRegistry`(`TypeCache.GetTypesDerivedFrom<IMapGenerator>`)가 찾는다.
  **새 맵 타입 추가에 레지스트리 수정이 필요 없다.**
- `MapSceneTable.Pairs` 는 `backrooms`/`test-room` 두 특수 씬 전용이며 **새 맵은 행을 추가하지
  않는다** — 카탈로그에 프리팹이 있으면 `SessionSceneRouter.SceneFor` 가 공용 `MapRuntime` 씬으로
  라우팅한다 (`SessionSceneRouter.cs → SceneFor`, 해석 순서: `sceneOverride` → `MapSceneTable` →
  프리팹 있으면 `"MapRuntime"`).
- **주의: `Assets/Scenes/MapRuntime.unity` 와 `Assets/Resources/MapCatalog.asset` 은 아직 존재하지
  않는다** (한 번도 베이크된 적이 없다). V2 이전에 만들어야 하는 선행 조건이다 (§7 Phase 0).

### 2b. 서버 — 등록은 파일이고, 검증은 한 곳이다

- `Game:MapDirectory`(`../MapData`) 의 `*.json` 전수 스캔, **파일명 어간 = 맵 id = 내부 `name`**.
  셋이 다르면 부팅 거부 (`NVserver/Infrastructure/FileSystem/MapCatalogLoader.cs`).
  `Game:Maps` 는 별칭 테이블일 뿐이다 (`default` → `backrooms`).
- 해시(FNV-1a)는 `name` + `boxes`(배열 순서 포함) + `grid`(있을 때만) 만 포함한다.
  `version`/`source`/`meta`/`spawns` 는 의도적으로 제외 (`Shared/Collision/MapData.cs → ComputeHash`).
- 검증은 `Shared/Collision/MapDataValidator.cs` 단일 지점 — 서버 부팅(`TryValidateSchema`)과
  Unity 익스포트(`TryValidateSchema` + `InspectSimulation`)가 같은 함수를 부른다.
- 오브젝티브(제단→문→열쇠10→장치9)는 맵 파일에 없다. 매치마다
  `Shared/Simulation/ObjectivePlacement.cs` 가 워커빌리티 격자의 `FreeFloor` 셀에서 뽑고,
  클라이언트는 같은 시드로 같은 함수를 돌려 미러링한다 (`Assets/Scripts/Game/MatchManager.cs`).
  **격자 없는 맵 = 열쇠도 문도 없는 매치**이며, 로비의 `supportsMatch` 도 격자 유무로 갈린다.
- 스폰은 `PlayerId % SpawnCount` 로 고정 선택 (`Modules/Realtime/Simulation/Room.cs → Join`).
  룸 정원은 5지만 `ExportedMapTests` 가 **디렉터리의 모든 파일에 스폰 8개**를 단언한다 —
  V2 도 8개를 싣는다.

---

## 3. V2 생성 방식 — 단층 개방형, BSP 구역 분할

### 3a. V1 과의 구조적 차이 (요약 대비표)

| | Backrooms (V1) | Backrooms V2 |
|---|---|---|
| 공간 인상 | 좁은 방 + 복도 미로, 2층 + 계단실 | 넓은 개방 홀 + 기둥 밭 + 부분 칸막이, **단층** |
| 레이아웃 해법 | 방 배치 반복 시도 → 복도 체인 → 도달 불가 지역 사후 복구 | **BSP 재귀 분할 → 구역 인접 그래프의 스패닝 트리로 출입구를 뚫는다** — 연결성이 구성 단계에서 보장되고, 사후 복구 단계 자체가 없다 |
| 고정 앵커 | spawn/exit/stairwell 사각형 3개 고정 | 없음 — 스폰 구역은 규칙(가장 큰 개방 홀 리프)으로 결정 |
| 수직 구조 | 계단실 + `StairLink` | 없음 (`StairLink` 미사용) |
| 팔레트 | 모노 옐로 (damp wallpaper) | 회백 콘크리트 + 청록 형광 (침수 주차장/설비층 무드) — 별도 스펙 |
| 연결성 검증 | 복구 수단 (최대 12회 뚫기) | **검증 수단** — BFS 가 실패하면 생성 실패로 처리하고 원인을 `Blueprint.Blocker` 에 적는다 |

### 3b. 알고리즘 (결정 순서 = 시드 재현성의 일부)

시드는 `MapGeneratorSettings.ResolveSeed()` 하나에서 나오고, `System.Random` 인스턴스 하나로
아래 순서를 고정해서 뽑는다. **분기와 무관하게 뽑는 횟수가 일정해야** 파라미터를 바꿔도
시드 호환이 예측 가능하다 — V1 이 지키는 규율("거부돼도 4회 뽑기")과 같은 이유, 다른 구현.

1. **BSP 분할.** `gridSize`(기본 44) × `cellSize`(기본 2.5m) 격자를 재귀 이분할.
   리프 조건: 한 변 5~10셀. 분할 축은 긴 변, 분할 위치는 중앙 ±25% 범위에서 뽑는다.
2. **구역 타입 배정.** 리프마다 가중치 추첨: 개방 홀(기둥 격자) / 칸막이 구역(가장자리에서
   자라는 벽 토막) / 소실(小室) 클러스터 / 빈 공백. 가중치는 설정 자산의 필드.
3. **출입구.** 리프 인접 그래프를 만들고 스패닝 트리(시드 셔플한 간선 순서의 크루스칼)의
   모든 간선에 폭 2셀 출입구를 뚫는다. 나머지 간선은 `loopChance` 로 추가 개방 — 순환로가
   있어야 술래를 도는 플레이가 성립한다.
4. **구역 내부 채움.** 타입별 규칙으로 셀을 `Solid`/`Open` 마킹. 기둥은 3셀 간격 격자,
   칸막이는 구역 경계에서 내부로 1~반폭 길이. **내부 채움은 출입구 셀과 그 전방 1셀을
   침범할 수 없다** (연결성 보장이 여기서 깨지면 안 된다).
5. **스폰 구역.** 개방 홀 리프 중 면적 최대(동률이면 그래프 중심에 가까운 쪽)를 스폰
   구역으로 지정, 내부 링에 스폰 8개 배치. yaw 는 구역 중심을 향한다.
   `Blueprint.SpawnCentre` = 구역 중심 (술래 시작점, `ILevelQuery.SpawnCentre`).
6. **검증 BFS.** 스폰 구역에서 4방향 플러드필. `Standable` 셀 도달률 100% 미만이면
   **생성 실패** — `Blueprint.Blocker` 에 미도달 셀 수를 적는다. (스패닝 트리가 보장하므로
   정상 경로에서 실패할 수 없고, 실패는 곧 4단계 침범 버그의 조기 검출이다.)
7. **지오메트리 방출.** 방출 순서가 곧 해시이므로 순서를 상수로 고정한다:
   바닥 슬래브 → 외곽 벽 4면 → 내부 벽(런 병합, Z수직 패스 → X수직 패스) → 기둥 →
   천장 슬래브(충돌 있음, **충돌 조각 중 마지막**) → 조명 스트립(비충돌, 맨 끝).
   벽 런 병합은 V1 도 쓰는 일반 기법이지만 **구현은 새로 쓴다** (코드 복사 금지 전제).
8. **격자 기록.** `MapGridData` 에 `Standable` 만 세운다. 단층이므로 `StairLink` 없음,
   `FreeFloor` 는 익스포트 파이프라인이 서버 플레이어 박스로 계산해 채운다
   (`MapGridBuilder.MarkFreeFloor` — 생성기가 직접 쓰지 않는 것이 기존 역할 분담이다).

### 3c. 기본 파라미터와 그 근거

| 파라미터 | 기본값 | 근거 |
|---|---|---|
| `gridSize` | 44 | 44×2.5 = 110m — V1(105m)과 비슷한 도보 스케일 |
| `cellSize` | 2.5 | V1(3.0)보다 촘촘한 격자 = 열쇠/장치 배치 후보가 더 고르게 분포 |
| `floors` | 1 (고정) | 박스 수·격자 크기 절감, `StairLink` 불필요. §5 의 상호작용 높이 제약도 자동 충족 |
| `floorHeight` | 3.6 | `MatchConstants.InteractHeight`(2.5)·`KeyPickupHeight`(1.6) 보다 커야 층간 누출이 없다 — 단층이라 실질 무관하지만 격자 스키마 필드라 명시 |
| `leafMin`/`leafMax` | 5 / 10 | 리프 = 12.5~25m — 은신과 조우가 둘 다 성립하는 방 크기 |
| `pillarSpacing` | 3 | 개방 홀에서 시야가 뚫리되 엄폐가 남는 간격 |
| `loopChance` | 0.35 | 트리만으로는 막다른 구조 — 추격전을 위해 V1(0.15)보다 높게 시작 |
| `doorwayWidth` | 2 (셀) | 5m — 술래와 러너가 교차 가능 |

전부 `BackroomsV2Settings` 의 직렬화 필드로 두고, 튜닝은 에셋에서 한다.

---

## 4. 에디터 툴 구조 — 신규 파일 3+1개, 기존 수정 0개

### 4a. 신규

| 파일 | 내용 |
|---|---|
| `NVproject/Assets/Scripts/Map/BackroomsV2Settings.cs` | `: MapGeneratorSettings`, `[CreateAssetMenu(menuName = "NV/Map/Backrooms V2 Settings")]`. §3c 의 파라미터 + 팔레트 색. **런타임 어셈블리에 있어야 한다** — 에디터 전용 스크립트의 에셋은 빌드에서 로드가 깨진다 (기존 규칙, `MapGeneratorSettings.cs` 주석) |
| `NVproject/Assets/Editor/Map/Generators/BackroomsV2Generator.cs` | `: IMapGenerator, IMapSceneDecorator`. `DisplayName "Backrooms V2"`, `DefaultMapName "backrooms-v2"`, public 무인자 ctor. §3b 알고리즘 전체를 내부 `Solver` 클래스로. `Decorate` 는 `BackroomsV2Ambience` 부착만 |
| `NVproject/Assets/Scripts/Map/BackroomsV2Ambience.cs` | 베이크할 수 없는 것만: `RenderSettings`(안개 색·밀도, 앰비언트), 조명 인스턴스(`asset.Lights` 에서 생성), 환경음. V1 `BackroomsAmbience` 를 열어보지 않고 새로 쓴다 — 무드 자체가 다르므로 수치 겹침도 없다 |
| `NVproject/Assets/Editor/Tests/BackroomsV2GeneratorTests.cs` | §8 의 EditMode 테스트 |

`DefaultMapName` 은 반드시 새 문자열이어야 한다 — 기존 맵 이름을 쓰면 **첫 익스포트가 그
맵의 json 을 덮어쓴다.**

### 4b. 수정하지 않는 것 (전체 목록 — 이 목록이 늘어나면 설계가 틀린 것이다)

`MapGeneratorRegistry`(TypeCache 자동 발견) · `MapGeneratorWindow`(이름 기반 드롭다운) ·
`MapBlueprint` · `MapSceneBuilder` · `MapBakePipeline` · `MapBakedAsset` · `BakedMapSource` ·
`MapCatalog`/`MapCatalogWriter` · `MapExportPipeline`/`MapExport` · `MapSceneTable`(새 맵은
행 금지) · `SessionSceneRouter` · `NVserver/Shared/**` · `NVserver/Api/appsettings.json`
(파일 드롭 = 등록; 별칭/정적 룸 지정은 별도 결정 사항, §6d).

단 하나의 예외 가능성: `MapSurface` enum (`MapBlueprint.cs`) 에 V2 전용 표면(예: `Pillar`)이
필요하면 값 추가는 허용 — 기존 값의 의미·순서는 불변. 기존 6종(Wall/Floor/Ceiling/Trim/
LightPanel/Cover)으로 충분하면 추가하지 않는다.

### 4c. 작업 흐름 (전부 기존 UI)

`Tools ▸ NV ▸ Map ▸ Map Generator` → 드롭다운에 "Backrooms V2" 자동 표시 → 설정 에셋 지정 →
**Generate Preview**(격자 페인팅으로 육안 확인) → **Bake**(에셋+프리팹+카탈로그) →
**Check Export** → **Write To Server**. 익스포트 게이트(`MapExportPlan.CanExport`)가
`randomizeSeed` 켜짐·이름 공백·격자 무결성·`FreeFloor == 0`·시뮬레이션 검사를 전부 막아준다.

---

## 5. 데이터 구조 — 스키마 v2 를 처음으로 온전히 쓴다

파일 스키마는 기존 그대로다 (`Shared/Collision/MapData.cs`, `MapSchema.Current = 2`).
새 필드도, 새 `MapCellFlags` 도 추가하지 않는다 (map-export-plan.md §6.2 B5 재확인).
V2 가 새로 하는 일은 **기존 맵들이 아직 안 쓰는 v2 스키마의 `meta` 블록을 싣는 것**뿐이다:

```jsonc
{
  "version": 2,
  "name": "backrooms-v2",                    // == 파일명 어간, 서버가 부팅 시 강제
  "source": { ... },                          // 파이프라인이 자동 스탬프, 해시 제외
  "meta": {                                   // 해시 제외 → 자유롭게 수정 가능
    "displayName": "백룸 V2",
    "description": "...",
    "recommendedPlayersMin": 2,
    "recommendedPlayersMax": 5,               // 룸 정원과 일치
    "tags": ["match"]
  },
  "boxes":  [ ... ],                          // 방출 순서 = 해시. §3b-7 의 고정 순서
  "spawns": [ ...8개... ],                    // 해시 제외, 그러나 InspectSimulation 이 검증
  "grid":   { "floors": 1, "width": 44, "depth": 44, "cellSize": 2.5,
              "floorHeight": 3.6, "originX": -55, "originZ": -55, "cells": "..." }
}
```

- `meta` 는 해시 밖이므로 표시 이름·설명은 재익스포트 없이 언제든 수정 가능. 반대로
  `boxes`·`grid` 는 **한 조각이라도 바뀌면 해시가 바뀌고**, 구버전 클라이언트는 접속 시
  map-hash 불일치를 맞는다 — 튜닝은 클라이언트 재빌드와 한 묶음이다.
- 격자 셀 값: `Standable(1)` 만 생성기가 쓰고, `FreeFloor(2)` 는 익스포트가 덧쓴다.
  단층이므로 `StairLink(4)` 는 0. `cells` 길이 = 1×44×44 = 1,936 바이트 (base64 ≈ 2.6KB).
- float 은 왕복 형식 `"R"`, UTF-8 no BOM, LF — 전부 `MapExportPipeline.Serialize` 가 처리하므로
  V2 쪽 코드가 신경 쓸 일이 없다. **맵 파일 비교는 파싱된 수치로** (conventions.md — `"R"` 은
  정규형이 아니다, Mono/CoreCLR 표기 차이).

`MapBakedAsset` 도 그대로 — V2 의 산출물이 기존 필드 집합(박스/스폰/격자/조명/로비 메타/
출처)을 벗어나지 않도록 알고리즘을 맞췄다. 벗어나고 싶어지면 그 요구를 먼저 의심한다.

---

## 6. NVserver 연동 — 서버 코드 변경 0줄

### 6a. 등록

`NVserver/MapData/backrooms-v2.json` 을 쓰는 순간 끝이다. 서버는 디렉터리를 전수 스캔해
파일명으로 등록하고, `GET /maps` 목록·로비 맵 선택·`supportsMatch` 판정까지 자동이다.
설정 파일 수정 없음.

### 6b. 매치 성립 조건 (V2 가 맞춰야 하는 서버 계약)

| 계약 | 값 | 어기면 |
|---|---|---|
| 스폰 개수 | 정확히 8 | `ExportedMapTests` 실패 (디렉터리 전 파일 자동 검사) |
| 스폰 유효성 | 디페너트레이션 무이동 + 10틱 접지 (오차 0.05m) + 전방 60틱 보행 통과 | `InspectSimulation` / `ExportedMapTests` 실패 |
| `FreeFloor` | 층당 ≥1 (단층이라 자동), 전체 ≥64 (미만이면 경고 — 오브젝티브 밀집) | 제단·문·열쇠10·장치9 배치 실패 또는 밀집 |
| 제단 자리 | 격자 중앙 부근 `FreeFloor` + 8방향 인접 `FreeFloor` 1개 (체인 낙하점) | 제단 미배치 → 체인 패널티 붕괴. **개방 홀 하나를 격자 중앙에 걸치게 하는 것이 안전하다** — `PlaceAltar` 는 중앙에서 링 확장 탐색 |
| 열쇠/장치 간격 | 4m / 5m 간격으로 10 + 9개 (64회 시도 후 간격 포기) | 배치는 되지만 뭉친다 — `FreeFloor` 가 넓게 퍼져 있으면 무관 |
| 박스 상한 | 1,100 초과 시 검토 경고 (`MapDataValidator.BoxCountReviewThreshold`) | §7 성능 절차 발동 |

### 6c. 해시 동기화

클라이언트와 서버가 같은 `MapData.ComputeHash()` 를 돌린다 — `Shared` 가 양쪽에서
컴파일되므로 별도 작업이 없다. 카탈로그의 `bakedHash` 와 서버 `Welcome(0x83)` 의 해시가
일치하는지가 접속 시 유일한 가드다. **`dotnet build` 통과는 절반이다 — `Shared` 를 건드리지
않았어도, 익스포트 후 Unity 에디터에서 접속해 해시 경고가 없는 것까지 확인한다.**

### 6d. 별도 결정 사항 (계획 범위 밖, 구현 후 논의)

- `Game:Maps` 의 `default` 별칭을 V2 로 옮길지 — 옮기면 맵 미지정 방 생성이 V2 로 간다.
- `Game:StaticRooms` 에 V2 고정 개발 룸을 추가할지 — 2클라 개발 루프가 V2 에서 필요해지면.
- 문서 정리: `docs/readme.md:86`·`conventions.md:200` 의 "56×56, 1367 박스" 는 현행
  `backrooms.json`(35×35, 736 박스)과 이미 어긋난 **선행 오류**다. V2 추가와 같이 고친다.

---

## 7. 성능 고려사항

### 7a. 서버 틱 예산 — 측정 기반 상한

`CollisionWorld` 에 브로드페이즈가 없는 것은 **측정 후 결정 사항**이다 (map-export-plan.md
§10, Phase 4-3 취소). 실측 기준 (Release, backrooms 736 박스):

| 항목 | 실측 | 736박스 기준 환산 |
|---|---|---|
| `PlayerMovement.Step` | 0.0129 ms | 8인 틱당 0.103 ms = 틱 예산(33.3ms)의 0.31% |
| `Raycast` 1회 | 1.98 µs | 사격 판정 |
| `MarkFreeFloor` | 1.3 ms | 익스포트/접속 시 1회 |

비용이 박스 수에 선형이므로 V2 의 규율은 상한이 아니라 **목표치**로 잡는다:

- **박스 목표 ≤ 700, 경고선 1,100.** 단층 + 벽 런 병합 + 기둥(개당 1박스) 구성이면
  V1(736, 2층)보다 적게 나오는 것이 정상이다. 넘으면 병합 패스부터 의심한다.
- 1,100 을 정말 넘어야 한다면: **먼저 §10 방식으로 재측정**하고, 그래도 문제면 맵을
  줄인다. 브로드페이즈 추가는 금지 — `Depenetrate` 가 순서 의존이라 클라이언트 예측과의
  비트 일치가 깨질 수 있고, 증상은 "특정 지점에서만 캐릭터 떨림"으로만 나타난다.

### 7b. 클라이언트

- 비충돌 조각(천장 타일, 조명 스트립)은 `MapSceneBuilder.BuildMergedPieces` 가 표면별 1메시로
  병합한다 — V2 가 할 일은 비충돌 조각을 아끼는 게 아니라 **충돌 조각을 아끼는 것**.
- 개방 홀 구조는 V1 미로보다 오버드로가 아니라 **시야 거리**가 문제다. 안개 밀도를 시야
  차단 수단으로 쓰고(게임플레이 겸용), 조명은 `shadows = None` 유지.
- WebGL: 병합 메시 `IndexFormat.UInt32` 는 기존 파이프라인 그대로. 맵 에셋은 빌드에
  구워지므로 (`GET /maps` 는 목록만) 다운로드 비용 변화 없음.

### 7c. 생성/익스포트 시간

전부 에디터 타임이다. BSP + 플러드필은 44×44 = 1,936셀에서 밀리초 단위 — 성능 항목이
아니라 정확성 항목이다. `MarkFreeFloor` 1.3ms 급도 동일.

---

## 8. 구현 단계

각 단계 끝의 검증을 통과하기 전에는 다음 단계로 가지 않는다.

| Phase | 내용 | 검증 |
|---|---|---|
| **0. 선행 조건** | `Tools ▸ NV ▸ Scene ▸ Create Map Runtime Scene` 실행 (`MapRuntime.unity` 부재). 기존 맵 하나를 베이크해 `MapCatalog.asset` 생성 경로 확인 | `MapGeneratorParityTests` 통과 유지 — 베이크가 기존 json 과 바이트 일치하는지가 이 테스트의 존재 이유 |
| **1. 뼈대** | `BackroomsV2Settings` + `BackroomsV2Generator` 최소 구현 (외곽 벽 + 바닥 + 스폰 8) | Map Generator 창 드롭다운에 표시, Generate Preview 성공 |
| **2. 알고리즘** | §3b 1~6단계 (BSP, 구역, 출입구, 채움, 스폰, 검증 BFS) + 격자 | EditMode 테스트: 같은 시드 → 같은 blueprint (조각·스폰·격자 바이트 일치), 100 시드 × 연결성 100%, 스폰 8개, 시드 불변 규율(뽑기 횟수) |
| **3. 지오메트리** | §3b 7단계 방출 + 팔레트 + `BackroomsV2Ambience` | Bake 성공, 씬 육안 확인, `DescribeDrift` 무경고, 프리팹/카탈로그 행 생성 |
| **4. 익스포트·서버** | Check Export → Write To Server | `MapExportPlan` 오류 0 · `dotnet test` (`ExportedMapTests` 가 새 파일 자동 포함, 394+ 통과) · `dotnet run --project Api` 부팅 로그에 `backrooms-v2` 등록 · `GET /maps` 에 `supportsMatch: true` |
| **5. 통합 플레이** | 로비에서 V2 방 생성 → `MapRuntime` 라우팅 → 2클라 매치 | Build and Launch 2 Clients: 맵 해시 경고 없음, 제단·문·열쇠·장치 배치 확인, 술래/러너 1라운드 완주 |
| **6. 문서** | 이 문서 상태 갱신, §6d 선행 오류 수정, 함정 발견 시 `conventions.md` 에 증상→원인→해법 추가 | — |

## 9. 하지 않을 것

- **서버 측 레벨 생성** — 시드 해석이 두 구현으로 갈라지는 순간 "열쇠가 가끔 벽 안에 스폰"
  류의 버그가 된다 (map-export-plan.md §8 기존 결정 재확인).
- 새 `MapCellFlags` 값, OBB(회전 콜라이더), 메시 단위 익스포트, 브로드페이즈, 맵 파일
  바이너리화 — 전부 기존 "하지 않기로 한 것" 목록에 있다.
- `MapSceneTable` 에 V2 행 추가 — 전용 씬이 필요한 맵이 아니다.
- V1 파일 수정 — §1a 목록의 어떤 파일에도 diff 가 생기면 안 된다. 리뷰에서 이걸 먼저 본다.
- 8스폰/5정원 불일치 해소, `test-room` 격자 추가 같은 인접 정리 — 범위 밖.

## 10. 위험과 함정 (기존 트랩 리스트에서 V2 에 걸리는 것)

| 함정 | 증상 | 예방 |
|---|---|---|
| `DefaultMapName` 이 기존 이름과 충돌 | 기존 맵 json 덮어씀 | `"backrooms-v2"` 고정, Phase 1 에서 확인 |
| 파일명 ≠ `name` | 서버 부팅 거부 | 파이프라인이 이름으로 파일명을 만들므로 수동 리네임만 금지 |
| `randomizeSeed` 켠 채 익스포트 | 게이트가 거부 (정상) | 익스포트 전 설정 에셋에서 끄기 |
| `MapRuntime.unity` 부재 | `SceneFor` 가 `""` 반환, 매치 진입이 **조용히** 실패 | Phase 0 |
| 생성기에서 `UnityEngine.Object` 생성 | 계약 위반 — 프리뷰가 씬을 오염 | `IMapGenerator` 주석의 금지 조항, 리뷰 체크 |
| 조각 방출 순서 비결정 | 시드가 같아도 해시가 다름 | §3b-7 고정 순서 + Phase 2 바이트 일치 테스트 |
| 반쯤 쓴 json 이 `MapData/` 에 남음 | **서버 부팅 정지** (의도된 동작) | 익스포트는 원자적 — 수동으로 파일을 만들지 않는다 |
| 위층 `FreeFloor` 전멸 (SkinWidth) | — | 단층이라 무관하지만, `MarkFreeFloor` 를 직접 재구현하지 않는 것이 진짜 예방책 |
