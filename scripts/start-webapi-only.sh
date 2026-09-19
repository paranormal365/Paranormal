#!/usr/bin/env bash
# start-webapi-only.sh
# Starts the WebApi (if not already running), polls until Swagger is ready,
# opens the browser, then stays alive so the VS Code task keeps the process running.
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# 127.0.0.1, not localhost — see the note in start-website-with-api.sh and item 187: binding
# "localhost" opens an IPv6 listener too, and .NET's IPv6 accept path on macOS kills the process
# (dotnet/runtime#102663). Clients using "localhost" still reach this.
API_URL="${BEN_WEBAPI_URL:-http://127.0.0.1:5252}"
SWAGGER_URL="$API_URL/swagger/index.html"
LOG="$ROOT_DIR/.vscode/webapi.log"

is_api_up() {
  curl -sf -o /dev/null --max-time 2 "$SWAGGER_URL" 2>/dev/null
}

echo "[startup] Checking WebApi at $API_URL..."

# ── Already running ──────────────────────────────────────────────────────────
if is_api_up; then
  echo "[startup] WebApi already running at $API_URL"
  open "$SWAGGER_URL"
  echo "[swagger] Swagger UI launched — following log (Ctrl+C to stop)"
  # Keep the task alive by tailing the log
  tail -f "$LOG"
  exit 0
fi

# ── Start WebApi in background ───────────────────────────────────────────────
echo "[startup] Starting WebApi..."
ASPNETCORE_ENVIRONMENT=Development \
  dotnet run --project "$ROOT_DIR/Ben.Data.WebApi/Ben.Data.WebApi.csproj" \
    --urls "$API_URL" > "$LOG" 2>&1 &

WEBAPI_PID=$!
echo "[startup] WebApi started (PID $WEBAPI_PID)"

# ── Poll until Swagger responds ──────────────────────────────────────────────
for _ in {1..90}; do
  # Abort if the process died
  if ! kill -0 "$WEBAPI_PID" 2>/dev/null; then
    echo "[startup] WebApi process exited unexpectedly. Last lines:"
    tail -20 "$LOG"
    exit 1
  fi

  if is_api_up; then
    echo "[startup] Swagger is ready at $SWAGGER_URL"
    open "$SWAGGER_URL"
    echo "[swagger] Swagger UI launched — following log (Ctrl+C to stop task)"
    break
  fi

  sleep 1
done

if ! is_api_up; then
  echo "[startup] Timeout — WebApi did not start within 90s. Last lines:"
  tail -20 "$LOG"
  exit 1
fi

# ── Keep task alive ──────────────────────────────────────────────────────────
# 'wait' blocks until dotnet run exits. This prevents VS Code from killing
# the WebApi process when the task shell would otherwise exit.
wait "$WEBAPI_PID"
