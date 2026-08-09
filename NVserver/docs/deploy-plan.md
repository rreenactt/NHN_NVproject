# Oracle Cloud 배포 자동화 계획

Oracle Cloud Free Tier 인스턴스에 NVserver 를 올리고, GitHub Actions 가 SSH 로 배포하는 파이프라인을 만든다.

## 원칙

- **운영 환경에 Docker 를 쓰지 않는다.** `dotnet publish` 산출물을 systemd 가 직접 실행한다. `Api/Dockerfile` 과 `compose.yaml` 은 개발 환경 전용으로 그대로 둔다.
- **코드 변경 0 을 지향한다.** 서버 코드는 이미 배포를 견디게 만들어져 있다 — `/health` 엔드포인트가 있고(`Program.cs`), 설정은 전부 환경변수로 덮어쓸 수 있으며(`__` 절 구분자), 봇 같은 개발 전용 옵션은 `Production` 에서 켜면 기동이 멈춘다. 추가되는 것은 워크플로 파일, 배포 스크립트, 서버 위 설정 파일뿐이다.
- **빌드·실행 방식을 유지한다.** 진입점은 `Api` 하나, 빌드는 `dotnet publish`, 실행은 그 산출물의 `./Api`. 로컬에서 `dotnet run --project Api` 로 돌리는 것과 같은 프로세스가 서버에서 돈다.

## 대상 인프라

| 항목 | 선택 | 근거 |
|---|---|---|
| 인스턴스 | **VM.Standard.A1.Flex** (Ampere ARM), 2 OCPU / 12GB | Free Tier 한도(4 OCPU / 24GB) 안에서 넉넉하다. x64 `E2.1.Micro`(1GB)는 예비로 남긴다 |
| OS | Ubuntu 24.04 (aarch64) | systemd, 패키지 최신 |
| 게시 방식 | `dotnet publish -c Release -r linux-arm64 --self-contained` | 서버에 .NET 런타임 설치가 불필요해지고, 런타임 버전이 배포 아티팩트에 고정된다. 아티팩트가 ~90MB 로 커지는 대가는 Free Tier 대역폭(월 10TB)에서 무시할 수 있다 |
| TLS | **Caddy** 리버스 프록시 + Let's Encrypt | WebGL 페이지가 HTTPS 면 `ws://` 는 브라우저가 차단한다 — `wss` 없는 배포는 클라이언트가 빌드를 거부한다(루트 `CLAUDE.md`). Caddy 는 인증서 발급·갱신이 자동이고 WebSocket 프록시에 추가 설정이 없다 |
| 도메인 | 필요. 없으면 `duckdns` 류 무료 서브도메인 | Let's Encrypt 는 IP 로 발급하지 않는다 |

트래픽 경로: `브라우저 → :443 Caddy → 127.0.0.1:5202 Kestrel`. Kestrel 은 루프백에만 바인딩해 5202 직접 노출을 없앤다.

### 리버스 프록시의 함정 두 개

1. **요청 제한이 프록시 IP 하나로 묶인다.** `RateLimit:*` 는 원격 IP 로 파티션하는데, 프록시 뒤에서는 모두가 127.0.0.1 이다. `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` 로 전달 헤더를 켠다 — 이 스위치의 기본 신뢰 목록이 루프백뿐이라, 같은 머신의 Caddy 가 붙인 `X-Forwarded-For` 는 믿고 외부에서 위조해 온 것은 믿지 않는다. 코드 변경 없이 환경변수 하나로 끝난다. 배포 후 서로 다른 IP 두 곳에서 429 한도가 따로 도는지 확인한다.
2. **프록시 유휴 타임아웃 > WebSocket keepalive(30초).** Caddy 기본값은 스트리밍 연결에 타임아웃을 걸지 않으므로 추가 설정이 없다. nginx 로 바꾼다면 `proxy_read_timeout` 을 늘려야 한다.

## 1. OCI 서버 초기 환경 구성 (1회 수작업)

인스턴스 생성 후 한 번만 한다. 이후에는 GitHub Actions 만 서버를 만진다.

1. **네트워크** — VCN 보안 목록(또는 NSG)에서 인바운드 22, 80, 443 허용. 5202 는 열지 않는다.
2. **OS 방화벽** — OCI 의 Ubuntu 이미지는 **기본 iptables 에 REJECT 규칙이 들어 있다.** 보안 목록을 열어도 접속이 안 되면 십중팔구 이것이다. 80/443 을 `iptables -I INPUT` 으로 허용하고 `netfilter-persistent save` 로 고정한다.
3. **배포 사용자** — `nvserver` 시스템 사용자를 만든다. sudo 없음, 로그인 셸 있음(rsync/ssh 대상). GitHub Actions 는 이 사용자로만 접속한다. `systemctl restart nvserver` 한 줄만 sudoers 에 NOPASSWD 로 허용한다.
4. **디렉터리 구조**

   ```
   /opt/nvserver/
     releases/20260809-140000/   # 배포 단위. publish 산출물 + MapData
       app/                      # dotnet publish 출력 (실행 파일 Api 포함)
       MapData/                  # 저장소의 NVserver/MapData 그대로
     current -> releases/20260809-140000   # 심볼릭 링크. 전환이 곧 배포다
     shared/
       nvserver.env              # 환경변수. chmod 600 nvserver:nvserver
   ```

   `app/` 옆에 `MapData/` 를 두는 이유: `appsettings.json` 의 `Game:MapDirectory` 가 `../MapData` 라서, 작업 디렉터리를 `current/app` 으로 잡으면 상대 경로가 저장소에서와 똑같이 성립한다. 설정을 바꾸지 않는 것이 목적이다. 만약 경로 해석이 어긋나면 `Game__MapDirectory=/opt/nvserver/current/MapData` 환경변수로 절대 경로를 준다 — 어느 쪽이든 코드 변경은 없다.
5. **systemd 유닛** — `/etc/systemd/system/nvserver.service`

   ```ini
   [Unit]
   Description=NVserver game server
   After=network.target

   [Service]
   User=nvserver
   WorkingDirectory=/opt/nvserver/current/app
   ExecStart=/opt/nvserver/current/app/Api
   EnvironmentFile=/opt/nvserver/shared/nvserver.env
   Restart=always
   RestartSec=3

   [Install]
   WantedBy=multi-user.target
   ```

   `Restart=always` 가 크래시 복구의 1차 방어선이다. `Type=notify` + watchdog 은 `Microsoft.Extensions.Hosting.Systemd` 패키지가 필요한데, 새 NuGet 도입은 이 저장소에서 확인 요청 대상이므로 계획에서 뺀다 — 아래 health check 타이머가 그 자리를 대신한다.
6. **Caddy** — `apt install caddy`, `/etc/caddy/Caddyfile`:

   ```
   game.example.com {
       reverse_proxy 127.0.0.1:5202
   }
   ```

7. **journald 로그 상한** — `SystemMaxUse=500M`. 앱은 stdout 으로만 로그를 남기므로 로그 로테이션은 journald 가 전부다.

## 2. 환경변수·Secret 관리

앱 비밀은 **서버의 env 파일에만** 둔다. GitHub Secrets 에는 SSH 접속 정보만 둔다 — 워크플로가 앱 설정을 알 필요가 없어야, 설정 변경이 재배포 없이 서버에서 끝난다.

`/opt/nvserver/shared/nvserver.env`:

```bash
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5202
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
Cors__AllowedOrigins__0=https://game.example.com
```

- `Cors:AllowedOrigins` 를 비우면 전 오리진 허용으로 뜨고 기동 로그에 경고가 남는다(`Program.cs`). 배포에서는 반드시 지정한다.
- `Realtime__Bots__Enabled` 는 손대지 않는다. 기본값이 꺼짐이고, `Production` 에서 켜면 `GuardDevelopmentOnlyOptions` 가 기동을 멈춘다 — 실수로 켜진 채 올라가는 경로가 없다.
- 지금 서버에는 DB 도 외부 API 키도 없으므로 이 파일이 전부다. 비밀이 생기면 이 파일에 추가하는 것으로 충분하다.

## 3. SSH Key 및 GitHub Secrets

| Secret | 값 |
|---|---|
| `DEPLOY_HOST` | 인스턴스 공인 IP 또는 도메인 |
| `DEPLOY_USER` | `nvserver` |
| `DEPLOY_SSH_KEY` | 배포 전용 ed25519 **개인키**. 이 용도로 새로 만들고 다른 곳에 쓰지 않는다 |
| `DEPLOY_KNOWN_HOSTS` | `ssh-keyscan` 결과. 핀 고정해야 첫 접속의 호스트 키 확인을 안전하게 건너뛴다 |

키 생성 → 공개키를 서버 `~nvserver/.ssh/authorized_keys` 에 등록 → 개인키를 Secrets 에 등록. 개인키는 이 시점 이후 로컬에서 지운다.

## 4. GitHub Actions 구성

`.github/workflows/` 에 두 개. 클라이언트(Unity)는 CI 대상이 아니다 — CLI 빌드가 없다.

### `server-ci.yml` — PR 과 main 푸시

`NVserver/**` 변경에만 트리거(`paths` 필터). `dotnet build`(경고 0 — `TreatWarningsAsErrors` 가 이미 강제한다) + `dotnet test`(394개). 이것이 배포의 게이트다.

### `server-deploy.yml` — main 푸시(NVserver 변경 시) + `workflow_dispatch`

```
build 잡:
  actions/setup-dotnet (10.0.x)
  dotnet test                                    # 게이트 재확인
  dotnet publish Api -c Release -r linux-arm64 --self-contained
  tar (publish 출력 → app/, NVserver/MapData → MapData/)
deploy 잡:
  ssh-agent 에 DEPLOY_SSH_KEY 적재, known_hosts 핀 고정
  scp 아티팩트 → 서버
  ssh: /opt/nvserver/deploy.sh <아티팩트>        # 아래 5절
```

GitHub 러너는 x64 지만 `dotnet publish` 의 ARM64 크로스 게시는 네이티브 컴파일이 없어 그대로 된다. `workflow_dispatch` 는 재배포·롤백(입력으로 릴리스 지정)용 수동 트리거다.

## 5. 배포 방식 — release 디렉터리 + 심볼릭 링크 전환

서버에 두는 `deploy.sh` 하나가 배포·검증·롤백을 다 안다. 워크플로는 이 스크립트를 부를 뿐이다 — 로직이 서버에 있어야 SSH 로 사람이 붙어도 같은 절차를 쓸 수 있다.

```
deploy.sh <tarball>:
  1. releases/{타임스탬프}/ 에 풀기
  2. 이전 릴리스 기록 (current 의 대상)
  3. current 링크를 새 릴리스로 전환 (ln -sfn — 원자적)
  4. sudo systemctl restart nvserver
  5. curl -sf http://127.0.0.1:5202/health — 2초 간격 15회
  6. 성공: 오래된 릴리스 정리(최근 5개 유지), 종료 0
     실패: current 를 이전 릴리스로 되돌리고 재시작, 종료 1 → 워크플로가 실패로 표시
```

**재시작은 진행 중인 방을 전부 끊는다.** 상태가 전부 메모리에 있으므로 매치·룸·초대 코드가 배포와 함께 사라진다. 지금 규모에서는 감수한다 — 클라이언트의 자동 재시도는 새 세션으로 이어지고, 프리플라이트가 404 를 돌려주므로 증상이 "없는 방" 으로 명확하다. 무중단이 필요해지는 시점에 드레이닝(새 방 생성 중단 → 방이 빌 때까지 대기)을 이 스크립트에 넣는다. 지금 넣지 않는다.

## 6. Health Check 및 장애 시 자동 복구

세 겹이고, 각각 다른 실패를 잡는다.

| 겹 | 잡는 실패 | 수단 |
|---|---|---|
| 프로세스 크래시 | 예외로 죽음, OOM | systemd `Restart=always` (3초 후) |
| 살아 있지만 응답 없음 | 교착, 소켓 고갈 | systemd 타이머(1분 주기)가 `curl /health` 3회 연속 실패 시 `systemctl restart nvserver` |
| 배포 직후 기동 실패 | 잘못된 설정, 맵 파일-이름 불일치(기동이 멈추는 검증들) | `deploy.sh` 5단계 → 자동 롤백 |

두 번째 겹은 `nvserver-healthcheck.timer` + 셸 스크립트로, 초기 구성 때 함께 설치한다. 외부 감시(UptimeRobot 류로 `https://도메인/health`)는 선택이지만 공짜이므로 붙인다 — 위 세 겹이 전부 실패하는 경우(인스턴스 자체가 죽음)를 사람에게 알리는 유일한 길이다.

## 7. Rollback

- **자동**: 배포 직후 health check 실패 → `deploy.sh` 가 이전 릴리스로 링크를 되돌리고 재시작. GitHub Actions 잡이 빨간불이 된다.
- **수동**: 배포는 성공했는데 게임 결함이 나중에 발견된 경우. `workflow_dispatch` 로 릴리스 디렉터리 이름을 지정해 `deploy.sh rollback <릴리스>` 를 부르거나, SSH 로 직접 같은 명령을 친다. 최근 5개 릴리스가 서버에 남아 있으므로 재빌드 없이 즉시다.
- 롤백이 못 고치는 것: 프로토콜 버전이 올라간 배포를 되돌리면 **새 버전으로 빌드된 클라이언트가 426 으로 거절된다.** `ProtocolInfo.Version` 을 올린 배포의 롤백은 클라이언트 재배포와 짝이다. 이것은 도구가 아니라 절차로 기억한다.

## 8. Free Tier 리소스 최적화

A1.Flex 2 OCPU / 12GB 에서 이 서버(방당 5인, 인메모리, 30Hz)는 여유가 크다. 최적화의 대부분은 "하지 않아도 됨을 확인" 이다.

- `InvariantGlobalization` 이 이미 켜져 있어 ICU 가 필요 없다 — self-contained 게시가 그대로 돈다.
- GC: 12GB 에서는 기본값(Server GC)으로 충분하다. `E2.1.Micro`(1GB) 로 내려가야 한다면 `DOTNET_gcServer=0` + 스왑 2GB 를 env 파일에 추가한다.
- 디스크: 부트 볼륨 최소 47GB 안에서 릴리스 5개(~500MB) + journald 상한 500MB 로 고정 상한이 잡힌다.
- 대역폭: 스냅샷은 풀 스냅샷이지만 방당 5인 × 30Hz 규모라 월 10TB 한도와는 자릿수가 다르다.
- **Free Tier 유휴 회수 주의**: Always Free A1 인스턴스는 장기 유휴(CPU·네트워크 모두 낮음) 시 회수 대상이 될 수 있다. 외부 `/health` 감시가 트래픽을 만들어 주지만, 회수 정책은 Oracle 쪽 사정이므로 인스턴스 재생성 절차(이 문서의 1절)가 곧 재해 복구 문서다 — 1절에서 벗어난 수작업 설정을 서버에 만들지 않는 이유이기도 하다.

## 9. 구현 및 검증 순서

각 단계는 이전 단계가 검증되어야 시작한다. 실패의 원인을 한 층으로 좁히기 위한 순서다.

| # | 단계 | 완료 조건 |
|---|---|---|
| 1 | 인스턴스 생성, 방화벽(보안 목록 + iptables), 사용자·디렉터리 | SSH 접속, 80 포트에 임시 응답 확인 |
| 2 | 로컬에서 `dotnet publish -r linux-arm64` → 수동 scp → 손으로 실행 | 서버에서 `curl 127.0.0.1:5202/health` = `ok`, 기동 로그에 맵 2개 로드 |
| 3 | systemd 유닛 + env 파일 | `systemctl status` active, `kill -9` 후 3초 내 재기동 |
| 4 | Caddy + 도메인 + TLS | `https://도메인/health` = `ok` |
| 5 | 클라이언트 연결 — `Assets/Settings/Environments/` 에 운영 환경 에셋(호스트=도메인, `secure` 켬) 추가, 에디터에서 접속 | 방 생성 → 두 클라이언트로 매치 시작·종료. `secure` 끄면 빌드가 거부되는지도 확인 |
| 6 | 전달 헤더 검증 | 서로 다른 IP 두 곳의 429 한도가 독립적으로 돈다 |
| 7 | `server-ci.yml` | PR 에서 빌드·테스트가 돈다 |
| 8 | `deploy.sh` + `server-deploy.yml` | main 푸시로 배포되고 `/health` 녹색, 릴리스 디렉터리가 쌓인다 |
| 9 | 장애 훈련 | (a) 프로세스 kill → 자동 재기동 (b) 고의로 깨진 릴리스(예: 맵 파일명 불일치) 배포 → 자동 롤백, Actions 빨간불 (c) `workflow_dispatch` 수동 롤백 |
| 10 | health check 타이머 + 외부 감시 | 타이머 동작 로그, 외부 감시 녹색 |

## 하지 않는 것

- 운영 Docker, 컨테이너 레지스트리 — 요구사항에서 제외했고, systemd 직접 실행이 더 단순하다.
- 무중단 배포(블루그린, 드레이닝) — 인메모리 상태의 서버 1대에서 비용 대비 의미가 없다. 5절에 확장 지점만 적어 두었다.
- GitHub Secrets 에 앱 설정 넣기 — 설정은 서버의 env 파일이 단일 출처다.
- `Microsoft.Extensions.Hosting.Systemd` 도입 — 새 NuGet 은 확인 요청 대상이고, 타이머 기반 health check 가 같은 구멍을 막는다.
- WebGL 클라이언트 배포 자동화 — Unity 는 CLI 빌드가 없다(루트 `CLAUDE.md`). 클라이언트 배포는 별도 계획으로 다룬다. 다만 서버의 `wwwroot` 에 WebGL 빌드를 두면 같은 도메인에서 서빙되어 CORS 구성이 단순해진다는 점은 그 계획의 입력이다.
