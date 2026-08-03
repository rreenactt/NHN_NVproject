# LOOP PROGRESS — NVproject 인게임 구현

최종 갱신: 2026-08-04 (이터레이션 13)
현재 이터레이션: 13
기준 커밋: `9866e60`

## 이 루프가 실제로 하는 일

부트스트랩 조사 결과 한 줄 요약: **기획서의 인게임 규칙은 이미 거의 전부 구현되어 있고, 잘못된
자리에 있다.** `MatchManager.cs` 는 750줄짜리 완성된 심판이지만 **클라이언트 전원에서 각자 한 벌씩
돈다.** 서버가 아는 것은 넷뿐이다 — 시작 틱, Seeker, 배치 씨드, 종료 중계.

그래서 이 루프의 작업은 "기능 구현" 이 아니라 대부분 **판정 주체 이관**이다. 갭 매트릭스 39개 항목
중 `NONE` 은 보이스(§7) 3개뿐이고, 31개는 `PARTIAL` = 클라이언트에 있으나 서버가 판정하지 않음.

`NVserver/docs/match-authority-plan.md` 가 이 이관의 Phase 0~6 계획을 이미 코드 인용 기반으로
담고 있다(untracked 상태였다). 아래 백로그는 그 계획을 LOOP §6 의 작업 단위 규칙(8파일 이내,
독립 검증 가능)으로 쪼갠 것이다.

---

## 명령 카탈로그

부트스트랩에서 **실제로 실행해 확인한** 명령만 적는다.

| 용도 | 명령 | 확인 결과 |
|---|---|---|
| 서버 빌드+테스트 | `cd NVserver && dotnet test` | ✅ 통과 — Architecture 4 + Modules **205** = **209개**, 실패 0 (이터레이션 2 기준) |
| 서버 경고 0 확인 | `cd NVserver && dotnet build` | ✅ 경고 0개 오류 0개 (`TreatWarningsAsErrors` 라 IDE0011 같은 스타일 규칙도 빌드를 깬다) |
| **Unity 가 `Shared` 를 컴파일했는지** | `Unity_RunCommand` 로 ①`AssetDatabase.Refresh(ForceUpdate)` + `UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation()` → ②`NV.Client.*` 타입을 만지는 커맨드가 성공하는지 + `Library/ScriptAssemblies/Shared.dll` 의 타임스탬프 | ✅ 동작 (이터레이션 2). `Shared` 변경 뒤에는 `dotnet build` 만으로 충분하지 않다 — 이 절차가 그 확인이다 |
| 서버 단일 테스트 | `cd NVserver && dotnet test --filter "FullyQualifiedName~MovementTests"` | (표기만, `dotnet test` 로 검증됨) |
| 클라이언트 컴파일 검증 | `cd NVproject && dotnet build Assembly-CSharp.csproj` | ✅ **오류 0개**, 경고 2개 (MSB3277 `System.IO.Compression` 참조 통합 — 무해, 기존부터 있음) |
| 로컬 서버 실행 | `cd NVserver && dotnet run --project Api` | 미실행 (스모크 테스트 태스크에서) |
| EditMode 테스트 | **없음** | ❌ `com.unity.test-framework` 1.6.0 은 설치되어 있으나 `Assets/**` 에 **asmdef 가 0개**, 테스트 폴더도 없다. 인프라를 만들어야 한다 → IG-018 |
| 맵 export (Unity MCP) | `Unity_RunCommand` 로 `NV.Client.EditorTools.MapCollisionExporter.Export()` 호출 | ✅ 동작 (이터레이션 1). 씬이 열려 있어야 하고 play 모드가 아니어야 한다 |
| 맵 export (사람) | **Tools ▸ NV ▸ Map ▸ Export Map Collision** | 같음 |

**MCP 함정 — `Unity_RunCommand` 의 커맨드 어셈블리는 `Shared`(`com.nv.shared`)를 참조하지 않는다.**
`MapData` 를 이름으로 쓰든 `var` 로 받든 메서드 체인에 끼우든 전부
`CS0012: The type 'MapData' is defined in an assembly that is not referenced` 로 컴파일 실패한다.
그래서 MCP 커맨드에서 **맵 해시를 직접 계산할 수 없다**(`ComputeHash()` 의 수신자가 `MapData` 다).
우회는 `Shared` 타입이 시그니처에 나오지 않는 API 만 쓰는 것 — `INetworkMapSource.MapName`(string),
`CollisionBoxes`/`ComputeCollision()`(`IReadOnlyList<Bounds>`), `GetSpawns(List<(Vector3,float)>)` 는
문제없다. 박스 수와 스폰 수로 간접 검증하고, 해시는 서버 기동 로그에서 읽는다.

**MCP 함정 — exporter 의 `Debug.Log` 가 `Unity_GetConsoleLogs` 에 잡히지 않았다.** 박스 수·해시를
그 로그로 확인하려던 경로가 빈 배열을 돌려주므로, **부작용은 파일시스템에서 확인한다**
(`NVproject/CLAUDE.md` 가 이미 지시하는 규칙이며 이번에 실제로 필요했다). 콘솔이 비어 있는 것을
"에러 없음" 의 증거로 쓸 수 없다는 뜻이기도 하다 — 컴파일 성공은 위의 `Shared` 확인 절차처럼
**바인딩이 실제로 되는지**로 봐야 한다.

**MCP 함정 — 커맨드 래퍼의 namespace 가 `CompilationPipeline` 을 가로챈다.** 래퍼가 코드를
`Unity.AI.Assistant.Agent.Dynamic.Extension.Editor` 안에 넣으므로 `using UnityEditor.Compilation;`
을 써도 `Unity.CompilationPipeline` 로 해석되어 `CS0234` 가 난다. 완전 수식
(`UnityEditor.Compilation.CompilationPipeline`)으로 쓴다. `NVproject/CLAUDE.md` 가 적어 둔
`Mesh`/`Image` 충돌과 같은 부류이며, `Unity.` 로 시작하는 이름은 전부 이 위험을 갖는다.

**`Shared` 에 파일을 추가하면 `.meta` 가 함께 커밋되어야 한다.** `NVserver/Shared` 는
`Shared.asmdef` 를 가진 Unity 로컬 패키지다. 새 `.cs` 를 만들면 `.meta` 는 **Unity 가 임포트할 때**
생기므로, `AssetDatabase.Refresh` 를 한 번 돌리지 않으면 `.meta` 없이 커밋된다.

**주의 — 새 `.cs` 는 `Assembly-CSharp.csproj` 의 `Compile` 목록에 없다.** 추가 후 첫 빌드는
자기 namespace 에서 `CS0234` 로 실패한다. `<Compile Include="…" />` 를 넣고 다시 돌린다
(`NVproject/CLAUDE.md`). 이 csproj 는 gitignore 되어 있고 Unity 가 재생성한다.

**주의 — 클라이언트 컴파일 검증은 타입 체크일 뿐이다.** 플레이어를 빌드하지 않고 아무것도
실행하지 않는다. `Shared` 를 바꿨으면 Unity 에디터가 그것을 컴파일하는지도 확인해야 한다.

### 검증 전략 결정 (D-2)

**순수 게임 로직의 자동 테스트는 `dotnet test` 로 한다.** LOOP §7.2 는 순수 로직에 Unity EditMode
테스트를 요구하지만, 이 루프의 목적 자체가 그 로직을 **서버로 옮기는 것**이므로 옮긴 뒤의 로직은
`NVserver/tests/Modules.Tests` 의 대상이 된다. Unity EditMode 테스트는 클라이언트에 남는
**뷰 로직**(전문 → 이벤트 발화)에만 필요하고, 그 지점이 IG-018 이다. Unity 배치모드 테스트 실행은
MCP 브리지 환경에서 취약하므로 (§7.1 의 "그에 준하는 방법") 컴파일 검증 + 서버 테스트를 주
게이트로 삼는다.

---

## 메시지 카탈로그 (클라이언트 ↔ 서버)

`ProtocolInfo.Version` = **3** (IG-008 에서 2 → 3). 업그레이드 전 검사이며 불일치는 426 으로
거절된다 — `?v=2` 조회가 실제로 426 을 내는 것을 확인했다.

### 현존 (코드 확인)

| 메시지 | 방향 | 페이로드 | 권위 | 정의 위치 |
|---|---|---|---|---|
| `Input` `0x01` | C→S | `InputFrame` 7B × 최근 3틱 (buttons, moveX/Z, yaw, pitch) — **위치는 보내지 않는다** | 클라이언트 의도만 | `Shared/Contracts/Messages/InputFrame.cs` |
| `Control` `0x02` | C→S | `ControlKind` (1 `StartMatch`, 3 `EndMatch`, 4 `ReturnToLobby`) + value | **요청**, 서버가 재판정 | `Shared/Contracts/Enums/ControlKind.cs` |
| `Snapshot` `0x81` | S→C | `SnapshotHeader` 10B + `EntityState` 13B × N. 8인 = 114B. 매 틱, `Playing` 에서만 | 서버 | `Shared/Contracts/Messages/SnapshotHeader.cs` |
| `Event` `0x82` + `EventKind.RoomState=1` | S→C | `RoomStateHeader` 15B (phase, host, seeker, outcome, startTick, **placementSeed**, count) + 명단 | 서버 | `Shared/Contracts/Messages/RoomStateMessage.cs` |
| `Welcome` `0x83` | S→C | 13B (protocolVersion, playerId, serverTick, mapHash, tickRate) | 서버 | `Shared/Contracts/Messages/WelcomeMessage.cs` |
| `Event` `0x82` + `EventKind.MatchState=2` | S→C | 고정부 9B (phase, timeRemaining u16 0.1초, keysInserted, escapes, outcome, count) + 참가자 5B × N (playerId, role, flags, hits, carriedKeys). 8인 = 49B. **세션별 인코딩** | 서버 | `Shared/Contracts/Messages/MatchStateMessage.cs` |

| `Event` `0x82` + `EventKind.ObjectiveState=3` | S→C | 고정부 5B (flags, keyCount, deviceCount) + 제단 12B + 문 9B(위치·yaw·개방) + 열쇠 6B × N + 장치 10B × N. 최악 ≈176B. **변경 즉시 + 5초**, 세션별 인코딩 | 서버 | `Shared/Contracts/Messages/ObjectiveStateMessage.cs` |

`ObjectiveState` 는 IG-011b 에서 들어왔다. **`MatchState` 보다 한 걸음 더 나간 필터다** —
값을 0 으로 채우는 것이 아니라 **Seeker 사본에서 문 블록 자체를 뺀다.** 0 으로 채우면 "문이
있다" 는 사실과 블록 크기가 여전히 남기 때문이다. 헤더의 `HasDoor` 비트도 함께 내려가고,
전문 길이가 정확히 9B 짧아진다.

열쇠·제단·장치는 전원 공통이다. 룰셋이 그렇게 정한다 — 복도의 열쇠는 물리적 물건이고 Seeker 가
그것을 보는 것이 열쇠를 지키는 전술을 만든다. 제단은 벌칙 지점, 장치는 §5.3 의 파괴 대상이다.

주기가 다르다(2Hz 가 아니라 5초). 배치는 매치 중 거의 바뀌지 않으므로 2Hz 로 보내면 8인 룸에서
2.8KB/s 가 더 붙고 그만큼의 정보가 없다. **바뀐 틱에는 즉시 보낸다.**

아직 0 으로 나가는 필드: 장치 `State`(소진·파괴·쿨다운 → IG-013·IG-015), 문 개방 여부(→ IG-012).

`MatchState` 는 IG-008 에서 들어왔다. **`RoomState` 와 성격은 같고(전문, 2Hz + 변경 즉시, 멱등)
본문이 수신자마다 다르다** — Seeker 사본에서는 `keysInserted` 와 모든 `carriedKeys` 가 0 이다.
필터는 `MessageCodec.WriteMatchState` 안에 있어 호출부가 우회할 수 없다.

아직 서버가 세지 않아 0 으로 나가는 필드: `keysInserted`, `escapes`, `flags`, `hits`,
`carriedKeys`(→ IG-012·IG-014), `outcome`(→ IG-007). **자리를 잡아 두었으므로 값이 채워질 때
와이어 포맷은 바뀌지 않는다.**

`RoomState` 는 **알림이 아니라 전문**이다 — 2Hz + 변경 즉시, 멱등, 영구 반복. 한 번짜리 알림은
세션의 `Bounded(32, DropOldest)` 채널이 버리는 프레임이 될 수 있고, 그 클라이언트는 로비 화면에
영구히 남는다.

### 신규 예정 (프로토콜 → **3**)

| 메시지 | 방향 | 페이로드 | 권위 | 태스크 |
|---|---|---|---|---|
| ~~`EventKind.MatchState=2`~~ | — | **완료 (IG-008)** — 위 현존 표로 옮겼다 | — | ✅ |
| ~~`EventKind.ObjectiveState=3`~~ | — | **완료 (IG-011b)** — 아래 현존 표로 옮겼다 | — | ✅ |
| ~~`EntityFlags` 확장~~ | — | **완료 (IG-009)** — `Bleeding=8`, `Seeker=16`, `Escaped=32`, `Frozen=64`. `EntityState` 13B 그대로. `Seeker`·`Frozen` 은 실제 값이 나가고 `Bleeding`·`Escaped` 는 0(→ IG-014·IG-012) | 서버 | ✅ |

`MatchPhase`(Lobby/RoleReveal/Playing/Ended)는 **IG-006 에서 이미 `Shared/Contracts/Enums` 에
들어왔다.** 아직 어떤 프레임에도 실리지 않으며, `MatchState` 전문의 첫 바이트가 될 값이다.
클라이언트에 같은 이름의 열거형(`NV.Game.MatchPhase`)이 남아 있고 값이 같다 — 통합은 IG-010.
| `ButtonFlags.Interact=1<<4` | C→S | `All` 마스크 함께 수정. **대상 id 는 싣지 않는다** — 서버가 근접+시선을 재계산 | 서버 재판정 | IG-013 |
| 삭제: `ControlKind.EndMatch=3` | — | 서버가 결과를 정하므로 방장 보고 경로가 사라진다. 값 3 은 비워 두고 주석. **IG-008 에서 하지 않았다** — 서버만 먼저 제거하면 클라이언트가 아직 그 경로로 보고하는 동안 매치가 끝나지 않는다 | — | IG-010 |
| 삭제: `RoomStateHeader.PlacementSeed` | — | 와이어에서 뺀다 (술래에게 문 좌표가 새는 경로). `WireSize` 15 → 11 | — | IG-011 |

**두 전문 모두 세션별 인코딩이 필요하다.** 역할별 필터링을 와이어에서 해야 하기 때문이다 —
술래 사본에서는 열쇠 진행도와 문 좌표를 빼야 하고, 클라이언트에서 숨기면 디컴파일로 되살아난다.

---

## 태스크 백로그

| ID | 제목 | 상태 | 우선순위 | 의존 | 요구사항 ID |
|---|---|---|---|---|---|
| IG-001 | 맵 이름·등록·export 정합성 복구 | **DONE** | P0 | - | R-0.1, R-0.2 |
| IG-002 | `MapData` 격자 스키마 + 해시 + `DeterministicSequence` | **DONE** | P0 | IG-001 | R-0.3 |
| IG-003 | 클라이언트 격자 export (`MapExport`·`INetworkMapSource`) | **DONE** | P0 | IG-002 | R-0.3 |
| IG-004 | 서버 `MapGrid` 질의 + 테스트 | **DONE** | P0 | IG-003 | R-0.3 |
| IG-005 | `MatchConstants` 분리 (`Shared`) + `GameConfig` 프로퍼티 대체 | **DONE** | P1 | IG-004 | R-1.x 전반 |
| IG-006 | 서버 매치 단계·시계 (`Match.cs`) | **DONE** | P1 | IG-005 | R-1.3, R-1.4, R-1.6 |
| IG-007 | 승리 조건 판정 | **BLOCKED** | P1 | IG-006 | R-1.5, R-6.6 |
| IG-008 | `MatchState` 전문 + 역할별 필터 + 프로토콜 3 | **DONE** | P1 | IG-006 | R-1.3~R-1.5, R-9.x |
| IG-009 | `EntityFlags` 확장 (Bleeding/Seeker/Escaped/Frozen) | **DONE** | P1 | IG-008 | R-2.2, R-4.1, R-5.1 |
| IG-010 | 클라이언트 전문 수신·적용 (뷰 전환 1/2) | **DONE** | P1 | IG-008 | R-1.3, R-1.4, R-9.x |
| IG-022 | 클라이언트 예측에 `Frozen` 반영 + 플래그 필터 | **DEFERRED** | P2 | IG-010 | R-1.6 |
| IG-023 | 클라이언트 이동 예측 + 리컨실리에이션 | TODO | P4 | - | (품질) |
| IG-021 | 클라이언트 판정 경로 제거 (뷰 전환 2/2) | **BLOCKED** | P1 | IG-010, IG-007 | R-1.5, R-3.1 |
| IG-011a | 서버 목표물 배치 (제단·문·열쇠·장치) | **DONE** | P2 | IG-010 | R-6.3, R-7.1 |
| IG-011b | `ObjectiveState` 전문 + 역할별 필터 | **DONE** | P2 | IG-011a | **R-2.3**, R-6.4 |
| IG-011c1 | 배치 코드를 `Shared` 로 이동 (ADR 0002) | **DONE** | P2 | IG-011b | (기반) |
| IG-011c2 | 클라이언트 목표물 수신·적용 + 클라이언트 배치 제거 | TODO | P2 | IG-011c1 | R-6.3, R-7.1 |
| IG-011c3 | `PlacementSeed` 와이어 제거 | TODO | P2 | IG-011c2 | **R-2.3** |
| IG-012 | 열쇠 습득·삽입·문 개방·탈출 판정 | TODO | P2 | IG-011 | R-6.1, R-6.2, R-6.5, R-6.7 |
| IG-013 | `Interact` 입력 + 장치 사용 판정 | **BLOCKED** | P2 | IG-011 | R-7.2~R-7.7, R-4.3 |
| IG-014 | 서버 발사체 + 피격 규칙 + 탄약 | TODO | P2 | IG-009 | R-3.1~R-3.6, R-2.1 |
| IG-015 | 장치 파괴 (4발) | TODO | P3 | IG-013, IG-014 | R-7.8 |
| IG-016 | 체인 드래그 서버 판정 | **BLOCKED** | P3 | IG-014 | R-3.7 |
| IG-017 | 근접 보이스 시스템 | **BLOCKED** | P3 | - | R-8.1~R-8.3 |
| IG-018 | Unity EditMode 테스트 인프라 (asmdef) | TODO | P2 | IG-010 | (검증 수단) |
| IG-019 | 상수 정리·문서 갱신·죽은 경로 제거 | TODO | P4 | 전부 | (정리) |
| IG-020 | 레거시 맵 파일·스크립트 정리 | **BLOCKED** | P4 | IG-001 | R-0.2 |

**배포 단위 주의:** IG-008 이 `ProtocolInfo.Version` 을 3 으로 올린다. 그 이후 IG-014 까지는
**서버와 클라이언트를 같은 커밋에 배포**해야 한다 — 구버전 클라이언트는 426 으로 전부 거절되고
WebGL 빌드는 수 분이 걸린다. 버전은 한 번만 올린다.

---

## 태스크 상세

### IG-001 — 맵 이름·등록·export 정합성 복구
- 상태: **DONE** (이터레이션 1, 2026-08-04)
- 기획서 근거: (선행 차단 요소, R-0.1·R-0.2) — 기획서 항목은 아니지만 인게임 전체를 막는다
- 문제: `BackroomsMapGenerator.cs:113` 의 `MapName` 이 `"backrooms2f"`, `SessionSceneRouter` 는
  `"backrooms"` → `SampleScene`, `appsettings.json` 에 `backrooms2f` 미등록,
  `MapData/backrooms.json` 은 레거시 export(1367박스·±89.6m) — 현재 씬 지형은 735박스·±43.5m.
  **로비로 방을 만들면 접속마다 맵 해시 불일치가 확정된다.**
- 계획:
  1. `BackroomsMapGenerator.MapName` 을 `"backrooms"` 로 바꾼다 (고칠 곳이 가장 적다 — 라우터
     표와 `Game:Maps` 기본 항목이 이미 그 이름이다).
  2. **Tools ▸ NV ▸ Map ▸ Export Map Collision** 을 `SampleScene` 에서 실행해
     `MapData/backrooms.json` 을 현재 지형으로 덮는다.
  3. 서버 기동 로그의 `맵 backrooms: … 박스 N개` 가 새 값인지 확인.
  4. **레거시 삭제는 이 태스크에서 하지 않는다** → OQ-5 (확인 대기). `BackroomsMap.cs`,
     `backrooms2f.json`, `arena.json`.
- 변경 파일 (2개):
  - `NVproject/Assets/Scripts/BackroomsMapGenerator.cs` — `MapName` `"backrooms2f"` → `"backrooms"`,
    이 이름이 세 곳(export 파일명 · 서버 등록 키 · 라우터 조회)에서 동시에 하중을 받는다는 설명을 주석으로
  - `NVserver/MapData/backrooms.json` — export 재실행으로 갱신 (1376줄 삭제, 761줄 추가)
- 검증 (전부 실행함):

  | 확인 | 명령/수단 | 결과 |
  |---|---|---|
  | 씬 전제 | `Unity_RunCommand` 프로브 | `scene=SampleScene`, `isPlaying=False`, `levelComponent=BackroomsMapGenerator`, `MapName=backrooms2f` (변경 전) |
  | export 실행 | `MapCollisionExporter.Export()` via MCP | 파일 141492B → **70864B** |
  | export 내용 | PowerShell `ConvertFrom-Json` | `name=backrooms`, **박스 736개**, 스폰 8개, x ±52.50 (=35셀×3m), y −0.20..6.40 (=2층×3.2m) |
  | 서버 로드 | `dotnet run --project Api` 기동 로그 | `맵 default: backrooms 해시 3B4B1D41 박스 736개 스폰 8개` |
  | 서버 테스트 | `dotnet test` | ✅ **173개 통과**, 실패 0 (`ExportedMapTests` 가 새 736박스 지형에서 스폰 8개의 겹침·착지·전진통과를 검사) |
  | 클라이언트 컴파일 | `dotnet build NVproject/Assembly-CSharp.csproj` | ✅ 오류 0개 (경고 2개는 기존 MSB3277) |
  | export 경로 재현 | `src.ComputeCollision().Count` via MCP | **736**, 스폰 8, `MapName=backrooms` |
  | **런타임 경로 일치** | Play 모드 진입 후 `src.CollisionBoxes.Count` | **736** — `Generate()` 와 `ComputeCollision()` 이 같은 박스를 낸다 |
  | 씬 오염 없음 | `git status --short` | `SampleScene.unity` **변경 없음**. 변경은 2개 파일뿐 |

- 결과: 체인이 닫혔다. `default`(클라이언트가 요청하는 map id, `CreateRoomPopup.cs:19`) →
  `backrooms.json` → `WorldMap.Name = "backrooms"` → 라우터 `"backrooms"` → `SampleScene` →
  `BackroomsMapGenerator.MapName = "backrooms"` → 해시 대조 대상이 같은 파일. 이전에는 마지막
  링크가 `"backrooms2f"` 라 끊겨 있었다.
- 비고:
  - **런타임과 export 가 다른 경로를 쓴다는 점이 이 태스크의 실질적 위험이었다.** 접속 시
    클라이언트는 `Generate()` 가 채운 `CollisionBoxes` 로 해시를 계산하고
    (`NetworkBootstrap.cs:350`), export 는 지오메트리를 만들지 않는 `ComputeCollision()` 을
    쓴다. 둘이 어긋나면 이름을 고쳐도 해시가 계속 불일치한다. `_collisionOnly` 플래그가 같은
    `Prepare`/`SolveGrid`/`BuildGeometry` 를 돌며 지오메트리 생성만 건너뛰는 구조이고
    (`BackroomsMapGenerator.cs:168-181`), **양쪽 다 736 으로 실측해 확인했다.**
  - `backrooms2f.json` 은 735박스로 남아 있다 — 새 export 보다 1개 적은 것은 나중에 추가된
    `Ceiling Lid` 가 그 export 에 없었기 때문이다. 이제 확실히 죽은 파일이다 → IG-020.
  - `match-authority-plan.md` §3 의 "`backrooms2f.json` … 범위 ±43.5m 부근" 은 부정확했다.
    실측 ±52.50m (35셀 × 3m = 105m). 다른 결론에는 영향이 없다.
  - **접속 실측(맵 해시 `일치` 로그)은 하지 않았다.** 두 클라이언트를 띄워 로비에서 방을 만들고
    시작하는 절차는 MCP 로 입력을 주입할 수 없어(`NVproject/CLAUDE.md`) 사람의 조작이 필요하다.
    세 경로가 모두 736 으로 일치하므로 해시 일치는 확정적이지만, **실측은 IG-010 의 동기화 스모크
    테스트에서 함께 확인한다.**

### IG-002 — `MapData` 격자 스키마 + 해시 + `DeterministicSequence`
- 상태: **DONE** (이터레이션 2, 2026-08-04)
- 기획서 근거: R-0.3 — 서버가 "여기 설 수 있는가" 를 답해야 R-3.4·R-6.x·R-7.1 이 가능해진다
- 계획: `MapGridData { Floors, Width, Depth, CellSize, FloorHeight, OriginX, OriginZ, Cells[] }`.
  셀당 1바이트 `MapCellFlags` — `Standable`(격자 통행 가능), `FreeFloor`(플레이어 캡슐이 실제로
  들어감 = 계단·기물 제외), `StairLink`(위층 수직 연결). 세 개를 나누는 이유는 쓰임이 다르다:
  열쇠는 `Standable`, 제단·순간이동 착지점은 `FreeFloor`, `StairLink` 는 경로 탐색용.
  `DeterministicSequence` 는 xorshift32 + 상태 명시 — 기존 `DeterministicRandom` 은
  (틱,엔티티,salt)→값 **무상태 해시**라 같은 틱에 여러 번 뽑으면 같은 값이 나오고, "열쇠 10개를
  차례로" 같은 수열을 만들 수 없다.
- 변경 파일 (7개):
  - `Shared/Collision/MapGridData.cs` (신규) — `MapCellFlags` + `MapGridData`,
    `CellIndex`/`InBounds`/`At`/`Has`/`TryValidate`/`CombineInto`
  - `Shared/Collision/MapData.cs` — `Grid` 프로퍼티 + `HasGrid`, `ComputeHash` 에 격자 반영
  - `Shared/Simulation/DeterministicSequence.cs` (신규)
  - `Infrastructure/FileSystem/MapLoader.cs` — 어긋난 격자를 로드 단계에서 거절
  - `tests/Modules.Tests/Simulation/DeterministicSequenceTests.cs` (신규, 11개)
  - `tests/Modules.Tests/Simulation/MapGridDataTests.cs` (신규, 16개)
  - `tests/Modules.Tests/Realtime/MapLoaderGridTests.cs` (신규, 4개)
  - (+ `Shared` 의 새 `.cs` 2개에 대한 `.meta` 2개)
- 검증 (전부 실행함):

  | 확인 | 명령/수단 | 결과 |
  |---|---|---|
  | 서버 테스트 | `dotnet test` | ✅ **209개 통과**, 실패 0 (173 → 209, +36) |
  | 서버 경고 0 | `dotnet build` | ✅ 경고 0개 오류 0개 |
  | 클라이언트 컴파일 | `dotnet build NVproject/Assembly-CSharp.csproj` | ✅ 오류 0개 (경고 2개는 기존 MSB3277) |
  | **Unity 가 `Shared` 를 컴파일** | Refresh + `NV.Client.*` 바인딩 + `Shared.dll` 타임스탬프 | ✅ `Shared.dll` 00:12:50 재작성(요청 시점), `Assembly-CSharp` 바인딩 성공 |
  | `.meta` 생성 | 파일시스템 | ✅ `MapGridData.cs.meta`, `DeterministicSequence.cs.meta` |
  | **회귀 — 해시 불변** | `dotnet run --project Api` 기동 로그 | ✅ `맵 default: backrooms 해시 3B4B1D41 박스 736개` — IG-001 과 **같은 값** |
  | **base64 왕복** | `MapLoaderGridTests.격자가_base64_로_왕복한다` | ✅ 8바이트 격자가 base64 로 오가고 플래그가 좌표에 정확히 앉는다 |

- 결정 두 개를 여기서 확정했다 (DECISIONS D-3·D-4 참고):
  - **`Cells` 는 `byte[]` → base64.** System.Text.Json 의 기본 동작이라 서버 파싱 코드가 0줄이고,
    2층 35×35 = 2450셀이 한 줄에 들어간다. 숫자 배열이면 같은 정보가 4배 넘게 커진다.
    클라이언트 export 는 JSON 을 손으로 쓰므로 `Convert.ToBase64String` 을 내면 된다(IG-003).
  - **`Grid == null` 이면 해시에 기여하지 않는다.** 계획서(`match-authority-plan.md` §4)는 격자를
    해시에 넣으면 "이번에도 export 를 다시 돌려야 한다" 고 했지만, 없을 때 0 을 섞으면 격자가
    아직 **없는** 기존 맵 파일 전부의 해시가 바뀌어 정보를 하나도 늘리지 않는 re-export 를
    강요한다. 없으면 빼고, **있으면 반드시 넣는다.** 덕분에 IG-002 는 해시를 바꾸지 않아
    회귀 위험이 0 이고, 해시는 격자가 실제로 채워지는 IG-003 에서 한 번만 바뀐다.
- 비고:
  - `CellIndex` 식(`((floor * Depth) + z) * Width + x`)은 **이 한 곳에만** 있다. 클라이언트
    export 와 서버 조회의 순서가 어긋나면 격자가 90도 돌아간 채 크기와 해시가 모두 맞아,
    증상이 "맵의 절반에서만 열쇠가 벽에 박힘" 으로 나타난다. 유일성 테스트를 붙였다.
  - `NextInt` 는 거부 표집이다. 나머지 연산만 쓰면 앞쪽 셀이 한 번 더 뽑힐 기회를 갖고, 후보가
    수천 개일 때 그 치우침이 배치에 보인다. 16버킷 160,000회로 ±5% 안을 확인했다.
  - `DeterministicSequence` 는 구조체다. `default(...)` 로도 만들어지고 그 내부 상태 0 은
    xorshift 의 **고정점**이라 계속 0 만 낸다(증상: 목표물이 전부 한 자리에 겹침). 씨드 0 과
    `default` 둘 다 걸러 내고 테스트로 고정했다.
  - `DeterministicSequence` 를 초대 코드·방장 토큰에 쓰면 안 된다 — 그쪽은 예측 불가능해야 하므로
    `RandomNumberGenerator` 다(`conventions.md`). 클래스 주석에 적어 두었다.
  - **`MapGrid` 질의(`TryRandomPoint`/`TryNearestFreeFloor`/`CellToWorld`/`FloorIndexAt`)는 아직
    없다.** IG-004 의 범위다. 지금은 스키마와 조회 원시연산(`At`/`Has`)만 있다.

### IG-003 — 클라이언트 격자 export
- 상태: **DONE** (이터레이션 3, 2026-08-04)
- 계획했던 것과 **다르게 구현했다.** 계획은 `FreeFloor` 를 `MatchManager.IsFreeFloor` 와 같은
  캡슐(`Physics.CheckCapsule`, 반지름 0.32)로 계산하는 것이었다. 두 가지 이유로 불가능하고 부정확했다:
  1. **불가능** — export 는 지오메트리를 만들지 않는 경로(`ComputeCollision`, `_collisionOnly`)로
     도는데 `Physics.CheckCapsule` 은 씬에 실제 콜라이더가 있어야 답한다. 그 경로에서 물리 질의는
     전부 "아무것도 없음" 을 돌려주고 결과는 **모든 셀 통과**다.
  2. **부정확** — 캡슐 반지름 0.32 는 서버 플레이어 박스의 `SimConstants.PlayerRadius`(0.4)보다
     **작다.** 작은 프로브는 서버가 밀어낼 자리를 통과시킨다.
  대신 **서버의 플레이어 박스 + 서버의 충돌 코드**로 판정한다(`MapGridBuilder`, `Shared`).
  계단이 걸러지는 성질은 유지된다 — 계단 스텝은 콜리전 박스로 export 되므로
  (`AddBox("Step", …)`, `BackroomsMapGenerator.cs:868`) 박스 목록 안에 있다.
- 변경 파일 (9개):
  - `Shared/Collision/MapGridBuilder.cs` (신규) — `MarkFreeFloor`/`IsFree`/`StandingHalfExtents`
  - `Shared/Collision/MapGridData.cs` — `CellToWorld`, `FloorIndexAt` 추가
  - `Net/INetworkMapSource.cs` — `BuildGrid()` 추가 (`null` 허용)
  - `Net/MapExport.cs` — `AttachGrid`: 격자를 싣고 `FreeFloor` 를 채운다
  - `Editor/Map/MapCollisionExporter.cs` — `grid` 블록 직렬화 (base64), 로그에 격자 통계
  - `BackroomsMapGenerator.cs` — `BuildGrid()` 구현 (`Standable`·`StairLink`)
  - `TestRoomMap.cs` — `BuildGrid() => null`
  - `BackroomsMap.cs` — `BuildGrid() => null` (레거시, 인터페이스 확장에 걸려 어쩔 수 없이 수정)
  - `tests/…/ExportedMapTests.cs` — 격자 검증 5개
  - (+ `MapData/backrooms.json` 재생성, `MapGridBuilder.cs.meta`)
- 검증 (전부 실행함):

  | 확인 | 수단 | 결과 |
  |---|---|---|
  | 서버 테스트 | `dotnet test` | ✅ **214개 통과**, 실패 0 (209 → 214) |
  | 서버 경고 0 | `dotnet build` | ✅ 오류 0개 |
  | 클라이언트 컴파일 | `Assembly-CSharp` + `Assembly-CSharp-Editor` | ✅ 둘 다 오류 0개 |
  | export 내용 | PowerShell base64 디코드 | ✅ 2층 35×35 = **2450셀**, `Standable` 583, `FreeFloor` 574, `StairLink` 30 |
  | `StairLink` 정합 | 계산 대조 | ✅ 30 = stairwell 3×5 × 2층 (정확히 일치) |
  | 층별 `FreeFloor` | PowerShell | ✅ 1층 275/284, 2층 299/299 |
  | 불변식 | PowerShell + 테스트 | ✅ `FreeFloor ⊆ Standable`, 위반 0개 |
  | **해시 변경** | 기동 로그 | ✅ `backrooms` `3B4B1D41` → **`7996AF3A`** (D-4 대로 격자 포함) |
  | **해시 불변** | 기동 로그 | ✅ `test-room` `27A9412D` 그대로 (격자 없음 → 기여 없음) |
  | 좌표계 검산 | `FreeFloor_로_표시된_칸에는_실제로_플레이어가_들어간다` | ✅ 서버가 574셀 전부를 자기 충돌 코드로 재검증 |

- **이 태스크에서 실제 버그를 하나 찾아 고쳤다.** 첫 export 에서 **위층 `FreeFloor` 가 0개**였다
  (1층 275, 2층 0). 격자 크기·플래그 검증·맵 해시는 전부 통과했으므로 자동 검증으로는 잡히지
  않는 종류였다. 원인은 박스 하단을 `(feet + halfY) - halfY` 로 왕복 계산하는 데 있었다 —
  float 에서 `(3.2f + 0.9f) - 0.9f == 3.1999999` 이고 발밑보다 아래라, 박스가 바닥 슬래브를
  1e-7 만큼 파고들어 `Depenetrate` 가 밀어냈다. **발밑이 정확히 `0f` 인 1층만 오차가 0 이라
  무사했고**, 그래서 증상이 "위층에만 목표물이 생기지 않는다" 로만 나타난다.
  판정 시 발밑을 `SimConstants.SkinWidth` 만큼 올려 고쳤다 — 임의의 여유가 아니라 서버가
  접촉면에 정지할 때 실제로 띄우는 값이다(`CollisionWorld.MoveBox`). `conventions.md` §시뮬레이션
  에 기록했고, `모든_층에_몸이_들어가는_셀이_있다` 테스트로 고정했다.
- 비고:
  - `test-room` 은 격자를 내놓지 않는다. 계획서는 "방 하나이므로 전부 `FreeFloor`" 라고 했지만
    그 맵에는 **중앙 플랫폼과 커버 블록 4개**가 있어 전부 채우면 블록 안이 걸을 수 있는 곳이
    된다. 그리고 그 씬은 매치 규칙을 돌리지 않으므로(`MultiplayerTest` 는 규칙 없는 몸만 필요)
    배치할 목표물이 없다. 격자 없음이 정확한 답이고, 덕분에 `test-room.json` 해시도 안 바뀐다.
  - `StairLink` 는 stairwell 사각형 **전체**에 세운다. 위층 샤프트 셀은 일부러 `Standable` 이
    아니지만(바닥이 없다) 경로가 층을 넘는 자리가 정확히 거기다.
  - 레거시 `BackroomsMap.cs` 가 `INetworkMapSource` 를 구현하고 있어 인터페이스 확장에 걸렸다.
    삭제는 OQ-5 대기 중이라 `BuildGrid() => null` 한 줄로 맞췄다. **레거시가 살아 있는 비용이
    이번에 실제로 발생했다** — OQ-5 의 가치가 올랐다.
  - 파일 9개로 §6.1 의 8개를 하나 넘겼다. 인터페이스를 확장하면 구현체를 모두 맞춰야 하고
    (레거시 포함), 검증 없이 `DONE` 을 적을 수 없어 테스트도 같은 단위에 들어간다.

### IG-004 — 서버 `MapGrid` 질의 + 테스트
- 상태: **DONE** (이터레이션 4, 2026-08-04)
- 계획: `Shared/Collision/MapGrid.cs` — 무작위 `FreeFloor` 선택과 최근접 탐색.
  `CellToWorld`·`FloorIndexAt` 은 IG-003 에서 이미 `MapGridData` 에 들어갔다.
- 변경 파일 (5개):
  - `Shared/Collision/MapGrid.cs` (신규) — `TryRandomFreeFloor(ref seq, …)`,
    `TryNearestFreeFloor(pos, …)`, `FreeFloorCount`
  - `Shared/Collision/MapGridData.cs` — `TryWorldToCell` 추가 (`CellToWorld` 의 역)
  - `Shared/Collision/WorldMap.cs` — `Grid`·`HasGrid` 노출 (격자 없으면 `null`)
  - `tests/Modules.Tests/Simulation/MapGridTests.cs` (신규, 12개)
  - `tests/Modules.Tests/Realtime/ExportedMapTests.cs` — 실제 맵 질의 검산 3개
  - (+ `MapGrid.cs.meta`)
- 검증 (전부 실행함):

  | 확인 | 수단 | 결과 |
  |---|---|---|
  | 서버 테스트 | `dotnet test` | ✅ **229개 통과**, 실패 0 (214 → 229, +15) |
  | 서버 경고 0 | `dotnet build` | ✅ 오류 0개 |
  | 클라이언트 컴파일 | `Assembly-CSharp` | ✅ 오류 0개 |
  | Unity `Shared` 컴파일 | Refresh + `.meta` 생성 | ✅ `MapGrid.cs.meta` |
  | **실제 맵 무작위 질의** | `무작위_질의가_돌려준_자리에_플레이어가_들어간다` | ✅ `backrooms` 에서 500회 뽑아 전부 서버 충돌 코드로 검산 |
  | **실제 맵 최근접 탐색** | `스폰_근처에서_가장_가까운_자리를_찾는다` | ✅ 스폰 8곳 전부에서 찾고, 찾은 자리에 플레이어가 들어간다 |
  | 재현성 | `같은_씨드는_같은_자리를_고른다` | ✅ 64회 연속 일치 |
  | **회귀 — 해시 불변** | 기동 로그 | ✅ `backrooms 7996AF3A`, `test-room 27A9412D` — IG-003 과 동일 (질의 계층이라 해시에 영향 없어야 하고, 실제로 없다) |

- **테스트가 결함 하나를 잡았다.** `TryNearestFreeFloor` 를 격자 밖 먼 좌표
  (`-50, -50`)에서 부르면 시작 셀이 `(-25, -25)` 가 되는데, 링 반지름 상한이 격자
  크기(35)라 **링이 격자에 닿기 전에 상한에 걸려** "찾지 못했다" 로 끝났다. 주석은
  "가장자리에서 안쪽으로 링이 자라 들어온다" 고 적어 두었지만 코드가 그렇게 하지
  않았다. 시작 셀을 격자 안으로 클램프해 고쳤다 — 당겨 놓은 자리가 곧 그 좌표에서
  가장 가까운 격자 셀이므로 답의 뜻도 달라지지 않는다.
- 비고:
  - **`TryRandomFreeFloor` 는 셀 중심을 돌려준다. 지터를 주지 않는다** (AS-7). 클라이언트의
    같은 함수는 `margin` 0.55 로 셀 안에서 흔들어 열쇠 10개가 격자에 정렬되지 않게 하는데,
    그 지터는 `FreeFloor` 의 보장 밖이다 — 이 플래그는 셀 **중심**에서 플레이어 박스를
    검사해 세워진 값이다. 셀 중심에서 벽 내측면까지 1.375m 인데 지터 폭이 0.95m 면 여유가
    0.425m 로 줄어, 반지름 0.4 인 서버 박스와 0.025m 차이다. 열쇠는 콜라이더가 없어 무해하지만
    **순간이동 착지점은 플레이어다.** 지터가 필요하면 흔든 뒤 `MapGridBuilder.IsFree` 로 다시
    검사해야 하고, 그 판단은 배치 태스크(IG-011)의 몫이다.
  - 후보 목록을 생성 시점에 **월드 좌표로** 만들어 둔다. 셀 인덱스를 저장하면 뽑을 때마다
    역변환(인덱스 → floor·x·z)이 필요하고 그 식은 `CellIndex` 의 역이라 두 곳에서 어긋날 수 있다.
    목록의 순서(층 → z → x)가 곧 무작위 선택의 색인이므로 고정되어 있어야 한다.
  - `TryNearestFreeFloor` 는 **같은 층에서만** 찾는다. 격자 거리로는 바로 위층 셀이 가장
    가깝지만 그리로 걸어갈 수는 없다.
  - 격자가 없는 맵의 `WorldMap.Grid` 는 `null` 이다. 빈 `MapGrid` 를 만들어 주면 호출자가
    "후보 0개" 와 "격자 없음" 을 구분할 수 없다.
  - `MathF.Floor` 를 `TryWorldToCell` 에 썼다. `conventions.md` 가 IEEE 754 규정 함수로
    명시적으로 허용한다. 단순 `(int)` 캐스팅은 음수를 0 쪽으로 절단해 격자 밖에서 셀이 밀린다.

### IG-005 — `MatchConstants` 분리
- 상태: **DONE** (이터레이션 5, 2026-08-04)
- 변경 파일 (4개):
  - `Shared/Simulation/MatchConstants.cs` (신규) — 규칙 수치 25개
  - `NVproject/Assets/Scripts/Game/GameConfig.cs` — 공유 필드 25개를 `MatchConstants` 를 읽는
    프로퍼티로 대체, 남는 필드를 성격별로 재분류
  - `NVproject/Assets/Settings/GameConfig.asset` — Unity 재직렬화로 **40개 → 18개 필드**
  - `tests/Modules.Tests/Simulation/MatchConstantsTests.cs` (신규, 11개)
  - (+ `MatchConstants.cs.meta`)
- 검증 (전부 실행함):

  | 확인 | 수단 | 결과 |
  |---|---|---|
  | 서버 테스트 | `dotnet test` | ✅ **240개 통과**, 실패 0 (229 → 240, +11) |
  | 서버 경고 0 | `dotnet build` | ✅ 오류 0개 |
  | 클라이언트 컴파일 | `Assembly-CSharp` + `-Editor` | ✅ 둘 다 오류 0개 |
  | 에셋 정리 | `grep` | ✅ 공유 키(`matchDuration`·`keysRequired`·`seekerMagazine`·`teleportSharedCooldown`·`freezeDuration`·`escapesToWin`) **0건** |
  | **런타임 실측** | Play 모드에서 `MatchManager` 조회 | ✅ `phase=Playing`, `timeRemaining=478.1` (480 에서 감소 중), `keys=0/10`, `escapes=0/2`, `magazine=3`, `deviceCount=9`, `teleportCd=12` — 전부 프로퍼티 경로. `hitImmunity=0.75`, `xray=0.18` 은 필드 경로 |
  | 회귀 — 해시 | 기동 로그 | ✅ `7996AF3A` / `27A9412D` 그대로 |

- **런타임 실측이 이 태스크의 핵심 증거다.** 프로퍼티는 직렬화되지 않으므로 전환이 잘못되면
  값이 조용히 0 이 되고, 증상은 "매치가 즉시 끝난다" 또는 "탄약이 없다" 다. 오프라인 매치가
  실제로 `Playing` 까지 진행하고 시계가 480 에서 흐르는 것을 확인했다.
- **계획서 표의 일부는 옮기지 않았다.** 계획은 `hitImmunity`·`deviceDestroyHits`·`dropKeysOnDeath`·
  `teleportOnHit` 을 `RealtimeConstants.Match` 로 보내는 것이었는데, `RealtimeConstants` 는 서버
  모듈의 `internal` 이고 **클라이언트가 아직 그 규칙들을 판정하고 있다**(`MatchManager.ReportHit`,
  `MapDevice.OnHit`, `ScatterKeys`). 지금 옮기면 클라이언트가 자기가 쓰는 값을 읽을 수 없게 된다.
  각 값은 해당 판정이 실제로 서버로 가는 태스크에서 함께 옮긴다 — `GameConfig` 의 헤더와 주석에
  그 대응을 적어 두었다:

  | 값 | 함께 옮길 태스크 |
  |---|---|
  | `dropKeysOnDeath`, `teleportOnHit`, `hitImmunity`, `deviceDestroyHits` | IG-014 (전투) |
  | `seekerWinsOnWipe` | IG-007 (승리 조건, OQ-2 차단) |
  | `seekerCanActivateDevices` | IG-013 (장치, OQ-1 차단) |
  | `chainAnchorRange` | IG-016 (체인, OQ-4 차단) |

  그래서 이 태스크는 `RealtimeConstants.Match` 를 **만들지 않았다.** 빈 클래스를 미리 두면
  어디까지 옮겨졌는지 읽어서 알 수 없다.
- 비고:
  - **프로퍼티 이름을 소문자로 유지했다.** `config.matchDuration` 이 호출부에서 필드처럼 읽히므로
    `MatchManager`·HUD·무기·장치의 호출부를 **한 줄도 고치지 않았다.** 이름을 C# 관례대로 바꾸면
    수십 곳을 고쳐야 하고, 그 변경은 이 태스크의 목적(값의 출처를 하나로 모으기)과 무관한 위험이다.
  - `KeyPickupHeight`(1.6m)와 탈출 층 허용치(2m)를 `MatchConstants` 에 넣지 않았다. 지금 클라이언트에
    하드코딩되어 있어(`KeyPickup.Update`, `MatchManager.TickEscapes`) 값만 옮겨 적으면 **같은 수가
    두 곳에 있는 상태**가 된다. 그 판정이 서버로 가는 IG-012 에서 함께 올린다.
  - 에셋 정리를 YAML 손편집이 아니라 `EditorUtility.SetDirty` + `AssetDatabase.SaveAssets` 로 했다.
    Unity 가 직렬화 가능한 필드만 남기므로 옛 줄이 정확히 사라지고, 손으로 지울 때의 실수가 없다.
  - Play 모드가 `ProjectSettings/EditorBuildSettings.asset` 을 줄바꿈만 바꿔 저장했다(내용 diff 없음).
    태스크와 무관하므로 되돌렸다.

  | 값 | 어디로 | 왜 |
  |---|---|---|
  | `matchDuration`, `roleRevealDuration`, `keysRequired`, `keysPlaced`, `carryLimit`, `escapesToWin`, `runnerHitsToDie`, `seekerMagazine`, `doorUseRadius`, `escapeHoldTime`, `keyPickupRadius`, `keyInsertInterval`, `deviceCount`, `deviceUseRadius`, 체인 4개, 장치 쿨다운·지속시간 6개 (총 25개) | `Shared/Simulation/MatchConstants.cs` ✅ | 클라이언트가 HUD·프롬프트·쿨다운을 예측해야 한다. 디컴파일되어도 무해 |
  | `hitImmunity`, `deviceDestroyHits`, `dropKeysOnDeath`, `teleportOnHit` | `RealtimeConstants.Match` (아직 아님 → IG-014) | 판정이지 표시가 아니다. 다만 클라이언트가 지금도 그 판정을 하고 있어 옮길 수 없다 |
  | `bloodSpacing`, `bloodLifetime`, `bleed*` 3개, `xrayWallAlpha`, `showDoorCompass`, `localRole`, `practiceRunners`, `practiceRunnerSpeed`, `placementSeed` | `GameConfig.asset` 유지 ✅ | 순수 표현 또는 오프라인 연습 전용 |
  | `seekerWinsOnWipe`(OQ-2), `seekerCanActivateDevices`(OQ-1), `chainAnchorRange`(OQ-4) | `GameConfig.asset` 유지 | 규칙이 미해결이라 옮길 곳이 아직 정해지지 않았다 |

### IG-006 — 서버 매치 단계·시계
- 상태: **DONE** (이터레이션 6, 2026-08-04)
- 기획서 근거: §3, §8 (R-1.3, R-1.4, R-1.6)
- 변경 파일 (7개 + meta):
  - `Shared/Contracts/Enums/MatchPhase.cs` (신규) — Lobby/RoleReveal/Playing/Ended
  - `Modules/Realtime/Simulation/Match.cs` (신규) — 단계·시계·이동 잠금
  - `Modules/Realtime/Simulation/Room.cs` — `Match` 소유, 잠금 적용, 시간 종료로 `Ended`
  - `tests/Modules.Tests/Realtime/MatchTests.cs` (신규, 13개)
  - `tests/Modules.Tests/Realtime/RoomTests.cs` — 통합 검증 7개 + 기존 1개 수정
  - `tests/Modules.Tests/Realtime/RoomFixture.cs` — `skipReveal` 옵션과 `SkipReveal`
  - `NVproject/…/Net/Session/MatchSync.cs` — 이름 충돌 해소 (2줄)
- 검증 (전부 실행함):

  | 확인 | 수단 | 결과 |
  |---|---|---|
  | 서버 테스트 | `dotnet test` | ✅ **260개 통과**, 실패 0 (240 → 260, +20) |
  | 서버 경고 0 | `dotnet build` | ✅ 오류 0개 |
  | 클라이언트 컴파일 | `Assembly-CSharp` + `-Editor` | ✅ 둘 다 오류 0개 |
  | Unity `Shared` | Refresh + `.meta` | ✅ `MatchPhase.cs.meta` |
  | **리빌 잠금** | `역할_공개_중에는_전진_입력이_위치를_바꾸지_않는다` | ✅ 20틱 전진 입력에도 Z=0 |
  | **입력 누적 없음** | `역할_공개_중_입력이_쌓여_나중에_순간이동하지_않는다` | ✅ 리빌 내내 전진을 보낸 뒤 잠금 해제 첫 틱에 Z < 32 (한 틱 이동량 ≈ 14) |
  | 시선은 자유 | `역할_공개_중에도_시선은_돌아간다` | ✅ yaw 16384 반영 |
  | 잠금 해제 | `역할_공개가_끝나면_전진_입력이_먹는다` | ✅ Z > 0 |
  | **서버가 매치를 끝낸다** | `서버_시계가_0_이_되면_룸이_결과_단계로_간다` | ✅ 14400틱 후 `RoomPhase.Ended` + `MatchPhase.Ended`, 결과 코드는 0(미정) |
  | 시계 환산 | `시계가_고정_틱으로_환산된다` | ✅ 480초 = 14400틱, 4초 = 120틱 (나머지 없음) |
  | 회귀 — 해시 | 기동 로그 | ✅ `7996AF3A` / `27A9412D` 그대로 |

- **기존 테스트 3개가 이 변경으로 깨졌고, 그것이 정상이다.** 리빌은 기획서 §3 의 규칙이므로
  "시작 직후 바로 움직인다" 는 옛 가정이 틀린 것이 됐다.
  - `전진_입력은_서버_판정으로_위치를_옮긴다`, `벽을_넘어가지_못한다` → `RoomFixture.FillAndStart`
    가 기본으로 리빌을 통과하게 해 해결. 리빌 자체를 검사할 때만 `skipReveal: false`.
  - `시작하면_스폰_위치에서_출발한다` 의 `Assert.Equal(2u, header.Tick)` → `room.Tick` 과 비교로
    바꿨다. 절대 틱은 "시작 1틱 + 실행 1틱" 이라는 구현 세부에 묶인 값이었고, 리빌 길이 같은
    진행 파라미터가 바뀔 때마다 깨진다.
- 비고:
  - **잠금은 두 갈래 모두에 걸어야 한다.** `StepPlayer` 는 입력이 있을 때와 없을 때(마지막 입력
    반복)를 따로 처리하는데, 반복 갈래를 빼면 잠금이 걸린 첫 틱에 새 입력이 없는 플레이어가
    직전 프레임의 이동을 그대로 반복해 **리빌 중에 혼자 계속 달린다.**
  - **입력을 버리지 않고 소비한다.** 버리면 큐에 쌓이고 리빌이 끝나는 순간 한 틱에 적용되어
    순간이동한다. 테스트가 그 경로를 직접 확인한다.
  - **매치 시계는 이동을 처리한 뒤에 올린다.** 먼저 올리면 시간이 0 이 된 틱의 입력이 버려지고,
    그 한 틱이 마지막 탈출을 판정하는 틱일 수 있다.
  - `MatchPhase` 가 클라이언트의 `NV.Game.MatchPhase` 와 **이름이 겹친다.** `MatchSync` 두 줄을
    `NV.Game.MatchPhase` 로 수식해 해소했다. 값은 같으며 통합은 IG-010 에서 한다 — 그때
    클라이언트가 서버 전문을 받으므로 자기 열거형을 버릴 수 있다.
  - 결과 코드(`_outcome`)는 서버가 채우지 않는다. 시간 종료가 술래 승리인 것은 기획서 §8 에
    있지만 OQ-2·OQ-6 이 남아 있어 승패 판정 전체를 IG-007 로 미뤘다. 지금은 단계만 옮긴다.
  - `MatchRules.cs` 를 만들지 않았다. 이 태스크에 판정이 없다 — 단계 전이 조건은 `Match` 안에
    있는 것이 맞고, 빈 파일을 미리 두면 어디까지 왔는지 읽어서 알 수 없다(D-8 과 같은 이유).
- 계획: `Modules/Realtime/Simulation/Match.cs`(상태·전이), `MatchRules.cs`(판정).
  `RoomPhase` 는 건드리지 않는다 — 룸 생애(Waiting/Playing/Ended)와 매치 진행
  (RoleReveal/Playing/Ended)은 다른 축이고 `Room.Advance` 가 이미 `Playing` 에서만 시뮬레이션한다.
  매치 단계는 `RoomPhase.Playing` 안의 상태로 둔다. **리빌 중 정지는 단계 전이가 아니라 입력
  무력화**로 구현한다 — `InputValidator.Neutral` 과 같은 방식으로 이동 성분만 0 으로 만들고
  시선은 남긴다(`MatchManager.ApplyMovementLocks` 의 의도).
- 변경 예정 파일: `Modules/Realtime/Simulation/Match.cs`, `MatchRules.cs`, `Room.cs`,
  `tests/Modules.Tests/Realtime/MatchTests.cs`
- 검증: `dotnet test` — 리빌이 끝나면 Playing 으로 간다 / 시계가 고정 틱으로 감소한다
- 비고: **승리 조건은 IG-007 로 분리**했다. OQ-2·OQ-6 에 걸려 있어 이 태스크를 막지 않게 한다.

### IG-007 — 승리 조건 판정
- 상태: **BLOCKED** (OQ-2, OQ-6)
- 기획서 근거: §3, §8
- 차단 사유: 기획서 §8 은 술래 승리를 "2명 미만 탈출 / 시간 종료" 로만 정하고 **Runner 전멸
  승리를 언급하지 않는다.** 구현에는 `MatchOutcome.SeekerWipedRunners` 와
  `GameConfig.seekerWinsOnWipe: 1` 이 있다(`MatchManager.cs:339-347`). 또한 `escapesToWin` 이 2
  인데 서버의 `MinPlayersToStart` 는 2 라, **2인 매치(Seeker 1 + Runner 1)에서는 Runner 승리가
  구조적으로 불가능**하다. 둘 다 승패 규칙이므로 LOOP §6.4 에 따라 추측하지 않는다.
- 비고: IG-006(단계·시계)은 이것 없이 진행 가능하다. 시간 종료 시 단계만 `Ended` 로 보내고
  결과 코드는 이 태스크에서 채운다.

### IG-008 — `MatchState` 전문 + 역할별 필터 + 프로토콜 3
- 상태: **DONE** (이터레이션 7, 2026-08-04)
- 변경 파일 (11개 + meta 2) — **§6.1 의 8개를 넘겼다.** 프로토콜 추가는 계약·코덱·버전·
  송신·수신·테스트를 한꺼번에 건드리므로 본질적으로 크다. 다음에 유사한 태스크는
  (a) 와이어 계약 + 코덱 + 코덱 테스트, (b) 송신·수신 + 통합 테스트로 미리 쪼갠다.
  - `Shared/Contracts/Enums/MatchRole.cs` (신규)
  - `Shared/Contracts/Enums/EventKind.cs` — `MatchState = 2`
  - `Shared/Contracts/Messages/MatchStateMessage.cs` (신규) — 헤더 9B + 참가자 5B
  - `Shared/Serialization/MessageCodec.cs` — `WriteMatchState`(역할 필터 포함)·
    `ReadMatchState`·`ReadEventKind`·`MatchStateMaxWireSize`
  - `Shared/Contracts/Messages/ProtocolInfo.cs` — **Version 3**
  - `Modules/Realtime/RealtimeConstants.cs` — `MatchStateIntervalTicks`(RoomState 에서 유도)
  - `Modules/Realtime/Simulation/Room.cs` — 세션별 인코딩 송신, 단계 전이 시 즉시 전송
  - `NVproject/…/Net/NetworkClient.cs` — `DispatchEvent` 로 종류별 분기
  - `tests/…/Serialization/MatchStateCodecTests.cs` (신규, 17개)
  - `tests/…/Realtime/RoomFixture.cs` — `TryLastMatchState`·`TryLastEvent`·`CountOfEvent`
  - `tests/…/Realtime/RoomTests.cs` — 통합 검증 7개
- 검증 (전부 실행함):

  | 확인 | 수단 | 결과 |
  |---|---|---|
  | 서버 테스트 | `dotnet test` | ✅ **285개 통과**, 실패 0 (260 → 285, +25) |
  | 서버 경고 0 | `dotnet build` | ✅ 오류 0개 |
  | 클라이언트 컴파일 | `Assembly-CSharp` | ✅ 오류 0개 |
  | Unity `Shared` | Refresh + `.meta` | ✅ `MatchRole.cs.meta`, `MatchStateMessage.cs.meta` |
  | **역할 필터 (바이트)** | `두_사본의_바이트가_열쇠_자리에서만_다르다` | ✅ Runner 사본 `keysInserted=7`, Seeker 사본 `0`. 그 두 자리 외 모든 바이트 동일 |
  | 필터 범위 | `Seeker_도_탈출_수는_받는다` | ✅ 탈출 수는 걸러지지 않는다 (막아야 하는 수다) |
  | **세션별 인코딩** | `Seeker_세션과_Runner_세션이_서로_다른_바이트를_받는다` | ✅ 두 세션에 각각 1프레임 (한 번 인코딩해 전원 전송이 아님) |
  | 역할 배정 | `전문에_Seeker_와_Runner_역할이_실린다` | ✅ Seeker 정확히 1명, 나머지 전부 Runner |
  | 즉시 전송 | `단계가_바뀌면_간격을_기다리지_않고_보낸다` | ✅ 리빌 종료 틱에 `Playing` 전문 |
  | 주기 전송 | `전문은_주기적으로_다시_나간다` | ✅ 30틱에 2회 이상 |
  | 로비에서 침묵 | `대기_단계에서는_매치_전문을_보내지_않는다` / `로비로_되돌리면_매치_전문이_멈춘다` | ✅ 0회 |
  | **프로토콜 게이트** | `curl "…/rooms/test?v=2"` vs `?v=3` | ✅ **HTTP 426** vs **HTTP 200** — 버전 3 이 실제로 구버전을 거절한다 |
  | 회귀 — 해시 | 기동 로그 + 조회 응답 | ✅ `7996AF3A` / `27A9412D` (응답의 `mapHash 665403693` = `0x27A9412D`) |

- **`ControlKind.EndMatch` 를 제거하지 않았다.** 계획서는 이 태스크에서 없애라고 하지만,
  서버만 먼저 제거하면 클라이언트가 보내는 프레임이 무시되고 **그 상태에서는 매치가 끝나지
  않는다** — 클라이언트가 아직 승리를 판정하고 그 경로로 보고하기 때문이다. 제거는 클라이언트가
  뷰가 되는 IG-010 과 같은 커밋이어야 한다. 지금은 전문을 **추가**만 했고, 서버 시계로 끝나는
  경로와 방장 보고로 끝나는 경로가 공존한다(둘 다 `Ended` 로 가므로 먼저 오는 쪽이 이긴다).
- **클라이언트가 깨지지 않게 최소 개입을 했다.** `NetworkClient.Dispatch` 는 `Event` opcode 를
  받으면 **무조건 룸 상태로 파싱**했고, `ReadRoomState` 는 종류 불일치를 예외로 던진다. 새
  전문을 보내기 시작하면 2Hz 로 `LastError` 가 덮여 화면에 네트워크 오류가 뜬다. `DispatchEvent`
  로 종류를 먼저 보게 하고 `MatchState` 는 지금은 버린다(적용은 IG-010).
  **테스트 헬퍼도 똑같은 함정에 빠져 3개가 깨졌다** — `TryLastRoomState` 가 opcode 만 걸러
  마지막 `Event` 를 집었다. `EventKind` 까지 거르도록 고쳤다.
- 비고:
  - **필터를 인코딩 지점에 두었다.** `MessageCodec.WriteMatchState(…, MatchRole forRole)` 가
    받는 역할로 열쇠 자리를 0 으로 만든다. 호출부에서 필터링하면 필터를 잊는 경로가 생기고,
    그 경로로 정보가 샌다. 클라이언트에서 숨기는 방식은 디컴파일로 되살아난다.
  - **게이트 깃발을 둘로 나눴다.** `_stateDirty` 를 공유하면 룸 상태를 보내는 쪽이 깃발을
    내려버려서 같은 틱에 매치 전문이 즉시 전송을 건너뛴다.
  - 아직 서버가 세지 않는 값(삽입 열쇠·탈출·피격·상태 플래그)은 **자리를 잡아 두고 0 으로**
    보낸다. 그래야 IG-012·IG-014 가 값을 채울 때 와이어 포맷이 바뀌지 않고, 프로토콜 버전을
    한 번만 올릴 수 있다.
  - 상태 플래그의 열거형은 만들지 않았다. 채울 값이 생기는 것은 탈락·탈출이 서버 판정이 되는
    IG-012 이고, 빈 열거형을 미리 두면 어디까지 왔는지 읽어서 알 수 없다.
  - `MatchRole` 이라는 이름을 쓴 이유는 충돌을 미리 피하기 위해서다. 클라이언트에 `NV.Game.Role`
    이 있고, IG-006 에서 `MatchPhase` 가 겹쳐 호출부를 수식해야 했다.
- 계획: `EventKind.MatchState=2`. `RoomState` 와 같은 성격 — **전문**, 2Hz + 변경 즉시, 멱등.
  고정부: 단계 u8, 남은시간 u16(0.1초 단위 = 6553초까지), 삽입열쇠 u8, 탈출수 u8, 결과 u8,
  인원 u8. 참가자당: playerId u8, 역할 u8, 상태플래그 u8, 피격수 u8, 소지열쇠 u8.
  **세션별 인코딩** — 술래 사본에서는 삽입열쇠와 남의 소지열쇠를 0 으로 채운다(룰셋은 술래에게
  열쇠 진행도를 알리지 않는다). `ControlKind.EndMatch=3` 을 제거하고 값을 비워 둔다.
  `ProtocolInfo.Version` → 3.
- 변경 예정 파일: `Shared/Contracts/Enums/EventKind.cs`, `ControlKind.cs`,
  `Shared/Contracts/Messages/MatchStateMessage.cs`, `Shared/Serialization/MessageCodec.cs`,
  `Shared/Contracts/Messages/ProtocolInfo.cs`, `Modules/Realtime/Simulation/Room.cs`,
  `tests/Modules.Tests/Serialization/CodecRoundTripTests.cs`
- 검증: `dotnet test` — 라운드트립 + **술래 사본에 열쇠 진행도가 실리지 않는다(인코딩 결과
  바이트를 직접 본다)**
- 비고: 여기서 프로토콜이 3 이 된다. IG-010 과 같은 배포 단위로 묶는다.

### IG-009 — `EntityFlags` 확장
- 상태: **DONE** (이터레이션 8, 2026-08-04)
- **`Seeker` 와 `Frozen` 은 지금 실제 값이 있다.** 서버가 Seeker id 와 잠금 여부를 이미
  알고 있어, 이 태스크는 "자리만 잡는" 커밋이 아니다. `Bleeding`·`Escaped` 는 그 판정이
  오는 IG-014·IG-012 에서 채워진다.
- 변경 파일 (5개):
  - `Shared/Contracts/Enums/EntityFlags.cs` — 4비트 추가 + 두 종류의 소유자 구분을 주석으로
  - `Shared/Simulation/StateProjection.cs` — `ToEntityState` 3인자 오버로드,
    `SimulationFlagsOf`/`MatchFlagsOf`
  - `Modules/Realtime/Simulation/PlayerEntity.cs` — `MatchFlags`
  - `Modules/Realtime/Simulation/Room.cs` — `MatchFlagsFor` (매 틱 유도)
  - `tests/…/Simulation/EntityFlagsTests.cs` (신규, 9개)
  - `tests/…/Realtime/RoomTests.cs` — 통합 검증 6개
- 검증 (전부 실행함):

  | 확인 | 수단 | 결과 |
  |---|---|---|
  | 서버 테스트 | `dotnet test` | ✅ **301개 통과**, 실패 0 (285 → 301, +16) |
  | 서버 경고 0 | `dotnet build` | ✅ 오류 0개 |
  | 클라이언트 컴파일 | `Assembly-CSharp` | ✅ 오류 0개 |
  | 크기 불변 | `플래그를_늘려도_엔티티_크기가_그대로다` | ✅ `EntityState.WireSize` 13B |
  | 비트 분리 | `이동_비트와_매치_비트가_겹치지_않는다` | ✅ 교집합 없음 |
  | 비트 값 고정 | `비트_값이_고정되어_있다` | ✅ 1/2/4/8/16/32/64 |
  | **덮어쓰지 않음** | `매치_비트가_이동_비트를_덮지_않는다` (Room) | ✅ 20틱 후에도 `Alive`·`OnGround` 유지 |
  | **Seeker 정확히 1명** | `스냅샷에_Seeker_비트가_정확히_한_명에게_실린다` | ✅ |
  | **Frozen 전원** | `역할_공개_중에는_모든_몸에_Frozen_비트가_실린다` | ✅ 리빌 중 전원, 리빌 후 0명 |
  | 미판정 비트 침묵 | `아직_판정하지_않는_비트는_실리지_않는다` | ✅ `Bleeding`·`Escaped` 0 |
  | 재시작 누출 없음 | `로비로_되돌리면_Seeker_비트도_사라진다` | ✅ 두 번째 매치에서도 Seeker 1명 |
  | 해시 영향 없음 | `매치_비트는_시뮬레이션_상태의_해시를_바꾸지_않는다` | ✅ |
  | 회귀 — 맵 해시 | 기동 로그 | ✅ `7996AF3A` / `27A9412D` |

- **매치 비트를 `PlayerState` 에 담지 않았다.** 기술적으로는 가능하다 —
  `PlayerMovement.ResolveCrouch` 가 `state.Flags` 에서 시작해 다른 비트를 보존하는 것을
  확인했다. 그럼에도 나눈 이유는 `PlayerState` 가 `Shared` 의 **결정적 시뮬레이션 상태**이고
  `StateHash` 에 들어가기 때문이다. 클라이언트는 출혈·역할·잠금을 예측할 수 없으므로, 그
  비트가 섞이면 리컨실리에이션의 해시 비교가 **영구히 어긋난다.** 서버는
  `PlayerEntity.MatchFlags` 에 따로 들고 있다가 인코딩 순간에만 합친다.
- 비고:
  - `MatchFlagsFor` 는 **매 틱 유도한다.** 상태로 들고 있다가 갱신을 잊는 것보다, 근거가
    되는 값(Seeker id, 잠금 여부)에서 매번 계산하는 편이 어긋날 자리가 없다. 로비 복귀 시
    `_seekerPlayerId` 가 `NoPlayer` 로 돌아가므로 비트도 자동으로 사라진다 — 테스트가 그것을
    두 번째 매치로 확인한다.
  - `SimulationFlagsOf` 를 함께 넣었다. 예측 상태와 서버 스냅샷을 비교할 때 매치 비트를 빼야
    한다는 규칙을 코드로 적어 둔 것이다. **"IG-010 이 쓴다" 고 적었는데 그것은 틀렸다** —
    이터레이션 10 에서 클라이언트 예측이 아예 구현되어 있지 않은 것을 확인했다(IG-022 참고).
    쓰이는 시점은 예측이 도입될 때(IG-023)다.
  - `Frozen` 이 클라이언트에 필요한 이유: 서버는 잠금을 입력 무력화로 구현하므로, 클라이언트가
    이 비트를 모르면 자기 입력으로 계속 예측하고 매 틱 되돌려진다 — 증상은 잠긴 동안 화면이
    떨리는 것이다.
  - 8비트가 다 찼다(`1<<7` 하나 남음). 더 필요하면 `EntityState` 크기를 늘리는 대신 2Hz
    전문(`MatchState.Flags`)으로 보내야 한다 — 매 틱 필요한 것만 여기 온다는 기준이 그래서 있다.
- 계획: `Bleeding=1<<3`, `Seeker=1<<4`, `Escaped=1<<5`, `Frozen=1<<6`. 지금 3비트만 쓰고 5비트가
  남는다. `EntityState` 크기 13B 그대로이고 스냅샷 대역폭도 그대로다. 출혈·역할은 원격 몸의
  표현(피 흔적, 무기 유무)에 **매 틱** 필요하므로 2Hz 전문이 아니라 스냅샷에 있어야 한다.
- 변경 예정 파일: `Shared/Contracts/Enums/EntityFlags.cs`,
  `Shared/Simulation/StateProjection.cs`, `Modules/Realtime/Simulation/PlayerEntity.cs`,
  `Net/RemotePlayerPuppet.cs`, `tests/Modules.Tests/Serialization/WireSizeTests.cs`
- 검증: `dotnet test` — `EntityState.WireSize` 가 13 그대로

### IG-010 — 클라이언트 전문 수신·적용 (뷰 전환 1/2)
- 상태: **DONE** (이터레이션 9, 2026-08-04)
- **이 태스크에서 실제 결함 하나를 찾았다: `MatchSync` 가 어느 씬에도 없었다.**
  `Assets/Editor/MatchSetup.cs:44` 의 에디터 메뉴(**Tools ▸ Backrooms ▸ Set Up Match**)만
  그 컴포넌트를 붙이고, `SampleScene`·`MultiplayerTest` 어느 쪽도 갖고 있지 않다
  (`grep -l MatchSync Assets/Scenes/*.unity` → 0건, Play 모드에서 `matchSyncInScene=False` 실측).
  즉 로비를 통해 매치를 열면 **서버의 시작 신호·Seeker·배치 씨드·종료 중계가 존재하지 않는
  컴포넌트를 통해 전달되고 있었다.** `MatchBootstrap.Awake` 가 런타임에 만들도록 고쳤다 —
  이 프로젝트의 규칙("씬은 거의 아무것도 들고 있지 않다")과 같고, 메뉴 실행 여부에 의존하지
  않는다.
- 변경 파일 (4개):
  - `Net/NetworkClient.cs` — `MatchState`·`HasMatchState`·`ParticipantCount`·
    `MatchParticipantAt`, `ReadMatchState`, 연결 해제 시 초기화
  - `Game/MatchManager.cs` — `AcceptMatchState(serverPhase, secondsRemaining)`
  - `Net/Session/MatchSync.cs` — `Update` 에서 `ApplyMatchState` 폴링
  - `Game/MatchBootstrap.cs` — `EnsureMatchSync` (런타임 생성, 중복 방지)
- 검증 (전부 실행함):

  | 확인 | 수단 | 결과 |
  |---|---|---|
  | 클라이언트 컴파일 | `dotnet build Assembly-CSharp.csproj` | ✅ 오류 0개 |
  | 서버 회귀 | `dotnet test` | ✅ 301개 통과 (서버 미변경) |
  | **`MatchSync` 존재** | Play 모드 조회 | ✅ `matchSyncCount=1` (수정 전 0개) |
  | 중복 방지 | 같은 조회 | ✅ 1개 — 메뉴로 붙인 씬에서도 두 개가 되지 않는다 |
  | **오프라인 회귀** | Play 모드 조회 | ✅ `sessionExists=False` → `autoStart=True` 유지, `phase=Playing`, `timeRemaining=476.0` 감소 중 |
  | 적용 경로 침묵 | 같은 조회 | ✅ 세션이 없으면 `HasMatchState` 가 false 라 `AcceptMatchState` 에 도달하지 않는다 |

- **§7.4 스모크 테스트는 하지 못했다.** 서버는 매치가 시작돼야 전문을 보내고, 시작에는
  최소 2명이 필요하다(`MinPlayersToStart = 2`). MCP 로는 두 번째 클라이언트를 만들 수 없고
  입력도 주입할 수 없다(`NVproject/CLAUDE.md`). **두 클라이언트를 띄워 로비에서 방을 만들고
  시작하는 절차는 사람의 조작이 필요하다.** 그때 확인할 것:
  1. 두 화면의 단계 전이 시점과 남은 시간이 일치하는가
  2. 맵 해시 `일치` 로그 (IG-001 이 미룬 실측)
  3. 리빌이 서버 틱에 끝나는가 (프레임레이트가 다른 두 기기에서)
- 범위에서 **일부러 뺀 것들** — 각각 지금 적용하면 퇴화한다:

  | 뺀 것 | 이유 |
  |---|---|
  | `keysInserted`, `escapes` | 서버가 아직 세지 않아 **0 을 보낸다.** 적용하면 클라이언트가 올바르게 센 값(예: 7)을 0 으로 덮어써 HUD 가 되돌아간다. 증상은 "목표가 스스로 초기화된다" 로 보인다 → IG-012 |
  | `outcome` | 같은 이유 + 결과 코드는 IG-007(OQ-2·OQ-6) 이 정한다 |
  | `MatchPhase.Ended` 적용 | 매치를 끝내는 것은 **결과를 발표하는 일**이고 그 경로는 이미 있다(`AcceptOutcome` ← 룸 전문). 단계만 옮기면 결과 없는 결과 화면이 뜬다 |
  | 역할(`MatchRole`) 적용 | 이미 `RoomState.SeekerPlayerId` 경로로 동작한다. 두 경로가 갈리면 원인을 찾기 어렵다 |
  | `Frozen` 예측 반영 + `SimulationFlagsOf` 필터 | 리컨실리에이션을 건드리는 일이라 독립 검증이 필요하다 → **IG-022 로 분리** |
- 비고:
  - **`MatchStateChanged` 이벤트를 두지 않았다.** 이 전문은 시계를 싣고 있어 2Hz 마다 반드시
    달라지므로 "바뀌었다" 는 신호에 정보가 없다. `RoomState` 는 변경이 드물어 이벤트가 값을
    하지만(로비 UI 를 초당 두 번 다시 짓지 않기 위해) 여기서는 폴링이 맞다.
  - `ReadMatchState` 가 실패해도 들고 있던 값을 버리지 않는다. 프레임 하나가 손상되었을 때
    시계를 0 으로 되돌리면 HUD 가 "시간 종료" 를 그린다 — 다음 전문이 0.5초 안에 온다.
  - `TimeRemaining` 의 로컬 감소는 남겼다. 전문이 2Hz 라 그 사이를 메워야 HUD 시계가 튀지
    않는다. 전문이 올 때마다 서버 값으로 덮는다.
  - `MatchBootstrap` 이 `NV.Client.Net.Session` 을 참조하게 됐다. 계층상 역방향이지만
    `MatchSync` 가 이미 `NV.Game` 을 참조하고 같은 어셈블리다. `MatchManager` 자체는 여전히
    네트워크를 모르며, `MatchSync` 만 양쪽을 안다.
- **원래 하나였던 태스크를 둘로 쪼갰다** (이터레이션 8 판단). 이유가 둘이다:
  1. **판정 제거가 지금은 퇴화다.** IG-007(승리 조건)이 OQ-2·OQ-6 으로 BLOCKED 이므로,
     클라이언트의 `EvaluateWinConditions` 를 없애면 **탈출·전멸 승리를 아무도 판정하지 않고
     매치가 8분 시간 종료로만 끝난다.** §6.3 이 금지하는 종류의 변경이다.
  2. **§6.1 준수.** IG-008 이 11개 파일로 8개를 넘겼으므로 이번에는 미리 쪼갠다.
- 범위: 서버 전문을 **받아서 적용**한다. 기존 판정은 **그대로 둔다** — 서버 값이 덮으므로
  실질적으로 서버가 이기고, 이중 판정이 남아도 화면은 서버를 따른다.
  - `NetworkClient` 가 `MatchState` 를 파싱해 보관(`DispatchEvent` 의 자리가 이미 있다)
  - `MatchSync` 가 그것을 `MatchManager` 에 넘긴다
  - `MatchManager.AcceptMatchState` 가 단계·시계를 서버 값으로 덮고 **기존 이벤트를 발화**한다
    (`PhaseChanged`·`KeysChanged`·`EscapesChanged`) — HUD·`PlayerRoleLoadout`·
    `GameHudController` 는 그 이벤트를 구독하므로 **손대지 않는다**
  - `_phaseTimer`/`TimeRemaining` 의 로컬 감소는 **남긴다.** 전문이 2Hz 라 그 사이를 메워야
    HUD 시계가 튀지 않는다
  - `EntityFlags.Frozen` 을 로컬 예측에 반영하고, 예측 비교에서 `SimulationFlagsOf` 로 매치
    비트를 걸러낸다(IG-009 가 준비해 둔 것)
- 검증: `dotnet build Assembly-CSharp.csproj` + **동기화 스모크 테스트** — 로컬 서버 + 2
  클라이언트로 두 화면의 단계·시계 일치를 관측한다. IG-001 이 미룬 **맵 해시 `일치` 실측도
  여기서 함께** 한다.
- 비고: 역할 배정은 이미 `RoomState.SeekerPlayerId` 경로로 동작한다. 전문의 역할을 **중복
  적용하지 않는다** — 두 경로가 갈리면 원인을 찾기 어렵다.

### IG-022 — 클라이언트 예측에 `Frozen` 반영 + 플래그 필터
- 상태: **DEFERRED** (이터레이션 10, 2026-08-04) — **전제가 틀렸다. 고칠 문제가 없다.**
- 이 태스크는 "클라이언트가 자기 입력으로 예측하다가 서버 보정에 매 틱 되돌려져 리빌 4초 동안
  화면이 떨린다" 를 전제로 만들었다. 조사 결과 **클라이언트 예측이 구현되어 있지 않다.**

  | 확인 | 근거 |
  |---|---|
  | 클라이언트가 이동을 계산하지 않는다 | `grep -rn "PlayerMovement\."` → 클라이언트 코드에 **0건** |
  | 서버 권위 구간에서 로컬 이동이 꺼진다 | `ApplyAuthority` 가 `ControlMode.NetworkAuthority` 로 전환(`NetworkBootstrap.cs:216`), `FirstPersonController.HandleMove` 가 그 모드면 **즉시 return**(`:366`) |
  | 위치는 서버 스냅샷으로 온다 | `ApplyLocal` 이 `Vector3.SmoothDamp` 로 스냅샷 위치를 따라간다(`:256-280`) |

- 그래서 잠금의 실제 동작은 이미 옳다 — 서버가 이동을 무력화하면 스냅샷 위치가 멈추고
  클라이언트는 그 위치로 감쇠하므로 **떨리지 않고 그냥 멈춘다.** `EntityFlags.Frozen` 을
  예측에 반영할 대상이 없다.
- `StateProjection.SimulationFlagsOf` 도 지금 쓸 곳이 없다. **IG-009 의 "IG-010 이 쓴다" 는
  기록이 틀렸다** — 예측 비교가 존재하지 않으므로, 그 함수는 예측이 도입될 때(IG-023) 쓰인다.
  남겨 두는 이유는 그때 필요하고, 무엇을 걸러야 하는지를 코드로 적어 둔 값이 있기 때문이다.
- `Frozen` 비트 자체는 낭비가 아니다. 표현에 쓸 수 있다 — HUD 가 "정지" 를 표시하거나 원격 몸의
  걸음 애니메이션을 멈추는 데 필요하다. 그 용도는 IG-019(피드백) 범위다.
- 검증: **코드를 바꾸지 않았다** (`git diff --stat HEAD -- '*.cs'` 가 빈 출력). 회귀가 불가능하지만
  기준선을 확인했다 — `dotnet test` 301개 통과, `dotnet build Assembly-CSharp.csproj` 오류 0개.

### IG-023 — 클라이언트 이동 예측 + 리컨실리에이션
- 상태: TODO (P4)
- **기획서 요구가 아니다.** §8 은 "클라이언트 예측은 로컬 이동/연출에만 허용" 이라고 쓰는데,
  그것은 허용이지 요구가 아니다. 기획서에도 예측 요구가 없다. 그래서 우선순위를 낮게 둔다.
- 다만 **품질 영향이 크고 지금은 보이지 않는다.** 로컬 서버는 왕복 지연이 0에 가까워
  `SmoothDamp` 가 즉시 따라붙지만, 실제 배포에서는 입력과 화면 사이에 왕복 지연이 그대로
  드러난다 — 증상은 "캐릭터가 늦게 움직인다" 다.
- 이 갭이 아키텍처 문서와 어긋나는 지점이기도 하다. `NVserver/Shared` 가 존재하는 이유로
  루트 `CLAUDE.md` 는 "클라이언트 예측이 비트 동일하게 계산되어야 한다" 를 든다. 예측이
  미구현인 현재, `Shared` 의 실제 용도는 (a) 서버 시뮬레이션, (b) 맵 해시 대조, (c) 상수·와이어
  계약 공유다. 문서를 고칠지 예측을 구현할지는 이 태스크가 결정한다 → IG-019 와 함께 판단.

### IG-021 — 클라이언트 판정 경로 제거 (뷰 전환 2/2)
- 상태: **BLOCKED** (IG-007 → OQ-2·OQ-6)
- 범위: `MatchManager.EvaluateWinConditions`·`ResolvesOutcome`,
  `MatchSync.OnLocalMatchEnded`, `NetSession.ReportMatchEnd`, `ControlKind.EndMatch`(enum 값 3 을
  비워 두고 주석), 클라이언트의 `NV.Game.MatchPhase`·`Role` → `Shared` 의
  `MatchPhase`·`MatchRole` 로 대체.
- 차단 사유: **서버가 결과를 정하기 전에는 제거할 수 없다.** 지금 승패를 판정하는 것은
  클라이언트뿐이고(방장), 그것을 없애면 매치가 시간 종료로만 끝난다. IG-007 이 서버에 승리
  조건을 넣은 뒤에 이 제거가 안전해진다.
  **서버·클라이언트 어느 한쪽만 `ControlKind.EndMatch` 를 제거해도 매치가 끝나지 않는
  구간이 생기므로, 그 제거는 반드시 한 커밋이어야 한다.**

### IG-011a — 서버 목표물 배치
- 상태: **DONE** (이터레이션 11, 2026-08-04)
- 기획서 근거: §3(열쇠 10개), §4.3(체인 제단), §5(장치 8~9개), §6(문 랜덤 위치)
- 변경 파일 (6개 + meta):
  - `Shared/Contracts/Enums/MatchDeviceType.cs` (신규) — 기획서 §5 의 효과 6종
  - `Modules/Realtime/RealtimeConstants.cs` — `Match` 중첩 클래스 (간격·시도 횟수·조합표)
  - `Modules/Realtime/Simulation/Objectives.cs` (신규) — 배치 결과
  - `Modules/Realtime/Simulation/MatchRules.cs` (신규) — 배치 판정
  - `Modules/Realtime/Simulation/Room.cs` — 매치 시작 시 배치, 로비 복귀 시 초기화
  - `tests/…/Realtime/ObjectivePlacementTests.cs` (신규, 16개)
- 검증 (전부 실행함):

  | 확인 | 수단 | 결과 |
  |---|---|---|
  | 서버 테스트 | `dotnet test` | ✅ **317개 통과**, 실패 0 (301 → 317, +16) |
  | 서버 경고 0 | `dotnet build` | ✅ 경고 0개 오류 0개 |
  | 클라이언트 컴파일 | `Assembly-CSharp` | ✅ 오류 0개 |
  | Unity `Shared` | Refresh + `.meta` | ✅ `MatchDeviceType.cs.meta` |
  | **기획서 수치** | `기획서_수치대로_놓인다` | ✅ 열쇠 10개(§3), 장치 8~9개(§5) |
  | **전부 설 수 있는 자리** | `모든_목표물이_설_수_있는_자리에_있다` | ✅ 제단·착지점·문·열쇠 10·장치 9 **전부** `FreeFloor` 이고 플레이어 박스가 들어간다 (실제 `backrooms` 격자) |
  | 효과 전부 등장 | `여섯_효과가_모두_한_번은_놓인다` | ✅ 6종 전부 |
  | 중복은 다회용 | `중복되는_효과는_다회_사용_쪽이다` | ✅ `AddTime`·`FreezeAndXray` 각 1개, `Teleport` 2개 이상 |
  | 재현성 | `같은_씨드는_같은_배치를_낸다` | ✅ 문·yaw·제단·열쇠·장치 전부 일치 |
  | 씨드 반영 | `다른_씨드는_문을_다른_곳에_놓는다` | ✅ |
  | **제단은 고정** | `제단은_씨드가_달라도_같은_자리다` | ✅ 씨드 1 과 999999 가 같은 자리 |
  | 격자 없음 처리 | `격자가_없으면_배치하지_않는다` | ✅ `Placed=false`, 목록 비어 있음 |
  | 재배치 누적 없음 | `다시_배치하면_이전_것이_남지_않는다` | ✅ |
  | 회귀 — 맵 해시 | 기동 로그 | ✅ `7996AF3A` / `27A9412D` |

- **순서가 규칙의 일부다** — 제단 → 문 → 열쇠 → 장치. 클라이언트의
  `MatchManager.PlaceObjectives` 에 있던 순서를 그대로 옮겼다. 제단이 먼저인 이유는 그것이
  유일한 고정물이기 때문이고(격자 중앙 근처, 매치마다 같은 자리), 나머지가 제단을 피해 가야
  한다. 문이 그다음이고 열쇠·장치가 문에서 떨어지는 이유도 같다 — 열쇠가 문간에 생기면
  목표가 우연히 짧아진다.
- **제단은 씨드를 받지 않는다.** 무작위가 아니라 격자 중앙에서 링을 넓혀 가며 찾은 첫
  `FreeFloor` 셀이다. 기획서 §4.3 의 벌칙을 Seeker 가 예측할 수 있어야 하고, 예측할 수 없는
  벌칙은 그저 짜증이다. 착지점은 인접 8방향에서 따로 찾는다 — 제단이 놓인 셀에 몸을 내려놓을
  수는 없다.
- 비고:
  - **아직 와이어에 실리지 않는다.** 서버가 자기 배치를 갖는 것까지이고, 클라이언트는 여전히
    `PlacementSeed` 로 자기 배치를 계산한다. 두 배치가 같은 씨드에서 나오지만 **알고리즘이
    다르므로 좌표가 일치하지 않는다** — 서버는 셀 중심(AS-7), 클라이언트는 셀 안에서 지터.
    이 불일치는 IG-011c 가 클라이언트를 수신 측으로 바꿀 때 사라진다. 그때까지 네트워크
    매치의 목표물은 클라이언트 계산 그대로다(= 지금까지와 같다).
  - **간격 조건은 절대 보장이 아니다.** 64회 시도 후 포기하고 아무 자리나 쓴다. 좁은 맵에서
    조건을 만족하는 자리가 없을 때 목표물이 하나도 안 생기는 것보다 겹쳐서라도 생기는 편이
    낫다 — 열쇠가 0개면 매치가 성립하지 않는다. 그래서 테스트도 "전부 지킨다" 가 아니라
    "대부분 지킨다(쌍의 25% 이하가 위반)" 를 검사한다.
  - `Vector3.DistanceSquared` 를 쓰지 않고 직접 계산했다. `conventions.md` 가 SIMD 경로의
    라운딩 차이를 이유로 `System.Numerics` 의 벡터 연산을 금지한다.
  - 배치 간격(열쇠 4m·장치 5m)과 조합표는 기획서에 없고 클라이언트 구현에서 왔다 → AS-5·AS-9.

### IG-011b — `ObjectiveState` 전문 + 역할별 필터
- 상태: **DONE** (이터레이션 12, 2026-08-04)
- 변경 파일 (9개 + meta) — §6.1 을 하나 넘겼다. IG-008 과 같은 이유로 프로토콜 추가는 계약·
  코덱·상수·송신·수신·테스트를 한꺼번에 건드린다. **이미 a/b/c 로 쪼갠 뒤에도 그렇다** —
  다음에 와이어를 추가할 때는 (계약+코덱)과 (송신+통합 테스트)를 더 나눈다.
  - `Shared/Contracts/Messages/ObjectiveStateMessage.cs` (신규) — 헤더 5B + 가변 블록
  - `Shared/Contracts/Enums/EventKind.cs` — `ObjectiveState = 3`
  - `Shared/Serialization/MessageCodec.cs` — `WriteObjectiveState`(문 필터 포함)·`Read…`·`…MaxWireSize`
  - `Modules/Realtime/RealtimeConstants.cs` — `ObjectiveStateIntervalTicks` (5초)
  - `Modules/Realtime/Simulation/Room.cs` — 세션별 인코딩 송신, 버퍼, 게이트
  - `NVproject/…/Net/NetworkClient.cs` — 종류 분기에 자리 추가 (지금은 버린다)
  - `tests/…/Serialization/ObjectiveStateCodecTests.cs` (신규, 13개)
  - `tests/…/Realtime/RoomFixture.cs` — 격자 있는 픽스처 맵, `TryLastObjectiveState`
  - `tests/…/Realtime/RoomTests.cs` — 통합 검증 6개
- 검증 (전부 실행함):

  | 확인 | 수단 | 결과 |
  |---|---|---|
  | 서버 테스트 | `dotnet test` | ✅ **336개 통과**, 실패 0 (317 → 336, +19) |
  | 서버 경고 0 | `dotnet build` | ✅ 오류 0개 |
  | 클라이언트 컴파일 | `Assembly-CSharp` | ✅ 오류 0개 |
  | Unity `Shared` | Refresh + `.meta` | ✅ `ObjectiveStateMessage.cs.meta` |
  | **문 블록이 없다** | `Seeker_사본에는_문_블록이_없다` | ✅ `HasDoor=false`, 좌표·yaw·개방 전부 0 |
  | **바이트가 짧다** | `Seeker_사본이_문_블록만큼_짧다` | ✅ 정확히 9B 짧다 (0 으로 채우는 방식이면 같았을 것) |
  | **좌표가 남지 않는다** | `Seeker_사본_바이트에_문_좌표가_없다` | ✅ 문의 x(-3000)의 바이트 연속이 전문 어디에도 없다 |
  | 나머지는 동일 | `문_블록_앞부분은_두_사본이_같다` | ✅ flags 의 `HasDoor` 비트만 다르다 |
  | **룸이 세션별로 인코딩** | `문은_Runner_에게만_실린다` | ✅ 두 세션 중 **정확히 한쪽만** 문을 받고, 받지 않은 쪽 좌표는 0 |
  | 공통 항목 | `열쇠와_장치는_양쪽_모두_받는다` | ✅ 열쇠 수·장치 수·제단 모두 같다 |
  | 주기 | `목표물_전문은_매_틱_나가지_않는다` | ✅ 60틱에 1~3회 (매 틱이면 60회) |
  | 격자 없음 | `격자가_없는_맵에서는_목표물_전문이_나가지_않는다` | ✅ 0회, `MatchState` 는 정상 |
  | 로비 복귀 | `로비로_되돌리면_목표물_전문이_멈춘다` | ✅ 200틱 동안 0회 |
  | 버퍼 여유 | `최악의_경우가_수신_버퍼_안에_들어간다` | ✅ 열쇠 18 + 장치 9 = 최악이 512B 미만 |
  | 프로토콜 게이트 | `curl "…?v=3"` | ✅ HTTP 200 (버전은 3 그대로 — 이 전문은 IG-008 의 인상 안에 들어간다) |
  | 회귀 — 맵 해시 | 기동 로그 | ✅ `7996AF3A` |

- **R-2.3 이 닫혔다.** 문 좌표는 이제 Seeker 세션에 **도달하지 않는다.** 0 으로 채우는 방식을
  택하지 않은 것이 요점이다 — 그것도 "문이 있다" 는 사실과 블록 크기를 알려 준다. 없는 블록은
  복원할 방법이 없다. 테스트가 세 층으로 확인한다: 헤더 비트, 전문 길이, 그리고 **좌표 바이트가
  어디에도 남아 있지 않은지.**
- **픽스처 맵에 격자를 넣을 때 `FreeFloor` 를 손으로 적지 않았다.** `MapGridBuilder.MarkFreeFloor`
  로 실제 충돌에서 계산했다 — 손으로 적으면 벽 안의 셀을 통행 가능으로 표시하는 실수를 테스트가
  그대로 믿는다. 벽(x 5~6)이 지나가는 열이 자동으로 빠진다.
- 비고:
  - **씨드를 아직 제거하지 않았다.** 계획대로다 — 클라이언트가 수신 측이 되기 전에
    `RoomStateHeader.PlacementSeed` 를 빼면 클라이언트가 배치를 계산할 수 없어 목표물이 전부
    사라진다. IG-011c 와 같은 커밋이어야 한다. **IG-008 에서 `ControlKind.EndMatch` 를 서버만
    먼저 제거하면 매치가 끝나지 않는다고 판단한 것과 같은 종류의 순서 문제다.**
  - 그래서 **지금은 씨드와 전문이 둘 다 나간다.** 클라이언트는 전문을 버리고 씨드로 계산하므로
    게임 동작은 이전과 같다. 정보 누출도 아직 씨드 경로로 남아 있다 — **R-2.3 은 와이어 계약
    수준에서 닫혔고, 실제로 닫히는 것은 씨드가 빠지는 IG-011c 다.**
  - 장치 상태 바이트(소진·파괴·쿨다운)는 0 이 나간다 → IG-013·IG-015. 열거형을 만들지 않은
    이유는 D-8 과 같다.
  - 문 개방 여부도 `false` 가 나간다. 삽입된 열쇠 수로 정해지므로 IG-012 가 채운다.
  - 주기가 다른 전문이 셋이 됐다 — `RoomState`·`MatchState` 2Hz, `ObjectiveState` 5초. 게이트
    깃발도 셋이다. 공유하면 한쪽이 깃발을 내려 다른 쪽의 즉시 전송이 사라진다(IG-008 에서 확인).
- **이 태스크가 이 루프의 보안 목표를 달성한다 (R-2.3).** 지금은 `RoomStateHeader.PlacementSeed`
  가 와이어로 가서 **모든 클라이언트가 같은 씨드로 문 위치를 계산**하므로, Seeker 의 프로세스
  메모리에 문 좌표가 들어 있다. 룰셋은 문이 Runner 에게만 보여야 한다고 정하지만 WebGL 빌드가
  디컴파일되는 전제에서 컬링 레이어로 막을 수 있는 종류가 아니다.
- 범위: `EventKind.ObjectiveState = 3`. 세션별 인코딩 — **Seeker 사본에서 문 블록을 아예 뺀다.**
  열쇠는 전원 공통(룰셋: Seeker 가 열쇠를 보는 것이 지키는 전술을 만든다), 장치·제단도 공통.
  `RoomStateHeader.PlacementSeed` 를 와이어에서 제거 → `WireSize` 15 → 11.
- 주의: 전문 크기가 ≈166B 이고 클라이언트 수신 버퍼가 512B(`NetworkClient.ReceiveBytes`)라
  여유가 3배뿐이다. 주기는 AS-4(변경 즉시 + 5초)를 따른다.
- **씨드 제거는 클라이언트가 수신 측이 된 뒤여야 한다** — 먼저 빼면 클라이언트가 배치를
  계산할 수 없어 목표물이 사라진다. IG-011c 와 같은 배포 단위이거나, 씨드를 남긴 채 전문을
  먼저 보내고 IG-011c 에서 뺀다.

### IG-011c1 — 배치 코드를 `Shared` 로 이동
- 상태: **DONE** (이터레이션 13, 2026-08-04) — **ADR 0002**
- **먼저 판단을 정정했다.** 이전 이터레이션 노트에 "배치 상수를 `Shared` 로 옮기는 것이 D-8 과
  충돌한다" 고 적었는데 부정확했다. D-8 의 기준은 "클라이언트가 이 값으로 무언가를 계산하는가"
  이고, 오프라인 배치를 클라이언트가 계산한다면 배치 상수는 `Shared` 가 **맞다** — 충돌이 아니라
  기준의 정상 적용이다. 계획서(§5.2)와 `structure.md` 8문 표 1번도 같은 답을 낸다. 그래서
  §5.4 의 "갈래가 둘 이상" 조건에 해당하지 않아 BLOCKED 로 두지 않았다.
- 변경 파일 (7개 + meta 2):
  - `docs/adr/0002-objective-placement-in-shared.md` (신규)
  - `Shared/Simulation/Objectives.cs` (`Modules/Realtime` 에서 `git mv`, `public` 로)
  - `Shared/Simulation/ObjectivePlacement.cs` (`MatchRules.cs` 에서 `git mv`, `public` 로)
  - `Shared/Simulation/MatchConstants.cs` — 배치 상수 4개 이동 (간격 2, 시도 횟수, 조합표)
  - `Modules/Realtime/RealtimeConstants.cs` — `Match` 에서 배치 상수 제거, **전송 주기만 남김**
  - `Modules/Realtime/Simulation/Room.cs` — 참조 갱신
  - `tests/…/Realtime/ObjectivePlacementTests.cs` — 참조 갱신
- 검증 (전부 실행함):

  | 확인 | 수단 | 결과 |
  |---|---|---|
  | 서버 테스트 | `dotnet test` | ✅ **336개 통과**, 실패 0 — **순수 이동이라 수가 변하지 않는 것이 맞다** |
  | 서버 경고 0 | `dotnet build` | ✅ 경고 0개 오류 0개 (`netstandard2.1` 도 컴파일됨) |
  | 클라이언트 컴파일 | `Assembly-CSharp` | ✅ 오류 0개 |
  | Unity `Shared` | Refresh + `.meta` | ✅ `Objectives.cs.meta`, `ObjectivePlacement.cs.meta` |
  | 회귀 — 맵 해시 | 기동 로그 | ✅ `7996AF3A` |

- **`git mv` 로 옮겨 이력이 따라간다.** 새로 쓰고 지우면 `git log --follow` 가 끊긴다.
- 비고:
  - **전송 주기는 `RealtimeConstants.Match` 에 남겼다.** `ObjectiveStateIntervalTicks` 는 서버가
    얼마나 자주 보내는지이고, 받는 쪽은 그것을 계산에 쓰지 않는다. 같은 기준을 적용하면 배치
    상수와 갈라지는 자리가 정확히 여기다 — `RealtimeConstants.Match` 가 이제 "전송 파라미터" 만
    담는 클래스가 됐고 주석도 그렇게 고쳤다.
  - `Objectives` 와 `DevicePlacement` 가 `public` 이 됐다. 모듈 경계 검사는 `Shared` 를 모두가
    참조하는 것을 허용하므로 위반이 아니다 — `Architecture.Tests` 4개가 그대로 통과한다.
  - **이 이동만으로는 아무 동작도 바뀌지 않는다.** 서버가 같은 함수를 다른 이름으로 부를 뿐이다.
    클라이언트는 아직 이 코드를 호출하지 않고(IG-011c2), 씨드도 그대로 나간다(IG-011c3).
    되돌리기 가장 쉬운 형태의 커밋이다.
### IG-011c2 — 클라이언트 목표물 수신·적용
- 상태: TODO
- 범위: `KeyPickup`·`EscapeDoor`·`MapDevice`·`ChainAltar` 가 **좌표를 받아 그려지는 것**이 된다.
  `MatchManager.PlaceObjectives`·`PlaceChainAltar`·`PlaceDevices`·`TryFindSpacedPoint`·
  `IsFreeFloor` 가 사라지고, 그 자리에 전문 적용이 들어간다.
- **오프라인 경로는 유지한다** (ADR 0002 로 결정됨). 세션이 없으면 클라이언트가
  `ObjectivePlacement.PlaceObjectives` 를 직접 부른다 — 이제 `Shared` 에 있으므로 호출할 수 있다.
  세션이 있으면 전문을 기다린다.
- 주의: 클라이언트는 `MapGrid` 가 필요하다. `BackroomsMapGenerator` 는 자기 격자를 갖고 있지만
  `MapGridData` 형태가 아니다 — `BuildGrid()`(IG-003)가 그것을 만들므로 오프라인에서는 그 결과로
  `MapGrid` 를 만들면 된다. `FreeFloor` 를 채우려면 `MapGridBuilder.MarkFreeFloor` 에 콜리전이
  필요하고, 런타임에는 `CollisionBoxes` 가 있다.
- 검증: 오프라인 Play 모드에서 목표물이 여전히 생기는지 + 컴파일. 네트워크 경로는 §7.4 스모크
  (사람 조작 필요).

### IG-011c3 — `PlacementSeed` 와이어 제거
- 상태: TODO
- 범위: `RoomStateHeader` 에서 `PlacementSeed` 제거 (`WireSize` 15 → 11), 코덱·테스트 갱신,
  클라이언트 `MatchSync` 의 씨드 전달 제거. 서버는 내부 재현용으로 씨드를 계속 갖는다.
- **IG-011c2 이후여야 한다.** 클라이언트가 전문으로 배치를 받기 전에 씨드를 빼면 목표물이
  사라진다. 반대로 c2 이후에는 씨드가 아무도 쓰지 않는 값이므로 제거가 독립적이다.
- **이 태스크가 R-2.3 을 실제로 닫는다.** 씨드가 사라지면 Seeker 클라이언트에 문을 계산할 입력이
  없다 — 배치 함수를 갖고 있어도(ADR 0002) 계산할 수 없다.
- 기획서 근거: §6 (문 랜덤 위치·플레이어만 볼 수 있음), §5 (장치 8~9개)
- 계획: `MatchRules` 가 `MapGrid` + `DeterministicSequence` 로 **제단 → 문 → 열쇠 → 장치** 순서로
  배치한다. 순서와 간격(열쇠 4m·장치 5m 이격)은 `MatchManager.PlaceObjectives`(`:535-566`)의
  것을 그대로 옮긴다 — 제단이 먼저인 이유("유일한 고정물이므로 나머지가 피해 간다")가 유효하다.
  `EventKind.ObjectiveState=3`, 세션별 인코딩. **술래 사본에서는 문 블록을 아예 뺀다.** 열쇠는
  전원 공통(룰셋상 술래가 열쇠를 보는 것이 지키는 전술을 만든다). `RoomStateHeader.PlacementSeed`
  를 와이어에서 제거 → `WireSize` 15 → 11.
- 변경 예정 파일: `Modules/Realtime/Simulation/MatchRules.cs`,
  `Shared/Contracts/Messages/ObjectiveStateMessage.cs`, `MessageCodec.cs`, `RoomStateMessage.cs`,
  `Game/MatchManager.cs`, `Game/KeyPickup.cs`, `Game/EscapeDoor.cs`
- 검증: `dotnet test` + 스모크 — **술래 클라이언트에 문 좌표가 도달하지 않음**을 수신 바이트로 확인
- 비고: **이 태스크가 R-2.3 의 정보 누출을 닫는다.** 전문 크기 ≈166B 이고 클라이언트 수신 버퍼가
  512B(`NetworkClient.ReceiveBytes`)라 여유가 3배뿐 — 열쇠·장치 수를 늘리는 변경에서 가장 먼저
  넘칠 자리다. 주기는 OQ-7(2Hz vs 5초+변경즉시) 확인 후 결정하되, 기본값 "변경 즉시 + 5초" 로
  진행한다(AS-4).

### IG-012 — 열쇠 습득·삽입·문 개방·탈출 판정
- 상태: TODO
- 기획서 근거: §3, §6
- 계획: 열쇠 습득은 매 틱 거리 폴링 — 수평 `keyPickupRadius`, **수직 1.6m** (`KeyPickup.Update`
  의 비대칭 허용치를 그대로 옮긴다. 위층이 아래층 열쇠를 빨아들이지 않게 하는 값이다).
  삽입은 `Interact` + 반경 + `keyInsertInterval` + 소지 확인 — 한 곳에서 직렬화되므로 "두 Runner
  가 동시에 10번째 열쇠를 넣는" 경우가 자동 해결된다. 탈출은 개방된 문간에서 `escapeHoldTime`
  유지, 층 차이 2m 초과면 리셋. 클라이언트의 `KeyPickup.Update` 거리 폴링과
  `MatchManager.TryPickUpKey` 호출을 삭제하고 회전·상하 진동만 남긴다.
- 변경 예정 파일: `Modules/Realtime/Simulation/MatchRules.cs`, `Match.cs`,
  `Game/KeyPickup.cs`, `Game/MatchManager.cs`, `tests/Modules.Tests/Realtime/MatchTests.cs`
- 검증: `dotnet test` + 스모크 (두 클라이언트가 같은 삽입 수를 본다)
- 비고: `Interact` 비트 자체는 IG-013 에 있으나 그 태스크가 BLOCKED 이므로, 이 태스크에서
  `ButtonFlags.Interact` 만 먼저 추가한다(장치 사용 판정은 넣지 않는다).

### IG-013 — `Interact` 입력 + 장치 사용 판정
- 상태: **BLOCKED** (OQ-1)
- 기획서 근거: §5.1, §5.2, §5.3
- 차단 사유: 기획서 §5.2 는 1:1 순간이동을 **"술래 전용 장치"**(다회, 쿨타임 12초)로 명시한다.
  룰셋(`ruleset.md:70,76`)은 같은 장치를 **"shared across all Runners"** 로 서술하고, 구현은
  `GameConfig.seekerCanActivateDevices: 0` 으로 **술래의 장치 사용을 아예 금지**한다. 기획서가
  SSOT 1순위이므로 기획서가 이기지만, 기획서를 따르면 장치 시스템의 접근 제어가 뒤집힌다
  (술래가 장치를 쓸 수 있어야 하고, 12초 쿨다운의 공유 범위가 달라진다). 게임플레이 영향이
  크므로 §6.4 에 따라 추측하지 않는다.

### IG-014 — 서버 발사체 + 피격 규칙 + 탄약
- 상태: TODO
- 기획서 근거: §4.1, §4.3, §2.1
- 계획: **클라이언트는 이미 발사를 보내고 있다** — `NetworkBootstrap.Sample` 이
  `_controller.FireHeld` 를 `ButtonFlags.Fire` 로 싣고 `InputValidator.Sanitize` 가 통과시키고
  서버는 **그것을 무시한다.** 이 태스크는 그 비트를 소비하는 일이다.
  룸이 발사체 목록을 들고 매 틱 진행시킨다. 판정은 `Shared/Collision/Raycaster` 의 **스윕** —
  120m/s 면 한 틱에 4m 를 가므로 위치 검사는 0.25m 벽을 즉시 통과한다. 발사체가 실체이므로
  **비행에는 되감기가 필요 없다**; 되감기는 발사 순간의 사수 위치에만 필요하고 200ms 상한
  (플레이어당 6틱 이력)이 걸린다. 방향은 눈 위치 + yaw/pitch — 그 둘은 이미 `InputFrame` 에 있다.
  피격 규칙은 `MatchManager.ReportHit`(`:392-423`)을 그대로 옮긴다: Runner 만 대상,
  `hitImmunity` 0.75초, 1방 → 출혈 + 무작위 순간이동, 2방 → 사망 + 사망 지점에 열쇠 흘리기.
  탄약(매거진 3·발사 간격·재장전)은 `MatchConstants`(공유)에 둬야 HUD 가 예측된다.
  클라이언트: `Bullet` 은 순수 표현이 되고 `SendMessageUpwards("OnHit")`·`PlayerAgent.OnHit` 제거.
- 변경 예정 파일: `Modules/Realtime/Simulation/MatchRules.cs`, `Projectile.cs`, `Room.cs`,
  `Game/PlayerAgent.cs`, `Bullet.cs`, `WeaponController.cs`, `tests/…/MatchRulesTests.cs`
- 검증: `dotnet test` — 무적 창 안의 2발이 죽이지 않는다 / 2방이 죽인다 / 사망 시 열쇠가
  흘려진다 / **빠른 탄이 벽을 통과하지 않는다(클라이언트가 100,000m/s 로 검증한 것과 같은 테스트)**
  / 발사율 위반 거절. + 스모크로 **부정 클라이언트가 피격을 거부할 수 없음**
- 비고: **이 태스크가 R-3.1 의 구멍을 닫는다.** 히트마커가 "벽에도 뜬다" 는 기존 미결 항목이
  여기서 자연히 해결된다 — 서버가 명중을 알린 시점에 뜨게 된다.

### IG-015 — 장치 파괴 (4발)
- 상태: TODO
- 기획서 근거: §5.3
- 계획: 발사체가 장치 AABB 에 맞으면 `deviceDestroyHits` 4 를 센다. 장치는 클라이언트에서 유일하게
  콜라이더를 가진 목표물이므로 서버도 장치를 충돌체로 등록한다. **문과 열쇠는 콜라이더가 없다 —
  서버도 그렇게 둔다** (문에 콜라이더를 주면 술래가 허공에 부딪혀 문을 찾는다).
- 검증: `dotnet test`

### IG-016 — 체인 드래그 서버 판정
- 상태: **BLOCKED** (OQ-4)
- 기획서 근거: §4.3
- 차단 사유: 규칙(3발 소진 → 제단으로 끌려가 3초 정지 후 재장전)은 서버 판정이 맞지만, 현재
  구현은 `NavMesh.CalculatePath` 로 최단 보행 경로를 구하고 그 **경로 길이로 견인을 페이싱**한다.
  서버에는 navmesh 가 없다. 선택지가 (1) 직선 견인 — 측정된 연출("31개 코너·399m 경로 대 55m
  직선")이 사라진다, (2) 격자 A\* — `Shared` 에 A\* 가 들어온다, `StairLink` 가 층 연결을 답한다,
  (3) 위치만 클라이언트 권위 — 이동 권위에 예외를 만들어 권하지 않는다. 되돌리기 어려운
  두 갈래이므로 §5.4 에 따라 확인 후 진행한다. 서버 인터페이스(잠금 + 목표 지점 + 소요 시간)는
  1·2 가 같으므로 1 로 시작해 2 로 올릴 수 있다.

### IG-017 — 근접 보이스 시스템
- 상태: **BLOCKED** (OQ-3)
- 기획서 근거: §7 전체
- 차단 사유: 갭 매트릭스에서 유일하게 `NONE` 인 영역이다 (`microphone`/`webrtc`/`opus` 전체
  grep 0건). 기획서 §7.1~§7.3 은 거리 기반 음성을 요구하고 §7.4 는 옵션으로 표시한다.
  현재 아키텍처는 `System.Net.WebSockets` 원시 사용 + **NuGet 금지** + 모듈 추가 시 확인
  필요이고, WebGL 클라이언트에서 마이크 캡처와 실시간 음성 릴레이는 새 전송 계층을 요구한다
  (WebRTC/SFU 또는 오디오 프레임 릴레이). 아키텍처 결정이므로 확인 없이 진행하지 않는다.
- 비고: 범위 축소(§7.4 옵션 제외) 또는 `DEFERRED` 가 현실적 결론일 수 있다.

### IG-018 — Unity EditMode 테스트 인프라
- 상태: TODO
- 계획: `Assets/**` 에 asmdef 가 0개다. 런타임 asmdef 하나 + EditMode 테스트 asmdef 하나를 만들고
  `Packages/manifest.json` 에 `testables` 를 추가한다. 대상은 클라이언트에 **남는** 뷰 로직 —
  전문 → 이벤트 발화(IG-010 의 `AcceptMatchState`)가 첫 테스트다.
- 검증: 테스트가 실제로 실행되고 통과하는 명령줄을 확정해 명령 카탈로그에 기록
- 비고: asmdef 추가는 `Assembly-CSharp` 의 구성을 바꾸므로 컴파일 검증 명령이 함께 바뀔 수 있다.
  D-2 에 따라 순수 로직은 `dotnet test` 가 담당하므로 이 태스크의 범위는 좁다.

### IG-019 — 상수 정리·문서 갱신·죽은 경로 제거
- 상태: TODO
- 계획: `architecture.md` 기본값 대체표에 "클라이언트가 규칙을 판정 → 서버가 판정하고 전문으로
  내려보낸다", 와이어 포맷 표에 `MatchState`·`ObjectiveState`, 프로토콜 3. `conventions.md` 에
  이번에 확정한 규칙(역할별 필터링은 와이어에서 한다 / 씨드 공유 배치는 정보가 새므로 좌표를
  내려보낸다 / Unity 물리가 필요한 판정은 export 시점에 구워 넣는다). `NVproject/CLAUDE.md` 의
  "히트·열쇠·탈출은 여전히 각 클라이언트가 판정한다" 가 거짓이 되므로 함께 고친다.
  루트 `CLAUDE.md` 의 **"137 tests"** 도 실제 173개로 고친다.
- 검증: `dotnet build` 경고 0 + 전체 테스트

### IG-020 — 레거시 맵 파일·스크립트 정리
- 상태: **BLOCKED** (OQ-5)
- 기획서 근거: (정리, R-0.2)
- 대상: `NVproject/Assets/Scripts/BackroomsMap.cs`(+`.meta`) — 어느 씬도 참조하지 않고 `MapName` 이
  `"backrooms"` 라 IG-001 이후 생성기와 이름이 겹친다. `NVserver/MapData/backrooms2f.json` — 등록도
  참조도 없고 `Ceiling Lid` 이전의 낡은 export 다. `NVserver/MapData/arena.json` — 등록도 참조도 없다.
- 차단 사유: 삭제는 되돌리기 번거로운 변경이라 확인을 받는다(ADR 0001 보호장치 4, `match-authority-plan.md` §8-1).
- 비고: **이름이 겹치는 것이 새로 생긴 위험이다.** `BackroomsMap.MapName` 과
  `BackroomsMapGenerator.MapName` 이 이제 둘 다 `"backrooms"` 이므로, 두 컴포넌트가 한 씬에 있으면
  `MapExport.FindInScene` 이 `MonoBehaviour` 순회에서 **먼저 걸린 쪽**을 집는다. 지금은 어느 씬도
  `BackroomsMap` 을 참조하지 않아 실제 문제가 아니지만, 방치하면 나중에 재현하기 어려운 export 사고가 된다.

---

## 결정 로그 (DECISIONS)

| 날짜 | 결정 | 사유 | ADR |
|---|---|---|---|
| 2026-08-03 | `SampleScene` 을 복제하지 않고 제자리에서 쓴다 | 씬이 이미 프로덕션 진입점이고(라우터가 라우팅), 오브젝트 9개짜리 런타임 생성 포인터라 옮길 로직이 없다. 복제하면 정합성 사고를 한 벌 더 만든다 | [0001](adr/0001-scene-strategy.md) |
| 2026-08-03 | 순수 게임 로직 검증은 `dotnet test`, Unity EditMode 는 뷰 로직만 | 루프의 목적이 그 로직을 서버로 옮기는 것이므로, 옮긴 뒤 로직은 `Modules.Tests` 대상이다. Unity 배치모드 테스트는 MCP 환경에서 취약하다 | — (D-2, 위 명령 카탈로그) |
| 2026-08-03 | 승리 조건(IG-007)을 매치 단계·시계(IG-006)에서 분리 | 승리 조건이 OQ-2·OQ-6 에 걸려 있어, 묶어 두면 상태 머신 전체가 차단된다 | — |
| 2026-08-03 | `match-authority-plan.md` 를 이 루프의 구현 계획 근거로 채택 | 이미 코드 인용 기반으로 Phase 0~6 을 정리해 두었다. 백로그는 그것을 LOOP §6 단위로 쪼갠 것이다 | — |
| 2026-08-04 | (D-3) 격자 `Cells` 를 `byte[]` 로 두고 base64 로 직렬화 | System.Text.Json 의 기본 동작이라 서버 파싱 코드가 0줄이고, 2450셀이 한 줄에 들어간다. 숫자 배열이면 4배 넘게 커진다. `MapLoaderGridTests` 로 왕복을 실증했다 | — |
| 2026-08-04 | (D-4) `Grid == null` 이면 맵 해시에 기여하지 않고, 있으면 반드시 포함한다 | 계획서는 "격자를 넣으면 export 를 다시 돌려야 한다" 고 했지만, 없을 때 0 을 섞으면 격자가 아직 없는 기존 파일 전부의 해시가 바뀌어 정보를 늘리지 않는 re-export 를 강요한다. 있으면 반드시 넣어야 하는 이유는 반대다 — 빼면 격자가 어긋난 채 해시가 일치하고, 이동 판정은 격자를 쓰지 않으므로 걸어 다니는 동안 아무 신호도 나지 않는다 | — |
| 2026-08-04 | (D-5) `FreeFloor` 를 `Physics.CheckCapsule`(r 0.32) 대신 **서버 플레이어 박스**(`PlayerRadius` 0.4)와 서버 충돌 코드로 판정 | 계획서의 캡슐 방식은 두 가지로 틀렸다. 불가능하다 — export 는 지오메트리를 만들지 않는 경로로 돌고 물리 질의는 "아무것도 없음" 을 돌려주므로 모든 셀이 통과한다. 부정확하다 — 0.32 프로브는 0.4 박스가 밀려날 자리를 통과시킨다. 플래그의 뜻이 "서버가 여기 플레이어를 놓아도 밀려나지 않는다" 이므로 판정을 서버 기준으로 두는 것이 정의에 맞다. 계단은 스텝이 콜리전 박스로 export 되므로 그대로 걸러진다 | — |
| 2026-08-04 | (D-9) 목표물 배치 코드를 `Shared` 에 둔다 | 클라이언트가 오프라인 연습에서 같은 배치를 계산해야 하고, 알고리즘을 두 벌 두면 씨드나 간격을 바꿀 때 한쪽만 바뀐다. **코드를 공유해도 정보는 새지 않는다** — 씨드가 와이어에 없으면 함수를 가진 Seeker 도 문을 계산할 입력이 없다. 지금까지의 구멍은 함수 위치가 아니라 씨드 공유에서 왔다 | [0002](adr/0002-objective-placement-in-shared.md) |
| 2026-08-04 | (D-7) `GameConfig` 의 공유 값을 **소문자 프로퍼티**로 노출한다 | `config.matchDuration` 이 호출부에서 필드처럼 읽히므로 `MatchManager`·HUD·무기·장치의 호출부를 한 줄도 고치지 않고 값의 출처만 옮길 수 있다. C# 관례대로 대문자로 바꾸면 수십 곳을 고쳐야 하고 그 변경은 이 태스크의 목적과 무관한 위험이다. 프로퍼티는 직렬화되지 않으므로 에셋이 옛 사본을 들고 있을 수도 없다 | — |
| 2026-08-04 | (D-8) 판정 전용 값은 `RealtimeConstants.Match` 로 **지금 옮기지 않는다** | `RealtimeConstants` 는 서버 모듈의 `internal` 인데 클라이언트가 아직 그 규칙들을 판정한다(`ReportHit`, `MapDevice.OnHit`, `ScatterKeys`). 지금 옮기면 클라이언트가 자기가 쓰는 값을 읽을 수 없다. 각 값은 해당 판정이 서버로 가는 태스크에서 함께 옮기고, 빈 클래스를 미리 만들지 않는다 — 만들면 어디까지 옮겨졌는지 읽어서 알 수 없다 | — |
| 2026-08-04 | (D-6) `test-room` 은 격자를 내놓지 않는다 | 계획서는 "방 하나이므로 전부 `FreeFloor`" 라고 했으나 그 맵에는 중앙 플랫폼과 커버 블록 4개가 있어 전부 채우면 블록 안이 걸을 수 있는 곳이 된다. 게다가 그 씬은 매치 규칙을 돌리지 않으므로 배치할 목표물이 없다. 없으면 해시에도 기여하지 않아 `test-room.json` 이 안정적으로 유지된다 | — |

## 가정 (ASSUMPTIONS)

| ID | 가정한 내용 | 근거 | 영향 범위 | 확인 필요 |
|---|---|---|---|---|
| AS-1 | 룰셋 `NVproject/.claude/skills/game-rules/references/ruleset.md` 를 기획서의 **수치 보충 소스**로 쓴다 | LOOP §2 의 SSOT 표에 없지만, 기획서가 비워 둔 값(매치 시간·무적 창·삽입 간격 등)을 이 문서와 `GameConfig.asset` 이 이미 정해 두었다. 여기서 가져오는 것은 §9 가 금지하는 "창작" 이 아니다. **기획서와 충돌하면 기획서가 이긴다**(OQ-1 이 그 경우) | 전체 | 예 — SSOT 순위 확인 |
| AS-2 | 매치 시간 480초(8분) | 룰셋 "Match duration 8:00 (tune)", `GameConfig.asset:matchDuration 480`. 기획서 §8 은 "시간 종료" 만 말하고 값을 주지 않는다 | IG-005, IG-006 | 아니오 (AS-1 로 커버) |
| AS-3 | 역할 리빌 4초, 각종 연출 시간은 `GameConfig.asset` 의 현재 값 | 기획서에 없고 게임플레이 영향이 작은 연출 값 → §6.4 의 "합리적 기본값" | IG-005, IG-006 | 아니오 |
| AS-4 | `ObjectiveState` 전문 주기 = 변경 즉시 + 5초 | 2Hz 면 8인 룸에서 166B×2×8 ≈ 2.6KB/s 가 더 붙는다. 변경이 드문 블록이므로 낮추는 편이 낫다 | IG-011 | 예 → OQ-7 |
| AS-9 | 배치 간격 — 열쇠 4m, 장치 5m, 시도 64회 | 기획서에 없다. 클라이언트의 `MatchManager.PlaceObjectives`·`TryFindSpacedPoint` 가 쓰던 값을 그대로 옮겼다. 장치가 더 큰 이유는 상호작용 반경(2.2m)이 겹치면 어느 것을 쓰는지 모호해지기 때문이다 | IG-011a (배치 밀도) | 아니오 — 게임플레이 영향이 작고 기존 구현 인용이다 |
| AS-8 | **클라이언트는 이동을 예측하지 않는다** — 서버 위치를 `SmoothDamp` 로 따라간다 | `grep "PlayerMovement\."` 가 클라이언트에서 0건, `ApplyAuthority` 가 `ControlMode.NetworkAuthority` 로 로컬 이동을 끈다(`NetworkBootstrap.cs:216`, `FirstPersonController.cs:366`). §8 은 예측을 **허용**하지만 요구하지 않고 기획서에도 요구가 없다 | 입력 반응 지연 (로컬 서버에서는 안 보인다) | 예 → IG-023 에서 예측을 구현할지, 아키텍처 문서를 고칠지 판단 |
| AS-7 | `TryRandomFreeFloor` 는 셀 중심을 돌려주고 셀 안에서 지터하지 않는다 | `FreeFloor` 는 셀 중심에서 플레이어 박스를 검사해 세워진 값이라, 지터된 점은 그 보장 밖이다. 클라이언트의 `margin` 0.55(지터 폭 0.95m)를 그대로 쓰면 벽까지 여유가 0.425m 로 줄어 서버 박스 반지름 0.4 와 0.025m 차이다. 열쇠는 콜라이더가 없어 무해하지만 순간이동 착지점은 플레이어다 | IG-011 (배치) | 예 — 열쇠가 3m 격자에 정렬되어 보이는 것이 문제면, 지터 후 `MapGridBuilder.IsFree` 재검사를 IG-011 에서 추가한다 |
| AS-5 | 장치 조합표(`AddTime`,`FullMapView`,`StopBleeding`,`FreezeAndXray`,`SeekerCameraView`,`Teleport`×2,`FullMapView`,`StopBleeding`)를 그대로 서버로 옮긴다 | 룰셋이 "the mix of effects is a level-design choice" 로 위임하고 `MatchManager.PlaceDevices`(`:644-668`)가 이미 정해 두었다 | IG-011 | 아니오 |

## 미해결 질문 (OPEN_QUESTIONS)

| ID | 질문 | 차단 중인 태스크 | 제안 옵션 |
|---|---|---|---|
| OQ-1 | **1:1 순간이동 장치는 술래 전용인가?** 기획서 §5.2 는 "술래 전용 장치"(다회, 쿨타임 12초)로 명시하는데, 룰셋 #6 은 "shared across all Runners" 라 하고 구현은 `seekerCanActivateDevices: 0` 으로 술래의 장치 사용을 금지한다 | IG-013 | (a) 기획서대로 — 술래 전용, 12초는 술래의 개인 쿨다운 (b) 룰셋대로 — Runner 공용, 12초 전역 락아웃 (c) 둘 다 — 술래는 §5.2 텔레포트, Runner 는 §5.1 5종 |
| OQ-2 | **Runner 전멸이 술래의 즉시 승리인가?** 기획서 §8 은 "2명 미만 탈출 / 시간 종료" 만 말하고 전멸 승리가 없다. 구현에는 `MatchOutcome.SeekerWipedRunners` + `seekerWinsOnWipe: 1` 이 있다 | IG-007 | (a) 즉시 승리 유지 (현재 구현) (b) 기획서대로 제거 — 전멸 후에도 시간 종료를 기다린다 (탈출이 불가능하므로 결과는 같고 대기만 남는다) |
| OQ-3 | **근접 보이스(§7)의 범위와 기술을 어떻게 정하는가?** 유일한 `NONE` 영역이고 NuGet 금지·모듈 추가 확인 필요 제약에 걸린다 | IG-017 | (a) `DEFERRED` — 인게임 규칙 이관을 먼저 끝낸다 (b) WebRTC(브라우저 기본) + 서버는 시그널링만 (c) 오디오 프레임을 기존 WebSocket 으로 릴레이 |
| OQ-4 | **체인 드래그의 경로 방식?** 서버에 navmesh 가 없다 | IG-016 | (a) 직선 견인 — 가장 가볍고 연출이 사라진다 (b) **격자 A\*** — `StairLink` 가 층 연결을 답한다, 연출이 거의 같다 (권장) (c) 1 로 시작해 2 로 올린다 |
| OQ-5 | **레거시 파일을 삭제해도 되는가?** `BackroomsMap.cs`(+`.meta`) — 어느 씬도 참조하지 않음, `MapData/backrooms2f.json`, `MapData/arena.json` — 등록도 참조도 없음. IG-001 이후 `BackroomsMap.MapName` 이 생성기와 **같은 `"backrooms"`** 가 되어 한 씬에 둘이 있으면 export 대상이 순회 순서로 갈린다 | IG-020 | (a) 삭제 (b) 남긴다 — 그러면 `BackroomsMap.MapName` 을 충돌하지 않는 값으로 바꿔 둔다 |
| OQ-6 | **2인 매치에서 Runner 승리가 불가능한 것이 의도인가?** `escapesToWin` 2, `MinPlayersToStart` 2 → Seeker 1 + Runner 1 이면 탈출 2명을 만들 수 없다 | IG-007 | (a) 최소 인원을 3 으로 올린다 (b) `escapesToWin` 을 인원에 따라 정한다 (c) 의도된 것 — 2인은 개발용 조합일 뿐 |
| OQ-7 | `ObjectiveState` 전문 주기 — 2Hz 인가 "변경 즉시 + 5초" 인가 | (차단 아님, AS-4 로 진행) | (a) 변경 즉시 + 5초 (권장, AS-4) (b) 2Hz — 대역폭 +2.6KB/s |

---

## 다음 이터레이션

**IG-011c2 — 클라이언트 목표물 수신·적용** (P2).

배치 코드가 `Shared` 에 있으므로(ADR 0002) 클라이언트가 그것을 호출할 수 있다. c2 는 두 경로를
가른다 — **세션이 있으면 전문을 기다리고, 없으면 직접 계산한다.** 오프라인 Play 모드가 살아
있는지가 이 태스크의 주된 검증이고, 그것은 제가 실측할 수 있다(이터레이션 5·9·10 에서 같은
방식으로 확인했다).

그다음 IG-011c3 이 씨드를 와이어에서 빼면 **R-2.3 이 실제로 닫힌다.**

BLOCKED 6건(IG-007, IG-013, IG-016, IG-017, IG-020, IG-021)은 여전히 OQ-1·2·3·4·5·6 을 기다린다.

---

### 이전 판단 기록 (이터레이션 13)

**내 이전 노트를 정정했다.** "배치 상수를 `Shared` 로 옮기면 D-8 과 충돌한다" 고 적었는데,
D-8 의 기준("클라이언트가 이 값으로 계산하는가")을 적용하면 오히려 `Shared` 가 답이다. 충돌이
아니라 기준의 정상 적용이었고, 그래서 §5.4 의 BLOCKED 조건에 해당하지 않았다.

ADR 은 그래도 썼다. **`Shared` 표면을 늘리는 것은 구조적 결정**이고 — Unity(IL2CPP)와 .NET 이
같은 코드를 컴파일해야 하는 제약이 걸리는 면적이 늘어난다 — `architecture.md` 가 그런 변경에
주의를 요구한다. ADR 의 핵심 논점은 "코드를 공유해도 정보는 새지 않는다, 막아야 하는 것은 코드가
아니라 입력이다" 이고, 그것이 이 프로젝트의 출발 전제(WebGL 빌드는 디컴파일된다)와 일관된다.

`git mv` 로 옮겨 이력이 따라간다. 새로 쓰고 지우면 `git log --follow` 가 끊긴다.

IG-011b 로 와이어 계약 수준에서는 닫혔다 — 문 좌표가 Seeker 세션에 도달하지 않는다. 그런데
**씨드가 아직 나가므로 실제 누출은 남아 있다.** 클라이언트가 전문을 적용하고 씨드가 와이어에서
빠질 때 비로소 Seeker 의 프로세스에서 문 좌표가 사라진다.

**IG-011c 는 순서가 강제된다.** 한 커밋에서 셋을 함께 해야 한다:
1. 클라이언트가 `ObjectiveState` 를 받아 목표물을 배치한다
2. `MatchManager.PlaceObjectives`·`PlaceChainAltar`·`PlaceDevices`·`ScatterKeys`·
   `TryFindSpacedPoint`·`IsFreeFloor` 제거
3. `RoomStateHeader.PlacementSeed` 를 와이어에서 제거 (`WireSize` 15 → 11)

먼저 3만 하면 목표물이 사라지고, 먼저 1만 하면 서버 좌표와 클라이언트 좌표가 겹쳐 목표물이
두 벌 생긴다.

**시작 전에 판단할 것 — 오프라인 연습 모드.** 배치 코드가 클라이언트에서 사라지면 세션 없이
Play 할 때 목표물이 하나도 생기지 않는다(현재 `SampleScene` 을 그냥 Play 하면 오프라인 매치가
돈다 — 이터레이션 5·9에서 실측했다). 선택지 둘:
- (a) `MatchRules.PlaceObjectives` 를 `Shared` 로 옮겨 양쪽이 쓴다. **씨드가 와이어에 없으면
  코드가 공유되어도 정보 누출은 없다** — Seeker 클라이언트가 배치 함수를 갖고 있어도 씨드를
  모르므로 문을 계산할 수 없다. 계획서(§5.2 끝)가 권하는 방식이다.
- (b) 오프라인 모드를 포기한다.
(a) 를 권한다. 다만 `MatchRules` 가 `RealtimeConstants.Match`(서버 `internal`)를 읽고 있어,
옮기려면 배치 상수도 함께 `Shared` 로 가야 한다 — **그 상수들은 판정이지 표시가 아니라서
`MatchConstants` 에 두기로 한 기준(D-8)과 충돌한다.** ADR 이 필요할 수 있다.

BLOCKED 6건(IG-007, IG-013, IG-016, IG-017, IG-020, IG-021)은 여전히 OQ-1·2·3·4·5·6 을 기다린다.

---

### 이전 판단 기록 (이터레이션 12)

문 필터를 **0 으로 채우지 않고 블록을 빼는** 방식으로 만들었다. 0 으로 채우면 "문이 있다" 는
사실과 블록 크기가 남고, 그것도 정보다. 테스트를 세 층으로 두었다 — 헤더 비트, 전문 길이(정확히
9B 짧다), 그리고 **좌표 바이트가 전문 어디에도 남아 있지 않은지.**

픽스처 맵에 격자를 넣을 때 `FreeFloor` 를 손으로 적지 않고 `MapGridBuilder.MarkFreeFloor` 로
실제 충돌에서 계산했다. 손으로 적으면 벽 안의 셀을 통행 가능으로 표시하는 실수를 테스트가 그대로
믿는다.

§6.1 을 또 하나 넘겼다(9개). **이미 a/b/c 로 쪼갠 뒤에도** 프로토콜 추가는 계약·코덱·상수·송신·
수신·테스트를 한꺼번에 건드린다. 다음에 와이어를 추가할 때는 (계약+코덱)과 (송신+통합 테스트)를
더 나눈다.

**서버가 목표물을 배치한다** — 제단·문·열쇠 10·장치 9가 실제 `backrooms` 격자에서 전부 몸이
들어가는 자리에 놓이고, 같은 씨드가 같은 배치를 낸다. 아직 서버 안에만 있다.

**IG-011b 가 이 루프의 보안 목표(R-2.3)를 실제로 닫는 지점이다.** 지금은 `PlacementSeed` 가
와이어로 가서 Seeker 의 메모리에 문 좌표가 들어 있다. 전문이 좌표를 역할별로 걸러 내려보내면
그 구멍이 닫힌다 — 그것이 이 이관 작업의 애초 목적이었다.

**씨드 제거 시점에 주의한다.** 먼저 빼면 클라이언트가 배치를 계산할 수 없어 목표물이 사라진다.
IG-011c(클라이언트 수신)와 같은 배포 단위로 묶거나, 씨드를 남긴 채 전문을 먼저 보내고 IG-011c
에서 뺀다. **IG-008 에서 `ControlKind.EndMatch` 를 서버만 먼저 제거하면 매치가 끝나지 않는
구간이 생긴다고 판단한 것과 같은 종류의 문제다.**

BLOCKED 6건(IG-007, IG-013, IG-016, IG-017, IG-020, IG-021)은 여전히 OQ-1·2·3·4·5·6 을 기다린다.

---

### 이전 판단 기록 (이터레이션 11)

IG-011 을 a/b/c 로 미리 쪼갠 것이 맞았다. IG-011a 만으로도 파일 6개 + 테스트 16개이고,
전문·클라이언트 적용까지 한 커밋에 넣으면 IG-008(11개 파일)보다 커졌을 것이다.

배치가 서버로 왔지만 **아직 클라이언트와 좌표가 일치하지 않는다** — 같은 씨드를 쓰지만
알고리즘이 다르다(서버는 셀 중심, 클라이언트는 셀 안 지터). 이것은 IG-011c 가 클라이언트를
수신 측으로 바꿀 때 사라지고, 그때까지 네트워크 매치의 목표물은 지금까지와 같이 클라이언트
계산이다. **즉 IG-011a 는 아직 게임 동작을 바꾸지 않는다** — 되돌리기 쉬운 커밋이다.

IG-022 가 DEFERRED 되어 매치 진행(단계·시계·잠금)은 여기서 닫혔다. 다음 덩어리는 **목표물**이고,
그것이 갭 매트릭스에서 가장 큰 미완 영역이다 — R-2.3(Seeker 에게 문 좌표가 새는 구멍),
R-6.3(문 배치), R-7.1(장치 배치).

**IG-011 은 반드시 미리 쪼개야 한다.** IG-008 이 11개 파일로 §6.1 을 넘긴 전례가 있고, 이 태스크는
그보다 크다 — 배치 알고리즘(서버) + 새 전문(와이어) + 클라이언트 수신 + 목표물 컴포넌트 전환.
시작 시 최소 이렇게 나눈다:
- **IG-011a** 서버 배치 (`MatchRules` 가 제단 → 문 → 열쇠 → 장치 순서로 자리를 잡는다) + 서버 테스트
- **IG-011b** `ObjectiveState` 전문 + 역할별 필터(문 블록을 Seeker 사본에서 **뺀다**) + 코덱 테스트
- **IG-011c** 클라이언트 수신·배치 적용 (`KeyPickup`·`EscapeDoor`·`MapDevice` 가 좌표를 받아 그려진다)

**IG-011b 가 이 루프의 보안 목표를 실제로 달성하는 지점이다.** 지금은 `PlacementSeed` 가 와이어로
가서 **모든 클라이언트가 같은 씨드로 문 위치를 계산**하므로, Seeker 의 프로세스 메모리에 문
좌표가 들어 있다(갭 매트릭스 R-2.3). 컬링 레이어로는 막을 수 없는 종류다. 배치가 서버로 가고
좌표가 역할별로 걸러질 때 그 구멍이 닫히고, `RoomStateHeader.PlacementSeed` 를 와이어에서 뺄 수 있다.

**IG-012(열쇠·탈출 판정)는 IG-011 다음이다.** 그때 `MatchState` 전문의 `keysInserted`·`escapes`
필드가 실제 값을 갖고, IG-010 이 일부러 적용하지 않은 그 값들을 클라이언트가 받게 된다.

BLOCKED 6건(IG-007, IG-013, IG-016, IG-017, IG-020, IG-021)은 여전히 OQ-1·2·3·4·5·6 을 기다린다.

---

### 이전 판단 기록 (이터레이션 10)

IG-022 를 하려고 조사했더니 **전제가 틀렸다** — 클라이언트 예측이 구현되어 있지 않아 고칠 증상이
없었다. 코드 변경 없이 DEFERRED 로 확정하고, 그 사실을 AS-8 로 올렸다. IG-009 에 적어 둔
"`SimulationFlagsOf` 는 IG-010 이 쓴다" 도 틀린 기록이어서 함께 고쳤다.

**계획을 지우는 것도 이터레이션의 산출물이다.** 예측이 없다는 사실을 모르고 리컨실리에이션을
건드렸다면, 고칠 것이 없는 코드에 조건을 추가하고 그것을 "잠금 떨림 수정" 이라고 기록했을 것이다.

### 이전 판단 기록 (이터레이션 9)

**서버 시계가 클라이언트에 닿았다.** `MatchSync` 가 실제로 씬에 존재하게 됐고(그전에는 없었다),
전문의 단계·시계가 `MatchManager` 를 움직인다. 남은 것은 잠금이 예측에 반영되지 않아 리빌
동안 화면이 떨리는 문제(IG-022)와, 서버가 열쇠·탈출·전투를 세기 시작하는 일(IG-012·IG-014)이다.

**사람의 확인이 필요한 것이 쌓였다.** §7.4 스모크 테스트는 두 클라이언트와 조작을 요구하므로
제가 완결할 수 없고, 세 항목이 그것을 기다린다:
1. 두 화면의 단계·시계 일치 (IG-010)
2. 맵 해시 `일치` 로그 (IG-001)
3. 리빌이 서버 틱에 끝나는가 (IG-006)

**Tools ▸ NV ▸ Build and Launch 2 Clients** 로 두 클라이언트를 띄우고, 로비에서 방을 만들어
코드를 넘긴 뒤 **게임 시작**을 누르면 세 항목을 한 번에 볼 수 있다.

BLOCKED 6건(IG-007, IG-013, IG-016, IG-017, IG-020, IG-021)은 OQ-1·2·3·4·5·6 의 답을 기다린다.
나머지 7개는 진행 가능하다.

---

### 이전 판단 기록 (이터레이션 9)

IG-010 을 하면서 `MatchSync` 부재를 발견했다. 그것이 없는 동안 **서버 연동 매치는 시작 신호부터
받지 못했다** — 즉 이 루프가 여덟 이터레이션에 걸쳐 옮긴 서버 판정이 클라이언트에 도달할 경로가
애초에 끊겨 있었다. `MatchBootstrap` 이 런타임에 만들도록 고쳤고, 그래서 IG-010 의 가치는
"전문을 적용한다" 보다 "**적용될 수 있게 만들었다**" 쪽이 크다.

### 이전 판단 기록 (이터레이션 8)

**서버가 보내는 것을 클라이언트가 처음으로 쓴다.** 지금까지 여덟 이터레이션은 전부 서버
쪽이었고, 클라이언트는 `MatchState` 를 받아서 버린다(`DispatchEvent` 의 빈 자리). IG-010 이
그 자리를 채우면 서버 시계가 HUD 를 움직인다.

**이 태스크의 검증은 제가 완결할 수 없다.** §7.4 스모크 테스트는 두 클라이언트를 띄워 로비에서
방을 만들고 시작해야 하는데, MCP 로는 입력을 주입할 수 없다(`NVproject/CLAUDE.md`). 컴파일과
코드 경로까지는 확인할 수 있고, **화면 일치 관측은 사람의 조작이 필요하다** — 그 사실을
검증 기록에 그대로 적는다. IG-001 이 미룬 맵 해시 `일치` 실측도 같은 절차에 묶여 있다.

BLOCKED 6건(IG-007, IG-013, IG-016, IG-017, IG-020, **IG-021**)은 OQ-1·2·3·4·5·6 의 답을
기다린다. 나머지 7개는 의존 순서대로 진행 가능하다.

**OQ-2·OQ-6 의 값이 이번에 더 올랐다.** 그 답이 없으면 IG-007 이 막히고, IG-007 이 막히면
IG-021 도 막힌다 — 즉 **클라이언트에 남은 치팅 가능한 판정(`EvaluateWinConditions`, 히트 판정)을
제거할 수 없다.** 갭 매트릭스의 R-1.5·R-3.1 이 그 자리다.

---

### 이전 판단 기록 (이터레이션 8)

**서버가 매치 상태를 실제로 내려보내고 있다.** 프로토콜 3, 단계·시계·역할이 2Hz 전문으로
세션별 인코딩되어 나가고, Seeker 사본에서 열쇠 자리가 지워진다. 클라이언트는 그 전문을
받아서 **버린다** — 아직 자기 시계로 매치를 돌린다.

**순서를 판단해야 한다.** 백로그 순서는 IG-009(`EntityFlags`) → IG-010(뷰 전환)이지만,
IG-009 는 출혈·역할 비트를 스냅샷에 넣는 일이고 그 값을 **채우는 판정은 IG-012·IG-014** 에
있다. 즉 IG-009 를 먼저 하면 IG-006·IG-008 처럼 "자리만 잡은" 커밋이 하나 더 늘고, 클라이언트가
서버 시계를 무시하는 상태가 그만큼 길어진다.

IG-010 을 먼저 하면 **여기까지 옮긴 판정이 실제로 화면에 닿는다** — 서버 시계가 HUD 를 움직이고,
`ControlKind.EndMatch` 와 `EvaluateWinConditions` 가 사라지고, §7.4 의 두 클라이언트 스모크
테스트를 처음으로 의미 있게 돌릴 수 있다(IG-001 이 미룬 맵 해시 실측도 그때 함께).

다만 IG-010 은 `MatchManager` 를 심판에서 뷰로 바꾸는 일이라 크다. 다음 이터레이션 시작 시
**IG-010 을 (a) 전문 수신·적용 + 이벤트 발화, (b) 죽은 판정 경로 제거로 쪼갤지 먼저 판단**한다.
§6.1 을 이번에 넘겼으므로 이번에는 미리 쪼갠다.

BLOCKED 5건(IG-007, IG-013, IG-016, IG-017, IG-020)은 OQ-1·2·3·4·5·6 의 답을 기다린다.
나머지 8개는 의존 순서대로 진행 가능하다.

**IG-010 진행 시 주의**

1. **HUD·`PlayerRoleLoadout`·`GameHudController` 를 건드리지 않는다.** 그들은
   `MatchManager` 의 이벤트(`PhaseChanged`·`KeysChanged`·`EscapesChanged`·`RolesAssigned`·
   `MatchEnded`)를 구독하고 있으므로, `AcceptMatchState` 가 전문을 받아 **같은 이벤트를 발화**
   하면 화면 쪽은 그대로 동작한다. 이 이벤트 목록이 replication 계약이라던 설계가 여기서 값을 한다.
2. **`_phaseTimer`/`TimeRemaining` 의 로컬 감소는 남긴다.** 전문이 2Hz 라 그 사이를 메워야 HUD
   시계가 튀지 않는다. 전문이 올 때마다 서버 값으로 덮는다.
3. **`ControlKind.EndMatch` 제거를 이 커밋에 묶는다.** 서버·클라이언트 어느 한쪽만 제거하면
   매치가 끝나지 않는 구간이 생긴다. `NetSession.ReportMatchEnd`·`MatchSync.OnLocalMatchEnded`·
   `MatchManager.EvaluateWinConditions`·`ResolvesOutcome` 이 함께 사라진다.
   **단 결과 코드는 서버가 아직 정하지 않는다(IG-007, OQ-2·OQ-6)** — 그래서 이 제거를 하면
   **매치가 시간 종료로만 끝난다.** 전멸·탈출 승리가 판정되지 않는 구간이 생기므로, 그것을
   받아들일지 아니면 IG-007 의 답을 먼저 받을지 판단해야 한다.
4. 클라이언트의 `NV.Game.MatchPhase`·`Role` 을 `Shared` 의 `MatchPhase`·`MatchRole` 로
   대체할 수 있게 된다(IG-006·IG-008 이 남긴 두 벌 상태를 정리).
