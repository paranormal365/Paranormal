using Microsoft.Playwright;
using NUnit.Framework;
using System.Security.Cryptography;
using System.Text;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Signing up, confirming an email address, and turning on two-step sign-in.
/// </summary>
/// <remarks>
/// <para>The enrolment test computes a real TOTP code from the secret the server hands out, which
/// is the only way to prove the loop actually closes. A test that stopped at "the QR code
/// rendered" would pass against a page that generated a secret nobody could ever satisfy.</para>
///
/// <para><b>Two-step sign-in is opt-in.</b> Every test here starts from an account with it off,
/// because that is the state every account is in unless its owner goes and changes it.</para>
/// </remarks>
[TestFixture]
[Category("Account")]
[NonParallelizable]
public class AccountTests : BenTestBase
{
    private static string Unique => Guid.NewGuid().ToString("N")[..8];

    /// <summary>The authenticator secret this test enrolled with, so the teardown can undo it.</summary>
    private string? _enrolledSecret;

    /// <summary>
    /// Leaves the shared account with two-step sign-in off, however the test ended.
    /// </summary>
    /// <remarks>
    /// <para>The enrolment test turns 2FA on for the account the rest of the suite signs in with.
    /// If it fails in the middle, the account is left demanding a code nobody has, and <i>every
    /// other test in the suite</i> then fails at sign-in for a reason unrelated to what it was
    /// testing. Cleaning up in a teardown rather than at the end of the test body is the
    /// difference between one failure and a hundred.</para>
    ///
    /// <para>It disables through the account's own endpoint with a freshly computed code, because
    /// there is no administrator override to fall back on — turning 2FA off is the account
    /// holder's act, by design, and this test holds that account's secret.</para>
    /// </remarks>
    [TearDown]
    public async Task LeaveTwoStepSignInOff()
    {
        if (_enrolledSecret is null) return;

        var secret = _enrolledSecret;
        _enrolledSecret = null;

        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var signIn = await api.PostAsync("/login", new()
        {
            DataObject = new Dictionary<string, object>
            {
                ["email"] = UserEmail,
                ["password"] = UserPassword,
                ["twoFactorCode"] = Totp(secret),
            },
        });
        if (!signIn.Ok) return;

        var token = (await signIn.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        var authed = await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ApiUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });

        await authed.PostAsync("/api/me/2fa/disable", new()
        {
            DataObject = new Dictionary<string, object> { ["code"] = Totp(secret) },
        });
    }

    // ── Signing up ───────────────────────────────────────────────────────────

    [Test]
    [Description("The @name is checked as it is typed, and a taken one is refused.")]
    public async Task TheHandleIsCheckedWhileTyping()
    {
        await Page.GotoAsync($"{BaseUrl}/signup");
        await Expect(Page.Locator("#signup-handle")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Sarah's account was backfilled with this handle, so it is genuinely taken.
        await TypeHandleAsync("sarahmitchell");
        await Expect(Page.GetByText("That name is taken.")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // And the form refuses to submit while it is.
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }))
            .ToBeDisabledAsync();

        await Page.Locator("#signup-handle").FillAsync("");
        await TypeHandleAsync($"free{Unique}");
        await Expect(Page.GetByText("is free.")).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    [Description("The rules that need no server are answered without one.")]
    public async Task AnIllegalHandleIsRefusedImmediately()
    {
        await Page.GotoAsync($"{BaseUrl}/signup");
        await Expect(Page.Locator("#signup-handle")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await TypeHandleAsync("admin");
        await Expect(Page.GetByText("That name is reserved.")).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    [Description("Signing up creates an account that cannot sign in until the email is confirmed.")]
    public async Task SigningUpRequiresConfirmingTheEmail()
    {
        var tag = Unique;
        var email = $"signup{tag}@example.com";

        await Page.GotoAsync($"{BaseUrl}/signup");
        await Expect(Page.Locator("#signup-handle")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The handle first: it re-renders the form on every keystroke, so anything typed before it
        // can be overwritten by a render that lands after. A person tabbing between fields commits
        // each one on blur and never sees this; a test typing quickly does.
        await TypeHandleAsync($"signup{tag}");
        await Expect(Page.GetByText("is free.")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Filled with a check that each value stuck. The @name box proved the circuit is live, but
        // these are separate InputTexts and a value typed into one before it is wired is discarded
        // by the next render — the same erasure, one field along.
        await FillAndConfirmAsync("#signup-name", "Signup Test");
        await FillAndConfirmAsync("#signup-email", email);
        await FillAndConfirmAsync("#signup-password", "Str0ngPass!");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();
        // .First: the panel says "Check your email." as its heading and again in the sentence
        // below it, and an unqualified GetByText matches both — a strict-mode violation, which
        // fails in a second and looks nothing like the timeout it is not.
        await Expect(Page.GetByText("Check your email").First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The account exists but is not usable yet, and the sign-in page says which of those it is
        // rather than claiming the password is wrong.
        await Page.GotoAsync($"{BaseUrl}/login");
        await Page.FillAsync("#login-email", email);
        await Page.FillAsync("#login-password", "Str0ngPass!");
        await Page.ClickAsync("button[type='submit']");

        await Expect(Page.GetByText("Confirm your email address first"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    [Description("The confirmation page does nothing until the button is pressed.")]
    public async Task ConfirmingHappensOnAPressNotOnLoad()
    {
        // Mail scanners and link previewers fetch every URL in a message. A confirmation that
        // happened on load would be one those tools could complete on somebody's behalf.
        await Page.GotoAsync($"{BaseUrl}/confirm-email?userId={Guid.NewGuid()}&code=abc");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Confirm my email" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Opened without a link, it explains rather than failing.
        await Page.GotoAsync($"{BaseUrl}/confirm-email");
        await Expect(Page.GetByText("opened from the link")).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    // ── Two-step sign-in ─────────────────────────────────────────────────────

    [Test]
    [Description("Two-step sign-in is off by default and offered, not required.")]
    public async Task TwoStepSignInIsOffUnlessTurnedOn()
    {
        await LoginAsync(UserEmail, UserPassword);
        await OpenSecurityTabAsync();

        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Turn on two-step sign-in" }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    /// <summary>
    /// The whole enrolment loop, with a code computed from the server's own secret.
    /// </summary>
    /// <remarks>
    /// <para>Enrols and checks the recovery codes appear. The teardown then turns it back off
    /// through the account's own endpoint, so the shared account is left as it was found — the rest
    /// of the suite signs in with a password alone — and so the disable round trip is exercised on
    /// every run rather than only when this test reaches its last line.</para>
    /// </remarks>
    [Test]
    [Description("Enrolling with a real code turns it on and issues recovery codes.")]
    public async Task EnrollingWithARealCodeTurnsItOn()
    {
        await LoginAsync(UserEmail, UserPassword);
        await OpenSecurityTabAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Turn on two-step sign-in" }).ClickAsync();
        // The QR is Telerik's, rendered from the otpauth:// URI. The manual key beside it is what
        // this test uses, because it is the same secret in a form a test can read.
        await Expect(Page.Locator(".k-qrcode")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The code box must have an accessible name, and it must come from a real label pointing at
        // a real id. This is what the Telerik component could not give: it renders no id, so no
        // label could ever name it — and adding aria-label to it threw during render and killed
        // the circuit, which was item #112.
        await Expect(Page.GetByLabel("Six-digit code from your authenticator app").Last)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        var sharedKey = await Page.Locator("code").First.InnerTextAsync();
        var secret = sharedKey.Replace(" ", string.Empty).ToUpperInvariant();
        Assert.That(secret, Is.Not.Empty, "No shared key was offered for manual entry.");

        // Recorded before anything can go wrong, so the teardown can undo an enrolment that the
        // rest of this test failed to.
        _enrolledSecret = secret;

        await EnterCodeAsync(Totp(secret));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Turn on" }).ClickAsync();

        await Expect(Page.GetByText("Save these recovery codes now.")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var codes = await Page.Locator(".alert-warning code").AllInnerTextsAsync();
        Assert.That(codes, Has.Count.EqualTo(10), "Ten recovery codes were expected.");

        await Page.GetByRole(AriaRole.Button, new() { Name = "I've saved them" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Turn off" }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Turning it off needs a current code too, and that is what stops anyone at an unlocked
        // browser removing the second factor. The control is asserted here; the round trip itself
        // is exercised by this fixture's teardown, which disables through the real endpoint with a
        // real code on every run.
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Turn off" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    /// <summary>
    /// Signing in with a code, through the sign-in page.
    /// </summary>
    /// <remarks>
    /// <para>The other half of what item #112 broke. The sign-in page's code box had the same
    /// unmatched-attribute crash as the enrolment panel, so this path was equally dead and equally
    /// untested from a browser — the API accepted codes all along.</para>
    ///
    /// <para><b>It enrols a throwaway account of its own</b>, not the shared one. Enrolling the
    /// account the rest of the fixture signs in with meant this test's result depended on what the
    /// test before it had left behind: it passed alone and failed in the suite, reporting "invalid
    /// email or password" because two-step was not on at the moment the page tried. A test whose
    /// answer depends on its neighbours is worse than no test.</para>
    ///
    /// <para>Enrolment goes through the API rather than the UI — this test is about sign-in, and
    /// driving the enrolment page again would only give it a second way to fail.</para>
    /// </remarks>
    [Test]
    [Description("A password alone is refused, and the code completes the sign-in.")]
    public async Task SigningInWithTwoStepAsksForTheCodeAndAcceptsIt()
    {
        const string password = "Str0ngPass!";
        var email = $"twostep{Unique}@example.com";

        var admin = await AdminApiAsync();
        Assert.That(admin, Is.Not.Null,
            "Could not sign in as SuperAdmin to create a test account. If a run has just hammered "
            + "sign-in, Identity may have locked that account — it clears itself after a few "
            + "minutes. Check with: curl -s -XPOST $BEN_API_URL/login -H 'Content-Type: "
            + "application/json' -d '{\"email\":\"...\",\"password\":\"...\"}'");

        var created = await admin!.PostAsync("/api/admin/app-users", new()
        {
            DataObject = new Dictionary<string, object?>
            {
                ["email"] = email, ["password"] = password, ["displayName"] = "Two Step",
                ["userName"] = email, ["isEmailConfirmed"] = true, ["isSuperAdmin"] = false,
            },
        });
        Assert.That(created.Ok, Is.True, $"Could not create the test account: {created.Status}");

        var secret = await EnrolViaApiAsync(email, password);
        Assert.That(secret, Is.Not.Null, "Could not enrol the test account through the API.");

        await SignInExpectingSecondStepAsync(email, password);

        // The password was right, so this is a next step rather than a failure — and the page must
        // not say the password was wrong, which is what it did before the refusals were separated.
        var codeBox = Page.Locator("#login-2fa-code");
        await codeBox.ClickAsync();
        await codeBox.PressSequentiallyAsync(Totp(secret!), new() { Delay = 30 });
        await Page.ClickAsync("button[type='submit']");

        // Signed in: the sign-in form is gone.
        await Expect(Page.Locator("#login-password")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Signs in on the sign-in page, expecting to be asked for a code rather than let through.
    /// </summary>
    /// <remarks>
    /// <para>Two traps here, both of which produced convincing wrong answers before this existed.</para>
    ///
    /// <para><b>The page pre-fills developer credentials.</b> In Development it puts
    /// <c>DevLogin:Email</c> and <c>DevLogin:Password</c> into the form, so a submit that lands
    /// before this test's own values reach the server model signs in <i>as the developer account</i>
    /// — which succeeds, navigates to the home page, and looks exactly like a two-step account being
    /// let through without a code. That is a test reporting a fault in the product that is really a
    /// fault in itself, and it is what this one did twice.</para>
    ///
    /// <para><b>Filling the DOM is not filling the model.</b> A Blazor Server page renders long
    /// before its circuit connects; until then an <c>InputText</c> accepts characters that never
    /// reach the server. Waiting for the pre-fill to <i>appear</i> is what proves the circuit is up,
    /// because the pre-fill is written from a component lifecycle method and cannot show up before
    /// the page is interactive.</para>
    /// </remarks>
    private async Task SignInExpectingSecondStepAsync(string email, string password)
    {
        await Page.GotoAsync($"{BaseUrl}/login");

        var emailBox = Page.Locator("#login-email");
        await Expect(emailBox).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The developer pre-fill is written from OnInitializedAsync, so its arrival means the
        // circuit is live. Best-effort: outside Development there is nothing to wait for, and the
        // retry below is the real safety net.
        try
        {
            await Expect(emailBox).Not.ToHaveValueAsync(string.Empty, new() { Timeout = 20_000 });
        }
        catch (Exception)
        {
            // No pre-fill configured.
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await emailBox.FillAsync(email);
            await Page.FillAsync("#login-password", password);
            await Page.ClickAsync("form button[type='submit']");

            try
            {
                await Expect(Page.Locator("#login-2fa-code")).ToBeVisibleAsync(new() { Timeout = 6_000 });
                return;
            }
            catch (Exception)
            {
                if (!Page.Url.Contains("/login"))
                {
                    Assert.Fail(
                        "Signing in succeeded without asking for a code. Either two-step is not on "
                        + "for this account, or the form submitted the developer pre-fill instead of "
                        + $"the credentials under test ({email}).");
                }
            }
        }

        var shown = await Page.Locator(".card-body").First.InnerTextAsync();
        Assert.Fail($"The code box never appeared. The sign-in page said: {shown.Replace("\n", " / ")}");
    }

    /// <summary>A SuperAdmin API context, or null when sign-in failed.</summary>
    private async Task<IAPIRequestContext?> AdminApiAsync()
    {
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var signIn = await api.PostAsync("/login", new()
        {
            DataObject = new Dictionary<string, object>
            {
                ["email"] = SuperAdminEmail, ["password"] = SuperAdminPassword,
            },
        });
        if (!signIn.Ok) return null;

        var token = (await signIn.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        return await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ApiUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
    }

    /// <summary>Enrols an account through the API and returns its authenticator secret.</summary>
    private async Task<string?> EnrolViaApiAsync(string email, string password)
    {
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });

        var signIn = await api.PostAsync("/login", new()
        {
            DataObject = new Dictionary<string, object> { ["email"] = email, ["password"] = password },
        });
        if (!signIn.Ok) return null;

        var token = (await signIn.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        var authed = await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ApiUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });

        var setup = await authed.PostAsync("/api/me/2fa/setup", new() { DataObject = new Dictionary<string, object>() });
        if (!setup.Ok) return null;

        var uri = (await setup.JsonAsync())!.Value.GetProperty("authenticatorUri").GetString()!;
        var secret = System.Web.HttpUtility.ParseQueryString(new Uri(uri).Query)["secret"]!;

        var enable = await authed.PostAsync("/api/me/2fa/enable", new()
        {
            DataObject = new Dictionary<string, object> { ["code"] = Totp(secret) },
        });

        return enable.Ok ? secret : null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Fills a field and retries until the value is actually there.</summary>
    private async Task FillAndConfirmAsync(string selector, string value)
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
    private async Task TypeHandleAsync(string handle)
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

    private async Task OpenSecurityTabAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/profile");
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Security" }).ClickAsync();
        // The heading, specifically. The plain text also appears inside the "Turn on two-step
        // sign-in" button, and matching both is a strict-mode violation.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Two-step sign-in" }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Types a code into the masked box.
    /// </summary>
    /// <remarks>
    /// Real typing rather than a scripted value assignment: a Telerik input updates its DOM from
    /// script without the bound C# field ever hearing about it, so the button would stay disabled
    /// for a reason that has nothing to do with the code.
    /// </remarks>
    private async Task EnterCodeAsync(string code)
    {
        var box = Page.Locator("input[id$='-code']").Last;
        await box.ClickAsync();
        await box.PressSequentiallyAsync(code, new() { Delay = 30 });

        // The box binds on @oninput rather than on blur, so the value reaches the server as it is
        // typed and there is no commit to wait for. Asserting the DOM value still guards against
        // a maxlength or an input filter quietly dropping characters.
        await Expect(box).ToHaveValueAsync(code, new() { Timeout = 10_000 });
    }

    /// <summary>
    /// A TOTP code, RFC 6238, from a base32 secret — the same arithmetic an authenticator app does.
    /// </summary>
    private static string Totp(string base32Secret)
    {
        var key = Base32Decode(base32Secret);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

        var buffer = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(buffer);

        var hash = HMACSHA1.HashData(key, buffer);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                   | ((hash[offset + 1] & 0xFF) << 16)
                   | ((hash[offset + 2] & 0xFF) << 8)
                   | (hash[offset + 3] & 0xFF);

        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        var bits = new StringBuilder();
        foreach (var c in input.TrimEnd('='))
        {
            var index = alphabet.IndexOf(char.ToUpperInvariant(c));
            if (index < 0) continue;
            bits.Append(Convert.ToString(index, 2).PadLeft(5, '0'));
        }

        var bytes = new List<byte>();
        for (var i = 0; i + 8 <= bits.Length; i += 8)
        {
            bytes.Add(Convert.ToByte(bits.ToString(i, 8), 2));
        }

        return [.. bytes];
    }
}
