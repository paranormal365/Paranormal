using System.Net.Http.Json;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using System.Text.RegularExpressions;

namespace Ben.Web.Playwright;

/// <summary>
/// Base class for all IsHaunted front-end Playwright tests.
/// </summary>
/// <remarks>
/// <para>
/// Inherits from <see cref="PageTest"/> which provides a fresh browser <see cref="Page"/>
/// per test and handles browser lifecycle automatically via NUnit's SetUp/TearDown.
/// </para>
/// <para>
/// Configuration is driven by environment variables (or a local <c>.env</c> / test settings):
/// <list type="table">
///   <listheader><term>Variable</term><description>Purpose</description></listheader>
///   <item><term>BEN_BASE_URL</term><description>Root URL of the running WebApp. Defaults to <c>http://localhost:5078</c>.</description></item>
///   <item><term>BEN_SUPERADMIN_EMAIL</term><description>Email for the SuperAdmin login flow tests.</description></item>
///   <item><term>BEN_SUPERADMIN_PASSWORD</term><description>Password for the SuperAdmin login flow tests.</description></item>
///   <item><term>BEN_USER_EMAIL</term><description>Email for a regular authenticated user (used in voting tests).</description></item>
///   <item><term>BEN_USER_PASSWORD</term><description>Password for the regular user.</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Prerequisites before running tests:</strong>
/// <list type="number">
///   <item><description>Install Playwright browsers: <c>pwsh Ben.Web.Playwright/bin/Debug/net10.0/playwright.ps1 install</c>
///   or on macOS: <c>~/.nuget/packages/microsoft.playwright/1.52.0/runtimes/unix/native/playwright.sh install chromium</c></description></item>
///   <item><description>Start the full stack: run VS Code task <c>start-full-stack</c> or <c>start-web-app</c>.</description></item>
///   <item><description>Ensure dev seed data is present: set <c>SeedData:DevData:Enabled = true</c> in <c>appsettings.Development.json</c>.</description></item>
/// </list>
/// </para>
/// </remarks>
public abstract class BenTestBase : PageTest
{
    // ── Theme ────────────────────────────────────────────────────────────────

    /// <summary>The theme this run is asked to use: <c>BEN_THEME=dark</c>, or nothing.</summary>
    /// <remarks>
    /// <para><b>Why a switch in the base rather than a setting per fixture.</b> The site picks its
    /// theme in <c>ben-boot.js</c>: the <c>layoutSettings</c> localStorage object, then a legacy
    /// key, then <c>prefers-color-scheme</c>, then light. A Playwright context with no colour
    /// scheme set reports light, so every fixture that did not say otherwise — the sweep, the
    /// visual audit — has been walking the LIGHT site, while the walks that set
    /// <c>ColorScheme.Dark</c> walked the dark one. Two instruments, two themes, and nothing
    /// said which. Found 2026-09-21 when the audit's contrast figures did not match the CSS.</para>
    ///
    /// <para>Set, this does both halves: the colour scheme for a fresh context (so the boot
    /// script's media-query fallback picks dark) and the <c>layoutSettings</c> object for the very
    /// first paint (so nothing flashes light first). A derived fixture that overrides
    /// <see cref="ContextOptions"/> without calling base loses the first half and keeps the
    /// second, which is enough.</para>
    /// </remarks>
    protected static bool DarkThemeRequested
        => string.Equals(Environment.GetEnvironmentVariable("BEN_THEME"), "dark",
                         StringComparison.OrdinalIgnoreCase);

    /// <summary>"dark" or "light", for report headings, so a reader knows what was measured.</summary>
    protected static string ThemeName => DarkThemeRequested ? "dark" : "light";

    public override BrowserNewContextOptions ContextOptions()
    {
        var options = base.ContextOptions() ?? new BrowserNewContextOptions();
        if (DarkThemeRequested) options.ColorScheme = ColorScheme.Dark;
        return options;
    }

    [SetUp]
    public async Task ApplyRequestedThemeAsync()
    {
        if (!DarkThemeRequested) return;

        // The same object the help captures seed, for the same reason: the boot script reads it
        // synchronously from <head>, so this is what makes the FIRST paint dark rather than the
        // second.
        await Context.AddInitScriptAsync("""
            try {
                localStorage.setItem('layoutSettings', JSON.stringify({ theme: 'dark' }));
                localStorage.setItem('ben-theme', 'dark');
            } catch (e) { /* storage blocked; the media-query fallback still applies */ }
            """);
    }

    /// <summary>
    /// Root URL of the front end under test. Override with the BEN_BASE_URL env var.
    /// <para>
    /// Defaults to :5078 — <c>Ben.Web.Website</c>, which is now the only front end. The original
    /// <c>Ben.Web.WebApp</c> was removed once the port was complete; it is still readable at the
    /// <c>pre-old-site-removal</c> tag if a behaviour ever needs comparing against it.
    /// </para>
    /// </summary>
    protected static string BaseUrl => Environment.GetEnvironmentVariable("BEN_BASE_URL") ?? "http://localhost:5078";

    /// <summary>Root URL of the WebApi. Override with the BEN_API_URL env var.</summary>
    protected static string ApiUrl => Environment.GetEnvironmentVariable("BEN_API_URL") ?? "http://localhost:5252";

    /// <summary><see cref="ApiUrl"/>, for helper classes that are not fixtures.</summary>
    internal static string ApiUrlForHelpers => ApiUrl;

    /// <summary><see cref="SuperAdminTokenAsync"/>, for helper classes that are not fixtures.</summary>
    internal static Task<string?> SuperAdminTokenForHelpersAsync() => SuperAdminTokenAsync();

    // ── Site feature switches ────────────────────────────────────────────────
    //
    // Several features ship dark and are turned on per deployment. A suite that fails when one is
    // OFF is reporting the deployment's configuration as a defect: 21 of 27 failures on the
    // 2026-08-31 run were exactly this, and they drown the failures somebody should act on.
    //
    // The rule this enforces: a test may be SKIPPED only when the thing it tests is genuinely
    // switched off in the environment it is pointed at. Anything else, it must still fail.

    private static Dictionary<string, bool>? _features;
    private static readonly SemaphoreSlim FeatureLock = new(1, 1);

    /// <summary>The site's feature switches, read once per run.</summary>
    /// <remarks>
    /// An unreachable API returns an EMPTY map rather than "everything off". Treating a failed
    /// request as "the feature is off" would skip the whole suite the first time the host was
    /// slow to start, and report it as a pass.
    /// </remarks>
    protected static async Task<Dictionary<string, bool>> SiteFeaturesAsync()
    {
        if (_features is not null) return _features;

        await FeatureLock.WaitAsync();
        try
        {
            if (_features is not null) return _features;

            var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var http = new HttpClient { BaseAddress = new Uri(ApiUrl), Timeout = TimeSpan.FromSeconds(20) };
                var json = await http.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/public/site-features");
                if (json.TryGetProperty("features", out var features))
                {
                    foreach (var flag in features.EnumerateObject())
                    {
                        if (flag.Value.ValueKind is System.Text.Json.JsonValueKind.True
                                                 or System.Text.Json.JsonValueKind.False)
                        {
                            map[flag.Name] = flag.Value.ValueKind == System.Text.Json.JsonValueKind.True;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Left empty on purpose — see the remarks. Unknown is not off.
            }

            _features = map;
            return _features;
        }
        finally
        {
            FeatureLock.Release();
        }
    }

    /// <summary>True only when the switch is present AND explicitly off.</summary>
    protected static async Task<bool> FeatureIsOffAsync(string flag)
        => (await SiteFeaturesAsync()).TryGetValue(flag, out var on) && !on;

    /// <summary>
    /// Skips this test when the feature it covers is switched off on the target deployment.
    /// </summary>
    /// <remarks>
    /// Ignore rather than Pass: a skipped test is visible in the run summary as something that
    /// did not execute, and a passed one claims a guarantee nobody checked.
    /// </remarks>
    protected static async Task SkipIfFeatureOffAsync(string flag)
    {
        if (await FeatureIsOffAsync(flag))
            Assert.Ignore($"'{flag}' is switched off on this deployment, so there is nothing to test.");
    }

    // ── Seeded org ids, resolved once per run ─────────────────────────────────
    // Fixtures used to hardcode these GUIDs, which survives exactly until the next database
    // rebuild: the org comes back under a fresh id, the fixture navigates to an org that no
    // longer exists, and the failure blames whatever element it was waiting for. The slug is the
    // identity the seeder maintains, so the slug is what fixtures name.
    private static readonly Dictionary<string, string> _orgIdBySlug = [];
    private static readonly SemaphoreSlim _orgIdLock = new(1, 1);

    /// <summary>The seeded org's current id, looked up by its stable slug.</summary>
    /// <summary>
    /// A draft hosted event with this name in the group, for a capture that takes it through a story and archives it
    /// afterwards: the archived one is restored when it is already there, and it is made the first time.
    /// </summary>
    /// <remarks>
    /// Event names are unique within a group, so making a fresh draft each run fails on the second. Restoring brings an
    /// archived event that never went live back as a draft; anything else is reported rather than photographed.
    /// </remarks>
    /// <param name="admin">An API context signed in as somebody who may see every hosted event and edit this group's.</param>
    protected static async Task<string> DraftHostedEventAsync(
        IAPIRequestContext admin, string orgId, string name, string placeId)
    {
        var list = await admin.GetAsync("/api/admin/hosted-events");
        Assert.That(list.Ok, Is.True, await list.TextAsync());
        var existing = (await list.JsonAsync())!.Value.GetProperty("events").EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("organizationId").GetString() == orgId && e.GetProperty("eventName").GetString() == name);

        if (existing.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var id = existing.GetProperty("id").GetString()!;
            var restored = await admin.PostAsync($"/api/organizations/{orgId}/events/{id}/restore", new() { DataObject = new { } });
            Assert.That(restored.Ok, Is.True, await restored.TextAsync());
            Assert.That((await restored.JsonAsync())!.Value.GetProperty("lifecycleState").GetInt32(), Is.EqualTo(0),
                $"{name} is not a draft after restoring it.");
            return id;
        }

        var starts = DateTime.UtcNow.Date.AddDays(45);
        var made = await admin.PostAsync($"/api/organizations/{orgId}/events", new()
        {
            DataObject = new
            {
                name, placeId, timeZoneId = "America/Chicago",
                startsOn = starts.ToString("yyyy-MM-dd"), endsOn = starts.ToString("yyyy-MM-dd"), contactLine = "Call us.",
            },
        });
        Assert.That(made.Ok, Is.True, await made.TextAsync());
        return (await made.JsonAsync())!.Value.GetProperty("id").GetString()!;
    }

    /// <summary>
    /// Publishes a seeded hosted event, and answers with the slug its public page lives at.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a test has to do this at all.</b> HostedEventDemoSeeder creates both of its
    /// events as <c>Draft</c>, deliberately, and says so: "the point of the seed is a plan to
    /// arrange, and publishing it would spend one of the group's credits every time a database is
    /// built." The public endpoints answer only for Published, Live or Ended
    /// (<c>HostedEventStates.OnThePublicSite</c>), so a fixture that wants the event's PUBLIC page
    /// has to put it there itself.</para>
    ///
    /// <para><b>Four fixtures used to assume somebody else had.</b> They asked the public endpoint
    /// and asserted it answered, which was true on the shared e2e database only because an earlier
    /// run had published that event and the row outlived it. On a genuinely fresh database — the
    /// first one built in eight months — all eight of their tests failed at once (item 243,
    /// 2026-09-19). The green they had been giving was borrowed from history.</para>
    ///
    /// <para><b>Safe to call every time.</b> Publishing an event that is already published costs
    /// nothing: the controller only asks the entitlement on an event whose FirstPublishedUtc is
    /// null, and "the second publish of the same event is free, for ever, because the first one
    /// paid for it". The seeded group's tier is not excluded from HostEvents, so the first publish
    /// is free too — capabilities are excluded per tier, and nothing excludes that one.</para>
    ///
    /// <para>A refusal is reported with the server's own sentence rather than as "not on the public
    /// site", so the next person reads why instead of guessing.</para>
    /// </remarks>
    protected async Task<string> PublishSeededEventAsync(string orgId, string eventId)
    {
        await using var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });

        var login = await api.PostAsync("/login", new()
        {
            DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword },
        });
        Assert.That(login.Ok, Is.True, "the admin seat should be able to sign in to publish a seeded event");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };

        var published = await api.PostAsync(
            $"/api/organizations/{orgId}/events/{eventId}/publish",
            new() { Headers = headers, DataObject = new { } });
        Assert.That(published.Ok, Is.True,
            "the seeded event could not be published: " + await published.TextAsync());

        var ev = await api.GetAsync($"/api/public/hosted-events/{eventId}");
        Assert.That(ev.Ok, Is.True,
            "the seeded event was published and still is not on the public site: " + await ev.TextAsync());
        return (await ev.JsonAsync())!.Value.GetProperty("urlName").GetString()!;
    }

    /// <summary>
    /// The link a letter to <paramref name="to"/> carried, read from the outbox — failing loudly
    /// when there is none.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the outbox and not the log.</b> The harness has no mail server, so an emailed
    /// link — confirming a sign-up, holding picked seats — has to be read from somewhere. It used to
    /// be the API's log, which meant the API wrote working credentials into a text file for the
    /// tests' benefit (NoCredentialsInLogsTests). The outbox takes every letter whether or not SMTP
    /// is set up, and a SuperAdmin can read one's body, audited, which is the same thing a person
    /// answering "did we send it?" does.</para>
    ///
    /// <para><b>It also needs nothing but the admin seat.</b> Two of the four readers asked for a
    /// BEN_API_LOG variable that run-e2e.sh never set — one of them spelled BEN_API_LOGE — so the
    /// sign-up and onboarding journeys skipped in every standard run, and a skip reads as a pass.</para>
    ///
    /// <para>Newest letter first, and every test's address is its own, so the letter found is the
    /// one this test caused.</para>
    /// </remarks>
    /// <param name="linkPath">Where the link's path starts: "/confirm-email?" or "/event-picks/".</param>
    /// <returns>The link from that path onward — ready to follow after BaseUrl.</returns>
    protected async Task<string> LinkFromTheOutboxAsync(string to, string linkPath)
    {
        var link = await TryLinkFromTheOutboxAsync(to, linkPath);
        Assert.That(link, Is.Not.Null,
            $"No letter to {to} carrying a {linkPath} link reached the outbox. With no mail server "
          + "the letter should still be queued — look at /admin/mail as the SuperAdmin.");
        return link!;
    }

    /// <summary>
    /// <see cref="LinkFromTheOutboxAsync"/>, answering null instead of failing — for a capture that
    /// takes a picture only when it can.
    /// </summary>
    protected async Task<string?> TryLinkFromTheOutboxAsync(string to, string linkPath)
    {
        // From the path to the end of the attribute. Decoded first: a link in HTML writes its & as
        // &amp;, and following that literally is a different link.
        var shape = new Regex(Regex.Escape(linkPath) + "[^\"'<>\\s]*");

        var html = await TryLetterFromTheOutboxAsync(to, body => shape.IsMatch(System.Net.WebUtility.HtmlDecode(body)));
        return html is null ? null : shape.Match(System.Net.WebUtility.HtmlDecode(html)).Value;
    }

    /// <summary>
    /// The body of the newest letter to <paramref name="to"/> that <paramref name="matches"/>, read
    /// from the outbox as the SuperAdmin — or null after ten seconds of looking.
    /// </summary>
    /// <remarks>What <see cref="LinkFromTheOutboxAsync"/> reads, for a test that wants the whole letter.</remarks>
    protected async Task<string?> TryLetterFromTheOutboxAsync(string to, Func<string, bool> matches)
    {
        await using var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });

        var login = await api.PostAsync("/login", new()
        {
            DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword },
        });
        Assert.That(login.Ok, Is.True,
            "the admin seat reads the outbox for emailed letters, and could not sign in: " + await login.TextAsync());
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        var auth = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var list = await api.GetAsync("/api/admin/mail/outbox?take=100", new() { Headers = auth });
            Assert.That(list.Ok, Is.True, "the outbox could not be read: " + await list.TextAsync());

            foreach (var row in (await list.JsonAsync())!.Value.EnumerateArray())
            {
                if (!string.Equals(row.GetProperty("to").GetString(), to, StringComparison.OrdinalIgnoreCase)) continue;
                if (!row.GetProperty("hasBody").GetBoolean()) continue;

                var body = await api.GetAsync(
                    $"/api/admin/mail/outbox/{row.GetProperty("id").GetString()}/body", new() { Headers = auth });
                if (!body.Ok) continue;

                var html = (await body.JsonAsync())!.Value.GetProperty("html").GetString();
                if (html is not null && matches(html)) return html;
            }

            await Task.Delay(500);
        }

        return null;
    }

    protected async Task<string> OrgIdBySlugAsync(string slug)
    {
        await _orgIdLock.WaitAsync();
        try
        {
            if (_orgIdBySlug.TryGetValue(slug, out var cached)) return cached;

            var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
            var login = await api.PostAsync("/login", new()
            {
                DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword },
            });
            Assert.That(login.Ok, Is.True, "the admin seat should be able to sign in to resolve org ids");
            var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
            var orgs = await api.GetAsync("/api/organizations",
                new() { Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" } });
            Assert.That(orgs.Ok, Is.True, await orgs.TextAsync());
            foreach (var o in (await orgs.JsonAsync())!.Value.EnumerateArray())
                _orgIdBySlug[o.GetProperty("urlName").GetString()!] = o.GetProperty("id").GetString()!;
            await api.DisposeAsync();

            Assert.That(_orgIdBySlug.ContainsKey(slug), Is.True, $"no seeded org has the slug '{slug}'");
            return _orgIdBySlug[slug];
        }
        finally { _orgIdLock.Release(); }
    }


    // ── The seats ─────────────────────────────────────────────────────────────
    //
    // Four of them, named by what they can do rather than who they are, because which seat a test
    // sits in is the single most load-bearing decision it makes.
    //
    // The suite spent its life in the top two. Phase 5 then found three separate faults in group
    // messaging that were TOTAL for ordinary members and completely invisible from an owner
    // account — all three caught within minutes of signing in as James instead of Sarah. Reaching
    // for a less privileged seat has to be the easy path, or nobody does it: MemberEmail and
    // ClientEmail were previously re-declared in seven different fixtures, which is what happens
    // when the base class only offers the powerful ones.
    //
    // See item 109. Any new fixture should ask which of these four it means, and say so.

    /// <summary>
    /// A password read from the environment, with no fallback.
    /// </summary>
    /// <remarks>
    /// These used to default to the seeded literals. Because the development database is also the
    /// one ishaunted.com uses, those defaults were working production credentials sitting in a
    /// public repository — so the defaults are gone and a run without them stops with a message
    /// naming the variable rather than silently testing as somebody real.
    /// <para>Set them from your own shell, or source them from the API's
    /// <c>appsettings.Development.json</c>, which is gitignored. <b>They do not all come from the
    /// same key</b>, and using one for all five is the mistake that costs an hour: four of them
    /// fail, the login helper's retries lock the shared seats, and the run reports dozens of
    /// failures that look like product bugs.</para>
    ///
    /// <list type="table">
    ///   <listheader><term>Variable</term><description>Where its value lives</description></listheader>
    ///   <item><term>BEN_SUPERADMIN_PASSWORD</term>
    ///         <description><c>SeedData:SuperAdmin:Password</c></description></item>
    ///   <item><term>BEN_USER_PASSWORD</term>
    ///         <description><c>SeedData:SeedOrganization:Users</c> — the entry for
    ///         sarah.mitchell@benco.dev</description></item>
    ///   <item><term>BEN_MEMBER_PASSWORD</term>
    ///         <description>same list — james.thornton@benco.dev</description></item>
    ///   <item><term>BEN_CLIENT_PASSWORD</term>
    ///         <description>same list — daniel.park@benco.dev</description></item>
    ///   <item><term>BEN_VIEWER_PASSWORD</term>
    ///         <description><c>SeedData:DevData:Password</c> — victor.reyes@benco.dev, and every
    ///         other roster account, which the roster seeder creates with that one value</description></item>
    /// </list>
    ///
    /// <para>Check each seat before a run rather than after: a single
    /// <c>POST /login</c> per account answers 200 or 401 in a second, and 401 on one of them is
    /// worth more than the whole run's output.</para>
    /// </remarks>
    /// <summary>
    /// A strong password for an account this run is about to create.
    /// </summary>
    /// <remarks>
    /// Generated rather than written down. A fixture that registers an account needs *a* password,
    /// not a particular one — and three of them used to be inline constants in a public repository,
    /// which on a shared database meant live accounts anybody could sign into. Nothing outside the
    /// run needs to know this value, so nothing outside the run is told it.
    /// </remarks>
    protected static string NewTestPassword()
    {
        const string alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = System.Security.Cryptography.RandomNumberGenerator.GetString(alphabet, 20);
        return $"T!{random}9";   // satisfies Identity's upper/lower/digit/symbol rules
    }

    /// <summary>
    /// A seeded password from the environment, or a skip.
    /// </summary>
    /// <remarks>
    /// <para>Skips rather than throws. A password this machine was never given is a missing
    /// precondition, exactly like a feature being switched off or a host not running — and a run
    /// that reports failures for it buries the tests that found something. Ignore says plainly
    /// which tests did not run and why (2026-09-05 audit, F19).</para>
    ///
    /// <para><c>Assert.Ignore</c> throws, so the signature and every call site are unchanged; the
    /// declared return type is what keeps the compiler happy on the branch that never returns.</para>
    /// </remarks>
    protected static string RequiredSecret(string variable)
        => Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value
            ? value
            : throw AssertIgnoreMissingSecret(variable);

    private static Exception AssertIgnoreMissingSecret(string variable)
    {
        Assert.Ignore(
            $"{variable} is not set, so the tests that sign in cannot run. The seeded passwords "
            + "are not compiled into this file; export the variable (see "
            + "appsettings.Development.json, which is gitignored) before running the suite.");
        return new InvalidOperationException("unreachable — Assert.Ignore throws");
    }

    /// <summary>Runs the site. Passes every permission check by role, everywhere.</summary>
    protected static string SuperAdminEmail    => Environment.GetEnvironmentVariable("BEN_SUPERADMIN_EMAIL")    ?? "haveben@msn.com";
    protected static string SuperAdminPassword => RequiredSecret("BEN_SUPERADMIN_PASSWORD");

    /// <summary>
    /// Sarah — Administrator of Paranormal365 and owner of BenCo. The default seat, and
    /// the reason to think twice: an administrator passes <c>HasAccessAsync</c> on every table by
    /// role, so a surface broken for everyone else looks perfect from here.
    /// </summary>
    protected static string UserEmail          => Environment.GetEnvironmentVariable("BEN_USER_EMAIL")          ?? "sarah.mitchell@benco.dev";
    protected static string UserPassword       => RequiredSecret("BEN_USER_PASSWORD");

    /// <summary>
    /// James — a plain <c>Member</c> of Paranormal365, and the most useful seat in the
    /// suite.
    /// </summary>
    /// <remarks>
    /// He belongs to the group and holds no grants and no named role, which is what an ordinary
    /// member is. That combination makes <c>HasAccessAsync</c> return <b>false on every table</b>,
    /// so anything gated on it that members are meant to reach is broken from here and nowhere
    /// else. Use this seat for any surface a member is shown.
    /// </remarks>
    protected static string MemberEmail        => Environment.GetEnvironmentVariable("BEN_MEMBER_EMAIL")        ?? "james.thornton@benco.dev";
    protected static string MemberPassword     => RequiredSecret("BEN_MEMBER_PASSWORD");

    /// <summary>
    /// Victor — a <c>Viewer</c> of Paranormal365: belongs to the group, may look,
    /// changes nothing. The fourth seat of the four-seat pass (owner, administrator, member,
    /// viewer); seeded permanently so using it never requires mutating a real member's tier.
    /// </summary>
    protected static string ViewerEmail        => Environment.GetEnvironmentVariable("BEN_VIEWER_EMAIL")        ?? "victor.reyes@benco.dev";
    protected static string ViewerPassword     => RequiredSecret("BEN_VIEWER_PASSWORD");

    /// <summary>
    /// Daniel — has an account and belongs to no group. The client seat: cases of his own, and no
    /// membership anywhere.
    /// </summary>
    protected static string ClientEmail        => Environment.GetEnvironmentVariable("BEN_CLIENT_EMAIL")        ?? "daniel.park@benco.dev";
    protected static string ClientPassword     => RequiredSecret("BEN_CLIENT_PASSWORD");

    /// <summary>
    /// Wren — an account in no group at all, and no group's client either.
    /// </summary>
    /// <remarks>
    /// <para>The free lane's own seat (2026-09-17). <see cref="ClientEmail"/> is also group-less,
    /// but a client has a case being worked and so passes the feed's "people who belong here"
    /// rule — which makes him useless for testing the wider door a public place opens. Wren passes
    /// nothing: no membership, no case, no client access. She is who the free lane exists for, and
    /// what she may and may not do is the whole point of her.</para>
    ///
    /// <para>She has no personal organization either, because minting one is the behaviour under
    /// test and a seat already through the door cannot test the door.</para>
    /// </remarks>
    protected static string SoloEmail          => Environment.GetEnvironmentVariable("BEN_SOLO_EMAIL")          ?? "wren.ashby@benco.dev";
    protected static string SoloPassword       => RequiredSecret("BEN_SOLO_PASSWORD");

    // ── The public feed's switch, for fixtures that need it on ───────────────

    /// <summary>
    /// Turns the public feed on and <b>waits until the service agrees</b>, returning what it was
    /// before so the caller can put it back.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the waiting half exists.</b> Setting a site setting and starting to click is a
    /// race, and it is a race that loses quietly: the feed reads as off, a composer does not render,
    /// and three tests time out looking for an element whose absence is correct. That happened on
    /// 2026-09-17 — the place-post fixtures passed whenever some earlier fixture had left the feed
    /// on and failed whenever they were first, which is the worst possible failure pattern because
    /// it looks like flakiness in the product.</para>
    ///
    /// <para>Confirmation is <c>GET /api/feed</c>: it 404s wholesale while the feed is off, so a
    /// 200 is the service saying the switch has landed — and it is the same answer the pages under
    /// test depend on, rather than a proxy for it.</para>
    ///
    /// <para>Returns null when the switch could not be set or never took. That is a <b>missing
    /// precondition</b> and the caller should <c>Assert.Ignore</c>, never fail: a fixture that
    /// cannot arrange its own world has not found a bug.</para>
    /// </remarks>
    protected static async Task<bool?> TurnTheFeedOnAsync()
    {
        var token = await SuperAdminTokenAsync();
        if (token is null) return null;

        bool wasOn;
        using (var read = new HttpClient { BaseAddress = new Uri(ApiUrl), Timeout = TimeSpan.FromSeconds(30) })
        {
            read.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            try
            {
                var settings = await read.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/admin/site-settings");
                wasOn = settings.EnumerateArray().Any(setting =>
                    setting.GetProperty("key").GetString() == FeedSwitchKey
                    && setting.TryGetProperty("value", out var value)
                    && string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase));
            }
            catch (HttpRequestException) { return null; }
        }

        if (!await SetFeedSwitchAsync(token, on: true)) return null;

        // Waited for on the WEBSITE, not the API, and this is the whole point of the wait.
        //
        // The API answers /api/feed the moment the setting is written. The website does not: its
        // FeatureGate reads SiteFeaturesProvider, a singleton that refreshes on a 30-SECOND
        // snapshot, and nothing primes it when a switch is flipped through the API rather than
        // through the admin page. So the old probe proved the API agreed and then handed the
        // browser a page that still said the feed was off — for up to half a minute.
        //
        // It never showed while the e2e database carried the switch already on from a previous
        // run. On a genuinely fresh one, where the feed starts off by default, it failed at once
        // (item 243, 2026-09-19): the tests timed out looking for a tab whose absence was correct.
        //
        // Forty seconds, because it has to outlast a 30-second snapshot that may have refreshed
        // the instant before the setting was written.
        using var probe = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(10) };
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                using var response = await probe.GetAsync("/feed");
                if (response.IsSuccessStatusCode)
                {
                    // The gate renders "There is nothing at this address" when the flag is off, so
                    // asking for the tab strip asks the question the tests actually need answered.
                    var html = await response.Content.ReadAsStringAsync();
                    if (html.Contains("Latest", StringComparison.Ordinal)) return wasOn;
                }
            }
            catch (HttpRequestException) { }
            await Task.Delay(1000);
        }

        return null;
    }

    /// <summary>
    /// Refuses the run unless the host is serving the build that is on disk.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a crawl must not start without this.</b> On 2026-09-22 the every-seat sweep
    /// reported <b>879 findings and failed</b>, on thirteen "content clipped" rows — one of the two
    /// shapes the sweep treats as never a design choice. Restarting the host and running the same
    /// sweep, same code, same minute, gave <b>96 findings and passed</b>. Nothing about the site had
    /// changed.</para>
    ///
    /// <para>The host builds its static-asset URL map once, at startup. A later build that changes a
    /// fingerprint — <c>c40b809e</c> moved the clip browser's CSS out of isolation, which changed
    /// <c>Ben.Video.Editor.*.bundle.scp.css</c> — leaves a host that serves the NEW stylesheet text
    /// from disk while its map still only knows the OLD name. The <c>@import</c> 404s, every scoped
    /// style in that library is missing from every page, and the pages really are clipped and
    /// unreadable. The auditor was not wrong. The run was.</para>
    ///
    /// <para>That is the worst kind of bad result: not noise, which gets discounted, but a confident
    /// failure naming real selectors on real screens. It is indistinguishable from a regression
    /// until somebody restarts the host, and nobody restarts the host because the report looks like
    /// work to do.</para>
    ///
    /// <para>The test is the host's own words: its scoped-CSS bundle names the library bundles it
    /// expects, so asking the host for each one asks whether it can finish the page it just served.
    /// A host that cannot is stale, or broken, and either way its screens are not the product.</para>
    /// </remarks>
    protected static async Task RefuseAStaleHostAsync()
    {
        using var probe = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(20) };

        const string bundleUrl = "/Ben.Web.Website.styles.css";
        string bundle;
        try
        {
            using var got = await probe.GetAsync(bundleUrl);
            Assert.That(got.IsSuccessStatusCode, Is.True,
                $"{BaseUrl}{bundleUrl} answered {(int)got.StatusCode}. The host is not serving its own "
              + "scoped-CSS bundle, so nothing walked would be styled. Start the site before crawling.");
            bundle = await got.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException ex)
        {
            Assert.Fail($"Could not reach {BaseUrl}{bundleUrl}: {ex.Message}");
            return;
        }

        var imports = System.Text.RegularExpressions.Regex
            .Matches(bundle, @"@import\s+['""]([^'""]+)['""]")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        var missing = new List<string>();
        foreach (var href in imports)
        {
            using var got = await probe.GetAsync("/" + href.TrimStart('/'));
            if (!got.IsSuccessStatusCode) missing.Add($"{href} → {(int)got.StatusCode}");
        }

        Assert.That(missing, Is.Empty,
            "The host is serving a stylesheet that names files it cannot serve, which means it was "
          + "started before the build now on disk. Every scoped style in those libraries is missing "
          + "from every page, so a crawl would report real-looking clipping and contrast faults that "
          + "do not exist — 879 findings instead of 96, on 2026-09-22.\n\n  "
          + string.Join("\n  ", missing)
          + "\n\nRestart the site (scripts/run-e2e.sh, or the restart script) and run again.");
    }

    /// <summary>Puts the feed switch back where <see cref="TurnTheFeedOnAsync"/> found it.</summary>
    protected static async Task PutTheFeedBackAsync(bool? wasOn)
    {
        if (wasOn is not { } previous) return;
        if (await SuperAdminTokenAsync() is { } token) await SetFeedSwitchAsync(token, previous);
    }

    /// <summary>The site setting that switches the public feed on.</summary>
    protected const string FeedSwitchKey = "features.public-feed";

    /// <summary>The site setting that shows the store (storefront).</summary>
    protected const string StoreSwitchKey = "features.store";

    /// <summary>
    /// Sets the store switch and waits until the WEBSITE agrees — its feature snapshot refreshes
    /// every 30 seconds, so the API agreeing proves nothing about the page a browser is about to
    /// open (see <see cref="TurnTheFeedOnAsync"/>). Returns what the switch was before, or null
    /// when it could not be changed.
    /// </summary>
    protected static async Task<bool?> SetTheStoreAsync(bool on)
    {
        var token = await SuperAdminTokenAsync();
        if (token is null) return null;
        var wasOn = !await FeatureIsOffAsync(StoreSwitchKey);

        using (var http = new HttpClient { BaseAddress = new Uri(ApiUrl), Timeout = TimeSpan.FromSeconds(30) })
        {
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var response = await http.PutAsJsonAsync($"/api/admin/site-settings/{StoreSwitchKey}", new { value = on ? "true" : "false" });
            if (!response.IsSuccessStatusCode) return null;
        }

        using var probe = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(10) };
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                // Asks for the page each state draws, not merely the absence of the other: an API
                // that already says off behind a website that still says on draws neither.
                var html = await probe.GetStringAsync("/store");
                var settled = on
                    ? html.Contains("data-testid=\"store-hero\"", StringComparison.Ordinal)
                    : html.Contains("There is nothing at this address.", StringComparison.Ordinal);
                if (settled) return wasOn;
            }
            catch (HttpRequestException) { }
            await Task.Delay(1000);
        }
        return null;
    }

    /// <summary>Puts the store switch back where <see cref="SetTheStoreAsync"/> found it.</summary>
    protected static async Task PutTheStoreBackAsync(bool? wasOn)
    {
        if (wasOn is { } previous) await SetTheStoreAsync(previous);
    }

    private static async Task<bool> SetFeedSwitchAsync(string token, bool on)
    {
        using var http = new HttpClient { BaseAddress = new Uri(ApiUrl), Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var response = await http.PutAsJsonAsync(
                $"/api/admin/site-settings/{FeedSwitchKey}", new { value = on ? "true" : "false" });
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
    }

    /// <summary>
    /// A SuperAdmin bearer token, or null when sign-in failed.
    /// </summary>
    /// <remarks>
    /// A plain <c>HttpClient</c> rather than Playwright's request context: the Playwright instance
    /// is created per test and does not exist during <c>[OneTimeSetUp]</c>, which is where this is
    /// needed.
    /// </remarks>
    protected static async Task<string?> SuperAdminTokenAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri(ApiUrl), Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            using var response = await http.PostAsJsonAsync("/login",
                new { email = SuperAdminEmail, password = SuperAdminPassword });
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            return json.GetProperty("accessToken").GetString();
        }
        catch (HttpRequestException) { return null; }
    }

    /// <summary>
    /// Logs in as the specified user via the /login page and waits for redirect.
    /// </summary>
    /// <param name="email">Email address to enter into the login form.</param>
    /// <param name="password">Password to enter.</param>
    protected async Task LoginAsync(string email, string password)
    {
        await Page.GotoAsync($"{BaseUrl}/login");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // /login redirects away when someone is already signed in, so there is no form to fill and
        // the wait below would simply time out. Any test that switches user hits this — sign the
        // previous one out and come back. Without it, a fixture that logs in as a second person
        // fails at the form with no hint that the first session was the cause.
        if (!Page.Url.Contains("/login"))
        {
            await LogoutAsync();
            await Page.GotoAsync($"{BaseUrl}/login");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Still redirected: sign-out did not take, and there is nothing useful to do here but
            // say so rather than time out on a missing field.
            Assert.That(Page.Url, Does.Contain("/login"),
                $"could not reach the sign-in form to sign in as {email} — a previous session is "
                + "still active and signing out did not clear it");
        }

        await FillCredentialsAsync(email, password);

        // Submitting is retried for the same reason every other click here is: Blazor Server
        // attaches its handlers when the circuit connects, which happens *after* NetworkIdle, and
        // a click that lands in that window is silently dropped — the form just sits there. That
        // surfaced as "waiting for navigation" timeouts in tests that had nothing to do with
        // login, because the sign-in they depended on had quietly not happened.
        //
        // Scoped to the login <form>: the app bar also carries button[type='submit'] elements
        // (the icon toggle, and Sign Out when already authenticated), and an unscoped selector
        // matches one of those instead.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                await Page.ClickAsync("form button[type='submit']", new() { Timeout = 5_000 });
            }
            catch (TimeoutException)
            {
                continue;   // form still rendering
            }

            try
            {
                await Page.WaitForURLAsync(url => !url.Contains("/login"), new() { Timeout = 8_000 });
                await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                return;
            }
            catch (TimeoutException)
            {
                // The API rate-limits sign-in: a fixed one-minute window per client. A suite this
                // size, retrying, is exactly the traffic that limiter exists to refuse — so back
                // off and let the window roll rather than spending the retries inside it. This is
                // the suite adapting to a real protection, not the protection being wrong.
                if (await Page.GetByText("Too many sign-in attempts", new() { Exact = false })
                              .CountAsync() > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(20));
                }

                // ── Two answers that retrying cannot fix, and makes worse ────────
                //
                // Identity locks an account after five failed attempts. This loop makes five, so
                // one wrong password locks the account it was aimed at — and the seats here are
                // SHARED, so the next forty tests that sign in as that person fail too. A run on
                // 2026-09-04 turned four wrong passwords into sixty-two failures, none of which
                // were about the product.
                //
                // A wrong password will not become right on the fourth try, and a locked account
                // will not open before the loop ends. Stop, and report what the page said.
                if (await Page.GetByText("Invalid email or password", new() { Exact = false })
                              .CountAsync() > 0
                 || await Page.GetByText("locked", new() { Exact = false }).CountAsync() > 0)
                {
                    break;
                }

                // Dropped click, bad credentials, or a navigation still in flight from a sign-out
                // that had not finished — that last one moves the page out from under the form
                // mid-fill. Go back to a known state rather than assuming the form is still there,
                // then try again; the assert below reports honestly if it never takes.
                if (!Page.Url.Contains("/login"))
                {
                    await Page.GotoAsync($"{BaseUrl}/login");
                    await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    if (!Page.Url.Contains("/login")) return;   // already signed in as this user
                }

                await FillCredentialsAsync(email, password);
            }
        }

        // Say what the page was showing, not just that it did not move. "Never left the login page"
        // is the same message whether the credentials were rejected, the submit never fired, or a
        // navigation pulled the form away — and those need different fixes.
        var shown = "";
        try
        {
            shown = (await Page.Locator(".alert, .text-danger, .validation-message")
                               .AllInnerTextsAsync() is { Count: > 0 } texts)
                    ? string.Join(" | ", texts) : "(no error shown on the page)";
        }
        catch { shown = "(could not read the page)"; }

        Assert.That(Page.Url, Does.Not.Contain("/login"),
            $"sign-in as {email} never left the login page. Page reported: {shown}");
        // Not NetworkIdle: sign-in usually lands on the home page, which streams map tiles and
        // may never go quiet. Every test in the suite passes through here.
        await WaitUntilLoadedAsync();
    }

    private const string EmailSelector =
        "input[type='email'], input[placeholder*='email' i], input[id*='email' i], input[placeholder='you@example.com']";

    /// <summary>
    /// Types the credentials and confirms the fields actually hold them before returning.
    /// <para>
    /// Filling two Blazor-bound inputs back to back is not reliable: the first field's value
    /// change re-renders the component, and that render can land while the second field is being
    /// set, wiping it. The form then submits an empty password and the server answers "Invalid
    /// email or password" — with credentials that are perfectly good, which is what made this look
    /// like an account problem rather than a typing one.
    /// </para>
    /// </summary>
    /// <summary>
    /// Puts the credentials in the form, having first established that the page is interactive.
    /// </summary>
    /// <remarks>
    /// <para><b>Matching DOM values is not enough, and this used to check only that.</b> A Blazor
    /// Server page renders long before its circuit connects, and an <c>InputText</c> that is not
    /// yet wired accepts characters that never reach the server model. The box then reads back
    /// exactly what was typed while the server still holds something else.</para>
    ///
    /// <para>What makes that dangerous rather than merely flaky is the sign-in page's developer
    /// pre-fill: in Development it writes <c>DevLogin:Email</c> and <c>DevLogin:Password</c> into
    /// the model. So a submit landing in that window does not fail — it <b>succeeds as the
    /// developer account</b>, navigates away, and every caller believes it signed in as whoever it
    /// asked for. Tests then fail much later, somewhere unrelated, because they are looking at the
    /// wrong person's data.</para>
    ///
    /// <para>Waiting for that pre-fill to appear is the cure and the proof at once: it is written
    /// from a component lifecycle method, so it cannot show up before the circuit is live. Outside
    /// Development there is nothing to wait for, hence best-effort with a short bound.</para>
    /// </remarks>
    protected async Task FillCredentialsAsync(string email, string password)
    {
        var emailBox    = Page.Locator(EmailSelector).First;
        var passwordBox = Page.Locator("input[type='password']").First;

        try
        {
            await Expect(emailBox).Not.ToHaveValueAsync(string.Empty, new() { Timeout = 15_000 });
        }
        catch (Exception)
        {
            // No developer pre-fill configured — nothing to wait for. The retry below still applies.
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await emailBox.FillAsync(email);
            await passwordBox.FillAsync(password);

            if (await emailBox.InputValueAsync() == email
             && await passwordBox.InputValueAsync() == password)
                return;
        }
    }

    /// <summary>
    /// Gives a hosted-event booking form the name, phone and (signed out) email the organizer needs
    /// (item 235 slice 11d), filling only what the account did not already provide.
    /// </summary>
    /// <param name="prefix">The form's id prefix: <c>picker</c>, <c>ask</c> or <c>attend</c>.</param>
    protected async Task FillBookingContactAsync(string prefix, string? email = null)
    {
        var summary = Page.Locator($"#{prefix}-contact-summary");
        var phone = Page.Locator($"#{prefix}-phone");

        // Either the account filled everything and the form folded to one line, or it is asking.
        await Expect(summary.Or(phone)).ToBeVisibleAsync(new() { Timeout = 15_000 });
        if (!await phone.IsVisibleAsync()) return;

        async Task FillIfEmpty(string id, string value)
        {
            var field = Page.Locator($"#{prefix}-{id}");
            if (await field.CountAsync() == 0) return;
            if (string.IsNullOrWhiteSpace(await field.InputValueAsync())) await field.FillAsync(value);
        }

        await FillIfEmpty("first", "Test");
        await FillIfEmpty("last", "Guest");
        if (email is not null) await FillIfEmpty("email", email);
        await FillIfEmpty("phone", "615-555-0100");
    }

    /// <summary>
    /// Clicks <paramref name="target"/> until <paramref name="expected"/> appears, then returns.
    /// <para>
    /// Blazor Server attaches its event handlers when the circuit connects, which happens *after*
    /// NetworkIdle. A click that lands in that window is silently dropped: nothing throws, the
    /// page simply does not advance, and the next assertion fails somewhere unrelated — which is
    /// what made a visible, uniquely-named case report as "not visible".
    /// </para>
    /// <para>
    /// Retrying is more honest than sleeping: it costs nothing once the circuit is up, and it
    /// states the actual requirement — the click has to have had its effect.
    /// </para>
    /// </summary>
    protected async Task ClickUntilAsync(ILocator target, ILocator expected, int attempts = 4)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                await target.ClickAsync(new() { Timeout = 5_000 });
            }
            catch (TimeoutException)
            {
                continue;   // covered by an overlay mid-render; try again
            }

            try
            {
                // 8s, not 4s. The old figure was tuned on an idle machine, where every one of
                // these round trips lands in well under a second. Running 405 tests it does not:
                // a Blazor Server sign-in has to reach the API, the database and back over the
                // circuit, and 4s was close enough to that to lose occasionally — which is how
                // SigningUpRequiresConfirmingTheEmail failed a full run and then passed alone in
                // one second.
                //
                // Raising a timeout is the standard way to paper over a real defect, so it is
                // worth being precise about why it is not that here: this waits longer for a
                // CORRECT condition and cannot make a wrong one pass. Nothing became more
                // permissive, only more patient, and a genuinely broken page still fails — it just
                // takes about twenty seconds longer to say so.
                await expected.First.WaitForAsync(
                    new() { State = WaitForSelectorState.Visible, Timeout = 8_000 });
                return;
            }
            catch (TimeoutException)
            {
                // Click was dropped, or the page is still catching up.
            }
        }

        // Out of attempts: assert so the failure names what was actually missing. Generous,
        // because this is the message somebody will read — a timeout here should mean "it never
        // arrived", not "the machine was busy at the wrong moment".
        await Expect(expected.First).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Finds a sidebar link by name — through the nav's own filter box — and returns its locator.
    /// </summary>
    /// <remarks>
    /// <para>The sidebar is grouped by subject, so most entries sit inside a collapsed group and a
    /// bare <c>GetByRole(Link, "My Cases")</c> finds nothing. This broke eleven tests at once when
    /// the grouping landed, all with the same misleading symptom: a link "not visible" for a page
    /// that worked perfectly.</para>
    ///
    /// <para>Typing into the filter is how it is resolved here, deliberately, instead of clicking
    /// the group open: the filter prunes the menu to matches and expands everything left, which is
    /// both the real path a person uses to find an entry they cannot see and the only way these
    /// tests exercise the filter at all. A group renaming its children breaks this loudly; a group
    /// silently swallowing a link breaks it too — which is the point.</para>
    ///
    /// <para>The filter is typed via <c>FillAsync</c> and re-tried, because the box is a Blazor
    /// <c>@oninput</c> binding and the circuit-not-yet-live race erases early keystrokes rather
    /// than ignoring them — the same lesson as every other typed input in this suite.</para>
    /// </remarks>
    protected async Task<ILocator> FindSidebarLinkAsync(string name)
    {
        var filter = Page.Locator(".app-menu-filter-container #searchInput");
        var link = Page.Locator(".primary-nav").GetByRole(AriaRole.Link, new() { Name = name });

        for (var attempt = 0; attempt < 8; attempt++)
        {
            await filter.FillAsync(name);
            try
            {
                await Expect(link.First).ToBeVisibleAsync(new() { Timeout = 2_000 });
                return link.First;
            }
            catch (Exception)
            {
                // Keystrokes erased by the first interactive render, or the entry genuinely is
                // not offered to this account. Retry decides which.
            }
        }

        // Left visible-or-not for the caller's Expect to report with the caller's own context.
        return link.First;
    }

    /// <summary>Finds a sidebar link through the filter and follows it to <paramref name="urlPattern"/>.</summary>
    protected async Task OpenSidebarLinkAsync(string name, string urlPattern)
    {
        var link = await FindSidebarLinkAsync(name);
        await ClickUntilUrlAsync(link, urlPattern);

        // Leave the menu the way it was found — a filtered sidebar would quietly change what
        // every later assertion in the same test can and cannot see.
        var filter = Page.Locator(".app-menu-filter-container #searchInput");
        if (await filter.CountAsync() > 0) await filter.FillAsync(string.Empty);
    }

    /// <summary>
    /// Clicks <paramref name="target"/> until the URL matches <paramref name="urlPattern"/>.
    /// <para>
    /// The counterpart to <see cref="ClickUntilAsync"/> for a control whose only visible effect is
    /// navigation — a Telerik GridCommandButton that calls NavigationManager, for instance. There
    /// is no element to wait for, so waiting on the address is the honest expectation.
    /// </para>
    /// </summary>
    protected async Task ClickUntilUrlAsync(ILocator target, string urlPattern, int attempts = 4)
    {
        // Why a click never landed, in the failure message. "Clicking never navigated" is true and
        // useless: a control that is missing, covered, or disabled all read the same from here, and
        // each wants a different fix.
        var lastClickProblem = "none — every click was accepted";

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                await target.ClickAsync(new() { Timeout = 5_000 });
            }
            catch (TimeoutException ex)
            {
                // Diagnostics only: whatever happens here must never change what this method does,
                // or a helper meant to explain a failure becomes a way to cause one.
                try
                {
                    lastClickProblem = await target.CountAsync() == 0
                        ? "the control was not on the page"
                        : $"the click was never accepted ({ex.Message.Split('\n')[0]})";
                }
                catch
                {
                    lastClickProblem = "the click was never accepted, and the control could not be re-counted";
                }
                continue;
            }

            try
            {
                await Page.WaitForURLAsync(new Regex(urlPattern), new() { Timeout = 4_000 });
                await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                return;
            }
            catch (TimeoutException)
            {
                // Click dropped before the circuit was live; try again.
            }
        }

        Assert.That(Page.Url, Does.Match(urlPattern),
            $"clicking never navigated to something matching {urlPattern}. "
            + $"Clicks: {lastClickProblem}.");
    }

    /// <summary>
    /// Opens an organisation from /organizations by name.
    /// <para>
    /// The org name is a plain grid cell on the new site, not a link — you get in through the row's
    /// View control. Clicking the name text did nothing, which is why a whole cluster of case tests
    /// failed several steps later, reporting a case they never navigated to as "not visible".
    /// A link is still tried first, so this works against the original site too.
    /// </para>
    /// </summary>
    protected async Task<bool> OpenOrganizationAsync(string orgName)
    {
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var asLink = Page.GetByRole(AriaRole.Link, new() { Name = orgName, Exact = false });
        var row = Page.Locator("tr", new() { HasTextString = orgName }).First;

        ILocator opener;
        if (await asLink.CountAsync() > 0)
        {
            opener = asLink.First;
        }
        else if (await row.CountAsync() > 0)
        {
            opener = row.GetByRole(AriaRole.Button, new() { Name = "View" })
                        .Or(row.GetByRole(AriaRole.Link, new() { Name = "View" })).First;
        }
        else
        {
            return false;   // seed data differs; caller decides whether to skip
        }

        // The org hub is identified by its tab strip, which only the detail page renders.
        await ClickUntilAsync(opener, Main.GetByRole(AriaRole.Tab).Or(Main.Locator(".nav-tabs .nav-link")));
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        return true;
    }

    /// <summary>
    /// Switches to a tab on the organisation hub or a case, waiting for the click to take effect.
    /// Accepts the new site's <c>role="tab"</c> buttons and the original's plain text tabs.
    /// </summary>
    protected async Task OpenTabAsync(string tabName, ILocator expected)
    {
        var tab = Main.GetByRole(AriaRole.Tab, new() { Name = tabName, Exact = true })
                      .Or(Main.Locator(".nav-tabs .nav-link", new() { HasTextString = tabName }))
                      .Or(Main.GetByText(tabName, new() { Exact = true }))
                      .First;

        await Expect(tab).ToBeVisibleAsync(new() { Timeout = 8_000 });
        await ClickUntilAsync(tab, expected);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Waits for the page's "Loading…" placeholders to go away.
    /// <para>
    /// Needed because WaitForLoadState(NetworkIdle) proves nothing here: a page's data arrives
    /// over the SignalR circuit, not as an HTTP request, so NetworkIdle is already true while the
    /// component is still showing its placeholder. Tests that read the body straight after
    /// navigating captured "Loading your case…" and reported the content as missing.
    /// </para>
    /// </summary>
    protected async Task WaitUntilLoadedAsync(int timeoutMs = 15_000)
    {
        // The SPINNER is the signal, and the word is a hint at best.
        //
        // Both halves of that mattered. Several screens spin with no words at all, so a
        // text-only wait returned immediately and callers photographed and asserted against a
        // placeholder — that is how the first-run walk came back "ok" on a page still loading.
        // And a page is allowed to TALK about loading without doing it: /changes renders the
        // changelog line "…no longer says there are no accounts while it is still loading", which
        // a text-only wait reads as a page that never came up. BenLoaderOverlay is this site's one
        // loading marker and always renders a .spinner-border, so structure answers both.
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                // The spinner alone, and nothing about the words. BenLoaderOverlay is the one
                // loading marker on this site and ALWAYS renders a .spinner-border — including
                // behind BenListState — so a page with no spinner has finished, whatever its prose
                // happens to say. Consulting the text as well only reintroduced the /changes bug
                // from the other direction, and made every page that mentions loading wait.
                if (await Spinners.CountAsync() == 0) return;
            }
            catch (Exception) { return; }   // the page went away; the caller's assertion will say so
            await Task.Delay(150);
        }
        // Deliberately does not throw: a caller's own assertion is a better failure message than
        // a generic timeout here.
    }

    /// <summary>
    /// A Telerik grid's row command button, by the words of its tooltip (its <c>title</c>).
    /// </summary>
    /// <remarks>
    /// <para><b>Not a page-wide <c>GetByRole(Button, name)</c>.</b> Telerik's filter row renders one icon
    /// button per column carrying <c>aria-label="Open"</c> — fifteen of them on the SuperAdmin cases grid —
    /// so an accessible-name match finds a filter toggle long before it finds the row's command, clicks it
    /// happily, and reports that the row would not navigate. That cost this suite one standing failure for
    /// weeks. Command buttons live in <c>td.k-command-cell</c>, which is what keeps this to the rows.</para>
    /// <para>By title, not by text: since 2026-09-14 a grid's row actions are icons and their words are the
    /// tooltip (Ben: no text in grid buttons). The match is a substring, so "Open" finds "Open the case".</para>
    /// </remarks>
    protected ILocator GridCommand(string text, ILocator? within = null)
        => (within ?? Main).Locator($"td.k-command-cell button[title*='{text}']");

    /// <summary>
    /// Skips a guided tour if one has opened over the page, and says whether it did.
    /// </summary>
    /// <remarks>
    /// <para>Item 166 gave several pages a tour that <b>auto-launches for anybody who has not
    /// dismissed it</b>, which on a freshly seeded database is every seat the suite signs in as.
    /// Its backdrop covers the page, so the next click is intercepted and the test fails pointing
    /// at whatever it was reaching for — never at the tour, which is the thing in the way.</para>
    ///
    /// <para>This is what a person does: read it or skip it, then get on. Tests that are ABOUT a
    /// tour must not call this — they assert the tour is offered, which is a different job.</para>
    /// </remarks>
    protected async Task<bool> SkipAnyTourAsync(int timeoutMs = 3_000)
    {
        var skip = Page.Locator(".ben-tour-card").GetByRole(AriaRole.Button, new() { Name = "Skip tour" });

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await skip.CountAsync() > 0)
            {
                await skip.First.ClickAsync();
                await Expect(Page.Locator(".ben-tour-card")).ToHaveCountAsync(0, new() { Timeout = 5_000 });
                return true;
            }
            await Task.Delay(150);
        }

        return false;
    }

    /// <summary>
    /// Clicks whichever of <paramref name="candidates"/> is actually on top at its own centre.
    /// Returns false when every one of them is covered.
    /// <para>
    /// Map pins overlap: at the default zoom two of them sit close enough that the first in DOM
    /// order is underneath the second, and clicking it is intercepted until the timeout. Forcing
    /// the click would paper over that — a pin a person cannot click is a real problem — so this
    /// picks one that is genuinely reachable and leaves the overlap visible for what it is.
    /// </para>
    /// </summary>
    protected async Task<bool> ClickTopmostAsync(ILocator candidates)
    {
        var count = await candidates.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var candidate = candidates.Nth(i);
            if (!await candidate.IsVisibleAsync()) continue;

            var onTop = await candidate.EvaluateAsync<bool>(@"el => {
                const r = el.getBoundingClientRect();
                const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
                return !!hit && (hit === el || el.contains(hit) || hit.contains(el));
            }");
            if (!onTop) continue;

            await candidate.ClickAsync(new() { Timeout = 5_000 });
            return true;
        }
        return false;
    }

    /// <summary>
    /// Opens a case from an organisation: /organizations → the org → its Cases tab → the case.
    /// Returns false when the seed data does not contain them, so a caller can skip rather than
    /// fail.
    /// <para>
    /// Several fixtures grew their own copy of this walk, written against the original site, and
    /// each copy carried the same two faults: it clicked the organisation's *name* (a plain grid
    /// cell here, not a link) and then an unscoped "Cases" link, which resolves to the sidebar's
    /// own entry and navigates away. Those tests ended up on My Cases and reported the case they
    /// never opened as "not visible".
    /// </para>
    /// </summary>
    protected async Task<bool> OpenOrgCaseAsync(string orgName, string caseText)
    {
        if (!await OpenOrganizationAsync(orgName)) return false;

        await OpenTabAsync("Cases", Main.GetByText(caseText, new() { Exact = false })
                                        .Or(Main.GetByText("No cases", new() { Exact = false })));

        // Each case is a card with its own "Open" button, and the card's text is not itself
        // clickable — the same shape as the organisation list. Clicking the case name did nothing
        // at all, so the walk sat on the organisation hub and reported the case as missing.
        var row = Main.Locator(".card").Filter(new() { HasTextString = caseText }).First;
        if (await row.CountAsync() == 0) return false;

        // Not `.Or(row).First`: Or() resolves in DOM order, and the card contains the button, so
        // the card always won and the click landed on dead space. Pick the button when there is
        // one, and only fall back to the card — which the original site made clickable — when
        // there is not.
        var openButton = row.GetByRole(AriaRole.Button, new() { Name = "Open" })
                            .Or(row.GetByRole(AriaRole.Link, new() { Name = "Open" }));
        var opener = await openButton.CountAsync() > 0 ? openButton.First : row;

        // Waiting on the URL, not on a tab strip: the organisation hub has tabs of its own, so
        // "a tab strip is visible" was already true before the click and the helper returned
        // having gone nowhere.
        await ClickUntilUrlAsync(opener, @"/organizations/[0-9a-f\-]+/cases/[0-9a-f\-]+");
        await WaitUntilLoadedAsync();
        return true;
    }

    /// <summary>
    /// The page's main content, excluding the chrome.
    /// <para>
    /// Tab and link lookups need this on the new site: its sidebar lists "My Cases",
    /// "Cases &amp; Investigations", "Equipment", "Messages" and more, and sits *before* the
    /// content in the DOM — so an unscoped <c>GetByText("Cases").First</c> resolves to a nav entry
    /// and either clicks the wrong thing or trips strict mode. Matches the original site's layout
    /// too, where it simply narrows to the same region.
    /// </para>
    /// </summary>
    protected ILocator Main => Page.Locator(".app-content, main, .content-wrapper").First;

    /// <summary>Every spinner showing in the page's content — the site's loaders all draw Bootstrap's.</summary>
    /// <summary>Anything on the page that means "not finished yet".</summary>
    /// <remarks>
    /// <para>Every walk treats "no spinners" as "the page has settled", so whatever this misses is
    /// a page photographed, audited and reported while it is still empty — and an empty page has no
    /// clipping and no contrast fault, so it comes back CLEAN. A false pass, not a flaky one.</para>
    ///
    /// <para>It used to be <c>.spinner-border:visible</c> alone. <c>BenLoaderOverlay</c> renders a
    /// spinner, so its 33 uses were always seen; but **36 places across 34 files** say so with text
    /// instead — <c>&lt;p class="text-secondary"&gt;Loading…&lt;/p&gt;</c> and its div and span
    /// variants — and **33 of those files have no spinner anywhere**, so on those screens no walk
    /// could tell a loading page from a finished one. Found while fixing W2, where exactly that let
    /// ProductWalk photograph the admin dashboard mid-load and then report that it never said
    /// "Sign-ins and registrations" (2026-09-22, W13).</para>
    ///
    /// <para>Matched by exact text, not a substring: a page is allowed to TALK about loading. All
    /// 36 use the ellipsis character, and a new one spelled any other way will not be seen —
    /// <c>LoadingPlaceholdersAreVisibleToTheHarnessTests</c> fails when that happens rather than
    /// leaving it to be discovered by a report that says a screen is clean.</para>
    /// </remarks>
    protected ILocator Spinners => Main.Locator(
        ".spinner-border:visible, "
      + "p:text-is(\"Loading…\"):visible, "
      + "div:text-is(\"Loading…\"):visible, "
      + "span:text-is(\"Loading…\"):visible");

    /// <summary>Waits until the circuit has taken over the server-rendered page.</summary>
    /// <remarks>
    /// A page arrives prerendered and only starts loading its data once its circuit connects, so "no Loading
    /// placeholder" is briefly true of a page that has not begun. The prerendered HTML marks each interactive component
    /// with a <c>&lt;!--Blazor:…--&gt;</c> comment and the circuit consumes those comments as it attaches them — none
    /// left means the components are live and any placeholder they show is already on the page.
    /// </remarks>
    protected Task WaitForTheCircuitAsync() => Page.WaitForFunctionAsync(@"() => {
        const comments = document.createTreeWalker(document, NodeFilter.SHOW_COMMENT);
        while (comments.nextNode()) if (comments.currentNode.data.startsWith('Blazor:')) return false;
        return true;
    }", null, new() { Timeout = 60_000 });

    /// <summary>
    /// The typing surface of the site's rich-text box — one place that knows its shape.
    /// </summary>
    /// <remarks>
    /// <para>Two fixtures each carried <c>Page.FrameLocator(".k-editor iframe").Locator("body")</c>,
    /// which was right while the editor ran in Telerik's default Iframe mode. The 2026-09-06
    /// evaluation's W-R3 moved every editor on the site to <c>EditMode.Div</c> so the content
    /// inherits the page's font, and there is no longer an iframe to reach into — both fixtures
    /// then timed out waiting thirty seconds for an element that will never exist, in tests whose
    /// subject was a client request and an org message.</para>
    ///
    /// <para>Type into what this returns; never set its value from script. Setting an editor's
    /// value from script updates the DOM without ever reaching the bound C# field.</para>
    /// </remarks>
    protected ILocator EditorBody => Page.Locator(".k-editor [contenteditable='true']").First;

    /// <summary>
    /// Opens the header's profile menu, which is where the new site keeps the signed-in email and
    /// the Sign Out button. The original showed both directly in the app bar, so this is a no-op
    /// there — it only clicks when the menu's contents are not already visible.
    /// </summary>
    protected async Task OpenProfileMenuAsync()
    {
        var signOut = Page.GetByText("Sign Out");
        if (await signOut.IsVisibleAsync()) return;

        var profile = Page.Locator("[aria-label='Open Profile Dropdown'], .user-menu > button").First;
        if (!await profile.IsVisibleAsync()) return;

        // The button is Blazor-interactive; clicking before the circuit is live does nothing, so
        // retry rather than assuming the first click lands.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            // Do not click a menu that is already open — that would close it again.
            if (await profile.GetAttributeAsync("aria-expanded") == "true")
            {
                try
                {
                    await signOut.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
                    return;
                }
                catch (TimeoutException) { }
            }

            await profile.ClickAsync(new() { Timeout = 5_000 });
            try
            {
                await signOut.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
                return;
            }
            catch (TimeoutException) { /* circuit not ready yet — try again */ }
        }
    }

    /// <summary>
    /// Logs out through the header. On the new site Sign Out lives inside the profile dropdown,
    /// so the menu has to be opened first; on the original it is directly in the bar. Opening the
    /// menu when it is already open would close it, so this only clicks when Sign Out is hidden.
    /// </summary>
    protected async Task LogoutAsync()
    {
        await Page.GotoAsync(BaseUrl);
        // NOT NetworkIdle. This navigates to the HOME page, and the home page carries a map
        // that streams map tiles for as long as it is displayed — so "no network
        // activity for 500ms" is a condition it may never reach. Under the full suite's load it
        // did not: AVisitorOpensAThreadAndAProfile died here on a bare 30s timeout that named
        // this helper and nothing about what was wrong.
        //
        // OpenProfileMenuAsync below already retries until the circuit is live, so all this wait
        // has to do is let the placeholders clear.
        await WaitUntilLoadedAsync();

        await OpenProfileMenuAsync();

        var signOut = Page.GetByText("Sign Out");
        if (!await signOut.IsVisibleAsync()) return;   // already signed out

        await signOut.ClickAsync();

        // Wait for the sign-out to have actually happened before returning. Signing out navigates
        // to /login on its own, and returning early let the next LoginAsync start filling the form
        // while that navigation was still in flight — it then wiped the fields, the submit went
        // nowhere, and sign-in "failed" three times running for no reason visible in the app.
        try
        {
            await Page.GetByText("Sign In").Or(Page.Locator("form button[type='submit']"))
                      .First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            // Fall through: LoginAsync retries and reports honestly if it never takes.
        }
            // Not NetworkIdle: see LogoutAsync's first wait. Bounded, and it does not require
            // the page to fall silent.
            await WaitUntilLoadedAsync();
    }

    // ── Promoted from AccountTests for the journey fixture: erasure-safe input on
    // not-yet-interactive Blazor pages. See each method's remarks.

    /// <summary>Fills a field and retries until the value is actually there.</summary>
    protected async Task FillAndConfirmAsync(string selector, string value)
    {
        var field = Page.Locator(selector);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await field.FillAsync(value);
            if (await field.InputValueAsync() == value) return;
        }

        Assert.Fail($"{selector} would not hold \"{value}\" after five attempts.");
    }

    /// <summary>
    /// Types an @name, retrying until the characters actually stick.
    /// </summary>
    /// <remarks>
    /// <para>The trap this exists for, and it is not slowness. A Blazor Server page renders its
    /// inputs before the circuit connects, and this one binds <c>value="@_form.Handle"</c>. A
    /// character typed in that window goes into the DOM, and then the first interactive render
    /// overwrites the field with the server's value — which is empty. The keystroke is not merely
    /// ignored, it is <b>erased</b>, leaving an empty box and no echo.</para>
    ///
    /// <para>Measured, the page is interactive about 450ms after navigation on a cold host. So the
    /// cure is to type again rather than to wait longer: a generous timeout here only turns a fast
    /// failure into a slow one, and hides a real regression behind a minute and a half of nothing.
    /// Retrying costs one keystroke when the circuit is already up.</para>
    ///
    /// <para>The page's own echo — the hint repeating the normalised name — is the signal, because
    /// it can only change if a handler ran.</para>
    /// </remarks>
    protected async Task TypeHandleAsync(string handle)
    {
        var field = Page.Locator("#signup-handle");
        var firstChar = handle[..1].ToLowerInvariant();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            await field.ClickAsync();
            await field.PressSequentiallyAsync(handle[..1], new() { Delay = 20 });

            try
            {
                await Expect(Page.Locator(".form-text code").First)
                    .ToHaveTextAsync($"@{firstChar}", new() { Timeout = 1_500 });

                if (handle.Length > 1)
                    await field.PressSequentiallyAsync(handle[1..], new() { Delay = 20 });

                return;
            }
            catch (Exception)
            {
                // Swallowed by the circuit connecting mid-keystroke. Clear whatever survived and
                // try again — by the second or third attempt the page is always live.
                await field.FillAsync(string.Empty);
            }
        }

        var hint = await Page.Locator(".form-text code").First.InnerTextAsync();
        Assert.Fail(
            $"Typing the @name never took after ten attempts. The hint still shows \"{hint}\", "
            + "which means the page is not becoming interactive at all — a real fault, not a slow "
            + "start. Check the browser console: an exception during render kills the circuit and "
            + "leaves the page frozen exactly like this.");
    }
}
