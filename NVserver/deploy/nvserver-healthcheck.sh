#!/usr/bin/env bash
# 살아 있지만 응답하지 않는 프로세스(교착, 소켓 고갈)를 잡는다 — deploy-plan.md 6절의 둘째 겹.
# 크래시는 systemd 의 Restart=always 가, 배포 직후 기동 실패는 deploy.sh 의 롤백이 담당한다.
# nvserver-healthcheck.timer 가 1분 주기로 root 로 실행한다.
set -u

STATE=/run/nvserver-healthcheck.fails
THRESHOLD=3
URL="http://127.0.0.1:5202/health"

# 서비스가 돌고 있지 않으면 판단하지 않는다 — 첫 배포 전이거나 systemd 가 이미 재시작 중이다.
if ! systemctl is-active --quiet nvserver; then
    rm -f "$STATE"
    exit 0
fi

if curl -sf --max-time 5 "$URL" >/dev/null 2>&1; then
    rm -f "$STATE"
    exit 0
fi

FAILS=$(($(cat "$STATE" 2>/dev/null || echo 0) + 1))
if [ "$FAILS" -ge "$THRESHOLD" ]; then
    echo "health check ${FAILS}회 연속 실패 — nvserver 재시작"
    systemctl restart nvserver
    rm -f "$STATE"
else
    echo "health check 실패 ${FAILS}/${THRESHOLD}"
    echo "$FAILS" > "$STATE"
fi
