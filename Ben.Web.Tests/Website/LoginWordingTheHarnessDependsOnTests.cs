using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The two sentences the sign-in helper stops on are still the two the page says.
/// </summary>
/// <remarks>
/// <para><b>What this protects.</b> <c>BenTestBase.LoginAsync</c> retries a submit up to five
/// times, because a Blazor Server click that lands before the circuit attaches is silently
/// dropped. ASP.NET Identity locks an account after five failed attempts. So without a way out,
/// one wrong password spends all five retries and locks the account it was aimed at — and the
/// suite's seats are SHARED, so every later test signing in as that person fails too. A run on
/// 2026-09-04 turned four wrong passwords into sixty-two failures, roughly forty-five of which
/// were that cascade and none of which were about the product.</para>
///
/// <para>The helper already breaks out on the two answers retrying cannot fix: "Invalid email or
/// password" and "locked". But it finds them by READING THE PAGE, so the protection is only as
/// good as the wording — and error copy is exactly the kind of text that gets rewritten. Reword
/// either sentence and the guard stops matching in silence, the retries resume, and the cascade
/// comes back looking like forty unrelated product failures.</para>
///
/// <para>Nothing asserted this before: on 2026-09-22 the only file in the repository mentioning
/// "Invalid email or password" outside the page itself was the helper relying on it.</para>
///
/// <para><b>Why here rather than beside the helper.</b> Ben.Web.Playwright sets
/// <c>IsTestProject=false</c> to stay out of the solution's test run, so a guard living there runs
/// only when somebody asks for it. This runs on every build.</para>
/// </remarks>
public sealed class LoginWordingTheHarnessDependsOnTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static string Read(params string[] parts)
    {
        var path = Path.Combine(RepoRoot(), Path.Combine(parts));
        Assert.True(File.Exists(path), $"expected to find {path}");
        return File.ReadAllText(path);
    }

    /// <summary>The page still refuses a bad password in the words the helper waits for.</summary>
    [Fact]
    public void The_sign_in_page_still_says_what_the_helper_stops_on()
    {
        var page = Read("Ben.Web.Website", "Components", "Pages", "Login.razor");

        Assert.True(page.Contains("Invalid email or password", StringComparison.Ordinal),
            "Login.razor no longer refuses a bad password with \"Invalid email or password\". "
          + "BenTestBase.LoginAsync watches for that exact phrase to stop retrying; without it the "
          + "helper will submit five times, Identity will lock the account on the fifth, and because "
          + "the suite's seats are shared every later test signing in as that person fails too. "
          + "Either keep the phrase or change the helper to match the new one.");

        Assert.True(page.Contains("locked", StringComparison.OrdinalIgnoreCase),
            "Login.razor no longer uses the word \"locked\" when an account is locked out. "
          + "BenTestBase.LoginAsync watches for it to stop retrying against an account that cannot "
          + "let it in yet. Keep the word, or change the helper.");
    }

    /// <summary>And the helper still stops on them, rather than only knowing about the limiter.</summary>
    [Fact]
    public void The_helper_still_breaks_out_on_both_answers()
    {
        var helper = Read("Ben.Web.Playwright", "BenTestBase.cs");

        Assert.True(helper.Contains("Invalid email or password", StringComparison.Ordinal),
            "BenTestBase no longer watches for \"Invalid email or password\". Its submit loop runs "
          + "five times and Identity locks an account on the fifth, so one wrong password against a "
          + "shared seat takes the rest of the suite down with it — which is what happened on "
          + "2026-09-04.");

        Assert.True(helper.Contains("\"locked\"", StringComparison.Ordinal),
            "BenTestBase no longer watches for \"locked\", so it will keep retrying against an "
          + "account that is already locked out and cannot admit it until the window passes.");
    }
}
