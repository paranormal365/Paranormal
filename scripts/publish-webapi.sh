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

python3 - "$OUT/appsettings.Production.json" "$SQL_CONN" "$FILE_ROOT" <<'PY'
import json, sys
path, sql, root = sys.argv[1], sys.argv[2], sys.argv[3]

cfg = {
    "ConnectionStrings": {"BenDbConnectionString": sql},
    # Serilog's sink reads its own connection string from its own Args, so pointing the app at a
    # database is not enough — this is a second place the same value has to appear.
    "Serilog": {"WriteTo": [{"Name": "MSSqlServer", "Args": {
        "connectionString": sql, "tableName": "Logs", "autoCreateSqlTable": True,
        "restrictedToMinimumLevel": "Error",
    }}]},
}

if root:
    cfg["FileStorage"] = {"RootPath": root}

open(path, "w", encoding="utf-8").write(json.dumps(cfg, indent=2) + "\n")
print(f"  wrote {path.split('/')[-1]}")
PY

rm -f "$OUT/appsettings.Development.json"

echo
echo "Checks:"
python3 -c "
import json
c = json.load(open('$OUT/appsettings.Production.json'))
print('  connection string set :', bool(c['ConnectionStrings']['BenDbConnectionString']))
print('  file storage root     :', c.get('FileStorage', {}).get('RootPath') or '(unset)')
print('  smtp password present :', 'Smtp' in c, '(must be false — it belongs in an env var)')
"
echo "  web.config present (AspNetCoreModuleV2)"
echo "  $(du -sh "$OUT" | cut -f1) total"
echo
echo "Copy the CONTENTS of $OUT to C:\\ishaunted\\webapi, then convert that folder to an"
echo "IIS Application. See docs/deploy-production.md."
