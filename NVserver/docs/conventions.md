# 규약

구현하면서 확정된 규칙을 누적한다. 미리 채우지 않는다.

아키텍처 결정은 `architecture.md`, 폴더와 네이밍은 `structure.md`에 있다. 이 문서는 **그 둘로 답이 안 나온 것을 실제로 겪고 정한 결과**만 담는다.

**추가 기준** — 아래 중 하나에 해당할 때만 적는다.

- 실제로 밟은 문제이고, 모르면 다시 밟는다
- 어겼을 때 증상이 원인과 멀어 추적이 어렵다
- 두 사람 이상이 다르게 해도 되는 선택이라 통일이 필요하다

추측이나 일반론은 적지 않는다. 틀린 것으로 밝혀지면 지운다.

**형식** — 항목당 규칙 한 줄, 필요하면 근거 한 줄, 코드는 잘못된 예 ↔ 올바른 예 대조가 필요할 때만.

**갱신** — 규칙을 확정했거나 30분 이상 걸린 문제를 해결했으면 해당 절에 추가한다. 절이 없으면 만든다. 나중에 틀린 것으로 밝혀지면 지운다.

---

## 협상 불가 제약

플랫폼이 강제하는 것들이라 검증 없이 확정할 수 있다. 나머지 절은 비어 있는 상태로 시작한다.

### `Shared` 컴파일

Unity(IL2CPP)와 .NET 양쪽에서 같은 `.cs` 파일을 컴파일한다.

| 제약 | 값 |
|---|---|
| 타겟 프레임워크 | `netstandard2.1;net10.0` |
| C# 버전 | 9.0 — Unity 6 상한. `netstandard2.1` 기본값이 8이므로 명시한다 |
| NuGet 참조 | 금지 |
| `UnityEngine` 참조 | 금지 |
| 벡터 타입 | `System.Numerics.Vector3` |
| `ImplicitUsings` | 끈다. 전역 using이 `obj/`에 생성되는데 Unity는 보지 않는다 |

`System.Text.Json`이 NuGet이므로 DTO에 `[JsonPropertyName]`을 붙일 수 없다. 순수 POCO로 두고 명명 규칙은 양쪽 직렬화 설정에서 맞춘다.

`Shared/obj/`가 생기면 Unity가 그 안의 `AssemblyInfo.cs`를 패키지 범위로 인식해 중복 정의 에러를 낸다. `Directory.Build.props`에서 출력 경로를 `artifacts/`로 리디렉션한다.

### WebGL 클라이언트

싱글 스레드다. `Task.Run`, `Thread`, `lock`을 쓸 수 없다. `System.Net.Sockets`도 없어 WebSocket은 `.jslib` 경유다.

HTTPS 페이지에서 `ws://`는 mixed content로 차단된다.

빌드가 디컴파일되므로 `Shared`에 들어간 값은 클라이언트가 안다고 가정한다.

### `SourceGenerator`

Roslyn 분석기와 소스 제너레이터는 `netstandard2.0`만 로드된다.

`Shared`에 적용하지 않는다. Unity에서 쓰려면 DLL을 패키지 안에 넣고 `RoslynAnalyzer` 라벨을 붙여야 하는데, 어긋나면 서버는 생성된 코드로 Unity는 원본으로 컴파일된다. 증상이 런타임 캐릭터 떨림으로만 나타나 추적이 어렵다.

### 틱 루프 예외

.NET 6부터 `BackgroundServiceExceptionBehavior` 기본값이 `StopHost`다. 틱 하나에서 튄 예외가 서버 전체를 내린다. 루프 안을 `try/catch`로 감싼다.

---

## 빌드

`Directory.Build.props`의 출력 경로 리디렉션은 SDK 내장 `UseArtifactsOutput`·`ArtifactsPath`를 쓴다. `BaseIntermediateOutputPath`를 직접 지정하면 `MSBuildProjectExtensionsPath` 유도 순서에 의존하게 된다.

패키지 버전 중앙 관리를 켠 상태에서는 `.csproj`의 `PackageReference`에 `Version` 속성을 쓸 수 없다. `dotnet new` 템플릿은 `Version`을 붙여서 만들므로 생성 직후 restore가 NU1008로 실패한다. 오류 메시지가 "복원하지 못했습니다"뿐이라 원인이 드러나지 않는다. 버전은 `Directory.Packages.props`의 `PackageVersion`으로 옮기고 `.csproj`에서는 이름만 남긴다.

모듈과 `Infrastructure`가 호스팅·로깅·라우팅 타입을 쓸 때는 `FrameworkReference Include="Microsoft.AspNetCore.App"`을 쓴다. 공유 프레임워크 참조이며 NuGet 패키지가 아니다. `Microsoft.Extensions.*` 패키지를 개별로 추가하지 않는다.

---

## 시뮬레이션

`MathF.Sin`·`Cos`·`Tan`을 쓰지 않는다. 정확도가 구현에 맡겨져 있어 Unity(IL2CPP)의 libm과 .NET 구현이 마지막 비트에서 갈릴 수 있다. `DeterministicMath`의 다항식 근사를 쓴다. 어겼을 때 증상은 리컨실리에이션 시 떨림으로만 나타난다.

`Vector3.Normalize`·`Length`·`Dot`·`Distance`를 쓰지 않는다. 구현이 SIMD·FMA 경로를 타면 라운딩이 달라진다. `Vector3`는 데이터 컨테이너로만 쓰고 연산은 `DeterministicMath`의 스칼라 구현을 쓴다.

`MathF.Sqrt`·`Floor`·`Abs`는 IEEE 754가 결과를 규정하므로 써도 된다.

서버도 입력을 `MoveIntent.FromInput`으로 역양자화해서 쓴다. 클라이언트가 예측에 쓰는 값은 양자화를 통과한 값이므로, 서버가 원본 부동소수점을 쓰면 양쪽 결과가 갈린다.

이동 함수에 `deltaTime` 파라미터를 두지 않는다. 파라미터가 있으면 호출자가 실제 경과 시간을 넣을 수 있게 되고, 그 순간 재적용 결과가 달라진다. `SimConstants.TickDelta`만 쓴다.

상태 해시에서 `-0.0`을 `0`으로 정규화한다. 비트 패턴이 달라 부호만 다른 0이 다른 해시를 만든다.

AABB 겹침 판정은 등호를 포함하지 않는다. 면이 맞닿은 상태를 겹침으로 보면 바닥에 서 있는 것만으로 매 틱 밀려난다.

이미 겹친 상태에서 스윕하면 진입 시점이 음수로 나와 이동이 그대로 통과한다. 이동 전에 관통이 가장 얕은 축으로 밀어내는 단계를 먼저 거친다.

---

## 모듈 경계

경계 검사는 `.csproj`의 `ProjectReference` 선언을 읽는다. `Assembly.GetReferencedAssemblies()`는 타입을 실제로 쓰지 않으면 참조를 보고하지 않아, 코드가 적은 단계에서 검사가 공허하게 통과한다.

경계 테스트가 실제로 위반을 잡는지 확인할 때 `Infrastructure → Modules/X` 참조만 추가하면 순환 종속이 되어 restore 단계에서 MSB4006으로 먼저 실패한다. 테스트가 실행되지 않으므로 `Modules/X → Infrastructure` 참조를 함께 제거해야 확인된다.

모듈이 0개일 때 공허하게 통과하는 것을 막는 검사를 함께 둔다. 프로젝트 탐색 기준이 바뀌면 모든 경계 검사가 조용히 무력화된다.

룸 커맨드에 세션 객체를 싣지 않고 식별자만 싣는다. 룸이 전송 계층을 알지 않아도 되고, 소켓 없이 룸을 테스트할 수 있다.

---

## 영속화

DB는 아직 없다. 상태는 전부 메모리다.

맵 파일 스키마에 `System.Numerics.Vector3`를 노출하지 않는다. `X`·`Y`·`Z`가 프로퍼티가 아니라 필드라 기본 설정의 `System.Text.Json`이 빈 객체로 직렬화한다. 증상이 "맵이 통째로 사라짐"으로만 나타난다. `MinX`…`MaxZ` 개별 프로퍼티로 두고 변환 메서드를 둔다.

맵을 못 읽으면 기동을 실패시킨다. 빈 콜리전으로 조용히 올라가면 플레이어가 지형을 통과하고 증상이 로직 버그처럼 보인다. `min > max`인 박스도 거부한다. 스윕에서 조용히 무시되어 벽이 사라진 것처럼 된다.

---

## 네트워크

브라우저는 WebSocket 핸드셰이크에 커스텀 헤더를 붙일 수 없다. 프로토콜 버전과 룸은 쿼리스트링으로 받고 업그레이드 전에 검사한다. 헤더로 설계하면 서버 테스트는 통과하고 브라우저에서만 실패한다.

`WebSocket`은 동시 `SendAsync`를 허용하지 않는다. 세션당 송신 채널 하나와 펌프 하나로 직렬화하고, `SendAsync` 호출 지점을 그 펌프 한 곳으로 제한한다.

브라우저는 JS에서 ping 프레임을 보낼 수 없다. 유휴 연결 유지는 서버의 `KeepAliveInterval`이 유일한 수단이다.

WebGL 클라이언트에서 `binaryType`을 `arraybuffer`로 지정하지 않으면 `Blob`으로 수신되어 동기 읽기가 불가능하다.

스냅샷 헤더의 `AckedInputTick`은 수신자마다 다르다. 본문이 같아도 세션별로 인코딩해야 하며, 하나를 만들어 공유할 수 없다.

클라이언트가 보내는 입력 틱 번호의 도약을 제한한다. 큰 값을 그대로 받아들이면 마지막 처리 틱이 튀어 이후 입력이 전부 거부되고 플레이어가 영구히 굳는다.

입력이 끊겼을 때 마지막 입력을 무제한 반복하지 않는다. 입력을 끊은 클라이언트가 계속 달린다.

네트워크 조건 주입기에서 지연을 밀리초에서 틱으로 환산할 때 절삭하지 않고 반올림한다. 30Hz에서 한 틱이 33.3ms이므로 절삭하면 30ms 지터가 0틱이 되어 설정이 조용히 무효가 된다.

손실 주입에서 신뢰 전송은 제외한다. `Welcome`이 사라지면 접속이 그대로 멈춘다.

---

## 문제 해결

30분 이상 걸린 문제만 기록한다. 증상 → 원인 → 대응 순으로 한 항목씩.

*(비어 있음)*
