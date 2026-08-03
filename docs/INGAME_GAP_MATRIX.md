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
| R-1.6 | §4.3·§5.1 이동 잠금 | **리빌·종료는 `DONE`** (IG-006·009); 프리즈 장치는 IG-013(BLOCKED), 체인은 IG-016(BLOCKED) | `Match.MovementLocked` + `InputValidator.Neutral`(입력 무력화, 단계 정지가 아니다) + `EntityFlags.Frozen`; 로컬 잠금은 `MatchManager.ApplyMovementLocks` | **서버** ✅ (리빌·종료) | ✅ 스냅샷 플래그 |

## 2. 비대칭 역할

| ID | 기획서 근거 | 현재 상태 | 근거 파일:라인 | 서버 권위 | 동기화 |
|---|---|---|---|---|---|
| R-2.1 | §2.1 술래: 총기 사용 | **판정 `DONE`** (IG-014a) — 발사 자격·탄창·연사 간격이 서버 소유. **HUD 탄약 표시만 남았다**(와이어에 탄약 자리가 없다 → IG-028) | `Room.FireWeapons`, `PlayerEntity.Ammo`, `Match.FireIntervalTicks`; 로컬 `WeaponController` 는 연출 | **서버** ✅ | 탄약은 아직 안 나간다 |
| R-2.2 | §2.1 술래: 출혈 흔적 추적 | `PARTIAL` | `BloodTrail.cs`, `MatchLayers.cs` (`SeekerVision` 레이어 11) | 판정 필요 (출혈 상태) / 표현은 클라이언트 | 필요 (`EntityFlags`) |
| R-2.3 | §2.1 술래: **탈출 문을 볼 수 없음** | **`DONE`** (IG-011b·c3) — 두 경로가 모두 닫혔다: 문 블록이 Seeker 사본에서 빠지고(바이트 확인), 배치 씨드가 와이어에서 사라져 좌표를 **계산할 입력도 없다**(`WireSize` 15→11) | `MessageCodec.WriteObjectiveState`, `RoomStateMessage` | 필요 ✅ | 필요 ✅ |
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
| R-3.1 | §4.1 피격 판정 | **`DONE`** (IG-014a·b·c) — **이 매트릭스가 "가장 심각하다" 고 적었던 경로가 닫혔다** | 서버: `Room.FireWeapons`·`StepProjectiles`·`TryFindVictim`·`ApplyHit`. 클라이언트: `MatchManager.ReportHit` 이 `ServerOwnsCombat` 에서 거부하고 `AcceptCombatState` 로 받는다. **`Bullet` 은 그대로 남아 순수 표현이 됐다** — 삭제가 아니라 무력화다 | **서버** ✅ | ✅ 스냅샷 플래그 + `MatchParticipant.Hits` |
| R-3.2 | §4.1 1회 피격 = 출혈 | **서버 판정 `DONE`** (IG-014b) | `Room.ApplyHit`, `PlayerEntity.Bleeding`(피격 수에서 유도), `EntityFlags.Bleeding`; 클라이언트 적용은 IG-014c | **서버** ✅ | ✅ 스냅샷 플래그(매 틱 — 흔적이 끊기면 안 된다) |
| R-3.3 | §4.1 2회 피격 = 사망 | **서버 판정 `DONE`** (IG-014b) | `Room.DownRunner`, `MatchConstants.RunnerHitsToDie`, **`EntityFlags.Downed`** — `Alive` 를 내리지 않는다(`StateHash` 오염) | **서버** ✅ | ✅ 스냅샷 플래그 + `MatchParticipant.Hits` |
| R-3.4 | §4.1 피격 시 랜덤 위치 순간이동 | **서버 판정 `DONE`** (IG-014b) | `Room.TeleportToRandomFreeFloor` → `MapGrid.TryRandomFreeFloor`; 난수는 배치와 분리된 수열 | **서버** ✅ | ✅ 스냅샷 위치 |
| R-3.5 | (룰셋) 무적 창 0.75초 | **서버 판정 `DONE`** (IG-014b) | `MatchConstants.HitImmunity`, `Match.HitImmunityTicks`(22.5 → **23, 올림**), `PlayerEntity.ImmuneUntilTick` | **서버** ✅ | 불필요 (서버 내부) |
| R-3.6 | §4.3 탄창 3발 | **서버 판정 `DONE`** (IG-014a) — 발사마다 차감하고 비면 거부한다. **재장전은 없다**(체인 경로가 OQ-4 에 막혀 있다 → IG-016) | `PlayerEntity.Ammo`, `Room.FireWeapons`, `MatchConstants.SeekerMagazine`·`FireInterval`, `Match.FireIntervalTicks` | **서버** ✅ | HUD 적용은 IG-014c |
| R-3.7 | §4.3 소진 시 체인 강제이동·3초 행동불가·자동 재장전 | `PARTIAL` | `ChainDrag.cs:1-371`, `ChainAltar.cs` — `NavMesh.CalculatePath` 사용 | 필요 | 필요 |

**~~R-3.1 이 가장 심각하다.~~ 닫혔다 (IG-014a·b·c, 이터레이션 23~25).**

원래 진단: `Bullet` 은 쏜 사람의 머신에서만 날고 `SendMessageUpwards` 는 그 머신의 `PlayerAgent`
에 닿는다. 원격 플레이어의 피격은 각 클라이언트가 자기 사본에 대해 따로 계산하므로, **맞았다고
인정하지 않는 클라이언트가 있으면 그 플레이어는 맞지 않는다.**

지금: 서버가 눈높이에서 총알을 날려(스윕 레이캐스트) 자기 시뮬레이션의 몸과 교차시키고, 결과를
스냅샷 플래그와 매치 전문으로 내려보낸다. `MatchManager.ReportHit` 은 세션이 있으면 거부하므로
클라이언트가 만들 수 있는 피격 판정이 없다. **`Bullet` 은 삭제되지 않았다** — 무력화됐고
오프라인 연습 경로에서 계속 같은 코드로 판정한다.

남은 것은 표현 두 가지다: 서버 총알이 화면에 없어 남의 예광탄이 보이지 않고(IG-028),
히트마커가 로컬 총알로 뜨므로 서버 판정과 어긋날 수 있다.

## 4. 출혈 시스템

| ID | 기획서 근거 | 현재 상태 | 근거 파일:라인 | 서버 권위 | 동기화 |
|---|---|---|---|---|---|
| R-4.1 | §4.2 이동 시 피 흔적 생성 | **상태 `DONE`** (IG-014b·c) — 출혈 여부가 서버 판정이고 매 틱 나간다. 흔적을 그리는 것은 계속 클라이언트다(표현) | 서버 `PlayerEntity.Bleeding`(피격 수 유도) → `EntityFlags.Bleeding`; 클라이언트 `AcceptCombatState` → `BloodTrail`. **값이 바뀔 때만 적용한다** — 매 프레임 부르면 흔적이 매 프레임 재시작해 아무것도 남지 않는다 | **서버**(상태) ✅ | ✅ 스냅샷 플래그 |
| R-4.2 | §4.2 술래가 추적 가능 | `PARTIAL` | `MatchLayers.cs` | 불필요 (표현) | 필요 |
| R-4.3 | §5.1 출혈 제거 (장치) | `PARTIAL` | `MatchManager.cs:425-429`, `DeviceSystem.cs` | 필요 | 필요 |

## 5. 스폰 / 리스폰 / 탈락

| ID | 기획서 근거 | 현재 상태 | 근거 파일:라인 | 서버 권위 | 동기화 |
|---|---|---|---|---|---|
| R-5.1 | §4.1 (사망) 탈락 처리 | **`DONE`** (IG-014b·c) | 서버 `Room.DownRunner` → `EntityFlags.Downed`; 클라이언트 `AcceptCombatState` → `PlayerAgent.Kill()`. **몸을 서버에서 지우지 않는다** — 전멸 판정이 명단을 세어야 하고, 지우면 탈출을 사망으로 셀 수 있다 | **서버** ✅ | ✅ 스냅샷 플래그 |
| R-5.2 | 초기 스폰 | `DONE` | `Room.cs:391-396`, `Room.cs:482-485` | 필요 | 필요 |
| R-5.3 | 리스폰 없음 (2회 피격 = 매치 이탈) | **`DONE`** (IG-014b) — 서버에 리스폰 경로가 없다. `Downed` 는 매치가 다시 시작될 때만 풀린다 | `Room.DownRunner`, `BeginMatch` 의 `player.Downed = false` | **서버** ✅ | ✅ |

## 6. 열쇠 / 탈출

| ID | 기획서 근거 | 현재 상태 | 근거 파일:라인 | 서버 권위 | 동기화 |
|---|---|---|---|---|---|
| R-6.1 | §3 열쇠 10개 수집 | **`DONE`** (IG-012a) | `Room.cs` `PickUpKeys`·`IsWithinPickupRange`, `PlayerEntity.CarriedKeys`, `MatchConstants.KeyPickupHeight`; 클라이언트는 `KeyPickup.cs` 가 폴링을 멈추고 `MatchSync.ApplyCarriedKeys` 로 받는다 | **서버** ✅ | ✅ `MatchState.carriedKeys` + `ObjectiveState` 열쇠 목록 |
| R-6.2 | §3·§6 탈출 문에 삽입 | **`DONE`** (IG-012b1·b2·b3) | `Room.cs` `InsertKeys`·`IsWithinDoorRange`, `Match.InsertKey`, `ButtonFlags.Interact`, `MatchConstants.InteractHeight`; 클라이언트는 `MatchManager.AcceptObjectiveProgress` 로 받고 `TryInsertKey` 는 `ServerOwnsObjectives` 에서 거부한다 | **서버** ✅ | ✅ `MatchState.keysInserted` |
| R-6.3 | §6 문은 랜덤 위치 생성 | **`DONE`** (IG-011a·b·c2) — 서버가 배치하고 좌표를 역할별로 걸러 내려보내며 클라이언트가 그것을 받아 그린다 | `ObjectivePlacement`, `WriteObjectiveState`, `MatchManager.AcceptObjectiveState` | 필요 ✅ | 필요 ✅ |
| R-6.4 | §6 플레이어만 볼 수 있음 | **`DONE`** (IG-011b·c3) — R-2.3 과 같은 경로로 닫혔다 | `WriteObjectiveState`, `RoomStateMessage` | 필요 ✅ | 필요 ✅ |
| R-6.5 | §6 열쇠 10개 삽입 시 개방 | **`DONE`** (IG-012b2·b3) | `Match.DoorOpen`(삽입 수에서 유도), `WriteObjectiveState` 의 `doorOpen` — **문 블록 안에 있어 Seeker 사본에는 실리지 않는다**; 클라이언트는 `NetworkClient.ObjectiveDoorOpen` → `AcceptObjectiveProgress` 로 매 프레임 멱등하게 적용한다 | **서버** ✅ | ✅ `ObjectiveState` 의 문 블록 |
| R-6.6 | §3 2명 이상 탈출 시 승리 | **탈출 감지 `DONE`** (IG-012c1·c2); **승리 판정은 BLOCKED** (IG-007 ← OQ-2·OQ-6) | `Room.TickEscapes`, `Match.Escapes`·`EscapeHoldTicks`, `EntityFlags.Escaped`; 클라이언트는 `AcceptEscapes`(수, 전문) + `AcceptEscaped`(대상, 스냅샷 플래그)로 받고 로컬 `TickEscapes` 는 거부한다 | **서버** ✅ (세는 것) | ✅ `MatchState.escapes` — **Seeker 도 받는다** |
| R-6.7 | (룰셋) 사망 시 소지 열쇠 흘리기 | **서버 판정 `DONE`** (IG-014b) — **사망 지점 한 점**에 놓는다(흩뿌리기는 표현이고 반경이 기획서에 없다 → IG-027) | `Room.DownRunner` → `Objectives.AddKey` ×소지 수 | **서버** ✅ | ✅ `ObjectiveState` 열쇠 목록 |

## 7. 맵 이벤트 (장치)

| ID | 기획서 근거 | 현재 상태 | 근거 파일:라인 | 서버 권위 | 동기화 |
|---|---|---|---|---|---|
| R-7.1 | §5 장치 8~9개 배치 | **`DONE`** (IG-011a·b·c2) — 효과 6종 전부 + 다회용 중복, 서버가 배치하고 클라이언트가 받는다. 개별 장치 **효과**는 별개(IG-013, OQ-1 차단) | `ObjectivePlacement.PlaceDevices`, `MatchConstants.DeviceMix` | 필요 ✅ | 필요 ✅ |
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
| R-9.1 | §3 열쇠 진행도 표시 (술래에게 숨김) | **`DONE`** (IG-012b3) — 값이 서버에서 오고 **코덱이 Seeker 사본에서 0 으로 만든다.** 트리 재구성은 그대로라 이중으로 막힌다 | `MatchState.keysInserted` → `AcceptObjectiveProgress` → `KeysChanged` → `GameHudController` | **서버** ✅ | ✅ 역할별 필터가 코덱 안에 |
| R-9.2 | §8 탈출 수 / 남은 시간 | **`DONE`** (IG-010·012c2) | `MatchState` 의 시계와 `escapes` → `AcceptMatchState`·`AcceptEscapes`. 시계는 전문 사이를 로컬 카운트다운으로 메우고 매 전문이 덮어쓴다 | **서버** ✅ | ✅ |
| R-9.3 | §4.3 탄약 표시 | `PARTIAL` — **탄약 판정은 서버**(IG-014a)인데 **와이어에 탄약 자리가 없어** HUD 가 로컬 `WeaponController` 값을 그린다 | `PlayerEntity.Ammo`(서버) vs `WeaponController._ammo`(HUD). `MatchParticipant` 에 `hits` 는 있고 탄약은 없다 → IG-028 | **서버** ✅ (판정) | ❌ 아직 안 나간다 |

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

---

# 최종 리포트 (이터레이션 27, 재검증 완료)

**위 문장은 부트스트랩 시점의 진단이고 지금은 뒤집혔다.** 서버 권위가 필요한 항목 중 **판정이
아직 클라이언트에 남은 것은 하나(승리 조건)뿐**이며, 그것은 기획서와 구현이 충돌해 답을 기다리는
중이다(OQ-2·OQ-6).

## 재검증 방법

이 리포트를 쓰기 전에 문서의 주장을 코드로 다시 확인했다 — 서버의 판정 지점 10곳이 실제로 있는지
(`PickUpKeys`·`InsertKeys`·`TickEscapes`·`FireWeapons`·`StepProjectiles`·`TryFindVictim`·
`ApplyHit`·`DownRunner`·`ProjectWire`·`Match.Advance`), 그리고 클라이언트의 권위 게이트가 문서가
주장하는 자리에 있는지.

**구멍 하나가 나왔다.** `MatchManager.TryPickUpKey` 에 게이트가 없었다 — 살아 있는 호출 경로는
이미 막혀 있었지만(`KeyPickup.Update` 가 폴링을 멈춘다) `public` 함수 자체는 거부하지 않았고,
`NVproject/CLAUDE.md` 는 거부한다고 적고 있었다. 이 이터레이션에서 게이트를 추가했다.

## 서버 권위로 옮겨진 것 (전부 자동 테스트로 고정)

| 계통 | 규칙 | 태스크 |
|---|---|---|
| 매치 진행 | 단계 전이·시계(고정 틱)·역할 공개·이동 잠금 | IG-006·009·010 |
| 목표물 | 배치(제단·문·열쇠·장치), **역할별 좌표 필터** | IG-011a·b·c |
| 열쇠 | 습득, 삽입, 문 개방 | IG-012a·b1·b2·b3 |
| 탈출 | 문간 유지 판정, 탈출 수 | IG-012c1·c2 |
| 전투 | 발사 자격·탄창·연사, 발사체 비행(스윕), 피격, 출혈, 순간이동, 사망, 열쇠 흘리기, 무적 창 | IG-014a·b·c |

**정보 규칙이 함께 옮겨졌다.** Seeker 사본에서는 열쇠 진행도와 소지 수가 0 이고 **문 블록이 아예
빠진다.** 필터가 코덱 안에 있어 호출부가 우회할 수 없고, 배치 씨드를 와이어에서 빼서 **계산
가능성까지** 닫았다 — 이것이 이 루프에서 가장 값이 컸던 발견이다(R-2.3).

## DEFERRED / 미완 (사유 포함)

| 항목 | 상태 | 사유 |
|---|---|---|
| 승리 조건 (R-1.5) | **BLOCKED** | OQ-2·OQ-6. 서버가 탈출·피격·열쇠를 다 세지만 **결과 코드를 정하지 않는다** — 방장이 판정해 `Control(EndMatch)` 로 중계하는 한시적 경로가 남아 있다 |
| 재장전 (R-3.7) | **미완** | 기획서 §4.3 의 재장전은 체인이 놓아준 뒤다. 체인 경로가 OQ-4 로 막혀 순서를 임의로 정하지 않았다 — **탄창 3발을 비우면 그 매치에서 더 쏠 수 없다** |
| 장치 6종 (R-7.2~7.8) | **BLOCKED** | OQ-1. 기획서 §5.2 와 룰셋·구현이 순간이동 장치의 소유자를 다르게 말한다 |
| 근접 보이스 (R-8.1~8.3) | **BLOCKED** | OQ-3. 전체 미구현 영역이고 §7.4 는 기획서가 옵션으로 표시 |
| 탄약 HUD (R-9.3) | **미완** | 판정은 서버인데 와이어에 탄약 자리가 없다 → IG-028 |
| 서버 총알 렌더 | **미완** | 각자 자기 총알만 그린다. 남의 예광탄이 보이지 않고 히트마커가 서버 판정과 어긋날 수 있다 → IG-028 |
| 클라이언트 예측 | **DEFERRED** | 애초에 없었다(AS-8). §8 은 예측을 허용하지만 요구하지 않는다 → IG-023 |
| EditMode 테스트 | **미완** | Unity asmdef 가 `Assembly-CSharp` 를 참조할 수 없어 스크립트 전체 이관이 선행된다 → IG-018 (ADR 필요) |

## 알려진 제약

- **클라이언트 적용 경로에 자동 테스트가 없다.** IG-012b3·c2·IG-014c 는 컴파일과 서버 회귀만으로
  검증됐다. 실질 검증은 2클라이언트 스모크이고 **한 번도 실행되지 않았다** —
  `LOOP_PROGRESS.md` 의 "사람이 해야 하는 검증" 표가 그 목록이다.
- `EntityFlags` 8비트를 전부 썼다. 매 틱 보낼 상태를 더 추가하려면 무엇이 정말 매 틱 필요한지
  다시 봐야 한다.
- 룸 스폰이 2개뿐이고 2m 간격이라 **3인 이상 매치를 서버 테스트로 재현할 수 없다.** "술래는 총을
  맞지 않는다" 는 그래서 실사격으로 검사되지 않았다(코드에는 있다).
- 오프라인 연습 경로가 같은 규칙을 자기 코드로 판정한다. 의도된 것이지만 **두 구현이 갈릴 수
  있는 자리**이고, 값은 `MatchConstants` 로 공유해 그 위험을 줄였다.

## 남은 위험

1. **스모크 미실행이 가장 큰 위험이다.** 서버 판정은 테스트로 고정됐지만 그것이 **화면에 도달하는지**
   는 확인되지 않았다. 적용 경로의 버그는 컴파일을 통과한다.
2. 프로토콜 3 이후 **서버와 클라이언트를 같은 커밋에 배포해야 한다.** 구버전 클라이언트는 426 으로
   전부 거절되고 WebGL 빌드는 수 분이 걸린다.
3. `Downed`·`Escaped` 몸이 서버에 남아 계속 시뮬레이션된다(투명하게 걸어다닌다). 판정에서는
   제외되지만 **의도된 상태인지 확인이 필요하다.**
4. 승리 조건이 방장 판정으로 남아 있어, 방장이 조작된 클라이언트면 결과를 바꿀 수 있다. IG-007 이
   닫힐 때까지 유효한 구멍이다.
