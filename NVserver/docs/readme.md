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
dotnet run --project Api      # http://localhost:5000
```

진입점은 `Api` 하나다. `Shared`, `Infrastructure`, `Modules/*`는 클래스 라이브러리라 단독 실행되지 않는다.

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
