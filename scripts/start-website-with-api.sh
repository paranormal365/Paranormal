#!/usr/bin/env bash
set -euo pipefail

# Starts the WebApi (if it isn't already up) and then Ben.Web.Website — the SmartAdmin/Night
# front end — on :5078.
#
# :5078 matters specifically: it is the redirect URI registered with Entra
# (http://localhost:5078/signin-oidc) and an allow-listed CORS origin on the API, so Microsoft
# sign-in works here without touching the app registration. The original Ben.Web.WebApp (removed 2026-08-19, see the pre-old-site-removal tag) ran
# on :5079 via start-webapp-with-api.sh; both can run at once.
#
# All the actual startup logic lives in the sibling script — this only chooses the project and URL.

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

exec env \
  BEN_APP_PROJECT="Ben.Web.Website/Ben.Web.Website.csproj" \
  BEN_WEBAPP_URL="${BEN_WEBSITE_URL:-http://localhost:5078}" \
  bash "$ROOT_DIR/scripts/start-webapp-with-api.sh"
