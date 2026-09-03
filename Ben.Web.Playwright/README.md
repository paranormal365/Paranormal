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
#      dotnet vstest Ben.Web.Playwright/bin/Debug/net10.0/Ben.Web.Playwright.dll
#
#    NOT `dotnet test Ben.Web.Playwright`: the project sets IsTestProject=false so the solution-wide
#    run skips it, and that makes `dotnet test` on the project itself print nothing and exit 0 —
#    which reads as a pass. vstest on the built DLL runs everything (2026-09-03).
#    Filter one fixture with --Tests:StartGroupWizardTests, or a category with
#    --TestCaseFilter:TestCategory=Smoke.
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
#   dotnet vstest Ben.Web.Playwright/bin/Debug/net10.0/Ben.Web.Playwright.dll --TestCaseFilter:TestCategory=Smoke
#   dotnet vstest Ben.Web.Playwright/bin/Debug/net10.0/Ben.Web.Playwright.dll --TestCaseFilter:TestCategory=Auth
#   dotnet vstest Ben.Web.Playwright/bin/Debug/net10.0/Ben.Web.Playwright.dll --TestCaseFilter:TestCategory=Home
#   dotnet vstest Ben.Web.Playwright/bin/Debug/net10.0/Ben.Web.Playwright.dll --TestCaseFilter:TestCategory=HomeMap
#   dotnet vstest Ben.Web.Playwright/bin/Debug/net10.0/Ben.Web.Playwright.dll --TestCaseFilter:TestCategory=PublicCase
#   dotnet vstest Ben.Web.Playwright/bin/Debug/net10.0/Ben.Web.Playwright.dll --TestCaseFilter:TestCategory=CaseManagement
#   dotnet vstest Ben.Web.Playwright/bin/Debug/net10.0/Ben.Web.Playwright.dll --TestCaseFilter:TestCategory=CaseMessages
#   dotnet vstest Ben.Web.Playwright/bin/Debug/net10.0/Ben.Web.Playwright.dll --TestCaseFilter:TestCategory=CaseReports
#   dotnet vstest Ben.Web.Playwright/bin/Debug/net10.0/Ben.Web.Playwright.dll --TestCaseFilter:TestCategory=CaseTransfer
#   dotnet vstest Ben.Web.Playwright/bin/Debug/net10.0/Ben.Web.Playwright.dll --TestCaseFilter:TestCategory=InvestigationPanel
#   dotnet vstest Ben.Web.Playwright/bin/Debug/net10.0/Ben.Web.Playwright.dll --TestCaseFilter:TestCategory=MyCases
#   dotnet vstest Ben.Web.Playwright/bin/Debug/net10.0/Ben.Web.Playwright.dll --TestCaseFilter:TestCategory=Navigation
#   dotnet vstest Ben.Web.Playwright/bin/Debug/net10.0/Ben.Web.Playwright.dll --TestCaseFilter:TestCategory=OrgDiscovery
#   dotnet vstest Ben.Web.Playwright/bin/Debug/net10.0/Ben.Web.Playwright.dll --TestCaseFilter:TestCategory=ErrorHandling
#   dotnet vstest Ben.Web.Playwright/bin/Debug/net10.0/Ben.Web.Playwright.dll --TestCaseFilter:TestCategory=Voting
#   dotnet vstest Ben.Web.Playwright/bin/Debug/net10.0/Ben.Web.Playwright.dll --TestCaseFilter:TestCategory=CaseNotes
#   dotnet vstest Ben.Web.Playwright/bin/Debug/net10.0/Ben.Web.Playwright.dll --TestCaseFilter:TestCategory=OrgPublic
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
