using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// No password may be written into a tracked file.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> On 2026-09-02 eighteen accounts' passwords were found in sixteen
/// tracked files — the SuperAdmin among them. The repository is public and development shares
/// production's database, so every one of them signed in on the live site. They were removed, but
/// nothing stopped them coming back, and within the hour four more turned up that the first sweep
/// had missed: inline constants the e2e suite used to <i>register</i> accounts, which are not
/// <c>??</c> fallbacks and so did not match how anybody was looking.</para>
///
/// <para><b>What it looks for</b> is a literal that behaves like a password: near the word, long
/// enough, and mixing a digit with an upper-case letter or a symbol. That shape is what a real
/// credential has and what a CSS class, an input type or a sentence does not.</para>
///
/// <para><b>When this fails,</b> the fix is never to widen the allow-list. It is to take the value
/// out: read it from configuration with no fallback (<c>SeedData:DevData:Password</c>,
/// <c>BEN_*_PASSWORD</c>), or generate one per run when the test is creating the account itself
/// and nothing outside the run needs to know it.</para>
/// </remarks>
public sealed class NoCredentialsInTheRepoTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("Could not find the repository root.");
    }

    /// <summary>A quoted literal sitting near the word "password".</summary>
    private static readonly Regex Candidate = new(
        """(?i)password[^\n]{0,60}?[=:>?]\s*["']([^"'\s${}]{8,})["']""",
        RegexOptions.Compiled);

    /// <summary>
    /// Things that are demonstrably not credentials: variable names we read secrets FROM, markup
    /// and CSS, and placeholders.
    /// </summary>
    private static readonly Regex Benign = new(
        """(?i)(BEN_[A-Z_]*PASSWORD|SeedData|<[^>]+>|example|placeholder|redacted|your |xxxx|\bnull\b|NewPassword|CurrentPassword|ConfirmPassword|form-|input|autocomplete|type=)""",
        RegexOptions.Compiled);

    /// <summary>
    /// A real password mixes classes. A CSS class or an English word does not, so requiring a digit
    /// alongside an upper-case letter or a symbol is what separates "P@ssw0rd!" from "form-control"
    /// without an allow-list that grows every time somebody writes the word.
    /// </summary>
    private static bool LooksLikeASecret(string value)
    {
        if (value.Contains('-') || value.Contains(' ') || value.Contains('/')) return false;
        var digit  = value.Any(char.IsDigit);
        var upper  = value.Any(char.IsUpper);
        var symbol = value.Any(c => !char.IsLetterOrDigit(c));
        return digit && (upper || symbol);
    }

    private static IEnumerable<string> TrackedFiles()
    {
        var root = RepoRoot().FullName;
        var git = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git", Arguments = "ls-files", WorkingDirectory = root,
            RedirectStandardOutput = true, UseShellExecute = false,
        })!;
        var listing = git.StandardOutput.ReadToEnd();
        git.WaitForExit();

        foreach (var relative in listing.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!relative.EndsWith(".cs") && !relative.EndsWith(".swift") && !relative.EndsWith(".sh")
             && !relative.EndsWith(".json") && !relative.EndsWith(".ps1") && !relative.EndsWith(".razor"))
                continue;

            // This file carries, on purpose, one line in the exact shape the scan hunts — the
            // self-check below — and a scanner that flags its own fixture proves nothing except
            // that it works. Nothing else is excused; see the remarks for what to do instead.
            if (relative.Trim().EndsWith("NoCredentialsInTheRepoTests.cs", StringComparison.Ordinal))
                continue;

            var full = Path.Combine(root, relative.Trim());
            if (File.Exists(full)) yield return relative.Trim();
        }
    }

    [Fact]
    public void No_tracked_file_carries_a_password_literal()
    {
        var root = RepoRoot().FullName;
        var found = new List<string>();

        foreach (var relative in TrackedFiles())
        {
            var lines = File.ReadAllLines(Path.Combine(root, relative));
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (Benign.IsMatch(line)) continue;

                var match = Candidate.Match(line);
                if (!match.Success) continue;
                if (!LooksLikeASecret(match.Groups[1].Value)) continue;

                // The value itself is never echoed: a failing test that prints the credential has
                // published it to every CI log that ever runs.
                found.Add($"{relative}:{i + 1}");
            }
        }

        Assert.True(found.Count == 0,
            "A password literal is in a tracked file, and this repository is public while "
            + "development shares production's database — so it is a live credential:\n  "
            + string.Join("\n  ", found)
            + "\nRead it from configuration with no fallback, or generate one per run.");
    }

    /// <summary>
    /// The scan is worthless if it cannot recognise the thing it exists to catch, so it is shown
    /// a line in the shape of the one that started this and required to object to it.
    /// </summary>
    [Fact]
    public void The_scan_would_catch_what_it_was_written_for()
    {
        const string offending = """    protected static string Password => "Y@ung615x";""";

        Assert.False(Benign.IsMatch(offending), "the benign filter must not excuse a real one");
        var match = Candidate.Match(offending);
        Assert.True(match.Success, "the pattern must find a quoted literal near 'password'");
        Assert.True(LooksLikeASecret(match.Groups[1].Value), "and must judge it a secret");
    }

    /// <summary>And must not object to markup, which is most of what the word appears in.</summary>
    [Theory]
    [InlineData("""<input type="password" class="form-control" />""")]
    [InlineData("""    protected static string P => RequiredSecret("BEN_MEMBER_PASSWORD");""")]
    [InlineData("""        var seedPassword = config["SeedData:DevData:Password"];""")]
    public void The_scan_leaves_innocent_lines_alone(string line)
    {
        var flagged = !Benign.IsMatch(line)
                      && Candidate.Match(line) is { Success: true } m
                      && LooksLikeASecret(m.Groups[1].Value);

        Assert.False(flagged, "this line carries no credential");
    }
}
