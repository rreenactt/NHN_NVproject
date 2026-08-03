# 초대 코드 세션 시스템 실행 계획

방장이 방을 만들면 초대 코드가 생기고, 참가자가 그 코드로 같은 룸에 붙고, 방장이 시작을 누르면 매치가 시작된다. 클라이언트와 서버 양쪽을 함께 바꾼다.

설계 기준은 `NVserver/docs/architecture.md`, `NVserver/docs/conventions.md`, `NVproject/CLAUDE.md`, `.claude/skills/game-rules/references/ruleset.md`. 이 파일은 임시 작업 산출물이다. 실행 중 계획이 바뀌면 이 파일을 고친다.

---

## 이 기능은 서버 기능이다

초대 코드 흐름이 요구하는 것은 대부분 클라이언트가 결정할 수 없는 것들이다.

| 요구 | 현재 서버 | 필요한 것 |
|---|---|---|
| 방을 "만든다"는 행위 | 없다. 아무 룸 id 로 접속하면 그 순간 룸이 생긴다 (`RoomRegistry.GetOrCreate`) | 명시적 생성. 코드를 모르는 접속은 거부 |
| 초대 코드 | 없다. 룸 id 를 사용자가 타이핑한다 | 서버가 생성·충돌 회피·수명 관리 |
| 방장 | 개념이 없다 | 소유 세션 추적 + 이탈 시 승계 |
| 대기/진행 구분 | 없다. 틱 루프가 항상 시뮬레이션한다 (`GameLoopService.RunTick`) | 룸 단계 `Waiting → Playing → Ended` |
| 시작 신호 | 없다 | 방장의 제어 메시지 → 틱 경계에서 단계 전이 |
| 명단 통보 | 없다. 서버→클라 메시지는 Welcome·Snapshot 둘뿐 | `Event(0x82)` 코덱 신설 → **프로토콜 버전 2** |
| 룸 정리 | 없다. 룸은 만들어지면 영원히 남는다 | 빈 룸 만료 (`MaxRooms` 16 이 죽은 방으로 차는 것을 막는다) |

클라이언트 단독으로는 만들 수 없다. 앞선 계획(세션 계층 분리)은 유효하고 그 위에 얹는다.

## 조사에서 나온 두 가지 결정적 사실

**1. 목표 배치가 씨드로 결정된다.** `MatchManager.BeginMatch:155` 는 `config.placementSeed != 0 ? placementSeed : Environment.TickCount` 로 난수를 만들고, 그 난수가 문·열쇠 10개·장치 9개의 위치를 정한다. 지금처럼 각 클라이언트가 자기 `TickCount` 로 시작하면 **플레이어마다 문이 다른 곳에 있다.** 증상은 "다른 사람이 없는 문에 열쇠를 꽂는다"로 나타나며 네트워크 버그처럼 보이지 않는다. 시작 신호는 반드시 씨드를 실어야 한다.

**2. 매치 단계는 클라이언트에 이미 있다.** `MatchPhase { Lobby, RoleReveal, Playing, Ended }` 와 `BeginMatch(PlayerAgent seeker)` 가 그대로 쓸 수 있는 모양이다 — Seeker 를 **인자로 받는다.** 서버가 채워야 하는 빈칸이 이미 함수 시그니처에 있다. `BeginMatch` 는 재호출 안전하다고 명시되어 있어 재경기도 같은 경로다.

---

## 계약 설계

### 초대 코드

6자, 알파벳 `abcdefghjkmnpqrstuvwxyz23456789`(31자 — `i·l·o·0·1` 제외, 받아쓰기에서 갈리는 문자를 뺀다). 31^6 ≈ 8.9억, 동시 룸 상한이 16이므로 추측 접속은 실질적으로 불가능하다. 내부 표현은 소문자 — 그래야 기존 `RoomRegistry.IsValidRoomId`(소문자·숫자·하이픈, 32자) 규칙을 그대로 만족하고 룸 id 검증을 두 벌로 만들지 않는다. **화면에는 대문자로 보여주고 입력은 소문자로 정규화한다.**

### 초대 링크 (코드와 함께 제공)

코드가 정본이고 링크는 그것을 감싼 것이다. 서버는 링크를 만들지 않는다 — 배포 URL 을 서버가 알 수 없고, 알게 만들면 배포 환경마다 설정이 하나 늘어난다. 링크는 **클라이언트가 자기 실행 위치에서 조립한다.**

```
WebGL   https://{배포 페이지}/?code=k7qm4p     Application.absoluteURL 에서 조립
그 외    코드만 표시                            링크를 만들 근거가 없다
```

WebGL 은 탭이 열릴 때 `?code=` 를 읽어 로비의 코드 칸을 자동으로 채운다. **자동 접속까지 하지 않는다** — 이름 입력과 서버 주소 확인을 건너뛰면 링크를 잘못 눌렀을 때 되돌릴 화면이 없다.

에디터·스탠드얼론에는 링크가 동작할 방법이 없다(URL 스킴 등록은 별개의 배포 작업이다). 그래서 **코드 입력 경로를 항상 유지한다** — 개발 중 3클라이언트 테스트는 전부 이 경로로 한다.

### HTTP (룸 생애주기 — 상태를 바꾸지 않는 것과 큐에 넣는 것만)

| 메서드 | 응답 | 비고 |
|---|---|---|
| `POST /rooms` `{ map }` | `201 { code, hostToken, map, mapHash, capacity, minPlayers }` | 룸 생성. `hostToken` 은 **생성자에게만** 돌아간다 |
| `GET /rooms/{code}?v=` | `200 { code, map, mapHash, phase, playerCount, capacity, hostPlayerId }` | 참가 전 프리플라이트 |
| | `404` 없는 코드/만료, `400` 형식 오류, `426` 버전 불일치, `503` 정원 초과, `409` 이미 진행 중 | 이 네 자리가 실패 원인 분리의 전부다 |
| `GET /rooms` | `200 [...]` — **개발 전용 플래그 뒤에 둔다** | 초대 코드 모델에서 공개 목록은 기능이 아니라 결함이다 |

`POST /rooms` 는 레지스트리 수준의 생성이므로 Kestrel 스레드에서 해도 된다 — 지금 접속 경로가 이미 그렇게 하고 있다. **룸 상태를 바꾸는 것(시작·강퇴)은 HTTP 로 하지 않는다.** 틱 루프가 소유자이므로 `RoomCommand` 로 간다.

### 시작은 WebSocket 제어 메시지다 (HTTP 가 아니다)

`hostToken` 은 **접속 시 한 번만** 쓴다. `ws?v=2&room={code}&token={hostToken}` 로 붙은 세션이 방장 세션이 된다. 그 뒤 시작 권한은 "네 세션이 현재 방장 세션인가"로 판정한다.

이렇게 하는 이유: 토큰을 매 요청에 쓰면 방장 승계가 불가능해진다. 방장이 나갔을 때 남은 사람에게 토큰을 새로 줄 방법이 없기 때문이다. 세션 신원으로 판정하면 승계는 `HostSessionId` 를 바꾸는 일 하나로 끝난다.

새 C→S opcode 하나가 필요하다.

```
0x02 Control   byte kind   (1 = StartMatch, 2 = Leave)
```

### 상태는 반복 송신한다 — 한 번 보내는 이벤트로 만들지 않는다

`Event(0x82)` 를 **룸 상태 전문**으로 정의하고 2Hz 로 계속 보낸다.

```
0x82 Event
  byte  kind            (1 = RoomState)
  byte  phase           (0 Waiting, 1 Playing, 2 Ended)
  byte  hostPlayerId
  byte  seekerPlayerId  (phase >= Playing)
  byte  outcome         (phase == Ended)
  byte  playerCount
  byte[playerCount] playerIds
  uint  startTick
  int   placementSeed
```

한 번짜리 "매치 시작했다" 알림으로 만들면 안 된다. 세션의 송신 채널은 `Bounded(32, DropOldest)` 이고(`RealtimeConstants.Sessions.OutboundCapacity`) 밀리면 오래된 프레임을 버린다 — 스냅샷은 다음 틱이 대체하므로 유실이 문제되지 않지만, 시작 알림이 그 규칙에 걸리면 그 클라이언트는 로비 화면에 영원히 남는다. 멱등한 상태 전문을 반복하면 ack·재전송 기계장치 없이 수렴한다.

### 룸 단계별 규칙

| 단계 | 틱 | 시뮬레이션 | 스냅샷 | 입력 | 신규 참가 |
|---|---|---|---|---|---|
| `Waiting` | 진행 | 안 함 | 안 보냄 | 버림 | 허용 (정원까지) |
| `Playing` | 진행 | 함 | 매 틱 | 처리 | **거부(`409`)** — 비대칭 매치 중간 합류는 규칙을 깬다 |
| `Ended` | 진행 | 정지 | 안 보냄 | 버림 | 거부 |

틱은 단계와 무관하게 계속 올린다. Welcome 이 `room.Tick` 을 싣고 클라이언트가 `_inputTick = ServerTick + 2` 로 잡으므로(`NetworkClient.ReadWelcome:310`), 여기서 시계를 멈추면 시작 순간에 입력 틱 기준이 어긋난다.

---

## 결정이 필요한 항목

| # | 질문 | 기본안 |
|---|---|---|
| 1 | 매치 규칙 판정을 이번에 서버로 옮기는가 | **확정: 옮기지 않는다(단계적 권위).** 서버는 룸·단계·역할·씨드·시작만 소유하고, 피격·열쇠·탈출 판정은 당분간 클라이언트에 남는다. 대가를 아래 "정직한 한계"에 적었다 |
| 2 | 표시 이름(닉네임) | v1 에 넣는다. 12자 ASCII, 접속 쿼리로 전달, 서버가 절단·필터. 와이어에 문자열이 처음 들어오므로 태스크로 분리했다 (S07) |
| 3 | Seeker 를 누가 정하는가 | 서버가 무작위. 방장 지정은 후속 |
| 4 | 최소 인원 | 2명 (Seeker 1 + Runner 1 — 룰셋의 하한) |
| 5 | 방장 이탈 | 남은 세션 중 최소 `PlayerId` 승계. 아무도 없으면 룸 만료 |
| 6 | 중간 합류 | 거부. 관전은 범위 밖 |
| 7 | 초대 수단 | **확정: 코드 + 공유 URL.** 코드가 정본, 링크는 WebGL 에서만 클라이언트가 조립한다. 코드 입력 경로는 항상 유지 (C03, C06) |

---

## 정직한 한계 (결정 1의 대가)

매치 규칙이 클라이언트에 남는 동안:

- 피격·열쇠 삽입·탈출은 각 클라이언트가 자기 화면에서 판정한다. 조작한 클라이언트는 자기가 맞지 않았다고 결정할 수 있다. 이 저장소의 원칙(`NVproject/CLAUDE.md`: "a client that decides its own hits decides it was never hit")에 어긋난다는 것을 알고 미루는 것이다.
- 역할과 배치 씨드가 서버에서 오므로 **문·열쇠·장치 위치와 누가 Seeker 인지는 모든 클라이언트가 일치한다.** 어긋날 수 있는 것은 그 뒤에 벌어지는 판정이다.
- 이동과 충돌은 이미 서버 권위다. 즉 위치는 맞고 결과가 갈릴 수 있다.
- `MatchManager` 서버 이관은 별도 계획이며, 이 계획의 M5 가 그 이관의 접속면을 미리 확정해 준다 — 이관 시 클라이언트 세션 계층을 다시 짜지 않아도 된다.

---

## 마일스톤

| # | 완료 시점에 동작하는 것 | 검증 |
|---|---|---|
| M1 | 세션이 씬과 분리된다 | 빈 씬에서 접속 유지, 씬 로드 후에도 소켓 유지 |
| M2 | 서버가 룸을 만들고 코드를 발급하고 만료시킨다. 단계와 방장이 있다 | `curl` 로 생성→조회→만료, `dotnet test` |
| M3 | 프로토콜 v2: 룸 상태 전문과 제어 메시지가 왕복한다 | 코덱 왕복 테스트, devtools WS 프레임 |
| M4 | 로비 화면에서 방을 만들고 코드로 참가하고 명단이 보인다 | 3클라이언트 (방장 1 + 참가 2) |
| M5 | 방장이 시작을 누르면 세 클라이언트가 같은 배치·같은 역할로 매치에 들어간다 | 문 위치·Seeker 신원이 3개 화면에서 일치 |
| M6 | 실패 11종이 화면에서 서로 다르게 보인다 | 재현 표 |

---

## M1 — 세션 코어를 씬에서 떼어낸다

### C01 — `NetSession` 도입

| 항목 | 내용 |
|---|---|
| 목적 | 접속 단계·룸 정보·실패 사유를 씬 오브젝트 밖으로 옮긴다. 지금은 `NetworkBootstrap` 이 없으면 접속 개념 자체가 없다 |
| 변경 대상 | 신규 `Assets/Scripts/Net/Session/NetSession.cs`, `SessionState.cs` |
| 내용 | `DontDestroyOnLoad` 싱글턴. `NetworkClient` 를 소유한다. 세션 단계는 `Idle → Creating → Resolving → Connecting → Handshaking → InLobby → InGame → Leaving → Failed`. `NetworkClient.ConnectionState` 를 고치지 않는다 — 그쪽은 와이어 단계, 이쪽은 세션 단계다 |
| 완료 조건 | 어느 씬에서도 `NetSession.Current` 접근 가능, 씬 로드가 소켓을 끊지 않음 |
| 함정 | 도메인 리로드는 매니지드 상태를 날리면서 `Awake` 를 다시 실행하지 않는다. `Current` 는 lazy 재탐색으로, 세션 상태는 재구성 가능한 평범한 필드로만 |

### C02 — `NetworkBootstrap` 을 씬 어댑터로 축소

| 항목 | 내용 |
|---|---|
| 목적 | 스냅샷을 몸에 적용하는 일만 남긴다 |
| 선행 | C01 |
| 변경 대상 | `Assets/Scripts/Net/NetworkBootstrap.cs`, `NetworkTestUi.cs` |
| 내용 | `AddComponent<NetworkClient>()`(`:120`)와 `connectOnStart` 제거, `NetSession.Current.Client` 구독. 맵 해시 검사와 로컬/원격 적용은 씬의 일이므로 남긴다 |
| 함정 | `connectOnStart` 를 지우면 `NetworkTestUi.Start:54` 의 패널 조건이 깨진다. 같은 커밋에서 고친다 |

---

## M2 — 서버: 룸 생애주기

### S01 — `RoomMaps` 를 맵 id 로 키잉

| 항목 | 내용 |
|---|---|
| 목적 | 생성된 코드는 `appsettings` 에 등록될 수 없다. 지금 구조로는 모든 초대 코드 룸이 조용히 `default` 맵으로 열린다 |
| 변경 대상 | `Modules/Realtime/Contracts/RoomMaps.cs`, `Api/Composition/ModuleRegistration.cs`, `appsettings.json` |
| 내용 | `For(roomId)` → `ByMapId(mapId)`. `Game:Maps` 의 의미가 "룸 id → 파일"에서 "맵 id → 파일"로 바뀐다. `RoomMaps.cs:21` 의 `FallbackKey = DefaultRoomId` 결합을 끊고 그 주석의 근거를 새 구조로 다시 쓴다 |
| 완료 조건 | `POST /rooms {map:"arena"}` 가 `arena.json` 으로 열린다. 등록되지 않은 맵 id 는 `400` — 빈 콜리전으로 열지 않는다 |
| 검증 | 기동 로그의 맵 목록, `dotnet test`(`ExportedMapTests`, `RoomMapsTests` 갱신) |
| 함정 | 이 태스크가 문서 3곳을 무효화한다 — `NVserver/docs/readme.md` 의 씬↔룸 짝 표, `NVproject/CLAUDE.md` 의 "Scene and room are a pair", `RoomMaps` 자체 주석. 같은 커밋에서 고친다 |

### S02 — 정적 개발 룸 유지

| 항목 | 내용 |
|---|---|
| 목적 | 초대 코드 전용이 되면 `room=test` 접속이 깨지고 **Build and Launch 2 Clients** 개발 루프가 죽는다 |
| 선행 | S01 |
| 변경 대상 | `appsettings.json`(`Game:StaticRooms`), `RoomRegistry` |
| 내용 | 기동 시 미리 만들어 두고 만료시키지 않는 룸. 방장이 없으므로 아무 세션이나 시작할 수 있다. `{"test": "test-room"}` |
| 완료 조건 | 코드 없이 `room=test` 로 붙어 종전처럼 2클라이언트 테스트가 된다 |

### S03 — 룸 단계와 방장

| 항목 | 내용 |
|---|---|
| 목적 | 대기와 진행을 나누고 시작 권한자를 정한다 |
| 선행 | 없음 |
| 변경 대상 | `Modules/Realtime/Simulation/Room.cs`, `RoomCommand.cs`, 신규 `RoomPhase.cs` |
| 내용 | `RoomCommandKind` 에 `Start`·`ClaimHost` 추가. `Room` 에 `Phase`, `HostSessionId`, `SeekerPlayerId`, `PlacementSeed`, `StartTick`. `Advance` 를 단계로 분기 — 위 단계 표대로. 방장 이탈 시 최소 `PlayerId` 승계. `Summarize()` 에 단계·방장을 추가 |
| 완료 조건 | `Waiting` 룸이 스냅샷을 보내지 않고, `Start` 커맨드가 틱 경계에서만 단계를 바꾼다 |
| 검증 | `dotnet test` — `RoomTests` 에 단계 전이·승계·최소 인원 미달 거부 케이스 추가 |
| 함정 | 슬롯 반납이 틱 루프의 퇴장 커맨드에서 일어난다(`Room.TryReserveSlot` 주석). 방장 승계도 같은 커맨드 안에서 해야 한다 — 접속 스레드에서 하면 퇴장이 적용되기 전에 이미 나간 세션을 방장으로 만든다 |

### S04 — 초대 코드 생성과 룸 만료

| 항목 | 내용 |
|---|---|
| 목적 | 코드 발급, 충돌 회피, 죽은 방 회수 |
| 선행 | S03 |
| 변경 대상 | `RoomRegistry.cs`, 신규 `InviteCode.cs`, `RealtimeConstants` |
| 내용 | `Create(mapId)` → 6자 코드 생성(충돌 시 재생성, 8회 실패면 `503`) + `hostToken`(16바이트 무작위). `GetOrCreate` 는 정적 룸에만 남기고 **접속 경로는 `TryGet` 으로 바꾼다** — 없는 코드로 붙으면 `404`. 만료: `Waiting` 무접속 60초 / `Playing`·`Ended` 무접속 30초 / 시작 없이 10분. 제거는 틱 루프가 한다 |
| 완료 조건 | 없는 코드는 `404`, 빈 방이 스스로 사라져 `MaxRooms` 16 이 죽은 방으로 차지 않는다 |
| 검증 | `dotnet test` — 만료·충돌 재생성·미등록 코드 거부 |
| 함정 | 코드 생성에 `DeterministicRandom` 을 쓰면 안 된다. 그쪽은 클라이언트와 같은 값을 내는 것이 목적인 시뮬레이션용이고, 초대 코드는 **예측 불가능해야 한다**. `RandomNumberGenerator` 를 쓴다 |

### S05 — HTTP 엔드포인트와 프리플라이트

| 항목 | 내용 |
|---|---|
| 목적 | 위 HTTP 계약을 구현하고, 실패 원인을 상태코드로 갈라 준다 |
| 선행 | S01, S04 |
| 변경 대상 | `Modules/Realtime/Transport/RealtimeEndpoints.cs`, `Api/Composition/ModuleRegistration.cs`(CORS) |
| 내용 | `POST /rooms`, `GET /rooms/{code}?v=`. 판정은 `IsValidRoomId`·`Rooms.MaxPlayers`·`RoomMaps` 를 재사용하고 다시 적지 않는다. `/ws` 는 이 검사를 **다시 한다** — 프리플라이트는 UX 이고 판정이 아니다. `GET /rooms` 목록은 개발 플래그 뒤 |
| 완료 조건 | 상태코드 6종(`200/400/404/409/426/503`)이 `curl` 로 재현된다 |
| 검증 | `curl -i -X POST localhost:5202/rooms -d '{"map":"backrooms"}'`, 발급된 코드로 `GET`, 없는 코드로 `GET`(404), `?v=999`(426), 8명 채운 뒤(503), 진행 중 룸(409) |
| 함정 | WebGL 의 `UnityWebRequest` 는 브라우저 XHR 이라 **CORS 가 필요하다.** WebSocket 은 CORS 대상이 아니라서 지금까지 드러나지 않았고 이 태스크에서 처음 나온다. 증상은 콘솔 CORS 오류 한 줄과 빈 응답 |

---

## M3 — 프로토콜 v2

### S06 — `Event(0x82)` 룸 상태 전문 + `Control(0x02)`

| 항목 | 내용 |
|---|---|
| 목적 | 명단·단계·역할·씨드를 클라이언트에 보내고, 방장의 시작을 받는다 |
| 선행 | S03 |
| 변경 대상 | `Shared/Contracts/Enums/MessageOpcode.cs`, 신규 `Shared/Contracts/Messages/RoomStateMessage.cs`·`ControlMessage.cs`, `Shared/Serialization/MessageCodec.cs`, `ProtocolInfo.Version` **1 → 2**, `GameSession.Dispatch`, `Room.Broadcast` |
| 내용 | 위 "계약 설계"의 두 포맷. 문자열 없음(이름은 S07). `RoomState` 는 2Hz(15틱마다) 전 세션에 브로드캐스트, `Control` 은 방장 세션만 `StartMatch` 가 통과 |
| 완료 조건 | `dotnet test` 의 `CodecRoundTripTests`·`WireSizeTests` 가 새 opcode 를 덮는다 |
| 검증 | `dotnet test`, devtools WS 프레임에서 0x82 관찰 |
| 함정 | `ProtocolInfo.Version` 을 올리는 순간 구버전 클라이언트는 업그레이드 전에 `426` 으로 전부 거부된다. 서버와 클라이언트를 **같은 커밋에 배포**해야 하고 WebGL 빌드는 수 분이 걸린다. `Shared` 는 C# 9·`System.Numerics` 제약이 걸린 어셈블리이므로 편집 후 `dotnet build` 통과만으로 끝내지 말고 Unity 에디터 컴파일도 확인한다 |

### S07 — 표시 이름 (결정 2)

| 항목 | 내용 |
|---|---|
| 목적 | 명단에 사람 이름이 보인다 |
| 선행 | S06 |
| 변경 대상 | `ProtocolInfo`(`NameQueryKey`), `RoomStateMessage`, `GameSession` |
| 내용 | 접속 쿼리 `name=`. 서버가 12자로 절단하고 제어문자·비ASCII 를 걸러 세션에 저장, `RoomState` 에 길이 접두 UTF-8 로 싣는다. 저장소가 없으므로 이름은 세션 수명만큼만 산다 |
| 완료 조건 | 3클라이언트 명단에 각자 이름이 뜨고, 32자 입력이 12자로 잘린다 |
| 함정 | 와이어에 문자열이 처음 들어온다. 길이를 신뢰하지 않고 상한을 넘으면 프레임을 버린다 — `Sessions.ReceiveBufferBytes` 256 이 상한이다. 이름 중복·사칭은 막지 않는다(계정이 없다). 그 사실을 UI 에 드러낸다 |

---

## M4 — 클라이언트 로비

### C03 — `RoomApi`

| 항목 | 내용 |
|---|---|
| 목적 | 방 생성과 프리플라이트를 한 곳에 둔다 |
| 선행 | S05, C01 |
| 변경 대상 | 신규 `Assets/Scripts/Net/Session/RoomApi.cs`, `RoomInfo.cs`, `InviteCodeText.cs`, `InviteLink.cs` |
| 내용 | `POST /rooms`(생성) · `GET /rooms/{code}`(참가 전 확인). 코드 입력 정규화(공백·하이픈 제거, 대→소문자, 6자, 제외 문자 거부)를 `InviteCodeText` 한 곳에만 둔다. `InviteLink` 는 `Application.absoluteURL` 로 공유 링크를 조립하고, 실행 시 `?code=` 를 읽어 코드를 돌려준다 — 링크 조립·해석이 같은 파일에 있어야 한 쪽만 고치는 일이 없다 |
| 완료 조건 | 생성 → 코드 수신 → 같은 코드로 조회가 왕복한다. `?code=K7QM4P` 로 열면 정규화되어 코드 칸이 채워진다 |
| 함정 | WebGL 은 스레드가 없다. 코루틴으로만. `isNetworkError` 와 `responseCode` 는 다른 정보다 — `426`·`404` 는 네트워크 오류가 아니라 정상 응답이다. `absoluteURL` 은 에디터에서 빈 문자열이므로 링크 기능은 `#if UNITY_WEBGL` 이 아니라 **값 유무로 분기한다** — 그러면 에디터에서도 파싱 경로를 테스트할 수 있다. 쿼리스트링은 사용자가 고칠 수 있는 입력이다. 코드 형식 검증을 반드시 통과시킨다 |

### C04 — 실패 분류기

| 항목 | 내용 |
|---|---|
| 목적 | 11종 실패를 화면에서 갈라낸다 |
| 선행 | C03 |
| 변경 대상 | 신규 `Assets/Scripts/Net/Session/SessionFailure.cs` |
| 내용 | `ServerUnreachable, VersionMismatch, InvalidCode, UnknownCode, RoomFull, RoomInProgress, RoomLimit, NotHost, TooFewPlayers, HandshakeTimeout, ConnectionLost, MapHashMismatch` + 사유별 문구와 다음 행동. 접속은 항상 프리플라이트를 먼저 통과한다 |
| 완료 조건 | 재현 표(M6) 전부가 서로 다른 문구를 낸다 |
| 함정 | 브라우저는 WS 핸드셰이크 실패 사유를 JS 에 주지 않는다 — `1006` 하나뿐이다(`ClientTransportFactory.FailureReason`). 프리플라이트가 그 앞에서 원인을 잡는 유일한 수단이고, 프리플라이트 통과 후의 `/ws` 실패는 진짜 전송 문제라는 것 자체가 정보다. 프리플라이트와 업그레이드 사이에 정원이 찰 수 있으므로 그 문구는 "정원이 방금 찼을 수 있다"로 남긴다 — 없는 확실성을 주지 않는다 |

### C05 — 정상 퇴장과 재시도

| 항목 | 내용 |
|---|---|
| 목적 | 나가는 것과 끊기는 것을 서버가 구분하게 한다 |
| 선행 | C01, S06 |
| 변경 대상 | `EditorWebSocketTransport.cs`, `NetSession` |
| 내용 | `Dispose` 전에 `CloseAsync(NormalClosure)`(WebGL 은 `NvWsClose` 가 이미 있다). 지수 백오프 재시도 0.5·1·2·4초 4회 상한. `ConnectionLost` 만 재시도하고 `VersionMismatch`·`UnknownCode`·`RoomInProgress` 는 하지 않는다 |
| 완료 조건 | 종료 시 서버 로그가 "정상 종료"로 남는다 |
| 함정 | 재시도는 **새 세션**이다. `PlayerId` 가 바뀌고 진행 중 룸은 아예 거부된다. 이것을 "복구"라고 표시하면 안 된다 |

### C06 — 로비 UI

| 항목 | 내용 |
|---|---|
| 목적 | 방 만들기 / 코드로 참가 / 명단 / 시작 |
| 선행 | C03, C04, C05, S06 |
| 변경 대상 | 신규 `Assets/Scenes/Lobby.unity`, `Assets/Resources/UI/Lobby.uxml`·`lobby.uss`, `Assets/Scripts/Net/Session/LobbyController.cs`, `Assets/Editor/LobbySetup.cs` |
| 내용 | 첫 화면은 두 갈래(방 만들기 / 코드로 참가) + 서버 주소 + 이름. 방 안에서는 코드 크게 표시(대문자) + **코드 복사 / 링크 복사 두 버튼**, 명단(방장 표시), 맵 이름, 시작 버튼(방장에게만, 최소 인원 미달이면 비활성 + 이유 표시), 나가기. 링크 복사 버튼은 링크를 만들 수 없는 플랫폼에서 숨긴다. UI Toolkit — `game-hud.uss` 의 톤을 따른다. 씬은 코드로 구성한다 |
| 완료 조건 | 방장 1 + 참가 2 로 명단이 세 화면에서 일치한다. WebGL 빌드에서 링크로 열면 코드 칸이 채워진 참가 화면이 나온다 |
| 함정 | `VisualElement` 는 도메인 리로드에서 살아남지 않는다. `GameHudController` 의 `TreeIsLive` 패턴을 쓴다 — bool 플래그는 null 트리를 "빌드됨"으로 오인한다. 로비 `PanelSettings` 는 게임 HUD 와 별도 에셋으로, `sortingOrder` 겹치지 않게. WebGL 클립보드는 **사용자 제스처 안에서만** 허용되고 권한이 없으면 조용히 실패한다 — 코드와 링크를 선택 가능한 텍스트로도 남기고, 복사 성공/실패를 화면에 표시한다 |

### C07 — 세션 진단

| 항목 | 내용 |
|---|---|
| 목적 | "안 되는데요"를 수치로 갈라낸다 |
| 선행 | C01 |
| 변경 대상 | 신규 `SessionDiagnostics.cs`, `NetworkTestUi.cs` |
| 내용 | 스냅샷 수신 간격(평균/최대), 마지막 수신 이후 경과, `InputLag`, 프리플라이트 왕복, 맵 해시 상태, 마지막 실패 사유, 룸 단계 |
| 검증 | 서버 조건 주입기(120ms/±30ms/2%)를 켜고 값이 그에 맞게 움직이는지 |

---

## M5 — 시작 신호를 매치에 연결

### C08 — 서버 룸 상태 → `MatchManager`

| 항목 | 내용 |
|---|---|
| 목적 | 방장의 시작 한 번으로 모든 클라이언트가 **같은 배치·같은 역할**로 매치에 들어간다 |
| 선행 | C06, S06 |
| 변경 대상 | 신규 `Assets/Scripts/Net/Session/MatchSync.cs`, `Assets/Scripts/Game/MatchBootstrap.cs`, `MatchManager.cs`(주입 지점만) |
| 내용 | `RoomState.phase == Playing` 을 받으면 그 룸의 맵에 대응하는 씬을 로드하고, `placementSeed` 를 `config.placementSeed` 에 주입한 뒤 `BeginMatch(seekerAgent)` 를 호출한다. Seeker 는 `seekerPlayerId` 로 찾는다. 오프라인 경로(`MatchBootstrap.Start` 의 로컬 시작)는 세션이 없을 때만 동작하도록 가드한다 |
| 완료 조건 | 3클라이언트에서 문·열쇠·장치 위치와 Seeker 신원이 일치한다 |
| 검증 | 세 화면에서 문 좌표를 로그로 찍어 비교. `placementSeed` 를 서버가 보낸 값으로 강제했는지 확인 |
| 함정 | **이 태스크의 존재 이유가 `BeginMatch:155` 다.** `placementSeed` 가 0 이면 `Environment.TickCount` 로 떨어지고, 그러면 클라이언트마다 문이 다른 곳에 생긴다. `GameConfig.asset` 은 자기 몫의 기본값을 따로 들고 있으므로(`NVproject/CLAUDE.md`) `.cs` 의 기본값만 고치면 아무 일도 일어나지 않는다. 씬 로드는 맵 해시로 검증한다 — 룸의 맵과 다른 씬을 열면 `MapHashMismatch` 로 잡힌다 |

### C09 — 원격 플레이어에 역할 붙이기

| 항목 | 내용 |
|---|---|
| 목적 | 명단의 역할이 화면의 몸에 반영된다 |
| 선행 | C08 |
| 변경 대상 | `Assets/Scripts/Net/RemotePlayerPuppet.cs` |
| 내용 | 퍼펫에 `PlayerAgent` 와 `FootstepAudio` 를 붙이고 역할을 적용한다. `MatchLayers.ApplyRoleVisibility` 가 이미 있는 컬링 층을 쓴다 |
| 완료 조건 | Runner 화면에서 Seeker 가 보이고, Seeker 화면에 문이 보이지 않는다 |
| 함정 | `AddComponent` 는 `Awake` 를 즉시 실행한다. `FootstepAudio.isLocalListener` 는 그 뒤에 설정되므로 `Start` 에서 적용되는 현재 구조를 지킨다(`NVproject/CLAUDE.md` 에 기록된 함정). 원격 몸은 씬 루트에 만든다 — 로컬 플레이어 트랜스폼을 상속하면 안 된다 |

---

## M6 — 실패 재현 표 (수락 기준)

각 행이 **서로 다른 화면**을 내야 한다. 하나라도 뭉치면 M6 은 미완이다.

| 재현 | 기대 분류 | 다음 행동 |
|---|---|---|
| 서버 미기동 상태로 방 만들기 | `ServerUnreachable` | 재시도 |
| 주소를 `localhost:5299` 로 오타 | `ServerUnreachable` (포트 표시) | 주소 수정 |
| `ProtocolInfo.Version` 을 서버만 올린 뒤 접속 | `VersionMismatch` | 클라이언트 재빌드 |
| 코드에 `ILO0` 입력 | `InvalidCode` (입력 단계에서 즉시) | 코드 다시 확인 |
| 존재하지 않는 6자 코드 | `UnknownCode` | 코드 확인 / 새 방 |
| `?code=` 를 손으로 고친 링크로 접속 | `InvalidCode` 또는 `UnknownCode` | 코드 입력 화면으로 (자동 접속 없음) |
| 만료된 방 코드 | `UnknownCode` (만료 문구) | 새 방 |
| 8명 찬 방에 9번째 | `RoomFull` | 다른 방 |
| 이미 시작된 방에 참가 | `RoomInProgress` | 다음 판 대기 |
| 17번째 방 생성 | `RoomLimit` | 잠시 뒤 재시도 |
| 방장이 아닌데 시작 시도 | `NotHost` | 방장에게 요청 |
| 1명인 방에서 시작 | `TooFewPlayers` | 인원 대기 |
| 플레이 중 서버 강제 종료 | `ConnectionLost` | 자동 재시도 → 실패 시 로비 |
| 룸 맵과 다른 씬 로드 | `MapHashMismatch` | Export 안내 |

검증은 에디터 + **Tools ▸ NV Network ▸ Build and Launch 2 Clients**, 8명 채우기는 스탠드얼론 빌드 여러 개. 클라이언트에는 CLI 테스트 수단이 없다 — 서버 로그와 화면이 증거다.

### 지금까지 확인된 것

**서버 쪽 (`curl` + 임시 WebSocket 콘솔 클라이언트로 실행 확인)**

| 항목 | 결과 |
|---|---|
| `POST /rooms` | `201` + 코드·방장 토큰·맵 해시 |
| `POST /rooms` 알 수 없는 맵 | `400 unknownMap` |
| `GET /rooms/{code}?v=2` | `200` + 상태. 대문자·하이픈 입력도 정규화되어 같은 방 |
| `?v=999` | `426 versionMismatch` |
| 형식 오류(기호·33자) | `400 invalidCode` |
| 없는 코드 | `404 unknownCode` |
| `GET /rooms` 목록 | `200` (개발 플래그 켠 상태) |
| CORS 프리플라이트 | `204` + `Access-Control-Allow-Origin` |
| `/ws` 거부 | 버전 `426`, 형식 `400`, 없는 코드 `404` |
| 8명 채운 뒤 9번째 접속 | `503` |
| Welcome | `0x83` 13B |
| 룸 상태 전문 | `0x82`. 5명·2자 이름에서 35B = 15 + 5×(2+2) |
| 정적 룸에서 `Control(StartMatch)` | 단계 `Waiting → Playing`, Seeker 배정, **씨드 0 아님**(`-1162551485`), 방장 `255`(정적 룸이라 없음) |
| 진행 중 룸에 새 접속 | `409` (자리를 비워 두고 확인 — 정원이 아니라 단계 때문이다) |
| 전원 퇴장 후 재접속 | 성공. 단계가 대기로 돌아갔다 |

**클라이언트 쪽 (에디터 필요, 미확인)**

`InvalidCode` 입력 힌트, `UnknownCode` 화면, `MapHashMismatch`, `ConnectionLost` 자동 재시도, `?code=` 를 손으로 고친 링크, 그리고 3클라이언트 로비→시작 왕복. 배치 씨드가 세 화면에서 같은 문 위치를 만드는지도 여기서만 확인된다.

---

## 범위 밖

| 항목 | 이유 |
|---|---|
| 매치 규칙 서버 이관 | 결정 1. C08·C09 가 접속면을 확정해 두므로 이관 시 세션 계층은 그대로 쓴다 |
| 세션 재개(같은 슬롯·상태로 재접속) | `Room.Leave` 의 즉시 슬롯 반납 설계를 바꾸는 일. 재접속 토큰 + grace 기간이 별도로 필요하다 |
| 계정·인증 | `Identity` 모듈 미구현. 이름도 세션 수명만큼만 산다 |
| 매치메이킹·공개 방 목록 | `Matchmaking` 모듈 미구현. 초대 코드가 그 역할을 한다 |
| 관전·중간 합류 | 비대칭 매치 중간 합류는 룰셋을 깬다 |
| 강퇴·차단 | 방장 권한 확장. 시작 권한이 세션 신원으로 판정되므로 같은 자리에 붙는다 |
| 예측·리컨실리에이션 | 별도 계획 |

---

## 파일 배치

```
NVserver/
├── Shared/Contracts/Enums/MessageOpcode.cs        Control 0x02 추가        S06
├── Shared/Contracts/Messages/
│   ├── ProtocolInfo.cs                            Version 1 → 2            S06
│   ├── RoomStateMessage.cs                        신규                     S06
│   └── ControlMessage.cs                          신규                     S06
├── Shared/Serialization/MessageCodec.cs           두 포맷 추가             S06
└── Modules/Realtime/
    ├── Contracts/RoomMaps.cs                      맵 id 키잉               S01
    ├── Contracts/RoomSummary.cs                   단계·방장 추가           S03
    ├── Simulation/RoomPhase.cs                    신규                     S03
    ├── Simulation/Room.cs                          단계·방장·씨드           S03
    ├── Simulation/RoomCommand.cs                   Start·ClaimHost          S03
    ├── Simulation/RoomRegistry.cs                  생성·만료·TryGet         S04
    ├── Simulation/InviteCode.cs                    신규                     S04
    ├── Transport/RealtimeEndpoints.cs              POST/GET·프리플라이트    S05
    └── Transport/GameSession.cs                    Control 수신·이름        S06 S07

NVproject/Assets/Scripts/Net/
├── NetworkClient.cs              (그대로 — 와이어)
├── SnapshotBuffer.cs             (그대로 — 보간)
├── ClientTransportFactory.cs     token·name 쿼리 추가                     C03
├── EditorWebSocketTransport.cs   Close 경로                                C05
├── NetworkBootstrap.cs           씬 어댑터로 축소                          C02
├── RemotePlayerPuppet.cs         역할·발소리                               C09
└── Session/                      신규
    ├── NetSession.cs  SessionState.cs                                      C01
    ├── RoomApi.cs  RoomInfo.cs  InviteCodeText.cs  InviteLink.cs           C03
    ├── SessionFailure.cs                                                   C04
    ├── SessionDiagnostics.cs                                               C07
    ├── LobbyController.cs                                                  C06
    └── MatchSync.cs                                                        C08

NVproject/Assets/Scenes/Lobby.unity                                         C06
NVproject/Assets/Resources/UI/Lobby.uxml, lobby.uss                         C06
NVproject/Assets/Editor/LobbySetup.cs                                       C06
```

---

## 실행 순서

```
C01 ─┬─ C02
     ├─ C07
     └───────────────┐
S01 ─── S02          │
S03 ─┬─ S04 ─── S05 ─┴─ C03 ─── C04 ─┬─ C05 ─┐
     └─ S06 ─── S07 ───────────────────┴───────┴─ C06 ─── C08 ─── C09 ─── M6
```

`S03`/`S06`(서버·프로토콜)과 `C01`(세션 코어)은 독립이므로 병행한다. `C06`(로비 UI)은 `C03`·`C04`·`C05`·`S06` 이 모두 끝난 뒤 시작한다 — UI 를 먼저 만들면 상태 분류가 UI 에 끌려간다.

서버 태스크는 각각 `dotnet build`(경고 0) + `dotnet test` 로, 클라이언트 태스크는 에디터 컴파일 + 재현 표의 해당 행으로 확인한다. `Shared` 를 건드린 태스크는 Unity 에디터 컴파일까지 확인한다. 30분 이상 걸린 문제는 `NVserver/docs/conventions.md` 에 증상 → 원인 → 대책으로 남긴다.

## 실행 중 바뀐 것

계획과 다르게 한 결정들. 이유를 남긴다.

| 계획 | 실제 | 이유 |
|---|---|---|
| 빈 룸 만료를 단계별로 60초/30초 | 하나로 합쳐 60초 | 전원이 나가면 룸이 스스로 대기 단계로 돌아가므로 "비어 있으면서 진행 중인 룸" 은 존재하지 않는다. 두 번째 기준은 도달할 수 없는 분기였다 |
| 시작 없이 10분이면 만료 | 없앴다 | 사람이 있는 로비를 시간만으로 닫는 규칙이었다. 아무도 들어오지 않은 방은 60초 기준이 이미 잡는다 |
| `ControlKind.Leave` 로 자발적 퇴장 | 프로토콜에서 뺐다(값 2는 비움) | WebSocket 정상 종료 프레임으로 이미 구분되고, 둘을 다 보내면 같은 소켓에 두 송신이 겹친다 — WebSocket 은 동시 송신을 허용하지 않는다 |
| S01·S02·S04 를 따로 커밋 | 한 커밋 | 맵 키잉·정적 룸·명시적 생성·만료가 모두 `RoomRegistry` 를 함께 바꾼다. 쪼개면 중간 커밋이 컴파일되지 않는다 |
| S07(표시 이름)을 따로 | S05 와 함께 | 이름 배선이 접속 경로 한 곳이라 엔드포인트 작업과 같은 자리였다 |
| `GET /rooms/{code}` 는 상태코드만 | 409·503 에도 본문을 실었다 | 상태코드가 "들어갈 수 있는가", 본문이 "지금 어떤 상태인가" 를 답한다. 본문이 없으면 로비가 "8/8 진행 중" 을 표시할 수 없다 |
| 서버가 코드 형식을 검사 | 룸 id 규칙만 검사 | 정적 룸 id 는 코드 형식을 만족하지 않는다(`test` 는 4자). 코드 형식을 요구하면 그 룸을 조회할 수 없고, 두 규칙을 합치면 정적 룸 id 에 쓸 수 있는 글자가 조용히 준다. 코드 오타는 클라이언트가 입력 칸에서 잡는다 |
| 서버 권위를 Welcome 시점에 | 룸이 진행 단계가 될 때 | 대기 단계에서는 서버가 시뮬레이션하지 않아 스냅샷이 오지 않는다. Welcome 시점에 넘기면 로비에서 캐릭터가 입력에도 반응하지 않고 서버 위치로도 옮겨지지 않는다 |
| 배치 씨드를 `config.placementSeed` 에 주입 | `MatchManager.PlacementSeedOverride` | 설정은 `ScriptableObject` 다. 런타임 변경이 에디터에서 그대로 저장되어 다음 오프라인 세션이 지난 매치의 씨드를 재사용한다 |
| — (계획에 없던 것) | 명단 전원의 몸을 기다린 뒤 시작 | 원격 몸은 첫 스냅샷에 만들어지고 역할은 시작 시점의 명단에만 배정된다. 먼저 시작하면 늦게 온 플레이어가 역할 없이 남는다 |
| — (계획에 없던 것) | `MatchManager.ResolvesOutcome` | 전원이 각자 결과를 판정하면 서로 다른 순간에 끝내고 결과도 갈린다. 방장 하나가 판정하고 나머지는 받는다 |

## 문서 갱신 (M2·M3 완료 시점에 함께)

| 문서 | 무효화되는 내용 |
|---|---|
| `NVserver/docs/readme.md` | 씬↔룸 짝 표, 접속 절차(코드가 생김) |
| `NVserver/docs/architecture.md` | 와이어 프로토콜 절(opcode 2개 추가, 버전 2) |
| `NVserver/docs/conventions.md` | 새 규칙 — 상태 전문 반복 송신, 초대 코드 난수 출처, 방장 승계 위치 |
| `NVproject/CLAUDE.md` | "Scene and room are a pair", 매치 레이어의 로컬 시작 서술 |
