#!/usr/bin/env bash
# Keeps the dev WebApi alive during a long local test run.
#
# WHY THIS EXISTS: ProjectNotes/Future-Improvements.md item 187. .NET on macOS can die with an
# unhandled ArgumentException from IPEndPoint.Create on the socket accept path
# (dotnet/runtime#102663). It is not an application fault — it happens below any of our code, on
# a threadpool thread, so nothing in the app can catch it — and it takes the process with it. It
# killed the API nine times during one full Playwright run on 2026-08-25, which invalidated the
# run twice before anyone realised the failures were all "API was down" in disguise.
#
# The first mitigation is in the start scripts: bind 127.0.0.1 rather than localhost, so no IPv6
# listener exists and that accept path is never taken. This supervisor is the belt to that
# braces — use it when running the full suite, and check the restart count afterwards. A run with
# restarts in it is a run whose failures you cannot trust.
#
#   bash scripts/dev-api-supervisor.sh &        # then run the suite
#   grep -c restarting /tmp/ben-api-supervisor.log
set -uo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API_URL="${BEN_WEBAPI_URL:-http://127.0.0.1:5252}"
PORT="${API_URL##*:}"
LOG="${BEN_API_SUPERVISOR_LOG:-/tmp/ben-api-supervisor.log}"

echo "$(date +%H:%M:%S) supervising $API_URL — logging to $LOG"
while true; do
  if ! curl -sS -o /dev/null --max-time 3 "$API_URL/swagger/index.html" 2>/dev/null; then
    echo "$(date +%H:%M:%S) API down — restarting" | tee -a "$LOG"
    pkill -f "Ben.Data.WebApi.dll" 2>/dev/null
    while [ -n "$(lsof -ti:"$PORT" 2>/dev/null)" ]; do sleep 1; done
    (
      cd "$ROOT_DIR"
      ASPNETCORE_ENVIRONMENT=Development nohup dotnet run --no-build \
        --project Ben.Data.WebApi/Ben.Data.WebApi.csproj --urls "$API_URL" \
        >> "$LOG" 2>&1 &
    )
    for _ in $(seq 1 40); do
      sleep 2
      curl -sS -o /dev/null --max-time 3 "$API_URL/swagger/index.html" 2>/dev/null && break
    done
  fi
  sleep 5
done
