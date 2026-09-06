#!/usr/bin/env bash
#
# Publishes the standalone WASM video editor for production, ready to copy to the IIS box.
#
# The editor is served from a sub-path of the main site (https://ishaunted.com/editors/video/)
# rather than its own subdomain, which is a deliberate choice: a subdomain needs its own
# certificate, and a sub-path inherits the site's. The path is /editors/video and not /video-editor
# because the website itself already routes /video-editor to its in-app editor page, and an IIS
# Application at that path would shadow it permanently.  That choice has two consequences this
# script handles, and both are silent failures if missed:
#
#   1. <base href> must be "/editors/video/", not "/". Blazor resolves every framework file against it,
#      so with the wrong value the browser asks for /_framework/... at the site root and gets the
#      API's 404 page instead of the runtime. The app then hangs on "Loading" with no error.
#
#   2. wwwroot/appsettings.json carries the WebApi origin. It is fetched at startup rather than
#      compiled in, so this is a file edit and not a rebuild — but an empty value is a *working*
#      configuration (fully local editor, no Server tab), so a mistake here does not throw. It
#      just quietly removes the half of the product that talks to the site.
#
# Usage:  scripts/publish-editor.sh [api-origin]
# Default api-origin is https://ishaunted.com/webapi — the API's own IIS Application, same origin
# as the sub-path so no CORS. The /webapi suffix is part of the origin here: the editor appends
# "/api/..." to whatever this value is.

set -euo pipefail

API_ORIGIN="${1:-https://ishaunted.com/webapi}"
BASE_PATH="/editors/video/"
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

# Publish pre-compresses every static file, so each file patched or removed above still has .br
# and .gz twins holding the ORIGINAL bytes — <base href="/"> and an empty WebApiBaseUrl. Stock IIS
# never serves those, but a server with pre-compressed static serving enabled would hand back a
# stale index.html and the app would look for its runtime at the site root.
#
# They are deleted rather than regenerated because there is no brotli encoder to regenerate the
# .br with, and one correct representation beats two that can disagree. The cost is a few KB on a
# ~10 MB app; the alternative is a fault that appears only on a server configured slightly
# differently from the one it was tested on.
for stale in index.html appsettings.json appsettings.Development.json; do
    rm -f "$WWW/$stale.br" "$WWW/$stale.gz"
done

# ── Sidecar downloads ────────────────────────────────────────────────────────
# wwwroot/downloads/index.html ships in source and links the zips at /files/sidecar-video/<rid>/.
# The zips themselves are NOT staged here: at ~160 MB and ~97 MB they do not belong in a folder
# that gets mirrored on every deploy, and keeping them outside the site means rebuilding the editor
# does not mean re-uploading a quarter of a gigabyte. They live under C:\ishaunted-files, published
# as their own IIS Application at /files, and scripts/deploy-ishaunted.ps1 stages them there from
# Ben.Video.Sidecar/installer/dist/ with a checksums.txt beside each — for unsigned builds a
# published hash is the only integrity story a tester has.

echo
echo "Checks:"
grep -o '<base href="[^"]*"' "$WWW/index.html" | sed 's/^/  /'
grep -o '"WebApiBaseUrl": "[^"]*"' "$WWW/appsettings.json" | sed 's/^/  /'
# Not "SPA rewrite". There is deliberately no <rewrite> section: URL Rewrite is a separate IIS
# download, and a rewrite rule on a server without it fails the whole folder with HTTP 500.19. The
# web.config says so at length; this line claimed the opposite (2026-09-05 audit, wasm-17).
[ -f "$WWW/web.config" ] && echo "  web.config present (IIS MIME types, security and cache headers)" \
                         || echo "  WARNING: no web.config — IIS will refuse .wasm and .dat files"

# Prove it rather than trust it: any surviving twin of a patched file is a stale copy of the very
# values this script exists to set.
for stale in index.html appsettings.json appsettings.Development.json; do
    for ext in br gz; do
        if [ -f "$WWW/$stale.$ext" ]; then
            echo "  FAILED: $stale.$ext survived — it holds the pre-patch bytes" >&2
            exit 1
        fi
    done
done
echo "  no stale pre-compressed copies of the patched files"
echo "  $(find "$WWW/_framework" -type f | wc -l | tr -d ' ') framework files"
echo "  $(du -sh "$WWW" | cut -f1) total"
echo
echo "Copy the CONTENTS of $WWW into the site's editor folder on the server,"
echo "so that index.html lands at <site root>\\editors\\video\\index.html."
