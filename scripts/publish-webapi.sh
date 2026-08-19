#!/usr/bin/env bash
# Publishes the WebApi for IIS.
#
#   scripts/publish-webapi.sh <sql-connection-string> [file-storage-root]
#
# Output: artifacts/webapi/
#
# The API is mounted as an IIS APPLICATION under the site, at /webapi — not at the site root, which
# the website now occupies, and not at /api, which would collide with the API's own route prefix
# and give you /api/api/cases. ASP.NET Core learns its path base from IIS, so no code changes:
# a controller at /api/cases answers on https://ishaunted.com/webapi/api/cases.
#
# NOT set here, on purpose:
#
#   Smtp__Password   A secret. Set it as an environment variable on the server (see
#                    docs/deploy-production.md). It is deliberately absent from every appsettings
#                    file in this repository and must stay that way.
set -euo pipefail

SQL_CONN="${1:-}"
FILE_ROOT="${2:-}"
STDOUT_LOG="${3:-0}"     # pass 1 during bring-up to capture startup exceptions
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/artifacts/webapi"

if [ -z "$SQL_CONN" ]; then
    echo "usage: scripts/publish-webapi.sh <sql-connection-string> [file-storage-root]" >&2
    echo >&2
    echo "The connection string is not optional: the API is the data layer, and every endpoint" >&2
    echo "fails without it. Pass __SET_ME__ deliberately if you want to fill it in on the server." >&2
    exit 2
fi

echo "Publishing the WebApi"
echo "  SQL         : ${SQL_CONN:0:40}..."
echo "  file storage: ${FILE_ROOT:-(unset — uploads fall back to the database blob column)}"
echo "  output      : $OUT"
echo

rm -rf "$OUT"
# -r win-x64 with --self-contained false: framework-dependent (the Hosting Bundle supplies
# the runtime) but RID-specific, so runtimes/ carries only the native assets Windows
# actually loads. Without the RID the publish copies every platform's natives —
# SkiaSharp, the SQL client and friends for linux, macOS and arm — which is how this
# package reached 488 MB, of which 444 MB could never execute on the target.
dotnet publish "$ROOT/Ben.Data.WebApi" -c Release -r win-x64 --self-contained false \
  -o "$OUT" --nologo -v q

[ -f "$OUT/Ben.Data.WebApi.dll" ] || { echo "publish produced no Ben.Data.WebApi.dll" >&2; exit 1; }
[ -f "$OUT/web.config" ]          || { echo "publish produced no web.config — IIS cannot host this" >&2; exit 1; }

# UAT, not production: this server is a persistent environment Ben develops against, so it
# wants the settings he actually runs with. The first cut of this script wrote a minimal
# production config and stripped the Telerik licence, the Geocodio key, the Entra registration
# and AppBaseUrl — every one of which turns a feature off silently rather than loudly.
python3 "$ROOT/scripts/uat-webapi-config.py" \
    "$ROOT/Ben.Data.WebApi/appsettings.Development.json" \
    "$OUT/appsettings.json" \
    "$SQL_CONN" "$FILE_ROOT" "https://ishaunted.com"

# Both environment-specific files go. Development points at a laptop; Production is no longer
# written at all, since the settings now live in the base file that loads regardless of what
# ASPNETCORE_ENVIRONMENT says on the server.
rm -f "$OUT/appsettings.Development.json" "$OUT/appsettings.Production.json"

# Startup logging. A .NET app that dies before its own logger exists reports HTTP 500.30 and
# nothing else — the exception goes to stdout, which IIS discards unless this is on. The log
# directory has to exist too: the module does not create it, and a missing folder silently
# disables the log you just switched on. Off by default because it grows without bound; worth
# having during bring-up, when the only question is why the thing will not start.
if [ "$STDOUT_LOG" = "1" ]; then
    mkdir -p "$OUT/logs"
    sed -i "" 's/stdoutLogEnabled="false"/stdoutLogEnabled="true"/' "$OUT/web.config"
    grep -q 'stdoutLogEnabled="true"' "$OUT/web.config" \
        || { echo "failed to enable stdout logging in web.config" >&2; exit 1; }
    echo "  startup logging ON -> logs\\stdout*.log (turn it off once the site is up)"
fi

echo
echo "Checks:"
echo "  web.config present (AspNetCoreModuleV2)"
echo "  $(du -sh "$OUT" | cut -f1) total"
echo
echo "Copy the CONTENTS of $OUT to C:\\ishaunted\\webapi, then convert that folder to an"
echo "IIS Application. See docs/deploy-production.md."
