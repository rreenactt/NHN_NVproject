# Blood Trail 렌더링 최적화 계획

`NVproject/Assets/Scripts/Game/BloodTrail.cs`가 혈흔 하나마다 GameObject를 만드는 구조를,
동작과 시각 품질을 유지한 채 ParticleSystem 기반으로 바꾼다. 규칙은 하나도 바뀌지 않는다 —
이 문서 전체가 presentation 레이어 안의 일이다.

## 1. 현재 구조와 병목 분석

### 생성 경로

출혈은 서버가 결정하고(`EntityFlags.Bleeding`), 클라이언트는 `MatchManager`가 변화 시점에만
`PlayerAgent.SetBleeding`을 불러 `BloodTrail.Begin/Stop`을 구동한다. 혈흔 자체는 와이어에
실리지 않는 순수 로컬 표현이다. 여기까지는 문제가 없고, 문제는 `BloodTrail` 내부에 있다.

`Drop()` 한 번마다 (BloodTrail.cs:142-181):

- `new GameObject("Blood")` + `AddComponent<MeshFilter>` + `AddComponent<MeshRenderer>`
- `new Material(SharedMaterial())` — **마크마다 머티리얼 인스턴스 하나**
- 바닥 스냅용 Raycast 1회 (이건 싸다 — 문제 아님)

`FadeMarks()`는 매 프레임 모든 마크에 대해 (BloodTrail.cs:114-140):

- `material.color` get/set + `HasProperty("_BaseColor")` 문자열 조회 + `SetColor`
- 알파가 변하지 않는 수명의 앞 75% 구간에도 매 프레임 쓴다

### 정량 추정 (GameConfig 기본값 기준)

| 상황 | 계산 | 정상 상태 마크 수 |
|---|---|---|
| 이동 중 (4 m/s) | 4 / `bloodSpacing`(1.1) × `bloodLifetime`(25s) | 러너 1명당 ~91개 |
| 정지 고임 | 1 / `bleedPoolInterval`(0.7) × 25 × `bleedPoolLifetimeScale`(2.5) | 러너 1명당 ~89개 |

방 정원 5명(러너 4명)이 전원 출혈이면 **~360개의 GameObject + 360개의 머티리얼 인스턴스**가
정상 상태다. 각각이 Transform, Renderer, 고유 머티리얼을 가지므로:

- **Draw Call**: 머티리얼이 전부 다르므로 배칭 불가. 마크당 1 드로우. Seeker 카메라 피드
  (`SeekerFeed`)가 켜져 있으면 SeekerVision 레이어 마크는 **두 카메라가 각각 렌더**하므로 2배.
- **CPU**: 매 프레임 마크 수만큼 머티리얼 프로퍼티 쓰기(CBUFFER 재업로드) + `HasProperty`
  문자열 조회. 생성/파괴 시 씬 그래프 갱신 비용.
- **GC**: `Drop()`마다 GO + 컴포넌트 2개 + Material의 관리 힙 할당, `Destroy` 예약 객체 누적.

### 누수 — 장시간 열화의 직접 원인

**`Destroy(mark.transform.gameObject)`는 `new Material(...)`로 만든 인스턴스를 파괴하지
않는다.** `FadeMarks`의 수명 만료 경로(BloodTrail.cs:124)와 `Stop()`(BloodTrail.cs:72) 둘 다
GameObject만 파괴하고 머티리얼을 `Destroy`하지 않으므로, 러너가 출혈 상태로 뛰는 동안
**초당 ~3.6개의 머티리얼이 네이티브 메모리에 영구히 쌓인다.** 관리 힙이 아니라 네이티브
자산이므로 GC가 회수하지 못한다. "장시간 플레이 시 성능이 지속적으로 악화"의 가장 직접적인
원인이 이것이다.

부가적으로: 마크 수에 상한이 없다. 수명이 상한 역할을 하지만, 수명·간격을 튜닝하면 조용히
수백 개까지 자란다.

## 2. 구현 방식 비교

전제: 유지해야 하는 동작 계약이 넷 있다.

1. **레이어 규칙** — 자기 피는 `Default`(모두 렌더), 남의 피는 `SeekerVision`.
   `MatchLayers.BloodLayer`가 클라이언트별로 한 번 결정한다 (BloodTrail.cs:59).
2. **개별 수명** — 달리는 마크 25s, 고임 마크 62.5s가 섞여 있고 각자 독립적으로 페이드.
   페이드 곡선은 "수명의 75%까지 평탄, 마지막 25% 선형 감쇠".
3. **선택적 삭제** — Stop Bleeding 장치는 **그 러너의** 트레일만 지운다 (`Stop()`).
4. **바닥 스냅 + 랜덤 yaw** — 계단·2층에서 마크가 공중에 뜨지 않아야 하고, 마크마다 회전이
   달라야 무늬로 안 읽힌다.

| 방식 | GO 수 | Draw Call | 개별 수명 | 선택 삭제 | 비고 |
|---|---|---|---|---|---|
| A. Object Pooling | 마크 수만큼 (재사용) | 마크당 1 | O | O | 생성 GC만 없앰. 드로우·페이드 비용 그대로. 근본 해결 아님 |
| B. `Graphics.RenderMeshInstanced` 매니저 | **0** | 레이어당 1 | O | O | 최고 효율. 단, 인스턴스별 색을 받는 **커스텀 셰이더 필요** (URP/Unlit은 인스턴스드 `_BaseColor` 미지원) + 매 프레임 행렬/색 배열 관리 코드 |
| C. URP Decal Projector | 마크당 1 | DBuffer 경유 | O | O | GO 문제 그대로 + DecalProjector 컴포넌트가 쿼드보다 비쌈. 시각 품질만 과잉 |
| D. 동적 단일 Mesh (버텍스 컬러) | 레이어당 1 | 레이어당 1 | O | O | 마크 추가/삭제마다 메시 리빌드. Particles/Unlit 셰이더로 버텍스 컬러 사용. 관리 코드가 B와 비슷하게 필요 |
| **E. ParticleSystem (러너당 1)** | **러너당 1** | **출혈 러너당 1** | O (`EmitParams.startLifetime`) | O (`Clear()`) | 시뮬레이션·페이드·수명·상한·정리를 엔진이 전부 대신함. 버텍스 컬러를 읽는 최소 셰이더 하나 필요 (아래) |

### 선정: E — BloodTrail 내부를 ParticleSystem 이미터로 교체

- `ParticleSystem.Emit(EmitParams)`로 마크당 위치·`rotation3D`(90, 랜덤 yaw, 0)·크기·수명·
  색(강도)을 전부 지정할 수 있다 — 현재 `Drop()`의 파라미터가 1:1로 대응된다.
- Render Mode **Mesh(Quad)** + 기존 `SharedMaterial()`(URP Unlit 투명, renderQueue+10 그대로)
  하나를 공유 — 시각적으로 지금과 동일한 쿼드다.
- **Color over Lifetime**이 정확히 현재 페이드 곡선을 대체한다: 커브가 파티클 개별 수명에
  대해 정규화되므로 25s 마크와 62.5s 마크가 각자 "75% 평탄 → 25% 감쇠"로 페이드된다.
  `FadeMarks()`와 매 프레임 머티리얼 쓰기가 **통째로 사라진다**.
- 트레일(=러너)마다 자기 ParticleSystem을 가지므로 `Stop()` = `Clear()`가 그 러너의 마크만
  지운다 — 계약 3이 공짜로 지켜진다. 레이어는 시스템이 붙은 GO의 layer 하나로 결정된다
  (계약 1, 지금과 같은 방식).
- `maxParticles`가 자연스러운 하드 캡이 된다 (계약에 없던 안전장치가 공짜로 생김).
- **셰이더 하나는 직접 쓴다.** 파티클의 색·페이드는 버텍스 컬러로 전달되는데
  `Universal Render Pipeline/Unlit`은 버텍스 컬러를 읽지 않고, 이를 읽는
  `URP/Particles/Unlit`은 이 프로젝트의 어떤 머티리얼도 참조하지 않아 **빌드에서
  스트리핑된다** (`Shader.Find`가 빌드에서 null — 혈흔이 조용히 사라지는 실패).
  따라서 미러 셰이더(`NV/Mirror Surface`)와 같은 방식의 최소 URP HLSL 셰이더를
  `Assets/Resources/`에 둔다 — Resources 안의 셰이더는 항상 빌드에 포함되므로
  `Shader.Find`가 어디서든 성립한다. 쿼드 메시도 수평면(XZ)으로 코드에서 직접 만들어,
  파티클 회전을 yaw 한 축만 쓰게 한다 (오일러 적용 순서 차이로 기울어지는 함정 회피).
- B안(인스턴싱)이 이론상 드로우는 더 적지만(전역 2 vs 러너당 1), 출혈 러너는 최대 4명이라
  차이가 4 드로우 이하다. 커스텀 셰이더 + 수동 배열 관리 코드를 감수할 이득이 없다.
  파티클 방식이 막히면(예: 메시 파티클의 소팅 문제) B가 대체안이다.

## 3. 네트워크(NVserver) 영향

**없음 — 확인 완료.** 혈흔 마크는 와이어에 실리지 않는다. 서버는 `EntityFlags.Bleeding`
비트만 보내고(스냅샷 매 틱), 클라이언트의 `MatchManager.ApplyBody` 폴링이 변화 시점에만
`SetBleeding`을 부른다 (MatchManager.cs:581 — 매 프레임 재시작 방지 주석 참조). 이 계약은
`PlayerAgent.SetBleeding` → `BloodTrail.Begin/Stop` 공개 API 위에 서 있으므로, **API 시그니처를
유지하면 `PlayerAgent`, `MatchManager`, 서버 어느 쪽도 손대지 않는다.** 서버 재빌드도,
프로토콜 변경도, 맵 재수출도 필요 없다.

## 4. 단계별 구현 계획

### Phase 0 — 누수 핫픽스 (독립 커밋, 즉시 효과)

구조 교체와 무관하게 지금 당장 고칠 수 있는 버그:

- `FadeMarks` 만료 경로와 `Stop()`에서 `Destroy(mark.material)` 추가.
- 롤백이 필요해질 경우를 대비해 Phase 1과 분리된 커밋으로.

### Phase 1 — ParticleSystem 교체 (핵심)

`BloodTrail.cs` 내부만 교체. `Begin(PlayerAgent)` / `Stop()` 시그니처와
`GameConfig`의 5개 필드(`bloodSpacing`, `bloodLifetime`, `bleedStillGrace`,
`bleedPoolInterval`, `bleedPoolLifetimeScale`)는 그대로.

- 버텍스 컬러 unlit 투명 셰이더 `NV/Blood Mark`를 `Assets/Resources/Shaders/`에 추가
  (Queue Transparent+10, ZWrite Off — 기존 공유 머티리얼의 설정 그대로).
- `Begin()`에서 자식 GO 하나에 ParticleSystem 구성: 이미션 0(수동 Emit 전용), 시뮬레이션
  스페이스 World, Render Mode Mesh(수평 쿼드), 공유 머티리얼, `maxParticles` 256,
  Color over Lifetime = (α 1.0 @ 0 → 1.0 @ 0.75 → 0 @ 1.0) × 기본 알파 0.85.
  GO layer = `MatchLayers.BloodLayer(...)` (지금과 동일한 결정 시점).
- `Update()`의 낙하 판정 로직(간격·정지 유예·고임 성장)은 **한 줄도 바꾸지 않는다** —
  `Drop()`의 몸통만 `Emit(EmitParams)`로 바뀐다. 바닥 스냅 Raycast와 랜덤 yaw 유지.
- `Stop()` = `Clear()` + 이미션 정지. `_marks` 리스트와 `FadeMarks()` 삭제.
- 도메인 리로드 주의: ParticleSystem 참조는 `UnityEngine.Object` 필드라 리로드를 살아남지만,
  이 프로젝트 관례대로 `Update`에서 null 가드 후 재구성.

### Phase 2 — 검증

클라이언트에는 CLI 빌드·테스트가 없으므로 (NVproject/CLAUDE.md):

1. `dotnet build Assembly-CSharp.csproj`로 컴파일 체크.
2. 에디터 오프라인 플레이 + F5(피격 디버그 키)로 출혈 유발, `Unity_RunCommand`로 측정:
   - `FindObjectsByType` 기준 "Blood" GO 수 → **0**, ParticleSystem 수 → 출혈 러너당 1
   - `Resources.FindObjectsOfTypeAll<Material>()` 카운트가 시간에 따라 **증가하지 않음** (누수 검증)
   - 파티클 수가 정상 상태(~90/러너)에서 수렴하고 `Clear()` 후 0
3. 시각 확인은 사용자 플레이 요청: 달리는 점선, 정지 고임 성장, 마지막 25% 페이드,
   Stop Bleeding 장치의 즉시 삭제, Seeker/Runner 가시성 규칙 4가지.
4. 2클라이언트 빌드로 네트워크 경로 확인: 원격 러너의 트레일이 SeekerVision 레이어에
   실리는지, Seeker 카메라 피드에 보이는지.

### Phase 3 — 후속 (선택)

- `SeekerFeed` 카메라의 컬링 마스크에서 혈흔이 이중 렌더되는 비용은 파티클화로 러너당
  1드로우×2로 줄어든 상태 — 추가 조치 불필요하면 종료.
- `conventions.md`에 "런타임 `new Material`은 만든 쪽이 Destroy한다" 트랩 기록.

## 5. 예상 개선 효과 (출혈 러너 4명, 정상 상태 ~360마크 기준)

| 항목 | 현재 | 개선 후 |
|---|---|---|
| GameObject / Transform | ~360 | 4 (러너당 파티클 GO 1) |
| 머티리얼 인스턴스 | ~360 + **초당 ~14개 누수** | 1 (공유) + 누수 0 |
| Draw Call (혈흔분) | ~360 (피드 카메라 시 최대 2배) | ≤4 (레이어별 시스템 1드로우) |
| 매 프레임 CPU | 마크 수 × (color get/set + HasProperty + SetColor) | 0 (엔진 시뮬레이션) |
| 마크당 GC 할당 | GO + 컴포넌트 2 + Material | 0 (`EmitParams`는 struct) |
| 마크 수 상한 | 없음 | `maxParticles` 하드 캡 |
| 장시간 플레이 | 네이티브 메모리 단조 증가 | 정상 상태 유지 |
