# INGAME GAP MATRIX — 기획서 ↔ 현재 구현

작성: 2026-08-03 (부트스트랩)
기준 커밋: `1817839`

기획서는 `docs/asymmetric_tag_shooter_game_design.md` (148줄). 상태 판정은 **서버 권위로 동작하는가**
를 기준으로 한다 — 클라이언트에만 있으면 `PARTIAL` 이다. 기획서가 요구하는 것은 "게임이 되는 것"
이고, 클라이언트 전원이 각자 판정하는 규칙은 두 명이 붙는 순간 서로 다른 게임이 된다.

## 판정 기준

| 상태 | 뜻 |
|---|---|
| `NONE` | 코드가 없다 |
| `PARTIAL` | 클라이언트에 구현되어 있으나 서버가 판정하지 않는다 (= 치팅 가능 / 클라이언트 간 불일치 가능) |
| `DONE` | 서버가 판정하고 클라이언트가 받아 표시한다 |

**중요:** 아래 `PARTIAL` 대부분은 "덜 만들어진 것" 이 아니라 **잘못된 자리에 완성되어 있는 것** 이다.
`MatchManager` 는 750줄짜리 완성된 심판이며, 문제는 그것이 클라이언트 전원에서 각자 한 벌씩
돈다는 점이다(`MatchManager.cs:12-20` 의 클래스 주석이 스스로 지적한다). 그래서 이 매트릭스의
작업량은 "구현" 이 아니라 대부분 "이관" 이다.

---

## 1. 매치/라운드 상태 머신

| ID | 기획서 근거 | 현재 상태 | 근거 파일:라인 | 서버 권위 | 동기화 |
|---|---|---|---|---|---|
| R-1.1 | §2·§3 (매치 성립) 룸 단계 Waiting/Playing/Ended | `DONE` | `Room.cs:188-206`, `RoomPhase.cs` | 필요 | 필요 (`RoomState` 2Hz) |
| R-1.2 | §2 역할 배정 (술래 1명) | `DONE` | `Room.cs:580-592` (`PickSeeker`) | 필요 | 필요 |
| R-1.3 | §3·§8 매치 내부 단계 (RoleReveal→Playing→Ended) | **`DONE`** (IG-006·008·010) | 서버 `Match.cs`, `EventKind.MatchState`, `MatchManager.AcceptMatchState` | 필요 ✅ | 필요 ✅ |
| R-1.4 | §8 매치 시계 | **`DONE`** (IG-006·008·010) | 서버가 고정 틱으로 세고 2Hz 전문으로 내려보낸다. 클라이언트는 전문 사이만 로컬로 메운다 | 필요 ✅ | 필요 ✅ |
| R-1.6 | §4.3·§5.1 이동 잠금 — **리빌·종료는 `DONE`**, 프리즈 장치는 IG-013(OQ-1 차단), 체인은 IG-016(OQ-4 차단) | 서버가 입력을 무력화하고(`Room.StepPlayer`, `Match.MovementLocked`) `EntityFlags.Frozen` 을 보낸다. 클라이언트는 서버 위치를 따르므로 별도 반영이 **불필요**하다 — 예측이 없다(AS-8, IG-022 DEFERRED) | 필요 ✅ | 필요 ✅ |
| R-1.5 | §3·§8 승패 결정 | `PARTIAL` | `MatchManager.cs:330-348` — 방장만(`ResolvesOutcome`), `ControlKind.EndMatch` 로 중계 | 필요 | 필요 |
| R-1.6 | §4.3·§5.1 이동 잠금 (리빌·프리즈·체인·종료) | `PARTIAL` | `MatchManager.cs:526-531` | 필요 | 필요 |

## 2. 비대칭 역할

| ID | 기획서 근거 | 현재 상태 | 근거 파일:라인 | 서버 권위 | 동기화 |
|---|---|---|---|---|---|
| R-2.1 | §2.1 술래: 총기 사용 | `PARTIAL` | `WeaponController.cs`, `PlayerRoleLoadout.cs` — 탄약이 클라이언트 소유 | 필요 | 필요 |
| R-2.2 | §2.1 술래: 출혈 흔적 추적 | `PARTIAL` | `BloodTrail.cs`, `MatchLayers.cs` (`SeekerVision` 레이어 11) | 판정 필요 (출혈 상태) / 표현은 클라이언트 | 필요 (`EntityFlags`) |
| R-2.3 | §2.1 술래: **탈출 문을 볼 수 없음** | `PARTIAL` — 컬링 레이어로만 가림 | `MatchLayers.cs`, `RoomStateMessage.cs:PlacementSeed` | 필요 (좌표를 내려보내지 않아야) | 필요 (역할별 필터) |
| R-2.4 | §2.2 플레이어: 무기 없음 | `PARTIAL` | `PlayerRoleLoadout.cs` | 필요 | 필요 |
| R-2.5 | §2.1 술래: 일부 맵 이벤트 전용 사용 | **모순** — 구현은 술래 장치 사용 금지 | `GameConfig.asset:seekerCanActivateDevices: 0` | — | — |

**R-2.3 이 이 프로젝트의 대표적 구멍이다.** 문은 `RunnerVision` 레이어로 술래의 카메라에서 빠지지만,
배치는 `PlacementSeed` 를 받아 **모든 클라이언트가 같은 씨드로 계산**하므로 술래의 프로세스
메모리에 문 좌표가 들어 있다. WebGL 빌드는 디컴파일된다는 전제(`architecture.md`) 위에서 카메라
마스크로 막을 수 있는 종류의 정보가 아니다. **씨드 공유 방식으로는 닫히지 않는다** — 서버가
배치하고 역할별로 걸러 좌표를 내려보내야 한다.

## 3. 태그(술래) 판정 = 사격·히트

기획서에 접촉 태그는 없다. 술래의 판정 수단은 총기다(§2.1, §4.1).

| ID | 기획서 근거 | 현재 상태 | 근거 파일:라인 | 서버 권위 | 동기화 |
|---|---|---|---|---|---|
| R-3.1 | §4.1 피격 판정 | `PARTIAL` — **쏜 클라이언트가 판정** | `Bullet.cs` → `SendMessageUpwards("OnHit")` → `PlayerAgent.OnHit` | 필요 | 필요 |
| R-3.2 | §4.1 1회 피격 = 출혈 | `PARTIAL` | `MatchManager.cs:415-422` | 필요 | 필요 |
| R-3.3 | §4.1 2회 피격 = 사망 | `PARTIAL` | `MatchManager.cs:399-413` | 필요 | 필요 |
| R-3.4 | §4.1 피격 시 랜덤 위치 순간이동 | `PARTIAL` | `MatchManager.cs:436-447` | 필요 | 필요 |
| R-3.5 | (룰셋) 무적 창 0.75초 | `PARTIAL` | `MatchManager.cs:396`, `GameConfig.asset:hitImmunity 0.75` | 필요 | 불필요 (서버 내부) |
| R-3.6 | §4.3 탄창 3발 | `PARTIAL` | `WeaponController.cs`, `GameConfig.asset:seekerMagazine 3` | 필요 | 필요 (HUD) |
| R-3.7 | §4.3 소진 시 체인 강제이동·3초 행동불가·자동 재장전 | `PARTIAL` | `ChainDrag.cs:1-371`, `ChainAltar.cs` — `NavMesh.CalculatePath` 사용 | 필요 | 필요 |

**R-3.1 이 가장 심각하다.** `Bullet` 은 쏜 사람의 머신에서만 날고 `SendMessageUpwards` 는 그
머신의 `PlayerAgent` 에 닿는다. 원격 플레이어의 피격은 각 클라이언트가 자기 사본에 대해 따로
계산하므로, **맞았다고 인정하지 않는 클라이언트가 있으면 그 플레이어는 맞지 않는다.**

## 4. 출혈 시스템

| ID | 기획서 근거 | 현재 상태 | 근거 파일:라인 | 서버 권위 | 동기화 |
|---|---|---|---|---|---|
| R-4.1 | §4.2 이동 시 피 흔적 생성 | `PARTIAL` (표현) | `BloodTrail.cs:1-197` | 상태만 | 필요 (`EntityFlags.Bleeding`) |
| R-4.2 | §4.2 술래가 추적 가능 | `PARTIAL` | `MatchLayers.cs` | 불필요 (표현) | 필요 |
| R-4.3 | §5.1 출혈 제거 (장치) | `PARTIAL` | `MatchManager.cs:425-429`, `DeviceSystem.cs` | 필요 | 필요 |

## 5. 스폰 / 리스폰 / 탈락

| ID | 기획서 근거 | 현재 상태 | 근거 파일:라인 | 서버 권위 | 동기화 |
|---|---|---|---|---|---|
| R-5.1 | §4.1 (사망) 탈락 처리 | `PARTIAL` | `PlayerAgent.cs` `SetPresent(false)`, `MatchManager.cs:406` | 필요 | 필요 |
| R-5.2 | 초기 스폰 | `DONE` | `Room.cs:391-396`, `Room.cs:482-485` | 필요 | 필요 |
| R-5.3 | 리스폰 없음 (2회 피격 = 매치 이탈) | `PARTIAL` | `MatchManager.cs:399-413` | 필요 | 필요 |

## 6. 열쇠 / 탈출

| ID | 기획서 근거 | 현재 상태 | 근거 파일:라인 | 서버 권위 | 동기화 |
|---|---|---|---|---|---|
| R-6.1 | §3 열쇠 10개 수집 | `PARTIAL` | `KeyPickup.cs:1-121` (거리 폴링), `MatchManager.cs:455-468` | 필요 | 필요 |
| R-6.2 | §3·§6 탈출 문에 삽입 | `PARTIAL` | `MatchManager.cs:475-503` | 필요 | 필요 |
| R-6.3 | §6 문은 랜덤 위치 생성 | `PARTIAL` → **서버가 배치한다**(IG-011a). 좌표를 내려보내는 것은 IG-011b, 클라이언트가 받는 것은 IG-011c | `MatchRules.PlaceObjectives` | 필요 ✅ | 필요 (역할별 필터) |
| R-6.4 | §6 플레이어만 볼 수 있음 | `PARTIAL` | R-2.3 과 같은 구멍 | 필요 | 필요 |
| R-6.5 | §6 열쇠 10개 삽입 시 개방 | `PARTIAL` | `MatchManager.cs:493-497` | 필요 | 필요 |
| R-6.6 | §3 2명 이상 탈출 시 승리 | `PARTIAL` | `MatchManager.cs:296-328` (문간 0.8초 유지) | 필요 | 필요 |
| R-6.7 | (룰셋) 사망 시 소지 열쇠 흘리기 | `PARTIAL` | `MatchManager.cs:703-722` | 필요 | 필요 |

## 7. 맵 이벤트 (장치)

| ID | 기획서 근거 | 현재 상태 | 근거 파일:라인 | 서버 권위 | 동기화 |
|---|---|---|---|---|---|
| R-7.1 | §5 장치 8~9개 배치 | `PARTIAL` → **서버가 배치한다**(IG-011a, 효과 6종 전부 + 다회용 중복). 좌표는 IG-011b·c | `MatchRules.PlaceDevices`, `RealtimeConstants.Match.DeviceMix` | 필요 ✅ | 필요 |
| R-7.2 | §5.1 시간 증가 (1회) | `PARTIAL` | `MatchManager.cs:506-510`, `MatchEnums.cs:AddTime` | 필요 | 필요 |
| R-7.3 | §5.1 전체 위치 공개 (다회) | `PARTIAL` | `MatchMapView.cs`, `MatchEnums.cs:FullMapView` | 발동 판정만 | 필요 |
| R-7.4 | §5.1 출혈 제거 (1회) | `PARTIAL` | `MatchEnums.cs:StopBleeding` | 필요 | 필요 |
| R-7.5 | §5.1 전체 정지 + 벽 투명화 (1회) | `PARTIAL` | `MatchManager.cs:514-518`, `MatchEnums.cs:FreezeAndXray` | 정지는 서버 / 투명은 표현 | 필요 |
| R-7.6 | §5.1 술래 시점 보기 (다회) | `PARTIAL` | `SeekerFeed.cs`, `MatchEnums.cs:SeekerCameraView` | 발동 판정만 | 필요 |
| R-7.7 | §5.2 1:1 순간이동, 쿨타임 12초 | **모순** (OQ-1) | `MatchEnums.cs:Teleport`, `GameConfig.asset:teleportSharedCooldown 12` | 필요 | 필요 |
| R-7.8 | §5.3 술래가 장치 공격, 4발 명중 시 파괴 | `PARTIAL` | `MapDevice.cs` `OnHit`, `GameConfig.asset:deviceDestroyHits 4` | 필요 | 필요 |

## 8. 보이스 시스템 (근접 음성 채팅)

| ID | 기획서 근거 | 현재 상태 | 근거 파일:라인 | 서버 권위 | 동기화 |
|---|---|---|---|---|---|
| R-8.1 | §7.1 거리 기반 음성 채팅 | `NONE` | 없음 (`microphone`/`webrtc`/`opus` 전체 grep 0건) | 릴레이 필요 | 필요 |
| R-8.2 | §7.2 술래도 청취 가능 / 위치 노출 | `NONE` | 없음 | — | — |
| R-8.3 | §7.4 속삭임·외침·사망자 채팅 분리 (옵션) | `NONE` | 없음 — 기획서가 **옵션**으로 표시 | — | — |

**§7 은 다른 항목과 성격이 다르다.** 나머지는 "이관" 이지만 이것은 처음부터 만드는 일이고,
현재 아키텍처(`System.Net.WebSockets` 원시 사용, NuGet 금지, 모듈 추가 시 확인 필요)에서
실시간 음성은 새 전송 계층을 요구한다. OQ-3 으로 올린다.

## 9. HUD / 피드백 (인게임 로직 종속만)

| ID | 기획서 근거 | 현재 상태 | 근거 파일:라인 | 서버 권위 | 동기화 |
|---|---|---|---|---|---|
| R-9.1 | §3 열쇠 진행도 표시 (술래에게 숨김) | `PARTIAL` | `GameHudController.cs:1-736` — 역할별 트리 재구성 | 값은 서버 | 필요 (역할별 필터) |
| R-9.2 | §8 탈출 수 / 남은 시간 | `PARTIAL` | `GameHudController.cs` | 값은 서버 | 필요 |
| R-9.3 | §4.3 탄약 표시 | `PARTIAL` | `GameHudController.cs`, `WeaponController.cs` | 값은 서버 | 필요 |

**클라이언트 이벤트 목록이 이미 replication 계약이다** — `PhaseChanged`, `KeysChanged`,
`EscapesChanged`, `RolesAssigned`, `MatchEnded`, `AgentHit`, `Notified`
(`MatchManager.cs:45-51`). HUD·`PlayerRoleLoadout` 이 이 이벤트를 구독하므로, 서버 전문을
받아 같은 이벤트를 발화시키면 **HUD 는 손대지 않아도 된다.** 이 설계가 여기서 값을 한다.

---

## 10. 선행 차단 요소 (기획서 항목이 아니지만 전부를 막는다)

| ID | 문제 | 근거 | 영향 | 상태 |
|---|---|---|---|---|
| R-0.1 | 씬의 맵 이름과 서버 등록 맵이 어긋난다 | `BackroomsMapGenerator.cs:113` → `"backrooms2f"`, `SessionSceneRouter.SceneByMap` 은 `"backrooms"` → `SampleScene` | 로비를 통해 기본 맵으로 방을 만들면 **접속마다 맵 해시 불일치 확정** | **해소** (IG-001) |
| R-0.2 | `MapData/backrooms.json` 은 레거시 export | 박스 1367개·범위 ±89.6m(56셀×3.2m) vs 현재 씬 지형 736박스·±52.50m(35셀×3m·2층) | 같음 | **해소** (IG-001) |
| R-0.3 | 서버가 "여기 설 수 있는가" 를 답할 수 없다 | `MapData` 는 AABB 박스 + 스폰 8개만 알았다 | 목표물 배치·피격 순간이동 지점 선정 불가 → R-3.4·R-6.x·R-7.1 전부 차단 | **해소** (IG-002·IG-003·IG-004) |

R-0.1·R-0.2 는 `conventions.md` 가 이미 경고한 두 항목("씨드·격자·벽 두께를 바꾸면 export 를
다시 돌린다", "등록되지 않은 맵 id 는 거절한다")이 겹쳐 걸린 상태였다.

**IG-001 (2026-08-04) 로 해소.** `MapName` 을 `"backrooms"` 로 통일하고 export 를 재실행했다.
`default` → `backrooms.json`(736박스) → `WorldMap.Name = "backrooms"` → 라우터 → `SampleScene`
→ 생성기 `"backrooms"` 로 체인이 닫혔고, 런타임 `Generate()` / export `ComputeCollision()` /
서버 파일 로드가 **모두 736박스로 실측 일치**했다.
접속 시 해시 `일치` 로그의 실측만 남아 있고 IG-010 의 스모크 테스트에서 함께 확인한다.

**R-0.3 은 IG-002·IG-003 으로 해소.** 서버는 이제 `backrooms` 의 격자를 갖는다 — 2층 35×35 =
2450셀, `Standable` 583, `FreeFloor` 574, `StairLink` 30. `FreeFloor` 는 **서버 자신의 플레이어
박스**로 판정되므로(`MapGridBuilder`) 그 플래그가 통과시킨 자리는 시뮬레이션도 통과한다.
맵 해시는 `3B4B1D41` → `7996AF3A` 로 바뀌었고, 격자를 내놓지 않는 `test-room` 은 `27A9412D` 로
그대로다.

**IG-004 로 질의까지 올라갔다.** `WorldMap.Grid` 가 무작위 `FreeFloor` 선택
(`TryRandomFreeFloor(ref DeterministicSequence, …)`)과 같은 층 최근접 탐색
(`TryNearestFreeFloor`)을 제공하고, 실제 `backrooms` 격자에서 500회 무작위 질의와 스폰 8곳
최근접 탐색이 **서버 자신의 충돌 코드로 검산**됐다. 격자가 없는 맵의 `Grid` 는 `null` 이라
호출자가 "후보 0개" 와 "격자 없음" 을 구분할 수 있다. R-3.4(피격 시 순간이동)·R-6.3(문 배치)·
R-7.1(장치 배치)이 이제 기술적으로 가능하다.

---

## 집계

| 상태 | 개수 |
|---|---|
| `DONE` | 3 (R-1.1, R-1.2, R-5.2) |
| `PARTIAL` | 33 |
| `NONE` | 3 (R-8.1~R-8.3, 보이스) |
| 모순 → `OPEN_QUESTIONS` | 2 (R-2.5/R-7.7 은 같은 건 = OQ-1) |
| 선행 차단 | 3 중 **3개 전부 해소** (R-0.1·R-0.2 → IG-001, R-0.3 → IG-002·IG-003) |

**서버 권위가 필요한 항목 31개 중 3개만 서버에 있다.** 나머지는 클라이언트에 완성되어 있으나
잘못된 자리에 있다.
