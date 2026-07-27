# FPS 수직 슬라이스 기초 구성 실행 계획

브라우저 클라이언트 여러 개가 서버 권위로 움직이고 서로를 보는 상태까지 도달한다.

설계 기준은 `docs/readme.md`, `docs/architecture.md`, `docs/structure.md`, `docs/conventions.md`. 이 계획은 임시 작업 산출물이며 실행 완료 후 `.claude/plans/done/`으로 옮긴다. 실행 중 계획이 바뀌면 이 파일을 고친다.

---

## 문서 기준과의 차이 — 해소 태스크 매핑

| # | 항목 | 현재 | 문서 기준 | 태스크 |
|---|---|---|---|---|
| 1 | `Directory.Build.props` | 없음 | 출력 경로를 `artifacts/`로 리디렉션 | T01 |
| 2 | `Directory.Packages.props` | 없음 | 패키지 버전 중앙 관리 | T01 |
| 3 | `global.json` | 없음 | .NET 10 SDK 고정 | T01 |
| 4 | `Shared` TFM | `netstandard2.1` | `netstandard2.1;net10.0` | T03 |
| 5 | `Shared` 언어 설정 | `Nullable enable`, C# 8 | `LangVersion 9.0`, `Nullable disable`, `ImplicitUsings` 끔 | T03 |
| 6 | `SourceGenerator` TFM | `netstandard2.1` | `netstandard2.0` | T04 |
| 7 | `Realtime` 위치 | 저장소 루트 | `Modules/Realtime` | T05 |
| 8 | `Api` 템플릿 | MVC 컨트롤러 + `AddControllers` | Minimal API + `wwwroot` | T06 |
| 9 | 프로젝트 참조 | 미연결 | `architecture.md` 참조 규칙 | T07 |
| 10 | 테스트 프로젝트 | 없음 | `tests/Architecture.Tests`, `tests/Modules.Tests` | T08, T09 |
| 11 | 문서 폴더명 | `Docs/` | `docs/` | T11 (사람 결정 선행 — 질문 1) |
| 12 | Unity 클라이언트 | 없음 | `Client/` (솔루션 밖) | T13, T14 |

### 조사 중 확인된 추가 차이

| 항목 | 현재 | 처리 | 태스크 |
|---|---|---|---|
| `compose.yaml` | 존재하지 않는 `Application/Dockerfile`을 참조하는 `application` 서비스 | 삭제 | T10 |
| `.gitignore` | `artifacts/`, `wwwroot/`, Unity 산출물 규칙 없음 | 보강 | T12 |
| `.editorconfig` | 없음 | 추가 | T12 |
| `Api` 네임스페이스 | `Api` | `NV.Api` | T06 |
| `Api/Program.cs` | `UseHttpsRedirection`, `UseAuthorization` | 제거 — 인증 범위 밖, 컨테이너 종단은 리버스 프록시 TLS | T06 |
| `Models/ErrorViewModel.cs` | 실제로는 없음 (`Controllers/HomeController.cs`만 존재) | 정리 대상 축소 | T06 |
| 기존 `bin/`·`obj/` | 5개 프로젝트 전부에 존재 | T01 직후 삭제 | T02 |
| Git 저장소 루트 | 부모 `NHN_NVproject/` (NVserver는 하위 폴더) | ignore 규칙 배치 위치 확정 필요 — 질문 8 | T12 |

---

## 마일스톤 개요

| # | 완료 시점에 동작하는 것 | 검증 명령 |
|---|---|---|
| M1 | `dotnet build` 경고 0, 경계 테스트 통과, 출력물이 `artifacts/`에만 생성됨 | `dotnet build NVserver.slnx -warnaserror`, `dotnet test`, `git status`로 `artifacts/` 외 `bin`·`obj` 부재 확인 |
| M2 | 브라우저 devtools에서 WebSocket 접속 후 서버 틱 번호가 30Hz로 도착 | `dotnet run --project Api` 후 devtools Network → WS 프레임 관찰 |
| M3 | 빈 WebGL 빌드가 배포 URL에서 로드되고 `wss://`로 서버에 붙음 | 배포 URL 접속, devtools Network에서 `.br` 응답 헤더와 WS 핸드셰이크 확인 |
| M4 | Unity 클라이언트에서 키 입력 → 서버 판정 → 화면 이동 (예측 없음, 지연 보임) | `dotnet test`, 클라이언트 조작 후 이동 관찰 |
| M5 | 클라이언트 예측 적용. 입력 즉시 반응하고 보정 시 떨리지 않음 | 조건 주입기로 120ms/±30ms/2% 설정 후 조작 관찰 |
| M6 | 두 클라이언트가 서로의 움직임을 부드럽게 봄 | 두 브라우저 탭 동시 접속 후 상호 관찰 |

M3을 M4보다 앞에 두는 이유는 배포 파이프라인 결함(Brotli 헤더, mixed content, WebGL 빌드 크기)을 게임 로직 이후에 발견하는 것이 가장 흔한 실패 패턴이기 때문이다.

---

## M1 — 빌드 기반과 모듈 경계

완료 시점에 `dotnet build`가 경고 0으로 통과하고, 경계 테스트가 참조 규칙 위반을 실패로 잡고, `artifacts/` 밖에 빌드 산출물이 생기지 않는다.

### T01 — 루트 빌드 설정 3종 추가

| 항목 | 내용 |
|---|---|
| 목적 | `Shared/obj/`가 생겨 Unity 컴파일이 깨지는 경로를 원천 차단하고, SDK·패키지 버전을 고정한다 |
| 선행 | 없음 |
| 변경 대상 | 저장소 루트 — `Directory.Build.props`, `Directory.Packages.props`, `global.json` |
| 내용 | SDK 내장 `UseArtifactsOutput`·`ArtifactsPath`로 `artifacts/` 리디렉션(수동 `BaseOutputPath` 지정보다 안전하다), `ManagePackageVersionsCentrally` 활성화, `ImplicitUsings` 끔, `TreatWarningsAsErrors` 활성화. `Directory.Packages.props`는 빈 `ItemGroup`. `global.json`은 설치된 10.0.201에 `rollForward: latestFeature` |
| 완료 조건 | `dotnet build` 후 각 프로젝트 폴더에 `obj/`·`bin/`이 새로 생기지 않고 `artifacts/` 하위에만 생성됨 |
| 검증 | `dotnet build NVserver.slnx` 실행 후 `git status --short`와 프로젝트 폴더 목록 확인 |
| 담당 | 에이전트 |

### T02 — 기존 `bin/`·`obj/` 정리 후 재빌드

| 항목 | 내용 |
|---|---|
| 목적 | 리디렉션 이전에 생성된 산출물이 남아 Unity가 `Shared/obj/`의 `AssemblyInfo.cs`를 중복 정의로 인식하는 것을 막는다 |
| 선행 | T01 |
| 변경 대상 | `Api/`, `Infrastructure/`, `Realtime/`, `Shared/`, `SourceGenerator/`의 `bin`·`obj` |
| 완료 조건 | 저장소 전체에서 `artifacts/` 밖에 `bin`·`obj` 디렉토리가 하나도 없음 |
| 검증 | 삭제 후 `dotnet build NVserver.slnx` 재실행, 이어서 재귀 디렉토리 검색으로 부재 확인 |
| 담당 | 에이전트 |

### T03 — `Shared` 프로젝트 설정 교체

| 항목 | 내용 |
|---|---|
| 목적 | Unity 공동 컴파일 제약을 빌드가 강제하게 만든다 |
| 선행 | T01 |
| 변경 대상 | `Shared/Shared.csproj` |
| 내용 | `TargetFrameworks`를 `netstandard2.1;net10.0`, `LangVersion` 9.0, `Nullable` disable, `ImplicitUsings` 끔, `AllowUnsafeBlocks` 활성화, `RootNamespace` `NV.Shared`. NuGet 참조 없음 |
| 완료 조건 | 두 TFM 모두 빌드되고, C# 10 이상 문법을 쓰면 컴파일이 실패함 |
| 검증 | 임시로 `record` 선언 후 `dotnet build`가 실패하는 것을 확인하고 제거 |
| 담당 | 에이전트 |

### T04 — `SourceGenerator` TFM 수정

| 항목 | 내용 |
|---|---|
| 목적 | Roslyn이 로드할 수 있는 TFM으로 맞춘다. 이번 단계에서 구현은 하지 않는다 |
| 선행 | T01 |
| 변경 대상 | `SourceGenerator/SourceGenerator.csproj` |
| 내용 | TFM을 `netstandard2.0`으로 변경. 코드 파일 없이 빈 상태 유지. `Shared`에 분석기로 연결하지 않는다 |
| 완료 조건 | 빌드 경고 0으로 통과. 다른 프로젝트가 이 프로젝트를 참조하지 않음 |
| 검증 | `dotnet build SourceGenerator/SourceGenerator.csproj` |
| 담당 | 에이전트 |

### T05 — `Realtime`을 `Modules/Realtime`으로 이동

| 항목 | 내용 |
|---|---|
| 목적 | 코드가 쌓이기 전에 배치를 확정한다. 이동 비용이 가장 낮은 시점이다 |
| 선행 | T02 |
| 변경 대상 | `Realtime/` → `Modules/Realtime/`, `NVserver.slnx`, `Api/Dockerfile`, `compose.yaml` |
| 내용 | `git mv`로 이동. `.slnx`의 `Project Path`를 `Modules/Realtime/Realtime.csproj`로 갱신하고 `/Modules/` 솔루션 폴더 아래로 넣는다. `Dockerfile`의 `COPY` 대상 경로를 갱신한다. `RootNamespace`를 `NV.Realtime`으로 설정한다 |
| 완료 조건 | `dotnet build NVserver.slnx` 통과, `docker build` 통과, 루트에 `Realtime/`이 남지 않음 |
| 검증 | `dotnet build NVserver.slnx`, `docker build -f Api/Dockerfile .` |
| 담당 | 에이전트 |

### T06 — `Api` MVC 잔여 제거 및 Minimal API 재작성

| 항목 | 내용 |
|---|---|
| 목적 | 엔드포인트 소유권을 모듈로 옮기고 정적 파일 호스트 형태를 확정한다 |
| 선행 | T02 |
| 변경 대상 | `Api/Controllers/` 삭제, `Api/Program.cs`, `Api/Composition/`, `Api/Middlewares/`, `Api/wwwroot/`, `Api/Properties/launchSettings.json` |
| 내용 | 컨트롤러 폴더와 `AddControllers`·`MapControllers`·`UseHttpsRedirection`·`UseAuthorization` 제거. `Program.cs`를 Minimal API로 재작성하고 `/health`만 매핑한다. `Middlewares/`에 예외 처리 미들웨어를 둔다. `wwwroot/`는 자리표시자 파일 하나로 생성하고 내용은 gitignore한다. `Api` 전체 타입은 `internal`, 네임스페이스는 `NV.Api`. `Composition/`은 등록할 모듈이 생기는 T21에서 만든다 — 비어 있는 등록 진입점을 미리 두면 자리표시자가 된다 |
| 완료 조건 | `dotnet run --project Api` 후 `/health`가 200을 반환. `Controllers/` 폴더 부재 |
| 검증 | `dotnet build` 경고 0, `/health` 요청 |
| 담당 | 에이전트 |

### T07 — 프로젝트 참조 연결

| 항목 | 내용 |
|---|---|
| 목적 | 참조 규칙을 컴파일러가 강제하는 형태로 고정한다 |
| 선행 | T03, T04, T05, T06 |
| 변경 대상 | `Infrastructure/`, `Modules/Realtime/`, `Api/`의 `.csproj` |
| 내용 | `Infrastructure → Shared`, `Modules/Realtime → Shared, Infrastructure`, `Api → Shared, Infrastructure, Modules/Realtime`. `Shared`는 어떤 참조도 갖지 않는다. `Shared`를 참조하는 쪽은 `net10.0` TFM 자산을 쓴다 |
| 완료 조건 | 빌드 경고 0. 순환 참조 없음 |
| 검증 | `dotnet build NVserver.slnx` |
| 담당 | 에이전트 |

### T08 — `Architecture.Tests` 작성

| 항목 | 내용 |
|---|---|
| 목적 | 경계가 조용히 무너지는 것을 구현 시작 전에 막는다 |
| 선행 | T07, 질문 2 결정 |
| 변경 대상 | `tests/Architecture.Tests/` 신규, `NVserver.slnx`, `Directory.Packages.props` |
| 내용 | 필수 검사 3개. (1) 모듈 간 상호 참조 없음. (2) `Infrastructure`가 모듈을 참조하지 않음. (3) 모듈의 공개 타입이 `NV.{모듈}.Contracts` 네임스페이스 하위 타입과 `{모듈}Module` 클래스뿐. 참조 그래프는 `.csproj`의 `ProjectReference` 선언에서 읽는다 — `Assembly.GetReferencedAssemblies()`는 타입을 실제로 쓰지 않으면 참조를 보고하지 않아 초기 단계에서 검사가 공허하게 통과한다. 모듈이 0개일 때 공허하게 통과하는 것도 막는다. 테스트 메서드명은 한글 스네이크 |
| 추가 검사 | (4) 각 모듈이 `InternalsVisibleTo("Modules.Tests")`를 공개함 — T09의 `AssemblyInfo`가 빠지면 유닛 테스트가 컴파일 단계에서 막히는데 원인이 드러나기 어렵다 |
| 완료 조건 | 4개 테스트 통과. 임시로 규칙을 위반시키면 해당 테스트가 실패 |
| 검증 | `dotnet test`. 임시로 `Infrastructure → Modules/Realtime` 참조와 `Contracts` 밖 `public` 타입을 넣어 실패 확인 후 되돌림. 이때 `Realtime → Infrastructure` 참조를 함께 제거해야 한다. 그러지 않으면 순환 종속이라 restore 단계에서 MSB4006으로 먼저 실패하고 테스트가 실행되지 않는다 |
| 담당 | 에이전트 |

### T09 — 모듈 `AssemblyInfo`와 `InternalsVisibleTo`

| 항목 | 내용 |
|---|---|
| 목적 | 유닛 테스트가 모듈 내부에 접근할 경로를 연다 |
| 선행 | T07 |
| 변경 대상 | `Modules/Realtime/AssemblyInfo.cs` |
| 내용 | 각 모듈에 `InternalsVisibleTo("Modules.Tests")`. `tests/Modules.Tests/` 프로젝트 자체는 첫 실제 테스트가 생기는 T17에서 만든다 — 테스트가 0개인 프로젝트는 러너 동작이 버전마다 다르고 골격만 있는 상태가 통과로 읽힌다 |
| 완료 조건 | `Architecture.Tests`의 `InternalsVisibleTo` 검사가 통과 |
| 검증 | `dotnet test` |
| 담당 | 에이전트 |

### T10 — `compose.yaml` 정리와 Dockerfile 경로 검증

| 항목 | 내용 |
|---|---|
| 목적 | 배포 검증 이전에 컨테이너 정의를 실제 프로젝트 구성과 일치시킨다 |
| 선행 | T05 |
| 변경 대상 | `compose.yaml`, `Api/Dockerfile`, `.dockerignore` |
| 내용 | 존재하지 않는 `Application/Dockerfile`을 참조하는 `application` 서비스 제거. `Dockerfile`이 `Shared`, `Infrastructure`, `Modules/Realtime`, 루트 빌드 설정 파일을 복사하도록 `COPY` 목록 갱신. `.dockerignore`에 `artifacts/`, `Client/` 추가 |
| 완료 조건 | `docker compose build`가 성공하고 컨테이너가 `/health`에 200을 반환 |
| 검증 | `docker compose build`, 컨테이너 기동 후 `/health` 요청 |
| 담당 | 에이전트 |
| 상태 | 파일 수정 완료. Docker 데몬이 기동되지 않아 `docker compose build` 검증은 미실행. T25 전에 실행해야 한다 |

### T11 — 문서 폴더 개명

| 항목 | 내용 |
|---|---|
| 목적 | 문서 기준 폴더명과 실제를 일치시킨다 |
| 선행 | 질문 1 결정 |
| 변경 대상 | `Docs/` → `docs/`, 상호 참조 경로를 가진 문서 파일 |
| 내용 | `git mv`를 2단계(임시명 경유)로 수행한다. Windows 파일시스템이 대소문자를 구분하지 않아 단일 `git mv`는 인덱스에 반영되지 않는다 |
| 완료 조건 | `git ls-files`에 `docs/` 소문자로 기록됨 |
| 검증 | `git ls-files docs` 출력 확인 |
| 담당 | 에이전트 |

### T12 — ignore 규칙과 에디터 설정

| 항목 | 내용 |
|---|---|
| 목적 | 빌드·클라이언트 산출물이 커밋되는 것을 막고 포맷 차이로 인한 경고를 없앤다 |
| 선행 | T01, 질문 8 결정 |
| 변경 대상 | `.gitignore`, `.gitattributes`, `.editorconfig` 신규 |
| 내용 | ignore에 `artifacts/`, `Api/wwwroot/*`, `data/`, Unity 산출물(`Client/Library/`, `Client/Temp/`, `Client/Logs/`, `Client/Builds/`, `Client/obj/`, `*.csproj.user`) 추가. `.claude/plans/`는 팀 공유 대상이므로 ignore하지 않는다. `.gitattributes`에 Unity YAML `eol=lf`와 바이너리 에셋 LFS 규칙. `.editorconfig`에 네이밍·`using` 순서 규칙 |
| 완료 조건 | 빈 WebGL 빌드를 `wwwroot/`에 넣어도 `git status`가 깨끗함 |
| 검증 | `git status --short`, `git check-ignore -v`로 대표 경로 확인 |
| 담당 | 에이전트 |

### T13 — Unity 6 LTS 설치

| 항목 | 내용 |
|---|---|
| 목적 | 선행 시간이 긴 사람 의존 작업을 앞으로 당긴다 |
| 선행 | 질문 5 결정 |
| 변경 대상 | 로컬 개발 환경 |
| 내용 | Unity 6 LTS 설치. WebGL Build Support 모듈 포함 |
| 완료 조건 | Unity Hub에서 해당 버전에 WebGL 타겟이 선택 가능 |
| 검증 | 에디터에서 Build Settings의 WebGL 플랫폼 활성 확인 |
| 담당 | 사람 |
| 의존 태스크 | T14, T15, T22, T23, T28 |

### T14 — `Client/` Unity 프로젝트 생성과 `Shared` 로컬 패키지 연결

| 항목 | 내용 |
|---|---|
| 목적 | `Shared`가 Unity 쪽에서 컴파일되는지 코드가 쌓이기 전에 확인한다 |
| 선행 | T02, T03, T13 |
| 변경 대상 | `Client/` 신규 (솔루션 밖), `Client/Packages/manifest.json`, `Shared/` — `Shared.asmdef`, `package.json` |
| 내용 | 3D URP 템플릿으로 `Client/` 생성. `Shared/`에 `package.json`과 `noEngineReferences: true`인 `Shared.asmdef` 추가. `manifest.json`에 `Shared`를 상대 경로 로컬 패키지로 등록. `.slnx`에는 포함하지 않는다 |
| 완료 조건 | Unity 에디터 콘솔에 에러 0. Package Manager에서 `Shared`가 로컬 패키지로 표시됨 |
| 검증 | Unity 에디터 재컴파일 후 콘솔 확인, `dotnet build NVserver.slnx` 경고 0 유지 |
| 담당 | 사람 (설정) + 에이전트 (`asmdef`·`package.json` 작성) |

### T15 — `Shared` 공동 컴파일 스모크

| 항목 | 내용 |
|---|---|
| 목적 | 양쪽 컴파일 실패를 최소 코드량 시점에 드러낸다 |
| 선행 | T14 |
| 변경 대상 | `Shared/Simulation/` |
| 내용 | `System.Numerics.Vector3`를 사용하는 순수 정적 함수 하나와 상수 하나를 넣고 양쪽에서 컴파일한다. 이후 M2 작업이 이 자리를 채운다 |
| 완료 조건 | `dotnet build` 경고 0, Unity 에디터 콘솔 에러 0 |
| 검증 | 두 빌드 각각 실행 |
| 담당 | 에이전트 + 사람 (Unity 측 확인) |

---

## M2 — WebSocket 접속과 30Hz 틱

완료 시점에 브라우저 devtools에서 WebSocket 접속 후 서버 틱 번호가 30Hz로 도착한다.

### T16 — `Shared/Contracts` 프로토콜 정의

| 항목 | 내용 |
|---|---|
| 목적 | 클라이언트와 서버가 다른 시점에 빌드되므로 버전 핸드셰이크를 프로토콜의 일부로 고정한다 |
| 선행 | T15 |
| 변경 대상 | `Shared/Contracts/Messages/`, `Shared/Contracts/Enums/` |
| 내용 | opcode 열거형(`0x01` Input, `0x81` Snapshot, `0x82` Event, `0x83` Welcome), 프로토콜 버전 상수, `InputFrame`(7B), `EntityState`(13B), `SnapshotHeader`(10B), `WelcomeMessage`(13B) 구조체, 버튼·플래그 열거형. 모두 `public readonly struct`, ORM·JSON 어트리뷰트 없음. `Event`(`0x82`)는 opcode 값만 정의하고 코덱은 쓰지 않는다 — 발행할 이벤트가 아직 없다 |
| 결정 | `EntityState`에 `pitch(i16)`를 포함한다 — 질문 11 참조. 스냅샷 헤더에 `AckedInputTick`을 둔다. 수신자마다 값이 달라 스냅샷 본문은 같아도 세션별로 인코딩해야 한다 |
| 완료 조건 | 구조체 크기가 문서 명세와 일치. 두 TFM 모두 빌드 |
| 검증 | `dotnet build`, 크기 검증 테스트 |
| 담당 | 에이전트 |

### T17 — `Shared/Serialization` 비트 코덱

| 항목 | 내용 |
|---|---|
| 목적 | 스냅샷 대역폭을 문서 명세(8인 114B) 안에 유지한다 |
| 선행 | T16 |
| 변경 대상 | `Shared/Serialization/`, `tests/Modules.Tests/` 신규, `NVserver.slnx` |
| 내용 | 비트 리더·라이터, 위치 양자화(`int16`, 1/64m), 각도 양자화, 메시지 코덱. 리틀엔디언 고정. `tests/Modules.Tests/`를 이 태스크에서 생성하고 `Simulation/`·`Serialization/`·`Realtime/`으로 나눈다 |
| 완료 조건 | 라운드트립 테스트 통과. 8인 스냅샷 인코딩 길이가 명세와 일치 |
| 검증 | `dotnet test --filter Serialization` |
| 담당 | 에이전트 |

### T18 — `Shared/Transport` 전송 인터페이스

| 항목 | 내용 |
|---|---|
| 목적 | 클라·서버가 각각 구현하는 지점을 명시한다 |
| 선행 | T16 |
| 변경 대상 | `Shared/Transport/`, `Shared/Simulation/` |
| 내용 | `IServerTransport`, `IClientTransport`, 신뢰성 열거형. 틱레이트 상수를 `Shared/Simulation/SimConstants`에 함께 넣는다 — 틱 루프(T20)가 이 값을 필요로 하고 고정 파라미터의 출처가 `Shared/Simulation`이다. 나머지 시뮬레이션 상수는 T26에서 같은 파일에 추가한다 |
| 완료 조건 | 두 TFM 모두 빌드. `Shared`에 NuGet 참조 없음 |
| 검증 | `dotnet build` |
| 담당 | 에이전트 |

### T19 — `Realtime` 세션과 송수신 펌프

| 항목 | 내용 |
|---|---|
| 목적 | WebSocket이 동시 `SendAsync`를 허용하지 않는 제약을 구조로 강제한다 |
| 선행 | T17, T18 |
| 변경 대상 | `Modules/Realtime/Transport/` |
| 내용 | 세션 타입, 수신 펌프 → `InboundQueue`(`ConcurrentQueue`), 송신 채널(`BoundedChannel(32, DropOldest)`) → 송신 펌프 하나, WebSocket 전송 구현. 세션당 송신 경로는 채널 하나 + 펌프 하나로 직렬화. 전부 `internal` |
| 완료 조건 | 두 클라이언트 동시 접속 시 프레임 손상이나 동시 전송 예외 없음 |
| 검증 | 두 브라우저 탭 접속 후 devtools에서 프레임 무결성 확인 |
| 담당 | 에이전트 |

### T20 — 룸과 틱 오케스트레이션

| 항목 | 내용 |
|---|---|
| 목적 | 룸 상태 소유권을 틱 루프로 고정하고 예외로 호스트가 내려가지 않게 한다 |
| 선행 | T19 |
| 변경 대상 | `Modules/Realtime/Simulation/`, `Modules/Realtime/Contracts/` |
| 내용 | `PeriodicTimer` 기반 30Hz 루프의 호스티드 서비스. 루프 전체를 `try/catch`로 감싸고 내부에 `await` I/O를 두지 않는다. 틱 시작 시 `InboundQueue`와 `CommandQueue`를 전부 드레인. 하드코딩된 단일 룸, 쿼리스트링으로 룸 지정. 접속 시 임시 ID 발급. `Contracts/`에 `IRoomQuery`(불변 스냅샷 반환), `IRoomCommand`(큐 적재), 룸 요약 타입. 살아 있는 룸 객체를 모듈 밖으로 반환하지 않는다 |
| 완료 조건 | 30초 관측에서 틱 번호가 초당 30±1로 증가하고 드리프트가 누적되지 않음 |
| 검증 | devtools WS 프레임의 틱 번호 증가율 확인 |
| 담당 | 에이전트 |

### T21 — 모듈 등록·엔드포인트와 `Api` 컴포지션

| 항목 | 내용 |
|---|---|
| 목적 | 엔드포인트 소유권을 모듈에 두어 추출 경로를 유지한다 |
| 선행 | T20 |
| 변경 대상 | `Modules/Realtime/` (모듈 등록 클래스, 엔드포인트), `Api/Composition/`, `Api/Program.cs`, `Infrastructure/Logging/` |
| 내용 | `Api/Composition/`을 이 태스크에서 생성한다. `RealtimeModule`의 `AddRealtime()`·`MapRealtime()`이 DI 등록, `AddHostedService`, `/ws` 매핑을 담당한다. `Api`는 호출만 한다. `Api`에 `UseWebSockets()` 추가. `Infrastructure/Logging/`에 로깅 설정만 둔다. `Api/Controllers/`를 다시 만들지 않는다 |
| 완료 조건 | `/ws` 업그레이드 성공, 프로토콜 버전 불일치 시 즉시 연결 종료, `Architecture.Tests` 공개 표면 테스트 통과 |
| 검증 | `dotnet test`, devtools에서 정상 접속과 버전 불일치 거부 각각 확인 |
| 담당 | 에이전트 |

---

## M3 — 배포 파이프라인 관통

완료 시점에 빈 WebGL 빌드가 배포 URL에서 로드되고 `wss://`로 서버에 붙는다.

### T22 — WebGL WebSocket 전송 도입과 클라이언트 접속 코드

| 항목 | 내용 |
|---|---|
| 목적 | WebGL 싱글 스레드 제약 아래 동작하는 전송 경로를 확보한다 |
| 선행 | T14, T18, 질문 5 결정 |
| 변경 대상 | `Client/` |
| 내용 | 브라우저 `WebSocket` API 를 `.jslib` 로 감싼 전송 구현. `IClientTransport` 를 구현한다. `Task.Run`·`Thread`·`lock`·`System.Net.Sockets`·`System.Net.WebSockets` 를 쓰지 않는다. 수신은 `.jslib` 콜백이 큐에 넣고 게임 루프가 폴링해 꺼낸다. `binaryType` 은 `arraybuffer`. 접속 후 Welcome 수신과 틱 번호 로그 출력까지 |
| 서버측 전제 | 이미 충족된 상태다. 서버는 raw `System.Net.WebSockets` 이며 Socket.IO·SignalR 프레이밍을 쓰지 않는다. 브라우저가 WS 핸드셰이크에 커스텀 헤더를 붙일 수 없으므로 프로토콜 버전과 룸을 쿼리스트링으로 받는다. 프레임은 전부 바이너리다 |
| 완료 조건 | 로컬 에디터 및 WebGL 빌드에서 접속 성공, 콘솔에 틱 번호 출력, 바이너리 프레임 왕복 |
| 검증 | 에디터 콘솔 및 브라우저 devtools Network → WS 프레임 확인 |
| 담당 | 사람 (Unity 측) + 에이전트 (`.jslib` 및 `IClientTransport` 구현 작성 가능) |

### 클라이언트 전송 방식 검토 결과

브라우저에서 쓸 수 있는 실시간 양방향 전송은 사실상 `WebSocket` 과 WebTransport 둘이다. WebTransport 는 Safari 지원이 늦고 Unity 쪽 경로가 없어 제외한다.

| 방식 | 판정 | 근거 |
|---|---|---|
| 브라우저 `WebSocket` + `.jslib` | **채택** | `conventions.md` 가 규정한 유일한 경로. 서버는 이미 raw WebSocket 으로 동작하며 브라우저 표준 클라이언트로 검증됨 |
| Socket.IO | 제외 | `architecture.md` 도입 금지 목록. .NET 서버 구현체 유지보수 중단. 또한 Engine.IO 자체 프레이밍이 얹혀 수기 비트패커의 바이트 예산(8인 114B)이 깨진다 |
| SignalR | 제외 | `architecture.md` 도입 금지 목록. 허브 프로토콜 오버헤드, Unity 지원 빈약 |
| `System.Net.WebSockets.ClientWebSocket` | 제외 | WebGL 에 `System.Net.Sockets` 가 없다. 에디터에서는 되고 WebGL 빌드에서만 실패해 발견이 늦다 |

`.jslib` 을 직접 작성할지 기존 라이브러리를 쓸지는 질문 5 에서 결정한다.

WebGL 에서 확인할 항목이다.

| 항목 | 확인 시점 |
|---|---|
| `binaryType = 'arraybuffer'` 설정 누락 시 `Blob` 으로 수신되어 동기 읽기가 불가 | T22 |
| 커스텀 헤더 불가 — 버전·룸은 쿼리스트링으로만 전달 | T22 (서버측 반영 완료) |
| HTTPS 페이지에서 `ws://` 는 mixed content 로 차단 | T25 |
| 브라우저는 ping 을 JS 에서 보낼 수 없다. 서버 `KeepAliveInterval` 이 유일한 유지 수단 | T25 |
| 탭 백그라운드 시 타이머 스로틀링으로 입력 전송이 끊김 | M5 |

### T23 — 빈 WebGL 빌드 산출과 `wwwroot` 배치

| 항목 | 내용 |
|---|---|
| 목적 | WebGL 빌드 크기와 산출물 형태를 배포 검증 전에 확정한다 |
| 선행 | T22 |
| 변경 대상 | `Api/wwwroot/` (내용은 gitignore) |
| 내용 | IL2CPP·Brotli 압축 설정으로 빈 씬 빌드. 산출물을 `Api/wwwroot/`에 배치 |
| 완료 조건 | `wwwroot/`에 `.br` 산출물 존재. `git status`가 깨끗함 |
| 검증 | `git status --short`, 파일 목록 확인 |
| 담당 | 사람 |

### T24 — 정적 파일 서빙과 Brotli 헤더 처리

| 항목 | 내용 |
|---|---|
| 목적 | `.br` 파일에 `Content-Encoding`이 붙지 않아 브라우저가 로딩 화면에서 멈추는 실패를 없앤다 |
| 선행 | T23 |
| 변경 대상 | `Api/Composition/` |
| 내용 | `.br`·`.gz`·`.wasm`·`.data` 확장자에 대한 `Content-Type`·`Content-Encoding` 매핑을 정적 파일 설정에 추가. 기본 문서 매핑 |
| 완료 조건 | 로컬에서 WebGL 페이지가 로딩을 완료하고 devtools의 `.br` 응답에 `Content-Encoding: br`가 붙음 |
| 검증 | devtools Network에서 응답 헤더 확인 |
| 담당 | 에이전트 |

### T25 — 배포 1회 검증

| 항목 | 내용 |
|---|---|
| 목적 | 파이프라인 결함을 게임 로직 이전에 드러낸다 |
| 선행 | T24, 질문 4 결정 |
| 변경 대상 | `compose.yaml`, `Api/Dockerfile`, 배포 대상 환경 설정 |
| 내용 | 컨테이너 이미지 빌드 후 배포 대상에 1회 배포. TLS 종단을 확인하고 클라이언트 접속 URL을 `wss://`로 맞춘다. 이미지 최적화와 CI 구성은 이번 단계 범위 밖 |
| 완료 조건 | 배포 URL에서 WebGL 페이지 로드 완료, `wss://` 핸드셰이크 성공, mixed content 차단 없음 |
| 검증 | 배포 URL 접속 후 devtools Console·Network 확인 |
| 담당 | 사람 (배포 실행) + 에이전트 (설정 작성) |

---

## M4 — 서버 권위 이동

완료 시점에 클라이언트 키 입력이 서버 판정을 거쳐 화면 이동으로 나타난다. 예측이 없어 지연이 보이는 상태다.

### T26 — `Shared/Simulation` 이동 함수

| 항목 | 내용 |
|---|---|
| 목적 | 클라이언트 예측이 성립할 수 있도록 이동 계산을 순수 함수로 고정한다 |
| 선행 | T16 |
| 변경 대상 | `Shared/Simulation/` |
| 내용 | 시뮬레이션 상수(틱 델타 33.3ms, 이동 속도, 중력), 플레이어 상태 구조체, 이동 적용 함수, 결정적 해시, 결정적 난수. 난수·시간·정적 가변 상태를 참조하지 않는다. `DateTime`·`Time.deltaTime`·`new Random()`을 쓰지 않는다 |
| 결정 — 삼각함수 | `MathF.Sin`·`Cos` 를 쓰지 않는다. 정확도가 구현에 맡겨져 있어 Unity(IL2CPP)의 libm 과 .NET 구현이 마지막 비트에서 갈릴 수 있다. 범위 축소 후 테일러 급수를 `x^11` 항까지 전개한 `DeterministicMath.Sin` 으로 대체한다. `MathF.Sqrt`·`Floor`·`Abs` 는 IEEE 754 가 결과를 규정하므로 사용한다 |
| 결정 — 벡터 연산 | `Vector3.Normalize`·`Length`·`Dot`·`Distance` 를 쓰지 않는다. 구현이 SIMD·FMA 경로를 타면 라운딩이 달라진다. `Vector3` 는 데이터 컨테이너로만 쓰고 연산은 `DeterministicMath` 의 스칼라 구현을 쓴다 |
| 결정 — 입력 역양자화 | 서버도 반드시 `MoveIntent.FromInput` 을 거친다. 클라이언트가 예측에 쓰는 값은 양자화를 통과한 값이므로, 서버가 원본 부동소수점을 쓰면 양쪽 결과가 갈린다 |
| 검증 추가 | 중간 상태에서 남은 입력을 재적용한 결과가 통째로 돌린 결과와 상태 해시까지 같은지 확인한다. M5 리컨실리에이션이 성립하기 위한 성질이며, 깨지면 증상이 떨림으로만 나타난다 |
| 완료 조건 | 동일 입력 시퀀스를 반복 적용하면 상태 해시가 항상 동일 |
| 검증 | `dotnet test --filter Determinism` |
| 담당 | 에이전트 |

### T27 — `Shared/Collision` AABB와 스윕

| 항목 | 내용 |
|---|---|
| 목적 | 물리 엔진 없이 클라·서버 동일 결과를 보장한다 |
| 선행 | T26 |
| 변경 대상 | `Shared/Collision/` |
| 내용 | AABB, 스윕, 레이캐스트, 맵 데이터 스키마. `Physics.Raycast`·`Rigidbody`·`CharacterController`를 쓰지 않는다 |
| 결정 — 스윕 형태 | 월드 충돌은 **스윕 AABB** 로 한다. `readme.md` 의 기술 스택이 "물리: 없음 — `Shared`에 직접 구현 (AABB)" 이고, 민코프스키 합으로 레이·AABB 교차 하나에 환원되어 프리미티브가 늘지 않는다 |
| 결정 — 캡슐 제외 | 캡슐과 레이·캡슐 교차는 넣지 않는다. 캡슐이 필요한 지점은 히트스캔의 플레이어 히트박스이고 히트스캔은 이번 단계 범위 밖이다. 범위 제외 기능을 미리 준비하지 않는다 |
| 결정 — 겹침 해소 | 스윕은 시작 시점에 이미 겹쳐 있으면 진입 시점이 음수로 나와 이동이 통과한다. 이동 전에 관통이 가장 얕은 축으로 밀어내는 단계를 둔다 |
| 결정 — 맵 스키마 | 맵 JSON 에 `Vector3` 를 노출하지 않는다. `X`·`Y`·`Z` 가 프로퍼티가 아니라 필드라 기본 설정의 `System.Text.Json` 이 빈 객체로 직렬화한다. 증상이 "맵이 통째로 사라짐" 으로만 나타난다. `MinX`…`MaxZ` 개별 프로퍼티를 쓴다 |
| 완료 조건 | 경계 관통·모서리 미끄러짐·면 접촉·겹침 해소·정지 케이스 테스트 통과 |
| 검증 | `dotnet test --filter Collision` |
| 담당 | 에이전트 |

### T28 — 맵 export 에디터 스크립트와 맵 데이터

| 항목 | 내용 |
|---|---|
| 목적 | 클라이언트가 보는 지형과 서버가 판정하는 콜리전을 하나의 출처에서 만든다 |
| 선행 | T27 |
| 변경 대상 | `Client/` (에디터 스크립트), `MapData/`, `Infrastructure/FileSystem/` |
| 내용 | Unity 에디터에서 콜리전 박스를 JSON으로 export. `MapData/`에 산출. `Infrastructure/FileSystem/`에 로더. 맵 해시를 Welcome에 포함 |
| 완료 조건 | 서버가 맵을 로드하고, 클라·서버 맵 해시가 일치 |
| 검증 | 서버 기동 로그의 맵 해시와 클라이언트 로그 대조 |
| 담당 | 사람 (export 실행) + 에이전트 (스크립트·로더 작성) |

### T29 — 입력 검증과 스냅샷 브로드캐스트

| 항목 | 내용 |
|---|---|
| 목적 | 클라이언트 권위가 생기는 경로를 차단한다 |
| 선행 | T20, T27 |
| 변경 대상 | `Modules/Realtime/Simulation/` |
| 내용 | 입력 수용 시 속도 클램프와 틱 범위 검사. 위치를 클라이언트로부터 받지 않는다. 틱마다 풀 스냅샷을 전 세션에 브로드캐스트. 델타 압축을 하지 않는다. 입력은 최근 3틱치 중복 전송을 전제로 중복을 무시한다 |
| 완료 조건 | 조작한 위치 값을 주입해도 서버 상태가 변하지 않음. 스냅샷이 매 틱 도착 |
| 검증 | `dotnet test --filter Realtime`, devtools 프레임 관찰 |
| 담당 | 에이전트 |

### T30 — 클라이언트 입력 전송과 서버 상태 렌더

| 항목 | 내용 |
|---|---|
| 목적 | 마일스톤의 관찰 가능한 결과를 만든다 |
| 선행 | T29, T22, T28 |
| 변경 대상 | `Client/` |
| 내용 | 키 입력을 `InputFrame`으로 전송하고 수신 스냅샷 위치를 그대로 적용한다. 예측·보간을 넣지 않는다 |
| 완료 조건 | 키 입력 후 왕복 지연만큼 늦게 캐릭터가 이동 |
| 검증 | 로컬 및 배포 URL에서 조작 관찰 |
| 담당 | 사람 |

### T31 — 네트워크 조건 주입기

| 항목 | 내용 |
|---|---|
| 목적 | 예측·보정 검증에는 지연이 필요하다. M5 진입 전에 준비한다 |
| 선행 | T29 |
| 변경 대상 | `Modules/Realtime/Transport/`, `Api/appsettings.Development.json` |
| 내용 | 송수신 경로에 지연·지터·손실을 주입하는 개발 전용 래퍼. 설정으로 on/off와 수치 지정. 프로덕션 설정에서는 비활성 |
| 완료 조건 | 지연 120ms·지터 ±30ms·손실 2% 설정 시 devtools에서 프레임 간격 변화가 관찰됨 |
| 검증 | 설정 변경 후 프레임 타임스탬프 분포 확인, 비활성 시 오버헤드 없음 확인 |
| 담당 | 에이전트 |

---

## M5 — 클라이언트 예측과 리컨실리에이션

완료 시점에 입력이 즉시 반응하고 보정 시 떨림이 없다.

### T32 — 클라이언트 예측과 입력 버퍼

| 항목 | 내용 |
|---|---|
| 목적 | 입력 지연을 제거하되 서버 권위를 유지한다 |
| 선행 | T30, T31 |
| 변경 대상 | `Client/` |
| 내용 | 로컬 입력을 `Shared/Simulation` 함수로 즉시 적용하고 미확인 입력을 버퍼에 보관한다. 클라이언트 전용 이동 로직을 따로 만들지 않는다 |
| 완료 조건 | 입력 후 프레임 지연 없이 캐릭터가 반응 |
| 검증 | 조건 주입기 활성 상태에서 조작 관찰 |
| 담당 | 사람 |

### T33 — 리컨실리에이션과 재적용

| 항목 | 내용 |
|---|---|
| 목적 | 예측 오차 보정이 떨림으로 나타나지 않게 한다 |
| 선행 | T32 |
| 변경 대상 | `Client/`, `Shared/Simulation/` (필요 시 순수성 보강) |
| 내용 | 확인된 서버 틱까지 버퍼를 정리하고 남은 입력을 서버 상태 위에 재적용. 오차 임계값 이하는 무보정 |
| 완료 조건 | 지연 120ms·지터 ±30ms·손실 2%에서 벽 밀착 이동 시 떨림 없음 |
| 검증 | 조건 주입기 활성 상태 조작 관찰, 결정성 테스트 재실행 |
| 담당 | 사람 (클라이언트) + 에이전트 (`Shared` 순수성 검증) |

---

## M6 — 원격 엔티티 보간

완료 시점에 두 클라이언트가 서로의 움직임을 부드럽게 본다.

### T34 — 스냅샷 버퍼와 보간

| 항목 | 내용 |
|---|---|
| 목적 | 30Hz 스냅샷을 렌더 프레임레이트로 부드럽게 표현한다 |
| 선행 | T33 |
| 변경 대상 | `Client/` |
| 내용 | 100ms 보간 버퍼. 원격 엔티티는 두 스냅샷 사이를 보간한다. 로컬 플레이어에는 적용하지 않는다 |
| 완료 조건 | 두 탭 동시 접속 시 상대 캐릭터 이동에 끊김 없음 |
| 검증 | 두 브라우저 탭 조작 관찰, 조건 주입기 활성 상태 재확인 |
| 담당 | 사람 |

### T35 — 다중 접속 확인과 규약 기록

| 항목 | 내용 |
|---|---|
| 목적 | 확정된 규칙만 문서에 남긴다 |
| 선행 | T34 |
| 변경 대상 | `docs/conventions.md` |
| 내용 | 실행 중 확정된 규칙과 30분 이상 걸린 문제를 해당 절에 추가. 계획 내용을 옮기지 않는다. 이 계획 파일을 `.claude/plans/done/`으로 이동 |
| 완료 조건 | `conventions.md`에 겪은 문제 기반 항목만 존재. 추측·일반론 없음 |
| 검증 | 문서 리뷰 |
| 담당 | 에이전트 |

---

## 확정된 결정

| # | 항목 | 결정 |
|---|---|---|
| 1 | 문서 폴더명 | `docs/` 로 개명. 2단계 `git mv` 로 수행 |
| 2 | 테스트 패키지 | xUnit 2.9.3, xunit.runner.visualstudio 3.1.4, Microsoft.NET.Test.Sdk 17.14.1 승인. NetArchTest 미도입 — 경계 검사는 리플렉션과 `.csproj` 파싱으로 직접 작성 |
| 7 | `Shared` `LangVersion` | 두 TFM 모두 9.0 고정. `record struct` 선언이 `netstandard2.1`·`net10.0` 양쪽에서 CS8773 로 차단되는 것을 확인 |
| 8 | ignore 규칙 위치 | `NVserver/.gitignore`. 부모 저장소 루트의 `.gitignore` 는 건드리지 않음 |
| 3 | `Infrastructure/Logging` | 내장 `Microsoft.Extensions.Logging` 만 사용. `Logging/Serilog/` 폴더를 만들지 않는다. 새 NuGet 패키지 없음 |
| 6 | 참가 티켓 | 이번 단계는 티켓 없이 진행. 업그레이드 시 프로토콜 버전만 검사하고 서버가 룸 슬롯에서 `PlayerId` 를 발급한다. 티켓 검증은 `Identity`·`Matchmaking` 도입 시 추가한다 |
| — | 모듈·`Infrastructure` 의 웹 타입 접근 | `FrameworkReference Include="Microsoft.AspNetCore.App"` 사용. 공유 프레임워크 참조이며 NuGet 패키지가 아니다 |

---

## 질문 목록

계획에 임의로 반영하지 않았다. 결정 후 해당 태스크를 진행한다.

| # | 항목 | 배경 | 선택지 | 차단 태스크 |
|---|---|---|---|---|
| 4 | 배포 대상 플랫폼과 도메인 | `wss://` 필수이므로 TLS 종단이 필요하다. 대상이 정해지지 않으면 M3을 완료할 수 없다 | 대상 플랫폼 지정 / 도메인·인증서 발급 주체 지정 / 리버스 프록시 사용 여부 | T25 |
| 5 | Unity 버전 | Unity 6 LTS의 정확한 패치 버전이 필요하다 | 버전 지정 | T13 |
| 5b | WebGL WebSocket 구현 방식 | 전송 방식 자체는 브라우저 `WebSocket` + `.jslib` 로 확정됐다(위 검토 표). 남은 것은 `.jslib` 을 직접 쓸지 기존 라이브러리를 쓸지다. 기존 라이브러리는 새 의존성이라 확인 대상이다 | `.jslib` 직접 작성 — 필요한 것은 open·send·onmessage·close 네 함수뿐이고 의존성이 늘지 않는다 / 기존 WebGL WebSocket 라이브러리 도입 승인 | T22 |
| 9 | `Client/`의 Git 관리 | Unity 프로젝트는 LFS 대상 바이너리를 포함한다. 같은 저장소에 둘지 결정이 필요하다. `.gitignore`·`.gitattributes`는 같은 저장소를 전제로 작성했고 LFS 규칙이 이미 들어 있다 (git-lfs 3.7.1 설치 확인). 별도 저장소로 가면 두 파일에서 `Client/` 관련 줄을 제거한다 | 같은 저장소 + LFS(현재 반영 상태) / 별도 저장소 | T14 |
| 11 | `EntityState` 필드 목록과 크기 불일치 | `architecture.md`의 필드 목록 `id(u8), x/y/z(i16), yaw(u16), flags(u8), hp(u8)` 은 11B 로 합산되는데 명시된 크기는 13B 다. 같은 문서의 스냅샷 총계(8인 114B)는 10B 헤더 + 8×13B 와 정확히 맞는다. 필드 목록에서 `pitch(i16)` 가 누락된 것으로 판단해 13B 로 구현했다. 원격 플레이어의 조준 방향이 필요하므로 내용상으로도 `pitch` 가 맞다 | 13B + `pitch` 유지(현재 구현) / 11B 로 줄이고 총계 수정 / 다른 2B 필드 지정 | T16 이후 프로토콜 변경 |
| 12 | `Welcome` 의 `MapHash` | 맵 데이터가 없는 동안 `0` 을 보낸다. T28 에서 실제 해시로 채운다. `0` 을 "맵 없음" 으로 쓸지, 맵이 없을 때는 접속을 거부할지 | `0` 을 센티널로 유지(현재 구현) / 맵 미로드 시 접속 거부 | T28 |
| 10 | `MapData/` 생성 시점 | `data/`는 DB가 없어 만들지 않고 ignore 규칙만 넣었다. `MapData/`는 T28에서 처음 필요하다 | T28에서 생성(계획의 기본 가정) / 미리 생성 | T28 |

---

## 마일스톤별 예상 소요

에이전트 작업과 사람 작업을 분리해 적는다. 사람 작업은 대기 시간이 지배적이다.

| 마일스톤 | 에이전트 | 사람 | 비고 |
|---|---|---|---|
| M1 | 1.5 ~ 2일 | 0.5 ~ 1일 (Unity 설치·프로젝트 생성) | T13·T14는 T01~T12와 병행 가능 |
| M2 | 2 ~ 3일 | — | 틱 루프 안정화가 변동 요인 |
| M3 | 0.5일 | 1 ~ 2일 (WebGL 빌드·배포) | 배포 대상 미결정 시 무한 대기 |
| M4 | 2 ~ 3일 | 1 ~ 2일 (맵 export·클라이언트 렌더) | 충돌 케이스 테스트가 변동 요인 |
| M5 | 0.5일 | 2 ~ 3일 (예측·보정) | 떨림 원인 추적이 가장 큰 변동 요인 |
| M6 | — | 1일 | |

합계 에이전트 6.5 ~ 9일, 사람 5.5 ~ 9일. 병행 가능 구간을 고려하면 전체 8 ~ 12일.

---

## 가장 큰 리스크

**`Shared`가 Unity와 .NET 양쪽에서 동일하게 컴파일되지 않는 것.**

이 실패는 컴파일 에러로 즉시 드러나는 경우와 그렇지 않은 경우가 갈린다. 후자가 위험하다. TFM별 조건부 컴파일이나 `LangVersion` 차이로 양쪽 코드 경로가 갈리면 증상이 "가끔 캐릭터가 떨림"으로만 나타나고, 예측·리컨실리에이션 코드를 의심하며 시간을 소모한다. 원인과 증상의 거리가 가장 먼 실패다.

발현 시점은 M5지만 원인은 M1의 프로젝트 설정에서 생긴다.

**앞당겨 검증하는 방법** — T14·T15로 M1 안에서 관통한다.

1. `Directory.Build.props`의 출력 경로 리디렉션을 Unity 연결보다 먼저 적용한다 (T01 → T02 → T14 순서 고정)
2. `Shared`에 코드가 거의 없는 시점에 Unity 로컬 패키지로 물려 양쪽 컴파일을 확인한다 (T15)
3. T03에서 C# 10 이상 문법이 실제로 컴파일 실패하는지 확인한다. 빌드가 제약을 강제하지 못하면 나중에 Unity에서만 깨진다
4. `Shared`에 `#if` 조건부 컴파일을 두지 않는다. 두어야 할 상황이 생기면 그 자체를 확인 대상으로 올린다
5. M4의 결정적 해시 테스트(T26)를 Unity 측에서도 한 번 실행해 양쪽 결과를 대조한다
