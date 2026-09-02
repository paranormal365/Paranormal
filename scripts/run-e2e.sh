#!/usr/bin/env bash
set -euo pipefail

# Runs the Playwright suite against a database and a file store of its own.
#
# WHY THIS EXISTS (item 200)
#
# The suite used to run against the shared dev database, which is also the one ishaunted.com
# uses. Two things followed from that, and both cost real time on 2026-08-27:
#
#   1. The suite WRITES on every run — posts, groups, events — so the database it tests against
#      drifts further from a fresh install with each run. Identical code failed two different
#      pairs of tests on consecutive runs. Worse, the drift MASKED a real bug: seeded groups were
#      being created without their roles, ladder and duties, and nobody could see it because the
#      backfill on the next startup covered for it. It only appeared on a genuinely fresh
#      database.
#
#   2. Localhost and the live site share that database but NOT their file storage. A photo
#      uploaded on the server leaves a row every local run can see and bytes no local run can
#      read, so /media/... answers 404 and any test asserting a thumbnail decodes fails with
#      nothing wrong in the code. Three of Ben's own profile photos did exactly this.
#
# So: its own database, its own uploads directory, seeded from scratch by the API's own seeders.
# What the suite tests is then the product, not the history of this machine.
#
# USAGE
#   scripts/run-e2e.sh                    # everything
#   scripts/run-e2e.sh --filter Nearby    # one slice, same isolation
#   scripts/run-e2e.sh --keep             # leave the hosts up afterwards to poke at
#   BEN_E2E_DB=OtherName scripts/run-e2e.sh   # a fresh name = a fresh database AND uploads dir
#
# It starts the hosts it needs and stops the ones it started. Hosts already running on those
# ports are left alone and REUSED — which is wrong for isolation, so it refuses instead and says
# so, rather than silently testing against whatever was there.

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

DB_NAME="${BEN_E2E_DB:-IsHauntedDb_e2e}"
SQL_SERVER="${BEN_E2E_SQL_SERVER:-192.168.1.71,1433}"
SQL_USER="${BEN_E2E_SQL_USER:-IsHaunted}"
SQL_PASSWORD="${BEN_E2E_SQL_PASSWORD:-ishaunted}"
CONN="Server=${SQL_SERVER};Database=${DB_NAME};User Id=${SQL_USER};Password=${SQL_PASSWORD};Encrypt=True;TrustServerCertificate=True;"

# Named AFTER the database, and that pairing is load-bearing. The database persists between runs,
# and once a seeded file has been migrated to disk its row carries a StoragePath and its bytes are
# no longer in the database — so deleting the uploads directory without the database leaves rows
# pointing at files that do not exist, and every thumbnail silently 404s. (Done exactly that on
# 2026-08-27 and spent a while reading it as a seeding bug.) Tying the name to the database means
# a fresh database always gets a fresh directory, and the two can never drift apart.
UPLOADS_DIR="$ROOT_DIR/.uploads-${DB_NAME}"

# BIND on 127.0.0.1; BROWSE on localhost. The two are deliberately different.
#
# "localhost" makes Kestrel open an IPv4 AND an IPv6 listener, and .NET on macOS has a bug in the
# IPv6 accept path (dotnet/runtime#102663) that kills the process from a threadpool thread — nine
# times in one run on 2026-08-25. Binding the address directly leaves only IPv4, so that accept
# path is never taken.
#
# All three hosts now bind IPv4, not just the API. The website and the WASM host had kept their
# IPv6 listeners on the grounds that neither had been SEEN to crash, which is an argument about
# observation rather than exposure: the API crashed because it takes the most connections, not
# because it was built differently.
#
# What clients ASK FOR stays "localhost", and it has to: :5078 is the redirect URI registered with
# Entra and an allow-listed CORS origin on the API, both of which are matched on the browser's URL.
# Nothing about those changes, because a browser asking for localhost still reaches an IPv4-only
# listener — it falls back from ::1 to 127.0.0.1.
#
# The readiness probes below deliberately use the localhost form. That makes startup itself the
# proof that the fallback works: if it ever stops, the run fails immediately with "never became
# ready" instead of dying strangely somewhere in the middle.
API_BIND="http://127.0.0.1:5252"
WEB_BIND="http://127.0.0.1:5078"
WASM_BIND="http://127.0.0.1:5180"

API_URL="http://localhost:5252"
WEB_URL="http://localhost:5078"
WASM_URL="http://localhost:5180"

KEEP=0
PASSTHROUGH=()
for arg in "$@"; do
  case "$arg" in
    --keep) KEEP=1 ;;
    *)      PASSTHROUGH+=("$arg") ;;
  esac
done

STARTED_PIDS=()
LOG_DIR="$(mktemp -d)"

cleanup() {
  if [[ $KEEP -eq 1 ]]; then
    echo ""
    echo "Hosts left running (--keep). Logs: $LOG_DIR"
    echo "  api  $API_URL   web  $WEB_URL   wasm $WASM_URL"
    echo "  database: $DB_NAME"
    return
  fi
  for pid in "${STARTED_PIDS[@]:-}"; do
    [[ -n "${pid:-}" ]] && kill "$pid" 2>/dev/null || true
  done
  # dotnet run spawns the real Kestrel process as a CHILD; killing the parent leaves it holding
  # the port, which then looks like "a host is already up" on the next run.
  pkill -f "Ben.Data.WebApi" 2>/dev/null || true
  pkill -f "Ben.Web.Website" 2>/dev/null || true
  pkill -f "Ben.Wasm.Video"  2>/dev/null || true
}
trap cleanup EXIT

port_busy() { curl -fsS -o /dev/null --max-time 2 "$1" 2>/dev/null; }

echo "── Checking the ports are free ─────────────────────────────────────────"
for url in "$API_URL/api/public/build" "$WEB_URL/" "$WASM_URL/"; do
  if port_busy "$url"; then
    echo "REFUSING: something is already serving ${url%%/api*}."
    echo "That host is pointed at whatever database it was started with — probably the shared one."
    echo "Running against it would defeat the isolation this script exists for. Stop it first:"
    echo "    pkill -f 'Ben.Data.WebApi'; pkill -f 'Ben.Web.Website'; pkill -f 'Ben.Wasm.Video'"
    exit 1
  fi
done

echo "── Migrating $DB_NAME ──────────────────────────────────────────────────"
# --connection, NOT an environment override. `dotnet run` honours
# ConnectionStrings__BenDbConnectionString; `dotnet ef` IGNORES it and silently uses the DEFAULT
# connection string — which is how the real IsHauntedDb got dropped once while a scratch database
# was the intended target. The flag is the only form that is actually obeyed.
dotnet ef database update \
  --project Ben.Data.Source --startup-project Ben.Data.WebApi \
  --connection "$CONN" >"$LOG_DIR/migrate.log" 2>&1 \
  || { echo "Migration failed:"; tail -20 "$LOG_DIR/migrate.log"; exit 1; }
echo "   schema is current"

mkdir -p "$UPLOADS_DIR"

start_host() {
  # bind_url is what Kestrel LISTENS on (IPv4); ready_url is what a client ASKS for (localhost).
  local name="$1" dir="$2" bind_url="$3" ready="$4" extra_env="$5"
  echo "── Starting $name ──────────────────────────────────────────────────────"
  (
    cd "$dir"
    # shellcheck disable=SC2086
    env ASPNETCORE_ENVIRONMENT=Development \
        ConnectionStrings__BenDbConnectionString="$CONN" \
        $extra_env \
        nohup dotnet run --no-launch-profile --urls "$bind_url" >"$LOG_DIR/$name.log" 2>&1 &
    echo $! >"$LOG_DIR/$name.pid"
  )
  STARTED_PIDS+=("$(cat "$LOG_DIR/$name.pid")")

  for _ in $(seq 1 90); do
    port_busy "$ready" && { echo "   $name ready"; return 0; }
    sleep 2
  done
  echo "$name never became ready. Last lines:"; tail -25 "$LOG_DIR/$name.log"
  exit 1
}

# Bound on the IPv4 address, probed on the localhost name — see the note by the URLs.
start_host api  "$ROOT_DIR/Ben.Data.WebApi"  "$API_BIND"  "$API_URL/api/public/build" \
  "FileStorage__RootPath=$UPLOADS_DIR"
start_host web  "$ROOT_DIR/Ben.Web.Website"  "$WEB_BIND"  "$WEB_URL/" ""
# dotnet.js, not "/": the WASM host answers 200 on its root while serving a stale or half-built
# framework, and eight video-editor tests then fail for reasons that look like product bugs.
start_host wasm "$ROOT_DIR/Ben.Wasm.Video"   "$WASM_BIND" "$WASM_URL/_framework/dotnet.js" ""

if grep -q "DATABASE IS BEHIND" "$LOG_DIR/api.log" 2>/dev/null; then
  echo "WARNING: the API says the schema is behind — see $LOG_DIR/api.log"
fi

echo "── Turning on the features the walks audit ─────────────────────────────"
# The site-wide walks discover routes from the app itself, so they visit /publications and
# /publications/{name}. Publications ships OFF by default ("Off until the feature ships"), and a
# gated-off section deliberately renders the ordinary "Page not found" body — a section that is
# off should look exactly like one that was never built. That is right, and it means the walk
# cannot tell a working gate from a broken route by looking at the page: both failed as
# "not routed".
#
# The gate's own behaviour is covered by unit tests (SiteFeatureFlagTests,
# FeatureFlagGatesSomethingTests, PublicationControllerTests), so switching it on here loses no
# coverage and gains the thing the walk exists for: actually rendering those pages. This is
# exactly what an isolated database buys — a configuration can be set for the run without
# touching anything Ben ships.
SA_EMAIL="${BEN_E2E_ADMIN_EMAIL:-haveben@msn.com}"
SA_PASSWORD="${BEN_E2E_ADMIN_PASSWORD:?set BEN_E2E_ADMIN_PASSWORD — the seeded password is no longer in this repo}"
SA_TOKEN=$(curl -fsS -X POST "$API_URL/login" -H "Content-Type: application/json" \
  -d "{\"email\":\"$SA_EMAIL\",\"password\":\"$SA_PASSWORD\"}" 2>/dev/null \
  | python3 -c "import sys,json;print(json.load(sys.stdin).get('accessToken',''))" 2>/dev/null || true)

if [[ -z "${SA_TOKEN:-}" ]]; then
  echo "   could not sign in as $SA_EMAIL — leaving features at their defaults."
  echo "   (walks that visit a gated section will report it as 'not routed')"
else
  for feature in features.publications; do
    code=$(curl -fsS -o /dev/null -w "%{http_code}" -X PUT \
      "$API_URL/api/admin/site-settings/$feature" \
      -H "Authorization: Bearer $SA_TOKEN" -H "Content-Type: application/json" \
      -d '{"value":"true"}' 2>/dev/null || echo "000")
    echo "   $feature -> $code"
  done
  # The website caches the feature snapshot, so give it a moment to notice.
  sleep 3
fi

echo ""
echo "── Running the suite ───────────────────────────────────────────────────"
echo "   database: $DB_NAME"
echo "   uploads : $UPLOADS_DIR"
echo ""

# -p:IsTestProject=true is NOT optional: the csproj sets it false to stay out of the solution's
# test run, and without the override `dotnet test` finds zero tests and EXITS 0 — a silent pass
# that has been reported as a real one before.
set +e
dotnet test Ben.Web.Playwright -p:IsTestProject=true -c Release --nologo \
  -e BEN_BASE_URL="$WEB_URL" "${PASSTHROUGH[@]:-}" 2>&1 | tee "$LOG_DIR/e2e.log"
STATUS=${PIPESTATUS[0]}
set -e

echo ""
echo "── Result ──────────────────────────────────────────────────────────────"
grep -E "Passed!|Failed!" "$LOG_DIR/e2e.log" | tail -1 || echo "no summary line — did anything run?"
grep -E "^  Failed " "$LOG_DIR/e2e.log" | sed 's/\[.*//' | head -20 || true
echo ""
echo "Logs: $LOG_DIR"
exit "$STATUS"
