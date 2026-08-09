# Client WebGL 빌드·배포 자동화 계획

[deploy-plan.md](deploy-plan.md)(서버 배포)의 자매 문서. 같은 OCI Free Tier 인스턴스에서 API 서버와 WebGL 사이트를 함께 운영하기 위한 **클라이언트 빌드·배포 워크플로우**의 설계와 구현 순서를 적는다.

## 원칙

- **서버 배포 구조는 건드리지 않는다.** `/opt/nvserver`, `nvserver.service`, `server-deploy.yml`, 기존 Secrets 는 한 줄도 바뀌지 않는다. 클라이언트는 그 옆에 같은 패턴(릴리스 디렉터리 + 심볼릭 링크)으로 놓인다.
- **빌드는 전부 GitHub 러너에서 한다.** 프리티어 인스턴스는 서빙만 한다 — Unity 빌드는 수십 분짜리 CPU 작업이고, 그것을 인스턴스에 시키는 순간 게임 서버의 틱 루프와 경쟁한다.
- **정적 파일에는 서비스가 없다.** WebGL 사이트는 데몬이 아니라 디렉터리다. 재시작할 것이 없으므로 배포·롤백은 심볼릭 링크 전환으로 끝나고, 웹 서버는 이미 있는 Caddy 가 겸한다.
- **서버에는 Docker 가 없다.** GameCI 의 Unity 실행이 Docker 이미지를 쓰는 곳은 **GitHub 러너 안**뿐이다. OCI 인스턴스에 배포되는 것은 정적 파일 디렉터리 하나이고, 컨테이너 런타임은 어느 단계에도 설치되지 않는다 — 기존 서버 운영(systemd 직접 실행)과 같은 원칙이다.
- **코드는 한 줄도 새로 쓰지 않는다.** CI 는 기존 메뉴 **`Tools ▸ NV ▸ Build Production (WebGL)`** 의 메서드(`NV.Client.EditorTools.BuildMenu.BuildProductionWebGl`)를 `-executeMethod` 로 그대로 부른다. 에디터에서 뽑는 빌드와 CI 가 뽑는 빌드가 **같은 메서드의 산출물**이므로 두 경로가 다른 빌드를 만들 수 없고, "로컬에서는 됐다" 가 성립할 자리가 없다. 새로 쓰는 것(워크플로 YAML, 배포 셸 스크립트, Caddy 블록)은 코드가 아니라 인프라 파일이다.

## 1. 현재 서버 배포 구조 분석

| 구성 요소 | 현재 상태 | 클라이언트 배포가 재사용하는 것 |
|---|---|---|
| `server-ci.yml` | PR/main 푸시에 `dotnet build` + `dotnet test` | 패턴만. 클라이언트는 CLI 테스트가 없어 빌드 자체가 게이트다 |
| `server-deploy.yml` | main 푸시(NVserver 변경) → 테스트 → self-contained 게시 → tar → scp → 서버의 `deploy.sh` 실행. `workflow_dispatch` 로 재배포/롤백 | SSH 구성 스텝, `environment: production`, concurrency 직렬화, `rollback_to` 입력 — 전부 같은 모양으로 복제 |
| `deploy.sh` | `/opt/nvserver/releases/<ts>` 전개 → `current` 심링크 전환 → systemd 재시작 → health check, 실패 시 자동 롤백, 릴리스 5개 유지, 빈 서버 부트스트랩 | 구조 전체. 클라이언트판은 "재시작 + health check" 가 "정적 검증" 으로 줄어든 축소판이다 |
| Caddy | TLS 종단 + `reverse_proxy 127.0.0.1:5202`. 설치·도메인은 수동 관리 | 인스턴스 하나, Caddy 하나. 사이트 블록만 추가한다 |
| Secrets | `DEPLOY_HOST`, `DEPLOY_SSH_KEY` (+선택 `DEPLOY_USER`, `DEPLOY_KNOWN_HOSTS`) | 그대로 재사용 — 같은 서버, 같은 계정 |
| 도메인 | `api.nhn-backroom.kro.kr` 가 API. `appsettings.Production.json` 의 CORS 에 `play.nhn-backroom.kro.kr` 가 **이미 등록**되어 있고, `production.asset` 도 host=api, secure=1 로 완성되어 있다 | 남은 것은 `play` 서브도메인의 DNS 레코드와 Caddy 블록뿐이다 |

즉 서버 쪽 준비는 사실상 끝나 있다. 새로 만드는 것은 **① 클라이언트 배포 스크립트, ② 워크플로 하나, ③ Caddy 블록 하나**다 — 빌드 자체는 기존 메뉴 `Tools ▸ NV ▸ Build Production (WebGL)` 의 메서드를 CI 가 그대로 실행하므로 새 빌드 코드가 없다.

## 2. Unity WebGL 빌드 — GameCI 적용

**`game-ci/unity-builder@v4`** 를 쓴다. `NVproject/ProjectSettings/ProjectVersion.txt`(6000.3.20f1)에서 에디터 버전을 읽어 `unityci/editor` 의 `-webgl` 이미지를 받는다. 이 Docker 사용은 **GitHub 러너 내부에 한정**된다 — 산출물은 tar 하나로 나오고, 서버는 그 tar 만 받는다(원칙 절 참고).

- **라이선스**: Unity Personal 기준 Secrets 세 개 — `UNITY_LICENSE`(.ulf 파일 내용 전체), `UNITY_EMAIL`, `UNITY_PASSWORD`. `.ulf` 는 GameCI 의 activation 절차(수동 1회)로 발급한다.
- **buildMethod 는 기존 메뉴 메서드다** — `NV.Client.EditorTools.BuildMenu.BuildProductionWebGl`. GameCI 의 기본 빌드는 `BuildPipeline.BuildPlayer` 만 부르는데, 이 프로젝트의 빌드는 그 전에 **선택된 환경을 `Assets/Resources/NVEnvironment.asset` 에 굽는 단계**(`BuildRunner.BakeEnvironment`)가 있어 기본 빌드로는 어느 서버에 붙을지 미정인 빌드가 나온다. 메뉴 메서드는 그 전부를 이미 한다: `production.asset` 을 `EnvironmentOverride` 로, `Development=false`, 압축 Brotli. **새 진입점도, 기존 파일 수정도 없다.**
- `BuildRunner` 는 이미 배치모드-안전이다(대화창 없음, 전부 로그 — 코드 주석의 설계 근거 그대로). `production.asset` 이 secure=1 이므로 유일한 빌드 거부 사유(원격 호스트 + 평문)도 통과한다.
- **실패 전파의 공백 하나를 워크플로가 메운다.** `BuildProductionWebGl` 은 `BuildRunner.Run` 의 반환값을 버리므로, 빌드가 실패해도 배치모드 Unity 는 exit 0 으로 끝난다 — GameCI 스텝만 보면 실패가 성공으로 읽힌다. 그래서 빌드 스텝 바로 다음에 **산출물 검증 스텝**을 둔다: `NVproject/Builds/production/WebGL/index.html` 과 `Build/` 가 존재해야 통과. 러너 작업 공간은 매번 새것이고 `Builds/` 는 캐시하지 않으므로 이전 빌드의 잔재가 이 검증을 속일 수 없다. (언젠가 정확한 exit code 가 필요해지면 `BuildProduction` 에 `Application.isBatchMode` 분기 서너 줄을 넣는 것이 다음 수다 — 지금은 넣지 않는다.)
- **`com.nv.shared` 는 그대로 성립한다.** `file:../../NVserver/Shared` 는 저장소 전체 체크아웃에서 해석되고, actions/checkout 은 전체를 받는다.
- **씬 목록은 저장소가 진실이다.** CI 는 씬 생성 메뉴를 돌릴 수 없으므로, 커밋된 `EditorBuildSettings.asset` 이 곧 빌드의 씬 목록이다. `MainLobby`/`GameLobby` 등 다섯 씬은 이미 커밋되어 있다. 0번 씬이 `MainLobby` 가 아니면 `BuildRunner` 가 경고를 남긴다 — CI 로그에서 그 경고를 보면 씬 목록 커밋이 어긋난 것이다.
- **Library 캐시**: `actions/cache` 로 `NVproject/Library` 를 캐시한다(키: `Packages/packages-lock.json` + `ProjectSettings` 해시). 첫 빌드는 30~60분, 캐시 히트 후 10~20분을 기대한다.
- `versioning: None` 을 명시한다 — 기본값(Semantic)은 git 이력으로 버전을 계산하는데 shallow checkout 에는 이력이 없고, 이 프로젝트는 `Application.version` 을 읽지 않는다.
- `buildMethod` 의 공식 요구는 둘이다 — **static 메서드**일 것, **클래스가 `Assets/Editor`(또는 Editor 어셈블리) 안**일 것. `BuildMenu.BuildProductionWebGl` 은 `public static` 이고 `Assets/Editor/BuildManager/` 에 있다 — 둘 다 충족 (game.ci/docs/github/builder 대조).

**0단계 사전 확인**: `unityci/editor` 의 6000.3.20f1 webgl 이미지 존재 — **확인 완료** (`ubuntu-6000.3.20f1-webgl-3.2.2`, 약 7.5GB, 2026-08 기준 Docker Hub 에 있다). 이미지가 7.5GB 이므로 러너 디스크 확보 스텝이 필요할 수 있다는 3절의 대비가 실제 근거를 얻었다.

## 3. GitHub Actions Workflow 구성 — `client-deploy.yml`

`server-deploy.yml` 과 같은 골격, 잡 세 개.

```yaml
on:
  push:
    branches: [main]
    paths:
      - 'NVproject/**'
      - 'NVserver/Shared/**'        # 클라이언트가 같은 소스를 컴파일한다
      - '.github/workflows/client-deploy.yml'
  workflow_dispatch:
    inputs:
      rollback_to: ...              # 서버판과 동일한 의미
concurrency:
  group: client-deploy              # 서버 배포와는 다른 그룹 — 서로 막지 않는다
  cancel-in-progress: false
```

- **build**: checkout → Library 캐시 → (필요 시 러너 디스크 확보 스텝) → `unity-builder@v4`(projectPath `NVproject`, targetPlatform `WebGL`, buildMethod **`NV.Client.EditorTools.BuildMenu.BuildProductionWebGl`** — 에디터 메뉴 `Tools ▸ NV ▸ Build Production (WebGL)` 그 메서드) → **산출물 검증**(`NVproject/Builds/production/WebGL` 에 `index.html` 과 `Build/` 가 있는가 — 2절의 exit code 공백을 메우는 스텝) → tar → 아티팩트 업로드.
- **deploy**: 아티팩트 다운로드 → 서버판과 동일한 SSH 구성 스텝(같은 Secrets) → `client-deploy.sh` 와 tarball 을 `/tmp` 로 scp → 실행.
- **rollback**: `rollback_to` 입력 시 — `client-deploy.sh rollback <릴리스>` 호출.

클라이언트에는 별도 CI 게이트(테스트)가 없다. EditMode 테스트는 에디터 Test Runner 가 필요하고, 그것을 CI 에 올리는 일(`unity-test-runner`)은 이 계획의 범위 밖이다 — 빌드 성공이 게이트다.

## 4. 산출물 전송·배포 방식

서버와 같은 경로: tarball → `/tmp` → 서버 상주 스크립트. 새 스크립트 `client-deploy.sh` 는 `deploy.sh` 의 축소판이다.

```
client-deploy.sh <tarball>           # 전개 → 검증 → current 전환. 실패 시 이전 릴리스 유지
client-deploy.sh rollback <릴리스>    # 심링크만 되돌린다
```

- 전개: `/opt/nvclient/releases/<ts>/`. 검증은 정적이다 — `index.html` 과 `Build/` 디렉터리가 있는가. 없으면 전개물을 지우고 실패한다(심링크는 건드리지 않았으므로 이전 버전이 그대로 서비스 중).
- 전환: `ln -sfn` 으로 `current` 교체. **재시작이 없다** — Caddy 는 요청마다 심링크를 따라가므로 전환 즉시 다음 요청이 새 빌드를 받는다.
- 부트스트랩: `/opt/nvclient` 가 없으면 만들고, 성공한 배포는 자신을 `/opt/nvclient/deploy.sh` 로 남긴다 — 서버판과 같은 규칙(사람이 SSH 로 붙어도 같은 절차).
- 뒷정리: tarball 삭제, 릴리스 **3개** 유지(서버는 5개 — WebGL 산출물이 더 크고, 정적 파일 롤백은 어차피 직전 버전으로 가는 것이 대부분이다).
- 권한: 릴리스 디렉터리는 배포 계정 소유, `o+rx` — Caddy(caddy 계정)가 읽을 수 있어야 한다. `install -d -m 755` 로 만든다.

## 5. 서버 디렉터리·서비스 구조

```
/opt/nvserver/            ← 기존, 불변
    releases/  current → …  shared/nvserver.env  deploy.sh
/opt/nvclient/            ← 신규
    releases/<ts>/        index.html, Build/, TemplateData/, StreamingAssets?
    current → releases/<ts>
    deploy.sh             (client-deploy.sh 의 상주 사본)
```

새 systemd 유닛도, 새 데몬도 없다. "WebGL 웹사이트" 의 실체는 Caddy 사이트 블록 하나 + 디렉터리 하나다. health check 타이머도 만들지 않는다 — 정적 파일은 교착하지 않고, Caddy 의 생존은 API 도메인이 이미 증명한다.

## 6. Caddy — HTTPS 와 도메인 연결

`play.nhn-backroom.kro.kr` 의 A 레코드를 인스턴스로 추가하고(DNS, 수동 1회), `/etc/caddy/Caddyfile` 에 블록을 추가한다. 인증서는 Caddy 가 알아서 받는다. 기존 원칙대로 Caddyfile 반영은 수동이고, 저장소의 `Caddyfile.example` 을 같은 내용으로 갱신해 둔다.

```caddyfile
# 기존 블록 그대로 유지
api.nhn-backroom.kro.kr {
    reverse_proxy 127.0.0.1:5202
}

play.nhn-backroom.kro.kr {
    root * /opt/nvclient/current
    file_server

    # Unity 로더는 Brotli 산출물을 .br 경로 그대로 요청한다. Content-Encoding 을
    # 우리가 붙여야 브라우저가 푼다 — 이 헤더가 없는 정적 서버에서 Brotli 빌드가
    # 검은 화면이 되는 것이 이 저장소의 문서화된 함정이다(BuildSelection 주석).
    @wasmBr path *.wasm.br
    header @wasmBr Content-Encoding br
    header @wasmBr Content-Type application/wasm
    @jsBr path *.js.br
    header @jsBr Content-Encoding br
    header @jsBr Content-Type application/javascript
    @dataBr path *.data.br
    header @dataBr Content-Encoding br
    header @dataBr Content-Type application/octet-stream

    # 파일명에 해시가 없으므로 오래 캐시하면 배포가 반쪽만 보인다.
    header /index.html Cache-Control "no-cache"
    header /Build/* Cache-Control "public, max-age=300"
}
```

- **COOP/COEP(교차 출처 격리) 헤더는 넣지 않는다.** WebGL 스레드를 쓰지 않으므로 필요 없고, 켜면 교차 출처 리소스 로드에 새 제약이 생긴다.
- 루트 도메인(`nhn-backroom.kro.kr`)을 `play` 로 redir 하는 블록은 선택 — CORS 목록에 이미 있으므로 언제 붙여도 서버 변경이 없다.

## 7. API ↔ WebGL 접근 설정 (CORS 등)

**변경할 것이 없다 — 검증만 한다.** 이 절이 짧은 것이 기존 구조가 준비된 증거다.

- CORS: `Api/appsettings.Production.json` 에 `https://play.nhn-backroom.kro.kr` 가 이미 있다. 값은 "페이지의 오리진" 이고 play 서브도메인이 그 페이지다.
- 클라이언트 접속 대상: `production.asset` 이 `api.nhn-backroom.kro.kr` + `secure=1` — HTTPS 페이지에서 `wss://` 로 붙는다. mixed content 거부(빌드 거부 사유)는 성립하지 않는다.
- WebSocket: 기존 Caddy 블록이 업그레이드를 그대로 넘긴다(설정 없음이 설정이다 — Caddyfile.example 주석).
- 전달 헤더·요청 제한: `nvserver.env` 의 `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` 가 이미 있으므로 play 오리진에서 온 트래픽도 실제 IP 로 제한된다.

## 8. 서버 배포 워크플로와의 관계

- **평시에는 완전히 독립이다.** `paths` 필터가 서로 겹치지 않고(`NVserver/**` vs `NVproject/**`), concurrency 그룹도 다르다. 동시에 돌아도 서로 다른 디렉터리에 쓴다.
- **접점은 `NVserver/Shared/` 하나다.** 그 폴더의 변경은 두 워크플로를 **둘 다** 트리거한다(서버는 기존 `NVserver/**` 로, 클라이언트는 새로 추가한 `NVserver/Shared/**` 로). 같은 시뮬레이션 코드를 양쪽이 컴파일하므로 이것이 맞는 동작이다.
- **프로토콜 버전 인상은 짝 배포다.** `ProtocolInfo.Version` 이 오르면 서버·클라이언트 어느 한쪽만 새 버전인 조합은 426 으로 거절된다. 규칙은 순서가 아니라 **짝**: 올릴 때도 함께, 되돌릴 때도 함께 — `deploy/readme.md` 의 기존 함정("서버만 되돌리면 새 클라이언트가 426") 이 이제 반대 방향으로도 성립한다.

## 9. 롤백과 이전 빌드 관리

- **자동 롤백**: `client-deploy.sh` 는 검증 실패 시 심링크를 건드리지 않으므로, 실패한 배포는 곧 "아무 일도 없었던 배포" 다. 서버판의 "전환 후 health 실패 → 되돌림" 보다 한 단계 앞에서 끝난다.
- **수동 롤백**: Actions `client-deploy` ▸ `workflow_dispatch` ▸ `rollback_to`, 또는 서버에서 `bash /opt/nvclient/deploy.sh rollback <릴리스>`. 심링크 전환뿐이라 무중단이다.
- **보존**: 릴리스 3개. prune 은 배포 성공 시에만 돈다(실패한 배포가 성한 릴리스를 지우면 안 된다).
- **함정**: 프로토콜 버전이 걸린 롤백은 8절의 짝 규칙을 따른다.

## 10. Free Tier 리소스 운영

| 자원 | 부담 | 근거 |
|---|---|---|
| CPU/메모리 | **증가 없음** | 빌드는 전부 GitHub 러너. 인스턴스가 새로 하는 일은 Caddy 의 정적 파일 서빙뿐이고, Brotli 가 **사전압축**이라 런타임 압축 CPU 도 없다 |
| 디스크 | 릴리스 3개 × 대략 100~300MB | prune 자동, tarball 은 배포 직후 삭제. `/opt/nvserver` 의 5릴리스와 합쳐도 프리티어 부트 볼륨(약 47GB)에 여유가 크다 |
| 네트워크 | WebGL 초기 로드 수십 MB/방문 | OCI 프리티어 아웃바운드 10TB/월 — 문제되지 않는다 |
| GitHub Actions | 빌드당 수십 분 | 무료 한도(공개 저장소 무제한 / 비공개 2,000분·월)를 감안해 `paths` 필터로 문서-only 푸시를 걸러낸다. 잦아지면 `workflow_dispatch` 중심으로 전환한다 |

## 구현 순서 — 무엇을 만들고 무엇을 바꾸나

| 단계 | 작업 | 대상 파일 | 검증 |
|---|---|---|---|
| 0 | GameCI 이미지 확인(6000.3.20f1 webgl), Unity 라이선스 .ulf 발급, `play` DNS 레코드 | — | Docker Hub 태그, nslookup |
| 1 | 메뉴 메서드의 배치모드 실행 검증 — CI 가 부를 것을 로컬에서 먼저 부른다: `Unity.exe -batchmode -quit -projectPath NVproject -buildTarget WebGL -executeMethod NV.Client.EditorTools.BuildMenu.BuildProductionWebGl` | **없음 — 코드 변경 없이 기존 메뉴 메서드 그대로** | `Builds/production/WebGL/index.html` 생성, 로그의 `[NV] 빌드 완료` 줄 |
| 2 | 클라이언트 배포 스크립트 | `NVproject/deploy/client-deploy.sh` (신규) | 로컬 tarball 로 서버에서 수동 1회 — 전개·전환·prune·rollback |
| 3 | Caddy 블록 추가 | `NVserver/deploy/Caddyfile.example` 갱신 + 서버 `/etc/caddy/Caddyfile` 수동 반영, `systemctl reload caddy` | `curl -I https://play…/index.html`, `.br` 응답의 Content-Encoding 헤더 |
| 4 | GitHub Secrets 추가 | `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD` | — |
| 5 | 워크플로 작성 | `.github/workflows/client-deploy.yml` (신규) | `workflow_dispatch` 수동 실행 → Actions 로그에서 굽힌 환경이 production 인지 확인 |
| 6 | 엔드투엔드 검증 | — | 브라우저에서 `https://play…` 로딩 → 방 만들기(POST /rooms 가 CORS 통과) → 두 번째 브라우저로 초대 코드 입장 → wss 연결·매치 시작 |
| 7 | 문서 반영 | `NVserver/deploy/readme.md` 에 클라이언트 절 추가, 부딪힌 함정은 `docs/conventions.md` | — |

## 하지 않는 것

- **인스턴스 위 빌드** — 프리티어 CPU 를 게임 서버와 나누는 일은 하지 않는다.
- **서버에 Docker·컨테이너 런타임 설치** — 배포물은 정적 파일이고, 컨테이너가 풀 문제가 없다.
- **독자적인 CI 빌드 파이프라인** — CI 전용 진입점·전용 빌드 스크립트를 만들지 않는다. CI 는 `Tools ▸ NV ▸ Build Production (WebGL)` 메뉴의 메서드를 그대로 실행하고, 그 메서드가 곧 빌드의 단일 정의다. `BuildRunner` 일가도 수정하지 않는다.
- **별도 웹 서버(nginx 등) 추가** — Caddy 하나로 충분하고, 둘이면 인증서·포트가 얽힌다.
- **CDN, 파일명 해시, 서비스 워커** — 트래픽이 그것을 요구할 때 한다. 지금은 `max-age=300` 이 배포 반영과 캐시 절약의 절충이다.
- **`unity-test-runner` CI 게이트** — EditMode 테스트의 CI 편입은 별개 작업으로 미룬다.
- **서버 워크플로·스크립트 수정** — 접점이 생기면 이 문서의 8절이 먼저 갱신되어야 한다.
