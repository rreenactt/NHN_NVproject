# deploy/ — OCI 배포 파일

설계와 근거는 [docs/deploy-plan.md](../docs/deploy-plan.md) 에 있다. 이 문서는 실행 순서만 적는다.

**서버 코드는 이 배포를 위해 아무것도 바뀌지 않았다.** `/health` 는 이미 있었고, 설정은 환경변수(`__` 절 구분자)로 덮어쓴다. 여기 있는 것은 서버 위에 놓일 파일과 그 설치 절차뿐이다.

| 파일 | 놓이는 곳 | 역할 |
|---|---|---|
| `provision.sh` | (1회 실행) | 인스턴스 초기 구성. 반복 실행 안전 — 재해 복구 절차이기도 하다 |
| `deploy.sh` | `/opt/nvserver/deploy.sh` | 배포·롤백 실행기. 워크플로가 배포마다 최신본을 올린다 |
| `nvserver.service` | `/etc/systemd/system/` | 앱 유닛. 크래시 시 3초 후 무한 재시작 |
| `nvserver-healthcheck.{sh,service,timer}` | `/opt/nvserver/`, `/etc/systemd/system/` | 1분 주기 `/health` 감시, 3회 연속 실패 시 재시작 |
| `nvserver.env.example` | `/opt/nvserver/shared/nvserver.env` | 앱 설정의 단일 출처. provision 이 한 번 복사, 이후 서버에서만 수정 |
| `Caddyfile.example` | `/etc/caddy/Caddyfile` | TLS 종단 + 리버스 프록시 |

워크플로는 저장소 루트 `.github/workflows/` 에 있다 — `server-ci.yml`(빌드+테스트 게이트), `server-deploy.yml`(배포·롤백).

## 초기 구성 순서

1. **OCI 콘솔** — `VM.Standard.A1.Flex`(2 OCPU / 12GB, Ubuntu 24.04 aarch64) 생성.
   VCN 보안 목록에 인바운드 22/80/443 허용. 5202 는 열지 않는다.
2. **SSH 키 발급** (로컬에서):

   ```bash
   ssh-keygen -t ed25519 -C nvserver-deploy -f nvserver-deploy -N ""
   ssh-keyscan <서버 IP 또는 도메인>        # → DEPLOY_KNOWN_HOSTS 값
   ```

3. **GitHub Secrets** (저장소 Settings ▸ Secrets and variables ▸ Actions). 필수는 둘이다:

   | Secret | 필수 | 값 · 생략 시 |
   |---|---|---|
   | `DEPLOY_HOST` | 필수 | 서버 IP 또는 도메인 |
   | `DEPLOY_SSH_KEY` | 필수 | `nvserver-deploy` 개인키 파일 내용 전체 (BEGIN~END) |
   | `DEPLOY_USER` | 선택 | 생략하면 `nvserver` |
   | `DEPLOY_KNOWN_HOSTS` | 선택 | `ssh-keyscan <DEPLOY_HOST>` 출력. 생략하면 첫 접속을 신뢰(accept-new)한다 — 첫 배포의 중간자만 못 막고 이후 호스트 키 변경은 잡는다. 지정한다면 `DEPLOY_HOST` 와 같은 문자열로 스캔해야 한다 |

   등록 후 로컬의 개인키 파일은 지운다.

   **배포 계정을 `ubuntu` 로 쓰려면** (권장은 `nvserver` — 유출 반경이 "재배포뿐" 대 "인스턴스 root" 로 다르다):
   `DEPLOY_USER` Secret 을 `ubuntu` 로 두고, 배포용 공개키를 `~ubuntu/.ssh/authorized_keys` 에 추가하고,
   `sudo chown -R ubuntu:ubuntu /opt/nvserver` 로 소유권을 넘긴다. 앱은 여전히 `nvserver` 계정으로 돈다.
   되돌리려면 Secret 을 지우고 키를 `nvserver` 에 등록하고 소유권을 되돌린다.
4. **서버에서** — 이 디렉터리를 서버로 복사하고(`scp -r deploy/ ubuntu@서버:`), `sudo bash deploy/provision.sh`.
   끝나면 남은 수작업 4개(공개키 등록, iptables, Caddy, CORS 오리진)가 출력된다 — 순서대로 한다.
5. **첫 배포** — main 푸시 또는 Actions 의 `server-deploy` 를 `workflow_dispatch` 로 실행.
6. **검증** — `https://도메인/health` 가 `ok`. 클라이언트는 `Assets/Settings/Environments/` 에
   운영 에셋(호스트=도메인, `secure` 켬)을 만들어 접속한다.

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
- **접속이 안 되면 iptables 부터 본다.** OCI Ubuntu 이미지는 보안 목록과 별개로 기본 iptables 에 REJECT 규칙이 있다.
