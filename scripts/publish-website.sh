#!/usr/bin/env bash
# Publishes the IsHaunted.com website (Blazor Server) for IIS, ready to copy to the server root.
#
#   scripts/publish-website.sh [webapi-base-url] [sql-connection-string]
#
# Output: artifacts/website/
#
# Both arguments are production facts this repository does not know:
#
#   webapi-base-url        Where the WebApi answers. The website is a front end — sign-in, cases,
#                          media, the lot go through the API — so a wrong value here produces a
#                          site that loads perfectly and then fails every single operation.
#   sql-connection-string  Only Serilog's error sink uses it; the website itself holds no
#                          DbContext. Omit it and the sink is removed rather than left pointing at
#                          a localhost database that is not there, because that sink creates its
#                          table at startup and a bad connection string can take the site down
#                          with it.
#
# Values are written to appsettings.Production.json, which is a plain text file on the server —
# edit it there and restart the app pool rather than re-publishing for a changed URL.
set -euo pipefail

API_URL="${1:-}"
SQL_CONN="${2:-}"
SITE_URL="https://ishaunted.com"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/artifacts/website"

echo "Publishing the website"
echo "  WebApi     : ${API_URL:-(NOT SET — placeholder written)}"
echo "  SQL logging: ${SQL_CONN:+configured}${SQL_CONN:-disabled}"
echo "  output     : $OUT"
echo

rm -rf "$OUT"
# -r win-x64 with --self-contained false: framework-dependent (the Hosting Bundle supplies
# the runtime) but RID-specific, so runtimes/ carries only the native assets Windows
# actually loads. Without the RID the publish copies every platform's natives —
# SkiaSharp, the SQL client and friends for linux, macOS and arm — which is how this
# package reached 488 MB, of which 444 MB could never execute on the target.
dotnet publish "$ROOT/Ben.Web.Website" -c Release -r win-x64 --self-contained false \
  -o "$OUT" --nologo -v q

[ -f "$OUT/Ben.Web.Website.dll" ] || { echo "publish produced no Ben.Web.Website.dll" >&2; exit 1; }

# web.config carries the AspNetCoreModuleV2 handler. Without it IIS has no idea this folder is a
# .NET application and serves the files as static content — you get the raw directory, not the site.
[ -f "$OUT/web.config" ] || { echo "publish produced no web.config — IIS cannot host this" >&2; exit 1; }

python3 - "$OUT/appsettings.Production.json" "${API_URL:-__SET_ME__}" "$SITE_URL" "$SQL_CONN" <<'PY'
import json, sys
path, api, site, sql = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]

cfg = {
    "WebApi":       {"BaseUrl": api},
    "SiteIdentity": {"BaseUrl": site},
}

if sql:
    cfg["ConnectionStrings"] = {"BenDbConnectionString": sql}
else:
    # appsettings.json configures Serilog's MSSqlServer sink against a localhost database. On the
    # server that address is something else's SQL, or nothing — and the sink creates its table on
    # startup, so it is a startup dependency rather than a logging nicety. An empty WriteTo here
    # replaces the list from the base file rather than adding to it.
    cfg["Serilog"] = {"WriteTo": []}

open(path, "w", encoding="utf-8").write(json.dumps(cfg, indent=2) + "\n")
print(f"  wrote {path.split('/')[-1]}")
PY

# The Development override ships by default and wins on any machine whose ASPNETCORE_ENVIRONMENT
# says Development. It points at localhost:5252 — nothing, on the server.
rm -f "$OUT/appsettings.Development.json"

echo
echo "Checks:"
grep -o '"BaseUrl": "[^"]*"' "$OUT/appsettings.Production.json" | sed 's/^/  /'
echo "  web.config present (AspNetCoreModuleV2)"
echo "  $(du -sh "$OUT" | cut -f1) total"

if [ -z "$API_URL" ]; then
    echo
    echo "WARNING: WebApi:BaseUrl is the placeholder __SET_ME__." >&2
    echo "         Edit appsettings.Production.json on the server before the site will do anything." >&2
fi

echo
echo "Copy the CONTENTS of $OUT to the site root (C:\\ishaunted)."
echo "See docs/deploy-website.md — /editor must become its own IIS Application, or this app"
echo "swallows every request to it."
