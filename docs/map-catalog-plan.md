# 맵 카탈로그 계획 — 생성한 맵을 방 만들기에서 고를 수 있게 한다

목표는 네 줄이다.

1. **MainLobby ▸ 방 만들기**에서 서버가 실제로 들고 있는 맵 목록을 골라 방을 만든다.
2. **서버**가 그 목록과 메타데이터를 내주는 엔드포인트를 갖는다.
3. 고른 맵이 방에 남고, 게임 시작 시 그 맵이 열린다.
4. **맵을 하나 더 붙일 때 코드를 고치지 않는다** — 도구를 돌리고 파일을 떨어뜨리면 끝난다.

이 문서는 `docs/map-generator-tool-plan.md` 의 후속이다. 그 계획이 "맵을 에디터에서 굳힌다"
까지였고, 여기부터는 "굳힌 맵을 서버와 로비가 알아본다" 다.

---

## 1. 지금 어떻게 되어 있는가

### 1.1 이미 관통되어 있는 것 (놀랍게도 대부분)

방 생성 시 `mapId` 를 서버에 넘기고 룸에 남기는 경로는 **이미 있다.** 새로 만들 것이 아니다.

| 자리 | 파일 | 지금 하는 일 |
|---|---|---|
| 요청 | `NVproject/Assets/Scripts/Net/Session/RoomApi.cs:39` | `POST /rooms` 에 `{ map, isPublic }` 을 싣는다 |
| 접수 | `NVserver/Modules/Realtime/Transport/RealtimeEndpoints.cs:45` | 맵 id 를 받아 `RoomRegistry.TryCreate` 로 넘긴다 |
| 검증 | `NVserver/Modules/Realtime/Contracts/RoomMaps.cs:60` | 등록되지 않은 id 는 `null` → `400 unknownMap`. **기본 맵으로 대신 열지 않는다** |
| 보관 | `NVserver/Modules/Realtime/Simulation/Room.cs:107` | 룸이 `WorldMap` 을 들고 산다 |
| 회신 | `RoomHttpContracts.cs:21` / `:49` | `CreateRoomResponse.MapName`, `RoomInfoResponse.MapName` |
| 씬 선택 | `NVproject/Assets/Scripts/Net/Session/SessionSceneRouter.cs:58` | 룸의 `MapName` 으로 열 씬을 정한다 |

즉 **와이어와 계약은 이미 맵별 룸을 지원한다.** 막혀 있는 것은 목록의 출처와 클라이언트가
그 맵을 그릴 수 있는가 두 가지뿐이다.

### 1.2 막혀 있는 것

**서버는 목록을 내주지 않는다.** `RoomMaps.ByMap` 의 주석에 이미 "기동 로그와 맵 선택 화면용"
이라고 적혀 있지만(`RoomMaps.cs:76`) 그것을 내주는 엔드포인트가 없다.

**클라이언트가 목록을 손으로 들고 있다.** `CreateRoomPopup.cs:19`:

```csharp
private static readonly string[] MapIds = { "default", "test-room" };
private static readonly string[] MapNotes = { "Backrooms — 실제 매치용 맵", "Test Room — 개발용 작은 맵" };
```

두 배열의 인덱스가 짝이고, 서버 `appsettings.json` 의 `Game:Maps` 키와 손으로 맞춰야 한다.
파일 머리의 주석이 이 상황을 정확히 인정하고 있다 — "표가 낡으면 화면에 400 으로 뜬다".

**맵이 등록되는 유일한 방법이 설정 파일 편집이다.** `ModuleRegistration.LoadMaps`
(`ModuleRegistration.cs:251`)가 `Game:Maps` 의 자식만 읽는다. `MapData/` 에 파일이 있어도
설정에 한 줄이 없으면 서버는 그 맵을 모른다. export 도구는 그것을 **경고만** 한다
(`MapExportPipeline.CheckRegistration`) — 에디터가 서버 설정을 고치는 것은 되돌리기 어렵다는
판단이었고, 그 판단 자체는 맞다. 결과적으로 `backrooms2f` 가 정확히 그렇게 죽었다(그 주석에
적혀 있다).

**식별자 공간이 둘이다.** `Game:Maps:default` → `../MapData/backrooms.json` 이고 그 파일의
`name` 은 `backrooms` 다. 그래서 **맵 id 는 `default`, 맵 이름은 `backrooms`** 다. 방 만들기
팝업은 id 로 말하고 씬 라우터는 이름으로 말한다. 지금은 둘 다 우연히 맞지만
(`MapSceneTable` 에 `default` 항목이 없어도 라우터가 서버가 준 `MapName`=`backrooms` 를 보므로
찾아진다) 표를 하나 늘릴 때마다 어느 쪽 이름인지 물어야 한다.

**맵마다 씬이 하나씩 있어야 하고, 그 짝이 코드에 있다.** `MapSceneTable.cs:29`:

```csharp
{ "backrooms", "SampleScene" },
{ "test-room", "MultiplayerTest" },
```

그리고 그 씬은 **Build Settings 에 등록되어 있어야 한다**(`SceneManager.LoadScene` 이 이름으로
찾는다). 현재 등록된 씬은 셋이다 — `MainLobby`, `MultiplayerTest`, `SampleScene`. 맵을 하나
늘리면 씬 하나 + 표 한 줄 + Build Settings 한 줄이다. **이것이 확장성의 진짜 벽이다.**

**메타데이터가 어디에도 없다.** 사람이 읽을 이름, 설명, 층 수, 크기, 권장 인원 — 하나도 없다.
지금 화면에 뜨는 설명은 위의 `MapNotes` 배열, 즉 클라이언트 코드에 박힌 문장이다.

**굳은 맵 자체는 이미 공용 구조에 가깝다.** 생성기가 내놓는 것이 셋이고 셋 다 제자리에 있다:

| 산출물 | 어디에 | 무엇의 출처인가 |
|---|---|---|
| `MapBakedAsset` | `Assets/Settings/Maps/{name}.asset` | **서버에 무엇을 말할지의 출처.** 박스·스폰·격자·조명 |
| 프리팹 | `Assets/Prefabs/Maps/{name}.prefab` | 그것이 어떻게 보이는지 |
| JSON | `NVserver/MapData/{name}.json` | 서버가 판정에 쓰는 것 |

`BakedMapSource` 하나가 `INetworkMapSource`(export)와 `ILevelQuery`(매치)를 동시에 답한다.
**공용 구조를 새로 설계할 필요가 없다** — 이 셋을 서버와 로비가 알아보게 잇는 일만 남았다.

---

## 2. 격차 목록

| | 격차 | 지금의 증상 |
|---|---|---|
| **G1** | 서버에 맵 목록 API 가 없다 | 클라이언트가 목록을 손으로 들고 있다 |
| **G2** | 방 만들기 팝업의 맵 표가 하드코딩 | 표가 낡으면 `400 unknownMap` |
| **G3** | 맵 등록이 `appsettings.json` 수동 편집 | export 는 성공하고 방은 안 만들어진다 |
| **G4** | 맵 id 와 맵 이름이 다른 공간 (`default` ↔ `backrooms`) | 표를 늘릴 때 어느 이름인지 매번 확인 |
| **G5** | 맵당 씬 하나 + 코드의 짝 표 + Build Settings | 맵 추가에 코드 수정 3곳 |
| **G6** | 메타데이터가 없다 | 화면에 보여 줄 이름·설명·크기가 코드에 박힌 문장 |
| **G7** | 클라이언트가 못 그리는 맵을 서버가 열 수 있다 | 접속 후 맵 해시 불일치 또는 "씬이 없다" 로그 |

G7 은 새로 만드는 문제가 아니라 **드러나는** 문제다. 지금은 목록이 하드코딩이라 고를 수 없는
맵이 화면에 뜨지 않는다. 목록을 서버에서 받는 순간 "서버에는 있는데 이 빌드에는 없는 맵" 이
화면에 뜰 수 있게 되고, 그것을 정직하게 다루는 것이 설계의 일부가 된다.

### 1.3 작업 사본에 남아 있는 것 (이 계획과 별개로 정리해야 한다)

- `Assets/Scenes/BackroomScene.unity` 와 `BackroomScene1.unity` 가 커밋되지 않은 채 있다.
  앞의 것은 `SampleScene` 과 **같은 1080줄**, 즉 사본이다. 뒤의 것은 거의 비어 있다.
- 그래서 지금의 `backrooms.json` 은 `"source": { "scene": "BackroomScene", … }` 다 —
  export 를 돌린 창이 그 씬에서 열려 있었다는 뜻이고, 그 씬은 저장소에 없다. 출처 필드가
  **다른 사람에게는 아무 말도 해 주지 않는 상태**다. 이 계획을 시작하기 전에 씬을 정리하고
  한 번 재-export 하는 것이 맞다.
- `test-room.json` 에는 `version` 필드가 없다(= `MapSchema.Unversioned`). 2단계의 v1 관용
  경로가 가정이 아니라 지금 실제로 필요한 경로라는 뜻이다.

---

## 3. 설계 결정

### D1. 맵 id 는 맵 이름이다. `default` 는 별칭이다

canonical id = 파일명 stem = JSON 의 `name` = `MapBakedAsset.MapName` = export 파일명.
**넷이 같은 문자열이고 기동 때 그것을 검사한다** — 파일명과 `name` 이 다르면 기동을 멈춘다.

`default` 는 지우지 않는다. `Game:StaticRooms`, 맵을 지정하지 않은 요청, 옛 클라이언트가
그것을 쓰고 있고, `RoomMaps` 는 `default` 항목이 없으면 생성자에서 던진다(`RoomMaps.cs:36`).
대신 **별칭 표**로 남긴다 — `default` → `backrooms`. `ByMapId` 가 별칭을 먼저 푼다.

이 결정으로 G4 가 사라지고, `RoomSummary` 에 `MapId` 를 새로 붙일 필요도 없어진다
(`MapName` 이 곧 id 다). 룸은 `WorldMap` 만 들고 있으면 되고 지금 그렇다.

`CreateRoomResponse.Map` 은 요청한 id 를 되돌려주는 것이 아니라 **해석된 canonical id** 를
돌려준다. 클라이언트가 `default` 로 만들어도 자기 방의 맵이 `backrooms` 라는 것을 안다.

### D2. 맵 등록은 디렉터리에 파일을 떨어뜨리는 것이다

`Game:MapDirectory`(기본 `../MapData`)를 기동 때 훑어 `*.json` 전부를 맵으로 등록한다.
`Game:Maps` 는 **명시 등록/별칭 표**로 남기지만 그것 없이도 맵이 등록된다.

이것이 G3 을 없애는 유일한 방법이다. 대안(에디터가 `appsettings.json` 을 고친다)은 이미
거절된 설계고, 그 거절은 맞다 — 에디터가 서버 설정을 고치면 되돌릴 자리가 없다.

**깨진 파일 하나가 기동을 멈춘다.** 이 저장소의 규칙이 그것이다(`MapLoader` 의 머리 주석,
`GuardDevelopmentOnlyOptions`, 원격 호스트 + `secure` 꺼짐 빌드 거부 — 전부 같은 판단).
`MapData/` 에 파일이 있는 것은 사고가 아니라 누군가 export 를 돌린 결과이므로, 못 읽는 파일은
조용히 건너뛸 것이 아니라 시끄럽게 실패할 것이다. 지금 그 폴더에는 `backrooms.json` 과
`test-room.json` 둘뿐이라 이 전환에 걸릴 파일이 없다.

### D3. 메타데이터는 맵 JSON 안에 산다. 스키마 v2

`MapData` 에 `meta` 블록을 붙이고 `MapSchema.Current` 를 **2** 로 올린다.

사이드카 파일(`{name}.meta.json`)을 만들지 않는다. export 는 이미 원자적으로 한 파일을 쓰고
있고(`MapExportPipeline.TryWrite`), 파일이 둘이면 한쪽만 오래된 상태가 생긴다 — 그리고 그
상태를 감지할 방법이 없다.

**해시에 넣지 않는다.** `Version` 과 `Source` 가 그렇고 이유가 같다 — 맵 해시는 "같은 지형인가"
를 답해야 하고 표시용 문장은 지형이 아니다(`MapData.cs:73` 의 주석이 이 규칙을 못 박아 두었다).
따라서 `meta` 를 도입해도 **기존 맵의 해시가 바뀌지 않고 재-export 가 강제되지 않는다.**

**v1 파일(meta 없음)은 거절하지 않는다.** 서버가 맵 자체에서 합성한다 — 이름은 `name`,
층/크기는 `grid`, 스폰 수는 `spawns`. `MapSchema.Unversioned` 를 받아 주는 것과 같은 논리다.

### D4. 클라이언트는 `MapCatalog` 에셋으로 자기가 그릴 수 있는 맵을 안다

`Assets/Resources/MapCatalog.asset` (ScriptableObject). 항목마다:

- `mapId` (= 맵 이름)
- `MapBakedAsset` 참조 — 격자·박스·스폰의 출처
- 프리팹 참조 — 보이는 쪽
- 표시용 이름/설명 (`MapGeneratorSettings` 에서 넘어온다)
- 베이크 시점의 맵 해시 — **서버 목록의 해시와 비교해 접속 전에 경고할 수 있다**
- 씬 재정의 (비면 공용 런타임 씬)

**베이크 파이프라인이 이 에셋을 갱신한다.** 사람이 손으로 유지하는 표를 하나 더 만들면 그것이
낡는다 — 지금 `CreateRoomPopup` 의 배열이 정확히 그 상태다. `MapGeneratorRegistry` 가 타입을
*찾는* 것과 같은 판단이다("Found rather than listed").

`MapSceneTable` 은 **지우지 않고 재정의 표로 축소한다.** `test-room` ↔ `MultiplayerTest`,
`backrooms` ↔ `SampleScene` 은 개발 루프가 쓰는 짝이고, 그 두 씬은 맵 말고도 다른 것을 담고
있다. 카탈로그에 재정의가 있으면 그 씬을, 없으면 공용 런타임 씬을 연다.

### D5. 공용 런타임 씬 하나가 어떤 맵이든 연다

`Assets/Scenes/MapRuntime.unity` — Build Settings 에 **한 번** 등록한다. 이 씬은 레벨을 담지
않고, 룸의 맵 id 로 카탈로그에서 프리팹을 찾아 인스턴스화한다. 플레이어·매치 레이어·HUD·
Global Volume 은 씬에 있다.

이것이 G5 를 없애는 부분이고, **가장 큰 작업이며 Play 로만 확인된다.** 그래서 마지막 단계다.

### D6. 목록은 서버 ∩ 클라이언트로 보여 주고, 교집합 밖도 이유와 함께 보여 준다

WebGL 빌드는 에셋을 구워서 나간다. **서버에 맵을 추가하는 것만으로는 이미 배포된 클라이언트가
그 맵을 그릴 수 없다.** 이것은 고칠 수 있는 버그가 아니라 구조다(Addressables 가 프로젝트에
없고, 도입은 이 작업의 범위가 아니다).

그래서 팝업은 세 부류를 구분해 보여 준다:

| 상태 | 판정 | 화면 |
|---|---|---|
| 고를 수 있다 | 서버에 있고 카탈로그에도 있고 해시가 같다 | 정상 항목 |
| 이 빌드에 없다 | 서버에만 있다 | 비활성 + "이 빌드에는 이 맵이 없다. 클라이언트를 업데이트한다" |
| 서버에 없다 | 카탈로그에만 있다 | 비활성 + "서버에 등록되지 않았다. `MapData/` 에 export 했는지 확인한다" |
| 지형이 다르다 | 양쪽에 있으나 해시가 다르다 | 비활성 + "서버의 맵이 이 빌드의 것과 다르다. 재-export 또는 재빌드" |

마지막 줄이 덤으로 얻는 것이다 — 지금은 이 상황이 **접속한 뒤** 맵 해시 불일치로만 드러난다.
목록에 해시가 실리면 방을 만들기 전에 말할 수 있다.

이유 없이 꺼진 버튼은 고장으로 읽힌다 — `RoomService.CanQuickJoin` 이 이미 그 규칙을 지키고
있고 여기서도 같다.

---

## 4. 데이터 구조

### 4.1 `Shared/Collision/MapData.cs` — `meta` 추가

```csharp
public sealed class MapData
{
    public int Version { get; set; }          // 2
    public string Name { get; set; }
    public MapBox[] Boxes { get; set; }
    public MapSpawn[] Spawns { get; set; }
    public MapGridData Grid { get; set; }
    public MapSourceInfo Source { get; set; }
    public MapMetaInfo Meta { get; set; }     // ← 새로. 없을 수 있다. 해시에 들어가지 않는다
}

/// 사람에게 보여 줄 값. **판정에 쓰지 않는다.**
public sealed class MapMetaInfo
{
    public string DisplayName { get; set; }        // "Backrooms"
    public string Description { get; set; }        // 한 줄
    public int RecommendedPlayersMin { get; set; }
    public int RecommendedPlayersMax { get; set; }
    public string[] Tags { get; set; }             // "match", "dev", "small"
}
```

`Shared` 의 규칙을 그대로 지킨다 — C# 9, NuGet 없음, `UnityEngine` 없음. `string[]` 과
`int` 뿐이므로 걸릴 것이 없다.

`ComputeHash()` 는 **한 줄도 고치지 않는다.**

썸네일은 넣지 않는다(§8). 격자 미리보기 텍스처가 이미 생성기 창에 있으므로 나중에 붙일 자리는
있다.

### 4.2 서버 — `RoomMaps` 확장

```csharp
public sealed class RoomMaps
{
    public const string DefaultMapId = "default";

    // id → 맵. id 는 canonical(= 맵 이름).
    // 별칭 → canonical id. `default` 가 그것이다.
    public WorldMap? ByMapId(string? mapId);      // 별칭을 먼저 푼다
    public string? ResolveId(string? mapId);      // canonical id. 모르면 null
    public IReadOnlyDictionary<string, WorldMap> ByMap { get; }
}
```

`RoomMaps` 는 `Realtime/Contracts` 에 있고 모듈 밖으로 나가도 안전한 불변 형이다 — 지금 그렇고
그대로 둔다. 파일을 읽는 것은 계속 `Api` 의 컴포지션 루트다.

### 4.3 서버 — 디렉터리 스캔 (`ModuleRegistration.LoadMaps`)

순서:

1. `Game:MapDirectory` 를 훑어 `*.json` 전부를 `MapLoader.Load`
2. **파일명 stem ≠ `data.Name` 이면 기동 실패** (D1 의 검사)
3. `Game:Maps` 의 항목을 읽는다 — 값이 파일 경로면 명시 등록, 이미 등록된 맵 id 면 별칭
4. `default` 별칭이 없고 `default` 라는 맵도 없으면 `backrooms` → `default` 를 붙인다
   (`Game:MapPath` 하위 호환 경로는 그대로 남긴다)
5. 기동 로그에 맵마다 한 줄 — `LogLoadedMaps` 가 이미 그것을 하고 있고, 별칭과 `supportsMatch`
   를 덧붙인다

전환 후의 `appsettings.json`:

```jsonc
"Game": {
  "MapDirectory": "../MapData",
  "Maps": { "default": "backrooms" },   // ← 별칭. 경로가 아니다
  "StaticRooms": { "test": "test-room" }
}
```

### 4.4 클라이언트 — `MapCatalog`

```csharp
public sealed class MapCatalog : ScriptableObject   // Assets/Resources/MapCatalog.asset
{
    [SerializeField] private MapCatalogEntry[] entries;

    public MapCatalogEntry Find(string mapId);
    public static MapCatalog Load();     // Resources.Load, 캐시
}

[Serializable] public sealed class MapCatalogEntry
{
    public string mapId;                 // = MapBakedAsset.MapName
    public MapBakedAsset asset;
    public GameObject prefab;
    public string displayName;
    public string description;
    public uint bakedHash;               // 접속 전 대조용
    public string sceneOverride;         // 비면 공용 런타임 씬
}
```

`MapBakedAsset` 에 `displayName`/`description`/`recommendedPlayers` 를 **함께** 넣는다. 그래야
`MapGeneratorSettings` → `MapBakedAsset` → (export) JSON `meta` → 서버 → 로비, 그리고
`MapBakedAsset` → 카탈로그 → 로비 두 경로가 같은 값에서 나온다.

---

## 5. API 계약

### 5.1 `GET /maps` — 새로

```jsonc
[
  {
    "id": "backrooms",
    "displayName": "Backrooms",
    "description": "2층 35×35 미로. 실제 매치용",
    "hash": 3735928559,           // uint. 클라이언트가 자기 베이크 해시와 대조한다
    "schemaVersion": 2,
    "isDefault": true,            // `default` 별칭이 이 맵을 가리킨다
    "supportsMatch": true,        // 격자가 있고 몸이 들어가는 셀이 있다
    "boxCount": 1371,
    "spawnCount": 8,
    "floors": 2, "width": 35, "depth": 35, "cellSize": 3.2,
    "recommendedPlayersMin": 2, "recommendedPlayersMax": 8,
    "tags": ["match"]
  }
]
```

- **어디에 사는가.** `RealtimeEndpoints.Map` 에 붙인다. 새 모듈을 만들지 않는다 — 모듈 추가는
  물어봐야 하는 항목이고, 맵을 소비하는 것은 이미 `Realtime` 이다.
- **레이트리밋.** `RateLimitPolicies.RoomList` 를 나눠 쓴다. 맵 목록은 화면 진입 시 1회이고
  방 목록과 성질이 같다. 새 양동이를 만들면 설정 키가 하나 늘고 얻는 것이 없다.
- **응답은 기동 때 한 번 만든다.** 맵은 기동 후 불변이므로 매 요청에 다시 만들 이유가 없다.
  `ETag` + `Cache-Control: max-age=60` 을 붙인다.
- **CORS.** `ModuleRegistration.CorsPolicy` 가 이미 `GET` 을 허용한다. 손댈 것 없다.
- **`supportsMatch` 는 서버가 판정한다.** 격자가 없는 맵은 열쇠도 문도 생기지 않는다
  (`Room.cs:1480`, `:1916` 이 그것을 로그로 남긴다). "격자 유무" 를 클라이언트에 던지고
  해석을 맡기면 그 해석이 두 곳에 생긴다.
- **버전 질의를 요구하지 않는다.** `GET /rooms/{code}` 는 `?v=` 로 프로토콜 버전을 검사하지만
  (접속 직전 조회이므로 맞다) 맵 목록은 접속 전 화면 구성용이다. 버전이 다른 클라이언트에게도
  목록은 답해 주는 편이 낫다 — 그쪽 실패는 접속 시점에 426 으로 정확히 갈린다.

### 5.2 `POST /rooms` — 응답만 바뀐다

요청 형식은 그대로다(`{ map, isPublic }`). 응답의 `map` 이 **canonical id** 가 된다
(`default` 로 요청해도 `"backrooms"`). `mapName` 은 남긴다 — 지금 클라이언트가 그것으로 씬을
정하고 있고, canonical id 와 같은 값이 되므로 둘이 갈릴 일이 없다.

`400 unknownMap` 의 뜻과 조건은 그대로다.

### 5.3 `GET /rooms/{code}` / `GET /rooms` — 표시용 필드 추가

`RoomInfoResponse` 에 `mapDisplayName` 을 더한다. 방 목록에 "backrooms" 대신 "Backrooms" 를
띄우기 위한 것이다. `mapName` 은 그대로 두고 판정은 계속 그것으로 한다.

### 5.4 프로토콜 버전

**`ProtocolInfo.Version` 은 3 그대로다.** 여기서 바뀌는 것은 HTTP 계약과 파일 스키마뿐이고,
바이너리 프레임은 한 비트도 건드리지 않는다. 옛 서버에 붙은 새 클라이언트는 `GET /maps` 에
404 를 받고 카탈로그만으로 목록을 만든다(§6 의 대체 경로).

---

## 6. UI/UX 변경

### 6.1 방 만들기 팝업

지금은 `DropdownField` + 설명 라벨 하나다. 항목이 두 개일 때는 맞았지만 상태가 넷(§D6)이
되면 드롭다운이 그것을 표현하지 못한다 — 비활성 항목과 그 이유를 담을 자리가 없다.

**세로 목록으로 바꾼다.** 항목마다: 표시 이름 / 한 줄 설명 / 층·크기 / 권장 인원 / 상태 배지.
고를 수 없는 항목은 눌리지 않고 이유를 그 자리에 적는다.

```
┌ 방 만들기 ────────────────────────────────┐
│ 맵                                        │
│ ┌───────────────────────────────────────┐ │
│ │ ● Backrooms                    2–8명  │ │
│ │   2층 35×35 미로. 실제 매치용         │ │
│ ├───────────────────────────────────────┤ │
│ │ ○ Test Room                    2–8명  │ │
│ │   개발용 작은 맵                      │ │
│ ├───────────────────────────────────────┤ │
│ │ ⊘ Arena                               │ │
│ │   이 빌드에는 이 맵이 없다            │ │
│ └───────────────────────────────────────┘ │
│ ☐ 방 목록에 공개                          │
│   방 목록에 뜨지 않는다. 초대 코드를…     │
│              [ 취소 ]  [ 만들기 ]         │
└───────────────────────────────────────────┘
```

- **목록을 받는 중**: 스켈레톤 한 줄 + "맵 목록을 받는 중…". 팝업을 열자마자 요청한다.
- **목록을 못 받았을 때**(구서버 404, 서버 미도달): 카탈로그만으로 목록을 만들고 머리에
  "서버의 맵 목록을 받지 못했다. 이 빌드가 아는 맵만 보인다" 를 적는다. **만들기를 막지 않는다** —
  틀리면 `400 unknownMap` 이 정확히 그것을 말해 주고, 그 실패는 이미 분류되어 있다.
- **마지막 선택 기억**: `nv.{env}.lobby.map`. `PlayerPrefs` 는 환경별로 네임스페이스가 나뉘어
  있고(`NVEnvironment` 규칙) 맵도 그 규칙을 따른다 — 서버가 다르면 있는 맵도 다르다.
  기억한 맵이 지금 목록에 없으면 기본 맵으로 조용히 되돌린다.
- 기본 선택은 `isDefault` 인 맵. 지금의 `MapIds[0]` 과 같은 자리다.

### 6.2 그 밖

| 화면 | 바뀌는 것 |
|---|---|
| 방 목록 (`RoomItemView`) | 맵 표시 이름을 한 줄 더한다. 지금은 코드와 인원만 보인다 |
| 방 안 (`RoomView`) | 참가자가 자기가 어느 맵에 들어왔는지 본다 |
| 실패 토스트 | `UnknownMap` 에 맵 이름을 넣는다 — "'arena' 는 이 서버에 없다. 목록을 새로고침한다" |
| 맵 생성기 창 | 베이크 후 "카탈로그에 등록됨" 한 줄. `appsettings` 등록 경고는 D2 로 사라진다 |

---

## 7. 구현 단계

각 단계는 **따로 커밋되고 그 자체로 회귀가 없다.** 1~3 은 Play 없이 확인되고, 4 는 Play 로만
확인된다 — 그래서 마지막이다.

### 1단계 — 서버: 디렉터리 스캔 + 별칭 + `GET /maps` (0.5일)

손대는 파일: `ModuleRegistration.cs`, `RoomMaps.cs`, `RealtimeEndpoints.cs`,
`RoomHttpContracts.cs`, `appsettings.json`.

- `LoadMaps` 를 스캔으로 바꾸고 파일명 ↔ `name` 검사를 넣는다
- `RoomMaps` 에 별칭과 `ResolveId`
- `GET /maps` + `MapInfoResponse`. 기동 때 한 번 만들고 `ETag`
- `CreateRoomResponse.Map` 을 canonical id 로

**확인:** `dotnet build` 0 경고. `dotnet test` 394개 유지 + 새 테스트.
`curl localhost:5202/maps` 가 두 맵을 답한다. `POST /rooms {"map":"default"}` 와
`{"map":"backrooms"}` 가 같은 방을 만들고 응답의 `map` 이 둘 다 `backrooms` 다.
**클라이언트는 한 줄도 고치지 않았고 그대로 돈다.**

새 테스트: 스캔이 두 파일을 찾는다 / 파일명 불일치가 기동을 멈춘다 / 별칭이 풀린다 /
등록되지 않은 id 가 여전히 거절된다 / `GET /maps` 의 필드 / 격자 없는 맵의 `supportsMatch` 가
false.

### 2단계 — 스키마 v2: `meta` (0.5일)

손대는 파일: `Shared/Collision/MapData.cs`, `MapSchema.cs`, `MapDataValidator.cs`,
`MapExportPipeline.cs`(직렬화), `MapBakedAsset.cs`, `MapGeneratorSettings.cs`.

- `MapMetaInfo` + `MapSchema.Current = 2`
- export 가 `meta` 를 쓴다. `AppendSource` 옆에 `AppendMeta`
- **`ComparisonKey` 를 확인한다** — 지금 `source` 줄 하나만 버리는 구현이고, `meta` 를
  여러 줄로 쓰면 "내용이 같으면 쓰지 않는다" 가 영향을 받는지 봐야 한다. `meta` 는 시각처럼
  매번 바뀌는 값이 아니므로 **비교에 남긴다**(고치면 다시 써야 하는 게 맞다)
- 서버가 `meta` 없는 파일에서 값을 합성한다
- 두 맵을 재-export 해 `meta` 를 채운다

**확인:** `MapData.ComputeHash()` 가 안 바뀌었다 — `meta` 를 붙인 전후로 두 맵의 해시가
같다는 테스트를 먼저 쓴다. `ExportedMapTests` 24개 유지. v1 파일이 여전히 로드된다.

### 3단계 — 클라이언트 로비: 카탈로그 + API 기반 목록 (1일)

손대는 파일: `MapCatalog.cs`(새), `CreateRoomPopup.cs`, `RoomApi.cs`, `MapsResult.cs`(새),
`LobbyService.cs`/`RoomService.cs`, `MainLobbyAssets` 의 UXML/USS, `MapBakePipeline.cs`.

- `MapCatalog` 에셋 + 베이크 파이프라인이 항목을 갱신
- `RoomApi.Maps()` 코루틴. 404 는 실패가 아니라 "이 서버는 목록을 안 준다"
  (`List()` 의 `NotPublished` 와 같은 처리)
- 팝업을 세로 목록으로. 네 상태와 해시 대조
- 마지막 선택 기억

**이 단계에서 씬은 건드리지 않는다.** 고를 수 있는 맵이 여전히 둘이므로 라우팅에 회귀가 없다.
G1·G2·G6·G7 이 여기서 닫힌다.

**확인:** 에디터 Play + 2클라이언트 빌드로 방 만들기 → 시작 → 두 맵 각각. 서버를 끄고
팝업을 열어 대체 경로. `appsettings` 의 별칭을 지워 `default` 가 없는 서버에서의 동작.

### 4단계 — 공용 런타임 씬 (1.5일, Play 검증 필수)

손대는 파일: `Assets/Scenes/MapRuntime.unity`(새, `Assets/Editor/Scene/` 에 생성 메뉴 추가),
`MapSceneTable.cs`, `SessionSceneRouter.cs`, `MapCatalog`.

- `MapRuntime` 씬을 **코드로 생성하는 메뉴**를 만든다 — 이 저장소는 씬을 그렇게 만든다
  (`Create Main Lobby Scene`, `Create Multiplayer Test Scene`)
- 씬이 룸의 맵 id 로 카탈로그에서 프리팹을 찾아 인스턴스화
- `MapSceneTable` 을 재정의 표로 축소. 라우터는 재정의 → 없으면 `MapRuntime`
- Build Settings 에 `MapRuntime` 한 줄 (생성 메뉴가 등록까지 한다 — 로비 씬이 index 0 을
  다시 못 박는 것과 같은 방식)

**확인:** `SampleScene` 경로가 그대로 돈다(재정의가 남아 있으므로). `MapRuntime` 으로
`backrooms` 를 열어 매치가 정상 진행된다 — 열쇠 10개, 문, 장치 9개, 거울.

### 5단계 — 검증과 기록 (0.5일)

- **수용 기준: 세 번째 맵을 코드 수정 0으로 붙인다.** 생성기로 새 맵을 굽고 export 하고,
  서버를 재기동하고, 로비에서 그것을 골라 방을 만들고 2인 매치를 한 판 돈다.
  `.cs` 파일 diff 가 0 이어야 한다
- 로비 표시(§6.2), 실패 토스트 문장
- `NVserver/docs/conventions.md` 에 걸린 것들을 증상 → 원인 → 고침으로
- `docs/map-generator-tool-plan.md` 에 이 계획의 완료를 잇는다

---

## 8. 하지 않는다

`docs/map-generator-tool-plan.md` §9 의 보류 목록을 그대로 지키고 여기서 더한다.

- **Addressables / AssetBundle 맵 다운로드.** 이것이 있으면 G7 이 진짜로 닫히지만, 패키지 추가 +
  WebGL 호스팅 + 버전 관리가 붙는다. 이 작업의 범위가 아니고, 교집합 UI 로 충분히 정직하다
- **썸네일.** 격자 미리보기 텍스처가 이미 창에 있어 나중에 붙일 자리는 있다. 지금은 텍스처를
  어디에 저장하고 어떻게 로비까지 보낼지가 별 문제다
- **맵 투표 / 로테이션 / 매치메이킹.** `Matchmaking` 모듈은 미구현이고 모듈 추가는 물어봐야 한다
- **런타임 맵 리로드**(서버 무중단으로 `MapData/` 재스캔). 맵이 불변이라는 전제가 `GET /maps`
  캐시와 룸의 `WorldMap` 참조 양쪽에 깔려 있다
- **`BackroomsMapGenerator` 삭제.** 4단계까지 끝나도 남긴다 — 되돌아갈 길을 먼저 없애지 않는다
- **`MapData.ComputeHash()` 수정.** 절대

---

## 9. 리스크

| | 리스크 | 왜 위험한가 | 대응 |
|---|---|---|---|
| **R1** | 디렉터리 스캔이 기동을 멈춘다 | `MapData/` 에 실험용·반쯤 쓰인 파일을 하나 두면 서버가 안 뜨고, 증상은 "갑자기 서버가 안 뜬다" 다 | 그것이 이 저장소의 규칙이라 그대로 간다. 완화는 로그다 — 어느 파일의 어느 줄인지 말한다(`MapLoader` 가 이미 그렇게 한다). export 는 원자적으로 쓰므로 반쯤 쓰인 파일은 정상 경로에서 생기지 않는다 |
| **R2** | 파일명 ↔ `name` 검사가 기존 배포를 깬다 | `backrooms.json` 의 `name` 이 `backrooms` 가 아니면 기동이 멈춘다 | **확인했다** — `backrooms.json` 은 `"name": "backrooms"`, `test-room.json` 은 `"name": "test-room"` 이다. 걸릴 파일이 없다 |
| **R3** | 4단계가 실패하면 되돌릴 수 있어야 한다 | 공용 씬은 Play 로만 확인되므로 착수 전에 결과를 알 수 없다 | 재정의 표(`MapSceneTable`)를 지우지 않는다. 4단계만 되돌리면 1~3단계는 그대로 산다 |
| **R4** | 카탈로그가 낡는다 | 베이크가 갱신하지만 사람이 에셋을 손으로 고칠 수 있다. 낡으면 "고를 수 있는데 못 그리는 맵" 이 된다 | EditMode 테스트 — 카탈로그의 모든 항목이 에셋과 프리팹을 갖는다, 모든 항목의 `bakedHash` 가 그 에셋에서 다시 계산한 값과 같다, `Assets/Settings/Maps/*.asset` 전부가 카탈로그에 있다 |
| **R5** | `meta` 가 해시에 새어 들어간다 | 들어가면 재-export 마다 해시가 바뀌어 맵 해시가 뜻을 잃는다 | 2단계에서 **해시 불변 테스트를 먼저** 쓴다. `MapData.cs:73` 의 주석이 이 규칙을 이미 못 박고 있다 |
| **R6** | `default` 를 지우고 싶어진다 | 지우면 `RoomMaps` 생성자가 던지고(설계상 맞다) `Game:StaticRooms` 와 맵을 지정하지 않는 요청이 죽는다 | 별칭으로 영구히 남긴다. 테스트로 못 박는다 |
| **R7** | `GET /maps` 가 정찰 창구가 된다 | 맵 목록은 비밀이 아니지만 상시 열린 공개 엔드포인트가 하나 늘어난다 | `RoomList` 양동이를 나눠 쓴다. 룸 정보와 달리 응답이 기동 후 불변이라 캐시로 부담이 없다 |
| **R8** | 4단계가 `SampleScene` 을 깬다 | 그 씬에는 런타임 생성기가 아직 서 있고, 매치·거울·연출이 씬에 얽혀 있다 | 재정의 표로 `SampleScene` 을 유지한다. `MapRuntime` 은 **새 맵의 경로**로 먼저 살고, `SampleScene` 교체는 그 다음 판단이다 (`map-generator-tool-plan.md` 가 남긴 4단계 절차) |

---

## 10. 실행 결과 (2026-08-05)

1~4단계를 커밋했다. **각 커밋은 그 자체로 회귀가 없고, 4단계는 씬을 만들기 전까지 동작을
바꾸지 않는다.**

| 단계 | 커밋 | 무엇으로 확인했는가 |
|---|---|---|
| 1 | `373416c` | 서버 테스트 488개(+24). 기동 로그가 `id=backrooms(별칭 default)`. `default` 와 `backrooms` 로 만든 방이 같은 맵, 모르는 id 는 여전히 400, 정적 룸 `test` 그대로, `GET /maps` 두 번째 조회가 304 |
| 2 | `9a05794` | 서버 테스트 495개(+7). `meta` 를 실은 v2 파일을 `MapData/` 에 놓고 서버를 띄워 `GET /maps` 가 그 값을 답하고 스키마 1 인 두 맵은 대신하는 값으로 답하는 것을 확인(그 파일은 지웠다). 해시 불변을 테스트로 고정 |
| 3 | `bc4628d` | 클라이언트 두 어셈블리 0 오류. `POST /rooms`·`GET /rooms` 응답에 표시용 이름이 실린다. EditMode 테스트 12개는 붙였으나 **실행은 에디터 Test Runner 가 필요하다** |
| 4 | `9bd01f4` | 두 어셈블리 0 오류. 라우터가 카탈로그 → 표 → 공용 씬 순으로 답하므로 카탈로그가 없는 지금은 **오늘과 같은 씬을 연다** |

### 이 작업 중에 드러난 것 — 구운 에셋과 배포된 맵이 다른 지형이다

3단계에서 카탈로그를 만들어 보자 해시가 어긋났다. 원인을 찾아보니 값 자체가 어긋난 것이 아니라
**두 파일이 서로 다른 맵이었다.**

| | 박스 | 층 | 몸이 들어가는 셀 | 해시 |
|---|---|---|---|---|
| `NVproject/Assets/Settings/Maps/backrooms.asset` (미커밋) | 359 | **1층** | 294 | `2A293243` |
| `NVserver/MapData/backrooms.json` (커밋됨) | 736 | **2층** | 574 | `7996AF3A` |

에셋을 다른 설정으로 다시 구운 뒤 재-export 하지 않은 상태다. 이 계획 이전부터 그랬고, 새 해시
대조가 그것을 방을 만들기 **전에** 드러낸 첫 사례다.

**그래서 `MapCatalog.asset` 을 커밋하지 않았다.** 씬(`SampleScene`)은 아직 런타임 생성기로
지형을 만들므로 클라이언트가 실제로 그리는 것은 2층 쪽이고, 에셋의 해시를 카탈로그에 적으면
**실제와 무관한 값으로 방 만들기를 막는다.** 카탈로그는 씬이 구운 에셋으로 그려질 때 뜻을 갖는다.

### 검토에서 걸러낸 것 (같은 날, 후속 커밋)

1~5단계를 다시 훑어 **1단계가 남긴 거짓말** 을 찾았다. `Game:Maps` 가 등록 표에서 별칭 표로
바뀌었는데 export 도구는 여전히 "`Game:Maps` 에 등록하라" 고 경고하고 붙여 넣을 조각까지
내주고 있었다 — 문자열 검색이라 `default` 별칭이 있는 `backrooms` 는 우연히 통과하고
`test-room` 만 틀리게 경고했다. **틀린 경고는 없는 경고보다 나쁘다.** `CheckRegistration` 과
`RegistrationSnippet` 을 지우고, 두 창은 "이 디렉터리에 쓰면 등록된다" 를 알린다.

같은 이유로 낡은 문장 일곱 군데를 고쳤다(`MapGeneratorSettings`·`MapBakedAsset`·
`MapBlueprint` 의 툴팁, `MapSceneTable`·`MapCollisionExporter`·`BackroomsMapGenerator` 의 주석,
`SessionFailure` 의 `UnknownMap` 조치 문구 — 그 문구는 사용자에게 별칭 표를 뒤지라고 말하고
있었다). 루트 `CLAUDE.md` 의 맵 등록 단락도 새 모델로 다시 썼다.

코드 결함 둘:

- **팝업이 "맵 목록을 받는 중…" 에서 영원히 멈추는 경로.** `Ensure` 가 이미 조회 중이면 그냥
  돌아갔으므로, 그 사이에 열린 팝업의 갱신 콜백이 버려졌다 — 팝업을 열고 닫고 다시 여는 것만으로
  재현된다. 콜백을 모아 두고 응답이 오면 전부 부른다.
- **라우터가 매 프레임 카탈로그를 뒤졌다.** `Update` 가 매치 내내 `EnterGame` 을 부르는데
  `SceneFor` 가 `Resources.Load` 로 카탈로그를 찾아 훑었다. 이미 들어와 있으면 아무것도 묻지
  않는다 — 덤으로, 씬이 없는 맵의 오류 로그가 프레임마다 찍히던 것도 한 번으로 줄었다.

레이트리밋도 실측했다. `/maps` 를 34번 두드리면 30번째까지 200, 그 뒤 429 이고, 그 시점에
`GET /rooms` 도 429(같은 양동이)이지만 `GET /rooms/{code}` 는 200 이다 — 목록 새로고침이
방에 들어갈 예산을 깎지 않는다는 불변식이 엔드포인트를 하나 늘린 뒤에도 성립한다.

### 남은 일

| | 무엇 | 왜 여기서 멈췄는가 |
|---|---|---|
| A | 구운 에셋과 배포된 맵 중 **어느 쪽이 맞는지 정한다** — 2층으로 다시 굽거나, 1층 에셋을 재-export | 지형을 고르는 것은 내용 결정이다 |
| B | `Tools ▸ NV ▸ Scene ▸ Create Map Runtime Scene` 을 눌러 공용 씬을 만든다 | 메뉴가 Build Settings 등록까지 하지만, 열린 씬을 갈아치우므로 사람이 눌러야 한다 |
| C | 그 씬으로 매치를 한 판 돌린다 — 열쇠 10개, 문, 장치 9개, 거울 | Play 로만 확인된다 |
| D | EditMode 테스트를 Test Runner 로 돌린다(`MapChoiceTests` 12개) | MCP 로는 실행되지 않는다 — `NVproject/CLAUDE.md` 에 이유를 적었다 |
| E | 두 맵을 재-export 해 `meta` 를 채운다 | 에디터가 필요하다. 지금은 스키마 1 이고 대신하는 값으로 정상 동작한다 |
| F | 세 번째 맵을 코드 수정 0으로 붙여 §7 5단계의 수용 기준을 확인 | A~C 가 끝난 뒤에 뜻이 있다 |

---

## 11. 이 작업이 덤으로 가져오는 것

1. **맵 해시 불일치를 접속 전에 말할 수 있다.** 지금 이 실패는 접속 후 경고 한 줄이고, 그때
   사람은 이미 방을 만들었다.
2. **`appsettings.json` 편집이 사라진다.** export 도구의 "등록되지 않았다" 경고와 그것을 위한
   문자열 검색 코드(`CheckRegistration`)가 필요 없어진다.
3. **식별자가 하나가 된다.** 맵 id = 맵 이름 = 파일명 = 에셋 이름, 그리고 기동 때 검사된다.
4. **맵 추가 비용이 도구 한 번이 된다.** 지금은 코드 3곳 + 씬 1개 + 설정 1줄이다.
