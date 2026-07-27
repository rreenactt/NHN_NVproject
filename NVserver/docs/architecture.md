# 아키텍처

개요는 `readme.md`, 폴더와 네이밍은 `structure.md`, 구현하며 확정된 규약은 `conventions.md`.

---

## 전체 구성

```
브라우저 (Unity WebGL)
        │  HTTPS  페이지 · 빌드 로드
        │  WSS    게임 트래픽 (30Hz)
        ▼
   단일 .NET 10 프로세스 (Kestrel)
        ├── 정적 파일 서빙   Api/wwwroot
        ├── HTTP API         모듈이 소유
        └── 30Hz 틱 루프     Realtime 모듈
        ▼
   SQLite (모듈별 파일)
```

게이트웨이·매치메이킹 서버·오케스트레이터를 두지 않는다.

---

## 원칙

**모듈러 모놀리스.** 단일 프로세스로 배포하되 개별 모듈을 서비스로 추출할 수 있는 상태를 유지한다.

**컴파일러가 강제하는 것만 구조로 만든다.** 실제로 작동하는 규칙은 둘뿐이다.

1. 모듈 간 참조 금지
2. `Contracts/` 밖은 `internal` — 모듈이 어셈블리 하나이므로 `internal`이 곧 경계

**헥사고날 계층을 만들지 않는다.** `Adapters` / `Application` / `Domain` 폴더 분리를 하지 않는다.

**구현체가 하나뿐인 인터페이스를 만들지 않는다.** 리포지토리 포트, 서비스 포트를 두지 않고 서비스가 `DbContext`를 직접 받는다. 허용되는 인터페이스는 넷뿐이다.

| 인터페이스 | 두 번째 구현 |
|---|---|
| 모듈 `Contracts/`의 공개 계약 | 없음. 모듈 경계 정의가 목적 |
| `IEventBus` | 추출 시 메시지 브로커 클라이언트 |
| `IServerTransport` / `IClientTransport` | 클라·서버가 각각 구현 |
| `IClock` | 테스트 결정성 |

새 인터페이스를 만들려면 두 번째 구현이 무엇인지 답할 수 있어야 한다.

---

## 도입하지 않는 것

| 라이브러리 | 사유 |
|---|---|
| Photon, Mirror, Netcode for GameObjects, FishNet | 프로젝트 방침 |
| SignalR | 허브 프로토콜 오버헤드, Unity 지원 빈약 |
| Socket.IO | .NET 서버 구현체 유지보수 중단 |
| BepuPhysics, Jitter, Bullet | 클라이언트 예측과 결과 불일치 |
| MessagePack, protobuf (스냅샷용) | 수기 비트패커 사용. 메타 DTO에는 무방 |
| Redis | 단일 프로세스라 공유 상태 없음 |
| MediatR (모듈 간 통신용) | `IEventBus` 사용 |

`Shared`에는 어떤 NuGet 패키지도 추가하지 않는다.

---

## 기본값 대체표

일반적인 .NET · Unity 코드에서 흔한 선택이지만 이 프로젝트에서는 틀린 것들이다.

| 쓰지 않는다 | 대신 | 근거 |
|---|---|---|
| `UnityEngine.Vector3` (Shared) | `System.Numerics.Vector3` | 서버에 해당 어셈블리가 없음 |
| `new Random()`, `UnityEngine.Random` (Shared) | 틱·엔티티에서 유도한 해시 | 재적용 시 결과가 달라짐 |
| `DateTime.Now` / `UtcNow` | 틱 번호 (Shared) · `IClock` (모듈) | 동일 |
| `Time.deltaTime` | 고정 틱 델타 상수 | 동일 |
| `Physics.Raycast`, `Rigidbody`, `CharacterController` | `Shared/Collision` | 클라·서버 결과 불일치 |
| `MonoBehaviour` 상속 (Shared) | 순수 클래스 · 구조체 | 서버에서 컴파일 불가 |
| `[JsonPropertyName]` (Shared) | 순수 POCO + 직렬화 설정 | `System.Text.Json`이 NuGet |
| `Domain.csproj` 생성 | 모듈 프로젝트가 도메인의 소유자 | 모듈 경계 소실 |
| `Domain/`·`Application/`·`Adapters/` 폴더 | 모듈 루트에 평평하게 | 계층 폴더 미사용 |
| 구현체 하나짜리 인터페이스 | 구체 클래스 직접 주입 | 간접 참조만 늘어남 |
| `Api/Controllers/` | 모듈의 `{모듈}Endpoints` | 엔드포인트는 모듈 소유 |
| 모듈 간 서비스 직접 주입 | 통합 이벤트 | 모듈 간 참조 금지 |
| 크로스 모듈 JOIN · 외래 키 | 이벤트로 복제본 유지 | DB 분리 불가 |
| `Infrastructure`에 리포지토리 | 모듈이 `DbContext` 직접 사용 | 허브가 되어 추출 불가 |
| 틱 루프 안 `await` | 채널 발행 | 모든 룸이 함께 멈춤 |
| HTTP 스레드에서 룸 직접 변경 | `IRoomCommand` 큐 | 틱 루프가 순회 중인 컬렉션 |
| `Task.Delay` 루프 | `PeriodicTimer` | 드리프트 누적 |
| `EnsureCreated()` | 마이그레이션 | 두 번째 컨텍스트의 테이블 누락 |
| 스냅샷 델타 압축 | 매 틱 풀 스냅샷 | TCP head-of-line blocking |
| 클라이언트가 위치 전송 | 입력만 전송 | 클라이언트 권위가 됨 |

---

## 프로젝트

| 프로젝트 | TFM | 역할 |
|---|---|---|
| `Shared` | `netstandard2.1;net10.0` | 공유 커널. 시뮬레이션, 와이어 포맷 |
| `Infrastructure` | `net10.0` | 기술 기반. 모듈에 종속되지 않는 것만 |
| `Modules/Realtime` | `net10.0` | 진행 중인 매치 |
| `Modules/Identity` | `net10.0` | 플레이어 식별, 토큰 |
| `Modules/Matchmaking` | `net10.0` | 룸 목록, 배정 |
| `Modules/Leaderboard` | `net10.0` | 점수 집계 |
| `Api` | `net10.0` (Web) | 호스트 + 컴포지션 루트 |
| `SourceGenerator` | `netstandard2.0` | 서버 전용 코드 생성 (선택) |
| `tests/Architecture.Tests` | `net10.0` | 모듈 경계 검증 |
| `tests/Modules.Tests` | `net10.0` | 유닛 테스트 |

`Modules/`와 `tests/`는 프로젝트가 아니라 그룹핑 폴더다. 전역 `Domain` 프로젝트를 두지 않는다.

---

## 참조 규칙

```
Shared           → 없음 (NuGet 포함)
Infrastructure   → Shared
Modules/*        → Shared, Infrastructure
Api              → 전부
```

| 금지 | 사유 |
|---|---|
| `Modules/X → Modules/Y` | 추출 가능성 소실 |
| `Infrastructure → Modules/*` | 허브가 되어 추출이 막힘 |
| `Shared → 무엇이든` | Unity 공동 컴파일 제약 |

`tests/Architecture.Tests`가 검증한다.

---

## `Shared`의 경계

Unity(IL2CPP)와 .NET이 **같은 `.cs` 파일**을 각자 컴파일하는 유일한 어셈블리다. 클라이언트 예측이 성립하려면 이동 계산이 양쪽에서 완전히 같아야 한다.

무엇을 어디에 둘지는 질문 하나로 갈린다. **클라이언트가 이 계산을 직접 실행해야 하는가?**

| | `Shared` | 모듈 |
|---|---|---|
| 역할 | 계산 (mechanics) | 판정 (policy) |
| 타입 | `struct`, 값 타입 | `class`, 엔티티 |
| 함수 | `static`, 순수 함수 | 인스턴스 메서드, 상태 변경 |
| 접근성 | `public` | `internal` |
| 실행 주체 | 클라이언트 + 서버 | 서버만 |
| 예시 | 중력, 스윕, 탄퍼짐 패턴, 레이 교차 계산 | 명중 인정, 데미지, 스폰 선정, 입력 검증 |

사격 한 번이 두 곳에 걸친다. "레이가 캡슐과 만나는가"는 `Shared`, "그래서 맞은 것으로 인정하는가"는 모듈이다.

**`Shared`에 들어가는 것은 둘뿐이다.** 클라이언트도 컴파일해야 하는 결정적 시뮬레이션, 그리고 와이어 포맷(게임 메시지, HTTP DTO). 특정 모듈만 쓰는 타입과 통합 이벤트는 넣지 않는다.

**WebGL 빌드는 디컴파일된다.** `Shared`에 들어간 값은 클라이언트가 안다고 가정한다. 값 공유와 검증 위임은 다르다 — 쿨다운 시간값은 `Shared`에 있어야 UI 예측이 되지만, 위반 판정은 모듈에서 다시 한다.

컴파일 제약은 `conventions.md`.

---

## `Infrastructure`의 범위

**모듈에 종속되지 않는 기술 기반만** 담는다.

| 담당 | 담당 아님 |
|---|---|
| `DbContext` 기반 클래스, 프로바이더 등록 헬퍼 | 모듈별 `DbContext`, 엔티티 매핑 |
| 마이그레이션 실행기 | 모듈별 마이그레이션 파일 |
| `IEventBus`, 통합 이벤트 계약, 인프로세스 구현 | 이벤트 핸들러 |
| `IClock`, JSON 기본 옵션, 로깅 설정 | 서비스, 리포지토리, 게임 규칙 |

**폴더를 외부 기술 단위로 나눈다.** 관심사 폴더 루트에 인터페이스를, 기술 하위 폴더에 구현을 둔다. 교체 지점이 폴더 경계와 일치하게 되어, Postgres 전환은 `Database/Postgres/` 추가로 끝난다. 폴더 구성은 `structure.md`.

---

## 모듈

### Realtime

진행 중인 매치의 시뮬레이션과 중계.

**DB를 갖지 않는다.** 틱 루프가 I/O를 하지 않으므로 `DbContext`도 없다. 매치 결과는 이벤트로 발행한다.

**외부 공개 API는 큐 기반이어야 한다.** 룸 상태는 틱 루프가 소유하므로 HTTP 스레드가 직접 변경하면 안 된다. 커맨드는 큐에 넣어 틱 경계에서 적용하고(`IRoomCommand`), 조회는 불변 스냅샷을 반환한다(`IRoomQuery`). 살아 있는 룸 객체를 모듈 밖으로 반환하지 않는다.

### Identity / Matchmaking / Leaderboard

익명 토큰 발급, 룸 배정과 참가 티켓, 점수 집계. 요청/응답 워크로드이며 각자 `DbContext`를 갖는다.

### Api

호스트 겸 컴포지션 루트. **컨트롤러가 없다.** 엔드포인트는 각 모듈이 `MapXxx()`로 등록하고, `Api`는 `Program.cs`에서 `AddXxx()` / `MapXxx()`를 호출하기만 한다.

---

## 모듈 간 통신

동기 호출을 만들지 않는다. 발행·구독 모듈 모두 `Infrastructure`를 참조하므로, 이벤트 계약을 `Infrastructure/Messaging/IntegrationEvents/`에 두면 서로를 참조하지 않아도 된다.

인메모리 채널이므로 워커 처리 전에 프로세스가 죽으면 유실된다. 아웃박스 패턴은 도입하지 않는다.

**예외**: `Matchmaking → Realtime`은 참가 응답을 즉시 돌려줘야 하므로 `IRoomCommand` 동기 호출을 허용한다. 추출 시 이 부분만 바꾼다.

---

## 실행 모델

```
Kestrel 스레드풀                          GameLoopService
┌────────────────┐                      ┌────────────────┐
│  ReceivePump   │ ──▶ InboundQueue ──▶ │  틱 루프 30Hz   │
│  SendPump      │ ◀── OutboundChannel ◀│  룸 순회        │
│  HTTP 요청     │ ──▶ CommandQueue  ──▶ │                │
└────────────────┘                      └────────────────┘
                                                 │ EventChannel
                                                 ▼
                                          영속화 워커
```

| 큐 | 타입 | 정책 |
|---|---|---|
| `InboundQueue` | `ConcurrentQueue` | 틱 시작 시 전부 드레인 |
| `OutboundChannel` | `BoundedChannel(32, DropOldest)` | 밀리면 오래된 스냅샷 폐기 |
| `CommandQueue` | `ConcurrentQueue` | 틱 시작 시 전부 드레인 |
| `EventChannel` | `UnboundedChannel` | 유실 불가 |

**틱 루프는 이 지점들 외에 어떤 소켓이나 DB도 건드리지 않는다.**

`Realtime`은 `Api`가 `AddHostedService`로 기동한다.

---

## 데이터베이스

| 데이터 | 위치 |
|---|---|
| 위치·속도·체력, 세션, 스냅샷 히스토리 | 메모리 |
| 맵 콜리전 | 파일 (JSON) |
| 밸런스 수치 | 코드 상수 |
| 플레이어, 매치 결과, 점수, 참가 티켓 | DB |

### 소유 규칙

- 모듈마다 자기 `DbContext`, 자기 DB 파일
- 테이블 접두어로 소유 모듈을 식별한다 (`id_`, `mm_`, `lb_`)
- 크로스 모듈 외래 키·JOIN 금지. 다른 모듈 데이터는 ID로만 참조하고, 필요한 필드는 이벤트로 복제한다
- `Realtime`은 테이블을 갖지 않는다

### EF Core

- 리포지토리를 만들지 않는다. 모듈 서비스가 `DbContext`를 직접 받는다
- 엔티티에 ORM 어트리뷰트를 붙이지 않는다. `IEntityTypeConfiguration`으로 매핑
- `DbContext`는 `internal`
- 쓰기는 이벤트 워커에서 배치 단위로. 배치마다 `IServiceScopeFactory`로 스코프 생성
- 기동 시 모델을 미리 데운다 (컨텍스트당 100~300ms)
- `EnsureCreated()` 금지. 마이그레이션 사용
- SQLite는 WAL 모드 + `busy_timeout`

---

## 네트워크 프로토콜

### WebSocket

바이너리 프레임. 첫 바이트가 opcode, 리틀엔디언.

| opcode | 방향 | 메시지 |
|---|---|---|
| `0x01` | C → S | `Input` — 최근 3틱치 중복 전송 |
| `0x81` | S → C | `Snapshot` — 풀 스냅샷, 매 틱 |
| `0x82` | S → C | `Event` — 킬 피드, 매치 상태 |
| `0x83` | S → C | `Welcome` — 자기 ID, 서버 틱, 맵 해시 |

| 구조체 | 크기 | 필드 |
|---|---|---|
| `InputFrame` | 7B | buttons(u8), moveX/Z(i8), yaw(u16), pitch(i16) |
| `EntityState` | 13B | id(u8), x/y/z(i16), yaw(u16), flags(u8), hp(u8) |

8명 기준 스냅샷 114B, 30Hz에서 3.6KB/s.

프로토콜 버전이 다르면 접속 시 즉시 끊는다. 클라이언트와 서버가 다른 시점에 빌드되므로 이 핸드셰이크가 유일한 방어선이다.

### HTTP

경로 접두어가 모듈과 1:1이다. 추출 시 리버스 프록시가 접두어로 라우팅한다.

| 경로 | 모듈 |
|---|---|
| `/api/auth/*` | Identity |
| `/api/match/*` | Matchmaking |
| `/api/leaderboard/*` | Leaderboard |
| `/ws` | Realtime |
| `/health` | Api |

---

## 보안

서버 권위가 1차 방어선이다. 클라이언트는 입력만 보내고 위치를 보내지 않는다.

| 항목 | 방식 |
|---|---|
| 인증 | 익명 토큰 |
| 참가 검증 | 일회성 티켓, 만료 있음. WS 업그레이드 시 소비 |
| 입력 검증 | `Realtime`에서 속도 클램프, 발사율 위반 독립 검사 |
| 프로토콜 | 버전 불일치 시 연결 거부 |
| 전송 | `wss://` 필수 |
| 비밀 | 환경 변수. 설정 파일에 커밋 금지 |

---

## 테스트

| 테스트 | 대상 | 우선순위 |
|---|---|---|
| `Architecture.Tests` | 모듈 간 참조, `Infrastructure`의 모듈 미참조, `public` 표면 | **구현보다 먼저** |
| 시뮬레이션 유닛 | `Shared/Simulation`, `Shared/Collision` | 높음 |
| 직렬화 라운드트립 | `Shared/Serialization` | 높음 |
| 모듈 서비스 유닛 | 각 모듈 | 중간 |

커버리지 수치 목표는 두지 않는다. `Shared/Simulation`만 예외 없이 요구한다. 이동 로직이 깨지면 증상이 "가끔 캐릭터가 떨림"으로만 나타나 원인 추적이 어렵다.

네트워크 조건 주입기(지연·지터·손실)를 초기에 넣는다.

---

## MSA 추출 경로

| 단계 | 작업 |
|---|---|
| 1 | 모듈 프로젝트를 새 호스트로 이동 |
| 2 | 새 진입점에서 `AddXxx()`, `MapXxx()` 그대로 호출 |
| 3 | 해당 DB 파일 이동 (크로스 FK가 없으므로 그대로) |
| 4 | `IEventBus` 구현을 브로커 클라이언트로 교체 |
| 5 | `Api`에서 등록 제거, 리버스 프록시가 경로 접두어로 라우팅 |

모듈 코드는 바뀌지 않는다. `Realtime`은 스테이트풀이라 세션 라우팅이 추가로 필요하며 범위 밖이다.
