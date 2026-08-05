# 메인 로비와 대기방을 가르는 계획

> **상태: Phase 0~5 구현 완료(2026-08-05).** 지금 동작하는 와이어 계약은
> `NVproject/.claude/skills/lobby-builder/references/server-contract.md` 에 있고, 밟은 함정은
> `conventions.md` 로 옮겼다. **이 문서는 왜 그렇게 만들었는지를 남긴다.** 열린 항목은 §10 이며,
> 계획과 달라진 곳은 §11 에 적었다.
>
> 배경은 `architecture.md`(설계 원칙·금지 목록·신뢰 경계), `structure.md`(파일 배치 8문 표),
> `conventions.md`(함정 목록), `NVproject/CLAUDE.md`(클라이언트 쪽 전부)다.

지금 방에 들어간 사람이 할 수 있는 일은 **기다리는 것과 나가는 것**뿐이다. 준비도 없고 캐릭터도
없고 방장이 누구를 내보낼 방법도 없다. 그 자리를 **대기방(Game Lobby)**으로 만들고, 그러면서
메인 로비가 들고 있던 "방 안의 일"을 넘긴다.

동시에 프로토타입으로 들어와 있는 `Assets/Scenes/Lobby.unity` + `Assets/Scripts/Lobby/`
(커밋 `22769af`)를 **정리한다.** 그 코드는 서버가 없다고 가정하고 쓰였고, 이제 서버가 있다.

---

## 1. 지금 있는 것 — 세 덩어리가 겹쳐 있다

한 폴더(`Assets/Scripts/Lobby/`)에 성격이 다른 세 덩어리가 섞여 있다. 이것을 먼저 갈라야
"무엇을 재사용하는지" 를 말할 수 있다.

| 덩어리 | 파일 | 성격 |
|---|---|---|
| **A. 메인 로비 (서버 연동됨)** | `Controllers/`, `Models/`, `Services/`, `UI/`, `Events/`, `MainLobbyAssets.cs` (커밋 `1817839`) | `NetSession`·`RoomApi`·`NetworkClient` 위에 서 있다. 실제로 동작한다 |
| **B. 대기방 프로토타입 (서버 없음)** | `LobbyManager`, `ILobbyTransport`, `LobbyTypes`, `LobbyConfig`, `LobbyBootstrap`, `LobbyRoom`, `LobbySlot`, `LobbySlotPicker`, `LobbyHud`, `LobbyMannequin`, `LobbyCharacterCatalog` + `Lobby.unity` (커밋 `22769af`) | 네트워크를 **의도적으로 뺀** 상태. `OfflineLobbyTransport` 가 가짜 플레이어를 만들어 준비·카운트다운·자리 교환을 혼자 돌린다 |
| **C. 임시로 A 안에 들어간 대기방** | `UI/RoomView.cs`, `Controllers/RoomController.cs`, `templates/RoomPopup.uxml` | A 의 주석이 스스로 적어 두었다 — "요구사항의 UI 트리에는 이 화면이 없다 … 없으면 방을 만든 뒤 아무것도 할 수 없는 화면에 갇히므로, 이 로비가 대체하는 옛 로비에서 가져왔다" |

**B 와 C 는 같은 화면의 두 구현이다.** B 는 규칙이 있고 서버가 없으며, C 는 서버가 있고 규칙이
없다. 이 계획이 하는 일은 **B 의 규칙을 서버로 올리고, C 의 자리에 그 화면을 제대로 세우는 것**
이다.

### 1.1 B 를 그대로 쓸 수 없는 이유

`LobbyManager` 는 잘 쓰인 코드다. `Request*`(클라이언트가 요청) / `Handle*`(권위가 판정) /
`Apply*`(클라이언트가 받아 적음) 로 갈라 두었고, 판정은 전부 `Handle*` 안에만 있다. 문제는
**그 권위가 클라이언트 프로세스 안에 있다는 것**이다.

- `architecture.md` 의 신뢰 경계는 "방 상태를 바꾸는 판정은 서버에만 있다"다. `LobbyManager`
  를 남기면 준비·카운트다운·자리를 판정하는 두 번째 권위가 생긴다. 서버의 `Room` 과 어긋날 때
  증상은 "어떤 클라이언트에서만 시작 버튼이 켜진다" 로 나타난다.
- `ILobbyTransport` 는 **구현이 하나뿐인 인터페이스**가 된다 — `architecture.md` 가 물어보고
  넘어가야 하는 항목으로 못 박은 것이다. 실제 채널은 이미 있다: `ControlKind`(C→S)와
  `EventKind.RoomState`(S→C). 그 위에 두 번째 추상을 얹으면 요청이 두 층을 지난다.
- `LobbyConfig.maxPlayers` / `minPlayers` 는 **서버가 아는 값의 사본**이다. 정원은
  `RealtimeConstants.Rooms.MaxPlayers`(8), 최소 인원은 `MinPlayersToStart`(2)이고 클라이언트는
  `GET /rooms/{code}` 로 이미 받는다(`RoomInfo.Capacity`, `MinPlayers`). 사본을 두면 6 대 8 로
  어긋난다 — 프로토타입은 실제로 6 이다.

그래서 **B 의 판정은 서버로 이관하고 B 의 코드는 대부분 지운다.** 살리는 것은 §7 의 표에 있다.

### 1.2 `OfflineLobbyTransport` 를 지우는 근거

`lobby-builder` 스킬의 인계 문서(`references/netcode-integration.md`)는 "네트워킹이 들어온
뒤에도 오프라인 전송을 살려 둘 것 — 혼자 확인하는 유일한 길이다"라고 적어 두었다. 그 전제가
이미 깨졌다.

- 혼자 확인하는 길은 **서버 쪽에 생겼다.** 정적 룸(`test`)의 봇(`BotOptions`,
  `test-room-bots-plan.md`)이 가짜 플레이어가 하던 일을 **권위 있는 쪽에서** 한다. 오프라인
  전송으로 확인한 준비 흐름은 서버에서 성립한다는 보장이 없고, 봇으로 확인한 것은 성립한다.
- 세션이 없으면 로비 자체가 오프라인 경로로 갈린다(`NetSession.Exists`). "서버 없이 대기방을
  띄운다" 는 시나리오는 이제 제품에 없다.

이 판단과 그 근거는 인계 문서에 **덮어 쓴다.** 없어진 전송을 가리키는 문서를 남기면 다음 사람이
그것을 찾는다.

---

## 2. 결정된 세 갈래

| 질문 | 결정 | 근거 |
|---|---|---|
| 대기방을 어떤 형태로 만드는가 | **`MainLobby` 씬 안의 전체화면 UI 페이지** | 씬 전환이 없어 `NetSession`·재시도·실패 표시가 그대로 살아 있다. 3D 스탠드 대기실은 지금 얻는 것보다 유지 비용이 크다 |
| 매치 시작 규칙 | **준비는 조건, 시작은 방장이 누른다.** 서버 카운트다운 없음 | 서버 변경이 `Ready` 필드와 `Control` 한 종류로 끝난다. 카운트다운은 lock 상태·취소 경로·시작 틱 복제를 함께 들고 오는데, 그것 없이도 대기방은 성립한다 |
| 1차 범위 | **준비 · 캐릭터 선택 · 강제 퇴장 · 방장 위임** | 넷 다 와이어에 자리가 필요하다. 프로토콜 버전 인상은 서버·클라이언트 동시 배포를 강제하므로(§4.1) 한 번에 끝낸다 |

**자리(슬롯) 이동·교환은 1차에서 뺀다.** 지금 스탠드 번호는 `PlayerId` 이고 그것이 서버의 스폰
위치를 고르므로(`_map.SpawnPosition(playerId)`), 자리를 옮기는 것은 스폰을 옮기는 것과 같은
말이 된다. 표시용 슬롯을 `PlayerId` 에서 떼어내는 일은 §9 의 확장 자리에 둔다.

---

## 3. 역할과 책임

### 3.1 메인 로비 — 방 **밖**의 전부

| 책임 | 지금 어디 |
|---|---|
| 표시 이름·서버 주소 프로필 | `LobbyService`, `SettingsPopup` |
| 접속 상태·실패 문구·다시 시도 | `ConnectionStatusView`, `SessionFailure` |
| 방 목록(`GET /rooms`)·새로고침·온라인 인원 | `RoomService`, `RoomListView` |
| 방 만들기(맵 선택·공개 여부) | `CreateRoomPopup`, `MapChoiceService` |
| 코드로 참가 · 빠른 참가 | `JoinByCodePopup`, `RoomService` |
| 게임 종료 | `LobbyController.Quit` |

**메인 로비는 방의 내부를 모른다.** 지금은 `RoomController` 가 `RoomView` 를 모달로 띄워 그
경계가 흐릿하다.

### 3.2 대기방 — 방 **안**의 전부

| 책임 | 상태 |
|---|---|
| 초대 코드 표시·복사, 초대 링크 | `RoomView` 에서 승계 |
| 맵 이름·공개 여부 표시 | `RoomView` 에서 승계 |
| 명단(정원 전체) — 이름·방장·자기 행 | `RoomView` 에서 승계, 정원 전체로 확장 |
| **준비 상태 표시와 토글** | **신규** |
| **캐릭터 선택과 미리보기** | **신규** (프로토타입의 `LobbyCharacterCatalog`·`LobbyMannequin` 재사용) |
| **강제 퇴장 · 방장 위임** | **신규** |
| 시작 버튼과 꺼져 있는 이유 | `RoomView.StartNote` 에서 승계 |
| 나가기 | `RoomView` 에서 승계 |
| 매치 종료 후 결과 · 로비로 되돌리기 | 지금 문구만 있다. 화면으로 만든다 |

### 3.3 경계 한 줄

> 메인 로비는 **`NetSession.State` 가 방 밖일 때** 살아 있고, 대기방은 **방 안일 때** 살아 있다.
> 둘 다 `RoomPhase` 를 직접 보지 않는다 — 세션이 이미 그것을 상태로 번역해 두었다.

---

## 4. 흐름

```
[메인 로비 #page-browser]
   방 만들기 ─ POST /rooms ─────┐
   코드로 참가 ─ GET /rooms/{code} ─┤
   목록에서 참가 ────────────────┤
   빠른 참가 ───────────────────┘
                              └─► /ws 접속 ─ Welcome ─► SessionState.InLobby
                                                              │
[대기방 #page-room] ◄─────────────────────────────────────────┘
   RoomState(2Hz) 로 명단·준비·캐릭터·방장을 그린다
   Control(SetReady / SetCharacter / KickPlayer / TransferHost) 을 보낸다
   방장이 시작 ─ Control(StartMatch) ─► 서버가 자격·인원·준비를 다시 본다
                                          │ RoomPhase.Playing
                                          ▼
                              SessionState.InGame ─ SessionSceneRouter ─►
                                          [SampleScene / MapRuntime]
                                          │ RoomPhase.Ended
                                          ▼
   대기방(결과 표시) ◄─ 라우터가 MainLobby 로 복귀
   방장이 로비로 ─ Control(ReturnToLobby) ─► RoomPhase.Waiting (준비 전원 해제)
   나가기 ─ 소켓 정상 종료 ─► SessionState.Idle ─► [메인 로비]
```

세션 상태 → 화면의 표는 이렇게 굳힌다. **`SessionSceneRouter` 는 손대지 않는다** — 대기방이
같은 씬 안이므로 라우터가 아는 씬 목록이 늘어나지 않는다.

| `SessionState` | 화면 |
|---|---|
| `Idle`, `Creating`, `Resolving`, `Connecting`, `Handshaking`, `Leaving` | 메인 로비 (+ 로딩 오버레이) |
| `Failed` | 메인 로비 + 실패 줄·다시 시도 |
| `InLobby` | **대기방** |
| `InGame` | 대기방 유지(씬이 바뀌는 몇 프레임 동안 비어 보이지 않게) → 게임 씬 |
| `Ended` | **대기방 · 결과** |

### 4.1 프로토콜 버전을 올린다 — 3 → 4

`ProtocolInfo.Version` 은 업그레이드 전에 426 으로 **구버전 클라이언트를 전부 거부**하고,
WebGL 빌드는 수 분이 걸린다. 그래서 §2 의 1차 범위 넷을 한 번의 인상 안에서 끝낸다. 서버와
클라이언트는 같은 커밋 계열로 배포한다.

---

## 5. 와이어 설계

### 5.1 `RoomState` 명단 항목을 넓힌다

지금 (`RoomPlayerEntry.FixedWireSize = 2`):

```
playerId(1) nameLength(1) name(n)
```

바꾼 뒤 (`FixedWireSize = 4`):

```
playerId(1) flags(1) characterId(1) nameLength(1) name(n)
```

| 필드 | 값 |
|---|---|
| `flags` bit0 | `Ready` |
| `flags` bit1 | `IsBot` — 지금 클라이언트는 봇을 구분할 방법이 없다. 서버는 이미 안다(`PlayerEntity.IsBot`) |
| `flags` bit2~7 | 예약. 팀·관전자가 여기 들어간다(§9) |
| `characterId` | 0..`ProtocolInfo.LobbyCharacterCount-1`. 미배정은 `0xFF` |

**헤더(`RoomStateHeader`, 11B)는 그대로 둔다.** 방장은 이미 있고, 준비 인원은 명단에서 세면
되고, 최소 인원은 참가 전 조회가 이미 답한다. 헤더에 유도 가능한 값을 넣지 않는다.

**이 전문은 계속 전문이다.** 준비 토글이 최대 0.5초 늦게 보이는 것은 잘못된 상태를 남기지
않는다 — ADR 0003 의 기준("놓쳤을 때 틀린 상태가 남는가")에 따라 새 알림을 만들지 않는다.

고쳐야 하는 것:

- `MessageCodec.WriteRoomState` / `ReadRoomState` — 필드 둘 추가
- `MessageCodec.RoomStateMaxWireSize` — `RoomPlayerEntry.FixedWireSize` 에서 유도하므로 **고칠
  것이 없다.** 다만 `Room` 의 `_stateBuffer` 가 그 함수로 잡혀 있는지 확인한다(잡혀 있다)
- `Room.WriteRoomState` / `BroadcastRoomState` — 명단을 만들 때 준비·캐릭터·봇을 싣는다
- **`NetworkClient` 의 명단 변경 판정** (`Assets/Scripts/Net/NetworkClient.cs:634-642`) — 지금 `PlayerId` 와
  `Name` 만 비교한다. 준비·캐릭터를 비교에 넣지 않으면 **화면이 갱신되지 않는다.** 이 계획에서
  가장 조용히 실패할 수 있는 한 줄이다

### 5.2 `ControlKind` 에 네 종류를 더한다

`ControlMessage` 는 3B(`opcode` + `kind` + `value`)이고 `value` 한 바이트로 넷 다 표현된다.
`playerId` 는 바이트다.

| 값 | 종류 | `value` | 서버가 다시 보는 것 |
|---|---|---|---|
| 5 | `SetReady` | 0/1 | 단계가 `Waiting` 인가. 참가자인가 |
| 6 | `SetCharacter` | 캐릭터 인덱스 | 범위 안인가. **방 안의 다른 사람이 쓰고 있지 않은가**. 단계가 `Waiting` 인가 |
| 7 | `KickPlayer` | 대상 `playerId` | 요청자가 방장인가. 대상이 방장 자신이 아닌가 |
| 8 | `TransferHost` | 대상 `playerId` | 요청자가 방장인가. 대상이 방 안의 **사람**인가(봇은 안 된다) |

`2` 는 계속 비워 둔다 — 자발적 퇴장을 두었다 뺀 자리이고, 그 이유가 `ControlKind` 주석에 있다.

`ControlKind` 는 요청이지 명령이라는 규칙을 그대로 지킨다. **서버 판정 없이 방 상태가 바뀌는
경로는 하나도 만들지 않는다.**

### 5.3 캐릭터 개수는 `Shared` 에 둔다

서버가 범위를 검사하고 클라이언트가 같은 표에서 고르므로, **개수는 둘이 아는 값**이다.
`ProtocolInfo.LobbyCharacterCount = 8` 로 두고, 클라이언트의 `LobbyCharacterCatalog` 는
**인덱스 순서가 와이어 값**이 되도록 정리한 뒤 길이가 그 상수와 같은지 시작 시 한 번 검사한다.

에셋 경로·색·이름 같은 **표현**은 `Shared` 에 넣지 않는다. `structure.md` 8문 표 1번의 반대
방향이다 — 서버는 캐릭터가 어떻게 생겼는지 알 필요가 없고, 아는 순간 그 표가 두 곳에 생긴다.

---

## 6. 서버 구현

### 6.1 `PlayerEntity` — 필드 둘

```csharp
public bool Ready;        // 대기 단계에서만 의미가 있다
public byte CharacterId;  // 0xFF = 미배정
```

- `Join` 에서 `Ready = false`, `CharacterId = 첫 빈 캐릭터`. 아무것도 안 입은 참가자를 만들지
  않는다 — 그러면 명단에 빈 칸이 생기고, 정원 8 · 캐릭터 8 이므로 항상 하나는 남는다.
- **`ResetToWaiting` 에서 전원의 `Ready` 를 내린다.** 매치가 끝나고 로비로 돌아온 방이 즉시
  "전원 준비" 를 만족하면, 자리를 비운 사람을 데리고 다음 매치가 시작된다.
- 봇은 항상 `Ready = false` 다. 준비 판정에서 봇을 빼는 것(§6.3)이 그 짝이다.

### 6.2 `RoomCommand` 에 네 종류

HTTP·소켓 스레드는 방 상태를 직접 만지지 않는다. 넷 다 `_commands` 를 지나 `DrainCommands`
에서 적용된다. `GameSession.DispatchControl` 에 `case` 넷을 더한다.

`Kick` 만 한 가지가 다르다 — **소켓을 닫아야 한다.** 틱 루프는 소켓을 만지지 않으므로
(`architecture.md` 의 스레딩 모델), `Room` 이 "이 세션들을 끊어라" 목록을 쌓고
`Broadcast(IServerTransport)` 에서 비운다. `_pendingFireCount` 와 같은 모양이고,
`IServerTransport.Disconnect(sessionId, reason)` 는 이미 있다.

### 6.3 `Start` 에 준비 조건을 더한다

```
Phase == Waiting  &&  IsAuthorized(sessionId)  &&  _players.Count >= MinPlayersToStart
                  &&  방장을 뺀 모든 사람이 Ready       ← 추가
```

- **방장은 준비하지 않는다.** 시작 버튼을 누르는 것이 방장의 준비다. 방장에게도 토글을 요구하면
  같은 뜻의 조작이 둘이 된다.
- **봇은 세지 않는다.** 봇은 준비 요청을 보내지 않으므로 세면 영구히 시작할 수 없다.
- **정적 룸(`test`)은 이 조건에서 뺀다.** 두 클라이언트 개발 루프가 그 룸으로 돌아가고,
  `IsAuthorized` 가 이미 정적 룸을 예외로 두고 있다 — 개발용 훅을 그 경계 안에만 둔다는
  `test-room-bots-plan.md` §2 의 규칙과 같은 자리다. `NetworkTestUi` 에는 준비 토글을 붙여
  경로 자체는 개발 루프에서도 눌러 볼 수 있게 한다.

### 6.4 강제 퇴장의 한계를 문서에 적는다

**계정이 없으므로 킥은 소켓을 끊는 것까지다.** 초대 코드를 아는 사람은 다시 들어올 수 있다.
막으려면 신원이 필요하고 이 프로젝트에는 없다. 비공개 방의 코드가 실제 관문이라는 사실을
바꾸지 않는다.

끊긴 클라이언트가 **이유를 알아야 한다.** 모르면 `SessionFailureKind.ConnectionLost` 로 읽혀
자동 재시도(0.5·1·2·4초)가 그 방에 다시 붙는다. 닫힘 코드를 하나 정해
(`4003`) `SessionFailure` 에 재시도하지 않는 종류를 더한다. 브라우저가 닫힘 코드를 잃는
경로가 있으면 재입장이 일어날 수 있고, 그것은 위 단락과 같은 한계다.

### 6.5 테스트 (`tests/Modules.Tests`)

- 준비하지 않은 사람이 있으면 `Start` 가 거부된다 / 전원 준비하면 통과한다
- 방장은 준비 없이 시작할 수 있다
- 봇이 있어도 시작할 수 있다
- `ReturnToLobby` 뒤 전원의 `Ready` 가 내려간다
- 이미 쓰이는 캐릭터를 요청하면 거부된다 / 자기 것을 다시 요청하면 아무 일도 없다
- 방장이 아닌 세션의 `Kick`·`TransferHost` 가 무시된다
- 킥은 **대상 세션만** 끊는다
- 방장 위임 뒤 옛 방장의 `Start` 가 거부되고 새 방장의 것이 통과한다
- 코덱 왕복 — 넓힌 명단 항목, `RoomStateMaxWireSize` 상한

`tests/Architecture.Tests` 는 영향이 없다. 모듈이 늘지 않고 참조 방향도 그대로다.

---

## 7. 클라이언트 구현

### 7.1 프로토타입 코드의 처분

| 파일 | 처분 | 이유 |
|---|---|---|
| `LobbyManager.cs` | **삭제** | 규칙이 `Room.cs` 로 간다. 남기면 두 번째 권위가 된다 |
| `ILobbyTransport.cs`(+`OfflineLobbyTransport`) | **삭제** | 실제 채널은 `Control`/`RoomState`. 구현 하나인 인터페이스는 금지 항목 (§1.2) |
| `LobbyTypes.cs` | **삭제** | `LobbyRequestKind`→`ControlKind`, `LobbyEventKind`→`RoomState` 전문, `LobbyPlayer`→`RoomPlayerEntry` |
| `LobbyConfig.cs` | **삭제** | 정원·최소 인원은 서버 값의 사본이었다. 남는 표현 값은 없다 |
| `LobbyBootstrap.cs` | **삭제** | 씬을 쓰지 않는다 |
| `LobbyRoom.cs`, `LobbySlot.cs`, `LobbySlotPicker.cs`, `LobbyHud.cs` | **삭제** | 3D 스탠드 대기실. §2 의 결정으로 쓰지 않는다 |
| `LobbyMannequin.cs` | **재사용** | 캐릭터 미리보기의 몸. 블록 비율과 절차적 idle 이 이미 들어 있다 |
| `LobbyCharacterCatalog.cs` | **재사용(정리)** | 인덱스 = 와이어 값으로 맞추고 개수를 `ProtocolInfo` 와 맞춘다 |
| `Lobby.unity`, `LobbyHUD.uxml`, `lobby.uss` | **삭제** | 스타일은 `main-lobby.uss` 로 통합. 씬은 Build Settings 에도 없다 |
| 에디터 메뉴 `Create Lobby Scene` | **삭제** | 만들 씬이 없다 |
| `UI/RoomView.cs`, `Controllers/RoomController.cs`, `templates/RoomPopup.uxml` | **승계 후 삭제** | 내용은 대기방 페이지로 옮긴다 (§7.2) |
| `lobby-builder` 스킬 + `netcode-integration.md` | **다시 쓴다** | 없어진 전송과 17개 `// NETCODE:` 표식을 가리키는 문서를 남기지 않는다. 복제 표는 §5 로 대체된다 |

### 7.2 모달 팝업에서 전체화면 페이지로

지금 대기방은 `PopupHost` 위의 모달이다. 두 가지가 걸린다.

- 뒤에 방 목록이 살아 있다. 모달로 막고 있지만, 막아야 하는 화면이 그 자리에 있다는 것 자체가
  구조의 문제다 — 방 안에서 다른 방을 누르는 것은 지금 방을 조용히 버리는 일이다.
- `.popup-frame` 은 `max-height: 84%` 에 스크롤이 없다. `RoomView` 는 **그래서** 정원 8칸을
  다 그리지 못하고 "몇 줄만" 그리는 규칙을 따로 만들어야 했다(그 주석이 파일에 있다). 준비 ·
  캐릭터 · 킥 메뉴가 붙으면 같은 벽에 다시 부딪힌다.

**`MainLobby.uxml` 을 두 페이지로 가른다.**

```
#lobby-root
 ├─ #header            (로고 · 프로필 · 접속 상태)   — 두 페이지 공용
 ├─ #page-browser      (#side-rail · #room-panel · #action-panel)   ← 지금의 #content
 ├─ #page-room         (신규 — 대기방)
 ├─ #status            (상태 줄 · 다시 시도)         — 두 페이지 공용
 ├─ #popup-root        (설정 · 확인 대화상자 · 킥 확인)
 ├─ #toast-root
 └─ #loading-overlay
```

페이지 전환은 `display` 하나다. **`RemoveFromHierarchy` 를 쓰지 않는다** — 게임 HUD 의 역할
분리는 정보 누출을 막기 위해 트리에서 뽑아내지만, 여기서 감출 정보는 없고 뽑아내면 돌아올 때
다시 만들어야 한다.

새 파일:

| 파일 | 역할 |
|---|---|
| `templates/GameLobbyPage.uxml` | 대기방 페이지 껍데기 |
| `templates/RosterRow.uxml` | 명단 한 줄 (이름 · 캐릭터 · 준비 도장 · 방장 · 행 메뉴) |
| `templates/CharacterPicker.uxml` | 캐릭터 목록 + 미리보기 자리 |
| `UI/GameLobbyView.cs` | `RoomView` 승계. 상태를 들지 않고 `NetSession`·`NetworkClient` 에서 읽어 그린다 |
| `UI/RosterRowView.cs` | 한 줄. 킥·위임 메뉴가 여기 붙는다 |
| `UI/CharacterPickerView.cs` | 선택과 미리보기 |
| `Controllers/GameLobbyController.cs` | `RoomController` 승계. 팝업 여닫기가 아니라 **페이지 전환**을 세션 상태에 맞춘다 |
| `Models/RoomMember.cs` | 명단 한 줄의 클라이언트 투영 — id · 이름 · 준비 · 캐릭터 · 방장인가 · 자신인가 · 봇인가 |

`RoomMember` 를 두는 이유: `RosterEntry` + `RoomState.HostPlayerId` + `LocalPlayerId` 를
조합하는 계산이 지금 `RoomView` 안에 세 군데 흩어져 있다(`Tag`, `RefreshRoster`, `StartNote`).
한 줄이 무엇인지 한 곳에서 답하면 EditMode 테스트도 그 함수 하나에 붙는다.

### 7.3 `NetSession` 에 더하는 문

```csharp
public bool SetReady(bool ready);        // Control(SetReady)
public bool SetCharacter(byte id);       // Control(SetCharacter)
public bool KickPlayer(byte playerId);   // Control(KickPlayer)
public bool TransferHost(byte playerId); // Control(TransferHost)
public bool IsLocalReady { get; }        // 명단에서 읽는다 — 사본을 들지 않는다
```

`CanStart` 에 준비 조건을 더한다. **UI 의 친절이고 판정이 아니다** — 서버가 다시 본다는
`RoomView` 의 주석 그대로다.

로컬 준비 상태를 `NetSession` 이 따로 들지 않는다. 자기 사본을 들면 서버가 거부한 토글이
화면에만 남고, 그 차이는 눈으로 잡히지 않는다.

### 7.4 UI/UX

- **명단은 정원 전체를 그린다.** 8칸 중 2칸인 방과 정원 2인 방이 같아 보이던 문제가 페이지에서는
  줄 수로 해결된다. `RoomView` 가 팝업 높이 때문에 만들었던 "모자란 만큼만 그리기" 규칙을 버린다
- 준비는 **색과 도장으로 먼저** 말한다. 숫자(`3/4 준비`)는 보조
- 방장 · 자기 행 표식은 승계. 봇은 새로 표시한다(와이어에 자리를 만들었다)
- 시작 버튼이 꺼진 이유는 항상 한 줄로 쓴다 — `StartNote` 를 승계·확장(준비 미완 인원)
- 킥은 **확인 대화상자**를 지난다. 되돌릴 수 없고 대상은 아무 잘못이 없을 수 있다
- 캐릭터는 이미 쓰이는 것을 **비활성으로 보여준다.** 감추면 8종 중 몇 종인지 알 수 없다
- 결과 화면(`Ended`)은 대기방 페이지 안의 배너로. 방장에게만 "로비로" 버튼
- 스타일시트는 `main-lobby.uss` 하나로. `lobby.uss` 를 남기면 같은 요소가 두 규칙을 받는다

### 7.5 Prefab / Scene

**프리팹을 만들지 않는다.** 이 프로젝트에서 재사용 단위는 `VisualTreeAsset` 이고
`CloneTree()` 가 `Instantiate()` 다 — `MainLobby.uxml` 의 머리 주석이 그렇게 적어 두었고
`templates/` 가 그 규칙의 결과다. 대기방도 같은 규칙을 따른다.

씬은 `MainLobby.unity` 하나가 그대로다. **Tools ▸ NV ▸ Scene ▸ Create Main Lobby Scene** 이
만드는 것도 그대로다 — 대기방이 같은 씬 안이므로 생성기가 아는 것이 늘지 않는다.

캐릭터 미리보기만 씬 오브젝트가 필요하다. `LobbyMannequin` 한 체를 카메라 하나와 함께 화면
밖에 세워 `RenderTexture` 로 받고, UI Toolkit 쪽은 그 텍스처를 배경으로 쓴다. 런타임 생성이므로
씬에 남는 것은 없다.

---

## 8. 단계별 구현 순서

각 단계가 끝날 때 **서버 `dotnet build` 0 경고 + `dotnet test`**, 그리고 클라이언트
`dotnet build Assembly-CSharp.csproj` 로 컴파일 검사. 새 `.cs` 는 그 프로젝트의 `Compile`
목록에 없으므로 처음엔 `CS0234` 로 떨어진다 — 한 줄 넣고 다시 돌린다.

| Phase | 내용 | 확인 |
|---|---|---|
| **0** | 대기방 페이지 껍데기. `RoomView`/`RoomController` 의 내용을 `#page-room` 으로 옮기고 모달을 없앤다. **와이어 변경 없음** | 지금과 같은 기능이 페이지로 동작한다. 방 만들기 → 대기방 → 시작 → 게임 → 복귀 |
| **1** | 와이어. `ProtocolInfo.Version` 4, 명단 항목 확장, `ControlKind` 넷, `ProtocolInfo.LobbyCharacterCount`. 서버는 값을 싣고 클라이언트는 읽기만 한다 | 코덱 왕복 테스트. `NetworkClient` 명단 비교에 새 필드가 들어갔는가(§5.1 의 함정) |
| **2** | 준비. `PlayerEntity.Ready`, `SetReady` 커맨드, `Start` 조건, `ResetToWaiting` 해제, 페이지의 토글·도장·시작 문구 | 두 클라이언트로 실제 게이팅. 종료 후 로비 복귀에서 준비가 내려가는가 |
| **3** | 캐릭터. 서버 중복 거부, 카탈로그 정리, 선택 UI, `LobbyMannequin` 미리보기 | 두 클라이언트에서 서로의 선택이 보인다. 같은 캐릭터 요청이 거부된다 |
| **4** | 방장 권한. 킥(+닫힘 코드·실패 종류·확인 대화상자), 위임 | 킥된 쪽이 이유를 보고 자동 재시도가 돌지 않는다. 위임 뒤 시작 권한이 옮겨진다 |
| **5** | 정리. 프로토타입 삭제(§7.1), `lobby-builder`·`netcode-integration.md` 재작성, `conventions.md` 에 함정 기록, `readme.md`·루트 `CLAUDE.md`·`NVproject/CLAUDE.md` 갱신 | `grep -r "NETCODE:" Assets/Scripts/Lobby` 가 비어 있다. 문서가 없는 파일을 가리키지 않는다 |

### 8.1 마이그레이션 전략

- **Phase 0 은 와이어를 건드리지 않는다.** 화면 구조 변경과 프로토콜 인상을 같은 커밋에 넣으면,
  두 클라이언트가 붙지 않을 때 원인이 UI 인지 와이어인지 가릴 수 없다.
- **Phase 1 부터는 서버·클라이언트를 함께 배포한다.** 426 거부는 부분 배포를 허용하지 않는다.
  브랜치는 `feature/lobby/game-lobby`, base 는 `main`.
- **프로토타입 삭제는 마지막(Phase 5)이다.** 남겨 두면 참고할 수 있고, 지운 뒤에 준비 규칙의
  세부(예: 준비 해제 시점)를 다시 확인하러 갈 곳이 없어진다. 대신 삭제 전에는 `Lobby.unity` 가
  Build Settings 에 없다는 사실을 확인한다 — 있으면 빌드가 죽은 씬을 싣는다.
- 각 Phase 는 하나의 커밋 계열이고 제목은 `feat(lobby):` / `feat(realtime):` / `refactor:` /
  `docs:` 로 붙인다.

---

## 9. 확장 자리 — 지금 만들지 않고 막지도 않는다

| 확장 | 자리 | 지금 하는 일 |
|---|---|---|
| **팀전** | 명단 항목 `flags` bit2~7, 또는 바이트 하나 추가 | 예약 비트를 남긴다. 팀 배정 판정은 `Room` 안에 생긴다 |
| **관전자** | 같은 `flags` 의 비트 하나 | 관전자는 **참가자에 플래그가 붙은 것**이다. 명단을 둘로 나누지 않는다 — 나누면 정원·방장 승계·준비 판정이 전부 두 벌이 된다 |
| **자리(슬롯) 이동·교환** | 명단 항목에 `slotIndex` 바이트 | 스폰 선택을 `PlayerId` 에서 떼어내는 것이 선행 조건이다(§2). 서버가 동시 요청을 중재하는 규칙은 프로토타입 `LobbyManager.HandleMove`·`HandleSwapResponse` 에 이미 쓰여 있으므로, 삭제 전에 이 문서로 옮긴다 |
| **대기방에서 맵 변경** | — | **지금 하지 않는다.** 맵은 방 생성 시 고정이고 맵 해시는 접속 시 검사된다. 로비에서 바꾸면 재핸드셰이크나 맵 변경 전문 + 클라이언트 씬·해시 재검사가 필요하다. 싼 길은 방을 다시 만드는 것이고, 그 길이 이미 있다 |
| **준비 → 카운트다운 → 자동 시작** | `RoomState` 헤더에 시작 틱 + 길이 | §2 에서 뺐다. 넣을 때는 **틱 + 길이**를 복제하고 남은 시간을 클라이언트가 계산한다 — 프레임마다 float 을 보내면 흔들리고, 클라이언트가 자기 시계로 세면 매치가 사람마다 다른 시각에 시작한다 |

---

## 10. 열린 항목

1. **캐릭터 선택이 게임에 영향을 주는가.** 지금은 순수 외형이고 서버는 중복만 본다. 능력이나
   역할 선호로 확장되면 그 순간 `Shared` 로 가는 값이 늘어난다. 외형으로 못 박을 것인지 결정이
   필요하다.
2. **원격 플레이어의 몸이 캐릭터를 반영하는가.** `RemotePlayerPuppet` 은 지금 캐릭터를 모른다.
   반영하려면 `characterId` 가 스냅샷 쪽에도 필요한지(매치 중 바뀌지 않으므로 `RoomState`
   전문으로 충분할 것으로 본다) 확인해야 한다.
3. **킥 재입장.** §6.4 의 한계를 그대로 둘 것인지, 방 단위 임시 차단 목록(코드 + 원격 IP)을
   둘 것인지. IP 는 프록시 뒤에서 한 통에 묶이므로 그 자체로 새 함정이다.
4. **매치 종료 후 명단.** 결과 화면에서 나간 사람의 자리를 어떻게 보여줄지. `Ended` 단계에서
   `Leave` 가 오면 명단이 줄어드는데, 결과는 그 사람을 포함해 판정된 값이다.

---

## 11. 계획과 달라진 곳

계획을 고쳐 적지 않고 무엇이 왜 달라졌는지 남긴다. 계획서를 결과로 덮어쓰면 판단이 바뀐
지점이 사라진다.

| 계획 | 실제 | 왜 |
|---|---|---|
| `Models/RoomMember.cs` 를 만들어 명단 한 줄을 투영한다 | 만들지 않았다 | 한 줄이 무엇인지 답하는 계산이 `GameLobbyView` 안의 두 함수(`Tag`, `AddHostActions`)로 줄었다. 지금 투영 타입을 만들면 `RoomPlayerEntry` 를 한 번 더 감싸는 것뿐이다 — 팀·관전자가 붙어 한 줄의 뜻이 늘어날 때 만든다 |
| `templates/RosterRow.uxml`·`CharacterPicker.uxml` 를 나눈다 | 나누지 않았다 | 명단 줄은 요소 넷이고 캐릭터 칸은 페이지 안에 있다. 템플릿으로 뽑으면 파일이 둘 늘고 얻는 것이 없다 — 반복 단위가 커지면 그때 뽑는다 |
| 캐릭터 선택을 팝업으로 연다 | 페이지의 세 번째 칸 | 팝업은 열려 있는 동안 남이 캐릭터를 집어 가는 것을 반영해야 하고, 그러려면 갱신 신호를 팝업까지 끌어와야 한다. 페이지 안에 있으면 명단 전문 주기에 함께 다시 그려진다 |
| 킥의 자격은 `IsAuthorized` | 방장 세션인지 직접 본다 | `IsAuthorized` 는 정적 룸에서 전원에게 참을 돌려준다. 그것은 "시작을 누를 수 있다" 를 위한 예외이고, 남을 쫓아내는 권한은 다른 것이다 |
| 명단 항목의 `flags` 에 `Ready` 만 | `Ready` + `Bot` | 봇 여부는 서버가 이미 알고 클라이언트는 알 방법이 없었다. 같은 바이트에 자리가 있고 프로토콜 인상을 한 번 더 하지 않으려면 지금 넣어야 했다 |
| 준비 UI 는 토글 하나 | 방장에게는 준비를 보이지 않는다 | 시작 버튼이 방장의 준비다. 둘 다 보이면 같은 뜻의 조작이 두 개가 된다 |

### 확인하지 못한 것

**캐릭터 미리보기의 그림은 눈으로 확인하지 않았다.** `CharacterPreview` 는 카메라 하나를
렌더 텍스처에 물려 UI 배경으로 넘기는데, 이 프로젝트에서 그림의 판정은 사람의 눈이다
(`NVproject/CLAUDE.md` — URP 에서 특정 카메라 캡처가 실패하고, 애니메이션의 자연스러움은
수치로 확인할 수 없다). 컴파일과 수명(도메인 리로드에서 무대가 늘어나지 않는지)은 코드로
확인했고, **프레임에 인형이 제대로 들어오는지는 에디터에서 봐야 한다.** 어긋나면 손댈 곳은
`CharacterPreview.Create` 의 카메라 위치·시야각 셋뿐이다.

두 클라이언트로 실제 준비·캐릭터·킥·위임을 눌러 보는 것도 남아 있다. 서버 쪽 판정은
`ReadyTests`·`CharacterTests`·`HostPowerTests` 로 못질했지만, 그것은 룸의 판정이고 화면과
와이어가 함께 도는 것은 아니다.
