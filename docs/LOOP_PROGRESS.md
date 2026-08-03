# LOOP PROGRESS — NVproject 인게임 구현

최종 갱신: 2026-08-04 (이터레이션 40 — **루프 종료**)
현재 이터레이션: 40 (종료)
기준 커밋: `72fe1e0`

> # 🛑 이 루프는 종료됐다
>
> 사용자 요청으로 이터레이션 39 뒤에 멈췄다. 10분 주기 스케줄(`4aacb7fb`)을 삭제했으므로 더 이상
> 자동으로 돌지 않는다. **이 파일은 이제 진행 상태가 아니라 인수인계 문서다.**
>
> 종료 시점 실측: `dotnet test` **424개 통과**(Modules 420 + Architecture 4), 실패 0 / 빌드 경고 0.
> **실행되지 않은 것 두 가지가 그대로 남아 있다** — 2클라이언트 스모크와 Unity EditMode 8개.
>
> §10 리포트: 최종 리포트는 `INGAME_GAP_MATRIX.md` 끝(이터레이션 27 작성 + 40 재확인),
> 질문 리포트는 이 파일의 **질문 리포트** 절. 아래 종료 리포트가 그 둘을 요약한다.

---

## 종료 리포트 (§10)

### 1. 이 루프가 실제로 한 일 — 한 문장

**기획서의 인게임 규칙 대부분은 이미 구현되어 있었고 잘못된 자리에 있었다.** 그래서 39번의
이터레이션은 "기능 구현" 이 아니라 **판정 주체를 클라이언트에서 서버로 옮긴 작업**이었다.
갭 매트릭스의 `DONE`(= 서버가 판정하고 클라이언트가 받아 표시한다)은 **3 → 28** 로 갔다.

### 2. 서버 권위로 옮겨진 것 (전부 자동 테스트로 고정)

| 계통 | 규칙 | 태스크 |
|---|---|---|
| 선행 차단 | 맵 이름·등록·export 정합성, `MapData` 격자 + 해시 + 서버 `MapGrid` 질의 | IG-001~004 |
| 매치 진행 | 단계 전이·시계(고정 틱)·역할 공개·이동 잠금 | IG-006·009·010 |
| 목표물 | 배치(제단·문·열쇠·장치)와 **역할별 좌표 필터** | IG-011a·b·c1~c3 |
| 열쇠 | 습득, 삽입, 문 개방 | IG-012a·b1~b3 |
| 탈출 | 문간 유지 판정, 탈출 수 | IG-012c1·c2 |
| 전투 | 발사 자격·탄창·연사, 발사체 비행(스윕), 피격, 출혈, 순간이동, 사망, 열쇠 흘리기, 무적 창 | IG-014a·b·c |
| 표현 | 탄약 와이어·HUD, 발사 알림으로 남의 예광탄 그리기 | IG-028a·b1·b2 |
| 검증 수단 | EditMode 인프라, 격자 없는 열화 경계, 타이브레이크 고정, 종료 권위 | IG-018·030·031·032 |

**정보 규칙이 함께 옮겨진 것이 이 루프에서 가장 값이 컸다(R-2.3).** Seeker 사본에서는 열쇠
진행도·소지 수가 0 이고 **문 블록이 아예 빠진다.** 필터가 코덱 안에 있어 호출부가 우회할 수 없고,
배치 씨드를 와이어에서 빼서 **계산 가능성까지** 닫았다 — 카메라 마스크로는 막을 수 없는 종류의
정보였다(WebGL 은 디컴파일된다).

### 3. 남은 태스크 10개 — 전부 사람의 결정이나 사람의 손을 기다린다

BLOCKED 8 / TODO 1 / DEFERRED 1. **아래 표가 백로그의 `DONE` 아닌 행 전부**이며,
위 백로그 표와 아래 "태스크 상세" 절에 각각의 계획·근거가 그대로 남아 있다.

| 태스크 | 상태 | 무엇을 기다리는가 |
|---|---|---|
| IG-007 승리 조건 판정 | BLOCKED | **OQ-2 + OQ-6** (최우선). 클라이언트에 남은 마지막 판정이다 |
| IG-021 클라이언트 판정 경로 제거 | BLOCKED | IG-007 (답이 오면 함께 열린다) |
| IG-013 `Interact` + 장치 6종 사용 판정 | BLOCKED | **OQ-1.** 기획서 §5 전체가 여기 걸려 있다 |
| IG-015 장치 파괴 (4발) | BLOCKED | IG-013. **부술 대상의 사용 규칙이 없으면 파괴 규칙을 정할 수 없다** |
| IG-016 체인 드래그 → 재장전 | BLOCKED | **OQ-4.** 지금 탄창 3발을 비우면 그 매치에서 더 못 쏜다 |
| IG-017 근접 보이스 | BLOCKED | **OQ-3.** 갭 매트릭스의 유일한 `NONE` 영역(3항목) |
| IG-020 레거시 맵 파일·스크립트 정리 | BLOCKED | **OQ-5.** 삭제해도 되는지 |
| IG-031 탈출·피격 타이브레이크 확정 | BLOCKED | **OQ-8.** 현재 동작은 `TieBreakTests` 가 고정 — 답이 (a)면 두 줄 이동으로 끝난다 |
| IG-023 클라이언트 이동 예측 + 리컨실리에이션 | TODO (P4) | **실측.** AS-8 이 "예측이 없다" 를 기록했고 §8 은 요구하지 않는다 — 로컬 서버에서는 증상이 없어 손대면 무엇을 고쳤는지 알 수 없다 |
| IG-022 예측에 `Frozen` 반영 + 플래그 필터 | DEFERRED | IG-023. 예측이 없으면 반영할 곳도 없다 |

**"규칙 태스크가 하나도 진행 가능하지 않다" 가 이 표의 요점이다.** 8개가 OQ 6개(1·2·3·4·5·8)에
걸려 있고, 진행 가능한 나머지 둘은 규칙이 아니라 품질(예측)이다.

### 4. 알려진 제약 · 남은 위험 (전문은 `INGAME_GAP_MATRIX.md` 끝)

1. **2클라이언트 스모크 미실행이 가장 큰 위험이다.** 서버 판정은 424개 테스트로 고정됐지만
   **그것이 화면에 도달하는지는 확인되지 않았다.** 적용 경로의 버그는 컴파일을 통과한다.
2. **승리 조건이 방장 판정으로 남아 있다.** 서버가 검사하는 것은 "보고자가 방장인가" 까지이고
   "방장이 어떤 결과를 보고하는가" 는 검사하지 않는다 — 조작된 방장은 결과를 바꿀 수 있다.
3. **탄창 3발을 비우면 그 매치에서 더 쏠 수 없다.** 기획서의 재장전이 체인 뒤에 오고 체인이
   OQ-4 로 막혀서, 순서를 임의로 정하지 않았다.
4. 프로토콜 3 이후 **서버와 클라이언트를 같은 커밋에 배포해야 한다**(구버전은 426 으로 거절).
5. `EntityFlags` 8비트를 전부 썼다. 매 틱 보낼 상태를 더 넣으려면 무엇이 정말 매 틱 필요한지
   다시 봐야 한다.
6. 룸 스폰이 2개뿐이라 **3인 이상 매치를 서버 테스트로 재현할 수 없다.**
7. **문서가 코드보다 먼저 낡는다.** 이터레이션 26·27·30·35·40 에서 내 문서가 거짓이 된 것을
   다섯 번 발견했다. 이 파일의 주장은 근거 파일 열로 되짚고 나서 신뢰하는 편이 낫다.

### 5. 사람이 이어받을 순서 (권장)

1. **에디터를 한 번 연다.** 이 한 번이 세 가지를 해소한다 — EditMode 8개 실행,
   `FireEventMessage.cs` 의 `.meta` 생성·커밋, 그리고 Unity 쪽 `Shared` 컴파일 확인.
2. **2클라이언트 스모크를 한 번 돌린다.** 절차와 태스크별 확인 항목은 아래 "사람이 해야 하는
   검증" 표에 있다. **가장 값이 큰 항목은 IG-012b1~b3(열쇠 삽입)** — 그 경로에는 자동 테스트가
   없다.
3. **OQ-2 + OQ-6 에 답한다.** 하나의 답이 IG-007 과 IG-021 을 함께 열고, 위 위험 2번을 닫는다.
4. 그다음은 OQ-1(장치) → OQ-8(타이브레이크, 답이 (a)면 두 줄 이동) → OQ-4(체인·재장전) 순.

---

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
| EditMode 테스트 | `Assets/Editor/Tests/` | **asmdef 가 필요 없다**(IG-018). `Assembly-CSharp-Editor` 가 `nunit.framework`·TestRunner·`Assembly-CSharp` 를 이미 참조하므로 그 폴더에 두면 보인다. 컴파일 검증은 `dotnet build Assembly-CSharp-Editor.csproj`. **실행은 에디터의 Test Runner 가 필요하다** — `internal`·`[SerializeField]` 는 보이지 않는다(→ IG-029) |
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
| `Event`/`FireEvent` `0x82`/4 | S→C | 17B — 사수·시작점·요·피치·틱. **이 프로젝트의 유일한 알림**(전문이 아니다): 발사한 틱에 한 번 보내고 반복하지 않는다. 놓치면 예광탄 하나를 잃고 판정에는 영향이 없다 | 서버 | `Shared/Contracts/Messages/FireEventMessage.cs`, ADR 0003 |

`buttons` 의 정의된 비트: `Jump`·`Fire`·`Crouch`·`Sprint`·**`Interact`(IG-012b1)**. `ButtonFlags.All`
밖의 비트는 `InputValidator.Sanitize` 가 지운다 — 버튼을 추가하면 그 마스크도 고친다. `Interact` 는
**엣지**이고(클라이언트가 래치해 틱마다 소비) **대상을 싣지 않는다** — 서버가 자기 좌표로 고른다.
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

아직 0 으로 나가는 필드: 장치 `State`(소진·파괴·쿨다운 → IG-013·IG-015).

**문 개방 여부는 IG-012b2 부터 실제 값이다.** 그 바이트는 **문 블록 안에** 있으므로 Seeker 사본에는
아예 실리지 않는다 — 별도의 역할 필터가 필요하지 않은 것이 블록을 통째로 빼는 설계의 부수 효과다.

**열쇠 목록은 IG-012a 부터 줄어든다.** 주워진 열쇠는 그 틱에 목록에서 빠지고 전문이 즉시 나가므로,
클라이언트는 개수 변화를 배치 갱신으로 읽어 목표물을 다시 세운다(`MatchSync._appliedObjectiveKeys`).

`MatchState` 는 IG-008 에서 들어왔다. **`RoomState` 와 성격은 같고(전문, 2Hz + 변경 즉시, 멱등)
본문이 수신자마다 다르다** — Seeker 사본에서는 `keysInserted` 와 모든 `carriedKeys` 가 0 이다.
필터는 `MessageCodec.WriteMatchState` 안에 있어 호출부가 우회할 수 없다.

`carriedKeys` 는 **IG-012a 부터**, `keysInserted` 는 **IG-012b2 부터** 실제 값이 나간다. 서버가
습득과 삽입을 판정하고 그 수를 센다. 둘 다 **Seeker 사본에서는 0 이고, 그 필터는 코덱 안에만 있다** —
룸은 걸러지지 않은 실제 값을 채운다. 필터가 두 곳에 있으면 한 곳을 고칠 때 다른 곳이 남는다.

`escapes` 는 **IG-012c1 부터** 실제 값이고, 열쇠 진행도와 달리 **Seeker 사본에서도 거르지 않는다** —
자기가 막아야 하는 수다. 기획서 §2.1 이 숨기는 것은 목표의 위치와 진행도이지 이것이 아니다.

아직 서버가 세지 않아 0 으로 나가는 필드: `flags`·`hits`(→ IG-014),
`outcome`(→ IG-007). **자리를 잡아 두었으므로 값이 채워질 때 와이어 포맷은 바뀌지 않는다.**

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
| IG-024 | `MatchManager` 의 시드 계산 중복 정리 | **DONE** (중복이 실제 결함이었다) | P4 | IG-011c3 | (정리) |
| IG-021 | 클라이언트 판정 경로 제거 (뷰 전환 2/2) | **BLOCKED** | P1 | IG-010, IG-007 | R-1.5, R-3.1 |
| IG-011a | 서버 목표물 배치 (제단·문·열쇠·장치) | **DONE** | P2 | IG-010 | R-6.3, R-7.1 |
| IG-011b | `ObjectiveState` 전문 + 역할별 필터 | **DONE** | P2 | IG-011a | **R-2.3**, R-6.4 |
| IG-011c1 | 배치 코드를 `Shared` 로 이동 (ADR 0002) | **DONE** | P2 | IG-011b | (기반) |
| IG-011c2 | 클라이언트 목표물 수신·적용 + 클라이언트 배치 제거 | **DONE** | P2 | IG-011c1 | R-6.3, R-7.1 |
| IG-011c3 | `PlacementSeed` 와이어 제거 | **DONE** | P2 | IG-011c2 | **R-2.3 ✅** |
| IG-012a | 열쇠 습득 서버 판정 | **DONE** | P2 | IG-011c3 | **R-6.1 ✅** |
| IG-012b1 | `ButtonFlags.Interact` 입력 경로 | **DONE** | P2 | IG-012a | (기반) |
| IG-012b2 | 삽입·문 개방 서버 판정 | **DONE** | P2 | IG-012b1 | R-6.2, R-6.5 |
| IG-012b3 | 클라이언트 삽입 적용 + 로컬 판정 차단 | **DONE** | P2 | IG-012b2 | **R-6.2 ✅, R-6.5 ✅** |
| IG-026 | 오프라인 상호작용에 이동 잠금 게이트를 맞춘다 | **DONE** | P4 | IG-012b3 | (정리) |
| IG-027 | 사망 시 흘린 열쇠를 흩뿌리기 (표현) | **DONE** | P4 | IG-014b | R-6.7 |
| IG-012c1 | 탈출 서버 판정 | **DONE** | P2 | IG-012b2 | R-6.7 |
| IG-012c2 | 클라이언트 탈출 적용 | **DONE** | P2 | IG-012c1 | R-6.7 |
| IG-013 | `Interact` 입력 + 장치 사용 판정 | **BLOCKED** | P2 | IG-011 | R-7.2~R-7.7, R-4.3 |
| IG-014a | 서버 발사체 + 탄약 | **DONE** | P2 | IG-009 | R-3.2, R-3.5 |
| IG-014b | 피격 판정 (출혈·순간이동·사망) | **DONE** | P2 | IG-014a | R-3.2, R-3.3, R-3.4, R-3.5, R-6.7 |
| IG-014c | 클라이언트 전투 적용 + 로컬 판정 차단 | **DONE** | P2 | IG-014b | **R-3.1 ✅**, R-2.1 |
| IG-028a | 탄약을 와이어에 싣고 HUD 에 적용 | **DONE** | P3 | IG-014a | **R-9.3 ✅** |
| IG-028b1 | 발사 알림 (서버 + 와이어, ADR 0003) | **DONE** | P3 | IG-028a | R-3.1 (연출) |
| IG-028b2 | 클라이언트가 발사 알림으로 예광탄을 그린다 | **DONE** | P3 | IG-028b1 | R-3.1 (연출) |
| IG-015 | 장치 파괴 (4발) | **BLOCKED** (IG-013 이 OQ-1 대기) | P3 | IG-013, IG-014 | R-7.8 |
| IG-016 | 체인 드래그 서버 판정 | **BLOCKED** | P3 | IG-014 | R-3.7 |
| IG-017 | 근접 보이스 시스템 | **BLOCKED** | P3 | - | R-8.1~R-8.3 |
| IG-018 | Unity EditMode 테스트 인프라 | **DONE** (asmdef 불필요였다) | P2 | IG-010 | (검증 수단) |
| IG-029 | 적용 경로에 테스트용 공개 이음새 | **DONE** (이음새가 필요 없었다) | P3 | IG-018 | (검증 수단) |
| IG-030 | 격자 없는 맵의 전투 열화 경계 고정 | **DONE** | P3 | IG-014b | (검증 수단) |
| IG-031 | 탈출·피격 타이브레이크 확정 | **BLOCKED** (OQ-8) | P2 | IG-012c1, IG-014b | R-3.1, R-6.6 |
| IG-032 | 매치 종료 권위 검증 | **DONE** | P2 | IG-010 | R-1.5 (신뢰 경계) |
| IG-019 | 상수 정리·문서 갱신·죽은 경로 제거 | **DONE** | P4 | 전부 | (정리) |
| IG-025 | `Jump` 엣지가 입력 반복에 실리는 문제 | **DONE** (결함 아님, 관계를 테스트로 고정) | P4 | - | (품질) |
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
  | ~~`escapes`~~ | **IG-012c1 이 세고 IG-012c2 가 적용한다.** 로컬 `TickEscapes` 도 같은 태스크에서 막았다. 수는 전문에서, **누가 나갔는지는 스냅샷의 `EntityFlags.Escaped`** 에서 온다 |
  | ~~`keysInserted`~~ | **IG-012b2 가 세고 IG-012b3 이 적용한다.** 로컬 판정(`TryInsertKey`)을 같은 태스크에서 막았다 — 적용만 하고 판정을 남기면 두 심판이 같은 카운터를 다르게 올린다 |
  | ~~`carriedKeys`~~ | **IG-012a 에서 적용하기 시작했다.** 서버가 습득을 판정하므로 이제 0 이 아니고, 클라이언트의 폴링은 같은 태스크에서 멈췄다 — 덮어쓸 "올바르게 센 값" 이 더 이상 없다 |
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
- 상태: **DONE** (이터레이션 14, 2026-08-04)
- 변경 파일 (4개):
  - `Net/NetworkClient.cs` — `ReadObjectiveState`, `Objectives`·`HasObjectiveState`·`HasObjectiveDoor`
  - `Game/MatchManager.cs` — 배치 헬퍼 6개 제거, `ServerPlacesObjectives`(→ IG-012b3 에서
    `ServerOwnsObjectives` 로 개명),
    `AcceptObjectiveState`, `BuildObjectiveObjects`, `OfflineGrid`
  - `Net/Session/MatchSync.cs` — `ServerPlacesObjectives = true`, `ApplyObjectiveState` 폴링
- 검증 (전부 실행함):

  | 확인 | 수단 | 결과 |
  |---|---|---|
  | 클라이언트 컴파일 | `dotnet build Assembly-CSharp.csproj` | ✅ 오류 0개 |
  | 서버 회귀 | `dotnet test` | ✅ 336개 통과 (서버 미변경) |
  | **오프라인 배치 유지** | Play 모드 조회 | ✅ `serverPlacesObjectives=False`, **열쇠 10 · 장치 9 · 제단 · 문** 전부 생성, `doorPos=(-21.0, 3.2, 3.0)` (2층), `phase=Playing` |

- **오프라인 실측이 이 태스크의 핵심 증거다.** 클라이언트에서 배치 알고리즘을 지웠는데도
  세션 없이 Play 하면 목표물이 그대로 생긴다 — `Shared` 의 같은 코드를 부르기 때문이다(ADR 0002).
  지우기 전과 개수·종류가 같고, 문은 2층에 놓였다(격자가 2층을 포함하므로 정상).
- **제거한 헬퍼 6개**: `PlaceChainAltar`, `TryFindLandingSpot`, `IsFreeFloor`, `PlaceDevices`,
  `TryFindSpacedPoint`, `IsClearOfPlacements`. `IsFreeFloor` 가 특히 의미 있다 — 그것은
  `Physics.CheckCapsule`(반지름 0.32)로 계단 셀을 걸렀는데, `Shared` 버전은 같은 질문을
  **콜리전 박스와 서버의 플레이어 박스(0.4)** 로 답한다. Unity 물리가 필요 없고 크기도 맞다.
- **온라인과 오프라인이 오브젝트 생성을 공유한다.** `BuildObjectiveObjects` 하나가 두 경로를
  받으므로, 열쇠가 어떻게 보이는지를 바꿀 때 한쪽만 바뀌는 일이 없다.
- 비고:
  - `AcceptObjectiveState` 는 **diff 하지 않고 다시 만든다.** 전문은 멱등하고 드물게 오며
    오브젝트 생성이 싸다. diff 를 하면 열쇠를 위치로 대조해야 하는데, 그것이 정확히 두 열쇠가
    같은 셀을 쓸 때(간격 조건을 포기한 경우) 깨지는 비교다.
  - 그래서 `MatchSync` 가 **열쇠 수로** 갱신 여부를 판별한다. 5초마다 같은 내용이 오는데 그때마다
    다시 만들면 초당 오브젝트를 지우고 세우는 일이 반복된다. 배치가 실제로 바뀌는 경우는 열쇠가
    주워지거나 흘려지는 것이고 그것이 곧 개수 변화다. 로비로 돌아가면 판별 상태를 지운다 —
    지우지 않으면 열쇠 수가 우연히 같을 때 두 번째 매치가 첫 매치의 목표물을 그대로 쓴다.
  - **씨드는 아직 나간다.** `MatchSync` 가 여전히 `PlacementSeedOverride` 를 넘기고, 그 값을
    쓰는 경로는 오프라인뿐이다. 와이어에서 빼는 것이 IG-011c3 이고 **그때 R-2.3 이 실제로 닫힌다.**
  - `MatchManager` 가 `NV.Client.Net.MapExport` 를 부른다(오프라인 격자 생성). `MatchBootstrap` 이
    이미 `NV.Client.Net.Session` 을 참조하는 것과 같은 성질이며, 같은 어셈블리다.
  - 네트워크 경로(서버 좌표로 목표물이 생기고 Seeker 화면에 문이 없는지)는 §7.4 스모크 테스트가
    필요하고 **사람의 조작을 요구한다.**
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
- 상태: **DONE** (이터레이션 15, 2026-08-04) — **R-2.3 이 닫혔다.**
- 변경 파일 (8개):
  - `Shared/Contracts/Messages/RoomStateMessage.cs` — 필드·생성자 인자 제거, `WireSize` 15 → **11**
  - `Shared/Serialization/MessageCodec.cs` — Write/Read 에서 제거
  - `Modules/Realtime/Simulation/Room.cs` — 헤더 생성에서 제거 (서버는 내부적으로 계속 갖는다)
  - `Net/Session/MatchSync.cs` — 전달·로그 제거
  - `Net/NetworkClient.cs` — `Differs` 비교 제거
  - `Net/NetworkTestUi.cs` — 씨드 표시 → 목표물 수신·문 포함 여부 표시
  - `Game/MatchManager.cs` — `PlacementSeedOverride` 주석을 사실에 맞게 (서버는 더 이상 보내지 않는다)
  - `tests/…` 4개 — `WireSizeTests`(15→11, 127→123), `CodecRoundTripTests`, `RoomTests`
- 검증 (전부 실행함):

  | 확인 | 수단 | 결과 |
  |---|---|---|
  | 서버 테스트 | `dotnet test` | ✅ **336개 통과**, 실패 0 |
  | 서버 경고 0 | `dotnet build` | ✅ 경고 0개 오류 0개 |
  | 클라이언트 컴파일 | `Assembly-CSharp` | ✅ 오류 0개 |
  | **와이어에서 사라짐** | `WireSizeTests` | ✅ `RoomStateHeader.WireSize` **11** (4바이트 감소), 8인 최대 123B |
  | **코드에 남은 참조가 정당한지** | `grep PlacementSeed` | ✅ 서버 내부 재현용(`Room._placementSeed`)과 오프라인 API(`PlacementSeedOverride`)뿐. **송신 경로에 없다** |
  | **오프라인 회귀** | Play 모드 | ✅ `seedOverride=0` 인데 열쇠 10 · 장치 9 · 제단 · 문 전부 생성, `phase=Playing` |
  | 프로토콜 게이트 | `curl "…?v=3"` | ✅ HTTP 200 |
  | 회귀 — 맵 해시 | 기동 로그 | ✅ `7996AF3A` |

- **`WireSizeTests` 가 정확히 이 변경을 잡았다.** 15바이트·127바이트를 못질한 두 테스트가
  실패했고, 그것이 그 테스트의 목적이다 — 고정부 크기가 변하면 무엇이 실리는지 확인하게 만든다.
  새 값(11·123)으로 갱신하면서 **왜 4바이트가 줄었는지**를 주석에 남겼다.
- **R-2.3 이 실제로 닫힌 근거.** Seeker 클라이언트에는 이제 문의 좌표를 얻을 경로가 둘 다 없다:
  1. `ObjectiveState` 전문의 문 블록이 빠진다(IG-011b, 바이트로 확인)
  2. 배치 씨드가 와이어에 없다(이 태스크) — 배치 함수를 갖고 있어도(ADR 0002) **계산할 입력이 없다**

  컬링 레이어는 화면에서 가릴 뿐이고 디컴파일로 되살아난다. 이 둘은 되살릴 수 없다.
- 비고:
  - `MatchManager` 에 시드 계산이 두 곳(`BeginMatch`, `PlaceObjectives`)에 적혀 있다. 각자 다른
    난수를 만들지만(`System.Random` vs `DeterministicSequence`) **계산 식이 중복**이다 →
    IG-024 로 올렸다.
  - `NetworkTestUi` 의 씨드 표시를 "목표물 수신(문 포함/문 없음)" 으로 바꿨다. 개발 패널에서
    **Seeker 화면에 "문 없음" 이 뜨는 것이 정상**임을 볼 수 있어야 한다 — 그것이 이 기능이
    동작하는 눈에 보이는 신호다.
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

### IG-012 — 열쇠 습득·삽입·문 개방·탈출 판정 (a/b/c 로 쪼갬)
- 상태: **분할됨** — IG-012a DONE, IG-012b·IG-012c TODO
- 기획서 근거: §3, §6
- 네 판정이 한 태스크에 들어가면 §6.1 의 8파일을 넘는다. 습득만으로 이미 7파일이었다.

### IG-012a — 열쇠 습득 서버 판정
- 상태: **DONE** (이터레이션 16·17)
- 기획서 근거: §3 — 열쇠 10개가 Runner 의 목표다
- 계획(실행됨): 매 틱 거리 폴링을 서버로 옮긴다. 수평 `KeyPickupRadius`, 수직은 새로 올린
  `KeyPickupHeight`. 습득한 열쇠는 `Objectives.RemoveKeyAt` 로 빠지고 두 전문이 즉시 갱신된다
  (목표물 — 맵에서 사라졌다, 매치 — 소지 수가 늘었다). 클라이언트는 `ServerOwnsObjectives`
  면 폴링을 멈추고 전문의 소지 수를 받는다.
- **범위 조정: `ButtonFlags.Interact` 를 넣지 않았다.** 기획서 §3 은 습득 방식을 정하지 않고,
  클라이언트가 쓰던 방식은 걸어가면 줍는 거리 폴링이었다(`KeyPickup.Update`). 상호작용 키는
  삽입(IG-012b)에서 필요하다 — 그쪽은 되돌릴 수 없는 행동이므로 명시적 입력을 받아야 한다.
- 변경 파일(8): `Shared/Simulation/MatchConstants.cs`(`KeyPickupHeight` 1.6m 추가),
  `Modules/Realtime/Simulation/PlayerEntity.cs`(`CarriedKeys`),
  `Modules/Realtime/Simulation/Room.cs`(`PickUpKeys`·`IsWithinPickupRange`, 전문에 소지 수,
  매치 시작 시 0 으로), `Game/KeyPickup.cs`(서버 권위면 폴링 중단 + 하드코딩 1.6f 제거),
  `Game/GameConfig.cs`(`keyPickupHeight`), `Game/PlayerAgent.cs`(`SetCarriedKeys`),
  `Game/MatchManager.cs`(`AcceptCarriedKeys`), `Net/Session/MatchSync.cs`(`ApplyCarriedKeys`)
  + 신규 `tests/Modules.Tests/Realtime/KeyPickupTests.cs`(9개)
- **오프라인 경로의 하드코딩된 1.6f 도 함께 없앴다.** 서버에 상수를 올려 두고 클라이언트가 계속
  literal 을 쓰면 "한 곳에 있다" 가 거짓이 된다 — D-7 의 소문자 프로퍼티(`keyPickupHeight`)로
  잇는다.
- 검증:
  - `dotnet test --filter "FullyQualifiedName~KeyPickupTests"` → **9/9 통과**
  - `dotnet test` → **345 통과** (Modules 341 + Architecture 4), 실패 0. 이전 336 에서 +9
  - `dotnet build` → **경고 0, 오류 0**
  - `dotnet build Assembly-CSharp.csproj` → **오류 0** (경고 2개는 기존 `System.Net.Http` 심 충돌)
  - **§7.4 스모크는 실행하지 못했다.** 두 화면에서 열쇠가 동시에 사라지고 소지 수가 한쪽만
    올라가는 것은 사람이 봐야 한다 — 아래 "사람이 해야 하는 검증" 에 적었다.
- 판정 순서: `Advance` 안에서 **이동 뒤, 매치 시계 앞**. 앞에 두면 이번 틱에 열쇠 위로 걸어간
  플레이어가 다음 틱까지 줍지 못한다.
- 열쇠 목록을 **뒤에서부터** 훑는다. `RemoveKeyAt` 이 리스트를 당기므로 앞에서부터 지우면 지운
  자리의 다음 열쇠를 한 틱 건너뛴다 — 증상이 한 틱 지연뿐이라 찾기 어려운 종류다.
- 소지 수는 **덮어쓴다**(`SetCarriedKeys`), 더하지 않는다. 전문은 누적이 아니라 현재 값이고,
  2Hz 로 같은 값이 다시 오므로 더하면 초당 두 개씩 늘어난다. `AddKeys` 는 오프라인 경로에 남는다.
- 알림("KEY (n carried)")은 `MatchManager.AcceptCarriedKeys` 가 **증가할 때만** 올린다. 전문마다
  올리면 매치 내내 0.5초에 한 번 뜬다.
- 비고: `CarryLimit` 은 0(무제한)이므로 상한 검사는 실질적으로 통과만 한다. 그래도 남겨 두었다 —
  값이 0 이 아니게 되는 날 판정이 아니라 설정만 바뀌어야 한다.

### IG-012b — 열쇠 삽입 + 문 개방 (b1/b2 로 쪼갬)
- 상태: **분할됨** — IG-012b1 DONE, IG-012b2 TODO
- 조사 결과 전체 범위가 **11파일**이었다(§6.1 초과): 입력 비트·마스크·클라이언트 래치·송신,
  서버 판정과 삽입 시각, `MatchState.keysInserted`, `ObjectiveState` 의 문 개방,
  `NetworkClient` 노출, `MatchSync` 적용, `MatchManager`·`EscapeDoor` 의 판정 경로 차단.
  입력 경로와 판정을 나누면 각각 독립적으로 검증되고, **입력 경로만으로는 기존 동작이 깨지지
  않는다**(§6.3) — 서버가 비트를 받아서 버리는 상태로 끝나기 때문이다.

### IG-012b1 — `ButtonFlags.Interact` 입력 경로
- 상태: **DONE** (이터레이션 18)
- 기획서 근거: §3(삽입), §5(장치 사용) — 둘 다 명시적 입력을 요구하는 행동이다
- 계획(실행됨): 비트를 추가하고 `All` 마스크에 넣는다. 클라이언트는 E 키를 프레임에서 래치해
  틱마다 소비한다(`ConsumeInteract`) — 점프와 같은 구조다. **판정은 넣지 않는다**; 서버는
  비트를 받아 버린다.
- **대상(무엇에 대한 상호작용인지)을 와이어에 싣지 않았다.** 클라이언트가 대상을 지정하면
  "나는 저 문을 쓴다" 를 클라이언트가 주장하는 구조가 되고, 사거리 밖의 문도 지목할 수 있다.
  서버가 자기 좌표로 대상을 고른다. 장치가 추가되는 IG-013 에서도 같은 규칙이 유지된다.
- **엣지이고 held 가 아니다.** 30Hz 틱 사이에 눌린 키를 그냥 읽으면 절반쯤 사라진다
  (`_jumpLatched` 가 있는 이유와 같다). `InputEnabled = false` 에서 래치를 비우는 것도 점프와
  같다 — 남겨 두면 UI 를 닫는 순간 눌리지 않은 키가 한 번 발동한다.
- **`MovementLocked` 로 막는다.** 체인에 끌려가는 중이거나 정지 장치에 걸린 동안 열쇠를 넣을 수
  있으면 그 벌칙이 막지 못한 유일한 행동이 된다. `PlayerInteractor`(오프라인 경로)는 이 게이트를
  거치지 않으므로 IG-012b2 에서 권위를 넘길 때 함께 정리한다.
- **`PlayerInteractor` 를 건드리지 않았다.** 지금 그쪽의 로컬 `Interact` 호출을 막으면 서버에
  판정이 없는 상태이므로 네트워크 매치에서 삽입이 아예 불가능해진다 — 기존 동작을 깨는 변경이라
  판정과 같은 태스크(IG-012b2)에 있어야 한다. 두 경로가 같은 키를 읽지만 **래치를 소비하는 것은
  와이어 경로뿐**이므로 서로의 신호를 먹지 않는다.
- 변경 파일(4): `Shared/Contracts/Enums/ButtonFlags.cs`(`Interact = 1 << 4`, `All` 갱신),
  `FirstPersonController.cs`(`_interactLatched`·`ConsumeInteract`, `InputEnabled` 해제 시 비움),
  `Net/NetworkBootstrap.cs`(`LocalInputSource.Sample` 에서 소비),
  `tests/Modules.Tests/Realtime/InputValidatorTests.cs`(+2개)
- 검증:
  - `dotnet test` → **347 통과** (Modules 343 + Architecture 4), 실패 0. 이전 345 에서 +2
  - `dotnet build` → **경고 0, 오류 0**
  - `dotnet build Assembly-CSharp.csproj` → **오류 0**
  - 기존 `미정의_버튼_비트는_제거된다`(`0xFF` → `All`)가 그대로 통과한다 — `All` 이 0x1F 로
    넓어졌을 뿐 마스크는 여전히 걸러낸다. 새 테스트가 비트 5 로 그것을 못질한다.
- **프로토콜 버전을 올리지 않는다.** 와이어 크기와 배치가 그대로다(`buttons` 는 예전부터 1바이트).
  구버전 클라이언트는 이 비트를 세우지 않고, 구버전 서버는 마스크로 지운다 — 어느 조합도 오독하지
  않는다. `ProtocolInfo.Version` 은 IG-008 이 올린 3 그대로다.

### IG-012b2 — 삽입·문 개방 서버 판정
- 상태: **DONE** (이터레이션 19)
- 기획서 근거: §3(열쇠 10개 → 문 개방), §6
- **와이어가 바뀌지 않았다.** 계획에는 `ObjectiveFlags.DoorOpen` 을 더한다고 적었지만, 조사해
  보니 **문 개방 바이트가 이미 문 블록 안에 있었다**(`WriteObjectiveState` 의 `doorOpen`).
  IG-011b 가 그 자리를 만들어 두었고, Seeker 사본에는 블록 자체가 없으므로 **별도의 필터가
  필요하지 않다** — 블록을 통째로 빼는 설계의 부수 효과다. 그래서 이 태스크는 서버 판정만으로
  끝나고 클라이언트 적용(IG-012b3)과 깔끔하게 갈렸다.
- 판정 위치: `Room.InsertKeys()`, 이동과 습득 **뒤**. 자격은 역할·소지·거리·간격 네 개다.
- **삽입이 한 곳에서 직렬화되는 것이 "두 Runner 가 동시에 10번째 열쇠를 넣는" 경우의 답이다.**
  먼저 도는 쪽이 문턱을 넘고 다음 쪽은 `_match.DoorOpen` 에서 걸린다 — 열쇠는 소비되지 않는다.
- `DoorOpen` 을 **필드로 두지 않고 삽입 수에서 유도**했다. 따로 들면 "열쇠는 10개인데 문은 닫혀
  있다" 가 표현 가능한 상태가 되고, 그 상태에 빠지는 경로를 찾는 일이 남는다.
- `MatchConstants.InteractHeight = 2.5f` 를 올렸다. 수직을 보지 않으면 **위층에서 아래층 문에
  열쇠를 넣을 수 있다** — 문은 Runner 에게만 보이지만 좌표는 그 클라이언트가 안다. 값의 출처는
  클라이언트의 `PlayerInteractor.Consider` 이고, 그쪽이 프롬프트를 띄우는 조건이었으므로
  **화면에 보이는 것과 판정이 일치하는 값**이다(AS-10). 층 간격 3.2m > 2.5m.
- 변경 파일(6): `Shared/Simulation/MatchConstants.cs`(`InteractHeight`),
  `Modules/Realtime/Simulation/Match.cs`(`KeysInserted`·`DoorOpen`·`InsertKey`·`InsertIntervalTicks`),
  `Modules/Realtime/Simulation/PlayerEntity.cs`(`InteractRequested`·`NextInsertTick`),
  `Modules/Realtime/Simulation/InputValidator.cs`(`WithoutEdgeButtons`),
  `Modules/Realtime/Simulation/Room.cs`(`InsertKeys`·`IsWithinDoorRange`, 엣지 수집, 두 전문 채우기,
  진단용 `MatchKeysInserted`·`MatchDoorOpen`)
  + 신규 `tests/Modules.Tests/Realtime/KeyInsertTests.cs`(13개)
- 검증:
  - `dotnet test --filter "FullyQualifiedName~KeyInsertTests"` → **13/13 통과**
  - `dotnet test` → **360 통과** (Modules 356 + Architecture 4), 실패 0. 이전 347 에서 +13
  - `dotnet build` → **경고 0, 오류 0** / `Assembly-CSharp` → **오류 0**
  - **§7.4 스모크 미실행** (사람이 해야 하는 검증 표에 추가)
- **자기 검증에서 내 주석 하나가 틀린 것을 잡았다.** `WithoutEdgeButtons` 를 "이것이 없으면 열쇠가
  저절로 들어간다" 로 적었는데, 실제로 그 줄을 `= frame` 으로 되돌려도 13개가 그대로 통과한다.
  상호작용 요청을 세우는 곳이 **새 입력 갈래뿐**이고 반복 갈래는 `Simulate` 만 부르기 때문이다.
  코드는 남기고(불변식이 두 곳의 협조에 의존하므로) 주석과 테스트 이름을 사실에 맞게 고쳤다 —
  그 검사가 지키는 것은 스트립이 아니라 "반복 갈래는 버튼을 읽지 않는다" 쪽이다.
- 비고: `Jump` 도 엣지지만 지금도 반복된다. 접지 검사가 대부분 걸러 무해하나 착지 순간에 재점프가
  가능한 구조다 — 이동 동작을 바꾸는 별개의 변경이므로 **IG-025** 로 올렸다.

### IG-028a — 탄약을 와이어에 싣고 HUD 에 적용
- 상태: **DONE** (이터레이션 28)
- 기획서 근거: §4.3 (탄창 3발), §2.1 (역할별 정보)
- **R-9.3 이 닫혔다.** 판정은 IG-014a 부터 서버였지만 HUD 가 로컬 `WeaponController._ammo` 를
  그리고 있었다 — "서버 권위" 와 "화면에 도달함" 이 다른 마지막 자리였다.
- **와이어 크기를 늘리지 않았다. 죽은 바이트를 썼다.** `MatchParticipant.Flags` 는 IG-014b 에서
  "출혈·탈출·쓰러짐은 매 틱 스냅샷으로 가야 하므로 여기 싣지 않는다" 고 결정한 뒤 **영구히 0**
  이었고, 프로덕션에서 아무도 읽지 않았다(테스트 왕복 비교 한 곳뿐). `Flags` → `Ammo` 로 바꿨다.
- **프로토콜 버전을 올리지 않았다.** 크기(5B)와 배치가 그대로이고, v3 는 아직 어디에도 배포되지
  않았으며 루트 `CLAUDE.md` 가 "이 시리즈에서 버전은 한 번만 올린다" 고 정하고 있다. 대가는
  **`WireSizeTests` 가 이 변경을 잡지 못한다는 것**(크기가 같다) — 그래서 바이트 위치를 직접
  비교하는 테스트로 못질했다.
- **필터가 양방향이 됐다.** 열쇠 진행도·소지 열쇠는 **Seeker 사본에서** 지워지고, 탄약은
  **Runner 사본에서** 지워진다 — 술래만 총을 들고(기획서 §2.1), 남은 탄을 정확히 아는 것은
  Runner 에게 주어지지 않은 정보다. **총성이 "한 발 줄었다" 를 알려 주는 것이 이 게임이 그 정보를
  전달하는 방식이고**, 숫자를 주면 그것을 무료로 넘긴다.
- 변경 파일(7): `Shared/Contracts/Messages/MatchStateMessage.cs`(`Flags` → `Ammo`),
  `Shared/Serialization/MessageCodec.cs`(`hideAmmo` 필터),
  `Modules/Realtime/Simulation/Room.cs`(전문에 탄약),
  `tests/.../Serialization/MatchStateCodecTests.cs`(+2, 바이트 비교 테스트 갱신),
  `WeaponController.cs`(`AcceptAmmo`), `Game/MatchManager.cs`(`AcceptAmmo`),
  `Net/Session/MatchSync.cs`(적용)
- 검증:
  - `dotnet test` → **396 통과** (Modules 392 + Architecture 4), 실패 0. 이전 394 에서 +2
  - `dotnet build` → 경고 0, 오류 0 / `Assembly-CSharp` → **오류 0**
  - **기존 테스트 하나가 정확히 옳은 이유로 실패했다** — `두_사본의_바이트가_열쇠_자리에서만_다르다`
    는 필터가 한 방향이라는 전제를 못질하고 있었다. 이제 세 자리에서 다르므로
    `두_사본의_바이트는_필터_자리에서만_다르다` 로 바꾸고 탄약 자리를 추가했다.
- **알려진 연출 artifact:** 로컬 `Fire()` 가 예측으로 탄약을 먼저 줄이므로, 서버가 거부한 발사는
  **탄피 아이콘이 0.5초 뒤 다시 켜지는 것**으로 보인다. 로컬 감소를 없애면 트리거 반응이 2Hz 로
  느려지므로 예측을 남겼다.

### IG-028b1 — 발사 알림 (서버 + 와이어)
- 상태: **DONE** (이터레이션 33)
- **ADR 0003 을 먼저 썼다.** 이것은 이 프로젝트의 **첫 알림**이다 — 지금까지 모든 서버 발신
  메시지는 멱등한 전문이었고, 그 규칙에는 이유가 있었다(세션 채널이 `Bounded(32, DropOldest)` 라
  한 번짜리 메시지는 사라질 수 있다). **발사는 상태가 아니라 사건**이므로 그 틀에 맞지 않는다.
- **예외를 좁게 정의했다:** "알림을 써도 된다" 가 아니라 **"결과가 전문으로 따라오는 사건에만
  알림을 쓴다"**. 놓쳤을 때 **틀린 상태가 남는가**를 물으면 답이 나온다 — 발사를 놓치면 잃는 것은
  예광탄 하나이고, 피격·사망·탄약은 전부 전문으로 온다.
- 거부한 대안(ADR 에 근거 기록): **(A) 총알 상태를 스냅샷에 매 틱** — 32슬롯이 비싸고 스냅샷은
  엔티티의 것이며 `EntityFlags` 는 8비트를 다 썼다. **(B) "최근 발사 목록" 전문** — 반복되는 발사가
  두 번 그려지고, 중복 제거를 넣는 순간 멱등성의 이득이 사라진다. 2Hz 면 예광탄이 60m 늦는데
  **늦은 예광탄은 없는 예광탄보다 나쁘다**(실제 탄도와 다른 곳을 가리킨다).
- 페이로드는 **발사체 상태가 아니라 초기 조건**이다(17B): 사수·시작점·요·피치·틱. 비행은
  클라이언트가 재현한다 — 등속 직선이고 중력이 0 이므로 정확하다.
  - **방향을 벡터가 아니라 요·피치로** 싣는다: 수신 측이 `PlayerMovement.Forward` 로 같은 벡터를
    만들어 요 규약이 한 곳에만 남고 6B 가 4B 가 된다.
  - **시작점을 싣는다**: 클라이언트가 아는 사수 위치는 보간된 100ms 과거다.
  - **틱을 싣는다**: 이벤트가 한 RTT 늦게 도착하므로 그 사이 총알이 간 거리를 건너뛰어야 한다.
- **역할 필터가 없다.** 총성이 이미 술래의 위치를 알려 주므로(그것이 `WeaponAudio` 감쇠의 설계
  의도다) 예광탄을 숨기면 **소리는 들리는데 궤적이 없는** 상태가 되어 소리의 정보만 줄어든다.
- 변경 파일(7): 신규 `docs/adr/0003-fire-event-is-a-notification.md`,
  `Shared/Contracts/Enums/EventKind.cs`(`FireEvent = 4`),
  신규 `Shared/Contracts/Messages/FireEventMessage.cs`,
  `Shared/Serialization/MessageCodec.cs`(`WriteFireEvent`·`ReadFireEvent`),
  `Modules/Realtime/Simulation/Room.cs`(`QueueFireEvent`·`BroadcastFireEvents`, 매치 시작 시 비움),
  신규 `tests/Modules.Tests/Realtime/FireEventTests.cs`(9개),
  `NVproject/Shared.csproj`(`Compile Include` — 아래 참조)
- 검증:
  - `dotnet test --filter "FullyQualifiedName~FireEventTests"` → **9/9 통과**
  - `dotnet test` → **409 통과** (Modules 405 + Architecture 4), 실패 0. 이전 400 에서 +9
  - `dotnet build` → 경고 0, 오류 0 / `Assembly-CSharp` → 오류 0
  - **핵심 검사는 `다음_틱에는_다시_오지_않는다`** — 200틱(전문 주기 15틱, 목표물 주기 150틱을
    모두 넘김)을 돌려도 수가 1 이다. 전문이면 늘어난다. 이것이 "알림" 의 정의다.
  - `시작점이_총알과_같다` 는 알림과 실제 탄도가 같은 지점에서 출발함을 확인한다
- **프로토콜 버전을 올리지 않았다.** 새 kind 이므로 기존 메시지의 크기·배치가 그대로이고, 모르는
  kind 는 클라이언트의 `DispatchEvent` 가 `default` 로 무시한다 — 구버전 클라이언트는 예광탄만
  못 본다.
- **`NVproject/Shared.csproj` 에 `Compile Include` 를 손으로 추가해야 했다.** `NVproject/CLAUDE.md`
  가 기록해 둔 트랩이다 — 새 `.cs` 는 Unity 가 생성한 프로젝트의 목록에 없어 **자기 네임스페이스에서
  먼저 실패**한다. 서버 테스트는 전부 통과하는데 클라이언트만 깨지므로 원인이 엉뚱해 보인다.
- 비고: 새 `Shared` 파일은 Unity 에디터가 열릴 때 `.meta` 를 생성한다(`Shared/*.meta` 는 의도적으로
  커밋된다). 이 커밋에는 `.meta` 가 없으므로 **에디터를 한 번 열어 커밋해야 한다.**

### IG-028b2 — 클라이언트가 발사 알림으로 예광탄을 그린다
- 상태: **DONE** (이터레이션 34)
- **남이 쏜 예광탄이 처음으로 보인다.** 지금까지 각 클라이언트는 자기 총알만 그렸다.
- **자기 발사는 알림으로 그리지 않는다.** 로컬 `Bullet` 이 트리거를 당긴 프레임에 이미 예광탄을
  만들고 **히트마커·발사음·반동의 타이밍도 함께 만든다.** 서버 알림으로 갈아타면 자기 사격의
  반응이 한 왕복만큼 늦어지는데, §8 이 로컬 연출에 예측을 허용하는 것이 정확히 그 이유다.
  대가는 히트마커가 서버 판정과 어긋날 수 있다는 것이고 **그것은 판정이 아니라 표시다.**
- **알림의 틱으로 앞으로 건너뛰지 않았다 — 이것이 이번의 판단이다.** 늦게 도착한 만큼 총알을
  진행시키는 것이 "정확해" 보이지만, **원격 몸은 보간 때문에 100ms 과거에 그려진다.** 예광탄만
  현재로 당기면 **그것을 쏜 몸의 총구와 어긋난다** — 총구에서 나가지 않는 예광탄이 된다.
  원격 표현 전체가 같은 만큼 과거에 있는 편이 일관되다. 틱은 그 보정을 원하는 클라이언트를 위해
  와이어에 남아 있고, **지금 클라이언트는 일관성을 택했다**(ADR 0003 이 실은 이유와 다르지 않다 —
  거기서도 "쓸 수 있게 둔다" 였다).
- 방향은 `PlayerMovement.Forward` 로 만든다 — **서버가 총알을 만들 때 쓴 것과 같은 함수**이므로
  예광탄이 실제 탄도와 같은 쪽으로 간다. 요 규약을 두 곳에 두지 않는 규칙의 연장이다.
- `FireObserved` 는 **이 클라이언트의 유일한 이벤트 기반 수신이다.** 다른 전문은 "지금 상태" 라
  폴링이 낫지만 발사에는 읽을 현재 값이 없다 — 폴링하려면 큐를 만들어야 하고 그것은 이벤트를
  손으로 다시 만드는 것이다.
- 변경 파일(2): `Net/NetworkClient.cs`(`FireObserved`·`ReadFireEvent`),
  `Net/NetworkBootstrap.cs`(`OnFireObserved` — 예광탄 생성)
- 검증:
  - `dotnet build Assembly-CSharp.csproj` → **오류 0**
  - `dotnet test` → **409 통과** (서버 무변경, 회귀 확인)
  - **자동 테스트 없음** — 클라이언트 전용(IG-018 미비). 와이어 쪽은 `FireEventTests` 9개가 고정한다.
  - **§7.4 스모크가 이 태스크의 실질 검증이다** — 사람 검증 표에 추가했다.
- 비고: 데미지 0 으로 만든다. `Bullet` 의 `OnHit` 는 `ReportHit` 로 가고 그쪽이
  `ServerOwnsCombat` 에서 거부하므로 판정을 만들지 않는다. 마스크에서 뷰모델 팔(레이어 8)만
  빼서 몸에 맞으면 멈추게 두었다 — 서버가 맞히지 않은 몸에서 멈출 수 있지만, 벽을 통과하는
  예광탄보다 낫다.
- 지금 각 클라이언트는 **자기 총알만** 그린다(`Bullet`, 로컬 발사 시점에 생성). 서버가 날리는
  총알은 화면에 없다. 그래서 남이 쏜 예광탄이 보이지 않고, 히트마커가 서버 판정과 어긋날 수 있다.
- 필요한 것: 스냅샷에 활성 총알(위치·방향 또는 시작점·틱)을 싣고, 클라이언트가 그것으로 예광탄을
  그린다. 8인 룸에서 총알 32개면 대역폭이 늘어나므로 **무엇을 싣는지가 설계 판단**이다 —
  발사 이벤트(시작점·방향·틱)만 보내고 비행은 클라이언트가 재현하는 편이 훨씬 싸다.
- 그렇게 하면 로컬 총알도 같은 경로로 그려져 히트마커 불일치가 사라진다.
- ~~탄약 HUD 도 여기 걸린다~~ → **IG-028a 가 먼저 해결했다.** 죽은 `Flags` 바이트를 탄약으로
  바꿔 크기도 버전도 그대로다.

### IG-027 — 사망 시 흘린 열쇠를 흩뿌리기 (표현)
- 상태: **DONE** (이터레이션 32)
- IG-014b 는 흘린 열쇠를 사망 지점 **한 점**에 놓았다. 규칙("사망 지점에 흘린다")에는 맞지만 한
  무더기는 밟은 Runner 가 한 틱에 전부 줍고 시각적으로도 하나처럼 보인다.
- `MatchConstants.KeyDropRadius = 0.7f`(클라이언트 `ScatterKeys` 의 값, AS-16).
- **클라이언트와 다르게 구현했고 그것이 의도다.** 클라이언트는 각 열쇠의 각도를 따로 뽑는데
  그러면 **각도가 겹쳐 두 열쇠가 같은 자리에 놓일 수 있다** — 흩뿌리는 목적이 겹침을 피하는
  것이므로 겹침이 곧 실패다. **원 위에 균등 배분하고 시작 각도만 무작위로 뽑아** 겹침을
  불가능하게 만들었고, 난수 draw 도 하나로 줄었다.
- **격자에 스냅하지 않는다.** `TryNearestFreeFloor` 는 셀 중심을 돌려주므로(AS-7) 반경 0.7m 안의
  후보가 전부 같은 셀 중심으로 모여 **다시 한 점이 된다** — 스냅이 흩뿌림을 무효로 만든다.
  스냅 없이 안전한 이유는 **습득 반경(1.4m) > 흩뿌림 반경(0.7m)** 이라는 관계다: 벽 쪽으로 밀린
  열쇠도 사망 지점에 서서 그대로 주울 수 있다. **그 관계를 테스트로 못질했다.**
- 난수는 피격 순간이동과 같은 수열(`_hitRandom`)을 쓴다. IG-014b 가 "용도별로 수열을 나눈다" 고
  적었지만 그 근거는 "한쪽의 draw 수 변경이 다른 쪽을 바꾼다" 였고, **둘 다 같은 판정(`ApplyHit`)의
  결과이므로 독립적으로 재현되어야 할 이유가 없다.** 세 번째 수열은 과한 분리다.
- 변경 파일(3): `Shared/Simulation/MatchConstants.cs`(`KeyDropRadius`),
  `Modules/Realtime/Simulation/Room.cs`(`ScatterKeys`),
  `tests/Modules.Tests/Realtime/HitTests.cs`(+2)
- 검증:
  - `dotnet test` → **400 통과** (Modules 396 + Architecture 4), 실패 0. 이전 398 에서 +2
  - `dotnet build` → 경고 0, 오류 0 / `Assembly-CSharp` → 오류 0
  - `흘린_열쇠는_서로_다른_자리에_놓인다` 는 **이전 구현에서 실패한다**(모두 같은 좌표였다) —
    개수만 세던 기존 검사로는 구별할 수 없던 것을 잡는다
  - `흩뿌림_반경은_습득_반경보다_작다` 는 스냅을 생략한 근거를 단정문으로 고정한다

### IG-026 — 오프라인 상호작용에 이동 잠금 게이트를 맞춘다 (원제: E 키를 `ConsumeInteract` 로 통합)
- 상태: **DONE** (이터레이션 31)
- **수단을 바꿨다. 목적은 그대로다.** 태스크는 "두 경로가 같은 키 읽기를 쓰게 한다" 로 적혀
  있었지만, 실제 결함은 **`MovementLocked` 게이트가 와이어 경로에만 있는 것**이었다. 게이트만
  맞추면 목적이 달성되고 래치 소유권을 건드리지 않는다.
- **래치 통합을 거부한 이유:** `ConsumeInteract` 는 지금 **소비자가 정확히 하나**다(와이어 경로).
  `PlayerInteractor` 도 소비하게 하면 조건이 겹치는 순간 한쪽이 다른 쪽의 신호를 먹는다 —
  IG-012b1 이 일부러 피한 바로 그 위험이고, 조건이 오늘 겹치지 않는다는 것은 **네트워크 상태에
  대한 결합**을 새로 만들어야 성립한다. 그리고 래치가 해결하는 문제(30Hz 틱과 프레임의 불일치)는
  **오프라인에 존재하지 않는다** — `Update` 에서 `wasPressedThisFrame` 은 이미 정확한 엣지다.
- **기획서가 게이트의 타당성을 답했다(§6.4 적용 불필요).** §4.3 은 체인 벌칙을 "3초 **행동 불가**"
  로, §5.1 은 정지 장치를 "**전체 정지**" 로 적는다 — 장치 사용은 행동이므로, 계속 쓸 수 있게
  두면 그 벌칙은 걷는 것만 막는 벌칙이 된다. **모호하지 않으므로 OQ 로 올리지 않았다.**
- 프롬프트도 함께 사라진다(게이트가 `FindTarget` 앞이므로 `Prompt` 가 null 로 남는다). 쓸 수 없는
  동안 "[E] INSERT KEY" 를 띄우는 것은 IG-012c1 이 고친 "표시된 자리에 서 있는데 아무 일도 안
  일어난다" 와 같은 종류의 거짓말이다.
- 변경 파일(1): `Game/PlayerInteractor.cs`
- 검증:
  - `dotnet build Assembly-CSharp.csproj` → **오류 0**
  - `dotnet test` → **398 통과** (서버 무변경, 회귀 확인)
  - **자동 테스트 없음** — 클라이언트 전용(IG-018 미비). 오프라인 확인 절차: 술래로 3발을 비워
    체인에 끌려가는 3초 동안 장치 앞에서 E 를 눌러 프롬프트가 없고 발동하지 않는지 본다
    (디버그 키 F1 로 술래로 전환).

### IG-024 — `MatchManager` 의 씨드 계산 중복 정리
- 상태: **DONE** (이터레이션 30)
- **"중복"이 실제 결함이었다.** 같은 5줄 씨드 식이 `BeginMatch`(234)와 `PlaceObjectives`(824)에
  각각 있었고, **`PlaceObjectives` 는 `BeginMatch` 안에서 호출된다.** 기본 설정
  (`GameConfig.placementSeed == 0`)에서는 둘 다 `Environment.TickCount` 로 떨어지는데 **그 값은
  두 읽기 사이에 증가한다** — 목표물 배치를 돌리는 `DeterministicSequence` 와 순간이동·열쇠
  산포를 돌리는 `System.Random` 이 **서로 다른 씨드**를 받고 있었다.
- 서로 다른 것을 먹이므로 눈에 보이는 고장은 없었다. 깨진 것은 **"한 매치 한 씨드"** 라는 불변식
  이고, 그래서 `PlacementSeedOverride` 로 배치를 재현하려는 시도가 **절반만 동작**했다 —
  목표물은 재현되지만 순간이동 지점은 매번 달랐다.
- 수정: `ResolvePlacementSeed()` 하나로 모으고 **`BeginMatch` 가 한 번 계산해
  `PlaceObjectives(seed)` 로 넘긴다.** 값을 인자로 만들어 그 상태를 표현할 수 없게 했다.
- **`PlacementSeedOverride` 는 지우지 않았다.** 쓰는 곳이 없어 죽은 것처럼 보였지만, 문서가 이미
  "아무도 쓰지 않는다 — 테스트에서 특정 배치를 재현할 때 설정한다" 고 밝힌 **의도된 수동 훅**이고
  ScriptableObject 를 런타임에 쓰지 않는 이유도 함께 적혀 있었다. 문서화된 어피던스를 지우는 것은
  요청되지 않은 제거다(§9).
- 변경 파일(3): `Game/MatchManager.cs`, `NVproject/CLAUDE.md`(내가 IG-019 에 쓴 부정확한 주장
  정정 — "오프라인에서 `placementSeed` 가 이것을 먹인다" 는 틀렸다. 그 둘은 우선순위가 다른
  별개의 입력이다), `NVserver/docs/conventions.md`(일반 규칙으로 승격)
- 검증:
  - `dotnet build Assembly-CSharp.csproj` → **오류 0**
  - `dotnet test` → **398 통과** (서버 무변경, 회귀 확인)
  - `grep Environment.TickCount` → **한 곳**(`ResolvePlacementSeed`)만 남았다
  - **자동 테스트는 없다** — 클라이언트 전용이고 EditMode 인프라가 없다(IG-018). 다만 이 결함은
    구조로 막았다: 씨드가 인자이므로 두 번 계산하는 코드를 쓸 수 없다.

### IG-025 — `Jump` 엣지가 입력 반복에 실리는 문제
- 상태: **DONE (결함 아님으로 종결)** (이터레이션 29)
- **내가 IG-012b2 에 적은 "착지 순간에 재점프가 성립한다" 는 틀렸다.** 수치를 보면 성립할 수
  없다 — 반복 구간은 `MaxInputRepeatTicks` 3틱(0.1초)이고 체공은 `2·JumpSpeed/Gravity` =
  0.7초(21틱)다. **반복 구간 전체가 공중이므로** 반복된 `Jump` 는 전부 접지 검사에 걸린다.
  그 뒤에는 `InputValidator.Neutral` 이 버튼을 비운다.
- **지우지 않기로 한 이유가 하나 더 있다.** 공중에서 누른 점프가 3틱 재시도되므로 **착지 직전의
  입력이 착지와 함께 발동한다** — 의도한 것은 아니지만 점프 버퍼로 동작하고, 그것은 조작감에서
  얻는 쪽이다. 지우면 그 3틱을 잃는다.
- **실제 위험은 다른 데 있었다: 그 관계가 코드에 적혀 있지 않았다.** 지연 보상을 위해 반복 상한을
  30틱으로 올리는 변경은 자연스러운데, 그러면 착지가 반복 구간에 들어와 **한 번의 키 입력이 연속
  점프가 된다.** 그래서 **두 상수의 대소 관계를 단정문으로 고정했다.**
- 변경 파일(3): 신규 `tests/Modules.Tests/Realtime/JumpRepeatTests.cs`(2개 — 관계 단정 +
  "입력 한 번은 점프 한 번"), `Modules/Realtime/Simulation/InputValidator.cs`(거짓 주석 정정),
  `NVserver/docs/conventions.md`(같은 정정 + 일반 규칙으로 승격)
- 검증:
  - `dotnet test` → **398 통과** (Modules 394 + Architecture 4), 실패 0. 이전 396 에서 +2
  - `dotnet build` → 경고 0, 오류 0
  - `입력_한_번은_점프_한_번이다` 는 상승 구간을 세어 정확히 1 임을 확인한다(0 이면 점프가 아예
    없었다는 뜻이므로 그것도 잡힌다)
- **정직한 한계:** 오늘의 수치에서는 `Jump` 를 지워도 이 테스트들이 통과한다 — 반복이 착지에
  닿지 않으므로 두 구현이 구별되지 않는다. **그래서 load-bearing 인 것은 관계를 단정하는 첫
  테스트다.** 두 번째는 점프 자체가 동작한다는 것만 확인한다.

### IG-012b3 — 클라이언트 삽입 적용 + 로컬 판정 차단
- 상태: **DONE** (이터레이션 20)
- 기획서 근거: §3, §6
- **R-6.2·R-6.5 가 이것으로 닫힌다.** 서버 판정(b2)과 클라이언트 적용(b3)이 붙어 두 심판이
  병행하던 상태가 끝났다.
- 적용 경로: `NetworkClient.ObjectiveDoorOpen` → `MatchSync.ApplyMatchState` →
  `MatchManager.AcceptObjectiveProgress(keysInserted, doorOpen)`.
- **차단은 `MatchManager.TryInsertKey` 한 곳에서 한다.** 호출부(`EscapeDoor.Interact`,
  `PlayerInteractor`)를 건드리지 않은 것이 의도다 — 규칙을 판단하는 곳이 하나라는 이 프로젝트의
  구조를 유지하고, `Interact` 는 원래부터 **요청**이었다. 프롬프트는 그대로 뜬다: 플레이어가
  무엇을 할 수 있는지는 바뀌지 않았고 **누가 판정하는지**만 바뀌었다.
- **`ServerPlacesObjectives` → `ServerOwnsObjectives` 로 이름을 바꿨다.** IG-012a 부터 이 플래그가
  배치가 아니라 **판정**을 가르고 있었다(습득 폴링 차단). 권위를 가리키는 이름이 실제와 다르면
  이 코드베이스에서 가장 위험한 종류의 거짓말이 된다. 기계적 변경 3곳(`MatchManager`,
  `KeyPickup`, `MatchSync`)이고 프로퍼티라 씬 직렬화에 영향이 없다.
- **문 개방을 `keysInserted` 에서 유도하지 않고 전문 값을 쓴다.** 유도가 더 짧지만, 문 오브젝트를
  다시 세우는 경로(`AcceptObjectiveState` → `BuildObjectiveObjects`)가 있으므로 **열린 뒤에 다시
  세운 문이 잠긴 채로 돌아오는** 경우가 생긴다. 매 프레임 멱등하게 다시 적용하는 쪽이 그 순서
  문제를 아예 없앤다 — 문이 아직 없으면 넘기고 다음 폴링에 다시 온다.
- **열쇠 한 개마다 뜨던 알림("KEY IN 7/10")은 네트워크 경로에서 올리지 않는다.** 전문은 *누가*
  넣었는지 말하지 않으므로 전원에게 띄우면 오프라인 게임이 알려 주지 않던 것을 알려 주게 된다.
  HUD 의 열쇠 슬롯이 `KeysChanged` 로 진행도를 이미 보여 주고, 문 개방은 전원의 소식이므로 띄운다.
- 변경 파일(4): `Net/NetworkClient.cs`(`ObjectiveDoorOpen`),
  `Game/MatchManager.cs`(`AcceptObjectiveProgress`, `TryInsertKey` 차단, 이름 변경),
  `Net/Session/MatchSync.cs`(적용 호출), `Game/KeyPickup.cs`(이름 변경만)
- 검증:
  - `dotnet build Assembly-CSharp.csproj` → **오류 0**
  - `dotnet test` → **360 통과** (서버 무변경, 회귀 확인)
  - `grep ServerPlacesObjectives` → 코드에 0건 (남은 것은 이름 변경을 설명하는 주석 한 줄)
  - **이 태스크에는 자동 테스트가 없다.** 전부 클라이언트 코드이고 EditMode 인프라가 아직
    없다(IG-018). §7.2 를 만족하지 못하는 상태이며, **실질적인 검증은 2클라이언트 스모크**다 —
    그래서 아래 표의 IG-012b2·b3 항목이 지금 가장 값이 큰 사람 검증이다.
- 남긴 것: `PlayerInteractor` 의 E 키 직접 읽기를 `ConsumeInteract` 로 옮기지 않았다. 옮기면
  오프라인에도 `MovementLocked` 게이트가 걸려 두 경로가 일치하지만, **오프라인 동작을 바꾸는
  변경**이라 이 태스크의 검증 범위를 넘는다 → IG-026.

### IG-012c1 — 탈출 서버 판정
- 상태: **DONE** (이터레이션 21)
- 기획서 근거: §3 — 열린 문간에 머물면 빠져나간다
- 판정: `Room.TickEscapes()`, 이동·습득·삽입 뒤. 문이 열려 있고 Runner 이고 문 반경 안에서
  `Match.EscapeHoldTicks`(24틱) **연속**으로 머물면 `Escaped`.
- **층 허용치를 새로 만들지 않고 `InteractHeight` 를 재사용했다.** 클라이언트는 두 값을 달리
  썼다 — 삽입 프롬프트가 뜨는 조건은 2.5m(`PlayerInteractor.Consider`), 탈출 판정은 2.0m
  (`TickEscapes`). 그 0.5m 는 **"서 있으라고 표시된 자리에 서 있는데 아무 일도 안 일어나는"
  구간**이었다. 같은 질문(문 앞에 있는가)에 같은 답을 쓴다(AS-11). 탈출 판정이 0.5m 관대해지는
  변화가 있고, 층 간격 3.2m 보다는 여전히 작아 층 분리는 유지된다.
- **유지는 연속이다.** 문에서 벗어나면 0 으로 돌아간다 — 누적이면 문 앞을 스쳐 지나가는 것만으로
  탈출이 성립하고, `EscapeHoldTime` 이 만들려던 "Seeker 가 끊을 수 있는 순간" 이 사라진다.
- **몸을 지우지 않는다.** `EntityFlags.Escaped` 만 세운다 — 클라이언트가 감추고
  (`PlayerAgent.SetPresent(false)`), 승리 조건이 아직 명단을 세어야 한다. 서버에서 빼면 전멸
  판정이 탈출을 사망으로 셀 수 있다. 대신 습득·삽입 판정에서 제외한다.
- **승리 판정은 하지 않는다.** `Match.RegisterEscape` 는 세기만 한다 — `EscapesToWin` 2명은
  2인 매치에서 불가능하고(OQ-6) 전멸 승리의 유무도 미정이다(OQ-2). IG-007 이 이 값을 읽는다.
- 변경 파일(5): `Modules/Realtime/Simulation/Match.cs`(`Escapes`·`EscapeHoldTicks`·`RegisterEscape`),
  `Modules/Realtime/Simulation/PlayerEntity.cs`(`Escaped`·`EscapeHoldTicks`),
  `Modules/Realtime/Simulation/Room.cs`(`TickEscapes`, 판정 제외, `Escaped` 플래그, 전문 필드,
  **`ProjectWire` 분리**, 진단 3개를 `internal Match` 하나로 정리),
  `tests/Modules.Tests/Realtime/KeyInsertTests.cs`(검사 순서),
  + 신규 `tests/Modules.Tests/Realtime/EscapeTests.cs`(12개)
- 검증:
  - `dotnet test --filter "FullyQualifiedName~EscapeTests"` → **12/12 통과**
  - `dotnet test` → **372 통과** (Modules 368 + Architecture 4), 실패 0. 이전 360 에서 +12
  - `dotnet build` → **경고 0, 오류 0** / `Assembly-CSharp` → **오류 0**
  - **§7.4 스모크 미실행** (사람 검증 표에 추가)
- **테스트가 실제 결함 하나를 잡았다 — 와이어 상태의 한 틱 지연.** `MatchFlagsFor` 를
  `StepPlayer` 안에서 계산하고 있어서, 같은 틱의 판정이 세운 플래그가 **다음 틱 스냅샷에나**
  나갔다. 탈출이 33ms 늦게 보이는 정도지만 `Bleeding`(IG-014)도 같은 자리를 쓸 예정이었다.
  `ProjectWire` 를 판정 뒤의 별도 루프로 분리했다 — **틱 N 의 스냅샷은 틱 N 이 끝난 상태여야
  한다.** 8명 루프 한 번이 비용이다.
- **테스트 하나는 내 테스트가 틀렸다.** `Seeker는_탈출하지_않는다` 가 탈출 수로 확인하려 했는데,
  픽스처의 두 스폰이 2m 간격이고 문 반경이 2.2m 라 **Seeker 스폰에 놓은 문이 Runner 의 스폰도
  덮는다** — 그 Runner 가 나가면서 수가 1 이 됐고 그것은 맞는 동작이다. 그 Seeker 자신의
  `Escaped` 비트를 보도록 고쳤다.
- **기존 테스트 하나가 새 규칙과 부딪쳤다.** `열린_문에는_더_넣지_않는다` 가 문이 열린 뒤 36틱을
  돌린 다음 소지 열쇠를 확인했는데, 그 사이에 탈출(24틱)이 성립해 0 이 됐다. 회귀가 아니라 규칙이
  정확히 발동한 것이므로 **검사 순서**를 고쳤다(삽입 직후에 소지 수를 본다).

### IG-012c2 — 클라이언트 탈출 적용
- 상태: **DONE** (이터레이션 22)
- 기획서 근거: §3
- **R-6.7 의 탈출 감지 절반이 이것으로 닫힌다.** 승리 판정은 여전히 IG-007(BLOCKED).
- **수와 대상이 서로 다른 경로로 온다.** `MatchState.escapes` 는 몇 명인지만 말하고 누구인지는
  말하지 않는다 — 몸을 감추려면 대상을 알아야 하므로 **대상은 스냅샷의 `EntityFlags.Escaped`**
  에서 읽는다. 이것이 이 태스크의 유일한 설계 판단이고, IG-012c1 이 고친 "플래그의 한 틱 지연"
  이 여기서 값을 한다 — 플래그가 늦으면 몸이 늦게 사라진다.
- 플래그는 **보간하지 않은 최신 값**을 읽는다(`TryLatest`). 보간은 위치를 위한 것이고 플래그를
  섞으면 두 스냅샷 사이에서 켜졌다 꺼졌다 한다.
- **탈출 알림에 숫자를 넣지 않았다.** 플래그는 30Hz 스냅샷으로, 수는 2Hz 전문으로 오므로 몸이
  사라지는 순간의 `Escapes` 는 최대 0.5초 뒤처져 있다 — 방금 나간 Runner 에게 "0/2" 를 띄우게
  된다. HUD 의 탈출 카운터가 권위 값을 보여 주므로 일시적 메시지가 그것을 나쁘게 중복할 필요가
  없다(IG-012b3 의 열쇠 알림과 같은 결론, 다른 이유).
- `ApplyCarriedKeys` 를 `ApplyParticipants` 로 넓혔다 — 참가자를 두 번 훑지 않는다.
- 변경 파일(2): `Game/MatchManager.cs`(`AcceptEscapes`·`AcceptEscaped`, `TickEscapes` 차단),
  `Net/Session/MatchSync.cs`(`ApplyParticipants`·`ApplyEscaped`)
- 검증:
  - `dotnet build Assembly-CSharp.csproj` → **오류 0**
  - `dotnet test` → **372 통과** (서버 무변경, 회귀 확인)
  - **자동 테스트 없음** — IG-012b3 와 같은 이유(클라이언트 전용, EditMode 인프라 없음).
    실질 검증은 2클라이언트 스모크다.

### IG-013 — `Interact` 입력 + 장치 사용 판정
- 상태: **BLOCKED** (OQ-1)
- 기획서 근거: §5.1, §5.2, §5.3
- 차단 사유: 기획서 §5.2 는 1:1 순간이동을 **"술래 전용 장치"**(다회, 쿨타임 12초)로 명시한다.
  룰셋(`ruleset.md:70,76`)은 같은 장치를 **"shared across all Runners"** 로 서술하고, 구현은
  `GameConfig.seekerCanActivateDevices: 0` 으로 **술래의 장치 사용을 아예 금지**한다. 기획서가
  SSOT 1순위이므로 기획서가 이기지만, 기획서를 따르면 장치 시스템의 접근 제어가 뒤집힌다
  (술래가 장치를 쓸 수 있어야 하고, 12초 쿨다운의 공유 범위가 달라진다). 게임플레이 영향이
  크므로 §6.4 에 따라 추측하지 않는다.

### IG-014 — 서버 발사체 + 피격 규칙 + 탄약 (a/b/c 로 쪼갬)
- 상태: **분할됨** — IG-014a DONE, b·c TODO. 아래 원래 계획이 세 태스크의 근거다.

### IG-014a — 서버 발사체 + 탄약
- 상태: **DONE** (이터레이션 23)
- 기획서 근거: §4.3 (탄창 3발), §2.1 (판정은 서버)
- **`Shared` 에 레이캐스트를 추가하지 않았다 — 이미 있었다.** `Raycaster.RayAabb` 와
  `CollisionWorld.Raycast` 가 테스트까지 갖춘 채로 들어 있었고(`CollisionTests`), 아무도 부르지
  않고 있었다. 계획서가 "스윕 판정을 만든다" 로 적혀 있었지만 만들 것은 **호출자**뿐이었다.
- 판정 순서: `StepProjectiles()` → `FireWeapons()`, 둘 다 이동·목표물 뒤. **쏜 틱에 총알을
  진행시키지 않는다** — 발사 틱에 4m 를 날아가면 눈앞의 벽이 총구보다 뒤에 있는 경우가 생기고,
  클라이언트의 예광탄은 총구에서 시작한다.
- **한 틱에 지나간 선분 전체를 검사한다.** 120m/s 는 한 틱에 4m 이므로 도착 지점만 보면 0.25m
  벽을 통과한다 — 클라이언트가 10만 m/s 로 검증한 것과 같은 함정이다.
- **눈높이에서 쏜다**(`PlayerHeight × EyeHeightRatio` = 1.62m). 클라이언트는 총구에서 쏘지만
  **조준점을 향해** 쏘고 명중 판정은 화면 중심에서 온다 — 실제 판정선은 눈에서 나가는 직선이고
  총구 오프셋은 연출이다.
- **시선 벡터 헬퍼를 `PlayerMovement.Forward` 에 두었다.** 요 규약(`전방 = (sin, 0, cos)`)이 이미
  `ApplyHorizontal` 에 살고 있으므로, 다른 파일에서 다시 세우면 한쪽을 고칠 때 다른 쪽이 남는다 —
  증상은 총알이 옆으로 날아가는 것이다. `요_90도면_플러스X_로_날아간다` 가 그것을 못질한다.
- **연사 간격 4.5틱 문제.** 0.15초 × 30Hz = 4.5 다. **올려서 5틱(0.167초)** 으로 두었다 —
  내리면 서버가 클라이언트보다 빠른 연사를 허용하고, 그 방향의 오차는 "클라이언트가 보내지도
  않은 발사를 서버가 받아 준다" 가 된다. 나눗셈이 아니라 값으로 적어 이 선택이 코드에 남게 했다.
- **재장전을 넣지 않았다.** 기획서 §4.3 의 재장전은 **체인이 놓아준 뒤** 일어나고 그 체인 견인이
  OQ-4 에 걸려 있다(IG-016). 순서를 임의로 정하면 벌칙의 의미가 달라지므로 §6.4 에 따라 만들지
  않았다 — **지금은 탄창 3발을 비우면 그 매치에서 더 쏠 수 없다.** 이것이 IG-016 이 풀려야
  완결되는 유일한 미완 구간이다.
- 변경 파일(7): `Shared/Simulation/PlayerMovement.cs`(`Forward`),
  `Shared/Simulation/MatchConstants.cs`(`BulletSpeed`·`BulletLifetime`·`FireInterval`),
  `Modules/Realtime/Simulation/Match.cs`(`FireIntervalTicks`·`BulletLifetimeTicks`),
  `Modules/Realtime/Simulation/PlayerEntity.cs`(`Ammo`·`NextFireTick`),
  `Modules/Realtime/Simulation/Projectile.cs`(신규),
  `Modules/Realtime/Simulation/Room.cs`(`FireWeapons`·`TrySpawnProjectile`·`StepProjectiles`,
  슬롯 32개, 매치 시작 시 초기화)
  + 신규 `tests/Modules.Tests/Realtime/ProjectileTests.cs`(11개)
- 검증:
  - `dotnet test --filter "FullyQualifiedName~ProjectileTests"` → **11/11 통과**
  - `dotnet test` → **383 통과** (Modules 379 + Architecture 4), 실패 0. 이전 372 에서 +11
  - `dotnet build` → **경고 0, 오류 0** / `Assembly-CSharp` → **오류 0**
  - 검사 내용: Seeker 만 발사 / 연사 간격 / 탄창 소진 / **벽 관통 없음** / 수명 만료 /
    눈높이 / 요 규약 / 리빌 중 발사 불가 / 다음 매치에 총알 미상속
  - **§7.4 스모크 미실행.** 지금은 서버 총알이 화면에 보이지 않으므로(클라이언트가 여전히 자기
    총알을 그린다) 스모크로 확인할 것이 IG-014c 까지 없다.
- 비고: 발사는 `player.LastInput` 을 읽으므로 **입력이 끊긴 뒤 `MaxInputRepeatTicks` 동안은 트리거가
  눌린 채로 반복된다.** 상호작용과 달리 이것은 "누르고 있는 상태" 라서 반복이 의미상 맞고, 반복
  상한이 지나면 `Neutral` 이 되어 멈춘다 — 무한하지 않다.

### IG-014b — 피격 판정 (출혈·순간이동·사망)
- 상태: **DONE** (이터레이션 24)
- 기획서 근거: §4.1 (1방 출혈 + 순간이동, 2방 사망), §4.2 (흔적)
- **지오메트리와 사람을 같은 선분에서 함께 본다.** 따로 검사하면 벽 뒤의 사람이 맞는다 — 벽이
  더 가까우면 벽이 이긴다. `벽_뒤의_사람은_맞지_않는다` 가 그것을 못질한다.
- **`EntityFlags.Downed = 1 << 7` 을 추가했다. `Alive` 를 내리지 않았다.** `Alive` 는 이동
  시뮬레이션 소유이고 `PlayerState.Flags` → `StateHash` 에 들어간다 — 매치 판정으로 그것을
  내리면 클라이언트가 예측할 수 없는 비트가 해시에 섞여 리컨실리에이션이 영구히 어긋난다.
  그 파일이 스스로 경고하던 함정이다. **이것이 `EntityFlags` 의 마지막 비트다.**
- `Bleeding` 은 **필드가 아니라 `Hits > 0 && !Downed` 유도값**이다. `DoorOpen` 과 같은 이유 —
  따로 두면 "1방 맞았는데 피가 안 난다" 가 표현 가능한 상태가 된다.
- **무적 창 0.75초 → 23틱(올림).** 22.5 라서 반올림이 필요하고, `FireIntervalTicks` 와 같은
  방향이지만 이유가 다르다: **창이 짧아지는 것이 곧 규칙이 깨지는 것**이다(3연사가 순간이동을
  관통해 죽이는 것을 막는 장치이므로). `무적_창_안의_두_번째_피격은_무시된다` 가 지킨다.
- **`MatchConstants` 머리말을 고쳤다.** "무적 창은 여기 두지 않는다" 고 적혀 있었는데 그 기준은
  "화면에 나오는가" 였다. 실제 기준은 **"클라이언트가 이 값으로 계산하는가"** 이고, 오프라인
  경로가 여전히 자기 피격을 판정하므로 답이 예다 — `KeyPickupHeight`·`InteractHeight` 와 같다.
  세 번 같은 판단을 한 뒤에야 머리말이 틀렸다는 것이 분명해졌다.
- **순간이동 난수를 배치와 분리했다**(`_placementSeed ^ 0x9E3779B9`). 같은 수열을 두 용도가
  나눠 쓰면 배치가 한 번 더 뽑는 변경이 순간이동 착지점을 바꾼다.
- **열쇠를 흩뿌리지 않고 한 점에 놓는다.** 클라이언트의 `ScatterKeys` 는 반경을 두지만 그 반경이
  기획서에 없고 표현이다 → IG-027 로 올렸다.
- **되감기는 넣지 않았다.** 발사체가 실체이므로 비행에는 필요 없고, 되감기가 필요한 것은 발사
  순간의 사수 위치뿐이다 — 서버가 자기 시뮬레이션의 눈에서 쏘므로 그 문제가 없다. 클라이언트
  예측(IG-023)이 들어오면 필요해진다.
- 변경 파일(6): `Shared/Contracts/Enums/EntityFlags.cs`(`Downed`),
  `Shared/Simulation/MatchConstants.cs`(`HitImmunity`, 머리말 수정),
  `Modules/Realtime/Simulation/Match.cs`(`HitImmunityTicks`),
  `Modules/Realtime/Simulation/PlayerEntity.cs`(`Hits`·`Downed`·`Bleeding`·`ImmuneUntilTick`),
  `Modules/Realtime/Simulation/Room.cs`(`TryFindVictim`·`BodyOf`·`ApplyHit`·`DownRunner`·
  `TeleportToRandomFreeFloor`, 플래그, 전문 `hits`, `internal Players`)
  + 신규 `tests/Modules.Tests/Realtime/HitTests.cs`(11개)
- 검증:
  - `dotnet test` → **394 통과** (Modules 390 + Architecture 4), 실패 0. 이전 383 에서 +11
  - **전체 실행 5회 반복** — 아래 흔들림을 고친 뒤 5회 연속 통과
  - `dotnet build` → **경고 0, 오류 0** / `Assembly-CSharp` → **오류 0**
  - **§7.4 스모크 미실행.** 클라이언트가 아직 이 값을 적용하지 않으므로 화면에 나타나는 것이
    없다 → IG-014c 이후.
- **테스트가 흔들렸고, 원인은 내 테스트였다.** `쏜_사람은_자기_총알에_맞지_않는다` 가 필터 실행
  에서는 통과하고 전체 실행에서는 실패했다. 픽스처의 두 스폰은 X 만 다르므로(0 과 -2),
  **어느 플레이어가 Seeker 로 뽑히느냐에 따라 +X 사격선에 Runner 가 정확히 놓인다.** Runner 를
  사격선에서 먼저 빼도록 고쳤다. 다중 피격 검사들도 같은 이유로 **사수 앞 2m 의 고정 지점**에
  Runner 를 세운다 — 순간이동 착지점이 무작위라 벽 뒤로 갈 수 있다.
- **실사격으로 검사할 수 없는 규칙이 하나 있다.** 기획서 §4 의 "술래는 총을 맞지 않는다" 는
  2인 매치에서 확인 불가다 — Seeker 는 한 명이고 Runner 는 쏠 수 없으므로 술래를 향해 날아가는
  총알을 만들 방법이 없다. 코드에는 역할 검사가 있고, **검사할 수 없다는 사실을 테스트 파일에
  주석으로 남겼다.**

### IG-014c — 클라이언트 전투 적용 + 로컬 판정 차단
- 상태: **DONE** (이터레이션 25)
- 기획서 근거: §4.1, §2.1
- **R-3.1 이 닫혔다.** 갭 매트릭스가 "가장 심각하다" 고 적어 둔 경로 — 쏜 클라이언트가 자기
  총알로 피격을 판정하던 것 — 가 사라졌다.
- **`Bullet` 을 한 줄도 고치지 않았다.** 차단을 `MatchManager.ReportHit` 한 곳에 두었고, 모든
  피격 경로가 그것을 통과한다(`PlayerAgent.OnHit`, 디버그 키 `MatchBootstrap:215`). 그래서
  `Bullet` 은 여전히 날고 `SendMessageUpwards("OnHit")` 도 남아 있지만 **아무 판정도 만들지
  않는다** — 순수 표현이 되는 데 삭제가 필요하지 않았다. IG-012b3 의 `TryInsertKey` 와 같은
  형태이고, 같은 이유로 규칙이 한 곳에 남는다.
- **`ServerOwnsCombat` 을 별도 플래그로 두었다.** 세션이 있으면 목표물과 전투를 함께 넘기지만,
  **그 둘은 서로 다른 태스크에서 건너왔고 여러 이터레이션 동안 서버가 한쪽만 판정했다** —
  IG-014a·b 기간에는 전투가 반쯤 서버였다. 도메인별 플래그는 그 이관을 조각으로 나눌 수 있게
  하고, 하나로 합친 플래그는 그 사이 기간에 거짓말을 해야 한다.
- **알림을 살렸다.** 열쇠 삽입 때는 "누가" 를 서버가 말하지 않아 알림을 뺐지만(IG-012b3),
  피격은 전문이 대상을 말하므로 "X HIT"·"X DOWN" 이 이 클라이언트가 정당하게 아는 정보다.
  말할 수 없는 것은 **언제** 이므로 **전이에서만** 올린다.
- **출혈은 값이 바뀔 때만 적용한다.** `SetBleeding` 이 `BloodTrail` 을 시작하므로 매 프레임
  부르면 흔적이 매 프레임 다시 시작한다 — 증상은 부상한 Runner 가 **흔적을 전혀 남기지 않는
  것**이다. 폴링으로 상태를 적용할 때 반복 호출이 무해한지 확인해야 하는 종류의 함정이다.
- 변경 파일(3): `Game/MatchManager.cs`(`ServerOwnsCombat`, `AcceptCombatState`, `ReportHit` 차단),
  `Game/PlayerAgent.cs`(`SetHits`), `Net/Session/MatchSync.cs`(`ApplyBody` — `ApplyEscaped` 를
  흡수해 스냅샷을 한 번만 읽는다)
- 검증:
  - `dotnet build Assembly-CSharp.csproj` → **오류 0**
  - `dotnet test` → **394 통과** (서버 무변경, 회귀 확인)
  - `grep "RegisterHit\(\)|\.Kill\(\)|ReportHit"` → 모든 피격 경로가 `ReportHit` 를 통과함을 확인
  - **자동 테스트 없음** — 클라이언트 전용, EditMode 인프라 없음(IG-018). IG-012b3·c2 와 같다.
- **알려진 표현 불일치:** 히트마커(`Crosshair.ShowHitMarker`)는 여전히 **로컬 총알**의 충돌로
  뜬다. 서버가 맞지 않았다고 판정해도 쏜 사람은 마커를 본다 — 판정이 아니라 연출이므로 규칙
  위반은 아니지만, 서버 총알을 그리게 되면(IG-028) 자연히 해소된다.

### IG-014 (원래 계획, 근거 보존)
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
- 상태: **DONE** (이터레이션 35) — **asmdef 가 필요하지 않았다**
- **내가 세 번(이터레이션 21·26·32) 적은 차단 사유가 틀렸다.** "Unity 의 asmdef 는
  `Assembly-CSharp` 를 참조할 수 없으므로 `Assets/Scripts` 전체를 asmdef 로 옮겨야 한다" 고
  적었는데, **그 전제는 asmdef 로 테스트를 만들 때만 성립한다.** 생성된 프로젝트를 열어 보니:
  - `Assembly-CSharp-Editor.csproj` 가 **`nunit.framework`·`UnityEditor.TestRunner`·
    `UnityEngine.TestRunner` 를 이미 참조한다**(테스트 프레임워크 패키지가 설치돼 있으므로).
  - 그리고 **`Assembly-CSharp` 와 `Shared` 를 `ProjectReference` 로 참조한다** — 에디터 스크립트가
    런타임 컴포넌트를 조작하기 위해 원래 그래야 한다.
  - 즉 **`Assets/Editor/` 아래 테스트를 두면 아무 설정 없이 클라이언트 코드가 보인다.**
  세 이터레이션 동안 "확인해야 한다" 고 적고 확인하지 않은 채 차단 사유로 인용했다.
- 첫 테스트 대상은 **폴링 멱등성**이다. `Accept*` 들은 여섯 이터레이션에 걸쳐 자동 테스트 없이
  쓰였고, 그 코드가 가장 쉽게 깨는 규칙이 "전문은 2Hz 로 반복되므로 더하면 안 된다" 다.
- 변경 파일(1): 신규 `NVproject/Assets/Editor/Tests/ClientApplyTests.cs`(4개 —
  소지 열쇠의 반복 적용·감소 방향·음수 하한·몸이 없는 참가자)
- 검증:
  - `dotnet build Assembly-CSharp-Editor.csproj` → **오류 0.** 실제 Unity DLL 에 대해
    `NUnit.Framework`·`MatchManager`·`PlayerAgent` 가 전부 해석된다 — 이것이 "인프라가 있다" 의
    증거다.
  - **테스트가 Test Runner 에서 실제로 실행되는 것은 확인하지 못했다.** 에디터를 열어
    `Window ▸ General ▸ Test Runner ▸ EditMode` 에서 `ClientApplyTests` 4개가 보이고 통과하는지
    봐야 한다 — 사람 검증 표에 추가했다. **컴파일이 통과한 것을 "테스트가 통과했다" 로 적지 않는다.**
- **이 경계가 남기는 제약을 기록해 둔다.** `Assembly-CSharp-Editor` 는 별개 어셈블리이므로
  **`internal` 멤버와 `[SerializeField] private` 필드가 보이지 않는다.** 그래서 지금 닿는 것은
  공개 표면뿐이다:
  - `AcceptCarriedKeys` ✅ (공개 메서드 + 공개 `CarriedKeys`)
  - `AcceptObjectiveProgress`·`AcceptEscapes` ❌ — `config`(`[SerializeField] private`)를 읽으므로
    테스트가 `GameConfig` 를 주입할 수 없다. `BeginMatch` 만 그것을 만든다.
  - `AcceptCombatState` ❌ — `PlayerAgent.SetBleeding`·`Kill` 이 `internal` 이라 상태를 준비할 수 없다.
  **이것을 뚫으려면 공개 이음새(테스트용 config 주입)가 필요하고 그것은 요청되지 않은 리팩터링이다**
  (§9) → IG-029 로 올렸다.
- 비고: D-2("순수 로직은 `dotnet test`")는 유효하다. 이 인프라의 대상은 **서버로 옮길 수 없는
  적용·표시 로직**이고, 그것이 지금 유일하게 테스트가 없는 층이다.

### IG-032 — 매치 종료 권위를 검증한다
- 상태: **DONE** (이터레이션 39)
- **`Control(EndMatch)` 는 클라이언트에 남은 마지막 권위 경로다.** 서버가 탈출·피격·열쇠를 다 세지만
  결과 코드를 정하지 않으므로(OQ-2·OQ-6 → IG-007) 방장이 판정해 보고하고 서버가 중계한다. 즉
  **이 경로의 인증이 "클라이언트 권위로 게임 결과를 결정하지 않는다"(§9)를 지키는 유일한 장치다.**
- **그런데 기존 테스트 6개가 전부 세션 1(방장)에서만 보냈다 — 거부되는 쪽이 검사되지 않았다.**
  `IsAuthorized` 를 지우는 변경은 그 6개를 모두 통과하고, 그러면 **아무 클라이언트나 아무 결과
  코드로 매치를 끝낼 수 있다.**
- 변경 파일(1): 신규 `tests/Modules.Tests/Realtime/EndMatchAuthorityTests.cs`(7개)
  - **비방장은 끝낼 수 없다**(load-bearing) / 방장의 결과 코드가 그대로 중계된다 / 시작하지 않은
    룸은 끝낼 수 없다 / **종료 뒤에도 최종 수치가 전문에 남는다** / 로비 복귀가 결과를 지운다 /
    비방장은 로비로도 돌릴 수 없다 / **정적 룸은 아무나 끝낼 수 있다**
- **"종료 뒤에도 최종 수치가 남는다" 는 `Match.ForceEnd` 가 `KeysInserted`·`Escapes` 를 0 으로
  만들지 않는 것이 의도라는 근거다**(`Reset` 만 지운다). 결과 화면이 "열쇠 7/10, 탈출 1" 을 보여
  줄 수 있는 이유이고, 누군가 `ForceEnd` 에 초기화를 넣으면 이 테스트가 막는다.
- **정적 룸의 비대칭을 함께 고정했다.** 개발용 룸은 코드 발급 경로가 없어 방장도 없으므로 아무나
  시작하고 아무나 끝낸다. 시작 쪽은 `RoomTests` 가 이미 고정하고 있었는데 종료 쪽은 없었다 —
  **같은 `IsAuthorized` 를 쓰지만 별개의 명령이므로 한쪽만 검사하면 다른 쪽이 열린 채 남는다**
  (`ReturnToLobby` 도 같은 이유로 함께 검사했다).
- 검증:
  - `dotnet test` → **424 통과** (Modules 420 + Architecture 4), 실패 0. 이전 417 에서 +7
  - `dotnet build` → 경고 0, 오류 0

### IG-031 — 탈출·피격 타이브레이크를 확정한다
- 상태: **BLOCKED** (OQ-8) — 현재 동작은 이터레이션 38 이 테스트로 고정했다
- **판정 순서가 규칙을 하나 정하고 있었고, 그것은 기획서가 정한 것이 아니다.**
  `Room.Advance` 는 `TickEscapes`(14행) → `StepProjectiles`(19행) 순이므로 **유지 시간의 마지막
  틱에 도착한 총알은 탈출을 끊지 못한다** — `TryFindVictim` 이 이미 `Escaped` 인 몸을 걸러낸다.
- **그것이 문서화된 의도와 어긋난다.** `MatchConstants.EscapeHoldTime` 과 `Room.TickEscapes` 는
  유지 시간의 목적을 "목표의 마지막 한 걸음을 Seeker 가 끊을 수 있는 순간으로 만드는 것" 이라고
  적는데, 그 순간의 마지막 33ms 는 끊을 수 없다.
- **사소해 보이지만 결과는 이진이다.** `EscapesToWin` 이 2 이므로 어느 Runner 가 나갔는지 죽었는지가
  매치 결과를 바꾼다. 그래서 §6.4 의 "게임플레이 영향이 큰 판정 방식" 에 해당하고 추측하지 않았다.
- **차단 상태로 두면서 현재 동작을 고정했다**(`TieBreakTests` 2개). 답이 (a)로 오면 첫 테스트가
  실패하고, 수정은 `StepProjectiles`·`FireWeapons` 를 목표물 판정 앞으로 옮기는 것이다.
- 검증(이터레이션 38 이 한 것):
  - `dotnet test` → **417 통과** (Modules 413 + Architecture 4), 실패 0. 이전 415 에서 +2
  - **대조군이 이 검사를 의미 있게 만든다.** 총알을 한 틱 앞세우면 피격이 성립하고 탈출이 막힌다 —
    즉 겹치는 틱에서 총알은 **빗나간 것이 아니라 대상에서 빠진 것**이고, 창은 정확히 한 틱이다.
    그 대조군이 없으면 "총알이 안 맞았다" 와 구별되지 않는다.
  - `Room.Advance` 에 그 순서가 규칙을 정하고 있다는 주석을 남겼다.

### IG-030 — 격자 없는 맵의 전투 열화 경계를 고정한다
- 상태: **DONE** (이터레이션 37)
- **`test-room` 은 격자가 없고**(D-6 — 중앙 플랫폼과 커버 블록이 있어 전부 `FreeFloor` 로 채우면
  블록 안이 걸을 수 있는 곳이 된다) **`MultiplayerTest` 씬이 그 룸에 붙는다.** 즉 두 클라이언트로
  확인할 때 실제로 도는 것은 이 **열화 모드**인데, 그것이 어디까지 정상인지 코드 어디에도 적혀
  있지 않았다.
- 조사 결과 격자는 **피격 순간이동에만** 필요하다(`Room.cs` 의 `HasGrid`/`_map.Grid` 참조 3곳 중
  배치는 `ObjectivePlacement` 가 null 을 막고, 목표물 판정 셋은 `Placed` 로 걸러진다).
  **정확한 경계는 "전투가 전부 정상이고 순간이동 하나만 빠진다" 다.**
- 이미 있던 것: `RoomTests.격자가_없는_맵에서는_목표물_전문이_나가지_않는다`. **덮이지 않은 것은
  전투 경로 전체였다.**
- 변경 파일(1): 신규 `tests/Modules.Tests/Realtime/GridlessMatchTests.cs`(6개 — 진행 단계 진입 /
  목표물 판정이 조용히 지나감 / 총알이 벽에 맞음 / **피격 성립** / **순간이동 미발생** /
  두 번 맞으면 쓰러짐)
- 검증:
  - `dotnet test` → **415 통과** (Modules 411 + Architecture 4), 실패 0. 이전 409 에서 +6
  - `dotnet build` → 경고 0, 오류 0
  - **`격자가_없으면_피격_순간이동이_일어나지_않는다` 가 load-bearing 이다** — `!_map.HasGrid`
    early-return 을 지우면 null 격자에서 예외가 나고, 예외로 바꾸면 그것도 잡힌다. **격자가 있는
    맵의 테스트는 그 변경에 전부 통과하므로 이것 없이는 개발 루프만 조용히 깨진다.**
- **내 테스트가 한 번 틀렸고 이터레이션 24 와 같은 실수였다.** 헬퍼(`ShootRunner`)가 판정 대상을
  먼저 옮기는데 호출 **전** 위치와 비교했다 — 헬퍼가 옮긴 것을 순간이동으로 착각한다. 쏜 자리를
  기준으로 고쳤다. **판정 대상의 자리를 고정하는 헬퍼는 그 자리를 반환하거나, 검사가 그 자리를
  스스로 계산해야 한다.**

### IG-029 — 적용 경로에 테스트용 공개 이음새를 만든다
- 상태: **DONE** (이터레이션 36) — **프로덕션 코드를 한 줄도 고치지 않았다.** 이음새가 필요 없었다.
- **ADR 이 필요한 갈림이 아니었다.** 세 선택지 중 (b)`InternalsVisibleTo` 는 **서버가 이미 쓰는
  관례**이고(`Modules/Realtime/AssemblyInfo.cs`, 그리고 `Architecture.Tests` 가 그 줄의 존재를
  **강제**한다) 한 줄이며 되돌리기 쉽다. 프로젝트가 "테스트는 어떻게 internal 을 보는가" 에 이미
  답해 두었으므로 §5.4 의 "두 갈래로 갈리는 설계" 가 아니다.
- **그런데 그것도 필요하지 않았다.** `InternalsVisibleTo` 를 넣고 테스트를 쓴 뒤 확인해 보니
  **어느 테스트도 `internal` 을 쓰지 않는다** — 적용 메서드는 전부 공개이고 그것이 쓰는 값은 전부
  공개 프로퍼티로 읽힌다. `SetBleeding` 으로 상태를 미리 만드는 대신 **`AcceptCombatState` 로
  구동하는 것이 오히려 나은 테스트**이기도 하다(실제 경로를 지난다). 그래서 넣었던
  `AssemblyInfo.cs` 를 지웠다 — **투기적 변경이었다.**
- 남은 진짜 장벽은 **`config`(`[SerializeField] private`)** 하나였고, `SerializedObject` 로 넣었다 —
  인스펙터가 쓰는 API 이고 프로덕션 표면을 넓히지 않는다.
- 변경 파일(1): `NVproject/Assets/Editor/Tests/ClientApplyTests.cs`(4개 → **8개**)
  - 추가: 열쇠 진행도가 **바뀔 때만** 이벤트를 올린다 / 필요 수를 넘지 않는다 / 탈출 수도 바뀔
    때만 올린다 / **출혈은 반복 적용해도 유지된다**(`SetBleeding` 이 `BloodTrail` 을 재시작하면
    흔적이 남지 않는다는 그 규칙) / 쓰러짐은 한 번만 적용된다
- 검증:
  - `dotnet build Assembly-CSharp-Editor.csproj` → **오류 0**
  - `BloodTrail.Begin` 이 환경 의존이 없는지 확인했다(참조만 저장하고 `MatchManager.Instance` 를
    null 가드한다) — 출혈 검사가 EditMode 에서 터지지 않을 근거다
  - **여전히 실행은 확인하지 못했다.** Test Runner 는 에디터가 필요하다 → 사람 검증 표.

### IG-019 — 상수 정리·문서 갱신·죽은 경로 제거
- 상태: **DONE** (이터레이션 26)
- **코드 정리는 한 곳뿐이었다.** `GameConfig.hitImmunity` 가 IG-014b 에서 `MatchConstants` 로
  올라갔는데 **직렬화 필드로 남아 있었다** — 에셋이 자기 사본 0.75 를 들고 있어 서버 값과 갈릴
  수 있는 상태였다. D-7 의 소문자 프로퍼티로 바꿨다. 이 루프가 같은 함정을 세 번째로 만난 것이다
  (`keyPickupHeight`·`interactHeight`·이것).
- 나머지 `GameConfig` 의 "서버로 옮겨간다" 항목은 확인 결과 **옮길 것이 없었다.**
  `dropKeysOnDeath`·`teleportOnHit` 는 서버가 기획서 §4.1 대로 항상 그렇게 하므로 이제
  **오프라인 스위치**일 뿐이고, 지우면 오프라인 노브만 없어진다. 주석을 그 사실로 고쳤다.
- **거짓이 된 문서 네 곳을 고쳤다.** 이것이 이 태스크의 실제 산출물이다:
  - 루트 `CLAUDE.md` — 프로토콜 2 → **3**, `Event` 가 세 종류라는 것과 **세션별 인코딩**,
    "매 틱 vs 2Hz" 를 나누는 기준, export 의 **격자**와 `FreeFloor` 의 뜻, 테스트 수 137 → 394
  - `NVproject/CLAUDE.md` — **"히트·열쇠·탈출은 여전히 각 클라이언트가 판정한다" 가 거짓이었다.**
    두 권위 플래그, `Bullet` 이 삭제가 아니라 무력화됐다는 것, **무엇이 어느 경로로 오는지 표**,
    "수는 전문·상태는 스냅샷", 폴링 적용은 멱등해야 한다는 것, 남은 유일한 클라이언트 판정이
    승리 조건이라는 것
  - `NVserver/docs/architecture.md` — 와이어 표에 `MatchState`·`ObjectiveState`·`MatchParticipant`,
    `RoomStateHeader` 15B → **11B 와 그 이유**, 버전 3, **버튼 비트 추가는 버전을 올리지 않는다**,
    기본값 대체표에 7행 추가(규칙 판정·상호작용 대상·씨드 공유·코덱 필터·`StateHash`·틱 변환)
  - `NVserver/docs/conventions.md` — §시뮬레이션에 6항목(요 규약 단일화, 선분 전체 검사, 와이어는
    판정 뒤, 매치 비트와 `StateHash`, 틱 반올림 방향, 난수 수열 분리), §클라이언트 연동에
    4항목(엣지 버튼 반복, 상호작용 대상, **폴링 멱등성**, 알림도 정보 규칙), §문제 해결에
    **"필터로만 돌린 테스트는 스폰 배치의 우연에 기대고 있을 수 있다"**
- 변경 파일(5): `Game/GameConfig.cs`, `CLAUDE.md`, `NVproject/CLAUDE.md`,
  `NVserver/docs/architecture.md`, `NVserver/docs/conventions.md`
- 검증:
  - `dotnet build Assembly-CSharp.csproj` → **오류 0** (프로퍼티 전환이 호출부를 깨지 않았다)
  - `dotnet test` → **394 통과**, `dotnet build` 경고 0
  - 문서 주장은 각각 코드를 열어 확인한 뒤 고쳤다 — 프로토콜 버전, `RoomStateHeader.WireSize`,
    `EntityFlags` 사용 비트, `ButtonFlags.All` 마스크, 테스트 수
- 비고: `GameConfig.asset` 에 `hitImmunity` 의 옛 직렬화 값이 남지만 프로퍼티가 그것을 가리므로
  무해하다 — IG-005 가 25개 필드를 프로퍼티로 바꿀 때와 같다.

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
| AS-16 | 흘린 열쇠를 사망 지점에서 **0.7m 반경**에 퍼뜨린다 | 기획서에 없다. 클라이언트 `ScatterKeys` 의 값이다. **습득 반경(1.4m)보다 작아야** 퍼뜨린 열쇠가 전부 사망 지점에서 닿고, 그 덕에 격자 스냅 없이도 회수 불가능한 열쇠가 생기지 않는다(테스트로 고정) | IG-027 (회수 난이도가 조금 오른다) | 아니오 — 기존 구현 인용, 게임플레이 영향이 작다 |
| ~~AS-15~~ | ~~사망 시 흘린 열쇠를 한 점에 놓는다~~ | **IG-027 에서 철회됐다.** 반경 없이 두면 한 무더기가 한 틱에 전부 주워진다 → AS-16 | — | — |
| AS-14 | **무적 창을 22.5틱에서 23틱으로 올렸다** | 0.75초 × 30Hz = 22.5 다. `FireIntervalTicks` 와 같은 올림이지만 이유가 다르다 — **창이 짧아지는 것이 곧 규칙이 깨지는 것**이다. 이 창의 존재 이유가 3연사가 순간이동을 관통해 죽이는 것을 막는 것이므로, 한 틱을 잃으면 막으려던 경우가 다시 열린다 | IG-014b | 아니오 |
| AS-15 | 사망 시 흘린 열쇠를 **사망 지점 한 점**에 놓는다(흩뿌리지 않는다) | 규칙은 "사망 지점에 흘린다" 이고 한 점이 그것에 맞다. 클라이언트의 `ScatterKeys` 는 반경을 두지만 그 반경이 기획서에 없다 — 없는 값을 만들지 않았다 | IG-014b (표현) | 예 → IG-027 |
| AS-12 | 총알 속도 120m/s, 수명 3초, 연사 간격 0.15초 | 기획서는 §4.3 에서 탄창(3발)만 정한다. 세 값은 클라이언트의 `Bullet.speed`·`WeaponController.bulletLifetime`·`fireCooldown` 이고 그대로 옮겼다. **히트스캔이 아닌 것이 설계**이므로 속도는 규칙에 가깝다 — 5m 를 40ms 에 지나므로 근거리에서도 피할 창이 있다 | IG-014a (전투 밸런스) | 아니오 — 기존 구현 인용 |
| AS-13 | **연사 간격을 4.5틱에서 5틱으로 올렸다** | 0.15초 × 30Hz = 4.5 로 틱에 나누어지지 않는다. 내리면(4틱) 서버가 클라이언트보다 빠른 연사를 허용하고, 그 방향의 오차는 **신뢰 경계를 넓히는 쪽**이다. 올리면 서버가 조금 엄격해지고 증상은 연사가 0.017초 느린 것뿐이다 | IG-014a | 아니오 — 방향이 안전한 쪽이다 |
| AS-11 | **탈출의 수직 허용치도 `InteractHeight`(2.5m)** — 클라이언트의 2.0m 를 옮기지 않았다 | 클라이언트는 삽입 프롬프트를 2.5m 에서 띄우고 탈출은 2.0m 에서 판정했다. **그 0.5m 는 "서 있으라고 표시된 자리에 서 있는데 아무 일도 안 일어나는" 구간**이고, 같은 질문(문 앞에 있는가)에 두 답을 두는 것이 원인이다. 층 간격 3.2m 보다는 여전히 작아 층 분리는 유지된다 | IG-012c1 (탈출이 0.5m 관대해진다) | 예 — 문간 판정을 의도적으로 좁게 두고 싶었다면 되돌린다 |
| AS-10 | 상호작용의 수직 허용치 2.5m (`MatchConstants.InteractHeight`) | 기획서에 없다. 클라이언트의 `PlayerInteractor.Consider` 가 쓰던 값이고, **그쪽이 프롬프트를 띄우는 조건이었으므로 화면에 보이는 것과 판정이 일치하는 값이다.** 층 간격 3.2m 보다 작아 위층에서 아래층 문에 넣을 수 없고, 반경 2.2m 보다 커서 계단 중간에서도 닿는다 | IG-012b2 (삽입), IG-013 (장치) | 아니오 — 기존 구현 인용이고 층 분리라는 규칙 제약을 만족한다 |

## 질문 리포트 (§10) — 이 답들이 없으면 규칙 작업이 더 진행되지 않는다

**규칙 이관은 끝났다.** 남은 규칙 태스크 5개(IG-007·013·015·016·017)는 전부 아래 질문에 걸려 있고,
§6.4 가 "게임플레이에 영향이 큰 값·규칙은 추측하지 말 것" 이라고 정하고 있다.

우선순위 순으로, **답 하나가 무엇을 여는지**:

| 순위 | 질문 | 답하면 열리는 것 | 왜 추측할 수 없는가 |
|---|---|---|---|
| **1** | **OQ-2 + OQ-6** — 전멸 승리가 있는가? 2인 매치에서 Runner 는 어떻게 이기는가? | **IG-007(승리 조건) + IG-021.** 클라이언트에 남은 마지막 판정이고, 지금 방장이 결과를 정하므로 조작된 방장이 결과를 바꿀 수 있다 | 기획서 §8 은 전멸 승리를 아예 언급하지 않는데 구현에는 `seekerWinsOnWipe` 가 있다. 그리고 `escapesToWin` 2 + 최소 인원 2 면 **Runner 가 이길 수 없는 조합**이 성립한다 — 어느 쪽이 의도인지가 승패의 정의를 바꾼다 |
| **2** | **OQ-1** — 1:1 순간이동 장치는 술래 전용인가? | **IG-013(장치 6종) → IG-015(장치 파괴).** 기획서 §5 전체가 여기 걸려 있다 | 기획서 §5.2 는 "술래 전용", 룰셋은 "Runner 공용", 구현은 "술래의 장치 사용 금지" 라고 서로 다르게 말한다. 기획서가 이기지만 그러면 장치 시스템의 접근 제어가 뒤집힌다 |
| **3** | **OQ-4** — 체인 견인의 경로 방식 | **IG-016(체인) → 재장전.** 지금 **탄창 3발을 비우면 그 매치에서 더 쏠 수 없다** — 기획서의 재장전이 체인 뒤에 오기 때문이다 | 서버에 navmesh 가 없다. 직선 견인은 측정된 연출(399m 경로 대 55m 직선)을 버리고, 격자 A\* 는 `Shared` 에 A\* 를 들인다. 서버 인터페이스는 같으므로 1 로 시작해 2 로 올릴 수 있다 |
| 4 | **OQ-5** — 레거시 파일을 삭제해도 되는가? | IG-020 | `BackroomsMap.cs` 가 생성기와 같은 `"backrooms"` 이름을 쓰므로 한 씬에 둘이 있으면 export 대상이 순회 순서로 갈린다 |
| 5 | **OQ-3** — 근접 보이스의 전송 방식 | IG-017 | 갭 매트릭스에서 유일한 `NONE` 영역이고 기획서 §7.4 는 옵션으로 표시돼 있다 |
| 6 | **OQ-7** | (차단 아님 — AS-4 로 진행 중) | 목표물 전문 주기 |

**OQ-8 이 이터레이션 38 에 추가됐다** — 탈출·피격 타이브레이크. 위 표에 없지만 우선순위는 **2위와
3위 사이**다: 한 틱짜리 창이지만 `EscapesToWin` 2 에서 결과가 이진이고, **답이 (a)면 두 줄 이동으로
끝난다.** 현재 동작은 `TieBreakTests` 가 고정하고 있다.

**답이 필요하지 않던 것은 전부 처리됐다** (루프 종료 시점 갱신): IG-018(EditMode 인프라)과
IG-028(서버 총알·탄약 와이어)은 닫혔다 — 남이 쏜 예광탄이 화면에 있고 탄약이 와이어에 있다.
남은 것 중 답이 필요하지 않은 것은 **IG-023(클라이언트 예측)** 하나이고, 그것은 실측이 필요하다.

**그리고 답보다 먼저 해야 할 것이 하나 있다:** 아래 "사람이 해야 하는 검증" 의 2클라이언트 스모크가
**한 번도 실행되지 않았다.** 서버 판정은 424개 테스트로 고정됐지만 그것이 화면에 도달하는지는
확인되지 않았고, 적용 경로의 버그는 컴파일을 통과한다.

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
| OQ-8 | **같은 틱에 탈출 완료와 총알 도착이 겹치면 무엇이 이기는가?** 현재는 판정 순서(`TickEscapes` → `StepProjectiles`) 때문에 **탈출이 이긴다** — 즉 유지 시간의 마지막 틱에 도착한 총알은 끊지 못한다. 그런데 `EscapeHoldTime` 의 문서화된 목적이 "Seeker 가 끊을 수 있는 순간을 만드는 것" 이다. 창은 한 틱(33ms)이지만 `EscapesToWin` 2 에서 결과는 이진이다 | IG-031 | (a) **피격이 이긴다** — 의도에 맞다. `StepProjectiles`·`FireWeapons` 를 목표물 판정 앞으로 옮긴다(부수 효과: 이번 틱에 쓰러진 Runner 가 같은 틱의 습득·삽입·탈출에서 빠진다 — 그것도 의도에 맞아 보인다) (b) 현재대로 탈출이 이긴다 — "문을 지났으면 끝" 이라는 해석 (c) 무적 창처럼 유지 완료에 **끊을 수 있는 유예 틱**을 둔다 |

---

## 사람이 해야 하는 검증 (§7.4 스모크, 누적)

**여기 있는 항목은 하나도 "통과" 로 기록되지 않았다.** MCP 로는 두 번째 클라이언트를 만들 수 없고
입력도 주입할 수 없다(`NVproject/CLAUDE.md`). 절차는 매번 같다:

1. **Tools ▸ NV ▸ Build and Launch 2 Clients** — 두 클라이언트가 `MainLobby` 로 열린다
2. 한쪽에서 **방 만들기** → 초대 코드를 다른 쪽에 넣는다
3. 방장이 **게임 시작**

그때 확인할 것 (태스크별로 쌓인 것):

| 태스크 | 확인할 것 |
|---|---|
| IG-001 | 접속 로그에 맵 해시 `일치` 가 뜬다 (불일치 경고가 없다) |
| IG-006 | 역할 공개가 **서버 틱에** 끝난다 — 프레임레이트가 다른 두 기기에서 같은 순간에 |
| IG-010 | 두 화면의 단계 전이 시점과 남은 시간이 일치한다 |
| IG-011c2 | 목표물(제단·열쇠·장치)이 두 화면에서 **같은 자리**에 있다. **Seeker 화면에는 문이 없다** |
| IG-012a | 한쪽이 열쇠를 밟으면 **두 화면에서 동시에 사라지고**, 소지 수는 밟은 쪽만 오른다. Seeker 화면의 소지 수는 계속 0 이다 |
| IG-012c1~c2 | 문이 열린 뒤 문간에 0.8초 서 있으면 그 Runner 가 **두 화면에서 사라지고**, **탈출 수가 Seeker 화면에서도 오른다** — 열쇠 진행도와 달리 이 값은 Seeker 도 받는다. 0.8초가 되기 전에 문에서 벗어나면 아무 일도 없다 |
| IG-018·029 | **에디터에서 확인한다**(스모크가 아니다): `Window ▸ General ▸ Test Runner ▸ EditMode` 에 `ClientApplyTests` **8개**가 보이고 통과하는가. 컴파일만 확인됐다 |
| IG-028a~b2 | 술래 화면의 탄피가 3발에서 줄고, **Runner 화면에서는 술래의 남은 탄이 보이지 않는다**. Runner 화면에 **술래가 쏜 예광탄이 보인다**(지금까지는 자기 총알만 보였다). 예광탄이 총구에서 나가고 벽에서 멈춘다 |
| IG-014a~c | 술래가 Runner 를 쏘면 **두 화면에서** 피가 나고, 두 번째 명중에 몸이 사라진다. 첫 명중에 Runner 가 **다른 곳으로 던져진다**. 0.75초 안의 연사는 한 번만 센다. 탄창 3발을 비우면 더 쏠 수 없다(재장전 없음 — IG-016 대기). **부정 클라이언트가 피격을 거부할 수 없다**: 맞은 쪽 클라이언트를 조작해도 서버가 정한다 |
| IG-012b1~b3 | **지금 가장 값이 큰 확인이다** (이 경로에는 자동 테스트가 없다). 문 앞에서 E 를 누르면 진행도가 두 Runner 화면에서 같이 오르고, 소지 수는 넣은 쪽만 줄어든다. 0.6초 안에 두 번 눌러도 한 개만 들어간다. 10개째에 두 화면에서 같은 순간에 문이 열린다. **Seeker 화면에는 문도 진행도도 나오지 않는다** |

---

## 다음 이터레이션 — 이력 (루프는 이터레이션 40 에서 종료됐다)

> 아래는 각 이터레이션이 끝날 때 남긴 "다음에 무엇을 할지" 의 기록이다. **지시가 아니라 이력이다** —
> 종료 시점의 인수인계는 이 파일 맨 위 **종료 리포트 §5** 에 있다. 이 절을 남겨 두는 이유는
> 판단이 어떻게 바뀌었는지가 그 자체로 근거이기 때문이다(특히 이터레이션 35·37 에서 "할 일이
> 없다" 는 판단이 두 번 틀렸다).

**검증 보강 후보가 소진됐다.** 이터레이션 37·38·39 가 세 개를 처리했고(격자 없는 열화 모드 /
탈출·피격 타이브레이크 / 종료 권위), 남은 것은 전부 OQ 대기이거나 사람의 손이 필요하다.

**이 방식으로 더 찾으려면 기준이 필요하다.** 세 이터레이션이 찾은 것의 공통점은 셋 다
**"거부되는 쪽" 또는 "구성이 다른 쪽"이 검사되지 않은 것**이었다:
- IG-030 — 격자가 **없는** 구성(개발 루프의 기본 경로)
- IG-031 — 두 판정이 **겹치는** 틱(순서가 규칙을 정하고 있었다)
- IG-032 — 권한이 **없는** 세션(인증의 거부 경로)

같은 기준으로 남은 것을 훑으면 후보가 더 나올 수 있다(예: 정원이 **찬** 룸의 입장 거부,
프로토콜 버전이 **다른** 접속, 맵 해시가 **어긋난** 접속 — 다만 이 셋은 `Realtime` 밖의 HTTP·
핸드셰이크 계층이고 그쪽 테스트 상황을 먼저 봐야 한다).

**사람만 할 수 있는 것은 그대로다** — 2클라이언트 스모크(한 번도 실행되지 않았다), OQ-1~8 의 답,
에디터에서 EditMode 8개 실행 + `FireEventMessage.cs` 의 `.meta`.

### 이전 판단 (이터레이션 38 시점)

이터레이션 37·38 이 같은 방식으로 둘을 찾았다 — 테스트되지 않은 경계를 찾다 보니 **판정 순서가
정하고 있던 미결 규칙**이 나왔다. 남은 후보:

- **`ForceEnd` 경로** — 방장의 `Control(EndMatch)` 뒤 룸이 `Ended` 로 가고 판정들이 멈추는지,
  재시작이 깨끗한지. `Match.ForceEnd` 는 `KeysInserted`·`Escapes` 를 0 으로 만들지 **않는다**
  (`Reset` 만 한다) — 결과 화면에서 그 값이 남아 있어야 하므로 맞아 보이지만, 재시작 시 `Begin`
  이 0 으로 만드는지 확인이 필요하다.
- ~~3인 이상 픽스처~~ — **성립하지 않는다.** 스폰을 늘려도 총을 쏘는 것은 Seeker 뿐이고 유일한
  비-Runner 가 그 자신(소유자)이므로, `TryFindVictim` 의 역할 검사와 소유자 검사가 완전히 겹친다.
  "술래는 총을 맞지 않는다" 는 **2인이든 8인이든 실사격으로 검사할 수 없다**(이터레이션 38 확인).

**사람만 할 수 있는 것은 그대로다** — 2클라이언트 스모크(한 번도 실행되지 않았다), OQ-1~8 의 답,
에디터에서 EditMode 8개 실행 + `FireEventMessage.cs` 의 `.meta`.

### 이전 판단 (이터레이션 37 시점)

이터레이션 37 이 보여 준 것: **"진행 가능한 태스크가 없다" 와 "할 일이 없다" 는 다르다.** 규칙은
전부 OQ 대기지만, **이미 구현된 규칙의 테스트되지 않은 경계**는 남아 있었다 — `test-room` 의 격자
없는 열화 모드가 개발 루프의 기본 경로인데 전투 쪽이 전혀 덮이지 않았다.

같은 방식으로 찾을 수 있는 후보(다음 이터레이션이 하나 집는다):
- **`ForceEnd` 경로** — 방장의 `Control(EndMatch)` 가 `Match.ForceEnd` 를 부른다. 그 뒤 룸이
  `Ended` 로 가고 판정들이 멈추는지, 재시작이 깨끗한지 확인하는 테스트가 있는지 봐야 한다.
- **`Downed`·`Escaped` 가 동시에 성립하는 경로** — 문간에서 유지 중에 맞으면? `TickEscapes` 가
  `Downed` 를 걸러내지만 순서(피격 → 탈출)가 그것을 보장하는지는 테스트가 없다.
- **정원 8명에서의 판정** — 스폰이 2개뿐이라 3인 이상을 재현할 수 없다는 제약을 IG-014b 에
  기록했다. 픽스처에 스폰을 늘리면 "술래는 총을 맞지 않는다" 를 실사격으로 검사할 수 있다.

**사람만 할 수 있는 두 가지는 그대로다** — 2클라이언트 스모크(한 번도 실행되지 않았다)와
OQ-1~7 의 답. 에디터를 한 번 여는 것이 EditMode 실행과 `FireEventMessage.cs` 의 `.meta` 도 함께
해소한다.

### 이전 판단 (이터레이션 36 시점): 진행 가능한 태스크가 없다

남은 10개는 전부 다음 둘 중 하나다:
- **OQ 대기 6개** — IG-007(OQ-2·6), IG-013(OQ-1) → IG-015, IG-016(OQ-4), IG-017(OQ-3), IG-020(OQ-5)
- **사람의 손이 필요한 것** — IG-023(클라이언트 예측: AS-8 이 "지금 없다" 를 기록했고 §8 은 요구하지
  않는다. 로컬 서버에서는 증상이 없어 실측 없이 손대면 무엇을 고쳤는지 알 수 없다)

**루프가 할 수 있는 일은 끝났다.** 남은 두 가지는 사람만 할 수 있다:

1. **2클라이언트 스모크를 한 번 돌린다** — 여전히 한 번도 실행되지 않았다. 409개 서버 테스트와
   8개 EditMode 테스트가 판정과 적용을 고정했지만 **그것이 화면에서 만나는지는 확인되지 않았다.**
   절차와 태스크별 확인 항목은 아래 "사람이 해야 하는 검증" 표에 있다. **에디터를 여는 것이
   그 표의 두 항목(EditMode 실행, `FireEventMessage.cs` 의 `.meta`)을 함께 해소한다.**
2. **OQ-1~7 에 답한다** — 우선순위와 "답 하나가 무엇을 여는지" 는 질문 리포트에 있다.

루프가 계속 돌면 할 수 있는 것은 **문서 정합성 재점검**(이터레이션 27·30·35 에서 세 번 거짓이
발견된 이력이 있다)과 **테스트 보강**뿐이다 — 새 규칙은 OQ 없이 만들 수 없다.

### 이전 계획 (이터레이션 35 시점)

IG-018 이 EditMode 인프라를 열었지만 `Assembly-CSharp-Editor` 에서 `internal` 과
`[SerializeField] private` 가 보이지 않아 적용 경로의 대부분에 닿을 수 없다 —
`AcceptObjectiveProgress`·`AcceptEscapes`(둘 다 `config` 를 읽는다)와 `AcceptCombatState`
(`PlayerAgent` 의 `internal` 상태 변경자가 필요하다).

세 갈래가 있고 되돌리기 어렵다:
- **(a) `MatchManager` 에 공개 초기화** — 가장 작지만 프로덕션 API 를 테스트를 위해 넓힌다.
- **(b) `InternalsVisibleTo("Assembly-CSharp-Editor")`** — 한 줄이지만 `internal` 의 뜻을 넓히고,
  이 프로젝트가 `internal` 로 모듈 경계를 지키는 것과 결이 다르다(서버 쪽은 테스트에 그것을 이미
  허용한다 — 일관성 논거가 양쪽에 있다).
- **(c) 적용 로직을 `MonoBehaviour` 밖 순수 클래스로** — **D-2 와 가장 일관되고**(순수 로직은
  `dotnet test`) 가장 크다. 적용 로직이 `PlayerAgent` 에 묶여 있어 그 의존부터 갈라야 한다.

**§5.4 에 따라 ADR 초안을 먼저 쓰고, 갈림이 남으면 OQ 로 올린다.**

### 그 밖에 진행 가능한 것은 없다 — §10 의 질문 리포트 상태다

남은 것을 성격별로 보면:

| 남은 것 | 왜 지금 못 하는가 |
|---|---|
| IG-007 승리 조건 | **OQ-2 + OQ-6.** 클라이언트에 남은 마지막 판정이고, 방장이 결과를 정하므로 조작된 방장이 결과를 바꿀 수 있다 |
| IG-013 장치 6종 → IG-015 파괴 | **OQ-1.** 기획서 §5 전체 |
| IG-016 체인 → 재장전 | **OQ-4.** 지금 탄창 3발을 비우면 그 매치에서 더 못 쏜다 |
| IG-017 근접 보이스 | **OQ-3.** 갭 매트릭스의 유일한 `NONE` 영역 |
| IG-020 레거시 정리 | **OQ-5** |
| IG-021 클라 판정 제거 | IG-007 대기 |
| ~~IG-018 EditMode 인프라~~ | **이터레이션 35 에서 DONE.** asmdef 이관이 필요하다는 내 주장이 틀렸다 |
| IG-023 클라이언트 예측 | 진행 가능하지만 **AS-8 이 "지금 예측이 없다" 를 기록**했고 §8 은 예측을 요구하지 않는다. 로컬 서버에서는 증상이 없으므로 실측 없이 손대면 무엇을 고쳤는지 알 수 없다 |

**따라서 다음 이터레이션이 할 수 있는 가장 값 있는 일은 두 가지다:**

1. **사람이 2클라이언트 스모크를 한 번 돌리는 것** — 여전히 한 번도 실행되지 않았다. 409개 테스트가
   서버 판정을 고정했지만 **그것이 화면에 도달하는지는 확인되지 않았고**, 클라이언트 적용 경로에는
   자동 테스트가 없다(IG-018 미비). 적용 경로의 버그는 컴파일을 통과한다. 절차와 태스크별 확인
   항목은 아래 "사람이 해야 하는 검증" 표에 있다.
2. **OQ-1~7 에 답하는 것** — 우선순위와 "답 하나가 무엇을 여는지" 는 위의 질문 리포트에 있다.

루프가 계속 돌면 IG-018(asmdef ADR 초안)이 유일하게 남은 자기완결적 작업이다 — 클라이언트 적용
경로에 테스트를 붙일 길을 여는 일이고, 위 1번의 대체 수단이기도 하다.

### 이전 계획 (이터레이션 33 시점)

서버가 발사 알림을 보내고 클라이언트는 **아직 무시한다**(`DispatchEvent` 의 `default`). 할 일:
`NetworkClient` 가 `FireEvent` 를 파싱해 노출하고, 누군가 그것으로 예광탄을 그린다.

설계 판단이 하나 남아 있다 — **자기 발사는 두 번 그리지 않아야 한다.** 로컬 `Bullet` 이 이미
예광탄을 그리므로 `ShooterId == LocalPlayerId` 인 알림은 버리거나, 반대로 **로컬 `Bullet` 을 끄고
알림만으로 그린다.** 후자가 일관되지만(모든 예광탄이 서버 판정과 같은 곳에서 출발한다) 자기
사격의 반응이 한 RTT 늦어진다. 전자가 안전하고, **히트마커 불일치는 그대로 남는다.**

**그 뒤로는 진행 가능한 규칙·표현 태스크가 없다.** OQ-1~7 의 답이 없으면 §10 질문 리포트 상태이고,
**2클라이언트 스모크가 여전히 한 번도 실행되지 않았다** — 그것이 이 프로젝트에서 가장 값이 큰
미실행 검증이다. 그리고 이번 커밋의 새 `Shared` 파일은 **에디터를 한 번 열어 `.meta` 를 커밋해야
한다.**

### 이전 계획 (이터레이션 32 시점)

남은 TODO 중 규칙·표현 작업은 이것 하나뿐이고(나머지는 BLOCKED 6개 + IG-018 asmdef ADR 선행 +
IG-023 예측), 이것이 끝나면 루프는 §10 의 질문 리포트 상태로 완전히 들어간다.

**ADR 초안부터 쓴다.** 새 `EventKind` 를 추가하는 것은 이 프로젝트가 일관되게 피해 온
**"전문이 아니라 알림"** 을 만드는 일이다 — `Bounded(32, DropOldest)` 채널에서 놓친 발사는 다음
상태로 수렴하지 않는다. 다만 잃는 것이 **예광탄 하나**이고 판정에는 영향이 없으므로 허용 가능한
예외로 보이며, **그 판단과 한계를 ADR 0003 에 적고 시작한다.**

설계: 총알 상태를 매 틱 싣지 않는다(8인 룸 32발은 비싸고 `EntityFlags` 는 8비트를 다 썼다).
**발사 이벤트(시작점·방향·틱)만 보내고 비행은 클라이언트가 재현한다** — 등속 직선이고 중력이
0 이므로 재현이 정확하다. 크기가 바뀌지 않는 새 kind 이므로 프로토콜 버전은 그대로다(모르는 kind 는
클라이언트의 `DispatchEvent` 가 `default` 로 무시한다).

**그 뒤:** OQ-1~7 의 답이 없으면 더 진행할 규칙이 없다. 그리고 **2클라이언트 스모크가 여전히 한
번도 실행되지 않았다** — 그것이 지금 이 프로젝트에서 가장 값이 큰 미실행 검증이다.

**IG-028b(발사 이벤트) 는 남은 것 중 가장 크고 유일하게 설계 판단이 필요하다.** 새 `EventKind` 를
추가하는 것은 이 프로젝트가 지금까지 피해 온 **"전문이 아니라 알림"** 을 하나 만드는 일이다 —
`Bounded(32, DropOldest)` 채널에서 놓친 발사는 수렴하지 않는다(총알 하나가 안 보일 뿐이므로
허용 가능하지만, 그 판단은 ADR 로 남기는 편이 맞다). 그 태스크를 열 때 ADR 초안부터 쓴다.

### 이전 계획 (이터레이션 28 시점)

IG-028a 로 탄약이 화면에 도달했다. 남은 연출 구멍은 **남이 쏜 총알이 보이지 않는 것**이다 —
각 클라이언트가 자기 `Bullet` 만 만들고, 서버가 날리는 총알에는 표현이 없다. 히트마커가 로컬
총알로 뜨는 불일치도 같은 뿌리다.

**설계 판단(예정): 총알 상태를 매 틱 싣지 않는다.** 8인 룸에서 32발을 매 틱 보내는 것은 비싸고
`EntityFlags` 는 8비트를 다 썼다. **발사 이벤트(시작점·방향·틱)만 보내고 비행은 클라이언트가
재현**하는 편이 훨씬 싸다 — 총알은 등속 직선이고 중력이 0(`bulletGravity` 기본값)이므로 재현이
정확하다. 다만 **새 `EventKind` 를 추가하는 것은 "전문이 아니라 알림" 을 하나 만드는 일**이고,
이 프로젝트는 `Bounded(32, DropOldest)` 채널 때문에 알림을 피해 왔다 — 발사는 놓쳐도 다음 상태로
수렴하지 않으므로(총알이 하나 안 보일 뿐) 허용 가능하지만, **그 판단을 ADR 로 남길지 검토해야 한다.**

대안(더 작은 것들): IG-024(씨드 중복 정리) → IG-025(Jump 엣지) → IG-026(E 키 통합) →
IG-027(열쇠 흩뿌리기).

### 이전 판단 (이터레이션 26 시점): §10 조건이 사실상 성립했다

규칙 이관이 끝났고 문서도 정합해졌다. 남은 TODO 를 성격별로 보면:

| 남은 것 | 성격 | 왜 지금 할 수 없는가 / 안 하는가 |
|---|---|---|
| IG-015 장치 파괴 | **규칙** | IG-013(장치 사용)이 OQ-1 로 BLOCKED. 부술 대상의 사용 규칙이 없다 |
| IG-007 승리 조건 | **규칙** | OQ-2·OQ-6. **클라이언트에 남은 마지막 판정** |
| IG-016 체인 견인 | **규칙** | OQ-4 (경로 방식) |
| IG-017 근접 보이스 | **규칙** | OQ-3 (전체가 미구현 영역) |
| IG-020 레거시 정리 | 정리 | OQ-5 (삭제해도 되는지) |
| IG-021 클라 판정 제거 | 정리 | IG-007 대기 |
| ~~IG-018 EditMode 인프라~~ | 검증 수단 | **이터레이션 35 에서 DONE** — asmdef 불필요 |
| IG-023 예측, IG-024 씨드 중복, IG-025 Jump 엣지, IG-026 E 키 통합, IG-027 열쇠 흩뿌리기, IG-028 서버 총알 | 품질·표현 | 진행 가능하지만 **규칙이 아니다** |

**즉 규칙 태스크는 전부 BLOCKED 이고, 진행 가능한 것은 전부 품질·표현·검증 수단이다.** §10 의
두 번째 조건("남은 태스크가 전부 BLOCKED")을 문자 그대로 만족하지는 않지만, 이 루프의 목표는
§1 이 정한 "인게임 기능과 게임 로직의 서버 권위화" 이고 **그 목표에 남은 것은 OQ 의 답뿐이다.**

다음 이터레이션에서 할 일: 갭 매트릭스 전체를 재검증하고 **최종 리포트 + 질문 리포트**를 쓴다
(구현 완료 / DEFERRED 와 사유 / OQ-1~7 / 알려진 제약 / 남은 위험). 그 뒤에도 루프가 계속 돌면
품질 태스크를 우선순위대로 집는다 — **IG-028(서버 총알)이 가장 값이 크다**: 지금 남이 쏜 총알이
화면에 없고 히트마커가 서버 판정과 어긋난다.

### 이전 판단 (이터레이션 25 시점): 루프가 §10 의 종료 조건에 가까워졌다 갭 매트릭스에서 서버 권위가 필요한 규칙 중 **판정이 아직
클라이언트에 남은 것은 승리 조건(IG-007)뿐이고 그것은 BLOCKED** 다. 남은 TODO 는 전부 정리·품질·
표현 항목이거나 BLOCKED 의 후속이다.

**IG-015 — 장치 파괴(4발)** 가 유일하게 남은 규칙 태스크지만 **IG-013(장치 사용)에 의존하고 그것이
OQ-1 로 BLOCKED** 다. 부술 대상의 사용 규칙이 정해지지 않은 상태에서 파괴만 만들면 §6.4 위반이다.

그래서 다음 이터레이션의 후보는 셋이다:
1. **IG-019 — 상수 정리·문서 갱신·죽은 경로 제거** (P4). 지금 값이 크다: `MatchConstants` 머리말을
   이미 한 번 고쳤고(IG-014b), `GameConfig` 에 서버로 옮겨간 값들의 사본이 남아 있는지 확인해야
   한다. **AS 14개·OQ 7개가 쌓여 있어 문서 정합성 점검 자체가 실질적인 작업이다.**
2. **IG-024 — `MatchManager` 의 씨드 계산 중복 정리** (P4). 작고 명확하다.
3. **IG-028 — 서버 총알 그리기** (P3). 새 기능이지만 규칙이 아니라 표현이고, 스냅샷에 무엇을
   실을지가 설계 판단이다.

**IG-019 를 권한다** — 규칙 이관이 한 계통 끝난 직후가 남은 사본과 죽은 경로를 걷어낼 때이고,
그것을 미루면 다음 사람이 어느 값이 살아 있는지 코드로 판별해야 한다.

**§10 최종 리포트를 준비할 시점이기도 하다.** 남은 태스크가 전부 BLOCKED 이거나 P4 정리라면
루프는 "질문 리포트" 조건에 해당한다 — OQ-1~7 의 답이 있어야 그 이상 진행되지 않는다.

**IG-012 계열이 전부 끝났다.** 목표 계통(습득·삽입·문 개방·탈출)이 서버 권위이고, 클라이언트는
전부 전문으로 받는다. 남은 것은 **전투**다 — 그리고 그것이 클라이언트에 남은 마지막 큰 판정이다
(`MatchManager.ReportHit`: 첫 피격 출혈 + 순간이동, 두 번째 사망, `hitImmunity` 0.75초).

IG-014 는 확실히 8파일을 넘는다. 예상 분할:
- **c1** 서버 발사체(스윕 레이캐스트) + 탄약·재장전 — `Fire` 비트는 이미 서버에 도달한다
- **c2** 피격 규칙(출혈·순간이동·사망) + `EntityFlags.Bleeding` + `MatchParticipant.hits`
- **c3** 클라이언트 적용 + 로컬 `ReportHit` 차단

`Escaped` 가 쓴 "몸을 지우지 않고 플래그만 세운다" 와 IG-012c1 의 `ProjectWire` 순서가 그대로
재사용된다. **`hitImmunity` 를 틱으로 옮기는 것**이 첫 번째 작업이 될 것이다.

주의: 기획서 §4.1 의 "1회 피격 = 출혈 + 순간이동" 에서 **순간이동 착지점**은 `TryRandomFreeFloor`
가 답한다(AS-7 이 셀 중심을 돌려주는 이유가 여기서 값을 한다 — 착지점은 플레이어다).

**IG-018 은 여전히 열지 않았다.** Unity 의 asmdef 는 **`Assembly-CSharp` 를 참조할 수 없다** —
테스트 asmdef 에서 클라이언트 스크립트를 보려면 `Assets/Scripts` 전체를 asmdef 로 옮겨야 하고,
그것은 MCP 커맨드·에디터 스크립트의 참조까지 흔드는 큰 변경이다. D-2 도 이미 "순수 로직 검증은
`dotnet test`" 로 결정해 두었다. **IG-018 을 열 때는 그 asmdef 이관을 ADR 로 먼저 세운다.**

BLOCKED 6건(IG-007, IG-013, IG-016, IG-017, IG-020, IG-021)은 여전히 OQ-1·2·3·4·5·6 을 기다린다.
**IG-012c 가 끝나면 남은 진행 가능 태스크가 거의 소진되고, OQ 의 답이 없으면 루프가 §10 의
"남은 태스크가 전부 BLOCKED" 조건에 가까워진다.**

---

### 이전 판단 기록 (이터레이션 39)

**"테스트가 6개 있다" 와 "그 규칙이 검사된다" 는 다르다.** `EndMatch` 를 쓰는 테스트가 6개 있었고
전부 통과하는데, **전부 방장에서만 보냈다.** 즉 그 6개는 "방장이 끝낼 수 있다" 를 여섯 번 검사하고
"방장이 아니면 끝낼 수 없다" 를 한 번도 검사하지 않았다. `IsAuthorized` 를 지우는 변경은 그 여섯 개를
모두 통과한다 — 그리고 그것이 지키는 것이 **§9 의 "클라이언트 권위로 게임 결과를 결정하지 않는다"**
다. 결과 코드를 서버가 정하지 않는 지금, 이 인증이 그 규칙의 전부다.

**세 이터레이션의 공통 패턴이 보인다.** 37·38·39 가 찾은 것은 셋 다 **"아닌 쪽" 이 검사되지 않은
것**이었다 — 격자가 **없는** 구성, 판정이 **겹치는** 틱, 권한이 **없는** 세션. 정상 경로는 자연히
테스트가 쌓이지만 거부·열화·경합 경로는 누가 일부러 찾지 않으면 비어 있다. 그 기준을 다음
이터레이션 계획에 적어 두었다.

**부수적으로 의도를 하나 못질했다.** `Match.ForceEnd` 가 `KeysInserted`·`Escapes` 를 0 으로 만들지
않는 것은 **결과 화면이 최종 수치를 보여 주기 위한 것**이다(`Reset` 만 지운다). 그 의도가 코드에는
없었으므로, 누군가 "종료인데 왜 안 지우지" 하고 초기화를 넣으면 결과 화면이 빈다. 테스트가 그것을
막는다.

### 이전 판단 기록 (이터레이션 38)

**테스트를 쓰려다 미결 규칙을 찾았다.** 후보는 "`Downed`·`Escaped` 가 동시에 성립하는 경로에
테스트가 없다" 였는데, 순서를 확인해 보니 **그 순서가 이미 규칙을 정하고 있었다** — 탈출이
총알보다 앞이므로 유지 시간의 마지막 틱에 도착한 총알은 끊지 못한다. 그리고 그것은
`EscapeHoldTime` 이 스스로 적어 둔 목적("Seeker 가 끊을 수 있는 순간")과 어긋난다.

**고치지 않고 OQ 로 올렸다.** 순서를 바꾸는 것이 의도에 맞아 보이지만 `EscapesToWin` 이 2 이므로
그 한 틱이 **매치 결과를 바꾼다** — §6.4 의 "게임플레이 영향이 큰 판정 방식" 이다. 대신 현재
동작을 테스트로 고정해서, 답이 오면 **무엇이 뒤집히는지가 즉시 보이게** 했다. 미결을 미결로
남기면서도 회귀는 막는 방법이다.

**대조군 없이는 이 테스트가 무의미했다.** "탈출이 이겼다" 는 총알이 빗나간 것과 구별되지 않는다.
총알을 한 틱 앞세운 대조군을 넣어 **같은 배치에서 피격이 성립함**을 보였고, 그것이 창이 정확히 한
틱이라는 것도 함께 증명한다. **"기대한 일이 일어나지 않았다" 를 검사할 때는 그 일이 일어나는
조건을 함께 검사해야 한다.**

**후보 하나를 폐기했다.** "스폰을 3개로 늘리면 '술래는 총을 맞지 않는다' 를 검사할 수 있다" 고
이터레이션 37 에 적었는데, 확인해 보니 **인원과 무관하게 불가능하다** — 총을 쏘는 것은 Seeker
뿐이고 유일한 비-Runner 가 그 자신(소유자)이므로 역할 검사와 소유자 검사가 완전히 겹친다.
쓰기 전에 확인해서 픽스처를 건드리지 않았다.

### 이전 판단 기록 (이터레이션 37)

**"진행 가능한 태스크가 없다" 를 두 이터레이션 적어 두고 나서, 실제로는 있었다.** 백로그의 규칙
태스크가 전부 OQ 대기라는 것은 맞지만, **이미 구현된 규칙 중 테스트되지 않은 경계**는 백로그에
항목이 없어서 보이지 않았다. `test-room` 이 격자를 내놓지 않는다는 것(D-6)과 `MultiplayerTest` 가
그 룸에 붙는다는 것은 둘 다 문서에 있었는데, **그 둘을 곱하면 "개발 루프의 기본 경로가 열화
모드다"** 가 된다 — 그 곱셈을 아무도 하지 않았다.

**그것이 특히 위험한 종류다.** 격자가 있는 맵의 테스트는 순간이동 경로를 바꾸는 변경에 전부
통과한다. 그래서 그 변경은 **자동 테스트를 통과하고 개발 루프만 조용히 깨뜨린다** — 그리고 개발
루프가 깨지면 사람이 스모크를 돌리는 것 자체가 막힌다(지금 가장 값이 큰 미실행 검증이 그것이다).

**내 테스트가 이터레이션 24 와 같은 실수를 반복했다.** 헬퍼가 판정 대상의 자리를 고정하는데 호출
**전** 위치와 비교해, 헬퍼가 옮긴 것을 순간이동으로 읽었다. 24 에서 같은 것을 고치고 기록까지
했는데 다시 했다 — **자리를 옮기는 헬퍼는 그 자리를 반환하게 만드는 편이 낫다.** 주석으로
경고하는 것으로는 부족했다.

### 이전 판단 기록 (이터레이션 36)

**차단 사유가 세 번째로 접촉하자 사라졌다 — 그리고 이번엔 두 겹이었다.**

첫 겹: IG-029 를 "ADR 이 선행되는 세 갈래" 로 세워 두었는데, 서버가 이미
`InternalsVisibleTo("Modules.Tests")` 를 쓰고 **`Architecture.Tests` 가 그 줄의 존재를 강제**한다.
프로젝트가 "테스트는 어떻게 internal 을 보는가" 에 답해 둔 것이므로 §5.4 의 갈림이 아니었다.

둘째 겹: 그래서 `InternalsVisibleTo` 를 넣고 테스트를 썼는데, 다 쓰고 보니 **어느 테스트도
`internal` 을 쓰지 않았다.** 적용 메서드는 전부 공개이고 그것이 쓰는 값은 전부 공개 프로퍼티로
읽힌다. 그리고 `SetBleeding` 으로 상태를 미리 만드는 대신 `AcceptCombatState` 로 구동하는 것이
**실제 경로를 지나므로 더 나은 테스트**였다. 넣었던 파일을 지웠다 — **투기적 변경이었고, 필요할
것 같아서 미리 넣는 것이 §9 가 막는 것과 같은 종류다.**

**남은 진짜 장벽은 하나(`private config`)뿐이었고 프로덕션을 고치지 않고 풀렸다** —
`SerializedObject` 는 인스펙터가 쓰는 API 다. **"테스트를 위해 프로덕션 API 를 넓힌다" 를 결론으로
내리기 전에 에디터 쪽 수단을 먼저 봐야 한다.**

**환경 의존을 미리 확인했다.** 출혈 검사가 `BloodTrail.Begin` 을 건드리므로 EditMode 에서 터질 수
있다고 보고 그 함수를 읽었다 — 참조만 저장하고 `MatchManager.Instance` 를 null 가드한다. 실행을
확인할 수 없을 때는 **터질 수 있는 지점을 코드로 확인하는 것이 다음으로 좋은 검증**이다.

### 이전 판단 기록 (이터레이션 35)

**세 이터레이션 동안 인용해 온 차단 사유가 거짓이었다.** 이터레이션 21·26·32 에서 "Unity 의
asmdef 는 `Assembly-CSharp` 를 참조할 수 없으므로 스크립트 전체를 옮겨야 한다" 고 적고 IG-018 을
미뤘다. 그 전제는 **asmdef 로 테스트를 만들 때만** 성립한다 — 생성된 프로젝트를 열어 보니
`Assembly-CSharp-Editor` 가 **`nunit.framework`·TestRunner·`Assembly-CSharp` 를 이미 참조한다.**
`Assets/Editor/` 아래 테스트를 두면 아무 설정도 필요 없었다.

**"확인해야 한다" 고 적은 것을 확인하지 않고 근거로 재사용한 것이 문제다.** 매 이터레이션마다
그 문장을 복사해 다음 이터레이션의 판단 근거로 썼고, 세 번째에는 그것이 확립된 사실처럼 보였다.
IG-014a(이미 있던 레이캐스트)·IG-025(없던 결함)와 같은 계열인데 이번은 **내 기록이 스스로를
보강한 경우**라 더 나쁘다.

**두 번째 정정:** 이터레이션 33 의 IG-028b1 기록에 "`NVproject/Shared.csproj` 에 `Compile Include`
를 손으로 추가해야 했다" 를 변경 파일로 적었는데, **`*.csproj` 는 gitignore 대상이라 커밋되지
않았고 Unity 가 재생성한다.** 즉 그것은 **내 오프라인 컴파일 검증을 위한 로컬 조치**이고 다른
사람이 할 일이 아니다. 커밋 목록에 넣은 것이 부정확했다.

**컴파일과 실행을 구별해 적었다.** 이 태스크의 검증은 "실제 Unity DLL 에 대해 컴파일된다" 까지다 —
Test Runner 가 `Assembly-CSharp-Editor` 의 테스트를 **발견하고 실행하는지**는 에디터가 필요하고
확인하지 못했다. §7 이 금지하는 것이 정확히 그것을 "통과" 로 적는 일이다.

**그리고 인프라를 열자마자 그 한계가 드러났다.** `internal` 과 `[SerializeField] private` 가 다른
어셈블리에서 보이지 않으므로 적용 경로의 대부분에 닿을 수 없다. 테스트를 위해 프로덕션을 고치는
것은 §9 가 막는 리팩터링이므로 **IG-029 로 올리고 ADR 을 선행하게 두었다** — 지금 뚫으면 그것이
설계 결정 없이 들어간다.

### 이전 판단 기록 (이터레이션 34)

**"더 정확한" 보정을 일부러 하지 않았다.** 발사 알림에 틱을 실어 둔 이유가 "늦게 도착한 만큼
총알을 진행시키는 것" 이었는데, 막상 구현하려니 **원격 몸이 보간 때문에 100ms 과거에 그려진다**는
사실과 충돌한다. 예광탄만 현재로 당기면 **그것을 쏜 몸의 총구에서 나가지 않는다.** 개별 요소의
정확도보다 **표현 전체의 일관성**이 낫다 — 원격의 모든 것이 같은 만큼 과거에 있어야 한다.

틱을 와이어에서 빼지는 않았다. 보정을 원하는 클라이언트가 쓸 수 있고, ADR 0003 이 그것을 실은
근거도 "쓸 수 있게 둔다" 였다. **다만 지금 쓰지 않는다는 사실을 적었다** — 쓰이지 않는 필드를
설명 없이 남기면 다음 사람이 버그로 읽는다.

**자기 발사를 알림으로 갈아타지 않은 것도 같은 종류의 판단이다.** 일관성만 보면 모든 예광탄이
서버 알림에서 나오는 편이 깔끔하지만, 로컬 `Bullet` 은 히트마커·발사음·반동의 타이밍도 만든다.
그것을 한 왕복 늦추는 것은 §8 이 로컬 연출에 예측을 허용하는 이유를 정면으로 거스른다.
**남는 대가(히트마커 불일치)는 판정이 아니라 표시**이므로 감당 가능하다.

### 이전 판단 기록 (이터레이션 33)

**규칙을 깨야 할 때는 규칙을 다시 쓴다.** 이 프로젝트의 모든 서버 발신 메시지는 전문이었고,
`architecture.md` 가 "한 번짜리 알림을 보내지 않는다" 를 명문화해 두었다. 발사는 그 틀에 맞지
않는데 — 상태가 아니라 사건이다 — 그렇다고 예외를 조용히 만들면 다음 사람이 그것을 선례로 쓴다.
그래서 **ADR 0003 에 예외를 좁게 정의했다: "결과가 전문으로 따라오는 사건에만 알림을 쓴다."**
판단 기준을 한 문장으로 남겼다 — **놓쳤을 때 틀린 상태가 남는가.**

**거부한 대안을 적는 것이 결정보다 중요했다.** 특히 (B) "최근 발사 목록을 전문으로 반복" 은
멱등성을 지키려는 자연스러운 시도인데, 중복 제거를 넣는 순간 그 이득이 사라지고 2Hz 로는 예광탄이
60m 늦는다 — **늦은 예광탄은 없는 예광탄보다 나쁘다**(실제 탄도와 다른 곳을 가리킨다). 그것이 왜
안 되는지를 적지 않으면 다음 사람이 같은 시도를 한다.

**핵심 테스트가 "오지 않음" 을 검사한다.** 전문 테스트들은 "주기마다 또 온다" 를 확인하는데,
여기서는 200틱을 돌려도 수가 1 이어야 한다. 알림의 정의가 그것이므로 그 검사가 없으면 누군가
반복 전송을 추가해도 아무것도 실패하지 않는다.

**`Shared` 에 파일을 추가할 때의 트랩을 다시 밟았다.** 서버 테스트 409개가 전부 통과하는데
클라이언트만 `CS0246` 으로 깨졌다 — Unity 가 생성한 `Shared.csproj` 의 `Compile` 목록에 새 파일이
없어서다. `NVproject/CLAUDE.md` 가 기록해 둔 것이고, **서버만 확인하고 넘어갔다면 다음 이터레이션에
"왜 클라이언트가 안 되는지" 를 찾았을 것이다.**

### 이전 판단 기록 (이터레이션 32)

**클라이언트 구현을 그대로 옮기지 않은 세 번째 경우다.** 클라이언트의 `ScatterKeys` 는 각 열쇠의
각도를 따로 뽑는데, **그러면 각도가 겹쳐 두 열쇠가 같은 자리에 놓일 수 있다** — 흩뿌리는 목적이
겹침을 피하는 것이므로 겹침이 곧 실패다. 원 위에 균등 배분하고 시작 각도만 뽑으면 겹침이
불가능해지고 난수 draw 도 하나로 줄어든다. (앞선 두 경우: `InteractHeight` 통일, `KeyPickupHeight`
의 단일 출처.) **기존 구현을 인용하는 것과 그 구현의 결함까지 인용하는 것은 다르다.**

**스냅하려던 것을 멈췄다.** 처음 계획은 흩뿌린 좌표를 `TryNearestFreeFloor` 로 스냅해 벽 안에
놓이지 않게 하는 것이었는데, 그 함수는 **셀 중심을 돌려준다**(AS-7 이 그렇게 정했다). 반경 0.7m
안의 후보가 전부 같은 셀 중심으로 모이므로 **스냅이 흩뿌림을 무효로 만든다.** 대신 스냅이 필요
없는 이유를 찾았다 — 습득 반경(1.4m)이 흩뿌림 반경(0.7m)보다 크므로 벽 쪽 열쇠도 사망 지점에서
닿는다. **그 관계를 단정문으로 고정했다**(IG-025 의 점프 관계와 같은 방식).

**AS 하나를 철회했다.** AS-15("한 점에 놓는다")는 IG-014b 에서 "반경이 기획서에 없으므로 만들지
않는다" 는 근거로 세운 것인데, 이번에 그 값이 클라이언트에 있다는 것을 확인하고 AS-16 으로
교체했다. 가정은 영구 기록이 아니라 **다음 조사가 뒤집을 수 있는 잠정 판단**이다.

**난수 수열은 나누지 않았다.** IG-014b 가 "용도별로 나눈다" 고 적었지만 그 근거는 "한쪽의 draw 수
변경이 다른 쪽을 바꾼다" 였고, 흩뿌림과 순간이동은 **같은 판정(`ApplyHit`)의
결과**이므로 독립적 재현이 필요하지 않다. 규칙을 기계적으로 적용하지 않고 근거로 되돌아가 판단했다.

### 이전 판단 기록 (이터레이션 31)

**태스크가 지정한 수단을 거부하고 목적을 달성했다.** IG-026 은 "E 키 읽기를 `ConsumeInteract` 로
통합" 이라고 적혀 있었지만, 실제 결함은 **`MovementLocked` 게이트가 와이어 경로에만 있는 것**이었다.
게이트만 맞추면 목적이 달성되고, 래치는 **소비자가 정확히 하나** 라는 성질을 유지한다 —
`PlayerInteractor` 도 소비하게 하면 조건이 겹치는 순간 한쪽이 신호를 먹고, 그것은 IG-012b1 이
일부러 피한 위험이다. 게다가 래치가 해결하는 문제(30Hz 틱과 프레임의 불일치)는 **오프라인에
존재하지 않는다.** 내가 몇 이터레이션 전에 적어 둔 계획이 반드시 최선의 수단은 아니다.

**기획서가 게이트의 타당성을 답했으므로 OQ 로 올리지 않았다.** 시작할 때 "§4.3 의 '행동불가'
해석이 애매하면 OQ" 라고 적어 두었는데, 열어 보니 §4.3 이 **"3초 행동 불가"** 라고 명시하고 §5.1 이
**"전체 정지"** 라고 적는다. 장치 사용은 행동이므로 답이 있다. **§6.4 는 답이 없을 때의 규칙이고,
확인해 보지 않고 "애매할 것" 이라고 미루는 것에는 적용되지 않는다.**

프롬프트가 함께 사라지는 것을 확인했다(게이트가 `FindTarget` 앞이다). 쓸 수 없는 동안
"[E] INSERT KEY" 를 띄우는 것은 IG-012c1 이 고친 "표시된 자리에 서 있는데 아무 일도 안 일어난다"
와 같은 종류의 거짓말이다.

### 이전 판단 기록 (이터레이션 30)

**"정리" 태스크에서 실제 결함이 나왔다.** IG-024 를 "죽은 코드일 것" 이라는 예상으로 열었는데,
중복된 씨드 식이 **한 호출 안에서 두 번 돌고 있었고** 기본 설정에서 두 `Environment.TickCount`
읽기가 서로 다른 값을 준다. 목표물 배치와 로컬 `Random` 이 다른 씨드를 받고 있었다.

**눈에 보이는 고장이 없는 결함을 어떻게 다룰지가 이 태스크의 판단이었다.** 두 RNG 가 서로 다른
것을 먹이므로 화면에는 아무 증상이 없다. 깨진 것은 **불변식**("한 매치 한 씨드")이고, 그 결과
`PlacementSeedOverride` 로 배치를 재현하려는 시도가 절반만 동작했다 — 목표물은 재현되고
순간이동 지점은 매번 달랐다. **증상이 없는 것과 고장이 아닌 것은 다르다.**

**구조로 막았다.** 씨드를 `PlaceObjectives(int seed)` 의 인자로 만들어 두 번 계산하는 코드를 쓸
수 없게 했다 — 클라이언트에는 자동 테스트가 없으므로(IG-018 미비) 테스트로 고정할 수 없고,
그럴 때 다음으로 좋은 것은 그 상태를 표현 불가능하게 만드는 것이다.

**죽어 보이는 것을 지우지 않았다.** `PlacementSeedOverride` 는 쓰는 곳이 없어 죽은 것처럼 보였지만
문서가 이미 "아무도 쓰지 않는다 — 테스트에서 배치를 재현할 때 설정한다" 고 밝히고 있었다.
**"참조 0" 은 죽음의 증거가 아니다** — 의도된 수동 훅일 수 있고, 그것을 구별하는 것은 주석이다.

그리고 **내가 IG-019 에 쓴 문서 주장 하나가 부정확했다.** "오프라인에서는 `GameConfig.placementSeed`
가 `PlacementSeedOverride` 를 먹인다" 고 적었는데 그 둘은 우선순위가 다른 별개의 입력이다.
문서를 고치는 이터레이션 다음마다 그 문서가 다시 틀리는 패턴이 세 번째다(IG-019 → 27 → 30).

### 이전 판단 기록 (이터레이션 29)

**결함을 고치러 열었고 결함이 없다는 것을 확인하며 닫았다.** IG-012b2 에서 "`Jump` 도 엣지인데
반복되므로 착지 순간에 재점프가 성립한다" 고 적었는데, 이번에 수치를 보니 성립할 수 없다 —
반복 3틱(0.1초) 대 체공 21틱(0.7초)이다. **그때 나는 구조만 보고 수치를 보지 않았다.** IG-022
(클라이언트 예측)·IG-014a(이미 있던 레이캐스트)에 이어 세 번째로, 계획에 적힌 전제가 코드와
달랐던 경우다.

**"고치지 않는다" 가 답일 때도 남길 것이 있다.** 지우면 오히려 잃는 것이 있었고(공중 입력이 3틱
재시도되어 점프 버퍼로 동작한다), **실제 위험은 그 무해함이 두 상수의 대소 관계에 의존한다는
사실이 코드 어디에도 적혀 있지 않은 것**이었다. 지연 보상을 위해 반복 상한을 올리는 변경은
자연스럽고, 그 순간 한 번의 키 입력이 연속 점프가 된다. 그래서 **관계를 단정문으로 고정했다** —
코드를 고치는 대신 코드가 의존하는 사실을 테스트로 만들었다.

**테스트의 load-bearing 성을 정직하게 적었다.** 오늘의 수치에서는 `Jump` 를 지워도 두 테스트가
모두 통과한다 — 반복이 착지에 닿지 않으므로 두 구현이 구별되지 않는다. IG-012b2 의
`WithoutEdgeButtons` 때와 같은 상황이고, 같은 방식으로 적었다: **무엇을 막고 있는지 확인하지 않고
"이것이 버그를 막는다" 고 쓰면 그것도 실행하지 않은 검증을 통과로 적는 것이다.**

### 이전 판단 기록 (이터레이션 28)

**필드를 늘리기 전에 죽은 필드를 찾았다.** 탄약을 실으려면 `MatchParticipant` 가 6B 가 되고
프로토콜을 또 올려야 한다고 지난 이터레이션에 적어 두었는데, 열어 보니 **`Flags` 바이트가 IG-014b
이후 영구히 0 이었다.** 그때 "출혈·탈출·쓰러짐은 매 틱 스냅샷으로 가야 하므로 여기 싣지 않는다"
고 결정했고 그 자리를 비워 둔 채 잊은 것이다. 크기도 버전도 그대로 두고 탄약이 그 자리를 쓴다.

**대가를 정직하게 적었다:** 크기가 같으므로 `WireSizeTests` 가 이 변경을 잡지 못한다. 그래서
바이트 위치를 직접 비교하는 테스트로 못질했고, 그 테스트가 이미 있었던 것이 도움이 됐다.

**필터가 양방향이 됐다는 것이 이 태스크의 개념적 산출물이다.** 지금까지 역할 필터는 한 방향이었다
— Seeker 에게서 목표 정보를 뺀다. 탄약은 반대다: **Runner 에게서 술래의 탄약을 뺀다.** 총성이
"한 발 줄었다" 를 알려 주는 것이 이 게임이 그 정보를 전달하는 방식이고, 숫자를 주면 그것을 무료로
넘긴다. 기존 테스트 `두_사본의_바이트가_열쇠_자리에서만_다르다` 가 **한 방향이라는 전제를 못질하고
있었으므로 정확히 옳은 이유로 실패했다** — 이름과 내용을 함께 고쳤다.

**"서버 권위" 와 "화면에 도달함" 이 다르다는 것을 R-9.3 이 보여 줬다.** 탄약 판정은 IG-014a 부터
서버였는데 HUD 는 다섯 이터레이션 동안 로컬 값을 그리고 있었다. 갭 매트릭스에서 그 행만
`PARTIAL` 로 남겨 둔 것이 이 태스크를 찾게 했다.

### 이전 판단 기록 (이터레이션 27) — 재검증 + 리포트

**재검증이 구멍 하나를 찾았고, 그것을 내가 지난 이터레이션에 만들었다.** `NVproject/CLAUDE.md` 에
"`TryPickUpKey`·`TryInsertKey`·`TickEscapes` 가 refuse 한다" 고 적었는데 **`TryPickUpKey` 에는
게이트가 없었다.** 살아 있는 호출 경로는 이미 막혀 있었으므로(`KeyPickup.Update` 가 폴링을 멈춘다)
동작은 옳았지만, 문서가 거짓이었고 패턴도 어긋났다. 게이트를 추가했다 — **"우연히 도달 불가능한
public 메서드" 는 새 호출자 하나 거리에 있는 두 번째 심판이다.**

이것이 §10 이 "갭 매트릭스 전체 재검증 후 리포트" 라고 정해 둔 이유다. 문서를 고친 다음
이터레이션에 그 문서를 코드로 검증하지 않으면, 방금 고친 거짓이 새 거짓으로 바뀐 채 남는다.

**갭 매트릭스의 `PARTIAL` 8개가 실은 `DONE` 이었다.** R-1.6(이동 잠금)·R-2.1(총기)·R-4.1(출혈
상태)·R-5.1(탈락)·R-5.3(리스폰 없음)·R-9.1(열쇠 진행도)·R-9.2(탈출·시계) — 각 태스크가 자기 행만
고치고 **파생 행을 놓쳐** 왔다. 30 DONE / 17 PARTIAL 로 갱신됐다.

**R-9.3(탄약 표시)은 정직하게 `PARTIAL` 로 남겼다.** 판정은 서버인데 **와이어에 탄약 자리가 없어**
HUD 가 로컬 값을 그린다 — "서버 권위" 와 "화면에 도달함" 이 다르다는 것을 이 행이 보여 준다.

**리포트에서 가장 중요한 문장은 "스모크가 한 번도 실행되지 않았다" 다.** 394개 테스트가 서버 판정을
고정했지만 그것이 **화면에 도달하는지**는 확인되지 않았고, 클라이언트 적용 경로에는 자동 테스트가
없다(IG-018 미비). 적용 경로의 버그는 컴파일을 통과한다.

### 이전 판단 기록 (이터레이션 26)

**정리 태스크에서 실제로 정리할 코드는 한 줄이었고, 거짓이 된 문서는 네 파일이었다.** IG-019 를
"상수 정리" 로 이해하고 시작했는데 `GameConfig` 에서 찾은 중복은 `hitImmunity` 하나였다. 반면
`NVproject/CLAUDE.md` 는 **"히트·열쇠·탈출은 여전히 각 클라이언트가 판정한다"** 고 적고 있었다 —
지난 열 번의 이터레이션이 정확히 그것을 뒤집었다. 루트 `CLAUDE.md` 는 프로토콜 2, 테스트 137개,
씨드가 전문에 실린다고 적고 있었다.

**이 프로젝트에서 `CLAUDE.md` 는 주석이 아니라 인터페이스다.** 다음 세션이 코드보다 먼저 읽는
문서이고, 거기 적힌 "클라이언트가 판정한다" 는 문장은 다음 사람에게 **클라이언트에 판정을 추가해도
된다는 허가**로 읽힌다. 규칙을 옮기는 작업의 마지막 단계는 그 문장을 고치는 것이다.

**문서 주장은 각각 코드를 열어 확인한 뒤 고쳤다.** 프로토콜 버전, `RoomStateHeader.WireSize`,
`EntityFlags` 의 사용 비트 수, `ButtonFlags.All` 마스크, 테스트 수 — 기억으로 적으면 방금 고친
거짓을 새 거짓으로 바꾸는 것이 된다.

**`conventions.md` 에 쌓아 둘 것이 열한 항목이었다.** 루트 `CLAUDE.md` 가 "30분 이상 걸린 문제는
거기 적어라" 고 하는데 이터레이션 17~25 동안 한 번도 적지 않았다 — 각 태스크의 `LOOP_PROGRESS`
항목에만 남겼다. 그 둘의 독자가 다르다: `LOOP_PROGRESS` 는 이 루프의 기록이고 `conventions.md` 는
**루프가 끝난 뒤에도 읽히는 트랩 목록**이다.

### 이전 판단 기록 (이터레이션 25)

**R-3.1 이 닫혔고, 그것을 위해 `Bullet` 을 한 줄도 고치지 않았다.** 계획서는 "`Bullet` 을 순수
표현으로 만들고 `SendMessageUpwards` 를 제거" 라고 적었지만, 차단을 `ReportHit` 한 곳에 두면
그 호출이 남아 있어도 아무 판정을 만들지 않는다. **삭제하지 않고 무력화하는 것이 더 작고 더
되돌리기 쉽다** — 오프라인 경로가 같은 코드를 계속 쓰기 때문이기도 하다. IG-012b3 의
`TryInsertKey` 와 정확히 같은 형태다.

**플래그를 하나로 합치지 않은 이유가 이 루프의 성질을 말해 준다.** `ServerOwnsObjectives` 와
`ServerOwnsCombat` 은 세션이 있으면 함께 켜지므로 하나로 합칠 수 있어 보인다. 그런데 **그 둘은
서로 다른 태스크에서 건너왔고, 여러 이터레이션 동안 서버가 한쪽만 판정했다.** 한 조각씩 옮기는
이관에서는 도메인별 플래그가 그 중간 상태를 정직하게 표현하고, 합친 플래그는 그 기간에
거짓말을 해야 한다.

**같은 결론에 다른 이유로 도달한 것을 구별해 적었다.** 열쇠 삽입 알림은 뺐고(서버가 "누가" 를
말하지 않는다), 탈출 알림은 숫자를 뺐고(수가 0.5초 늦게 온다), 피격 알림은 살렸다(전문이 대상을
말한다). 세 번 모두 "알림도 정보 규칙" 이라는 같은 원칙을 적용했지만 답이 달랐다.

**폴링으로 상태를 적용할 때는 반복 호출이 무해한지 확인해야 한다.** `SetBleeding` 은 `BloodTrail`
을 시작하므로 매 프레임 부르면 흔적이 매 프레임 다시 시작한다 — 증상은 "부상했는데 흔적이 전혀
안 남는다" 다. 값이 바뀔 때만 부르도록 했다. `AcceptCarriedKeys`·`AcceptObjectiveProgress` 는
멱등이라 괜찮았지만, 그것이 우연이었다는 것을 이번에 알았다.

### 이전 판단 기록 (이터레이션 24)

**`Alive` 를 내리려는 유혹이 있었고, 그 파일이 스스로 막아 주었다.** 사망을 표현할 비트를 찾다가
`EntityFlags.Alive` 를 보았는데, 같은 파일이 "이동 시뮬레이션 소유 / 클라이언트가 예측 / `StateHash`
에 들어간다" 고 적어 두었다. 매치 판정으로 그 비트를 내리면 예측 불가능한 값이 해시에 섞인다.
`Downed` 를 **합쳐지는 쪽**(`MatchFlags`)에 별도 비트로 두었다 — 그리고 그것이 8비트의 마지막
칸이라는 것도 적어 두었다.

**같은 판단을 세 번 하고 나서야 머리말이 틀렸다는 것이 보였다.** `MatchConstants` 는 "판정에만
쓰이고 화면에 나오지 않는 값은 여기 두지 않는다 — 무적 창이 그렇다" 고 적고 있었다. 그런데
`KeyPickupHeight`(IG-012a), `InteractHeight`(IG-012b2), 그리고 이번 `HitImmunity` 가 모두 그 규칙을
어기고 여기로 왔다. 실제 기준은 **"클라이언트가 이 값으로 계산하는가"** 이고 오프라인 경로가
그 규칙들을 여전히 판정하기 때문이다. **예외가 세 개면 규칙이 틀린 것이므로 머리말을 고쳤다.**

**반올림 방향의 이유가 태스크마다 다르다.** `FireIntervalTicks`(4.5→5)는 서버가 관대해지지 않게
하려는 것이었고, `HitImmunityTicks`(22.5→23)는 **창이 짧아지면 규칙 자체가 깨지기** 때문이다.
같은 "올림" 이지만 근거가 다르므로 각각 적었다 — 나중에 한쪽을 조정할 때 다른 쪽을 따라 바꾸지
않게 하려는 것이다.

**테스트가 흔들렸고 원인은 코드가 아니라 픽스처의 우연이었다.** 두 스폰이 X 만 다르므로(0 과 -2)
어느 쪽이 Seeker 로 뽑히느냐에 따라 +X 사격선에 Runner 가 놓인다. **필터 실행에서는 통과하고 전체
실행에서만 실패했으므로, 필터로만 확인하고 넘어갔다면 못 봤을 것이다** — 그리고 고친 뒤 5회 반복
실행으로 확인했다. 무작위가 섞인 판정을 검사할 때는 **판정 대상의 자리를 고정**하는 것이 답이다.

**검사할 수 없는 규칙을 검사한 척하지 않았다.** 기획서 §4 의 "술래는 총을 맞지 않는다" 는 2인
매치에서 실사격으로 확인할 수 없다(Seeker 는 한 명, Runner 는 쏠 수 없다). 억지 테스트를 쓰는
대신 **왜 확인할 수 없는지를 테스트 파일에 주석으로 남겼다.**

### 이전 판단 기록 (이터레이션 23)

**계획서를 믿기 전에 코드를 봤고, 그것이 이번의 수확이다.** IG-014 의 계획은 "`Shared` 에 스윕
레이캐스트 판정을 만든다" 였는데 **이미 있었다** — `Raycaster.RayAabb`, `CollisionWorld.Raycast`,
그리고 `CollisionTests` 의 테스트까지. 아무도 부르지 않아서 없는 것처럼 보였을 뿐이다. §5.3 의
"파일을 먼저 읽는다" 가 여기서 한 태스크만큼의 중복을 막았다.

**요 규약을 두 번 쓰지 않기 위해 헬퍼를 `PlayerMovement` 에 두었다.** 전방이 `(sin, 0, cos)` 라는
사실은 `ApplyHorizontal` 에만 있었다. 총알 방향을 다른 파일에서 다시 세우면 한쪽을 고칠 때 다른
쪽이 남고, 증상은 "총알이 옆으로 날아간다" 다. 이 프로젝트가 이미 총구 회전으로 같은 종류의 값을
치른 적이 있다(`AimLimb` 의 롤 문제).

**틱에 나누어지지 않는 시간 상수를 처음 만났다.** 0.15초 × 30Hz = 4.5. 올릴지 내릴지가 규칙이
아니라 **오차의 방향** 문제였다 — 내리면 서버가 클라이언트보다 관대해지고, 그것은 "클라이언트가
보내지도 않은 발사를 서버가 받아 준다" 는 쪽이다. 올렸다. 그리고 **나눗셈이 아니라 값으로 적어
그 선택이 코드에 남게 했다**(AS-13). `hitImmunity` 0.75초도 같은 문제를 갖고 있고(22.5틱)
IG-014b 에서 같은 판단을 한다.

**재장전을 만들지 않았다.** 기획서 §4.3 의 재장전은 체인이 놓아준 뒤 일어나고 그 체인이 OQ-4 에
막혀 있다. 순서를 임의로 정하면 벌칙의 의미가 달라지므로 §6.4 를 따랐다 — 대가는 "탄창 3발을
비우면 그 매치에서 더 쏠 수 없다" 는 미완 구간이고, 그것이 **IG-016 이 풀려야 완결되는 유일한
자리**임을 기록해 두었다.

### 이전 판단 기록 (이터레이션 22)

**"수" 와 "대상" 이 다른 경로로 온다는 것이 이번의 유일한 설계 판단이다.** `MatchState.escapes` 는
몇 명인지만 말한다 — 몸을 감추려면 누구인지 알아야 하고, 그것은 스냅샷의 `EntityFlags.Escaped`
에만 있다. 두 경로를 함께 쓰는 것이 옳고, **IG-012c1 이 고친 플래그의 한 틱 지연이 여기서 값을
한다** — 플래그가 늦으면 몸이 늦게 사라진다. 한 태스크의 수정이 다음 태스크의 전제가 됐다.

주기가 다른 두 경로를 쓰는 대가도 있었다. 플래그는 30Hz, 수는 2Hz 이므로 몸이 사라지는 순간의
`Escapes` 는 최대 0.5초 뒤처져 있다 — 그래서 **탈출 알림에 숫자를 넣지 않았다.** 방금 나간 Runner
에게 "0/2" 를 띄우는 것보다 이름만 띄우고 카운터에 맡기는 편이 낫다. IG-012b3 의 열쇠 알림과 같은
결론인데 이유가 다르다: 그쪽은 **누가** 했는지를 서버가 말하지 않아서였고, 이쪽은 **몇 개인지**가
늦게 와서다.

플래그를 `TryLatest` 로 읽는 것도 같은 성질의 선택이다. 보간은 위치를 위한 것이고, 불리언을 두 스냅샷
사이에서 섞으면 켜졌다 꺼졌다 한다.

### 이전 판단 기록 (이터레이션 21)

**테스트가 코드의 결함을 잡았다 — 와이어 상태의 한 틱 지연.** `MatchFlagsFor` 를 `StepPlayer`
안에서 계산하고 있었으므로 같은 틱의 판정이 세운 플래그가 **다음 틱 스냅샷에나** 나갔다. 탈출은
33ms 늦게 사라지는 정도지만 출혈(IG-014)이 같은 자리를 쓸 예정이었고, 그러면 "맞았는데 피가 한 틱
뒤에 나온다" 가 된다. `ProjectWire` 를 판정 뒤로 분리했다 — **틱 N 의 스냅샷은 틱 N 이 끝난
상태여야 한다.** 이 순서 규칙이 앞으로 붙는 모든 판정에 적용된다.

**층 허용치를 새로 만들지 않고 합쳤다.** 클라이언트는 삽입 프롬프트를 2.5m 에서 띄우고 탈출은
2.0m 에서 판정했다 — 그 0.5m 는 **"서 있으라고 표시된 자리에 서 있는데 아무 일도 안 일어나는"
구간**이다. 기존 구현의 값을 옮기는 것이 원칙이지만, **두 값이 같은 질문에 다른 답을 하고 있을
때는 옮기는 것 자체가 버그를 옮기는 것**이다. `InteractHeight` 로 통일했고 탈출이 0.5m 관대해진
것을 AS-11 에 적었다.

**내 테스트가 틀린 경우도 하나 있었다.** `Seeker는_탈출하지_않는다` 를 탈출 수로 확인하려 했는데,
픽스처의 두 스폰이 2m 간격이고 문 반경이 2.2m 라 Seeker 스폰에 놓은 문이 Runner 의 스폰도 덮는다.
그 Runner 가 나가면서 수가 1 이 됐고 **그것은 맞는 동작이다.** 대상을 그 Seeker 자신의 비트로
좁혔다 — 전역 카운터로 개별 주체를 검사하려 한 것이 실수였다.

**기존 테스트가 새 규칙과 부딪친 것도 회귀가 아니었다.** `열린_문에는_더_넣지_않는다` 가 문이 열린
뒤 36틱을 돌리고 소지 열쇠를 봤는데 그 사이에 탈출(24틱)이 성립했다. 규칙이 정확히 발동한 것이므로
코드가 아니라 검사 순서를 고쳤다. **두 규칙이 같은 좌표에서 만나면 테스트가 서로를 가린다** —
"문 앞에 서 있다" 는 삽입의 조건이면서 탈출의 조건이다.

`Room` 에 진단용 프로퍼티 3개를 두려던 것을 `internal Match` 하나로 정리했다. `Objectives` 가 이미
같은 방식이었다 — 테스트 전용 메서드를 프로덕션에 만드는 것보다 소유한 객체를 모듈 안에서 여는 편이
표면이 작다.

### 이전 판단 기록 (이터레이션 20)

**이름이 거짓말을 하고 있었다.** `ServerPlacesObjectives` 는 IG-012a 부터 배치가 아니라 **판정**을
가르고 있었다(습득 폴링 차단). 이번에 삽입 판정까지 같은 플래그로 막게 되면서 두 번째 거짓이 되므로
`ServerOwnsObjectives` 로 바꿨다. 권위를 가리키는 이름이 실제와 다른 것은 이 코드베이스에서 가장
비싼 종류의 부정확이다 — 다음 사람이 "배치만 서버가 하는군" 으로 읽고 판정을 클라이언트에 추가한다.
프로퍼티라 씬 직렬화에 영향이 없고 변경은 3곳뿐이었다.

**차단을 호출부가 아니라 `TryInsertKey` 한 곳에 두었다.** `EscapeDoor.Interact` 와
`PlayerInteractor` 를 건드리지 않은 것이 의도다 — "모든 규칙은 `MatchManager` 가 판단한다" 는
구조를 유지하고, `Interact` 는 원래부터 요청이었다. **프롬프트는 그대로 뜬다: 플레이어가 무엇을 할
수 있는지는 바뀌지 않았고 누가 판정하는지만 바뀌었다.**

**문 개방을 `keysInserted` 에서 유도하지 않았다.** 유도가 두 줄 더 짧지만, 문 오브젝트를 다시 세우는
경로가 있으므로 열린 뒤에 다시 세운 문이 잠긴 채로 돌아온다. 전문 값을 매 프레임 멱등하게 다시
적용하면 그 순서 문제가 아예 없다 — 이 프로젝트의 "전문은 알림이 아니다" 가 클라이언트 쪽에서도
같은 이득을 준다.

**알림 하나를 일부러 뺐다.** `TryInsertKey` 는 넣은 사람에게 "KEY IN 7/10" 을 띄웠는데, 전문은 누가
넣었는지 말하지 않는다. 전원에게 띄우면 오프라인 게임이 알려 주지 않던 것을 알려 주게 되므로,
진행도는 HUD 슬롯에 맡기고 문 개방만 띄운다. 비대칭 게임에서는 **알림도 정보 규칙**이다.

**이 태스크는 자동 테스트 없이 끝났다** — 전부 클라이언트 코드이고 EditMode 인프라가 없다(IG-018).
§7.2 를 만족하지 못하며, 그 사실이 다음 이터레이션에서 IG-018 을 앞으로 당기는 근거다.

### 이전 판단 기록 (이터레이션 19)

**계획이 틀렸고 조사가 그것을 고쳤다.** `ObjectiveFlags.DoorOpen` 을 추가한다고 적어 두었는데,
코덱을 열어 보니 **문 개방 바이트가 이미 문 블록 안에 있었다** — IG-011b 가 그 자리를 만들었고
잊은 것은 나다. 덕분에 와이어 변경이 0 이고, **Seeker 사본에는 블록 자체가 없으므로 개방 여부에
별도 필터가 필요하지 않다.** 블록을 통째로 빼는 설계가 새 필드마다 배당금을 낸다.

**`DoorOpen` 을 필드가 아니라 유도값으로 두었다.** 삽입 수와 개방 여부를 따로 들면 "열쇠는 10개인데
문은 닫혀 있다" 가 표현 가능한 상태가 되고, 그 상태에 빠지는 경로를 찾는 일이 남는다. 유도하면
그 상태가 존재하지 않는다.

**수직 허용치가 규칙 제약이었다.** 클라이언트의 `TryInsertKey` 는 수평 거리만 본다 — 층 검사는
`PlayerInteractor` 가 프롬프트를 띄울 때만 했다. 서버가 그대로 옮기면 **위층에서 아래층 문에 열쇠를
넣을 수 있다.** 문은 Runner 에게만 보이지만 좌표는 그 클라이언트가 알고 있으므로, 벽을 통과해 목표를
달성하는 경로가 된다. 값(2.5m)의 출처가 프롬프트 조건이라는 점이 오히려 근거다 — 보이는 것과 판정이
같은 값이다(AS-10).

**자기 검증이 내 주석을 반증했다.** `WithoutEdgeButtons` 에 "이것이 없으면 열쇠가 저절로 들어간다"
고 적었는데, 그 줄을 되돌려 실제로 돌려 보니 13개가 그대로 통과했다. 상호작용 요청을 세우는 곳이
**새 입력 갈래뿐**이고 반복 갈래는 `Simulate` 만 부르기 때문이다. 코드는 남기고(불변식이 두 곳의
협조에 의존한다) 주석과 테스트 이름을 사실에 맞게 고쳤다. **"이 방어가 무엇을 막는지" 를 확인하지
않고 적으면 그것도 실행하지 않은 검증을 통과로 적는 것과 같다.**

그 확인에서 `Jump` 가 같은 구조로 반복되고 있다는 것이 나왔다 — 접지 검사가 대부분 걸러 무해하지만
착지 틱에 재점프가 성립한다. 이동 동작을 바꾸는 변경이므로 고치지 않고 **IG-025** 로 올렸다.

### 이전 판단 기록 (이터레이션 18)

**조사가 태스크를 쪼갰다.** IG-012b 를 열어 보니 11파일이었다 — 입력 비트 하나 추가하는 일로
보였던 것이 클라이언트 래치·송신·서버 판정·두 전문의 필드·수신 적용·로컬 판정 차단까지 이어진다.
§6.1 을 넘기기 전에 나눈 것이 이번의 실질적인 산출물이고, **나누는 선을 "기존 동작을 깨는지" 로
그었다**(§6.3): 입력 경로만 넣으면 서버가 비트를 받아 버리는 상태로 끝나므로 아무것도 깨지지 않고,
판정을 넣는 순간 로컬 판정을 막아야 하므로 그쪽이 깨는 변경이다.

**대상을 와이어에 싣지 않기로 한 것이 이 태스크의 유일한 설계 결정이다.** "무엇에 상호작용하는가"
를 클라이언트가 지정하면 사거리 밖의 문도 지목할 수 있고, 그것은 §8 의 "클라이언트가 내가
맞췄다고 주장하는 구조" 와 같은 형태다. 서버가 자기 좌표로 대상을 고르면 그 주장이 불가능해진다.
장치가 6종으로 늘어나는 IG-013 에서도 같은 규칙이 유지된다 — 대상이 늘어나는 것은 서버의 후보
목록이 늘어나는 일이다.

**`PlayerInteractor` 를 일부러 건드리지 않았다.** 그쪽의 로컬 호출을 지금 막으면 서버에 판정이
없으므로 네트워크 매치에서 삽입이 불가능해진다 — "권위를 옮기는" 변경은 받는 쪽이 준비된 뒤에만
안전하다. 두 경로가 같은 E 키를 읽지만 래치를 소비하는 것은 와이어 경로뿐이므로 충돌하지 않는다.

`MovementLocked` 게이트를 붙인 것은 규칙 판단이다. 체인 벌칙과 정지 장치가 막지 못하는 행동이
하나 남으면 그 벌칙의 값이 달라진다. 다만 **오프라인 경로(`PlayerInteractor`)는 아직 이 게이트를
거치지 않으므로 두 경로의 동작이 미세하게 다르다** — IG-012b2 에서 로컬 판정을 막을 때 사라진다.

프로토콜 버전은 올리지 않았다. `buttons` 는 예전부터 1바이트이고 크기·배치가 그대로다. 구버전
클라이언트는 비트를 세우지 않고 구버전 서버는 마스크로 지운다 — 어느 조합도 오독하지 않는다.

### 이전 판단 기록 (이터레이션 17)

**클라이언트 폴링을 끄는 것만으로는 끝나지 않았다.** `KeyPickup.Update` 에 `return` 하나를
넣으면 서버가 판정하는 상태가 되지만, 그 순간 HUD 의 소지 수가 0 에 멈춘다 — 그 값을 세던 것이
방금 끈 폴링이었기 때문이다. 그래서 `MatchState` 의 `carriedKeys` 를 실제로 적용하는 경로
(`MatchSync.ApplyCarriedKeys` → `MatchManager.AcceptCarriedKeys` → `PlayerAgent.SetCarriedKeys`)
까지 같은 태스크에 넣었다. **판정을 옮기는 태스크는 그 판정의 결과를 표시하는 경로까지가 범위다.**

`SetCarriedKeys` 를 `AddKeys` 와 따로 둔 것이 그 경로의 핵심이다. 전문은 2Hz 로 같은 값을 다시
보내는 **현재 상태**이므로, 더하면 초당 두 개씩 늘어난다. 알림도 같은 문제를 갖는다 —
증가할 때만 올린다.

**`KeyPickupHeight` 를 IG-005 에서 일부러 미뤄 둔 판단이 맞았다.** 그때 값만 옮기면 같은 수가
`MatchConstants` 와 `KeyPickup.Update` 두 곳에 있는 상태였다. 판정이 올라오는 이 태스크에서
올리니 옮긴 쪽이 유일한 출처가 됐다.

열쇠 목록을 뒤에서부터 훑는 이유를 주석에 남겼다. 앞에서부터 지우면 `RemoveKeyAt` 이 리스트를
당겨 다음 열쇠를 한 틱 건너뛴다 — **증상이 "한 틱 늦게 주워진다" 뿐이라 아무도 신고하지 않는다.**

테스트는 열쇠를 **손으로 놓는다**(`Objectives.Reset` + `AddKey`). 배치가 고른 자리까지 걸어가게
하면 실패했을 때 "습득이 안 된다" 와 "거기까지 못 갔다" 를 구별할 수 없다.

`Assert.True(false, msg)` 는 xUnit 분석기(xUnit2020)가 **빌드 에러로** 막는다. `Assert.Fail` 이
있는지 확신이 없어 피하려 했는데, 분석기가 있다는 것을 알려 주었다.

### 이전 판단 기록 (이터레이션 16)

이터레이션 16 은 IG-012a 의 조사와 범위 조정으로 끝났다(코드 변경 없음). 조사 결과 **열쇠 습득에
`Interact` 가 필요 없다**는 것이 확인되어 그 비트를 IG-012b 로 넘겼다. 사용자의 루프 주기 변경
요청(5분 → 30분 → 10분)도 이 구간에 있었다.

### 이전 판단 기록 (이터레이션 15)

`WireSizeTests` 가 이 변경을 정확히 잡았다. 15바이트·127바이트를 못질한 두 테스트가 실패했고
그것이 그 테스트의 목적이다 — 고정부가 변하면 무엇이 실리는지 확인하게 만든다. 갱신하면서
**왜 4바이트가 줄었는지**를 주석에 남겼다.

R-2.3 을 닫는 데 **두 태스크가 필요했다**는 점이 이 작업의 성질을 말해 준다. 전문에서 문을
빼는 것(IG-011b)만으로는 부족했다 — 씨드가 남아 있으면 클라이언트가 좌표를 **계산**할 수 있다.
막아야 하는 것은 전송된 값이 아니라 **계산 가능성**이었고, 그것이 ADR 0002 의 논점("코드가
아니라 입력을 막는다")과 같은 이야기다.

클라이언트는 이제 전문으로 목표물을 받고, 씨드를 쓰는 경로는 오프라인뿐이다. 씨드가 와이어에서
빠지면 Seeker 클라이언트에 문을 계산할 입력이 사라진다 — 배치 함수를 갖고 있어도(ADR 0002)
계산할 수 없다.

범위가 작다: `RoomStateHeader` 에서 필드 제거(`WireSize` 15 → 11), 코덱·테스트 갱신,
`MatchSync` 의 `PlacementSeedOverride` 전달 제거. 서버는 내부 재현용으로 씨드를 계속 갖는다.

**오프라인 씨드는 `GameConfig.placementSeed` 가 계속 담당한다** — 그 값이 0 이면
`Environment.TickCount` 로 떨어지는 경로가 `MatchManager` 에 남아 있고, 그것이 오프라인의
정상 동작이다.

BLOCKED 6건(IG-007, IG-013, IG-016, IG-017, IG-020, IG-021)은 여전히 OQ-1·2·3·4·5·6 을 기다린다.

---

### 이전 판단 기록 (이터레이션 14)

오프라인 경로를 살린 판단(ADR 0002)이 이번에 값을 했다. 클라이언트에서 배치 알고리즘 6개를
지웠는데도 Play 모드에서 열쇠 10·장치 9·제단·문이 그대로 생기는 것을 실측했다 — **그 확인이
가능한 유일한 이유가 오프라인 경로를 남긴 것**이다. 네트워크 경로는 사람의 조작을 요구한다.

`AcceptObjectiveState` 를 diff 없이 재생성으로 만들었다. diff 는 열쇠를 위치로 대조해야 하고,
그것이 정확히 두 열쇠가 같은 셀을 쓸 때 깨지는 비교다 — 간격 조건을 포기하는 경로가 있으므로
(IG-011a) 실제로 일어날 수 있다.

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
