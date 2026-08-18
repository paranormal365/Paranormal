#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API_URL="${BEN_WEBAPI_URL:-http://localhost:5252}"

# Which front end to launch. Defaults to the original Telerik-skinned WebApp, which moved to
# :5079 when Ben.Web.Website took over :5078 (the port registered with Entra as a redirect URI
# and already allow-listed for CORS). start-website-with-api.sh sets these for the new site.
APP_PROJECT="${BEN_APP_PROJECT:-Ben.Web.WebApp/Ben.Web.WebApp.csproj}"
WEBAPP_URL="${BEN_WEBAPP_URL:-http://localhost:5079}"
API_PID_FILE="$ROOT_DIR/.vscode/.webapi.pid"

is_api_up() {
  curl -sS -o /dev/null --max-time 2 "$API_URL" && return 0
  curl -sS -o /dev/null --max-time 2 "$API_URL/swagger/index.html" && return 0
  return 1
}

start_api() {
  echo "[startup] WebApi not detected at $API_URL. Starting WebApi..."

  (
    cd "$ROOT_DIR"
    ASPNETCORE_ENVIRONMENT=Development \
    dotnet run --project Ben.Data.WebApi/Ben.Data.WebApi.csproj --urls "$API_URL" \
      > "$ROOT_DIR/.vscode/webapi.log" 2>&1 &
    echo $! > "$API_PID_FILE"
  )

  for _ in {1..60}; do
    if is_api_up; then
      echo "[startup] WebApi is running at $API_URL"
      return 0
    fi
    sleep 1
  done

  echo "[startup] WebApi failed to start in time. See .vscode/webapi.log"
  exit 1
}

if is_api_up; then
  echo "[startup] WebApi already running at $API_URL"
else
  start_api
fi

echo "[startup] Launching $APP_PROJECT at $WEBAPP_URL"

# Open the browser once the webapp is accepting connections.
# Runs as an orphaned background process so exec can replace this shell immediately.
(
  for _ in {1..60}; do
    sleep 1
    if curl -sS -o /dev/null --max-time 1 "$WEBAPP_URL" 2>/dev/null; then
      open "$WEBAPP_URL"
      break
    fi
  done
) &

cd "$ROOT_DIR"
exec env ASPNETCORE_ENVIRONMENT=Development \
  dotnet run --project "$APP_PROJECT" --urls "$WEBAPP_URL"
