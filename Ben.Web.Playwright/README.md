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
# BEN_USER_EMAIL            Regular user email       (default: sarah.mitchell@benco.dev)
#
# THE FIVE PASSWORDS COME FROM TWO DIFFERENT PLACES. Using one value for all of them is the
# mistake that costs an hour: four seats fail to sign in, LoginAsync's retries lock those shared
# accounts for five minutes, and the run reports dozens of failures that look like product bugs.
# All five live in Ben.Data.WebApi/appsettings.Development.json, which is gitignored.
#
#   BEN_SUPERADMIN_PASSWORD   SeedData:SuperAdmin:Password
#   BEN_USER_PASSWORD         SeedData:SeedOrganization:Users -> sarah.mitchell@benco.dev
#   BEN_MEMBER_PASSWORD       SeedData:SeedOrganization:Users -> james.thornton@benco.dev
#   BEN_CLIENT_PASSWORD       SeedData:SeedOrganization:Users -> daniel.park@benco.dev
#   BEN_VIEWER_PASSWORD       SeedData:DevData:Password       (victor.reyes@benco.dev, and every
#                                                              other roster account)
#
# Check the seats before the run, not after — one 401 is worth more than the whole run's output:
#
#   for a in sarah.mitchell james.thornton daniel.park victor.reyes; do
#     curl -s -o /dev/null -w "$a %{http_code}\n" -X POST http://localhost:5252/login \
#       -H 'Content-Type: application/json' \
#       -d "{\"email\":\"$a@benco.dev\",\"password\":\"...\"}"
#   done
#
# A locked seat clears itself after about five minutes, or immediately from the SuperAdmin user
# screen by saving that account with no lockout.
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
