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
# PARALLELISM
# -----------
# Four tests run at once. Ben.Web.Playwright/Parallelism.cs carries the two assembly attributes
# that make that happen — ParallelScope.Fixtures, so different fixtures run together and the tests
# inside one stay in order — and the whole argument for it, including why four and not ten.
#
# scripts/run-e2e.sh is the way to run the suite, and it owns the number:
#
#   scripts/run-e2e.sh                 # four at a time
#   scripts/run-e2e.sh --workers 6     # six
#   scripts/run-e2e.sh --workers 1     # one at a time
#
# ALWAYS REPRODUCE A FAILURE AT --workers 1 BEFORE BELIEVING IT. A test that fails in a parallel
# run and passes alone is not necessarily flaky: far more often it shares something with another
# fixture — the one seeded case, the one seeded hosted event, a site-wide switch — and the fix is
# [NonParallelizable] on the fixture WITH A SENTENCE saying what it shares, as the seventy-odd
# fixtures that already carry it do.
#
# Running the suite by hand with vstest or dotnet test gets the compiled-in four. Override it with
# `-- NUnit.NumberOfTestWorkers=N` after a bare -- on a dotnet test command line.
#
# A HAND RUN AGAINST A HOST THAT RUN-E2E.SH DID NOT START WILL COLLECT 429s. /login is anonymous,
# so the limiter keys it by address and the whole suite is one address, at twenty a minute. The
# script raises that for the length of a run; nothing else does. A 429 reads as "Invalid email or
# password", so this looks like a credentials problem in several fixtures at once.
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
