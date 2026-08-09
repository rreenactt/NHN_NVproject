#!/usr/bin/env bash
# NVclient(WebGL 정적 사이트) 배포 실행기. 워크플로가 배포마다 /tmp 로 올려 실행하고,
# 저장소 NVproject/deploy/client-deploy.sh 가 원본이다. 서버판 NVserver/deploy/deploy.sh 와
# 같은 릴리스 디렉터리 + 심볼릭 링크 구조의 축소판이다 — 정적 파일이라 재시작할 서비스가
# 없고, health check 도 전개물 검증으로 줄어든다. 웹 서버는 이미 있는 Caddy 가 겸한다
# (NVserver/deploy/Caddyfile.example 의 play 블록 참고). 설계는 docs/client-deploy-plan.md.
#
#   client-deploy.sh <tarball>          # 전개 → 검증 → current 전환. 검증 실패 시 이전 버전 유지
#   client-deploy.sh rollback <릴리스>   # releases/ 의 기존 릴리스로 전환. 예: rollback 20260810-140000
#
# 부트스트랩은 디렉터리 하나뿐이다 — 계정도 systemd 유닛도 만들지 않는다.
# sudo 는 /opt 아래 첫 디렉터리 생성에만 쓴다.
set -euo pipefail

BASE=/opt/nvclient
RELEASES="$BASE/releases"
CURRENT="$BASE/current"
# 서버는 5개를 남기지만 WebGL 산출물이 더 크고, 정적 파일 롤백은 어차피 직전
# 버전으로 가는 것이 대부분이다 — 프리티어 디스크를 아낀다 (client-deploy-plan.md 4절).
KEEP_RELEASES=3

log() { echo "[client-deploy] $*"; }

ensure_dirs() {
    if [ ! -d "$BASE" ]; then
        log "부트스트랩: $BASE 생성"
        sudo mkdir -p "$BASE"
        sudo chown "$(id -un):$(id -gn)" "$BASE"
    fi
    mkdir -p "$RELEASES"
}

switch_current() {
    # Caddy 는 요청마다 심볼릭 링크를 따라가므로 전환 즉시 다음 요청이 새 빌드를 받는다.
    ln -sfn "$1" "$CURRENT"
}

prune_releases() {
    # 최신 KEEP_RELEASES 개만 남긴다. 배포가 성공했을 때만 불린다 — 실패한 배포가
    # 성한 릴리스를 지우면 안 된다.
    #
    # 정렬은 mtime(-t)이 아니라 이름이다. 릴리스 이름이 곧 타임스탬프이고, tar 는
    # 아카이브에 실린 mtime 을 복원하므로 mtime 은 배포 순서를 보증하지 않는다 —
    # 아카이브에 ./ 항목이 있으면 릴리스 디렉터리 자신의 mtime 까지 빌드 시점으로
    # 덮여서, mtime 정렬은 방금 배포한 릴리스를 "가장 오래된 것" 으로 읽을 수 있다.
    # 그리고 current 가 가리키는 릴리스는 어떤 계산 결과가 나와도 지우지 않는다.
    local live
    live=$(readlink -f "$CURRENT" 2>/dev/null || true)
    ls -1d "$RELEASES"/*/ 2>/dev/null | sort -r | tail -n +$((KEEP_RELEASES + 1)) | while read -r old; do
        if [ -n "$live" ] && [ "$(readlink -f "$old")" = "$live" ]; then
            log "current 가 가리키는 릴리스는 남긴다: $old"
            continue
        fi
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
    switch_current "$target"
    log "롤백 완료 — 심볼릭 링크 전환뿐이라 재시작도 대기도 없다"
}

do_deploy() {
    local tarball="$1"
    if [ ! -f "$tarball" ]; then
        log "tarball 이 없다: $tarball"
        exit 2
    fi

    ensure_dirs

    local ts target
    ts=$(date +%Y%m%d-%H%M%S)
    target="$RELEASES/$ts"

    log "전개: $target"
    mkdir -p "$target"
    tar -xzf "$tarball" -C "$target"

    # 검증은 정적이다 — Unity WebGL 산출물의 두 필수 항목. 실패해도 current 는 아직
    # 건드리지 않았으므로 이전 버전이 그대로 서비스 중이고, 이 배포는 없었던 일이 된다.
    if [ ! -f "$target/index.html" ] || [ ! -d "$target/Build" ]; then
        log "산출물이 WebGL 빌드가 아니다 (index.html 또는 Build/ 없음) — 전개물을 버린다"
        rm -rf "$target"
        exit 1
    fi

    # Caddy(caddy 계정)가 읽어야 한다. /opt 아래라 world-read 로 충분하다.
    chmod -R a+rX "$target"

    switch_current "$target"
    log "배포 완료: $ts"

    rm -f "$tarball"
    prune_releases

    # 수동 운영(롤백)을 위해 최신 자신을 표준 위치에 남긴다 — 서버판과 같은 규칙.
    cp "$0" "$BASE/deploy.sh" 2>/dev/null || true
}

case "${1:-}" in
    "")
        log "사용법: client-deploy.sh <tarball> | client-deploy.sh rollback <릴리스>"
        exit 2
        ;;
    rollback)
        do_rollback "${2:-}"
        ;;
    *)
        do_deploy "$1"
        ;;
esac
