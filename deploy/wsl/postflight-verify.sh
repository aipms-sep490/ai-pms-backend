#!/bin/bash
# ==============================================================================
# AI-PMS Staging Post-Deployment Verification Script
# Location: deploy/wsl/postflight-verify.sh
# Target Host: WSL khaine161 (staging)
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

PUBLIC_URL="${1:-https://api-staging.khaidz.com}"
LOCAL_PORT="${AIPMS_LOCAL_PORT:-5088}"
LOCAL_URL="http://127.0.0.1:${LOCAL_PORT}"

# Color helpers
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

pass() { echo -e "  ${GREEN}[PASS]${NC} $1"; }
fail() { echo -e "  ${RED}[FAIL]${NC} $1"; }
warn() { echo -e "  ${YELLOW}[WARN]${NC} $1"; }
info() { echo -e "${CYAN}[INFO]${NC} $1"; }

echo "=========================================================="
echo " AI-PMS Post-Deployment Verification"
echo " Time: $(date -u +"%Y-%m-%dT%H:%M:%SZ")"
echo " Local URL:  $LOCAL_URL"
echo " Public URL: $PUBLIC_URL"
echo "=========================================================="

cd "$REPO_ROOT"
EXPECTED_SHA=$(git rev-parse HEAD 2>/dev/null || echo "")

info "Step 1: Inspecting running Docker container 'aipms-api'..."
if ! command -v docker >/dev/null 2>&1; then
    warn "Docker command not available in this shell. Skipping local container inspection."
    CONTAINER_RUNNING="unknown"
    RUNTIME_ENV_SHA="unknown"
    CONTAINER_IMAGE_ID="unknown"
else
    CONTAINER_ID=$(docker ps -q -f name=^/aipms-api$ || true)
    if [[ -z "$CONTAINER_ID" ]]; then
        fail "Container 'aipms-api' is NOT RUNNING."
        docker ps -a -f name=^/aipms-api$ --format "table {{.Names}}\t{{.Status}}\t{{.Image}}"
        exit 1
    fi

    CONTAINER_STATUS=$(docker inspect --format '{{.State.Status}}' aipms-api)
    CONTAINER_HEALTH=$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' aipms-api)
    CONTAINER_IMAGE_ID=$(docker inspect --format '{{.Image}}' aipms-api)
    RUNTIME_ENV_SHA=$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' aipms-api | grep '^COMMIT_SHA=' | cut -d= -f2- || echo "")

    pass "Container state: $CONTAINER_STATUS (Health: $CONTAINER_HEALTH)"
    pass "Container Image ID: $CONTAINER_IMAGE_ID"
    pass "Runtime ENV COMMIT_SHA: ${RUNTIME_ENV_SHA:-<EMPTY>}"
fi

# Step 2: Query Local Container Port (if accessible)
info "Step 2: Testing local container endpoint ($LOCAL_URL/api/v1/system)..."
LOCAL_SHA=""
if command -v curl >/dev/null 2>&1; then
    LOCAL_RESP=$(curl -s -m 5 "$LOCAL_URL/api/v1/system" 2>/dev/null || echo "")
    if [[ -n "$LOCAL_RESP" ]]; then
        LOCAL_SHA=$(echo "$LOCAL_RESP" | grep -o '"commitSha":"[^"]*"' | cut -d'"' -f4 || echo "")
        pass "Local endpoint responded: commitSha='$LOCAL_SHA'"
    else
        warn "Local port $LOCAL_PORT did not respond directly (may be running behind Cloudflare or isolated network)."
    fi
fi

# Step 3: Query Public Staging Endpoint
info "Step 3: Testing public staging endpoint ($PUBLIC_URL/api/v1/system)..."
PUBLIC_RESP=$(curl -s -m 10 "$PUBLIC_URL/api/v1/system" 2>/dev/null || echo "")

if [[ -z "$PUBLIC_RESP" ]]; then
    fail "Could not connect to public staging endpoint: $PUBLIC_URL/api/v1/system"
    exit 2
fi

PUBLIC_STATUS=$(echo "$PUBLIC_RESP" | grep -o '"status":"[^"]*"' | cut -d'"' -f4 || echo "")
PUBLIC_SHA=$(echo "$PUBLIC_RESP" | grep -o '"commitSha":"[^"]*"' | cut -d'"' -f4 || echo "")

if [[ "$PUBLIC_STATUS" != "ok" ]]; then
    fail "Public system status is '$PUBLIC_STATUS' (expected 'ok'). Payload: $PUBLIC_RESP"
    exit 3
fi
pass "Public system status: ok"

if [[ -z "$PUBLIC_SHA" ]]; then
    fail "Public response does not contain 'commitSha'. Payload: $PUBLIC_RESP"
    exit 4
fi

# Check for fallback version
if [[ "$PUBLIC_SHA" =~ ^[0-9]+\.[0-9]+\.[0-9]+ ]]; then
    fail "Public endpoint returned build version fallback '$PUBLIC_SHA' instead of a Git SHA."
    echo -e "  ${YELLOW}Cause: Image was built without '--build-arg COMMIT_SHA=...'${NC}"
    echo -e "  ${YELLOW}Remediation: Re-run deploy/wsl/preflight-deploy.sh or manually pass --build-arg COMMIT_SHA=\$(git rev-parse HEAD)${NC}"
    exit 5
fi

pass "Public endpoint commitSha: $PUBLIC_SHA"

# Step 4: Verification & Diagnostic Reconciliation
echo "=========================================================="
echo " Verification Summary"
echo " Expected Git SHA:    ${EXPECTED_SHA:-<not set>}"
echo " Container Env SHA:   ${RUNTIME_ENV_SHA:-<not checked>}"
echo " Local Endpoint SHA:  ${LOCAL_SHA:-<not checked>}"
echo " Public Endpoint SHA: $PUBLIC_SHA"
echo "=========================================================="

if [[ -n "$EXPECTED_SHA" ]]; then
    # Case-insensitive comparison
    EXP_LOWER=$(echo "$EXPECTED_SHA" | tr '[:upper:]' '[:lower:]')
    PUB_LOWER=$(echo "$PUBLIC_SHA" | tr '[:upper:]' '[:lower:]')

    if [[ "$PUB_LOWER" == "$EXP_LOWER"* ]] || [[ "$EXP_LOWER" == "$PUB_LOWER"* ]]; then
        echo -e "${GREEN}==========================================================${NC}"
        echo -e "${GREEN} POST-DEPLOYMENT VERIFICATION: SUCCESS (PASS)${NC}"
        echo " Deployed and verified commit SHA matches expected: $PUBLIC_SHA"
        echo -e "${GREEN}==========================================================${NC}"
        exit 0
    else
        fail "SHA MISMATCH: Public API returned $PUBLIC_SHA, but Git HEAD is $EXPECTED_SHA."
        if [[ -n "$LOCAL_SHA" && "$LOCAL_SHA" != "$PUBLIC_SHA" ]]; then
            warn "Local port returned $LOCAL_SHA while public returned $PUBLIC_SHA."
            echo -e "  ${YELLOW}Diagnostic: Cloudflare Tunnel may still be routing to a previous container or caching.${NC}"
            echo -e "  ${YELLOW}Remediation: Check cloudflared service on WSL: sudo systemctl restart cloudflared${NC}"
        else
            echo -e "  ${YELLOW}Diagnostic: Container image was built from a different commit.${NC}"
            echo -e "  ${YELLOW}Remediation: Rebuild image on WSL from current HEAD:${NC}"
            echo "    export COMMIT_SHA=\$(git rev-parse HEAD)"
            echo "    docker compose -f deploy/wsl/compose.yaml build --build-arg COMMIT_SHA=\$COMMIT_SHA api"
            echo "    docker compose -f deploy/wsl/compose.yaml up -d api"
        fi
        exit 6
    fi
fi

pass "Public endpoint returned valid Git SHA ($PUBLIC_SHA)."
exit 0
