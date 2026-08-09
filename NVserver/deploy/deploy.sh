#!/usr/bin/env bash
# NVserver 배포 실행기 — 서버의 /opt/nvserver/deploy.sh 로 설치되어 실행된다.
# 저장소 NVserver/deploy/deploy.sh 가 원본이고, 워크플로가 배포마다 최신본을 올린다.
# 로직이 서버에 있어야 사람이 SSH 로 붙어도 같은 절차를 쓴다 (deploy-plan.md 5절).
#
#   deploy.sh <tarball>           # 전개 → current 전환 → 재시작 → health check, 실패 시 자동 롤백
#   deploy.sh rollback <릴리스>    # releases/ 의 기존 릴리스로 전환. 예: deploy.sh rollback 20260809-140000
set -euo pipefail

BASE=/opt/nvserver
RELEASES="$BASE/releases"
CURRENT="$BASE/current"
# nvserver.env 의 ASPNETCORE_URLS 와 짝이다 — 포트를 바꾸면 여기도 바꾼다.
HEALTH_URL="http://127.0.0.1:5202/health"
HEALTH_RETRIES=15
HEALTH_INTERVAL=2
KEEP_RELEASES=5

log() { echo "[deploy] $*"; }

switch_and_restart() {
    ln -sfn "$1" "$CURRENT"
    sudo systemctl restart nvserver
}

wait_healthy() {
    local i
    for i in $(seq 1 "$HEALTH_RETRIES"); do
        if curl -sf --max-time 3 "$HEALTH_URL" >/dev/null 2>&1; then
            return 0
        fi
        sleep "$HEALTH_INTERVAL"
    done
    return 1
}

prune_releases() {
    # 최신 KEEP_RELEASES 개만 남긴다. current 는 항상 최신 쪽이므로 지워질 일이 없다.
    ls -1dt "$RELEASES"/*/ 2>/dev/null | tail -n +$((KEEP_RELEASES + 1)) | while read -r old; do
        log "오래된 릴리스 정리: $old"
        rm -rf "$old"
    done
}

do_rollback() {
    local name="$1"
    # 릴리스 이름은 releases/ 바로 아래의 디렉터리 이름이다. 경로 조각을 받지 않는다.
    case "$name" in
        "" | */* | .*)
            log "잘못된 릴리스 이름: '$name'"
            exit 2
            ;;
    esac
    local target="$RELEASES/$name"
    if [ ! -d "$target" ]; then
        log "릴리스가 없다: $target"
        log "있는 릴리스:"
        ls -1t "$RELEASES" || true
        exit 1
    fi

    log "롤백: $name"
    switch_and_restart "$target"
    if wait_healthy; then
        log "롤백 완료 — health OK"
    else
        log "롤백했지만 health check 가 실패한다. 로그: journalctl -u nvserver -n 100"
        exit 1
    fi
}

do_deploy() {
    local tarball="$1"
    if [ ! -f "$tarball" ]; then
        log "tarball 이 없다: $tarball"
        exit 2
    fi

    local ts target prev
    ts=$(date +%Y%m%d-%H%M%S)
    target="$RELEASES/$ts"
    # 실패 시 돌아갈 곳을 전환 전에 기록해 둔다.
    prev=$(readlink -f "$CURRENT" 2>/dev/null || true)

    log "전개: $target"
    mkdir -p "$target"
    tar -xzf "$tarball" -C "$target"

    if [ ! -f "$target/app/Api" ]; then
        log "아티팩트에 app/Api 가 없다 — tar 구성이 잘못됐다"
        rm -rf "$target"
        exit 2
    fi
    chmod +x "$target/app/Api"

    log "current 전환 후 재시작"
    switch_and_restart "$target"

    if wait_healthy; then
        log "health OK — 배포 완료: $ts"
        rm -f "$tarball"
        prune_releases
        return 0
    fi

    log "health check 실패 — 기동 로그:"
    journalctl -u nvserver -n 40 --no-pager || true

    if [ -n "$prev" ] && [ -d "$prev" ] && [ "$prev" != "$target" ]; then
        log "이전 릴리스로 롤백: $prev"
        switch_and_restart "$prev"
        if wait_healthy; then
            log "롤백 완료 — 이전 릴리스가 살아 있다"
        else
            log "롤백 후에도 health check 실패 — 사람이 봐야 한다"
        fi
    else
        log "돌아갈 이전 릴리스가 없다 (첫 배포의 실패)"
    fi
    exit 1
}

case "${1:-}" in
    "")
        log "사용법: deploy.sh <tarball> | deploy.sh rollback <릴리스>"
        exit 2
        ;;
    rollback)
        do_rollback "${2:-}"
        ;;
    *)
        do_deploy "$1"
        ;;
esac
