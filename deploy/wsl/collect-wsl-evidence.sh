#!/bin/bash
# ==============================================================================
# AI-PMS Read-Only WSL Evidence Collector
# Location: deploy/wsl/collect-wsl-evidence.sh
# Target Host: WSL khaine161 (staging)
# Note: STRICTLY READ-ONLY. Does not modify or restart any infrastructure.
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/compose.yaml"
OUTPUT_FILE="${1:-/tmp/aipms-wsl-evidence-$(date +%Y%m%d_%H%M%S).json}"

echo "=========================================================="
echo " AI-PMS WSL Read-Only Evidence Collector"
echo " Time: $(date -u +"%Y-%m-%dT%H:%M:%SZ")"
echo " Output Destination: $OUTPUT_FILE"
echo "=========================================================="

cd "$REPO_ROOT"

# Helper for safe JSON string escaping
json_escape() {
    python3 -c 'import json,sys; print(json.dumps(sys.stdin.read().strip()))' 2>/dev/null || \
    sed 's/"/\\"/g' | tr -d '\n'
}

# 1. Environment & System Info
SYS_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
SYS_USER=$(whoami 2>/dev/null || echo "unknown")
SYS_HOST=$(hostname 2>/dev/null || echo "unknown")
SYS_KERNEL=$(uname -r 2>/dev/null || echo "unknown")

# 2. Git Information
GIT_BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "detached")
GIT_HEAD_SHA=$(git rev-parse HEAD 2>/dev/null || echo "unknown")
GIT_DIRTY_COUNT=$(git status --porcelain 2>/dev/null | wc -l || echo "0")
GIT_IS_CLEAN=$([ "$GIT_DIRTY_COUNT" -eq 0 ] && echo "true" || echo "false")

# 3. Docker Runtime Status
DOCKER_AVAILABLE="false"
DOCKER_VER="none"
COMPOSE_VER="none"

if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
    DOCKER_AVAILABLE="true"
    DOCKER_VER=$(docker version --format '{{.Server.Version}}' 2>/dev/null || echo "unknown")
    COMPOSE_VER=$(docker compose version --short 2>/dev/null || echo "unknown")
fi

# 4. Container Inspection (Redacted, no secrets)
CONTAINER_RUNNING="false"
CONTAINER_STATUS="none"
CONTAINER_HEALTH="none"
CONTAINER_IMAGE_ID="none"
CONTAINER_RUNTIME_SHA="none"
IMAGE_CREATED="none"

if [[ "$DOCKER_AVAILABLE" == "true" ]]; then
    CID=$(docker ps -q -f name=^/aipms-api$ 2>/dev/null || true)
    if [[ -n "$CID" ]]; then
        CONTAINER_RUNNING="true"
        CONTAINER_STATUS=$(docker inspect --format '{{.State.Status}}' aipms-api 2>/dev/null || echo "unknown")
        CONTAINER_HEALTH=$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' aipms-api 2>/dev/null || echo "none")
        CONTAINER_IMAGE_ID=$(docker inspect --format '{{.Image}}' aipms-api 2>/dev/null || echo "unknown")
        CONTAINER_RUNTIME_SHA=$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' aipms-api 2>/dev/null | grep '^COMMIT_SHA=' | cut -d= -f2- || echo "none")
        IMAGE_CREATED=$(docker inspect --format '{{.Created}}' "$CONTAINER_IMAGE_ID" 2>/dev/null || echo "unknown")
    fi
fi

# 5. Local Port Resolution & System Endpoint Query
LOCAL_RESP="{}"
LOCAL_PORT="${AIPMS_LOCAL_PORT:-5088}"
if [[ "$DOCKER_AVAILABLE" == "true" && "$CONTAINER_RUNNING" == "true" ]]; then
    RESOLVED_PORT=$(docker port aipms-api 8080 2>/dev/null | cut -d: -f2 || echo "")
    if [[ -n "$RESOLVED_PORT" ]]; then
        LOCAL_PORT="$RESOLVED_PORT"
    fi
fi

if command -v curl >/dev/null 2>&1; then
    LOCAL_RAW=$(curl -s -m 5 "http://127.0.0.1:${LOCAL_PORT}/api/v1/system" 2>/dev/null || echo "")
    if [[ -n "$LOCAL_RAW" ]] && echo "$LOCAL_RAW" | grep -q '"status"'; then
        LOCAL_RESP="$LOCAL_RAW"
    fi
fi

# 6. Public Endpoint Query
PUBLIC_RESP="{}"
if command -v curl >/dev/null 2>&1; then
    PUB_RAW=$(curl -s -m 10 "https://api-staging.khaidz.com/api/v1/system" 2>/dev/null || echo "")
    if [[ -n "$PUB_RAW" ]] && echo "$PUB_RAW" | grep -q '"status"'; then
        PUBLIC_RESP="$PUB_RAW"
    fi
fi

# 7. Check Config Files Existence (Status Only - Never Expose Secrets)
CONFIG_EXISTS="false"
if [[ -f "/home/khaine161/.config/aipms/appsettings.Staging.json" ]]; then
    CONFIG_EXISTS="true"
fi

# 8. Assemble Clean JSON Document
cat <<EOF > "$OUTPUT_FILE"
{
  "collectorVersion": "1.0.0",
  "collectedAt": "$SYS_TIME",
  "system": {
    "user": "$SYS_USER",
    "hostname": "$SYS_HOST",
    "kernel": "$SYS_KERNEL"
  },
  "git": {
    "branch": "$GIT_BRANCH",
    "headCommitSha": "$GIT_HEAD_SHA",
    "isClean": $GIT_IS_CLEAN,
    "uncommittedFilesCount": $GIT_DIRTY_COUNT
  },
  "docker": {
    "available": $DOCKER_AVAILABLE,
    "serverVersion": "$DOCKER_VER",
    "composeVersion": "$COMPOSE_VER"
  },
  "container": {
    "running": $CONTAINER_RUNNING,
    "status": "$CONTAINER_STATUS",
    "health": "$CONTAINER_HEALTH",
    "imageId": "$CONTAINER_IMAGE_ID",
    "imageCreated": "$IMAGE_CREATED",
    "runtimeCommitSha": "$CONTAINER_RUNTIME_SHA"
  },
  "endpoints": {
    "localResolvedPort": "$LOCAL_PORT",
    "localSystemResponse": $LOCAL_RESP,
    "publicSystemResponse": $PUBLIC_RESP
  },
  "configuration": {
    "stagingConfigFileFound": $CONFIG_EXISTS
  }
}
EOF

echo "Evidence collected successfully."
echo "File created: $OUTPUT_FILE"
echo "----------------------------------------------------------"
cat "$OUTPUT_FILE"
echo "----------------------------------------------------------"
exit 0
