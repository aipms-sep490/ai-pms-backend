#!/bin/bash
# ==============================================================================
# AI-PMS Staging Deployment Preflight & Orchestration Script
# Location: deploy/wsl/preflight-deploy.sh
# Target Host: WSL khaine161 (staging)
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/compose.yaml"
ENV_FILE="$SCRIPT_DIR/.env"

DRY_RUN=0
CHECK_ONLY=0
ALLOW_DIRTY=0
TARGET_SHA=""

# Color helpers
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

pass() { echo -e "  ${GREEN}[PASS]${NC} $1"; }
fail() { echo -e "  ${RED}[FAIL]${NC} $1"; }
warn() { echo -e "  ${YELLOW}[WARN]${NC} $1"; }
info() { echo -e "${CYAN}[INFO]${NC} $1"; }

usage() {
    cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Preflight safety checks and deployment automation for AI-PMS Staging.

Options:
  -n, --dry-run        Validate all preconditions, show plan, but do not build or deploy.
  -c, --check-only     Alias for --dry-run.
      --allow-dirty    Allow proceeding even if Git working tree has unstaged/untracked changes.
      --commit-sha SHA Override Git commit SHA (defaults to git rev-parse HEAD).
  -h, --help           Show this help message.

EOF
    exit 0
}

# Parse options
while [[ $# -gt 0 ]]; do
    case "$1" in
        -n|--dry-run) DRY_RUN=1; CHECK_ONLY=1; shift ;;
        -c|--check-only) DRY_RUN=1; CHECK_ONLY=1; shift ;;
        --allow-dirty) ALLOW_DIRTY=1; shift ;;
        --commit-sha) TARGET_SHA="$2"; shift 2 ;;
        -h|--help) usage ;;
        *) echo -e "${RED}Unknown option: $1${NC}"; usage ;;
    esac
done

echo "=========================================================="
echo " AI-PMS Staging Deployment Preflight"
echo " Time: $(date -u +"%Y-%m-%dT%H:%M:%SZ")"
echo " Mode: $( [ $CHECK_ONLY -eq 1 ] && echo "CHECK ONLY / DRY-RUN" || echo "DEPLOYMENT" )"
echo "=========================================================="

cd "$REPO_ROOT"

# 1. Git Repository & SHA Validation
info "Step 1: Checking Git repository status..."
if ! git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    fail "Not inside a Git repository."
    exit 1
fi

CURRENT_BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "detached")
GIT_HEAD_SHA=$(git rev-parse HEAD)

if [[ -z "$TARGET_SHA" ]]; then
    TARGET_SHA="$GIT_HEAD_SHA"
fi

if [[ ! "$TARGET_SHA" =~ ^[0-9a-fA-F]{7,40}$ ]]; then
    fail "Target commit SHA is invalid ($TARGET_SHA)."
    exit 1
fi

pass "Git branch: $CURRENT_BRANCH"
pass "Target commit SHA: $TARGET_SHA"

DIRTY_STATUS=$(git status --porcelain 2>/dev/null || true)
if [[ -n "$DIRTY_STATUS" ]]; then
    if [[ $ALLOW_DIRTY -eq 1 ]]; then
        warn "Git working tree has uncommitted changes (--allow-dirty specified):"
        echo "$DIRTY_STATUS" | sed 's/^/    /'
    else
        fail "Git working tree is dirty. Staged or modified files present:"
        echo "$DIRTY_STATUS" | sed 's/^/    /'
        echo -e "  ${YELLOW}Use --allow-dirty to override if testing local modifications.${NC}"
        exit 1
    fi
else
    pass "Git working tree is clean."
fi

# 2. Docker & Docker Compose Availability
info "Step 2: Checking Docker runtime & CLI availability..."
if ! command -v docker >/dev/null 2>&1; then
    fail "Docker command not found in PATH."
    exit 1
fi

if ! docker info >/dev/null 2>&1; then
    fail "Docker daemon is not running or current user lacks docker group permissions."
    exit 1
fi
DOCKER_VER=$(docker version --format '{{.Server.Version}}' 2>/dev/null || echo "unknown")
pass "Docker daemon active (version $DOCKER_VER)."

if ! docker compose version >/dev/null 2>&1; then
    fail "Docker Compose (v2) is not installed."
    exit 1
fi
COMPOSE_VER=$(docker compose version --short 2>/dev/null || echo "unknown")
pass "Docker Compose active (version $COMPOSE_VER)."

# 3. Compose File & Target Service Validation
info "Step 3: Checking Compose file and target service..."
if [[ ! -f "$COMPOSE_FILE" ]]; then
    fail "Compose file not found: $COMPOSE_FILE"
    exit 1
fi

if ! grep -q "api:" "$COMPOSE_FILE"; then
    fail "Service 'api' not defined in $COMPOSE_FILE."
    exit 1
fi
pass "Compose file verified: $COMPOSE_FILE (service 'api' present)."

# 4. External Networks Validation
info "Step 4: Checking required external networks..."
for net in go-exe_goexe-net aipms-data; do
    if docker network inspect "$net" >/dev/null 2>&1; then
        pass "External network exists: $net"
    else
        fail "Required external network '$net' is missing. Run deploy/wsl/prepare-network.sh first."
        exit 1
    fi
done

# 5. Environment & Configuration Check (Safe, No Secrets Leaked)
info "Step 5: Checking environment variables and configuration files..."
if [[ -f "$ENV_FILE" ]]; then
    pass "Found .env file at $ENV_FILE."
    # Source .env safely into subshell to verify keys
    eval "$(grep -E '^[A-Za-z0-9_]+=' "$ENV_FILE" | sed 's/^/export /')"
else
    warn "No local .env file found at $ENV_FILE (using environment variables)."
fi

CONFIG_FILE="${AIPMS_CONFIG_FILE:-/home/khaine161/.config/aipms/appsettings.Staging.json}"
FILES_PATH="${AIPMS_FILES_PATH:-/mnt/c/Users/khaih/AppData/Local/AIPMS/private-files}"

if [[ -f "$CONFIG_FILE" ]]; then
    pass "Staging config file exists and is accessible: $CONFIG_FILE"
else
    fail "Staging config file missing: $CONFIG_FILE"
    exit 1
fi

if [[ -d "$FILES_PATH" ]]; then
    pass "Upload files directory exists: $FILES_PATH"
else
    warn "Upload files directory does not exist or is not mounted: $FILES_PATH"
fi

# 6. Current Container Inspection & Exact Image Digest Backup
info "Step 6: Inspecting currently deployed container and image..."
RUNNING_CONTAINER=$(docker ps -q -f name=^/aipms-api$ || true)
RUNNING_IMAGE_ID=""

if [[ -n "$RUNNING_CONTAINER" ]]; then
    RUNNING_IMAGE_ID=$(docker inspect --format '{{.Image}}' aipms-api 2>/dev/null || true)
    CURRENT_ENV_SHA=$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' aipms-api 2>/dev/null | grep '^COMMIT_SHA=' | cut -d= -f2- || echo "")
    
    pass "Container 'aipms-api' is currently RUNNING."
    pass "Running image ID: $RUNNING_IMAGE_ID"
    if [[ -n "$CURRENT_ENV_SHA" ]]; then
        info "Currently reported runtime COMMIT_SHA: $CURRENT_ENV_SHA"
    else
        warn "Current container has no COMMIT_SHA environment variable set."
    fi
else
    warn "Container 'aipms-api' is not currently running (initial deploy or stopped)."
fi

# 7. Rollback Plan Preparation
echo "----------------------------------------------------------"
echo " Rollback Strategy Prepared"
echo "----------------------------------------------------------"
if [[ -n "$RUNNING_IMAGE_ID" ]]; then
    BACKUP_TAG="aipms-api:staging-backup-$(date +%Y%m%d_%H%M%S)"
    echo "  Backup Tag to Create: $BACKUP_TAG and aipms-api:staging-backup"
    echo "  Exact Rollback Command (in case of deployment failure):"
    echo "    docker tag aipms-api:staging-backup aipms-api:staging && docker compose -f $COMPOSE_FILE up -d --no-build api"
else
    echo "  No running container to backup. Rollback target would be git checkout develop."
fi
echo "----------------------------------------------------------"

if [[ $CHECK_ONLY -eq 1 ]]; then
    echo -e "${GREEN}==========================================================${NC}"
    echo -e "${GREEN} PREFLIGHT CHECKS PASSED — READY FOR DEPLOYMENT${NC}"
    echo " Target Commit: $TARGET_SHA"
    echo " Deployment Command to Execute:"
    echo "   export COMMIT_SHA=$TARGET_SHA"
    echo "   docker compose -f $COMPOSE_FILE build --build-arg COMMIT_SHA=\$COMMIT_SHA api"
    echo "   docker compose -f $COMPOSE_FILE up -d api"
    echo -e "${GREEN}==========================================================${NC}"
    exit 0
fi

# 8. Execution of Build & Deployment (Only if not in check-only mode)
info "Step 7: Executing container backup and build..."

if [[ -n "$RUNNING_IMAGE_ID" ]]; then
    info "Tagging running image ($RUNNING_IMAGE_ID) as backup..."
    docker tag "$RUNNING_IMAGE_ID" "$BACKUP_TAG"
    docker tag "$RUNNING_IMAGE_ID" "aipms-api:staging-backup"
    pass "Backup tag created: $BACKUP_TAG and aipms-api:staging-backup"
fi

info "Building service 'api' with COMMIT_SHA=$TARGET_SHA..."
export COMMIT_SHA="$TARGET_SHA"
docker compose -f "$COMPOSE_FILE" build --build-arg COMMIT_SHA="$COMMIT_SHA" api

info "Launching updated service 'api'..."
docker compose -f "$COMPOSE_FILE" up -d api

pass "Deployment finished. Run deploy/wsl/postflight-verify.sh to verify public API."
exit 0
