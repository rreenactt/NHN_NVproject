# 매치 중단(Abort) 계획 — 정상 진행이 불가능해진 매치를 서버가 끝낸다

Seeker 가 매치 도중 퇴장해도 매치가 480초 시계가 다 될 때까지 그대로 돈다. 이를
일반화해 **매치가 더 이상 정상 진행될 수 없는 상태가 되면, 몇 초의 유예 뒤 서버가
매치를 종료**하도록 한다.

---

## 1. 현황 분석 — 왜 지금은 계속 도는가

### 1.1 퇴장 경로에 역할 판정이 없다

`Room.Leave` (`Modules/Realtime/Simulation/Room.cs:2114-2176`) 가 하는 일은 명단 제거,
슬롯 반납(이중 퇴장 가드 포함), 입력 폐기, 방장 승계, 그리고 **완전히 빈 방**의
`ResetToWaiting` 뿐이다. 나가는 사람이 술래인지 보지 않는다. 강제 퇴장(`Kick`)도
결국 같은 `Leave` 로 합류한다.

그 결과 Seeker 가 나가면:

- `_seekerPlayerId` (`Room.cs:157`) 는 비워진 슬롯 번호를 계속 가리킨다.
- 매치는 시계(`MatchTicks` 14400 = 480초)가 끝날 때까지 돈다. Runner 는 잡힐 위험이
  없는 맵에서 열쇠를 줍고, 탈출해도 큰 의미가 없고, 시계가 끝나야 로비로 돌아온다.
- **슬롯 재활용 위험** (구현 중 정정): 이전 커밋의 코드 리뷰가 "정적 룸에서 나간
  술래의 슬롯을 새 참가자가 받으면 `MatchFlagsFor` 가 술래 표식을 붙인다"를
  지적했는데, 확인 결과 그 창은 **이미 닫혀 있다** — `/ws` 는 룸이 `Waiting` 이
  아니면 접속을 거절하며(`RealtimeEndpoints`) 정적 룸도 예외가 아니다. 이 작업이
  닫는 것이 아니라, 진행 중 합류를 여는 날의 커밋이 이 판정과 함께 다시 결정해야
  하는 항목이다.

### 1.2 매치를 끝내는 길은 둘뿐이다

| 경로 | 위치 | 결과 코드 |
|---|---|---|
| `EndMatch` — 방장 클라이언트의 판정 보고 (`Control`) | `Room.cs:2548-2562` | 방장이 보낸 byte |
| `EndMatchByServer` — 시계 종료 (`_match.Advance()` 가 true, `Room.cs:377`) | `Room.cs:2570-2580` | **0 (미정)** — IG-007 미구현이라 서버는 승패를 추측하지 않는다 |

둘 다 `RoomPhase.Ended` 로 옮기고, 클라이언트는 `RoomState` 전문의 `Outcome` byte 를
`MatchSync.AcceptRemoteOutcome` (`MatchSync.cs:295-298, 401-411`) 으로 받아
`MatchManager.AcceptOutcome((MatchOutcome)outcome)` 로 결과 화면을 그린다.
`SessionSceneRouter` 는 `Ended` 를 대기방 씬으로 보낸다(`SessionSceneRouter.cs:73`) —
결과를 보고 방장이 로비로 되돌리는 화면이 대기방이다.

### 1.3 결과 코드는 클라이언트 enum 이다

`NVproject/Assets/Scripts/Game/MatchEnums.cs:24-36`: `None = 0`, `RunnersEscaped = 1`,
`SeekerTimeout = 2`, `SeekerWipedRunners = 3`. 서버는 byte 를 중계만 한다
(판정은 방장 클라이언트 — "한시적 경로", `Room.cs:2546-2547`).

### 1.4 이미 처리되는 인접 경우 (건드리지 않는다)

- **전원 퇴장**: `Leave` 가 `ResetToWaiting` 으로 되돌린다 (`Room.cs:2169-2172`).
  사람이 다 나가면 봇도 지워지므로(`Room.cs:2147-2150`) 봇만 남은 방은 생기지 않는다.
- **초대 코드 룸의 회수**: 마지막 참가자가 나가면 룸 자체가 회수된다 (별개 계층).

---

## 2. 설계

### 2.1 "정상 진행 불가"의 판정 — 구조로 정의한다

매 판정은 명단만 본다. 재접속·복귀 같은 시간적 개념을 쓰지 않는다 — 계정이 없으므로
끊긴 사람과 새로 온 사람을 구분할 방법이 없고, 새 세션은 새 슬롯을 받아 어차피
`_seekerPlayerId` 와 일치하지 않는다.

`Playing` 단계에서 다음 중 하나면 **진행 불가(unviable)** 다:

1. **Seeker 부재** — `_seekerPlayerId` 를 가진 참가자가 명단에 없다.
   쫓는 쪽이 없는 매치는 규칙이 성립하지 않는다.
2. **Runner 부재** — Seeker 를 뺀 참가자가 0명이다.
   쫓길 쪽이 없어도 같다. (봇은 참가자다 — 봇 Runner 가 남아 있는 정적 룸은
   진행 가능으로 본다. 봇 술래도 마찬가지다.)

둘 다 결국 "역할 한쪽이 비었는가" 이고, 전체 0명은 기존 `ResetToWaiting` 경로가
**먼저** 처리한다(§3.2 순서).

**역할 재배정은 하지 않는다.** 방장 승계처럼 남은 Runner 하나를 술래로 승격시키는
설계도 가능하지만 기각한다 — Runner 였던 클라이언트는 문과 열쇠 좌표를 이미 받았다
(`ObjectiveState` 는 역할별로 걸러 내려간다). 그 사람이 술래가 되는 순간 이 게임의
정보 비대칭이 무너지고, 그것을 막으려면 목표물 재배치까지 필요하다. 끝내는 것이 맞다.

### 2.2 유예(grace) — 즉시 끝내지 않는 이유

진행 불가를 감지한 틱에 바로 끝내지 않고 **유예 시간(기본 5초) 뒤에** 끝낸다.

- **이중 퇴장·강제 퇴장의 과도기를 흡수한다.** `Kick` → 소켓 종료 → `finally` 의
  `Leave` 가 시차를 두고 두 번 오는 것이 정상 경로다(`Room.cs:2116-2126`). 판정을
  틱 경계의 상태로 하므로 유예가 없어도 오판은 없지만, 유예가 있으면 순간적인
  상태 요동이 결과 화면 깜빡임으로 새지 않는다.
- **남은 사람이 무슨 일이 일어났는지 볼 시간을 준다.** "술래가 나갔고 곧 끝난다" 를
  화면이 말할 수 있다(§4).
- **취소 분기는 두지 않는다 — 회복이 불가능하기 때문이다.** (구현 중 정정) 진행 중
  합류는 `/ws` 가 단계를 보고 거절하고(`RealtimeEndpoints`, 정적 룸도 예외가 아니다)
  봇 채우기(`TopUpBots`)는 대기 단계에만 돌므로, `Playing` 중에 빠진 자리를 메울
  길이 없다. 취소 분기를 두면 지금은 죽은 코드이고, 진행 중 합류가 열리는 날에는
  새 참가자가 술래 표식을 조용히 물려받은 채 매치가 계속되는 길이 된다.
- **유예 중의 승패 보고는 받지 않는다.** (구현 중 추가 — 코드 리뷰 HIGH) 유예 5초는
  룸이 `Playing` 인 채로 흐르므로 방장 클라이언트의 판정도 그 안에서 계속 돈다 —
  술래가 나간 방에서 남은 전원이 자유롭게 탈출하면 Runner 승리가, Runner 전원이
  나간 방에서는 전멸 승리(`seekerWinsOnWipe`)가 유예를 앞질러 도착한다. `EndMatch`
  가 `_abortAtTick != 0` 이면 보고를 무시한다 — 예약이 살아 있는 동안 결과는
  중단뿐이다.

유예 값은 서버 단독 판정이므로 `RealtimeConstants` 에 둔다 (클라이언트가 같은 수로
계산할 것이 없다 — `Shared` 에 두지 않는다):

```csharp
// RealtimeConstants.Match
/// 매치가 정상 진행 불가(술래 부재, Runner 전멸 퇴장)로 판정된 뒤 종료까지의 유예.
/// 남은 사람이 상황을 볼 시간이고, 초대 코드 룸에서 복귀는 구조적으로 불가능하므로
/// 재접속 대기가 아니다.
public const float AbortGraceSeconds = 5f;
internal static readonly uint AbortGraceTicks = (uint)MathF.Ceiling(AbortGraceSeconds * SimConstants.TickRate); // 150
```

### 2.3 결과 코드 — `Aborted = 4`

새 결과 코드를 하나 추가한다. 승패가 아니라 **중단**이다 — 술래가 나갔다고 Runner
승리로 치면 퇴장이 Runner 팀의 무기가 되고, 반대도 같다.

- 서버: `_outcome = 4` 로 `Ended` 전이. **서버가 결과 byte 를 처음으로 직접 쓰는
  경우**가 된다 — 지금까지 결과는 방장 클라이언트의 보고(한시적 경로)였고, 중단은
  명단 사실에서 나오므로 서버만 옳게 판정할 수 있다. IG-007(서버 승패 판정)로 가는
  자연스러운 첫 걸음이기도 하다.
- 경합은 이미 안전하다: `EndMatch` 는 `Phase != Playing` 이면 무시하므로
  (`Room.cs:2550`), 서버 중단이 먼저면 방장의 늦은 보고가 결과를 덮지 못하고,
  방장 보고가 먼저면 유예 판정 자체가 `Playing` 게이트에서 멈춘다.
- 클라이언트: `MatchOutcome.Aborted = 4` 추가 + 결과 화면 문구("매치가 중단되었다 —
  진행에 필요한 인원이 남지 않았다"). enum 에 없는 byte 를 캐스팅해도 C# 은 예외를
  내지 않지만, 문구 없는 결과 화면은 침묵 실패이므로 enum 과 문구를 먼저 넣는다.

값 4 는 서버 문서(이 파일)와 클라이언트 enum 두 곳에 적힌다. 와이어 필드·전문 구조는
**변경 없음** — 기존 `Outcome` byte 에 새 값 하나가 흐를 뿐이므로 프로토콜 버전도
그대로다.

---

## 3. 서버(NVserver) 변경

### 3.1 상태 하나: `_abortAtTick`

`Room` 에 `uint _abortAtTick` (0 = 예약 없음)을 둔다. 체인의 `ChainReleaseTick` 과
같은 규약 — 별도 bool 을 만들지 않는다.

### 3.2 판정 지점 — 틱 경계에서, 명단 변경 뒤에

퇴장은 커맨드이고 커맨드는 `Advance` 첫 단계(`DrainCommands`)에서 적용되므로, 판정을
`Advance` 의 `Playing` 분기에 한 줄 넣으면 퇴장과 같은 틱에 감지된다. `Leave` 안에
넣지 않는 이유는 판정 조건이 "누가 나갔나"가 아니라 "지금 명단이 성립하는가"이기
때문이다 — 상태에서 판정하면 이중 퇴장·강제 퇴장·입장 경합을 전부 공짜로 얻는다.

```
Advance (Playing 분기, StepPlayer 루프 앞):
  viable = SeekerPresent() && RunnerPresent()
  if (!viable && _abortAtTick == 0)   → _abortAtTick = _tick + AbortGraceTicks, 로그
  if (viable && _abortAtTick != 0)    → _abortAtTick = 0, 로그 (회복)
  if (_abortAtTick != 0 && _tick >= _abortAtTick) → AbortMatch()
```

- 매 틱 명단 두 번 훑기(참가자 ≤ 5)는 비용이 아니다.
- **빈 방 우선**: 전원 퇴장은 `Leave` 의 `ResetToWaiting` 이 커맨드 적용 시점에 이미
  단계를 되돌리므로, 이 판정에 도달하지 않는다. 결과 화면 없이 조용히 대기로 돌아가는
  기존 동작이 유지된다 — 볼 사람이 없는 결과 화면은 만들지 않는다.
- `RoleReveal` 도 룸 단계는 `Playing` 이므로 같은 판정을 받는다 — 역할 공개 중에
  술래가 나가도 5초 뒤 끝난다.

### 3.3 `AbortMatch()`

`EndMatchByServer` 와 나란한 사설 함수:

```csharp
/// 매치가 정상 진행 불가로 끝났다. 승패가 아니라 중단이다 — 결과 4(Aborted)는
/// 서버가 직접 쓰는 첫 결과 코드다. 명단 사실은 서버만 옳게 안다.
private void AbortMatch()
{
    _outcome = MatchAborted;          // const byte 4
    _abortAtTick = 0;
    _match.ForceEnd();
    Volatile.Write(ref _phase, (int)RoomPhase.Ended);
    _stateDirty = true;
    _matchStateDirty = true;
    _logger.LogInformation("룸 {RoomId}: 진행 불가로 매치를 중단했다. ...", ...);
}
```

`ResetToWaiting` 과 `Start` 에서 `_abortAtTick = 0` 초기화를 함께 한다 — 남겨 두면
다음 매치가 지난 매치의 데드라인을 물려받는다(체인 틱 필드를 지우는 것과 같은 이유,
`Room.cs:2443-2448`).

### 3.4 판정 헬퍼

```csharp
private bool SeekerPresent()  // _seekerPlayerId 를 가진 참가자가 명단에 있는가
private bool RunnerPresent()  // Seeker 가 아닌 참가자가 하나라도 있는가
```

`FindByPlayerId` (`Room.cs` 기존 헬퍼)를 재사용한다. **슬롯 번호가 아니라 명단을
본다** — 슬롯 반납 여부(`ReleaseSlot`)는 예약의 문제고, 매치의 사실은 명단이다.

---

## 4. 클라이언트(NVproject) 변경

| 항목 | 내용 |
|---|---|
| `MatchEnums.cs` | `Aborted = 4` 추가 |
| 결과 화면 | `MatchOutcome.Aborted` 문구 — 승리 팀 없음("매치 중단"). `GameHudController` 의 승패 화면 분기와 대기방(`GameLobbyHud`)의 결과 표시 확인 |
| 퇴장 알림 (선택) | 유예 동안 "술래가 퇴장했다 — 잠시 후 종료된다" 배너. 서버가 새 필드를 보내지 않아도 된다 — 클라이언트는 `RoomState` 명단에서 `SeekerPlayerId` 가 사라진 것을 이미 볼 수 있다(2Hz). `MatchSync` 가 명단을 훑는 자리에서 감지해 `MatchManager.Notify` 로 띄운다 |
| 씬 흐름 | 변경 없음 — `Ended` 는 기존대로 대기방으로 간다 |

카운트다운 숫자를 정확히 그리고 싶으면 서버 유예 값을 와이어로 보내야 하지만,
**보내지 않는다** — "잠시 후"로 충분하고, 2Hz 전문에서 초 단위 카운트다운은 어차피
반 박자 늦는다. 정확한 숫자가 필요해지면 그때 `MatchState` 헤더 확장으로 다룬다.

---

## 5. 예외·호환

| 케이스 | 동작 |
|---|---|
| Runner 하나가 나감 (Runner 2+ 남음) | 아무 일 없음 — 진행 가능 |
| Seeker 퇴장/강퇴 | 유예 후 `Aborted` 종료 |
| Runner 전원 퇴장, Seeker 만 남음 | 유예 후 `Aborted` 종료 |
| 전원 퇴장 | **기존 경로 우선** — `ResetToWaiting`, 결과 화면 없음 |
| 정적 룸, 나간 술래의 슬롯을 새 참가자가 받음 | (구현 중 정정) 일어나지 않는다 — `/ws` 가 `Waiting` 이 아닌 룸의 접속을 거절하므로 매치 중 입장 자체가 없다. 그래서 취소 분기도 두지 않았다(§2.2) |
| 봇 술래(정적 룸)가 있는 매치에서 사람 전원 퇴장 | 사람 0 → 봇 제거 → 빈 방 → `ResetToWaiting` (기존) |
| 방장 클라이언트의 `EndMatch` 보고와 경합 | 먼저 온 쪽이 이긴다 — 양쪽 다 `Playing` 게이트가 있어 덮어쓰기 없음 |
| 매치 시계 종료와 경합 | `_match.Advance()` 의 시계 종료가 같은 틱에 나면 그쪽이 먼저 실행돼도 무해 — 둘 다 `Ended` 로 가는 전이 |
| 재매치 | `Start`/`ResetToWaiting` 이 `_abortAtTick` 을 지움 |
| 프로토콜 | 무변경 (기존 byte 에 새 값) — 버전 4 유지 |
| 구버전 클라이언트가 4 를 받으면 | enum 밖 값 캐스팅은 예외가 아니라 문구 없는 결과 화면 — 클라이언트 enum 추가를 서버보다 먼저 배포하면 창이 없다 |

---

## 6. 테스트 계획 (`tests/Modules.Tests/Realtime/MatchAbortTests.cs`)

역할이 무작위이므로 **전문에서 Seeker 를 물어** 그 세션을 내보낸다
(`SeekerSpawnTests.FindByRole` 과 같은 규칙).

1. 술래가 나가면 유예 동안은 `Playing` 이고, 유예 틱이 지나면 `Ended` + 결과 4.
2. Runner 하나가 나가도 (2인 매치 제외) 매치가 계속된다 — 3인으로 시작.
3. 2인 매치에서 Runner 가 나가면 (= Runner 부재) 유예 후 종료.
4. 전원이 나가면 `Waiting` 으로 돌아가고 `Ended` 를 거치지 않는다 (기존 동작 고정).
5. 강제 퇴장(`Kick`)으로 술래를 내보내도 1과 같다 — 이중 `Leave` 가 와도 한 번만 끝난다.
6. 재매치가 지난 매치의 데드라인을 물려받지 않는다 — 술래 퇴장 → 유예 도중 전원 퇴장 →
   재입장 → 새 매치 시작 → 유예 틱이 지나도 계속 `Playing`.
7. 유예 값 자체는 박지 않는다 — `AbortGraceTicks` 상수로 계산해 검사한다
   (체인 테스트가 시간 창을 다루는 방식과 같다).

---

## 7. 단계별 계획

| 단계 | 내용 | 규모 |
|---|---|---|
| **Phase 1 (서버)** | `_abortAtTick` + `Advance` 판정 + `AbortMatch` + 상수 + `MatchAbortTests` | 서버만, 작음 |
| **Phase 2 (클라이언트)** | `MatchOutcome.Aborted = 4` + 결과 화면 문구 + (선택) 유예 배너. 컴파일 확인은 `dotnet build Assembly-CSharp.csproj` | 작음 |
| **Phase 3 (QA)** | 두 클라이언트 실플레이 — 술래 창 닫기 → 남은 클라이언트가 5초 뒤 결과 화면 → 대기방 복귀. ESC 메뉴의 "나가기"와 강제 종료 두 경로 모두 | 에디터 수동 |

**구현 결과 (2026-08-08, `feature/server/match-abort`).** Phase 1·2 완료, 리뷰 반영
포함(유예 중 승패 보고 무시 — HIGH, 죽은 취소 분기 제거, 배너 가드 교정). 서버
테스트 630개(중단 10건 포함) 10회 연속 통과, 클라이언트 어셈블리 컴파일 0 오류.
**Phase 3 은 수동으로 남는다** — 에디터에서 두 클라이언트로 술래 창 닫기 / ESC
나가기 두 경로를 확인하고, 배너와 MATCH ABANDONED 카드가 그려지는지 본다. 남은
Runner 하나가 유예 동안 탈출을 시도해도 결과가 중단(4)으로 닫히는지가 핵심 확인
항목이다.

### 범위 밖 (별도 과제로 기록)

- 매치 중 입장(정적 룸)의 슬롯 재활용 → 새 참가자가 술래 표식을 받는 문제의 근본 해결
- IG-007 — 서버가 승패(1·2·3)까지 판정하는 것. `Aborted` 는 그 첫 조각이다
- 퇴장이 아닌 **무입력/방치**(AFK) 판정 — 소켓이 살아 있는 유령 참가자는 이 계획의
  판정에 걸리지 않는다
