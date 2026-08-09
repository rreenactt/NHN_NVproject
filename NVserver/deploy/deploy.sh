#!/usr/bin/env bash
# NVserver 배포 실행기. 워크플로가 배포마다 /tmp 로 올려 실행하고, 저장소
# NVserver/deploy/deploy.sh 가 원본이다. 로직이 서버에 있어야 사람이 SSH 로 붙어도
# 같은 절차를 쓴다 (deploy-plan.md 5절) — 그래서 성공한 배포는 자신을
# /opt/nvserver/deploy.sh 에도 남긴다.
#
#   deploy.sh <tarball>           # 전개 → current 전환 → 재시작 → health check, 실패 시 자동 롤백
#   deploy.sh rollback <릴리스>    # releases/ 의 기존 릴리스로 전환. 예: deploy.sh rollback 20260809-140000
#
# 사전 준비가 없는 서버도 배포할 수 있다. 디렉터리·앱 계정·환경변수 파일·systemd 유닛·
# health check 타이머가 없으면 tarball 에 실린 deploy/ 의 파일로 만들고, 있으면 절대
# 건드리지 않는다. 부트스트랩은 sudo 를 쓰므로 첫 배포의 계정에는 sudo 가 있어야 한다
# (OCI 의 ubuntu 기본). 이미 구성된 서버에서는 부트스트랩이 할 일이 없어 sudo 사용이
# systemctl restart 하나로 줄고, 그것이 최소 권한 배포 계정(readme 참고)의 전제다.
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

# 첫 배포의 빈 서버를 준비한다. 이미 있는 것은 전부 건너뛴다.
ensure_dirs() {
    if [ ! -d "$BASE" ]; then
        log "부트스트랩: $BASE 생성"
        sudo mkdir -p "$BASE"
        sudo chown "$(id -un):$(id -gn)" "$BASE"
    fi
    mkdir -p "$RELEASES" "$BASE/shared"
}

# 전개된 릴리스의 deploy/ 를 출처로, 없는 설정만 만든다. $1 = 릴리스 경로.
ensure_config() {
    local src="$1/deploy"

    # 앱 실행 계정 — 유닛의 User= 가 이 계정이다. 배포 계정과는 별개다.
    if ! id -u nvserver >/dev/null 2>&1; then
        log "부트스트랩: 앱 계정 nvserver 생성"
        sudo useradd --system --create-home --shell /bin/bash nvserver
    fi

    # 환경변수 파일 — 없을 때만 예시로 만든다. 있는 것은 배포가 절대 덮어쓰지 않는다.
    if [ ! -f "$BASE/shared/nvserver.env" ]; then
        log "부트스트랩: 환경변수 파일 생성 (예시값 — CORS 오리진은 도메인이 정해지면 수정)"
        sudo install -m 600 -o "$(id -un)" "$src/nvserver.env.example" "$BASE/shared/nvserver.env"
    fi

    # systemd 유닛 — 없을 때만 설치한다. 이후의 유닛 변경은 손으로 하고 daemon-reload.
    if [ ! -f /etc/systemd/system/nvserver.service ]; then
        log "부트스트랩: systemd 유닛 설치"
        sudo install -m 644 "$src/nvserver.service" /etc/systemd/system/nvserver.service
        sudo systemctl daemon-reload
        sudo systemctl enable nvserver >/dev/null 2>&1
    fi

    # health check 타이머 — 교착(살아 있지만 응답 없음) 감시. 크래시는 유닛의
    # Restart=always 가 이미 담당하므로, 이것까지 있어야 자동 복구가 두 겹이 된다.
    if [ ! -f /etc/systemd/system/nvserver-healthcheck.timer ]; then
        log "부트스트랩: health check 타이머 설치"
        sudo install -m 755 "$src/nvserver-healthcheck.sh" "$BASE/nvserver-healthcheck.sh"
        sudo install -m 644 "$src/nvserver-healthcheck.service" /etc/systemd/system/nvserver-healthcheck.service
        sudo install -m 644 "$src/nvserver-healthcheck.timer" /etc/systemd/system/nvserver-healthcheck.timer
        sudo systemctl daemon-reload
        sudo systemctl enable --now nvserver-healthcheck.timer >/dev/null 2>&1
    fi

    # 수동 운영(롤백 등)을 위해 최신 자신을 표준 위치에 남긴다.
    cp "$src/deploy.sh" "$BASE/deploy.sh" 2>/dev/null || true
}

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

    ensure_dirs

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

    ensure_config "$target"

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
