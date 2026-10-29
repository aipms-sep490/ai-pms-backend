#!/bin/bash
# ==============================================================================
# AI-PMS Staging True Rollback Script
# Location: deploy/wsl/rollback.sh
# Target Host: WSL khaine161 (staging)
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/compose.yaml"
ENV_FILE="$SCRIPT_DIR/.env"

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

TARGET_BACKUP_REF="${1:-aipms-api:staging-backup}"

echo "=========================================================="
echo " AI-PMS True Rollback Execution"
echo " Time: $(date -u +"%Y-%m-%dT%H:%M:%SZ")"
echo " Target Backup Reference: $TARGET_BACKUP_REF"
echo "=========================================================="

cd "$REPO_ROOT"

# 1. Verify Docker availability
if ! command -v docker >/dev/null 2>&1; then
    fail "Docker command not found in PATH."
    exit 1
fi

# 2. Resolve Backup Image ID / Digest
info "Step 1: Resolving backup image ID from reference '$TARGET_BACKUP_REF'..."
BACKUP_IMAGE_ID=$(docker inspect --format '{{.Id}}' "$TARGET_BACKUP_REF" 2>/dev/null || true)

if [[ -z "$BACKUP_IMAGE_ID" ]]; then
    fail "Backup image reference '$TARGET_BACKUP_REF' does not exist in Docker daemon."
    echo -e "  ${YELLOW}Available aipms-api backup images:${NC}"
    docker images aipms-api --format "table {{.Repository}}\t{{.Tag}}\t{{.ID}}\t{{.CreatedAt}}"
    exit 2
fi

pass "Found valid backup image ID: $BACKUP_IMAGE_ID"

# 3. Inspect Currently Running Container Image
info "Step 2: Inspecting currently deployed container..."
CURRENT_CONTAINER_ID=$(docker ps -q -f name=^/aipms-api$ || true)
CURRENT_IMAGE_ID=""

if [[ -n "$CURRENT_CONTAINER_ID" ]]; then
    CURRENT_IMAGE_ID=$(docker inspect --format '{{.Image}}' aipms-api)
    pass "Current running container image ID: $CURRENT_IMAGE_ID"
    if [[ "$CURRENT_IMAGE_ID" == "$BACKUP_IMAGE_ID" ]]; then
        warn "Current container is ALREADY running the backup image ID."
    fi
else
    warn "Container 'aipms-api' is not currently running. Will launch directly from backup image."
fi

# 4. Determine Active Tag from Environment / Compose
IMAGE_TAG="local"
if [[ -f "$ENV_FILE" ]]; then
    eval "$(grep -E '^AIPMS_IMAGE_TAG=' "$ENV_FILE" | sed 's/^/export /' || true)"
    IMAGE_TAG="${AIPMS_IMAGE_TAG:-local}"
fi
ACTIVE_IMAGE_NAME="aipms-api:${IMAGE_TAG}"

# 5. Retag Backup Image to Active Tag Name
info "Step 3: Retagging backup image ($BACKUP_IMAGE_ID) to active tag '$ACTIVE_IMAGE_NAME'..."
docker tag "$BACKUP_IMAGE_ID" "$ACTIVE_IMAGE_NAME"
pass "Retagged $BACKUP_IMAGE_ID -> $ACTIVE_IMAGE_NAME"

# 6. Recreate Container with --force-recreate and --no-build
info "Step 4: Recreating container 'aipms-api' from restored image..."
docker compose -f "$COMPOSE_FILE" up -d --force-recreate --no-build api

# 7. Strictly Verify Restored Image ID via docker inspect
info "Step 5: Verifying running container image ID after rollback..."
NEW_IMAGE_ID=$(docker inspect --format '{{.Image}}' aipms-api 2>/dev/null || echo "")

if [[ -z "$NEW_IMAGE_ID" ]]; then
    fail "Container 'aipms-api' failed to start after rollback."
    docker logs --tail 50 aipms-api
    exit 3
fi

if [[ "$NEW_IMAGE_ID" != "$BACKUP_IMAGE_ID" ]]; then
    fail "ROLLBACK FAILED: Container is running image $NEW_IMAGE_ID, expected $BACKUP_IMAGE_ID."
    exit 4
fi

pass "Container strictly verified running backup Image ID: $NEW_IMAGE_ID"

# 8. Postflight Verification
info "Step 6: Executing postflight verification on restored container..."
if [[ -f "$SCRIPT_DIR/postflight-verify.sh" ]]; then
    bash "$SCRIPT_DIR/postflight-verify.sh" || {
        warn "Postflight verification exited with warnings or non-zero. Check public ingress."
    }
fi

echo -e "${GREEN}==========================================================${NC}"
echo -e "${GREEN} ROLLBACK COMPLETED SUCCESSFULLY${NC}"
echo " Active Service:      aipms-api"
echo " Restored Image ID:   $NEW_IMAGE_ID"
echo -e "${GREEN}==========================================================${NC}"
exit 0
