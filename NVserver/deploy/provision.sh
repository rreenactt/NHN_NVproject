#!/usr/bin/env bash
# OCI 인스턴스 1회 초기 구성 — deploy-plan.md 1절의 실행본. root 로 실행한다.
#
#   sudo bash provision.sh
#
# 반복 실행해도 안전하다. 인스턴스를 새로 만들 때 이 스크립트가 곧 재해 복구 절차다 —
# 여기서 벗어난 수작업 설정을 서버에 만들지 않는 이유다 (deploy-plan.md 8절).
#
# 이 스크립트가 하지 않는 것(끝에 안내로 출력):
#   - iptables 개방 — OCI Ubuntu 이미지의 기본 REJECT 규칙은 위치가 이미지마다 달라
#     자동 삽입이 규칙을 REJECT 뒤에 넣는 사고를 낼 수 있다. 명령만 안내한다.
#   - Caddy 설치·도메인 설정 — 도메인은 사람이 정한다.
#   - GitHub Actions 공개키 등록 — 키는 이 저장소 밖에서 만들어진다.
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
    echo "root 로 실행해야 한다: sudo bash $0" >&2
    exit 1
fi

SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd)
BASE=/opt/nvserver

echo "[1/5] 배포 사용자 nvserver"
if ! id -u nvserver >/dev/null 2>&1; then
    useradd --system --create-home --shell /bin/bash nvserver
fi
# 배포 실패 시 deploy.sh 가 journalctl 로 기동 로그를 보여 준다 — 그 읽기 권한이다.
usermod -aG systemd-journal nvserver

echo "[2/5] 디렉터리 구조"
mkdir -p "$BASE/releases" "$BASE/shared" "$BASE/incoming"

echo "[3/5] 환경변수 파일 (이미 있으면 보존)"
if [ ! -f "$BASE/shared/nvserver.env" ]; then
    install -m 600 "$SCRIPT_DIR/nvserver.env.example" "$BASE/shared/nvserver.env"
    ENV_CREATED=1
fi
chown -R nvserver:nvserver "$BASE"

echo "[4/5] systemd 유닛과 health check 타이머"
install -m 644 "$SCRIPT_DIR/nvserver.service" /etc/systemd/system/nvserver.service
install -m 644 "$SCRIPT_DIR/nvserver-healthcheck.service" /etc/systemd/system/nvserver-healthcheck.service
install -m 644 "$SCRIPT_DIR/nvserver-healthcheck.timer" /etc/systemd/system/nvserver-healthcheck.timer
install -m 755 "$SCRIPT_DIR/nvserver-healthcheck.sh" "$BASE/nvserver-healthcheck.sh"
systemctl daemon-reload
# 시작은 하지 않는다 — current 링크가 아직 없어 첫 배포 전에는 뜰 수 없다.
systemctl enable nvserver >/dev/null 2>&1
systemctl enable --now nvserver-healthcheck.timer >/dev/null 2>&1

echo "[5/5] sudoers — 배포 사용자는 재시작 하나만 할 수 있다"
install -m 440 /dev/stdin /etc/sudoers.d/nvserver-deploy <<'SUDOERS'
nvserver ALL=(root) NOPASSWD: /usr/bin/systemctl restart nvserver
SUDOERS
visudo -cf /etc/sudoers.d/nvserver-deploy >/dev/null

cat <<'DONE'

완료. 남은 수작업 (순서대로):

 1. GitHub Actions 공개키 등록
      install -d -m 700 -o nvserver -g nvserver ~nvserver/.ssh
      echo '<ed25519 공개키>' >> ~nvserver/.ssh/authorized_keys
      chown nvserver:nvserver ~nvserver/.ssh/authorized_keys && chmod 600 ~nvserver/.ssh/authorized_keys

 2. OS 방화벽 — OCI Ubuntu 는 기본 iptables 에 REJECT 규칙이 있다.
    보안 목록(VCN)을 열어도 접속이 안 되면 십중팔구 이것이다.
      iptables -I INPUT -p tcp --dport 80 -j ACCEPT
      iptables -I INPUT -p tcp --dport 443 -j ACCEPT
      netfilter-persistent save
    (VCN 보안 목록의 22/80/443 인바운드 개방은 OCI 콘솔에서 별도로 한다. 5202 는 열지 않는다.)

 3. Caddy
      apt install caddy
      Caddyfile.example 을 /etc/caddy/Caddyfile 로 복사하고 도메인을 실제 값으로 바꾼 뒤
      systemctl reload caddy

 4. /opt/nvserver/shared/nvserver.env 의 Cors__AllowedOrigins__0 을 실제 오리진으로 바꾼다.

첫 배포는 main 푸시 또는 GitHub Actions 의 workflow_dispatch 로 한다.
DONE

if [ "${ENV_CREATED:-}" = "1" ]; then
    echo "※ $BASE/shared/nvserver.env 를 예시 값으로 만들었다 — 도메인을 수정해야 한다."
fi
