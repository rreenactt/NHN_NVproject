# 프로젝트 구조

시스템 설계는 `architecture.md`, 구현하며 확정된 규약은 `conventions.md`.

---

## 폴더 구조

```
NVserver/
├── Shared/                          netstandard2.1 · 프로젝트 · Unity 로컬 패키지 겸용
│   ├── Contracts/
│   │   ├── Http/                    HTTP 요청/응답 DTO
│   │   ├── Messages/                게임 프로토콜 메시지
│   │   └── Enums/
│   ├── Serialization/               비트 리더/라이터, 양자화
│   ├── Simulation/                  상수, 상태 구조체, 이동, 전투, 결정적 난수
│   ├── Collision/                   AABB, 캡슐 스윕, 레이캐스트
│   ├── Transport/                   전송 인터페이스
│   ├── Shared.csproj
│   ├── Shared.asmdef                Unity 컴파일 단위
│   └── package.json                 Unity 패키지 매니페스트
│
├── Infrastructure/                  net10.0 · 프로젝트
│   ├── Database/                    DbContext 기반 클래스, 커넥션 설정
│   │   └── Sqlite/                  프로바이더 등록, PRAGMA, 마이그레이션 실행기
│   ├── Messaging/                   이벤트 버스 인터페이스
│   │   ├── IntegrationEvents/       모듈 간 이벤트 계약
│   │   └── InProcess/               인프로세스 구현, 디스패처
│   ├── Logging/
│   │   └── Serilog/
│   ├── Json/                        직렬화 기본 옵션
│   ├── FileSystem/                  맵 JSON 로더
│   ├── Time/                        시스템 시계
│   └── Infrastructure.csproj
│
├── Modules/                         ← 프로젝트 아님. 그룹핑 폴더
│   ├── Realtime/                    net10.0 · 프로젝트
│   │   ├── Contracts/               룸 조회·커맨드 인터페이스, 발행 이벤트
│   │   ├── Simulation/              룸, 월드 상태, 판정 규칙, 틱 루프
│   │   ├── Transport/               세션, 송수신 펌프, WebSocket
│   │   ├── RealtimeConstants.cs     판정·용량 상수의 유일한 출처
│   │   └── Realtime.csproj
│   │
│   ├── Identity/                    net10.0 · 프로젝트
│   │   ├── Contracts/
│   │   └── Identity.csproj
│   │
│   ├── Matchmaking/                 net10.0 · 프로젝트
│   │   ├── Contracts/
│   │   └── Matchmaking.csproj
│   │
│   └── Leaderboard/                 net10.0 · 프로젝트
│       ├── Contracts/
│       └── Leaderboard.csproj
│
├── Api/                             net10.0 (Web) · 프로젝트 · 진입점
│   ├── Composition/                 모듈 등록, 정적 파일 설정
│   ├── Middlewares/                 예외, 클라이언트 버전 체크
│   ├── Properties/
│   ├── wwwroot/                     WebGL 빌드 (gitignore)
│   ├── Api.csproj
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   └── Dockerfile
│
├── SourceGenerator/                 netstandard2.1 · 프로젝트 (선택)
│
├── tests/                           ← 프로젝트 아님. 그룹핑 폴더
│   ├── Architecture.Tests/          net10.0 · 프로젝트 · 모듈 경계 검증
│   └── Modules.Tests/               net10.0 · 프로젝트 · 유닛 테스트
│
├── MapData/                         Unity에서 export한 맵 콜리전 JSON
├── data/                            SQLite 파일 (gitignore)
├── artifacts/                       obj/bin 출력 (gitignore)
│
├── docs/                            readme · architecture · structure · conventions
│
├── NVserver.slnx
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
├── compose.yaml
├── .dockerignore
├── .editorconfig
├── .gitignore
├── .gitattributes
└── README.md
```

**`.csproj`를 가진 디렉토리가 프로젝트 단위다.** `Modules/`와 `tests/`는 그룹핑 폴더이며 그 자체로는 아무것도 아니다. `Modules.csproj` 같은 것을 만들지 않는다. 묶으면 `internal`이 모듈 경계 역할을 하지 못한다.

---

## 루트 파일

| 파일 | 역할 |
|---|---|
| `NVserver.slnx` | 솔루션. `.sln` 대신 사용 — GUID가 없어 머지 충돌이 적다. Unity 클라이언트는 포함하지 않는다 |
| `Directory.Build.props` | **obj/bin을 `artifacts/`로 리디렉션.** `Shared/obj/`가 생기면 Unity 컴파일이 깨진다 |
| `Directory.Packages.props` | 패키지 버전 중앙 관리 |
| `global.json` | .NET 10 SDK 버전 고정 |
| `.gitattributes` | Unity YAML 개행 고정(`eol=lf`), 바이너리 에셋 LFS |

`Directory.Build.props`의 출력 경로 리디렉션은 선택이 아니다. Unity를 연결하기 전에 반드시 적용한다.

---

## Unity 클라이언트

클라이언트는 이 저장소 안이 아니라 **형제 폴더 `../NVproject`** 다. 계획서의 `Client/` 자리를 그 프로젝트가 대신한다 — 게임(블록 캐릭터, 절차적 애니메이션, Backrooms 레벨)이 이미 거기서 만들어져 있었고, 서버 폴더 안에 두 번째 Unity 프로젝트를 만드는 것은 그것을 복제하는 일이었다.

```
NHN_NVproject/
├── NVserver/                        이 저장소의 서버 부분
│   └── Shared/                      ← Unity 로컬 패키지로 참조된다
└── NVproject/                       Unity 프로젝트
    ├── Packages/manifest.json       "com.nv.shared": "file:../../NVserver/Shared"
    └── Assets/
        ├── Scripts/Net/             전송, 스냅샷 버퍼, 입력 송신, 원격 플레이어
        ├── Plugins/WebGL/           NvWebSocket.jslib
        └── Editor/                  맵 export, 네트워크 셋업 메뉴
```

| 위치 | 내용 |
|---|---|
| `Assets/Scripts/Net/NetworkClient.cs` | 접속, Welcome, 스냅샷 디코드, 30Hz 입력 송신. 와이어만 다룬다 |
| `Assets/Scripts/Net/NetworkBootstrap.cs` | 씬과 네트워크를 잇는 유일한 지점. 씬에 없으면 클라이언트는 혼자 돈다 |
| `Assets/Scripts/Net/SnapshotBuffer.cs` | 100ms 보간 버퍼 |
| `Assets/Scripts/Net/RemotePlayerPuppet.cs` | 원격 플레이어 몸. 로컬과 같은 리그·같은 애니메이터를 쓴다 |
| `Assets/Scripts/Net/BackroomsCollision.cs` | 레벨 생성기 → `MapData`. export 와 런타임 해시가 같은 함수를 쓴다 |
| `Assets/Editor/Map/MapCollisionExporter.cs` | **Tools ▸ NV ▸ Map ▸ Export Map Collision** → `MapData/backrooms.json` |
| `Assets/Editor/BuildManager/*` | **Tools ▸ NV ▸ Build…** / **Environment ▸ …** — 플랫폼·씬·환경을 골라 빌드한다 |

Unity 는 로컬 패키지로 참조한 `Shared/` 안에도 `.meta` 를 만든다. 그 파일들은 커밋한다 — ignore 하면 클론마다 GUID 가 새로 생기고, 나중에 `Shared.asmdef` 를 GUID 로 참조하는 어셈블리가 생겼을 때 참조가 끊어진다. `Shared/` 에 이미 `Shared.asmdef` 와 `package.json` 이 커밋되어 있으므로 같은 취급이다.

**맵의 출처는 클라이언트다.** 레벨이 씨드에서 코드로 생성되므로 서버는 export 된 박스 목록으로만 그 지형을 안다. 씨드·격자·벽 두께를 바꾸면 export 를 다시 돌린다. 잊으면 접속 직후 콘솔에 맵 해시 불일치가 뜬다 — 그 검사가 이 결합의 유일한 방어선이다.

---

## 모듈 내부 구조

**계층 폴더를 만들지 않는다.** `Domain/`, `Application/`, `Adapters/` 분리를 하지 않는다. 파일을 프로젝트 루트에 평평하게 두고 `Contracts/`만 분리한다.

```
Modules/{모듈}/                      ← .NET 프로젝트 하나
├── Contracts/                       public — 다른 모듈과 Api가 보는 전부
├── {모듈}.csproj
├── {모듈}Constants.cs               internal — 판정·용량 상수. 폴더로 나뉜 모듈도 루트에 둔다
├── {모듈}Module.cs                  public — DI 등록 + 엔드포인트 매핑
├── AssemblyInfo.cs                  InternalsVisibleTo("Modules.Tests")
└── (그 외 구현 파일)                internal — 루트에 평평하게
```

공개되는 것은 `Contracts/`와 `{모듈}Module` 둘뿐이다. 나머지 파일명 규칙은 아래 네이밍 표를 따른다.

파일이 10개를 넘어가면 성격에 따라 폴더를 나누되 계층 이름은 쓰지 않는다.

`Realtime`은 파일 수가 많아 처음부터 `Contracts` / `Simulation` / `Transport` 셋으로 나눈다. `DbContext`나 EF Core 참조가 생기려 한다면 설계가 어긋난 신호다.

---

## 코딩 컨벤션

### 네이밍

| 대상 | 규칙 | 예시 |
|---|---|---|
| 프로젝트 / 어셈블리 | PascalCase | `Matchmaking` |
| 네임스페이스 | `NV.{프로젝트}` | `NV.Matchmaking`, `NV.Shared.Simulation` |
| 파일명 | 주 타입명과 동일 | `MatchmakingService.cs` |
| 클래스 / 메서드 / 프로퍼티 | PascalCase | `JoinAsync` |
| 인터페이스 | `I` 접두어 | `IRoomCommand` |
| private 필드 | `_camelCase` | `_lastTick` |
| 지역 변수 / 파라미터 | camelCase | `roomId` |
| 상수 | PascalCase | `SimConstants.TickDelta` |
| 비동기 메서드 | `Async` 접미어 | `SaveChangesAsync` |
| 모듈 등록 클래스 | `{모듈}Module` | `MatchmakingModule` |
| 엔드포인트 클래스 | `{모듈}Endpoints` | `MatchmakingEndpoints` |
| 서비스 클래스 | `{모듈}Service` | `MatchmakingService` |
| DbContext | `{모듈}DbContext` | `LeaderboardDbContext` |
| 테스트 메서드 | 한글 스네이크 허용 | `모듈은_서로를_참조하지_않는다` |

`UPPER_SNAKE_CASE`를 쓰지 않는다. C# 관행이 아니다.

### 데이터베이스

| 대상 | 규칙 | 예시 |
|---|---|---|
| 테이블 | `{모듈접두어}_{복수형}` snake_case | `lb_scores` |
| 컬럼 | snake_case | `display_name` |
| 인덱스 | `ix_{테이블}_{컬럼}` | `ix_lb_scores_kills` |
| DB 파일 | `nv_{모듈}.db` | `nv_leaderboard.db` |
| 마이그레이션 히스토리 | `__ef_migrations_{모듈}` | `__ef_migrations_leaderboard` |

접두어가 다른 테이블에 JOIN하면 규칙 위반이다.

### 와이어 포맷

| 대상 | 규칙 | 예시 |
|---|---|---|
| HTTP 경로 | `/api/{모듈}/{동작}` kebab-case | `/api/match/join` |
| JSON 필드 | camelCase | `roomId` |
| DTO 클래스 | `{동작}Request` / `{동작}Response` | `JoinResponse` |
| 게임 메시지 | 구조체명 그대로 | `SnapshotMessage`, `InputFrame` |

경로 접두어가 모듈과 1:1이어야 추출 시 라우팅이 성립한다.

### 접근 제한자

| 위치 | 기본 접근성 |
|---|---|
| `Shared/`, `Infrastructure/` 전체 | `public` |
| `Modules/{모듈}/Contracts/` | `public` |
| `Modules/{모듈}/{모듈}Module` | `public` |
| `Modules/{모듈}/` 그 외 전부 | **`internal`** |
| `Api/` 전체 | `internal` |

`public`을 붙이기 전에 "다른 모듈이나 `Api`가 봐야 하는가"를 확인한다.

### 파일 배치

새 파일을 만들 때의 판단 절차다. 위에서부터 확인하고 처음 참이 되는 곳에서 멈춘다.

| # | 질문 | 위치 |
|---|---|---|
| 1 | 클라이언트도 동일한 계산을 해야 하는가 | `Shared/Simulation`, `Shared/Collision` |
| 2 | 네트워크로 오가는 데이터 형태인가 | `Shared/Contracts/` |
| 3 | 모듈 간 이벤트 계약인가 | `Infrastructure/Messaging/IntegrationEvents/` |
| 4 | 모듈과 무관한 기술 기반인가 | `Infrastructure/{기술}/` |
| 5 | 어느 바운디드 컨텍스트에 속하는가 | `Modules/{모듈}/` |
| 6 | └ 다른 모듈이나 `Api`가 봐야 하는가 | `Contracts/` (public) |
| 7 | └ 그 외 | 모듈 루트 (internal) |
| 8 | 프로세스 기동, DI 조립, 미들웨어인가 | `Api/` |

**1번이 참이면 다른 조건과 무관하게 `Shared/`다.**

어느 항목에도 해당하지 않으면 새 모듈이 필요하거나 잘못된 위치에 로직을 만들고 있는 것이다. `Api`나 `Shared`에 넣어 해결하지 말고 확인을 요청한다.

### `using` 순서

```csharp
using System;                              // 1. System.*
using Microsoft.EntityFrameworkCore;       // 2. 외부 패키지
using NV.Shared.Contracts.Http;            // 3. Shared, Infrastructure
using NV.Infrastructure.Messaging;
using NV.Matchmaking.Contracts;            // 4. 같은 모듈
```

`Shared`는 `ImplicitUsings`를 끈다. 전역 using이 `obj/`에 생성되는데 Unity는 그 파일을 보지 않는다.

### 테스트 배치

`tests/` 아래에 모은다. `Shared/`에 테스트가 들어가면 Unity가 xUnit 참조를 못 찾아 컴파일이 깨진다.

```
tests/Modules.Tests/
├── Simulation/          MovementTests, CollisionTests, DeterminismTests
├── Serialization/       CodecRoundTripTests
└── {모듈}/

tests/Architecture.Tests/
└── 모듈 경계 검증
```

`internal` 접근은 각 모듈의 `AssemblyInfo.cs`에서 연다.

```csharp
[assembly: InternalsVisibleTo("Modules.Tests")]
```
