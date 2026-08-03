# 매치 규칙을 서버로 옮기는 계획

`SampleScene` 에 구현된 게임 로직을 분석하고, 그것을 `NVserver` 가 판정하도록 옮기는 순서를 정한다.

배경은 `architecture.md`(설계 원칙·와이어 포맷), `structure.md`(파일 배치 8문 표), `conventions.md`(밟은 함정), 그리고 클라이언트 쪽 `../NVproject/CLAUDE.md` 와 룰셋 `../NVproject/.claude/skills/game-rules/references/ruleset.md` 다. 룰셋이 규칙의 출처이고 이 문서는 **그 규칙을 누가 판정하는가** 만 다룬다. 규칙 자체를 바꾸지 않는다.

---

## 1. 지금 씬에 있는 것

`SampleScene.unity` 는 1080줄이고 오브젝트가 9개뿐이다. 레벨·캐릭터·목표물·HUD 가 전부 런타임에 코드로 만들어지므로, 씬은 **무엇을 만들지 가리키는 포인터** 에 가깝다.

| 씬 오브젝트 | 컴포넌트 | 직렬화된 값 |
|---|---|---|
| `Player` | `CharacterController`, `FirstPersonController`, `BlockRig`, `BlockCharacterAnimator`, `ProceduralReload`, `WeaponController`, `WeaponSwitcher`, `Crosshair` | 리그·무기·조준 파라미터 |
| └ `FP Camera` | Camera (URP) | local (0, 1.62, 0), nearClip 0.02 |
| `Backrooms` | `BackroomsMapGenerator` | gridSize 35, cellSize 3, **floors 2**, floorHeight 3.2, wallThickness 0.25, seed 0, roomAttempts 22 |
| `Match` | `MatchBootstrap` | config → `GameConfig.asset`, map → Backrooms, player → Player, `autoStart 1`, `debugKeys 1` |
| `Mirror`, `Mirror Frame` | `PlanarMirror` | 스폰 룸으로 재배치됨 |
| `Global Volume` | Volume | `SampleSceneProfile.asset` |
| `Directional Light` | Light | **비활성** |

`MatchBootstrap.Awake/Start` 가 런타임에 만드는 것: `MatchManager`, `DeviceSystem`, `GameHudController`, 그리고 `Player` 에 붙는 `PlayerAgent`·`FootstepAudio`·`WeaponAudio`·`PlayerInteractor`·`ChainDrag`·`PlayerRoleLoadout`. 목표물(`__Objectives` 아래 문 1 · 열쇠 10 · 장치 9 · 체인 제단 1)은 `MatchManager.PlaceObjectives` 가 만든다.

밸런스 수치는 씬이 아니라 `Assets/Settings/GameConfig.asset` 에 있다 — matchDuration 480, roleRevealDuration 4, keysRequired 10, escapesToWin 2, runnerHitsToDie 2, hitImmunity 0.75, seekerMagazine 3, deviceCount 9, deviceDestroyHits 4, teleportSharedCooldown 12, doorUseRadius 2.2, escapeHoldTime 0.8, keyPickupRadius 1.4 등 40개 필드.

---

## 2. 지금 누가 무엇을 판정하는가

`MatchManager` 의 주석은 자신을 "심판" 이라고 말하지만, 실제로는 **모든 클라이언트에서 각자 한 벌씩 돌고 있다.** 서버가 넘기는 것은 넷뿐이다 — 시작 틱, Seeker, 배치 씨드, 종료 중계(`MatchSync`).

| 규칙 | 지금 판정하는 곳 | 서버가 아는가 |
|---|---|---|
| 룸 단계 Waiting/Playing/Ended, 방장, 정원, 시작 자격 | `Room` (서버) | ✅ |
| 이동·충돌·중력·점프 | `PlayerMovement` + `CollisionWorld` (서버) | ✅ |
| Seeker 선정 | `Room.PickSeeker` (서버) | ✅ |
| 매치 내부 단계(RoleReveal→Playing→Ended), 매치 시계 | `MatchManager.Update` (**클라이언트 전원**) | ❌ |
| 목표물 배치(문·열쇠·장치·제단) | `MatchManager.PlaceObjectives`, 공유 씨드 + `System.Random` (**클라이언트 전원**) | ❌ |
| 열쇠 습득 | `KeyPickup.Update` 거리 폴링 (**클라이언트 전원**) | ❌ |
| 열쇠 삽입 · 문 개방 | `MatchManager.TryInsertKey` (**클라이언트 전원**) | ❌ |
| 탈출(문간 0.8초 유지) | `MatchManager.TickEscapes` (**클라이언트 전원**) | ❌ |
| 피격 판정 | `Bullet` 의 `Physics.Raycast` → `SendMessageUpwards("OnHit")` (**쏜 클라이언트**) | ❌ |
| 피격 규칙(2방, 출혈, 피격 시 순간이동, 무적 0.75초, 사망, 열쇠 흘리기) | `MatchManager.ReportHit` (**클라이언트 전원**) | ❌ |
| 탄약·재장전 | `WeaponController` (**쏜 클라이언트**) | ❌ |
| 장치 사용·쿨다운·공유 12초 락아웃·소진·파괴(4방) | `DeviceSystem.TryActivate` / `MapDevice.OnHit` (**클라이언트 전원**) | ❌ |
| 승리 조건 | `MatchManager.EvaluateWinConditions`, 방장만 (`ResolvesOutcome`) | ❌ (중계만) |
| 이동 잠금(리빌·프리즈·체인·종료) | `MatchManager.ApplyMovementLocks` (**클라이언트 전원**) | ❌ |
| 체인 드래그 | `ChainDrag` + `NavMesh.CalculatePath` (**Seeker 클라이언트**) | ❌ |

코드가 이미 이 자리를 알고 있다. `MatchManager.AcceptOutcome` 의 주석은 *"규칙이 서버로 오면 이것이 매치가 끝나는 유일한 경로가 되고 `EvaluateWinConditions` 는 사라진다"* 고 적고, `ControlKind.EndMatch` 는 *"한시적 경로이며 규칙이 서버로 오면 이 종류는 사라진다"* 고 적는다. 이 계획은 그 주석들을 실행하는 일이다.

### 2.1 남아 있는 구멍 두 가지

**클라이언트가 자기 피격을 판정한다.** `Bullet` 은 쏜 사람의 머신에서만 날고, `SendMessageUpwards("OnHit")` 는 그 머신의 `PlayerAgent` 에 닿는다. 원격 플레이어의 피격은 각 클라이언트가 자기 사본에 대해 따로 계산하므로, 맞았다고 인정하지 않는 클라이언트가 있으면 그 플레이어는 맞지 않는다. `MatchManager` 클래스 주석이 스스로 지적하는 상황이다 — *"자기 히트를 판정할 수 있는 클라이언트는 자기가 맞지 않았다고 판정할 수 있다."*

**Seeker 의 클라이언트가 문의 위치를 이미 알고 있다.** 룰셋은 문이 Runner 에게만 보여야 한다고 정하고, 클라이언트는 컬링 레이어(`MatchLayers.RunnerVision`)로 그것을 지킨다. 그런데 배치는 `RoomStateHeader.PlacementSeed` 를 받아 **모든 클라이언트가 같은 씨드로 계산** 하므로, Seeker 의 프로세스 메모리에도 문의 좌표가 들어 있다. WebGL 빌드는 디컴파일된다는 `architecture.md` 의 전제 위에서, 이것은 카메라 마스크로 막을 수 있는 종류의 정보가 아니다.

두 번째 항목은 이 계획의 방향을 하나 결정한다. **씨드를 공유해 양쪽이 같은 배치를 계산하는 방식으로는 이 구멍이 닫히지 않는다.** 서버가 배치하고, 역할별로 걸러서 좌표를 내려보내야 한다(§5.2).

---

## 3. Phase 0 — 지형 정합성 복구 (선행 차단 요소)

**다른 모든 작업 앞에 있다.** 지금 상태로는 `SampleScene` 을 서버에 붙여 끝까지 돌릴 수 없다.

측정한 사실:

| | 값 |
|---|---|
| `SampleScene` 의 레벨 컴포넌트 | `BackroomsMapGenerator` (GUID `9d2872…`) — 씬 참조로 확인 |
| 그 컴포넌트의 `MapName` | `"backrooms2f"` (`BackroomsMapGenerator.cs:113`) |
| `BackroomsMap.cs` 의 `MapName` | `"backrooms"` (`BackroomsMap.cs:114`) — **어느 씬도 참조하지 않는다** |
| `MapData/backrooms.json` | 박스 1367개, 범위 ±89.6m (= 56셀 × 3.2m) → 레거시 `BackroomsMap` 의 export |
| `MapData/backrooms2f.json` | 박스 735개, 범위 ±43.5m 부근 (= 35셀 × 3m, 2층) → 현재 씬의 export |
| `appsettings.json` 의 `Game:Maps` | `default` → `backrooms.json`, `test-room` → `test-room.json`. **`backrooms2f` 도 `arena` 도 등록되어 있지 않다** |
| `SessionSceneRouter.SceneByMap` | `"backrooms"` → `SampleScene` |

즉 로비를 통해 기본 맵으로 방을 만들면, 라우터는 `backrooms` 를 보고 `SampleScene` 을 열고, 그 씬은 `backrooms2f` 지형(735박스)을 만들고, 서버는 `backrooms.json`(1367박스)으로 판정한다. **접속할 때마다 맵 해시 불일치가 확정** 이고, `backrooms2f` 로 방을 만들려 해도 등록되지 않은 맵 id 라 거절된다(`RoomMaps.ByMapId` → null).

`conventions.md` 가 이미 경고한 두 항목("씨드·격자·벽 두께를 바꾸면 export 를 다시 돌린다", "등록되지 않은 맵 id 는 거절한다")이 겹쳐 걸린 상태다.

**할 일**

1. 이름을 하나로 모은다. `BackroomsMapGenerator.MapName` 을 `"backrooms"` 로 바꾼다 — 라우터 표와 `Game:Maps` 의 기본 항목이 이미 그 이름이고, 고쳐야 할 곳이 가장 적다.
2. **Tools ▸ NV ▸ Map ▸ Export Map Collision** 을 `SampleScene` 에서 다시 돌려 `MapData/backrooms.json` 을 현재 지형으로 덮는다.
3. 레거시를 지운다 — `Assets/Scripts/BackroomsMap.cs`(+`.meta`), `MapData/backrooms2f.json`. `arena.json` 은 등록도 참조도 없으므로 함께 판단한다.
4. 서버를 띄워 기동 로그의 `맵 backrooms: … 박스 N개` 가 새 값인지, 접속 후 클라이언트 콘솔의 맵 해시가 `일치` 인지 확인한다.
5. `../NVproject/CLAUDE.md` 의 "`SampleScene` ↔ `backrooms`" 서술은 이 작업으로 비로소 참이 된다. `Ceiling Lid` 도 `CollisionBoxes` 에 있으므로 이 export 에 포함된다.

**검증** — `dotnet test --filter "FullyQualifiedName~ExportedMapTests"` 통과 + 두 클라이언트가 `SampleScene` 에서 서로를 보고 벽에 막히는 것.

`BackroomsMap.cs` 삭제는 되돌리기 어려운 편이니, 진행 전에 확인을 요청한다.

---

## 4. Phase 1 — 판정 기반: 걸을 수 있는 곳을 서버가 알게 한다

서버는 지형을 **AABB 박스 목록 + 스폰 8개** 로만 안다(`MapData`). 목표물을 배치하고 피격 시 순간이동 지점을 고르려면 "여기 설 수 있는가" 를 답해야 하는데, 지금은 답할 수 없다.

클라이언트에는 그 답이 이미 있다 — `BackroomsMapGenerator.CollectStandableCells`, `IsStandable`, `TryRandomPoint`, `TryNearestStandablePoint`, `FloorIndexAt`, `CellToWorld`. 문제는 그중 일부가 Unity 에만 있는 것에 기댄다는 점이다: `MatchManager.IsFreeFloor` 는 `Physics.CheckCapsule` 로 계단 위를 걸러내고, 체인 제단은 그 결과로 자리를 잡는다.

**결정: 격자를 export 하되, Unity 물리가 필요한 판정은 export 시점에 구워 넣는다.** 서버가 생성기 로직을 다시 구현하면 `structure.md` 가 금지하는 중복이 되고, 씨드를 바꿀 때마다 두 곳이 갈린다.

**`MapData` 확장** (`Shared/Collision/MapData.cs`)

```
Grid: { Floors, Width, Depth, CellSize, FloorHeight, OriginX, OriginZ, Cells[] }
```

`Cells` 는 셀당 1바이트 플래그로 둔다 — `Standable`(격자상 통행 가능), `FreeFloor`(플레이어 캡슐이 실제로 들어감 = 계단·기물 제외), `StairLink`(위층과 수직 연결). 세 개를 나누는 이유는 쓰임이 다르기 때문이다: 열쇠는 `Standable` 이면 되고, 제단·순간이동 착지점은 `FreeFloor` 여야 하며, `StairLink` 는 Phase 5 의 경로 탐색에 필요하다.

- `MapSpawn` 은 그대로 둔다.
- **`ComputeHash` 에 격자를 포함시킨다.** 포함하지 않으면 격자가 어긋난 채로 해시가 일치해, 증상이 "가끔 열쇠가 벽 안에 생김" 으로만 나타난다. 포함하면 이번에도 export 를 다시 돌려야 한다 — Phase 0 과 같은 커밋에서 처리하는 편이 낫다.
- 부동소수점은 `conventions.md` 대로 왕복 보존 형식(`"R"`)으로 쓴다.

**클라이언트 export 변경** — `MapExport.BuildMapData` 가 `INetworkMapSource` 에 새로 추가한 격자 질의를 호출한다. `FreeFloor` 는 `MatchManager.IsFreeFloor` 와 **같은 캡슐 크기·같은 오프셋** 으로 계산해야 한다(`feet + up*0.35` ~ `feet + up*1.5`, 반지름 0.32). 값을 다시 적지 않도록 그 상수를 한 곳으로 모은다. `TestRoomMap` 은 방 하나이므로 전부 `FreeFloor` 로 채운다.

**서버 질의** — `Shared/Collision/` 에 `MapGrid` 를 두고 `TryRandomPoint(ref seq, …)`, `TryNearestFreeFloor(pos, …)`, `CellToWorld`, `FloorIndexAt` 을 제공한다. `structure.md` 8문 표의 1번("클라이언트도 같은 계산을 하는가")에 걸린다 — 클라이언트도 예측·표시를 위해 같은 좌표를 계산해야 하므로 `Shared` 다.

**난수** — 배치는 *순서* 가 있는 난수를 쓴다. 기존 `DeterministicRandom` 은 (틱, 엔티티, salt) → 값의 무상태 해시라 시퀀스에 맞지 않고, `new Random()` 은 `architecture.md` 기본값 대체표가 금지한다. `Shared/Simulation/DeterministicSequence.cs` 로 상태를 명시적으로 들고 다니는 PRNG(xorshift 계열) 하나를 추가한다. 초대 코드·방장 토큰에는 절대 쓰지 않는다 — 그쪽은 `RandomNumberGenerator` 다(`conventions.md`).

**테스트** — `tests/Modules.Tests/Realtime/ExportedMapTests.cs` 확장(격자 크기·플래그 일관성·해시), `Simulation/` 에 `MapGridTests`(무작위 점이 항상 `FreeFloor`, 가장 가까운 점 탐색이 벽을 반환하지 않음), `DeterministicSequenceTests`(같은 씨드 → 같은 수열).

**프로토콜 변경 없음. 클라이언트 동작 변화 없음.** 이 단계는 혼자 검증되고 되돌리기 쉽다.

---

## 5. Phase 2~4 — 판정을 옮긴다

프로토콜 버전을 **3** 으로 올린다. `ProtocolInfo.Version` 이 바뀌면 구버전 클라이언트는 업그레이드 전에 전부 426 으로 거절되고 WebGL 빌드는 수 분이 걸리므로, 아래 세 단계는 **서버와 클라이언트를 같은 커밋에 배포** 한다. 버전을 세 번 올리지 않고 한 번만 올리려면 Phase 2~4 를 하나의 배포 단위로 묶고, 그 안에서 단계별로 커밋한다.

### 5.1 Phase 2 — 매치 상태 기계

**서버가 갖는 것.** 매치 내부 단계, 시계, 역할, 승리 조건, 이동 잠금.

`RoomPhase` 는 건드리지 않는다. 룸 생애(Waiting/Playing/Ended)와 매치 진행(RoleReveal/Playing/Ended)은 다른 축이고, `Room.Advance` 가 이미 `Playing` 에서만 시뮬레이션한다. 매치 내부 단계는 `RoomPhase.Playing` 안의 상태로 두고 새 전문에 싣는다. 리빌 중 정지는 단계 전이가 아니라 **입력 무력화** 로 구현한다 — `InputValidator.Neutral` 과 같은 방식으로 이동 성분만 0 으로 만들고 시선은 남긴다(룰셋과 `MatchManager.ApplyMovementLocks` 의 의도가 그렇다).

**새 위치** — `Modules/Realtime/Simulation/` 에 `Match.cs`(상태·전이), `MatchRules.cs`(판정)를 둔다. `structure.md` 8문 표의 5·7번 → 모듈 루트 아래, `internal`. 파일이 10개를 넘으면 `Modules/Realtime/Match/` 로 나누고 `structure.md` 의 폴더 표에 한 줄 추가한다(계층 이름이 아니라 성격 이름이므로 규칙 위반이 아니다).

**상수** — 두 갈래로 나눈다. `conventions.md` 의 "같은 값을 두 번 적지 않는다" 가 여기서 걸린다.

| 값 | 어디로 | 왜 |
|---|---|---|
| `matchDuration`, `roleRevealDuration`, `keysRequired`, `escapesToWin`, `runnerHitsToDie`, `seekerMagazine`, `doorUseRadius`, `escapeHoldTime`, `keyPickupRadius`, `keyInsertInterval`, `deviceUseRadius`, 쿨다운 값 전부 | `Shared/Simulation/MatchConstants.cs` | 클라이언트가 HUD·프롬프트·쿨다운 표시를 예측해야 한다. 디컴파일되어도 무해한 값이다 |
| `hitImmunity`, `deviceDestroyHits`, 배치 간격(4m·5m), 장치 조합표, 사망 시 열쇠 처리 | `RealtimeConstants.Match` | 판정이지 표시가 아니다 |
| `bloodSpacing`, `bloodLifetime`, `xrayWallAlpha`, `showDoorCompass`, `practiceRunners`, `practiceRunnerSpeed`, `localRole` | `GameConfig.asset` 에 남긴다 | 순수 표현 또는 오프라인 연습 전용 |

`GameConfig` 의 공유 필드는 삭제하고 `MatchConstants` 를 읽는 프로퍼티로 대체한다. 두 벌을 남기면 서버가 480초로, 클라이언트 HUD 가 에셋의 옛 값으로 세는 상태가 된다.

**새 와이어 — `EventKind.MatchState = 2`.** `RoomState` 와 같은 성격으로 만든다: **알림이 아니라 전문**, 2Hz + 변경 즉시, 멱등. `conventions.md` 가 이유를 이미 적어 두었다(`Bounded(32, DropOldest)` 채널에서 한 번짜리 알림은 영구히 잃을 수 있다).

고정부: 매치 단계(u8), 남은 시간(u16, 0.1초 단위면 6553초까지 = 충분), 삽입된 열쇠(u8), 탈출 수(u8), 결과(u8), 참가자 수(u8). 참가자당: playerId(u8), 역할(u8), 상태 플래그(u8), 피격 수(u8), 소지 열쇠(u8).

**역할별 필터링이 필요하다.** 룰셋은 Seeker 에게 열쇠 진행도를 알리지 않는다. `RoomState` 는 본문이 수신자와 무관해 한 번 인코딩해 전원에게 보내지만, 이 전문은 **스냅샷처럼 세션별로 인코딩** 해야 한다(스냅샷이 `AckedInputTick` 때문에 그러는 것과 같은 이유). Seeker 에게 가는 사본에서는 `삽입된 열쇠` 와 남의 `소지 열쇠` 를 0 으로 채운다. 클라이언트에서 숨기면 디컴파일로 되살아난다.

**`EntityFlags` 확장.** 지금 3비트(Alive·OnGround·Crouching)만 쓰고 5비트가 남는다. `Bleeding = 1<<3`, `Seeker = 1<<4`, `Escaped = 1<<5`, `Frozen = 1<<6` 을 추가한다. `EntityState` 크기는 13B 로 그대로이고 스냅샷 대역폭도 그대로다. 출혈·역할은 원격 몸의 표현(피 흔적, 무기 유무)에 매 틱 필요하므로 2Hz 전문이 아니라 스냅샷에 있어야 한다.

**사라지는 것**

- `ControlKind.EndMatch` — 서버가 결과를 정하므로 방장의 보고 경로가 필요 없다. enum 값 3 은 비워 두고 이유를 주석으로 남긴다(값 2 가 이미 그렇게 되어 있다).
- `RoomStateHeader.PlacementSeed` — Phase 3 에서 배치가 서버로 가면 클라이언트가 씨드를 알 이유가 없다. 오히려 알면 §2.1 의 문 위치 누출이 남는다. 서버 내부 재현용으로만 남기고 와이어에서 뺀다. `RoomStateHeader.WireSize` 가 15 → 11 로 줄어든다.
- `MatchManager.EvaluateWinConditions`, `ResolvesOutcome`, `MatchSync.OnLocalMatchEnded`, `NetSession.ReportMatchEnd`.

**클라이언트 쪽** — `MatchManager` 가 심판에서 **뷰** 로 바뀐다. `AcceptMatchState(in MatchStateMessage)` 하나가 단계·시계·역할·카운터를 받아 기존 이벤트(`PhaseChanged`, `KeysChanged`, `EscapesChanged`, `RolesAssigned`, `MatchEnded`)를 그대로 발화한다. HUD·`PlayerRoleLoadout`·`GameHudController` 는 그 이벤트를 구독하고 있으므로 **손대지 않는다** — 이 이벤트 목록이 replication 계약이라던 설계가 여기서 값을 한다. `MatchBootstrap.autoStart`/`debugKeys` 는 세션이 있으면 이미 꺼진다(`MatchSync.Awake`).

`_phaseTimer`·`TimeRemaining` 의 로컬 감소는 남긴다. 전문이 2Hz 라 그 사이를 메워야 HUD 시계가 튀지 않는다. 다만 전문이 올 때마다 서버 값으로 덮는다.

**테스트** — `tests/Modules.Tests/Realtime/MatchTests.cs`: 리빌이 끝나면 Playing 으로 간다 / 시계 0 은 Seeker 승 / 탈출 2 는 즉시 Runner 승 / Runner 0 명으로는 전멸 판정이 켜지지 않는다 / Seeker 사본에 열쇠 진행도가 실리지 않는다. 마지막 항목은 인코딩 결과 바이트를 직접 본다.

### 5.2 Phase 3 — 목표물

**서버가 배치한다.** `MatchRules` 가 Phase 1 의 `MapGrid` 와 `DeterministicSequence` 로 제단 → 문 → 열쇠 → 장치 순서로 자리를 잡는다. 순서와 간격(제단 먼저, 문 다음, 열쇠 4m·장치 5m 이격)은 `MatchManager.PlaceObjectives` 의 것을 그대로 옮긴다 — 제단이 먼저인 이유("유일한 고정물이므로 나머지가 피해 간다")가 그대로 유효하다. 제단 착지점은 `FreeFloor` 플래그로 찾는다(옛 `Physics.CheckCapsule` 의 자리).

**새 와이어 — `EventKind.ObjectiveState = 3`.** 역시 전문이고 **세션별 인코딩** 이다.

- 문: 위치(양자화 i16 ×3), yaw(u16), 개방 여부(u8). **Seeker 사본에서는 이 블록을 아예 뺀다.**
- 열쇠: 남아 있는 것만 위치 목록. 열쇠는 룰셋상 전원에게 보인다("복도에 놓인 열쇠는 물리적 물건이고, Seeker 가 그것을 보는 것이 열쇠를 지키는 전술을 만든다") — 그대로 유지한다.
- 장치: 위치·yaw·타입·상태(소진/파괴/쿨다운 남은 틱). 전원 공통.
- 제단: 위치 1개. 전원 공통(고정물이고 Seeker 는 알아야 한다).

전문 크기를 확인했다. 열쇠 10 + 장치 9 + 문 + 제단이면 대략 `10×7 + 9×9 + 8 + 7 ≈ 166B` 다. 걸릴 수 있는 상한은 **클라이언트의 수신 버퍼** 이고 그 값은 512B(`NetworkClient.ReceiveBytes` — 주석이 "스냅샷 최대 114B" 를 근거로 잡은 값이다). 166B 는 들어가지만 여유가 3배뿐이므로, 열쇠·장치 수를 늘리는 변경에서 가장 먼저 넘칠 자리다. 서버 쪽 `RealtimeConstants.Sessions.ReceiveBufferBytes` 256B 는 **수신 방향** 이라 무관하다.

대역폭은 2Hz 로 보내면 8인 룸에서 166B × 2 × 8 ≈ 2.6KB/s 로 스냅샷 3.6KB/s 와 같은 자릿수다. 허용 범위지만 변경이 드문 블록이므로 "변경 즉시 + 5초 주기" 로 낮추는 편이 낫다(§8-4).

**판정을 옮긴다**

| 규칙 | 서버 구현 |
|---|---|
| 열쇠 습득 | 매 틱 거리 폴링. 수평 `keyPickupRadius`, 수직 1.6m. `KeyPickup.Update` 의 비대칭 허용치를 그대로 옮긴다 — 위층이 아래층 열쇠를 빨아들이지 않게 하는 값이다 |
| 열쇠 삽입 | `Interact` 입력 + 반경 + `keyInsertInterval` + 소지 확인. 한 곳에서 직렬화되므로 "두 Runner 가 동시에 10번째 열쇠를 넣는" 경우가 자동으로 해결된다 |
| 문 개방 | 삽입 수가 `keysRequired` 에 도달한 틱 |
| 탈출 | 개방된 문간에서 `escapeHoldTime` 유지. 층 차이 2m 초과면 리셋 |
| 장치 사용 | `Interact` + 소진·파괴·쿨다운·공유 12초 락아웃·"출혈 중이 아니면 거절" 까지 `DeviceSystem.TryActivate` 의 게이트를 그대로 |
| 프리즈 + X-ray | 이동 잠금은 서버(전원 무력화), 벽 투명은 클라이언트 표현. 지속 시간은 서버가 소유 |
| 전체 맵 뷰 · Seeker 카메라 | 순수 표현. 서버는 "발동했고 N초간 유효" 만 판정하고, 화면은 클라이언트가 그린다 |

**`Interact` 입력.** `ButtonFlags` 에 `Interact = 1 << 4` 를 추가하고 `All` 마스크를 함께 고친다(enum 주석이 이미 그렇게 지시한다). **대상 id 는 싣지 않는다** — 서버가 위치와 yaw 를 알고 있으므로 `PlayerInteractor.FindTarget` 과 같은 근접 + 시선 타이브레이크를 서버에서 다시 계산하면 된다. id 를 받으면 "그 id 를 쓸 자격이 있는가" 를 또 검사해야 하고, 검사 항목이 하나 늘 뿐 얻는 것이 없다.

`InputFrame` 크기는 7B 로 그대로다.

**클라이언트 쪽** — `KeyPickup`·`EscapeDoor`·`MapDevice`·`ChainAltar` 는 **좌표를 받아 그려지는 것** 이 된다. `KeyPickup.Update` 의 거리 폴링과 `MatchManager.TryPickUpKey` 호출을 삭제하고, 회전·상하 진동만 남긴다. `EscapeDoor.Update` 의 패널 침강은 전문의 삽입 수로 계산하므로 그대로 동작한다. `MapDevice.Update` 의 패널 맥동도 상태를 받으면 그대로다. `PlayerInteractor` 는 프롬프트 문자열만 만들고, `Interact` 는 입력 비트로 나간다.

`MatchManager.PlaceObjectives`·`PlaceChainAltar`·`PlaceDevices`·`TryFindSpacedPoint`·`IsFreeFloor`·`ScatterKeys` 는 서버로 옮겨지고 클라이언트에서 사라진다. `ClearObjectives` 의 "파괴 전에 비활성화" 함정은 클라이언트에 남는다(오브젝트를 여전히 만들고 부순다).

**이 단계가 §2.1 의 두 번째 구멍을 닫는다.** Seeker 의 프로세스에는 문의 좌표가 도달하지 않는다.

**오프라인 연습 모드를 유지한다.** `practiceRunners`·F1/F2/F5 디버그 키·`autoStart` 는 세션이 없을 때만 도는 경로다. 배치 코드가 서버로 가면 오프라인에서 목표물이 하나도 생기지 않는다. 선택지는 둘이다 — (a) 배치를 `Shared` 에 두어 양쪽이 쓰되 **서버가 계산한 좌표만 와이어로 내려보낸다**(클라이언트는 오프라인에서만 직접 호출), (b) 오프라인 모드를 포기한다. (a) 를 권한다. `Shared` 에 배치 함수가 있어도 씨드가 와이어에 없으면 네트워크 매치에서 Seeker 는 문을 계산할 수 없으므로, 정보 누출 없이 코드 한 벌을 유지할 수 있다. `structure.md` 8문 표 1번에도 맞는다.

### 5.3 Phase 4 — 전투

**클라이언트는 이미 발사를 보내고 있다.** `NetworkBootstrap.Sample` 이 `_controller.FireHeld` 를 `ButtonFlags.Fire` 로 싣고, `InputValidator.Sanitize` 가 통과시키고, 서버는 **그것을 무시한다.** 이 단계는 그 비트를 소비하는 일이다.

**서버 발사체.** 룸이 발사체 목록을 들고 매 틱 진행시킨다. 판정은 `Shared/Collision/Raycaster` 의 스윕 — 클라이언트 `Bullet` 이 하던 것과 같은 방식이고(이동한 선분을 레이캐스트, 끝점 검사 금지) 이유도 같다. 120m/s 면 한 틱에 4m 를 가므로 위치 검사는 0.25m 벽을 즉시 통과한다.

발사체가 실체이므로 **비행에는 되감기가 필요 없다.** 되감기는 발사 순간의 사수 위치에만 필요하고, 거기에 `readme.md` 의 고정 파라미터인 200ms 상한이 걸린다 — 플레이어당 6틱치 위치 이력을 들고 입력 틱에 해당하는 위치에서 발사한다. 히트스캔이었다면 표적 위치까지 되감아야 했다. 발사체 방식이 그 비용을 없애 준다.

**방향.** 클라이언트는 `AimPoint`(화면 중앙 레이캐스트)로 쏜다. 서버는 그것을 재현할 수 없지만, 재현할 필요도 없다 — `AimPoint` 는 눈 위치 + yaw/pitch 에서 나온 값이고 그 둘은 이미 `InputFrame` 에 있다. 서버는 눈에서 yaw/pitch 방향으로 쏜다. 클라이언트의 총구 정렬 트릭(`muzzle.forward` 가 아니라 `AimPoint` 로 쏘는 이유)은 **표현** 이었으므로 그대로 남고, 판정과 어긋나지 않는다.

**피격 규칙** — `MatchManager.ReportHit` 를 그대로 옮긴다. Runner 만 피격 대상, `hitImmunity` 0.75초, 1방 → 출혈 + 무작위 순간이동, 2방 → 사망 + 사망 지점에 열쇠 흘리기. 무적 창의 이유(3발이 동시에 공중에 있어 순간이동을 관통해 죽인다)는 서버에서도 동일하다.

**탄약** — 매거진 3, 발사 간격, 재장전을 서버가 센다. `RealtimeConstants` 가 아니라 `MatchConstants`(공유)에 두어야 HUD 의 탄약 표시가 예측된다. `architecture.md` 의 "발사율 위반 독립 검사" 가 여기서 실현된다.

**장치 파괴** — 발사체가 장치 AABB 에 맞으면 `deviceDestroyHits` 4 를 센다. 장치는 지금 클라이언트에서 유일하게 콜라이더를 가진 목표물이므로, 서버도 장치를 충돌체로 등록해야 한다. 문과 열쇠는 콜라이더가 없다 — 서버도 그렇게 둔다(문에 콜라이더를 주면 Seeker 가 허공에 부딪혀 찾는다).

**클라이언트 쪽** — `Bullet` 은 순수 표현이 된다. `SendMessageUpwards("OnHit")` 제거, `PlayerAgent.OnHit` 제거, `MapDevice.OnHit` 제거. 예광탄은 서버의 발사 통지로 생성한다. 그 통지는 한 번짜리라 잃을 수 있지만 **표현이므로 허용한다** — 총성도 이미 판정 전에 울리는 설계다. 탄약 수와 피격 결과는 전문에 있으므로 유실되지 않는다.

`WeaponController.Fire` 는 소리·반동·예광탄만 남기고 탄약 감소를 서버 값으로 대체한다. 히트마커는 서버가 명중을 알린 시점에 뜬다 — 현재 "벽에도 뜬다" 는 문제(`../NVproject/CLAUDE.md` 가 적어 둔 미결 항목)가 이때 자연히 해결된다.

**테스트** — `MatchRules` 의 피격 규칙(무적 창 안의 2발이 죽이지 않는다 / 2방이 죽인다 / 사망 시 열쇠가 흘려진다), 발사체 스윕(빠른 탄이 벽을 통과하지 않는다 — 클라이언트가 100,000m/s 로 검증한 것과 같은 테스트), 발사율 위반 거절.

---

## 6. Phase 5 — 체인 드래그

가장 까다롭다. 규칙(3발 소진 → 제단으로 끌려가 3초 정지 후 재장전)은 서버 판정이 맞지만, 지금 구현은 **`NavMesh.CalculatePath`** 로 최단 보행 경로를 구하고 그 경로 길이로 견인을 페이싱한다. 서버에는 navmesh 가 없다.

선택지 세 개, 비용 순:

1. **직선 견인.** 서버가 제단 착지점까지 직선 보간으로 옮기고(충돌 무시), 잠금·대기·재장전을 소유한다. 클라이언트는 지금처럼 navmesh 경로로 체인을 **그리기만** 한다. 구현이 가장 가볍지만, "31개 코너 · 399m 경로 대 55m 직선" 이라는 측정된 연출이 사라진다 — 벌칙의 무게가 눈에 보이지 않게 된다.
2. **격자 A\*.** Phase 1 에서 이미 `Standable`·`StairLink` 플래그를 export 하므로 그 격자가 곧 경로 그래프다. `Shared` 에 A\* 를 두면 서버가 판정하고 클라이언트가 같은 경로를 그린다. `../NVproject/CLAUDE.md` 가 경고한 "직접 만든 격자 A\* 는 층이 계단으로 이어진 것을 배워야 한다" 는 문제는 `StairLink` 가 답한다. 비용이 실제로 붙는 유일한 항목이고, 결과는 지금 연출과 거의 같다.
3. **판정만 서버, 위치는 클라이언트.** 이동 권위를 이 구간만 클라이언트에 넘긴다. **권한다고 하지 않는다** — 이동 권위에 예외를 만드는 것이고, Seeker 가 벌칙 구간에 텔레포트할 수 있게 된다.

**2번을 권한다.** 다만 연출이 바뀌는 선택이므로 진행 전에 확인을 요청한다. 1번으로 시작해 2번으로 올리는 것도 가능하다 — 서버 인터페이스(잠금 + 목표 지점 + 소요 시간)는 둘이 같다.

`FirstPersonController.Phasing`(견인 중 충돌 해제)과 "루프가 항상 조금 모자라게 끝나므로 마지막에 명시적으로 스냅한다" 는 함정은 서버 구현에도 그대로 옮겨야 한다.

---

## 7. Phase 6 — 정리

- **`GameConfig` 분리** (§5.1 표). 공유 값은 삭제하고 `MatchConstants` 를 읽는다.
- **문서 갱신** — `architecture.md` 의 기본값 대체표에 "클라이언트가 규칙을 판정 → 서버가 판정하고 전문으로 내려보낸다", 와이어 포맷 표에 `MatchState`·`ObjectiveState` 추가, 프로토콜 버전 3. `structure.md` 에 `Match/` 폴더(나눴다면). `conventions.md` 에 이번에 확정한 규칙 — 역할별 필터링은 와이어에서 하고 컬링 레이어에 맡기지 않는다 / 배치를 씨드 공유로 하면 정보가 새므로 좌표를 내려보낸다 / Unity 물리가 필요한 판정은 export 시점에 구워 넣는다 / 30분 이상 걸린 문제는 증상→원인→대응으로. `readme.md` 의 세션 상태 표.
- **`../NVproject/CLAUDE.md`** 의 매치 레이어 절 — "히트·열쇠·탈출은 여전히 각 클라이언트가 판정하며, 이것이 이 파일이 경고하는 치팅 가능한 이음새다" 가 이 작업으로 거짓이 된다. 함께 고친다.
- **죽은 경로 제거** — `MatchSync` 의 결과 보고, `NetSession.ReportMatchEnd`, `MatchManager.AcceptOutcome`(전문으로 대체됨).

---

## 8. 확인이 필요한 것

`architecture.md` 는 문서화된 금지를 어기거나 고정 파라미터를 바꾸기 전에 묻도록 정한다. 이 계획에서 해당하는 항목:

1. **`BackroomsMap.cs` 와 `backrooms2f.json`·`arena.json` 삭제** (§3). 어느 씬도 참조하지 않지만 되돌리기 번거롭다.
2. **체인 드래그의 경로 방식** (§6). 1번(직선)은 연출이 바뀌고, 2번(격자 A\*)은 `Shared` 에 A\* 가 들어온다.
3. **`Shared` 에 `DeterministicSequence` 추가** (§4). 기존 `DeterministicRandom` 으로는 순서 있는 난수를 만들 수 없어 필요하지만, `Shared` 표면이 늘어나는 일이다.
4. **`ObjectiveState` 전문의 주기** (§5.2). 2Hz 면 8인 룸에서 2.6KB/s 가 더 붙는다. 5초 주기 + 변경 즉시로 낮출지.
5. **오프라인 연습 모드 유지 여부** (§5.2 끝). 유지하면 배치 함수가 `Shared` 에 있어야 한다.

## 9. 순서 요약

| Phase | 내용 | 프로토콜 | 독립 검증 |
|---|---|---|---|
| 0 | 맵 이름·등록·export 정합성 복구 | 없음 | 두 클라이언트가 `SampleScene` 에서 해시 일치 |
| 1 | `MapData` 격자 확장, `MapGrid`, `DeterministicSequence` | 없음 (해시는 바뀜) | 유닛 테스트 |
| 2 | 매치 상태 기계·시계·역할·승리 조건 | **→ 3** | 매치가 서버 시각으로 시작·종료 |
| 3 | 목표물 배치·열쇠·문·탈출·장치 | 3 | Seeker 사본에 문 좌표가 없음 |
| 4 | 발사체·피격·탄약·장치 파괴 | 3 | 부정 클라이언트가 피격을 거부할 수 없음 |
| 5 | 체인 드래그 | 3 | 3발 소진이 서버 판정으로 벌칙을 건다 |
| 6 | 상수 분리·문서·죽은 경로 제거 | 3 | `dotnet build` 경고 0, 전체 테스트 |

Phase 0·1 은 클라이언트에 영향이 없어 먼저 넣을 수 있다. Phase 2~4 는 프로토콜 3 을 공유하므로 **한 배포 단위** 로 묶는다. Phase 5 는 그 뒤 독립적으로 붙는다.
