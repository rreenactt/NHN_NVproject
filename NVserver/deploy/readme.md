# deploy/ — OCI 배포 파일

설계와 근거는 [docs/deploy-plan.md](../docs/deploy-plan.md) 에 있다. 이 문서는 실행 순서만 적는다.

**서버 코드는 이 배포를 위해 아무것도 바뀌지 않았다.** `/health` 는 이미 있었고, 설정은 환경변수(`__` 절 구분자)로 덮어쓴다. 여기 있는 것은 배포가 서버에 놓는 파일뿐이다.

| 파일 | 놓이는 곳 | 역할 |
|---|---|---|
| `deploy.sh` | 워크플로가 매번 `/tmp` 로 올려 실행 | 배포·롤백 실행기. **사전 준비 없는 서버도 부트스트랩한다** — 디렉터리·앱 계정·env·systemd 유닛·health check 타이머가 없으면 만들고, 있으면 절대 건드리지 않는다. 성공하면 자신을 `/opt/nvserver/deploy.sh` 에 남긴다 |
| `nvserver.service` | `/etc/systemd/system/` | 앱 유닛. 크래시 시 3초 후 무한 재시작 |
| `nvserver-healthcheck.{sh,service,timer}` | `/opt/nvserver/`, `/etc/systemd/system/` | 1분 주기 `/health` 감시, 3회 연속 실패 시 재시작 — 교착 감시. 부트스트랩이 설치한다 |
| `nvserver.env.example` | `/opt/nvserver/shared/nvserver.env` | 앱 설정의 단일 출처. 부트스트랩이 없을 때 한 번 복사, 이후 서버에서만 수정 |
| `Caddyfile.example` | `/etc/caddy/Caddyfile` | TLS 종단 + 리버스 프록시. Caddy 설치·도메인은 수동 |

워크플로는 저장소 루트 `.github/workflows/` 에 있다 — `server-ci.yml`(빌드+테스트 게이트), `server-deploy.yml`(배포·롤백). 이 폴더의 파일들은 배포 tarball 에 `deploy/` 로 함께 실린다 — 부트스트랩의 출처다.

## 배포 경로 — 빈 서버 + Secrets 만으로

서버에 미리 할 일이 없다. OCI 가 만들어 준 `ubuntu` 계정과 그 키를 그대로 쓰면
키 등록조차 필요 없다 — 인스턴스 생성 때 이미 등록되어 있다.

1. **OCI 콘솔** — `VM.Standard.A1.Flex`(2 OCPU / 12GB, Ubuntu 24.04 aarch64) 생성.
   VCN 보안 목록에 인바운드 22 허용 (80/443 은 Caddy 를 올릴 때).
2. **GitHub Secrets** (저장소 Settings ▸ Secrets and variables ▸ Actions):

   | Secret | 필수 | 값 · 생략 시 |
   |---|---|---|
   | `DEPLOY_HOST` | 필수 | 서버 IP 또는 도메인 |
   | `DEPLOY_SSH_KEY` | 필수 | 개인키 파일 내용 전체 (BEGIN~END) |
   | `DEPLOY_USER` | 선택 | 생략하면 `nvserver`. ubuntu 계정으로 배포하면 `ubuntu` |
   | `DEPLOY_KNOWN_HOSTS` | 선택 | `ssh-keyscan <DEPLOY_HOST>` 출력(핀 고정). 생략하면 첫 접속을 신뢰(accept-new)한다 — 첫 배포의 중간자만 못 막고 이후 호스트 키 변경은 잡는다. 지정 시 `DEPLOY_HOST` 와 같은 문자열로 스캔한다 |

3. **첫 배포** — main 푸시 또는 Actions 의 `server-deploy` ▸ `workflow_dispatch`.
   부트스트랩이 `/opt/nvserver`, 앱 계정(`nvserver`), env 파일, systemd 유닛,
   health check 타이머를 만든다. 부트스트랩은 sudo 를 쓰므로 첫 배포의 계정에는
   sudo 가 있어야 한다 — `ubuntu` 가 그 조건을 이미 만족한다.
4. **env 수정 (한 번)** — 부트스트랩이 만든 `/opt/nvserver/shared/nvserver.env` 의
   `Cors__AllowedOrigins__0` 을 Caddy 에 물린 실제 오리진으로 바꾸고
   `sudo systemctl restart nvserver`. 이후 배포는 이 파일을 보존한다.
5. **검증** — 서버에서 `curl 127.0.0.1:5202/health` 가 `ok`. Caddy 를 올린 뒤에는
   `https://도메인/health`. 클라이언트는 `Assets/Settings/Environments/` 에 운영
   에셋(호스트=도메인, `secure` 켬)을 만들어 접속한다.

이 경로의 대가 하나: 배포 키가 곧 `ubuntu`(passwordless sudo) 키이므로 **Secret
유출이 인스턴스 root 유출과 같다.** 운영이 진지해지면 아래로 옮긴다.

## 전용 키·최소 권한 경로 (선택)

첫 배포가 끝난 서버를 전제로 한다 — 앱 계정 `nvserver` 는 부트스트랩이 이미 만들었다.
배포 키의 유출 반경을 "인스턴스 root" 에서 "이 서버 재배포" 로 줄인다.

1. **키 발급** (로컬): `ssh-keygen -t ed25519 -C nvserver-deploy -f nvserver-deploy -N ""`
2. **서버에서** — 공개키 등록, 재시작만 허용하는 sudoers, 소유권 이전:

   ```bash
   sudo install -d -m 700 -o nvserver -g nvserver /home/nvserver/.ssh
   echo '<nvserver-deploy.pub 내용>' | sudo tee -a /home/nvserver/.ssh/authorized_keys
   sudo chown nvserver:nvserver /home/nvserver/.ssh/authorized_keys
   sudo chmod 600 /home/nvserver/.ssh/authorized_keys
   echo 'nvserver ALL=(root) NOPASSWD: /usr/bin/systemctl restart nvserver' | sudo tee /etc/sudoers.d/nvserver-deploy
   sudo chmod 440 /etc/sudoers.d/nvserver-deploy
   sudo chown -R nvserver:nvserver /opt/nvserver
   ```

3. **GitHub Secrets 교체** — `DEPLOY_SSH_KEY` 를 새 개인키로, `DEPLOY_USER` 는
   삭제(기본값이 `nvserver` 다). 등록 후 로컬의 개인키 파일은 지운다.

## 운영 명령 (서버에서)

```bash
systemctl status nvserver                 # 상태
journalctl -u nvserver -f                 # 로그 팔로우
ls -1t /opt/nvserver/releases             # 릴리스 목록
bash /opt/nvserver/deploy.sh rollback 20260809-140000   # 수동 롤백 (Actions 없이)
```

수동 롤백은 Actions 의 `server-deploy` ▸ `workflow_dispatch` ▸ `rollback_to` 입력으로도 같은 것을 한다.

## 함정

- **프로토콜 버전을 올린 배포의 롤백은 클라이언트 재배포와 짝이다.** 서버만 되돌리면 새 버전으로 빌드된 클라이언트가 426 으로 거절된다.
- **재시작은 진행 중인 방을 전부 끊는다.** 상태가 전부 메모리에 있다 — 매치·룸·초대 코드가 배포와 함께 사라진다.
- **포트 5202 를 바꾸려면 세 곳이 같이 움직인다** — `nvserver.env` 의 `ASPNETCORE_URLS`, `deploy.sh` 와 `nvserver-healthcheck.sh` 의 `HEALTH_URL`, `Caddyfile` 의 `reverse_proxy`.
- **접속이 안 되면 iptables 부터 본다.** OCI Ubuntu 이미지는 보안 목록과 별개로 기본 iptables 에 REJECT 규칙이 있다. 배포 자체는 영향이 없다 — health check 는 서버 내부에서 한다.
- **systemd 유닛·타이머는 "없을 때만" 설치된다.** 저장소에서 유닛 내용을 바꿔도 이미 구성된 서버에는 반영되지 않는다 — 서버에서 지우고 재배포하거나 손으로 교체하고 `sudo systemctl daemon-reload`.
