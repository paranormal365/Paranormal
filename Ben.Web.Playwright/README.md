# Playwright Configuration for Ben.Web.Playwright
#
# QUICKSTART
# ----------
# 1. Start the full stack (VS Code task: start-full-stack)
# 2. Install Playwright browsers:
#      dotnet build Ben.Web.Playwright
#      cd Ben.Web.Playwright/bin/Debug/net10.0
#      pwsh playwright.ps1 install chromium
#      # macOS fallback if pwsh is unavailable:
#      ~/.nuget/packages/microsoft.playwright/1.52.0/runtimes/unix/native/playwright.sh install chromium
# 3. Run the tests:
#      cd /Users/ben/Source/Ben
#      dotnet test Ben.Web.Playwright --no-build -e BEN_BASE_URL=http://localhost:5078
#
# ENVIRONMENT VARIABLES
# ---------------------
# BEN_BASE_URL              WebApp root URL          (default: http://localhost:5078)
# BEN_SUPERADMIN_EMAIL      SuperAdmin email         (default: haveben@msn.com)
# BEN_SUPERADMIN_PASSWORD   SuperAdmin password      (set in your shell; never commit)
# BEN_USER_EMAIL            Regular user email       (default: sarah.mitchell@benco.dev)
# BEN_USER_PASSWORD         Regular user password    (set in your shell; never commit)
#
# CATEGORIES
# ----------
# Run a single category:
#   dotnet test Ben.Web.Playwright --filter TestCategory=Smoke
#   dotnet test Ben.Web.Playwright --filter TestCategory=Auth
#   dotnet test Ben.Web.Playwright --filter TestCategory=Home
#   dotnet test Ben.Web.Playwright --filter TestCategory=PublicCase
#
# HEADFUL MODE (see the browser window)
# -----------
#   Set environment variable: HEADED=1
#   or set in .runsettings: <Parameter name="playwright:headed" value="true" />
#
# PREREQUISITES
# -------------
# - Dev seed data must be enabled (SeedData:DevData:Enabled = true)
# - Tests use the seeded "tgh" org and its 2026-001 case
