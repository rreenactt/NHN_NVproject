# MapGenerator Editor Tool — 개발 계획

`NVproject` 에 에디터 전용 맵 생성 도구(`MapGenerator.cs`)를 새로 만든다. 지금은 레벨이
**런타임 `Awake` 에서** 만들어지고, 그 결과를 에디터가 다시 계산해 서버로 export 한다. 이
계획은 그 순서를 뒤집는다 — **에디터에서 한 번 만들고 굳혀서**, 런타임은 굳은 것을 열기만
하고 export 는 굳은 것을 그대로 쓴다.

해커톤 규모다. 아래에서 "MVP" 로 표시되지 않은 것은 전부 **자리만 만들어 두고 구현하지
않는다**.

---

## 1. 기존 `BackroomsMapGenerator.cs` 분석 결과

`NVproject/Assets/Scripts/BackroomsMapGenerator.cs`, 1188줄, 클래스 하나.
`MonoBehaviour` 이면서 `INetworkMapSource` 다. 안에 성격이 다른 네 가지가 섞여 있고, 그
경계가 이 리팩터링의 절단선이다.

### 1.1 네 개의 책임

| # | 책임 | 대표 멤버 | Unity 의존 | 판정 |
|---|---|---|---|---|
| A | **격자 솔버** | `SolveGrid`, `StampAnchors`, `CarveRooms`, `ConnectRooms`, `CarveCorridor`, `CarveLine`, `WireStairwell`, `EnforceConnectivity`, `FloodFromSpawn` | `RectInt`/`Vector2Int`/`Vector3Int`/`Mathf.Abs` 뿐 | **그대로 재사용.** 순수 C# 에 가깝다 |
| B | **지오메트리 빌더** | `BuildGeometry`, `BuildTiles`, `BuildWalls`, `BuildStairs`, `BuildCeilingLid`, `AddBox` | `GameObject.CreatePrimitive`, `Destroy` | **출력 대상만 갈아끼우면 재사용.** 이미 `_collisionOnly` 로 두 갈래다 |
| C | **연출·분위기** | `BuildLights`, `ApplyAtmosphere`, `BuildHum`, `Update`(점멸), `SetWallTransparency`, `EnsureMaterials`, `PlaceActors`, `BakeNavMesh` | 전역 `RenderSettings`, `AudioClip.Create`, `Light`, `Material` | **분해한다.** 일부는 베이크 가능, 일부는 런타임에 남아야 한다 |
| D | **레벨 질의 API** | `EnsureGrid`, `IsStandable`, `FloorIndexAt`, `TryWorldToCell`, `CellToWorld`, `TryRandomPoint`, `TryNearestStandablePoint`, `CollectStandableCells`, `SpawnCentre`, `ExitCentre`, `BuildGrid`, `GetSpawns` | 없음 (좌표 계산) | **런타임에 반드시 남는다.** 이것이 "런타임 생성 제거" 의 진짜 장애물 |

### 1.2 재사용 가능한 로직 / 에디터 전용으로 옮길 것

**그대로 옮긴다 (A + B):**
- 그리드 셀 모델 `enum Cell { Solid, Room, Corridor, Anchor }` 와 `_cell[floor][x,z]`, `_protected[floor][x,z]`
- 방 배치(패딩 1셀 겹침 거절) → L자 복도 연결 → 루프 추가(`loopChance`) → 계단 배선 → **양층 플러드필 연결성 보정**(`EnforceConnectivity`, 12패스). 이 마지막 패스가 "예쁘지만 못 가는 방" 을 막는 유일한 장치다.
- 벽 **런 병합**(`BuildWalls`): 걸을 수 있는 셀과 막힌 셀의 경계를 X축·Z축으로 각각 훑어 연속 구간을 박스 하나로 만든다. 35×35 2층이 736박스로 끝나는 이유.
- 계단(`BuildStairs`): `sideInset 0.04` / `stepOverlap 0.03` 은 z-fighting 을 죽이려고 찾은 값이다. **숫자를 그대로 옮긴다.**
- 천장 뚜껑(`BuildCeilingLid`): 최상층 밖으로 보이는 것을 콜라이더 하나로 막는다.

**에디터로 옮긴다:**
- `Awake() → Generate()`. 런타임 생성 자체가 없어진다.
- `ComputeCollision()` — **이 함수의 존재 이유가 사라진다.** 지금은 "에디트 모드에서 씬에 레벨을 통째로 쏟을 수 없어서" `Generate` 의 난수 인출 순서를 그대로 재현하는 두 번째 경로를 유지하고 있다. 굳은 자료가 있으면 그냥 읽으면 된다.
- `DestroyImmediate` 로 `__BackroomsMap` 을 지우는 `ClearRoot` → `Undo` 를 아는 정리로 교체.
- `BakeNavMesh` — 에디터 베이크(`NavMeshSurface` 를 씬에 굽고 에셋으로 저장)로 옮긴다.

**런타임에 남는다 (C 일부 + D 전부):**
- `ApplyAtmosphere`(전역 `RenderSettings` — 씬 전역 상태라 프리팹에 담기지 않는다), `BuildHum`(코드 생성 오디오), 형광등 점멸 `Update`, `SetWallTransparency`(프리즈 장치의 x-ray).
- 질의 API 전체. `MatchManager`, `MatchBootstrap`, `GameHudController`, `MatchMapView` 가 **`BackroomsMapGenerator` 타입을 직접** 참조한다(4개 파일). 이것을 인터페이스로 끊지 않으면 새 도구는 기존 게임을 깨뜨린다.

### 1.3 반드시 지켜야 하는 제약 (깨면 증상이 조용하다)

1. **박스 목록의 순서가 맵 해시에 들어간다.** `MapData.ComputeHash` 가 순서대로 섞는다. 베이크된 지오메트리에서 박스를 다시 모을 때 순서가 달라지면 서버와 해시가 어긋나고, 증상은 접속 경고 한 줄뿐이다.
2. **`randomizeSeed` 는 export 차단 사유다** (`DescribeExportBlocker`). 씨드가 매번 달라지면 파일과 다음 실행의 지형이 갈린다.
3. **`FreeFloor` 플래그는 클라이언트가 계산하지 않는다.** `MapGridBuilder.MarkFreeFloor`(= `Shared`, 서버 코드)가 서버의 플레이어 박스로 판정한다. 그 뜻이 "서버가 여기 플레이어를 놓아도 밀려나지 않는다" 이기 때문이다. 새 도구도 이 경로를 우회하면 안 된다.
4. **`MapExport.InvalidateCache()`** — 지형을 다시 만드는 쪽이 불러야 한다. 캐시는 스스로 눈치챌 수 없다(같은 `List` 인스턴스를 비우고 다시 채운다).
5. **부동소수점은 `"R"` 왕복 형식으로 직렬화한다.** 자릿수를 줄이면 해시가 이유 없이 어긋난다.
6. **`MapName` = export 파일명 = 서버 `Game:Maps` 키 = `MapSceneTable` 의 맵 이름.** 넷이 같아야 한다. `backrooms2f` 가 정확히 이걸로 죽었다.

### 1.4 이미 잘 되어 있어 손대지 않을 것

`Assets/Editor/Map/` 의 export 파이프라인은 **이미 이 계획이 요구하는 모양이다.** 새로 쓰지
않는다.

- `MapExportPipeline.Plan()` — UI 없음. 판정만 하고 아무것도 쓰지 않는다.
- `MapExportPipeline.TryWrite()` — 원자적 쓰기(tmp → `File.Replace`), 내용 동일 시 쓰지 않음, `\r\n` 정규화, 출력 폴더가 정말 이 저장소의 서버인지 확인.
- `MapExportPlan` — 오류/경고/해시/변경여부/서버 등록 여부를 들고 있는 값 객체.
- `MapExportWindow`(439줄) — 쓰기 전에 무엇을 쓸지 보여 주는 창.
- `MapCollisionExporter` — 메뉴 3개(`Export Map Collision`, `창 없이`, `Export All Maps`).
- EditMode 테스트 24개(`MapExportValidationTests.cs`, 497줄).

**따라서 요구사항의 "`Tools/NV/Map Export` 메뉴 추가" 는 이미 존재한다** (`Tools ▸ NV ▸ Map ▸
Export Map Collision`). 이 계획은 메뉴를 새로 만들지 않고, **베이크된 맵을 그 파이프라인에
먹이는 어댑터**를 만든다.

---

## 2. 새 Editor Tool 아키텍처

### 2.1 한 줄 요약

> 생성기는 **자료를 만드는 순수 함수**가 되고, 그 자료를 **에셋**으로 굳히고, 씬/프리팹과
> 서버 JSON 은 **둘 다 그 에셋에서 파생된다.**

```
        [MapGeneratorSettings]  (ScriptableObject — Seed/Size/Cell/Floor/Wall …)
                    │
                    ▼  IMapGenerator.Generate(settings)      ← 순수 함수, UnityEngine.Object 안 만듦
        ┌───────────────────────────┐
        │      MapBlueprint         │   격자 + 박스목록(순서 보존) + 스폰 + 메타데이터
        └───────────────────────────┘
              │                    │
    MapSceneBuilder                └──────────────► MapExportPipeline (기존)
    (Undo 를 아는 씬 생성)                                   │
              │                                    IMapWriter  ► JsonMapWriter (기존 Serialize)
              ▼                                                └ (자리만) BinaryMapWriter
    씬 오브젝트 / 프리팹  +  MapBakedAsset ────────────────────► NVserver/MapData/*.json
              │
              ▼
    런타임: BakedMapSource (INetworkMapSource + ILevelQuery) — 아무것도 만들지 않고 에셋만 읽는다
```

### 2.2 핵심 결정 4가지

**결정 1 — `MapBlueprint` 가 유일한 중간 표현이다.**
지금은 지오메트리를 만드는 경로(`Generate`)와 콜리전만 계산하는 경로(`ComputeCollision`)가
둘 다 `BuildGeometry` 를 지나면서 `_collisionOnly` 플래그로 갈린다. 이 플래그가 사라진다 —
생성은 **언제나** blueprint 를 만들고, 씬을 만들지 말지는 그 다음 소비자의 문제다.
난수 인출 순서를 두 경로가 맞춰야 하는 제약 자체가 없어진다.

**결정 2 — 지오메트리는 프리팹, 콜리전은 에셋. 프리팹에서 박스를 다시 긁지 않는다.**
프리팹의 `Transform` 을 훑어 콜리전을 복원하면 순서가 계층 구조에 의존하고, 사람이 프리팹을
손으로 고치는 순간 서버와 갈린다. **`MapBakedAsset`(ScriptableObject) 이 박스 목록·격자·스폰의
출처**이고, 프리팹은 눈에 보이는 것일 뿐이다. 둘 다 같은 blueprint 에서 같은 순간에 나오고,
불일치는 도구가 검사한다(§10).

**결정 3 — 매치 레이어는 인터페이스로 끊는다.**
`ILevelQuery` 를 새로 뽑아 `MatchManager` / `MatchBootstrap` / `GameHudController` /
`MatchMapView` 가 그것만 보게 한다. 구현은 둘: 기존 `BackroomsMapGenerator`(런타임 생성,
당분간 유지) 와 새 `BakedMapSource`(굳은 것을 읽음). **이 인터페이스가 있어야 두 방식이
공존하며 갈아탈 수 있다** — 해커톤에서 한 번에 갈아엎는 것은 위험하다.

**결정 4 — export 파이프라인은 건드리지 않는다.**
`BakedMapSource` 가 `INetworkMapSource` 를 구현하면 기존 `MapExportPipeline.Plan()` 이
그대로 동작한다. 씬 스캔, 격자 검증, `FreeFloor` 채우기, 등록 검사, 원자적 쓰기, 24개
테스트가 전부 공짜로 따라온다.

---

## 3. 디렉터리 및 클래스 구성

```
NVproject/Assets/
├── Scripts/
│   ├── Map/                                    ← 신규. 런타임에서도 보이는 자료·인터페이스
│   │   ├── MapBlueprint.cs          [MVP]  격자·박스·스폰·메타. 순수 C#, MonoBehaviour 아님
│   │   ├── MapGeneratorSettings.cs  [MVP]  ScriptableObject. Inspector 파라미터의 집
│   │   ├── MapBakedAsset.cs         [MVP]  ScriptableObject. blueprint 를 굳힌 것
│   │   ├── ILevelQuery.cs           [MVP]  매치 레이어가 레벨에 묻는 것 전부
│   │   ├── BakedMapSource.cs        [MVP]  MonoBehaviour. ILevelQuery + INetworkMapSource
│   │   └── MapMetadata.cs           [2단계] SpawnPoint/Interactive/Nav 메타
│   ├── BackroomsMapGenerator.cs             기존. ILevelQuery 를 구현하도록만 수정
│   └── TestRoomMap.cs                       기존. 동일
│
└── Editor/
    ├── Map/                                    기존 폴더에 추가
    │   ├── MapGeneratorWindow.cs    [MVP]  EditorWindow — 이 작업의 얼굴
    │   ├── MapSceneBuilder.cs       [MVP]  blueprint → 씬 오브젝트, Undo 지원
    │   ├── MapBakePipeline.cs       [MVP]  blueprint → 프리팹 + MapBakedAsset (판정/쓰기 분리)
    │   ├── MapExportWindow.cs               기존
    │   ├── MapExportPipeline.cs             기존
    │   └── MapCollisionExporter.cs          기존
    └── Map/Generators/
        ├── IMapGenerator.cs         [MVP]  Generate(settings) → MapBlueprint
        ├── MapGeneratorRegistry.cs  [MVP]  타입 → 생성기. 확장점
        ├── TestRoomGenerator.cs     [MVP]  TestRoomMap.BuildGeometry 를 blueprint 로 옮긴 것
        └── BackroomsGenerator.cs    [MVP]  BackroomsMapGenerator 의 A + B 를 옮긴 것
```

### 3.1 자료 형태

```csharp
// Scripts/Map/MapBlueprint.cs  — UnityEngine.Object 를 하나도 만들지 않는다
public sealed class MapBlueprint
{
    public string MapName;            // = export 파일명 = 서버 Game:Maps 키
    public int UsedSeed;
    public MapGridData Grid;          // Shared 의 타입. Standable / StairLink 만 채운다
    public List<MapPiece> Pieces;     // 순서가 곧 맵 해시의 순서다
    public List<MapSpawnPoint> Spawns;
    public string Blocker;            // 재현되지 않는 이유. 없으면 null
}

public struct MapPiece                // AddBox 한 번에 해당한다
{
    public string Name;               // "Wall Z", "Carpet", "Step" …
    public Bounds Bounds;
    public MapSurface Surface;        // Wall / Floor / Ceiling / Trim / LightPanel
    public bool Collides;             // false 면 콜리전 목록에 안 들어간다 (천장 타일, 조명 패널)
}
```

`MapPiece.Collides` 가 기존 `AddBox(…, bool collider)` 인자를 그대로 이어받고,
`Surface` 가 머티리얼 참조를 대신한다 — blueprint 는 `Material` 을 모른다. 머티리얼 결정은
씬 빌더의 몫이다.

### 3.2 `ILevelQuery` — 매치 레이어와의 계약 *(0단계에서 확정됨)*

**호출부가 실제로 쓰는 것만 넣는다.** 구현부를 세어 보니 `MatchManager`,
`MatchBootstrap`, `DeviceSystem`, `GameHudController`, `MatchMapView` 다섯 곳이 쓰는 멤버는
11개뿐이다. `CellSize`/`FloorSpacing`/`FloorLevel`/`ExitCentre`/`CellToWorld`/
`CollectStandableCells` 는 `BackroomsMapGenerator` 에 public 으로 있지만 **밖에서 아무도
부르지 않는다** — 넣으면 두 번째 구현이 맞춰야 할 멤버만 늘어난다.

```csharp
public interface ILevelQuery
{
    int GridSize { get; }
    int FloorCount { get; }
    Vector3 SpawnCentre { get; }
    bool HasGrid { get; }
    void EnsureGrid();
    bool IsStandable(int floor, int x, int z);
    int FloorIndexAt(float worldY);
    bool TryWorldToCell(Vector3 world, out int floor, out int x, out int z);
    bool TryRandomPoint(System.Random random, out Vector3 point, float margin = 0.55f);
    bool TryNearestStandablePoint(Vector3 near, out Vector3 point);
    void SetWallTransparency(float alpha);      // 프리즈 장치의 x-ray
}
```

> **계획 수정 1 — `EnsureGrid()` 는 인터페이스에 들어간다.** 처음에는 "런타임 생성 방식의
> 사정" 이라 뺄 생각이었는데, `MatchManager.BeginMatch` 와 `MatchMapView.EnsureBuilt` 가
> **밖에서 직접 부르고 있었다.** 특히 `HasGrid` 는 스스로 `EnsureGrid` 를 부르지 않으므로,
> 도메인 리로드 뒤 `HasGrid` 를 읽기 전에 밖에서 불러 주는 것이 지금 동작이다. 빼면 0단계가
> 동작 변경이 된다. 굳은 맵에서는 아무 일도 하지 않는 메서드로 남는다.

> **계획 수정 2 — 구현체는 `MonoBehaviour` 여야 한다.** Unity 는 **인터페이스 타입 필드를
> 직렬화하지 못한다.** 그리고 이 참조는 도메인 리로드를 견뎌야 한다 — 평범한 관리 참조는
> 리로드 후 null 이 되고, 그 뒤로 레벨에 묻는 모든 규칙이 세션이 끝날 때까지 던진다.
> 그래서 `MatchManager`/`MatchBootstrap` 은 **`MonoBehaviour` 필드**를 들고
> `ILevelQuery` 로 내다본다:
>
> ```csharp
> [SerializeField] private MonoBehaviour map;
> public ILevelQuery Map => map == null ? null : map as ILevelQuery;
> ```
>
> `map == null` 이 Unity 의 연산자를 타는 것이 중요하다. **파괴된 컴포넌트는
> `MonoBehaviour` 로 비교하면 null 이고 인터페이스 참조로 비교하면 아니다** — 필드에 Unity
> 타입을 남겨 두는 것만으로 호출부 전부의 `== null` 이 지금 뜻을 유지한다.
>
> 대가는 인스펙터가 아무 컴포넌트나 받아 준다는 것이고, `MatchBootstrap.Awake` 가 그것을
> 검사해 문장으로 거절한다.

---

## 4. UI/UX — `MapGeneratorWindow`

메뉴: **Tools ▸ NV ▸ Map ▸ Map Generator** (기존 `Tools/NV/Map/…` 밑에 나란히, priority 60).

IMGUI(`OnGUI`) 로 만든다. 기존 `MapExportWindow` 가 IMGUI 이고, UI Toolkit 을 섞으면 배울
것만 늘어난다. (게임 HUD 만 UI Toolkit 인 것은 스타일시트가 필요해서다.)

```
┌─ Map Generator ─────────────────────────────────────────┐
│ Type   [ Backrooms ▾ ]     ← MapGeneratorRegistry 가 채운다
│ Preset [ MapGeneratorSettings.asset  ◎ ]  [New] [Save]  │
├─ Parameters ────────────────────────────────────────────┤
│  ▼ Footprint                                            │
│     Grid Size     [ 35 ]   Cell Size   [ 3.0 ]          │
│     Floors        [  2 ]   Floor Height[ 3.2 ]          │
│     Wall Thickness[ 0.25]                               │
│  ▼ Layout                                               │
│     Seed          [ 0 ]  [🎲 Roll]   ☐ Randomize        │
│     Room Attempts [ 22 ]  Min[3] Max[8]                 │
│     Corridor Width[ 1 ]   Loop Chance [ 0.15 ]          │
│  ▼ Anchors                                              │
│     Spawn  [3,3,6,6]  Exit [26,26,6,6]                  │
│     Stairwell [16,15,3,5]   Steps [16]                  │
│  ▼ Surfaces  (색·조명 — 씬 생성에만 쓰이고 해시에 안 들어감)│
├─ Preview ───────────────────────────────────────────────┤
│   [ 층 0 | 층 1 ]     ██▓▓░░  ← 격자를 Texture2D 로 그린 미리보기 │
│   박스 736 · 스폰 8 · 설 수 있는 셀 1204 · 해시 3F2A91C4  │
│   ⚠ randomizeSeed 가 켜져 있다 — export 할 수 없다        │
├─────────────────────────────────────────────────────────┤
│ [ Generate Preview ]  [ Build In Scene ]  [ Bake Prefab ]│
│ [ Export To Server ]                     ☐ Auto-export   │
└─────────────────────────────────────────────────────────┘
```

**UX 원칙 3개** (전부 기존 export 창에서 이미 지키고 있는 것을 따른다):

1. **버튼을 누르는 것이 곧 덮어쓰기가 되지 않는다.** `Generate Preview` 는 아무것도 쓰지
   않고 blueprint 만 만든다. 숫자와 경고를 먼저 보여 주고, 그 다음에 쓴다.
2. **거절 사유는 문장으로 말한다.** "export 할 수 없다" 가 아니라 "`randomizeSeed` 가 켜져
   있다. 씨드를 매번 새로 뽑으므로 export 한 지형이 다음 실행에서 다시 만들어지지 않는다."
   — 기존 `DescribeExportBlocker` 의 어투를 그대로 쓴다.
3. **미리보기는 격자를 그린다, 씬을 만들지 않는다.** 파라미터를 만지는 동안 씬에 3천 개
   오브젝트가 생겼다 지워졌다 하면 쓸 수 없다. 격자 `Texture2D` 는 `MatchMapView` 가 이미
   같은 것을 하고 있으니 그리는 법을 거기서 가져온다.

**Undo/Redo.** `Build In Scene` 은 루트 하나를 `Undo.RegisterCreatedObjectUndo` 로 만들고,
자식은 그 루트 밑에 붙인다(자식마다 등록하면 3천 건의 Undo 스택이 된다). 기존 루트는
`Undo.DestroyObjectImmediate` 로 지운다. 전체를 `Undo.SetCurrentGroupName("NV Map Generate")`
+ `Undo.CollapseUndoOperations` 로 **한 번의 Ctrl+Z** 로 만든다.

> ⚠ `NVproject/CLAUDE.md` 의 함정: `Undo.*` 는 **에디터 메뉴/창에서만** 쓴다. Unity MCP
> `Unity_RunCommand` 안에서 쓰면 그 커맨드가 나중에 에러날 때 롤백되어 반쯤 적용된 씬이
> 남는다. 이 창은 사람이 누르는 창이므로 `Undo` 를 써도 된다.

생성 후에도 **씬에서 바로 고칠 수 있다** — 만들어진 것은 평범한 GameObject 라 옮기고 지우고
복제할 수 있다. 다만 손으로 고친 지오메트리는 `MapBakedAsset` 에 반영되지 않으므로, 창이
"씬이 에셋보다 새롭다" 를 감지해 경고한다(§10 리스크 R2).

---

## 5. Map Generator 확장 구조

```csharp
public interface IMapGenerator
{
    string DisplayName { get; }                 // 드롭다운에 보이는 이름
    string DefaultMapName { get; }              // "backrooms", "test-room"
    System.Type SettingsType { get; }           // 이 생성기가 읽는 Settings 파생 타입
    MapBlueprint Generate(MapGeneratorSettings settings);
}
```

**등록 방식은 리플렉션 스캔이다.**

```csharp
// MapGeneratorRegistry — TypeCache 는 에디터 전용이고 컴파일 시점에 색인되어 있다
TypeCache.GetTypesDerivedFrom<IMapGenerator>()
```

속성(attribute) 이나 하드코딩 목록보다 이쪽이 싼 이유: 새 생성기를 추가하는 사람이
**파일 하나만** 만들면 되고, 등록을 잊어 드롭다운에 안 나오는 실패가 없다. `MapSceneTable`
처럼 표를 코드에 두는 선택과 모순되지 않는다 — 그 표는 맵↔씬이라는 *짝*의 출처이고, 이쪽은
*타입 목록*이라 코드에서 유도된다.

**새 생성기를 추가하는 절차** (문서에 이대로 적는다):

1. `Editor/Map/Generators/MyGenerator.cs` 에 `IMapGenerator` 구현
2. 파라미터가 더 필요하면 `MapGeneratorSettings` 를 상속한 SO 를 만든다
3. `MapSceneTable.Pairs` 에 `{ "my-map", "MyScene" }` 한 줄
4. 서버 `appsettings.json` 의 `Game:Maps` 에 한 줄 (창이 스니펫을 만들어 준다 — 기존
   `MapExportPipeline.CheckRegistration` 이 이미 그 조각을 뱉는다)

**MVP 시점의 생성기 둘:**

- `TestRoomGenerator` — `TestRoomMap.BuildGeometry()` 를 그대로 옮긴다. 10개 박스, 난수
  없음, 격자 없음(`BuildGrid() => null` 은 여기서도 정답이다 — 커버 블록 안쪽을 바닥이라
  선언하느니 격자를 안 내놓는 편이 맞다). **가장 먼저 이것으로 파이프라인을 관통시킨다.**
- `BackroomsGenerator` — 위 §1.2 의 A + B 를 옮긴 것.

---

## 6. Map Export 구조

### 6.1 포맷 추상화 (얇게)

```csharp
public interface IMapWriter
{
    string Extension { get; }                   // ".json"
    string Describe { get; }                    // 창에 보일 이름
    byte[] Write(MapData data);
}
```

- `JsonMapWriter` [MVP] — 기존 `MapExportPipeline.Serialize(MapData)` 를 그대로 호출한다.
  손으로 쓴 JSON 이고 `"R"` 왕복 형식이라는 성질을 그대로 유지한다.
- `BinaryMapWriter` [보류] — **인터페이스만 두고 구현하지 않는다.** 지금 맵 파일은 736박스
  ≈ 70KB 이고 서버 기동 때 한 번 읽힌다. 바이너리로 줄여서 얻는 것이 없다. 대형 맵(§8.6)이
  생겼을 때 이 자리에 넣는다.

`MapExportPipeline.TryWrite` 가 `plan.Serialized`(string) 대신 `IMapWriter` 를 거치게
바꾸는 것은 **2단계**로 미룬다. MVP 에서는 `IMapWriter` 를 정의만 하고 JSON 경로는 지금
그대로 둔다 — 원자적 쓰기·변경없음 감지·`\r\n` 정규화가 전부 string 기준으로 짜여 있고,
바이너리를 위해 그걸 다시 짜는 것이 지금 얻는 것보다 비싸다.

### 6.2 메타데이터

요구사항의 "충돌 정보 / Spawn Point / Navigation / Interactive Object" 를 현재 스키마에
대보면:

| 요구 | 지금 상태 | 계획 |
|---|---|---|
| 충돌 정보 | `MapData.Boxes` — **있다** | 그대로 |
| Spawn Point | `MapData.Spawns` (위치 + yaw) — **있다** | 팀/역할 구분을 2단계에 추가(§8.4) |
| Navigation | `MapData.Grid` (`Standable`/`StairLink`/`FreeFloor`) — **있다** | 그대로. 서버가 실제로 쓰는 것이 이것이다 |
| Interactive Object | **없다** | 2단계. `MapData.Objects[]` 신설 |

즉 **세 개는 이미 있다.** 새로 설계할 것은 Interactive Object 하나뿐이고, 그것도 2단계다.

```csharp
// 2단계에 Shared/Collision/MapData.cs 에 추가
public sealed class MapObject
{
    public string Kind;                  // "altar" | "device" | "door-anchor" …
    public string Id;
    public float X, Y, Z, Yaw;
}
```

**스키마를 늘리면 `MapSchema.Current` 를 2로 올린다.** 해시에는 넣지 않는다 — 지형이
아니기 때문이다. 옛 서버가 새 파일을 조용히 기본값으로 읽는 것을 막는 것이 버전의 일이고,
`MapSchema.IsReadable` 이 미래 버전을 이미 거절한다.

> 지금 제단·장치·문은 **서버가 격자에서 뽑아 배치한다**(`FreeFloor` 로). 그것을 맵 파일에
> 적으면 "배치를 사람이 정한다" 는 다른 설계로 넘어간다. 필요해지기 전에는 넣지 않는다.

---

## 7. NVserver 연동 구조

### 7.1 지금 이미 성립한 것 — 이것이 답이고, 새로 만들 것이 아니다

`NVserver/Shared/` 는 **Unity 로컬 패키지**다
(`manifest.json` 의 `"com.nv.shared": "file:../../NVserver/Shared"`). Unity(IL2CPP,
netstandard2.1) 와 서버(net10.0) 가 **같은 `.cs` 파일**을 컴파일한다. 그래서:

- `MapData` / `MapBox` / `MapSpawn` / `MapGridData` / `MapCellFlags` / `MapSchema` — **한 벌만
  존재한다.** 클라이언트가 채우고 서버가 읽는 자료가 같은 타입이다.
- `MapData.ComputeHash()` — 같은 코드가 양쪽에서 돈다. 그래서 해시 대조가 뜻이 있다.
- `MapGridBuilder.MarkFreeFloor` — 서버의 판정 기준으로 클라이언트가 격자를 채운다.
- `MapDataValidator` — export 전 검사와 서버 로드 검사가 같은 함수다.

**따라서 "Shared 프로젝트에서 공통으로 사용할 데이터 구조" 는 이미 있다.** 이 계획이 할
일은 새 자료 구조를 만드는 것이 아니라, **새 도구가 그 구조로 수렴하게** 하는 것이다.
`MapBlueprint` 는 Shared 에 넣지 않는다 — 그것은 생성 과정의 중간 표현이고, 서버는 알 필요가
없다.

### 7.2 `Shared` 에 코드를 넣을 때의 제약 (어기면 Unity 가 컴파일을 거부한다)

- C# 9 (Unity 의 상한), NuGet 참조 금지, `UnityEngine` 참조 금지, `ImplicitUsings` off
- `System.Numerics.Vector3` 는 **그릇으로만**. `Normalize`/`Length`/`Dot`/`MathF.Sin` 금지
  → `DeterministicMath`
- `dotnet build` 통과는 절반이다. **Unity 에디터가 컴파일하는지 확인해야 한다.**

### 7.3 연동 경로 (변화 없음 — 여기가 좋은 소식이다)

```
MapGeneratorWindow ─ Bake ─► MapBakedAsset ─► BakedMapSource : INetworkMapSource
                                                      │
                              MapExportPipeline.Plan() ┤  ← 기존 코드가 그대로 먹는다
                                                      ▼
                                        NVserver/MapData/{name}.json
                                                      │
                                    서버 기동 시 로드 ─┤ appsettings.json Game:Maps
                                                      ▼
                                        Welcome(0x83) 에 맵 해시 실어 보냄
                                                      │
                              클라이언트가 자기 계산값과 대조 ◄┘
```

---

## 8. 멀티플레이 동기화 고려사항

### 8.1 서버 권한 — 이미 그렇다

서버는 물리 엔진을 쓰지 않고 **export 된 박스 목록으로만** 이동을 판정한다
(`CollisionWorld`). 클라이언트는 입력만 보내고 위치를 보내지 않는다. 맵을 에디터에서 굽는
것은 이 구조를 **강화한다** — 지금은 클라이언트가 런타임에 지형을 만들고 서버는 그것의
export 본을 믿는데, 굳혀 두면 "클라이언트가 이번 실행에서 만든 것" 이라는 변수가 사라진다.

### 8.2 Map ID / Version

| 값 | 지금 | 계획 |
|---|---|---|
| Map ID | `INetworkMapSource.MapName` — 파일명·`Game:Maps` 키·`MapSceneTable` 이 공유 | 그대로. `MapBakedAsset` 이 네 번째 사본이 되지 않도록 **에셋이 이름의 출처**가 되고 나머지가 그것을 읽는다 |
| 스키마 Version | `MapSchema.Current` = 1, 파일의 `version` 필드 | Interactive Object 추가 시 2 |
| 프로토콜 Version | `ProtocolInfo.Version` = 3, 업그레이드 **전에** 확인, 불일치는 426 | 무관 — 일부러 분리되어 있다 |

맵 자체의 "버전" 을 따로 두지 않는다. **해시가 그 일을 한다** — 지형이 바뀌면 해시가 바뀌고,
바뀌지 않으면 같은 맵이다. 별도 버전 숫자는 사람이 올리는 것을 잊는 값이다.

### 8.3 검증 (Hash/Checksum)

- `MapData.ComputeHash()` — FNV 계열(`StateHash`). 이름 + 박스 목록(순서대로) + 격자(있을 때만).
- `Version` 과 `Source` 는 **일부러 빠져 있다.** 재-export 마다 해시가 바뀌면 대조가 뜻을 잃는다.
- **새 도구가 깨기 쉬운 곳:** 베이크된 프리팹에서 박스를 복원할 때 순서. §2.2 결정 2 가 이걸
  막는다(박스의 출처는 프리팹이 아니라 에셋).
- 대조 시점: `Welcome`(0x83) 수신 프레임. `NetworkBootstrap.OnWelcome` 이 `BuildMapDataCached`
  로 계산한다 — 굳은 맵에서는 이 계산조차 필요 없어지고, 에셋에 저장된 해시를 읽으면 된다
  (접속 프레임의 비용이 0 이 된다. 지금은 736박스 × 2450셀 겹침 해소를 캐시로 피하고 있다).

### 8.4 Spawn Point / 팀 시작 위치

지금 `MapSpawn` 은 `X/Y/Z/Yaw` 뿐이고 8개가 링으로 놓인다(= 서버 `Room.MaxPlayers`).
비대칭 게임인데 **Seeker/Runner 구분이 없다.** 2단계에 필드 하나를 더한다:

```csharp
public sealed class MapSpawn { … public int Team; }   // 0 = any, 1 = seeker, 2 = runner
```

`Team = 0` 이 기본값이므로 **옛 파일은 그대로 읽힌다.** 지금 배치가 "역할 무관 8개" 라는
뜻이고, 그게 맞다.

### 8.5 Static Object vs Network Object

| | 판정 | 어디에 있나 | export |
|---|---|---|---|
| **Static** | 매치 중 절대 안 움직인다 | 프리팹 / `MapBakedAsset.Boxes` | 박스 목록에 들어간다 |
| **Network** | 서버가 상태를 소유하고 스냅샷/불리틴으로 전파 | 서버 메모리. 클라이언트는 `ObjectiveState`/`MatchState` 로 받음 | **들어가지 않는다** |

경계선의 판정 기준: **"missing it leave a wrong state behind?"** (ADR 0003). 벽은 절대 변하지
않으니 static, 열쇠·문·장치는 상태가 있으니 network. 씬에 손으로 놓은
`NVCollisionVolume` 은 static 이고 export 시 정렬되어 박스 목록에 붙는다(순서가 해시에
들어가므로 `FindObjectsByType` 의 미규정 순서를 정렬로 고정한다 — 이미 그렇게 되어 있다).

**새 도구가 지켜야 할 규칙:** 생성기가 만드는 것은 전부 static 이다. 생성기에서 network
object 를 만들지 않는다.

### 8.6 Chunk Streaming / 대형 맵

**MVP 에서 구현하지 않는다.** 다만 두 가지를 미리 만들어 둔다:

1. `MapBlueprint.Pieces` 는 목록이므로 나중에 `ChunkId` 필드를 붙일 수 있다.
2. 서버의 콜리전은 **브로드페이즈가 없다** — 겹침 해소가 박스 목록을 선형으로 훑는다.
   35×35 2층 = 736박스에서 이미 접속 프레임에 문제가 됐고(캐시로 피했다), 그 위로 키우면
   여기가 먼저 무너진다. **대형 맵은 청크 스트리밍보다 서버 브로드페이즈가 먼저다.**
   (`NVserver/docs/` 에 "브로드페이즈를 재 보고 하지 않기로 한다" 는 결정이 이미 있다 —
   맵을 키우기로 하면 그 결정을 다시 열어야 한다.)

---

## 9. 단계별 구현 계획

### 0단계 — 준비 (0.5일)

- `ILevelQuery` 추출. `BackroomsMapGenerator` 가 구현하게 하고, 호출부 4개
  (`MatchManager`, `MatchBootstrap`, `GameHudController`, `MatchMapView`) 를 인터페이스로 바꾼다.
- **동작은 하나도 바뀌지 않는다.** `dotnet build Assembly-CSharp.csproj` 로 컴파일 확인
  (새 `.cs` 는 `<Compile Include>` 를 손으로 추가해야 한다 — 안 그러면 `CS0234`).
- 검증: 기존 EditMode 테스트 24개 통과.

### 1단계 — MVP: 관통 (1.5일) ★

**목표: `test-room` 을 새 경로로 만들어 export 하고, 두 클라이언트가 붙는다.**

1. `MapBlueprint`, `MapPiece`, `MapGeneratorSettings`, `MapBakedAsset`
2. `IMapGenerator`, `MapGeneratorRegistry`(`TypeCache`), `TestRoomGenerator`
3. `MapSceneBuilder` — blueprint → GameObject, Undo 지원
4. `BakedMapSource` — `INetworkMapSource` 구현. `ComputeCollision()` 은 에셋을 그대로 반환
5. `MapGeneratorWindow` — Type/Seed/Size/Generate Preview/Build In Scene/Bake
6. `MapBakePipeline` — 프리팹 + `MapBakedAsset` 저장

**완료 판정:** `MultiplayerTest` 씬을 새 도구로 다시 만들고 `Export Map Collision` 을
돌렸을 때 **바이트가 기존 `test-room.json` 과 같다.** (기존 파이프라인이 "내용이 같으면 쓰지
않았다" 를 출력하면 통과. 이것이 가장 싼 회귀 검증이다.)

### 2단계 — Backrooms 이식 (2일)

1. `BackroomsGenerator` — §1.2 의 A + B 를 옮긴다. 난수 인출 순서를 바꾸지 않는다
2. 연출(C)을 `BackroomsAmbience` MonoBehaviour 로 분리 — 안개, 험, 점멸, x-ray
3. `BakedMapSource` 에 `ILevelQuery` 구현 추가 — 격자는 에셋에서 읽는다
4. NavMesh 를 에디터에서 굽고 에셋으로 저장

**완료 판정:** `backrooms.json` 이 바이트 단위로 같다. `SampleScene` 을 플레이해 매치가
정상 진행된다(열쇠 10개, 문, 장치 9개, 체인 드래그).

### 3단계 — 확장 슬롯 (1일)

- 창에 프리셋(`MapGeneratorSettings.asset`) 저장/불러오기
- 격자 미리보기 텍스처
- `IMapWriter` 인터페이스 정의 + `JsonMapWriter`
- 씬↔에셋 불일치 감지 경고

### 보류 (하지 않는다)

`BinaryMapWriter`, `MapObject`(Interactive Object), `MapSpawn.Team`, 청크 스트리밍,
런타임 생성 코드 삭제 — **`BackroomsMapGenerator` 는 남겨 둔다.** 3단계까지 끝나도 지우지
않는다. 굳은 맵이 실전에서 한 라운드도 안 돌아 봤는데 되돌아갈 길을 먼저 없애는 것은 손해다.

---

## 10. 예상 리스크 및 개선 포인트

| | 리스크 | 왜 위험한가 | 대응 |
|---|---|---|---|
| **R1** | **프리팹이 거대하다** | Backrooms 는 셀마다 카펫+천장 타일이라 2층 35×35 에서 **수천 개** GameObject 다. `.prefab` 이 수 MB, git diff 불가, 씬 로드 느림, YAML 병합 충돌 | 벽·계단은 개별 오브젝트로 두고, **카펫/천장 타일은 층마다 하나의 병합 메시**(`Mesh.CombineMeshes`)로 굽는다. 콜리전은 어차피 에셋에서 오므로 시각 지오메트리를 합쳐도 판정이 안 바뀐다. **1단계 착수 전에 `test-room` 으로 프리팹 크기를 실측하고 결정한다** |
| **R2** | **씬을 손으로 고치면 에셋과 갈린다** | 벽 하나를 옮겨도 `MapBakedAsset` 은 모른다. export 는 에셋을 쓰므로 클라이언트에는 옮긴 벽이 있고 서버에는 없다. **맵 해시는 그때도 일치한다** — 기존 코드의 `RejectedVolumes` 주석이 지적하는 바로 그 실패 모양 | 베이크 시 blueprint 의 해시를 프리팹 루트 컴포넌트에 적어 두고, 씬 빌더가 씬의 박스를 세어 대조한다. 다르면 창이 **빨간 문장으로** 말한다. 완전 자동 동기화는 하지 않는다 — 사람이 손으로 고치는 것을 막을 이유는 없고, 다시 굽지 않은 것만 막으면 된다 |
| **R3** | **난수 인출 순서를 옮기다 어긋난다** | `CarveRooms` 의 `random.Next` 4회 / `ConnectRooms` 의 `NextDouble` / `CarveCorridor` 의 `Next(2)` / `BuildLights` 의 `NextDouble` — 순서가 하나만 밀려도 완전히 다른 맵이 나오고, 증상은 "맵이 좀 다른데?" 뿐이다 | 이식 **전에** 현재 씨드 0 의 blueprint 를 덤프해 골든 파일로 저장하고, 이식 후 박스 목록을 그것과 대조하는 EditMode 테스트를 먼저 쓴다. `MapExportValidationTests.cs` 옆에 놓는다 |
| **R4** | **`ILevelQuery` 추출이 매치 레이어를 깬다** | 4개 파일, 그중 `MatchManager` 는 규칙 전부를 들고 있다. 도메인 리로드 관련 함정(`Instance` 지연 재탐색, `EnsureGrid`)이 인터페이스 뒤로 숨으면 진단이 어려워진다 | 0단계를 **동작 변경 0** 으로 못 박고 따로 커밋한다. `EnsureGrid` 를 인터페이스에 올리지 않는 이유가 이것이다 |
| **R5** | **런타임 연출이 프리팹에 담기지 않는다** | `ApplyAtmosphere` 는 `RenderSettings`(씬 전역), `BuildHum` 은 코드 생성 `AudioClip`, `SetWallTransparency` 는 **공유 머티리얼 인스턴스 하나**를 바꾼다. 머티리얼이 프로젝트 에셋이 되면 x-ray 가 에디터에서 그 에셋을 영구 변경한다 | 연출은 `BackroomsAmbience` 로 런타임에 남긴다. 머티리얼은 베이크하되 `SetWallTransparency` 는 `Awake` 에서 `new Material(shared)` 인스턴스를 만들어 그것을 바꾼다 |
| **R6** | **Unity MCP 로 이 작업을 하면 조용히 실패한다** | `Unity_RunCommand` 는 경고 한 줄에도 `UNEXPECTED_ERROR` 를 뱉고, 그 안의 `Undo.*` 는 롤백되며, `System.Reflection` 은 바로 죽는다 | 이 도구는 **에디터 창으로** 만든다. MCP 는 컴파일 확인과 콘솔 읽기에만 쓴다. `dotnet build Assembly-CSharp-Editor.csproj` 로 타입 체크가 먼저 |
| **R7** | **범위가 새어 나간다** | Interactive Object, 바이너리 포맷, 청크 스트리밍, 팀 스폰 — 전부 "설계는 해 두자" 로 시작해 구현으로 번지기 쉽다 | §9 의 "보류" 목록을 지킨다. 인터페이스 정의까지만 하고 구현 파일을 만들지 않는다 |

### 개선 포인트 (이 작업이 덤으로 가져오는 것)

1. **`ComputeCollision()` 이라는 두 번째 경로가 사라진다.** 지금 `Generate` 와
   `ComputeCollision` 은 난수 인출 순서를 서로 맞춰야 하는 두 경로이고, 그 계약은 코드
   어디에도 강제되지 않는다(주석으로만 적혀 있다). 굳은 맵에는 경로가 하나뿐이다.
2. **접속 프레임의 비용이 0 이 된다.** `BuildMapDataCached` 가 존재하는 이유가 "접속하는
   프레임에 2450셀 × 736박스 겹침 해소가 돈다" 는 것인데, 에셋에 해시를 적어 두면 그 계산
   자체가 없어진다.
3. **도메인 리로드 함정 하나가 사라진다.** `EnsureGrid()` 는 스크립트를 고치면 격자가 날아가
   "레벨이 없다" 를 조용히 답하는 것을 막는 장치다. 격자가 에셋이면 `ScriptableObject` 라
   리로드를 견딘다.
4. **맵을 고치는 데 Play 를 누르지 않아도 된다.** 지금은 씨드를 바꿔 결과를 보려면 Play →
   확인 → Stop → 수정을 돈다.

---

## 부록 — 손대지 않는 것 목록 (실수 방지)

- `NVserver/Shared/Collision/*` — `MapObject`/`MapSpawn.Team` 추가(2단계 이후) 외에는 그대로
- `MapExportPipeline.cs`, `MapExportWindow.cs`, `MapCollisionExporter.cs` — 읽기만 한다
- `MapExport.cs` (`BuildMapData`, `AppendSceneVolumes`, `AttachGrid`, `FindAllInScene`) — 그대로
- `MapSceneTable.cs` — 맵을 늘릴 때 한 줄만
- `MapExportValidationTests.cs` 24개 — 하나도 깨지지 않아야 한다
- `MapData.ComputeHash()` — **절대 건드리지 않는다**
