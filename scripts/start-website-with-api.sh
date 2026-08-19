#!/usr/bin/env bash
set -euo pipefail

# Starts the WebApi (if it isn't already up) and then Ben.Web.Website — the SmartAdmin/Night
# front end — on :5078.
#
# :5078 matters specifically: it is the redirect URI registered with Entra
# (http://localhost:5078/signin-oidc) and an allow-listed CORS origin on the API, so Microsoft
# sign-in works here without touching the app registration.
#
# The startup logic used to live in a sibling start-webapp-with-api.sh, shared with the original
# Ben.Web.WebApp on :5079. That project and its script were removed in 1762dfc (see the
# pre-old-site-removal tag), which left this script exec'ing a file that no longer existed, so the
# logic is inlined here now.

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API_URL="${BEN_WEBAPI_URL:-http://localhost:5252}"
APP_PROJECT="${BEN_APP_PROJECT:-Ben.Web.Website/Ben.Web.Website.csproj}"
WEBSITE_URL="${BEN_WEBSITE_URL:-http://localhost:5078}"
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

echo "[startup] Launching $APP_PROJECT at $WEBSITE_URL"

# Open the browser once the site is accepting connections.
# Runs as an orphaned background process so exec can replace this shell immediately.
(
  for _ in {1..60}; do
    sleep 1
    if curl -sS -o /dev/null --max-time 1 "$WEBSITE_URL" 2>/dev/null; then
      open "$WEBSITE_URL"
      break
    fi
  done
) &

cd "$ROOT_DIR"
exec env ASPNETCORE_ENVIRONMENT=Development \
  dotnet run --project "$APP_PROJECT" --urls "$WEBSITE_URL"
