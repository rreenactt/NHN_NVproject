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

## 시뮬레이션

*(비어 있음)*

---

## 모듈 경계

*(비어 있음 — 참조 규칙은 `architecture.md`, 폴더 규칙은 `structure.md`)*

---

## 영속화

*(비어 있음)*

---

## 네트워크

*(비어 있음)*

---

## 문제 해결

30분 이상 걸린 문제만 기록한다. 증상 → 원인 → 대응 순으로 한 항목씩.

*(비어 있음)*
