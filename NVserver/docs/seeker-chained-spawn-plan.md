# Seeker Chained Spawn Point 개선 계획

Seeker(술래)가 Runner 와 같은 링 스폰을 나눠 쓰는 현재 구조를, **매치 시작 시 Seeker 는 Chained 상태의 위치(제단 착지점)에서만 스폰**되도록 바꾼다. 이 문서는 현행 분석, 설계, 단계별 계획을 담는다.

---

## 1. 현행 스폰 시스템 · 게임 시작 플로우 분석

### 1.1 스폰 결정은 `PlayerId` 인덱싱 한 줄이 전부다 — 역할 분기가 없다

스폰 위치가 정해지는 곳은 서버에 딱 세 곳이고, 세 곳 모두 같은 두 줄이다.

| 지점 | 위치 | 시점 |
|---|---|---|
| 입장 시 초기 배치 | `Modules/Realtime/Simulation/Room.cs:1938-1943` (`Join`) | 방 입장 |
| 봇 입장 | `Room.cs:2042-2047` (`JoinBot`) | 정적 룸 봇 채우기 |
| **매치 시작 재배치** | `Room.cs:2418-2420` (`Start`) | 시작 틱 |

```csharp
_map.SpawnPosition(player.PlayerId), _map.SpawnYaw(player.PlayerId)
```

조회는 `Shared/Collision/WorldMap.cs:44-64` — 인덱스를 `SpawnCount` 로 감아(`Wrap`) 항상 유효한 값을 낸다. `WorldMap.cs:42-43` 의 주석대로 **"어느 지점을 고를지는 모듈의 판정"** 이고, 그 판정이 지금은 `PlayerId` 인덱싱뿐이다.

역할 배정(`PickSeeker`, `Room.cs:2660-2680`, `Random.Shared` 무작위)은 `Room.cs:2371` 에서 일어나고 스폰 루프는 `:2418` 이므로, **스폰 시점에 서버는 이미 술래를 알고 있다** — 역할별 스폰을 넣을 때 순서를 바꿀 필요가 없다.

`MapSpawn`(`Shared/Collision/MapData.cs:133-147`)에는 `X/Y/Z/Yaw` 만 있고 팀/역할 필드가 없다. 세 맵 파일(`test-room`, `backrooms`, `backrooms-v2`) 모두 스폰 8개를 갖고 있으며 `tests/Modules.Tests/Realtime/ExportedMapTests.cs:55` 가 `Assert.Equal(8, map.SpawnCount)` 로 그것을 고정한다 (8스폰/5정원 불일치는 `docs/backrooms-v2-plan.md:342` 가 범위 밖으로 미룬 기존 항목).

### 1.2 매치 시작 플로우 (`Room.Start`, `Room.cs:2339-2465`)

1. 관문 4개 (phase/권한/최소인원/레디)
2. `_seekerPlayerId = PickSeeker()` (`:2371`)
3. `_placementSeed` 발급 (`:2372`)
4. `_match.Begin()` — `RoleReveal`(4초, 전원 `MovementLocked`) 시작 (`:2387`)
5. **`ObjectivePlacement.PlaceObjectives(...)` — 제단이 첫 번째로 배치된다** (`:2392-2393`)
6. 스폰 루프: 전원 `RespawnAt(_map.SpawnPosition(PlayerId), _map.SpawnYaw(PlayerId))` + 매치 상태 초기화 (`:2418-2453`)
7. `Phase = Playing` (`:2455`)

**핵심: 제단 배치(5)가 스폰 루프(6)보다 앞이다.** 즉 스폰 루프 시점에 `_objectives.AltarDragPoint` 는 이미 계산돼 있다 — 이 작업에 필요한 순서가 이미 갖춰져 있다.

### 1.3 위치 동기화 — 초기 위치 전용 통보는 없다

- `Welcome`(0x83)에는 위치가 없다. `Start` 가 `RespawnAt` 으로 서버 상태를 옮기면 **그다음 틱의 풀 스냅샷(0x81)이 전달한다** (`Room.cs:467-504`, 게이트 `:437-442`).
- 클라이언트는 스폰 포인트 개념 자체가 없다. 로컬 플레이어는 `ClientPrediction.HardResetTo` (`NVproject/Assets/Scripts/Networking/ClientPrediction.cs:118-136`)로 서버 위치에 수렴하고, 원격 플레이어는 첫 스냅샷 위치로 `SnapTo` (`NetworkManager.cs:873-935`).
- 따라서 **서버가 스폰 위치만 바꾸면 와이어 프로토콜·클라이언트 배치 코드는 무수정**이다.

### 1.4 Chained 상태와 제단 — 앵커 개념이 이미 존재한다

Chained 는 Seeker 의 재장전 벌칙이다: 탄창(3발)이 비면 `BeginChain`(`Room.cs:1328-1382`)이 격자 경로(`GridRoute`)로 **`_objectives.AltarDragPoint`** 까지 끌고 가고, 3초 대기 후 놓아주며 탄창을 채운다(`StepChain`, `Room.cs:1392-1443`).

- `AltarPosition` / `AltarDragPoint` (`Shared/Simulation/Objectives.cs:48-58`): 제단은 **맵마다 고정**(격자 중앙 최근접 `FreeFloor`, 무작위 아님 — `ObjectivePlacement.PlaceAltar`, `Shared/Simulation/ObjectivePlacement.cs:65-103`), 착지점은 제단 옆 `FreeFloor` 셀.
- 좌표는 `ObjectiveState` 전문으로 **전원에게** 내려간다 (`Room.cs:776-777`) — Seeker 를 제단에 세워도 새로 노출되는 정보가 없다.
- 격자 없는 맵(`test-room.json`)은 `_objectives.Placed == false` 라 체인 벌칙이 이미 비활성이다 — 열화 모드 경계가 존재한다.

### 1.5 맵 해시와 스폰의 관계

`MapData.ComputeHash()`(`Shared/Collision/MapData.cs:82-110`)는 `Name` + `Boxes` + `Grid`(있을 때만)만 섞는다. **`spawns` 는 해시 밖이다.** 실무적 결론 둘:

1. 스폰 관련 스키마 확장은 **기존 맵 재-export 를 강제하지 않는다** (하위 호환이 공짜).
2. 해시는 스폰 불일치를 잡아 주지 않는다 — **이 작업의 안전망은 해시가 아니라 테스트다** (`docs/conventions.md:502-520` 의 yaw 표기 사고가 선례).

---

## 2. 설계: Seeker 전용 Chained Spawn Point

### 2.1 "Chained Spawn Point" 의 정의

**`AltarDragPoint` — 체인이 Seeker 를 내려놓는 바로 그 자리.** 별도의 좌표를 새로 발명하지 않는다. 이유:

- 기획 서사와 일치한다: "게임 시작 시 Chained 상태의 위치" = 체인 벌칙이 끝나는 자리.
- 배치 계약이 이미 검증돼 있다: 격자 중앙 최근접 `FreeFloor` + 인접 `FreeFloor` 착지점 (`docs/backrooms-v2-plan.md:263` 의 제단 계약). `FreeFloor` 는 "서버가 여기 플레이어를 세울 수 있다"는 뜻이므로 스폰 적합성이 정의상 보장된다.
- 좌표가 이미 전원에게 동기화된다 (`ObjectiveState`).
- 맵 파일·프로토콜·클라이언트를 건드리지 않고 서버 판정 한 곳만 바꾸면 된다.

yaw 는 `AltarDragPoint → 격자 중앙(AltarPosition)` 반대 방향, 즉 제단을 등지고 맵을 향하게 `DeterministicMath` 로 계산한다 (0 = +Z 라디안 규약, `docs/map-export-plan.md:50`). 착지점과 제단이 같은 셀 축상에 있어 방향이 퇴화하면 yaw 0 으로 폴백.

### 2.2 Runner / Seeker 스폰 로직 분리

`Room.Start` 의 스폰 루프(`:2418-2420`)를 다음 판정으로 바꾼다:

```csharp
// 스폰은 역할의 것이다. Seeker 는 체인이 끝나는 자리(제단 착지점)에서 시작하고,
// Runner 는 링 스폰을 PlayerId 로 나눠 갖는다. 착지점이 없는 맵(격자 없음)은
// 체인 벌칙이 없는 맵이므로 Seeker 도 링 스폰으로 돌아간다 — 열화의 경계를 새로 만들지 않는다.
if (player.PlayerId == _seekerPlayerId && _objectives.Placed)
{
    player.RespawnAt(_objectives.AltarDragPoint, SeekerSpawnYaw());
}
else
{
    player.RespawnAt(_map.SpawnPosition(player.PlayerId), _map.SpawnYaw(player.PlayerId));
}
```

- Runner 는 현행 그대로 `PlayerId` 인덱싱 — Seeker 가 링 슬롯 하나를 비우지만 `PlayerId` 는 고유하므로 겹침이 없고, 슬롯 재배열도 하지 않는다 (슬롯=스폰 커플링 해체는 `docs/game-lobby-plan.md:370` 의 별도 선행 과제이고 이 작업의 범위가 아니다).
- `Join`/`JoinBot` 은 손대지 않는다: 입장 시점에는 목표물이 배치되지 않았고(`Placed == false`), 대기 중 배치는 `Start` 가 어차피 덮어쓴다.

### 2.3 서버 권한 · 동기화 — 프로토콜 무변경

이동이 서버 권위이므로 (`Room.cs:2416-2417`, `PlayerEntity.cs:195-198`) 새 메시지·필드·비트가 **하나도 필요 없다**:

- Seeker 의 초기 위치는 `Playing` 첫 틱의 풀 스냅샷에 실려 나간다 — 기존 경로 그대로.
- ADR 0003 의 기준("놓쳤을 때 틀린 상태가 남는가")으로 봐도 스냅샷 전문이 이미 답이다.
- `EntityFlags` 는 8비트가 다 찼지만 아무 비트도 필요 없다: 시작 시 Seeker 는 `Chained`(= `ChainReleaseTick != 0`) 상태가 **아니다**. 제단 *위치* 에서 시작할 뿐 체인 벌칙 상태로 시작하는 것이 아니므로, 탄창은 가득이고 `RoleReveal` 의 `MovementLocked` 가 4초 정지를 이미 담당한다 (`Match.cs:111`).

시작 시점에 실제로 `Chained` 상태(탄창 0 + 3초 견인/대기)로 만들자는 해석도 가능하지만 채택하지 않는다 — 그러면 매치가 Seeker 탄창 0 으로 시작해 §4.3 벌칙의 의미(빈 탄창의 대가)가 사라지고, `PlayerEntity.cs:108-112` 의 "이 필드를 채우는 곳은 두 곳뿐" 계약을 깨야 한다. 필요해지면 별도 기획 결정으로 다룬다.

### 2.4 맵별 Chained Spawn Point 관리 구조

**1단계: 파생값 (authored 아님).** `AltarDragPoint` 는 격자에서 결정적으로 계산되므로 맵별 관리가 자동이다 — 맵 파일에 아무것도 추가하지 않는다.

**2단계(선택): authored 오버라이드.** 맵 디자이너가 제단 위치와 무관하게 Seeker 시작점을 지정하고 싶어지면, `docs/map-generator-tool-plan.md:466-476` §8.4 의 기존 계획을 그대로 쓴다:

- `MapSpawn` 에 `public int Team;` 추가 (0 = any, 1 = seeker 전용, 2 = runner 전용). 기본값 0 이라 기존 파일이 그대로 읽힌다.
- 스폰은 해시 밖이므로 재-export 불요, 해시 불변.
- 서버 판정: `Team == 1` 스폰이 있으면 그것을 Seeker 스폰으로, 없으면 `AltarDragPoint` 파생값으로. Runner 는 `Team != 1` 스폰만 인덱싱.
- Export 측: `NVproject/Assets/Scripts/Editor/MapCollisionExporter.cs` 가 `team` 필드를 쓰고, `MapDataValidator.InspectSimulation` 이 seeker 스폰의 접지·`FreeFloor` 를 기존 `InspectSpawns`(`MapDataValidator.cs:146-193`) 방식으로 검산.

이 문서의 구현 범위는 1단계다. 2단계는 필요가 생겼을 때의 경로만 열어 둔다.

---

## 3. 클라이언트(NVproject) 영향 분석

스폰 배치 코드는 **무수정**. 확인만 필요한 지점:

| 항목 | 확인 내용 |
|---|---|
| 초기 텔레포트 | `MatchIntroUI`(역할 공개 인트로)가 시작 텔레포트 순간을 가린다 — 기존과 동일하게 동작하는지 확인 |
| `HeldBannerUI` | HELD 배너가 `Frozen` 비트에 걸려 있는지, 체인 상태에 걸려 있는지 확인. `RoleReveal` 동안 전원 `Frozen` 이므로 기존에도 구분이 있었을 것 — 시작 위치가 제단이 되면서 오인 표시가 생기지 않는지 플레이 확인 |
| `ChainVisualController` | 체인 시각화 트리거가 위치가 아니라 상태(견인)에서 오는지 확인 — Seeker 가 제단 옆에 서 있는 것만으로 체인이 그려지면 안 된다 |
| 스냅 임계 | 시작 배치는 `HardResetTo` 경로라 `localSnapDistance`(2m)와 무관 — 영향 없음 |

UI/게임 플로우 관점의 실질 변화는 하나다: **Runner 가 시작 직후 제단 방향을 보면 Seeker 가 그곳에 있다는 것을 학습하게 된다.** 제단 위치는 어차피 전원에게 동기화되고 맵마다 고정이므로 정보 노출은 아니지만, 밸런스상 "Seeker 시작 위치가 항상 예측 가능"해진다 — 이것은 이 기획의 의도된 결과로 간주한다 (Seeker 자신도 세 번째 총알이 자기를 어디로 보낼지 알아야 한다는 `Objectives.cs:53` 의 원칙과 같은 결).

---

## 4. 호환성 · 예외 처리

| 케이스 | 처리 |
|---|---|
| 격자 없는 맵 (`test-room`) | `_objectives.Placed == false` → Seeker 도 링 스폰 폴백. 체인 벌칙이 없는 맵이라는 기존 열화 경계와 일치. `GridlessMatchTests` 가 경계를 고정 |
| 스폰 0개 맵 | `MapDataValidator.Validate` 가 이미 거절 (`MapDataValidator.cs:87`) — 변화 없음 |
| 재매치 | `Start` 가 매번 목표물을 재배치하고 체인 틱 필드를 0 으로 (`Room.cs:2446-2448`) — 제단이 맵별 고정이므로 재매치도 같은 자리 |
| 피격 순간이동 | `TeleportToRandomFreeFloor`(`Room.cs:1749-1767`) 무변경 — 매치 중 이동은 이 작업의 범위 밖 |
| 봇 Seeker (정적 룸) | `TryPickPreferredSeeker` 경로도 `_seekerPlayerId` 로 귀결되므로 같은 판정을 탄다 |
| 맵 해시 | 불변 — 클라이언트 재빌드·맵 재-export 불요 |
| 프로토콜 | 불변 — `ProtocolInfo.Version` 4 유지 |
| Runner 만 남은 스폰 인덱스 | Seeker 의 링 슬롯이 빌 뿐 겹침 없음. 스폰 8 ≥ 정원 5 이므로 wrap 도 일어나지 않음 |

**Seeker 시작 셀과 첫 피격 순간이동이 같은 셀을 뽑는 경우**: `_hitRandom` 씨드가 배치 씨드와 XOR 분리돼 있고(`Room.cs:2397`) 순간이동 대상은 Runner 이므로 충돌 경로가 없다.

---

## 5. 구현 우선순위 · 단계별 계획

### Phase 1 — 서버 판정 변경 (필수, 작음)

1. `Room.Start` 스폰 루프에 역할 분기 (§2.2 코드). Seeker yaw 계산 헬퍼는 `Room` 내 private — 정책은 모듈의 것 (`WorldMap.cs:42-43` 의 경계 유지).
2. 격자 없음 폴백 + 로그 한 줄 (기존 `:2401-2410` 경고와 나란히).
3. 테스트 (`tests/Modules.Tests/Realtime/`):
   - 새 `SeekerSpawnTests`: 격자 맵에서 Seeker 위치 == `AltarDragPoint`, Runner 위치 == 링 스폰, 격자 없는 맵에서 전원 링 스폰.
   - **무작위 함정 준수** (`docs/conventions.md:468-492`): Seeker 를 가정하지 말고 룸의 `SeekerPlayerId` 를 물어서 단언한다. `RoomFixture` 의 두 스폰 2m 간격에 기대는 기존 테스트(`EscapeTests`, `HitTests`)가 Seeker 위치 변화로 깨지지 않는지 **전체 실행을 여러 번** 돌려 확인 — Seeker 가 더는 `(0,0,0)`/`(-2,0,0)` 에 서지 않으므로 사격선 가정이 있는 테스트는 재점검 대상.
   - `ChainTests`: 시작 직후 `Chained == false`, 탄창 3발 단언 추가.
4. `dotnet build` 0 경고 + `dotnet test` 통과.

### Phase 2 — 클라이언트 QA (필수, 코드 변경 예상 없음)

1. Unity 에디터에서 두 클라이언트 플레이: Seeker 가 제단 착지점에서 시작하고 인트로가 텔레포트를 가리는지.
2. `HeldBannerUI` / `ChainVisualController` 오인 트리거 확인 (§3 표).
3. 탄창을 비워 실제 체인 벌칙이 시작 위치와 같은 자리로 끌고 오는지 확인 (서사 일관성 검증).

**검증 결과 (정적 분석, 2026-08-07).** 클라이언트 코드 변경 불필요가 확인되었다. 실제 파일명은
분석 보고서와 달리 `ChainDrag.cs` / `GameHudController.cs` / `MatchSync.cs` 다.

- **체인 오인 트리거 없음.** 체인 신호는 `EntityFlags.Frozen` 이지만 세 겹으로 걸러진다 —
  `MatchSync.ApplyBody` 가 클라이언트 `MatchPhase.Playing` 으로 가르고(리빌의 Frozen 은
  페이즈가 아직 `RoleReveal` 이라 통과하지 못한다), `MatchManager.AcceptChained` 가 Seeker
  역할로 가르고, `ChainDrag.SetServerChained` 는 값의 **변화**에만 반응한다. Seeker 는 제단
  *위치* 에서 시작할 뿐 `Chained` 상태가 아니므로 어느 겹에도 걸리지 않는다.
- **HELD 배너**는 `GameHudController` 가 `ChainDrag.Remaining` 을 읽어 그리므로 체인이 실제로
  걸려야만 나타난다. 시작 위치와 무관하다.
- **시작 텔레포트는 리빌이 가린다.** 초기 배치는 룸이 `Playing` 이 된 첫 틱의 스냅샷으로
  도착하고, 그 4초 동안 화면은 역할 공개다. `ServerPlacesAgents` 가 로컬 배치를 억제하므로
  이중 순간이동도 없다.
- **오프라인 연습 모드는 영향 밖이다** — 배치 판정이 서버(`Room.Start`)에 있으므로 세션 없는
  Play 는 기존 로컬 배치를 그대로 쓴다.
- 남은 것: 두 클라이언트 실플레이 육안 확인(3번 항목 포함). 에디터에서만 가능하다.

### Phase 3 — authored 오버라이드 (선택, 필요 시)

`MapSpawn.Team` (§2.4 2단계): exporter + validator + 서버 판정 우선순위. `ExportedMapTests.cs:55` 의 `Assert.Equal(8, ...)` 은 이때 "역할 무관 스폰 N개 + seeker 스폰 유무" 형태로 재정의한다.

### 범위 밖 (별도 과제로 기록)

- 스폰 선택을 `PlayerId` 에서 분리 (대기방 슬롯 이동의 선행 조건, `docs/game-lobby-plan.md:370,467`)
- 8스폰/5정원 불일치 해소 (`docs/backrooms-v2-plan.md:342`)
- 시작 시 Seeker 를 실제 `Chained` 상태(탄창 0)로 두는 변형 (§2.3 말미 — 기획 결정 필요)

---

## 6. 완료 후 기록

구현 중 30분 이상 잡아먹는 문제가 나오면 증상 → 원인 → 고침을 `docs/conventions.md` 에 남긴다. 특히 예상 함정: RoomFixture 기반 테스트의 사격선/간격 가정, 위층 제단 인접 셀의 `FreeFloor` float 왕복 오차 (`conventions.md:88`).
