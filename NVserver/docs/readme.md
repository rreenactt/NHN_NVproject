# NVserver

브라우저에서 바로 플레이하는 8인 데스매치 FPS의 **서버 권위 게임 서버**.

Unity WebGL 클라이언트가 WebSocket으로 접속하고, 서버가 30Hz로 이동과 사격을 판정한다. 클라이언트는 자기 입력을 예측해 즉시 렌더링하되 최종 권한은 서버에 있다.

## 기술 스택

| 영역 | 선택 |
|---|---|
| 런타임 | .NET 10 |
| 웹 호스트 | ASP.NET Core Kestrel, Minimal API |
| 실시간 전송 | raw `System.Net.WebSockets` |
| 물리 | 없음 — `Shared`에 직접 구현 (AABB) |
| DB | SQLite + EF Core (모듈별 파일) |
| 클라이언트 | Unity 6 LTS, WebGL, URP, IL2CPP |
| 공유 코드 | C# / netstandard2.1 (Unity 겸용) |
| 아키텍처 | 모듈러 모놀리스 |

## 범위

**구현한다** — 서버 권위 이동, 히트스캔 판정, 클라이언트 예측·리컨실리에이션, 원격 플레이어 보간, 랙 보상, 룸 기반 데스매치, 리스폰, 스코어보드. 맵 1개, 무기 1종.

**구현하지 않는다** — 투사체, 조준경, 무기 교체, 재장전 동기화, 맵 2개 이상, 계정 시스템, 리플레이, 관전, 안티치트, 스케일아웃.

범위를 늘리려면 위 목록에서 하나를 뺀다.

## 고정 파라미터

| 항목 | 값 | 출처 |
|---|---|---|
| 틱레이트 | 30Hz (33.3ms) | `Shared/Simulation` |
| 룸당 인원 | 8명 | `Modules/Realtime/Simulation` |
| 위치 양자화 | `int16`, 1/64m | `Shared/Serialization` |
| 보간 버퍼 | 100ms | 클라이언트 |
| 랙 보상 상한 | 200ms (6틱) | `Modules/Realtime/Simulation` |
| 스냅샷 | 풀 스냅샷 (델타 없음) | — |

수치는 코드가 유일한 출처다. 문서와 다르면 코드가 맞다.

## 문서

| 작업 | 문서 · 절 |
|---|---|
| 새 파일을 어디에 둘지 판단 | [structure.md](structure.md) — 파일 배치 |
| 클래스·테이블·경로 이름 결정 | [structure.md](structure.md) — 코딩 컨벤션 |
| 프로젝트 참조 추가 | [architecture.md](architecture.md) — 참조 규칙 |
| 라이브러리 도입 검토 | [architecture.md](architecture.md) — 도입하지 않는 것 |
| `Shared` · `Infrastructure`에 코드 추가 | [architecture.md](architecture.md) — 해당 절 |
| DB · 프로토콜 변경 | [architecture.md](architecture.md) — 해당 절 |
| 구현 중 확정된 규칙 기록 | [conventions.md](conventions.md) |

익숙한 방식이 이 프로젝트에서는 금지인 경우가 많다. 코드를 쓰기 전에 [architecture.md](architecture.md)의 **기본값 대체표**를 확인한다.

## 작업 규칙

구현하지 말고 확인을 요청한다.

- 문서의 금지 규칙을 어겨야 문제가 풀릴 것 같을 때
- "구현하지 않는다" 목록의 기능을 요청받았을 때
- 새 NuGet 패키지, 새 모듈, 모듈 간 동기 호출, 새 인터페이스가 필요할 때
- 고정 파라미터를 바꿔야 할 때

작업 후 `dotnet build`(경고 0)와 `dotnet test`를 통과시킨다. `Shared`를 수정했다면 Unity 에디터 컴파일도 확인한다.

30분 이상 걸린 문제와 새로 확정된 규칙은 [conventions.md](conventions.md)에 기록한다.

## 실행

```bash
dotnet run --project Api      # http://localhost:5202
```

진입점은 `Api` 하나다. `Shared`, `Infrastructure`, `Modules/*`는 클래스 라이브러리라 단독 실행되지 않는다.

### 클라이언트와 함께 돌리기

Unity 클라이언트는 형제 폴더 `../NVproject`다. 배치와 파일 위치는 [structure.md](structure.md) — Unity 클라이언트.

**맵은 룸별로 배정된다.** 클라이언트가 어느 씬을 열었느냐에 따라 서버 설정을 바꾸고 재기동해야 한다면, 두 씬을 번갈아 확인하는 동안 그 왕복이 계속 반복되고 한 번 잊으면 증상이 맵 해시 불일치 하나로만 나타난다.

| 룸 | 맵 | 파일 | 용도 |
|---|---|---|---|
| `default` (그 외 전부) | `backrooms` | `MapData/backrooms.json` | 게임. 56×56 셀, 약 180m 정사각, 박스 1367개 |
| `test` | `test-room` | `MapData/test-room.json` | 멀티플레이 확인. 40m 정사각, 엄폐물 4개와 중앙 플랫폼, 스폰 8개가 링 위에서 중앙을 본다 |

```jsonc
// appsettings.json — 키가 룸 id 다. default 는 반드시 있어야 한다.
"Game": {
  "Maps": {
    "default": "../MapData/backrooms.json",
    "test": "../MapData/test-room.json"
  }
}
```

서버는 기동 시 로드한 맵을 전부 로그에 남기고, 룸을 만들 때 그 룸이 어느 맵을 물었는지 다시 남긴다. 클라이언트 콘솔의 해시와 그 줄을 대조하면 원인이 갈린다. 등록되지 않은 룸 id 는 `default` 맵으로 열린다 — 빈 콜리전으로 열면 플레이어가 지형을 통과하고 증상이 로직 버그처럼 보인다.

`MapData/*.json` 은 Unity 가 export 한다. 씨드·격자·벽 두께를 바꾸면 다시 돌린다.

멀티플레이를 확인하는 순서다.

1. `dotnet run --project Api` — 설정을 고칠 필요가 없다
2. Unity 에서 `Assets/Scenes/MultiplayerTest.unity` 를 열고 Play
3. 접속 패널에서 주소와 룸(`test`)을 확인하고 **접속** → **플레이**
4. 두 번째 클라이언트는 WebGL 빌드나 두 번째 에디터 인스턴스로 같은 룸에 붙인다

씬과 룸은 짝이다. `MultiplayerTest` 씬은 룸 `test`, `SampleScene` 은 그 밖의 아무 룸이다. 씬을 바꾸면 접속 패널의 룸도 함께 바꾼다 — 어긋나면 해시 불일치가 뜬다.

### 플레이어 2개 띄우기

에디터 인스턴스는 프로젝트당 하나뿐이므로, 둘 중 하나는 빌드된 플레이어여야 한다.

| 방법 | 준비 | 쓰는 경우 |
|---|---|---|
| **에디터 + Windows 빌드** | **Tools ▸ NV Network ▸ Build and Launch 2 Clients** | 기본. 반복이 빠르다 |
| 에디터 + WebGL 빌드 | WebGL 빌드를 `Api/wwwroot` 에 넣고 브라우저 탭 2개 | 최종 타겟 확인. 빌드가 몇 분 걸려 반복에는 못 쓴다 |
| Multiplayer Play Mode | `com.unity.multiplayer.playmode` 패키지 추가 | 빌드 없이 에디터 안에서 가상 플레이어. 새 패키지와 프로젝트 복제본 디스크 비용을 감수할 때 |

Windows 스탠드얼론은 전송 구현이 에디터와 같은 `ClientWebSocket` 경로다. WebGL 전용 결함(`.jslib`, `arraybuffer`, mixed content)만 빠지고 나머지 동기화 문제는 그대로 재현된다.

**두 플레이어를 못 쓰게 만드는 설정 두 개**가 있고 둘 다 프로젝트 설정에서 고쳐 두었다. 증상이 네트워크 결함으로 보이므로 적어 둔다.

| 설정 | 잘못된 값의 증상 |
|---|---|
| `runInBackground` | 꺼져 있으면 포커스를 잃은 창이 스크립트를 멈춘다. 상대가 얼어붙어 있다가 창을 클릭하면 순간이동한다. 서버는 정상이다 |
| `fullscreenMode` | 전체 화면이면 창 두 개를 나란히 볼 수 없다. Unity 기본값이 Fullscreen Window 다 |
| `forceSingleInstance` | 켜져 있으면 두 번째 실행이 조용히 거부된다 (원래 꺼져 있다) |

빌드된 클라이언트는 인스턴스마다 `Builds/TestClient/client-{시각}.log` 에 로그를 남긴다. 같은 파일을 쓰면 두 번째 인스턴스가 로그를 남기지 못한다.

`MultiplayerTest` 씬은 저장소에 들어 있다. 배선을 바꿀 때는 씬을 손으로 고치지 않고 **Tools ▸ NV Network ▸ Create Multiplayer Test Scene** 을 고쳐 다시 만든다. 씨드나 격자 수치를 바꿨다면 **Tools ▸ NV Network ▸ Export Map Collision** 을 먼저 돌린다.

### 접속 UI

접속은 자동이 아니라 UI 가 시작한다. 단계를 나눠 보여주는 것이 목적이다 — 서버 미기동, 주소 오타, 프로토콜 버전 불일치, 룸 정원 초과, 맵 해시 불일치는 전부 "안 됩니다" 하나로 나타나고, 단계가 갈려 있지 않으면 그 다섯을 구분할 수 없다.

| 단계 | 뜻 | 이 단계에서 멈추면 |
|---|---|---|
| 미접속 | 아직 소켓을 열지 않았다 | — |
| 접속 중 | 소켓을 여는 중. 8초 후 실패 처리 | 서버가 떠 있지 않거나 주소·포트가 틀렸다. 배포 환경이면 `ws://` 가 mixed content 로 차단된 경우다 |
| 핸드셰이크 | 소켓은 열렸다. `Welcome` 대기. 5초 후 실패 처리 | 룸 정원(8명)이 찼다. 프로토콜 버전 불일치는 업그레이드 전에 426 으로 거부되므로 여기까지 오지 않는다 |
| 플레이 | 서버가 슬롯을 주었다. 입력을 30Hz 로 보내는 중 | — |
| 실패 | 사유가 그대로 표시된다 | — |

`Esc` 로 패널을 여닫는다. 패널이 열려 있는 동안 캐릭터 입력은 끊기고 커서가 풀린다. HUD 는 배정된 플레이어 id, 스냅샷에 실린 엔티티 수, 서버 틱, 입력 지연(틱), 맵 해시 일치 여부를 보여준다.

접속이 끊기면 원격 플레이어의 몸이 지워지고 로컬 캐릭터는 오프라인 조작으로 되돌아간다. 서버 없이도 씬은 그대로 플레이된다.

## 구현 순서

| # | 단계 | 완료 조건 |
|---|---|---|
| 1 | 프로토콜 정의 + 비트 리더/라이터 | 라운드트립 테스트 통과 |
| 2 | WebSocket 에코 + 30Hz 틱 루프 | 클라이언트가 틱 카운터 수신 |
| 3 | 이동 컨트롤러 + AABB 충돌 | 유닛 테스트 통과 |
| 4 | 서버 권위 이동 (예측 없음) | 서버 응답대로 움직임 |
| 5 | 클라이언트 예측 + 리컨실리에이션 | 입력 즉시 반응, 떨림 없음 |
| 6 | 원격 엔티티 보간 | 다른 플레이어가 부드럽게 움직임 |
| 7 | 히트스캔 + 랙 보상 | 이동 중인 상대를 맞출 수 있음 |
| 8 | 룸 관리, 매치 시작/종료, 스코어보드 | 매치가 끝나고 재시작됨 |
| 9 | 부하 테스트 → 파라미터 조정 | 8인 접속 시 틱 오버런 없음 |

2단계 직후 빈 WebGL 빌드로 한 번 배포한다.
