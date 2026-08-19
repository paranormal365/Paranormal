#!/usr/bin/env bash
#
# Publishes the standalone WASM video editor for production, ready to copy to the IIS box.
#
# The editor is served from a sub-path of the main site (https://ishaunted.com/editor/) rather
# than its own subdomain, which is a deliberate choice: a subdomain needs its own certificate,
# and a sub-path inherits the site's. That choice has two consequences this script handles, and
# both are silent failures if missed:
#
#   1. <base href> must be "/editor/", not "/". Blazor resolves every framework file against it,
#      so with the wrong value the browser asks for /_framework/... at the site root and gets the
#      API's 404 page instead of the runtime. The app then hangs on "Loading" with no error.
#
#   2. wwwroot/appsettings.json carries the WebApi origin. It is fetched at startup rather than
#      compiled in, so this is a file edit and not a rebuild — but an empty value is a *working*
#      configuration (fully local editor, no Server tab), so a mistake here does not throw. It
#      just quietly removes the half of the product that talks to the site.
#
# Usage:  scripts/publish-editor.sh [api-origin]
# Default api-origin is https://ishaunted.com — same origin as the sub-path, so no CORS.

set -euo pipefail

API_ORIGIN="${1:-https://ishaunted.com}"
BASE_PATH="/editor/"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/artifacts/editor"

echo "Publishing the WASM editor"
echo "  API origin : $API_ORIGIN"
echo "  base href  : $BASE_PATH"
echo "  output     : $OUT"
echo

rm -rf "$OUT"
dotnet publish "$ROOT/Ben.Wasm.Video" -c Release -o "$OUT" --nologo

WWW="$OUT/wwwroot"
[ -d "$WWW" ] || { echo "publish produced no wwwroot at $WWW" >&2; exit 1; }

# base href — matched loosely because the quoting style is the template's, not ours.
python3 - "$WWW/index.html" "$BASE_PATH" <<'PY'
import re, sys
path, base = sys.argv[1], sys.argv[2]
html = open(path, encoding="utf-8").read()
patched, n = re.subn(r'<base\s+href="[^"]*"\s*/?>', f'<base href="{base}" />', html, count=1)
if n != 1:
    sys.exit(f"expected exactly one <base href> in {path}, replaced {n}")
open(path, "w", encoding="utf-8").write(patched)
print(f"  base href set to {base}")
PY

python3 - "$WWW/appsettings.json" "$API_ORIGIN" <<'PY'
import json, sys
path, origin = sys.argv[1], sys.argv[2]
cfg = json.load(open(path, encoding="utf-8"))
cfg.setdefault("BenVideo", {})["WebApiBaseUrl"] = origin
json.dump(cfg, open(path, "w", encoding="utf-8"), indent=2)
print(f"  WebApiBaseUrl set to {origin}")
PY

# The development override ships in the publish output and would win on any machine whose
# environment says Development. It points at localhost:5252, which on the production box is
# nothing at all — remove it rather than leave a file that can only do harm there.
rm -f "$WWW/appsettings.Development.json"

echo
echo "Checks:"
grep -o '<base href="[^"]*"' "$WWW/index.html" | sed 's/^/  /'
grep -o '"WebApiBaseUrl": "[^"]*"' "$WWW/appsettings.json" | sed 's/^/  /'
[ -f "$WWW/web.config" ] && echo "  web.config present (IIS MIME types + SPA rewrite)" \
                         || echo "  WARNING: no web.config — IIS will refuse .wasm and .dat files"
echo "  $(find "$WWW/_framework" -type f | wc -l | tr -d ' ') framework files"
echo "  $(du -sh "$WWW" | cut -f1) total"
echo
echo "Copy the CONTENTS of $WWW into the site's editor folder on the server,"
echo "so that index.html lands at <site root>\\editor\\index.html."
