#!/usr/bin/env bash
# start-webapi-only.sh
# Starts the WebApi (if not already running) then opens the Swagger UI.
# Designed to be run as the "start-webapi-swagger" VS Code task.
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API_URL="${BEN_WEBAPI_URL:-http://localhost:5252}"
SWAGGER_URL="$API_URL/swagger/index.html"
API_PID_FILE="$ROOT_DIR/.vscode/.webapi.pid"

is_api_up() {
  curl -sf -o /dev/null --max-time 2 "$SWAGGER_URL" && return 0
  return 1
}

# ── Start API if not already running ────────────────────────────────────────
echo "[startup] Checking WebApi at $API_URL..."

if is_api_up; then
  echo "[startup] WebApi already running at $API_URL"
else
  echo "[startup] Starting WebApi..."
  (
    cd "$ROOT_DIR"
    ASPNETCORE_ENVIRONMENT=Development \
    dotnet run --project Ben.Data.WebApi/Ben.Data.WebApi.csproj --urls "$API_URL" \
      > "$ROOT_DIR/.vscode/webapi.log" 2>&1 &
    echo $! > "$API_PID_FILE"
  )

  # Wait up to 60 s for Swagger to become available
  for _ in {1..60}; do
    if is_api_up; then
      echo "[startup] WebApi is running at $API_URL"
      break
    fi
    sleep 1
  done

  if ! is_api_up; then
    echo "[startup] WebApi failed to start. See .vscode/webapi.log"
    exit 1
  fi
fi

# ── Open Swagger UI ──────────────────────────────────────────────────────────
echo "[startup] Opening Swagger at $SWAGGER_URL"
open "$SWAGGER_URL"

echo "[swagger] Swagger UI launched — WebApi running at $API_URL"
