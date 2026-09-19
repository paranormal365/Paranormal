using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Every path that creates an account gives it an <c>@name</c>.
/// </summary>
/// <remarks>
/// <para><b>C1 of the 2026-09-06 evaluation.</b> <c>UserHandleService.AllocateAsync</c> had no
/// callers at all. Accounts created by an administrator, by a case invite or by an event magic
/// link got <c>Handle = null</c>, and stayed that way until the next API restart ran the backfill
/// — so for as long as the host kept running, those people could not be mentioned on the feed and
/// nothing on any screen explained why.</para>
///
/// <para>The four paths now allocate, and each has its own test. This exists for the fifth: the
/// account-creating path somebody adds next year, who has no reason to know that a null handle is
/// invisible rather than merely blank. A per-path test proves the paths that exist; only this
/// fails on the one that does not exist yet.</para>
///
/// <para><b>Source-scanned, and narrowly.</b> It reads object initialisers on <c>new AppUser</c>
/// in the API and asks whether each sets <c>Handle</c>. Two do not and should not — an audit
/// "before" snapshot and a throwaway object handed to the password validators — and neither is an
/// account. They are named here rather than pattern-matched, because "a construction that is not
/// a creation" is a judgement, and a judgement recorded by name is one somebody can disagree
/// with.</para>
/// </remarks>
public sealed class AccountCreationHandleGuardTests
{
    /// <summary>
    /// Constructions of <c>AppUser</c> that never become an account.
    /// </summary>
    /// <remarks>
    /// <para>One entry, not two. <c>PublicClientRequestController</c> also builds an
    /// <c>AppUser</c> — a throwaway handed to ASP.NET's password validators — and it needs no
    /// exemption, because nothing saves it and the scan below only looks at constructions that
    /// reach a save. An exemption that excuses nothing is a claim about the code that is not
    /// true.</para>
    /// </remarks>
    private static readonly (string File, string Why)[] NotAccounts =
    [
        ("MyProfileController.cs", "an audit snapshot of the values before an edit, held only to "
                                 + "diff against, and saved nowhere — the save later in that "
                                 + "method is the real user's"),
    ];

    /// <summary>
    /// The seeders, which deliberately leave the handle to <c>UserHandleBackfillService</c>.
    /// </summary>
    /// <remarks>
    /// Seeding runs at startup and the backfill runs at startup, so a seeded account has its
    /// handle before anybody can reach the site. Named here so the exemption is a decision on the
    /// record rather than a gap in the scan.
    /// </remarks>
    private const string SeedFolder = "SeedData";

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    /// <summary>
    /// Everything from the construction up to the moment the account is saved.
    /// </summary>
    /// <remarks>
    /// <para>Not just the object initialiser. The question is whether the account has an
    /// <c>@name</c> by the time it reaches the database, and one of the four paths allocates on
    /// the line AFTER the initialiser closes — reading the braces alone reported it as a fault
    /// when it is the fix.</para>
    ///
    /// <para>The window ends at the first <c>CreateAsync</c> or <c>SaveChangesAsync</c>, because
    /// that is the moment being asked about. A construction that never reaches one is not an
    /// account being created and is skipped.</para>
    /// </remarks>
    private static string? UpToTheSave(string text, int from)
    {
        var open = text.IndexOf('{', from);
        if (open < 0) return null;

        // Anything between "new AppUser" and its brace that is not whitespace means this is a
        // constructor call or a bare reference, not an initialiser we can read.
        if (text[from..open].Any(c => !char.IsWhiteSpace(c) && c != '(' && c != ')')) return null;

        var save = text.IndexOf("CreateAsync(", open, StringComparison.Ordinal);
        var write = text.IndexOf("SaveChangesAsync(", open, StringComparison.Ordinal);
        if (save < 0) save = int.MaxValue;
        if (write < 0) write = int.MaxValue;

        var end = Math.Min(save, write);
        return end == int.MaxValue ? null : text[open..end];
    }

    [Fact]
    public void Every_account_the_api_creates_is_given_a_handle()
    {
        var api = new DirectoryInfo(Path.Combine(RepoRoot().FullName, "Ben.Data.WebApi"));
        Assert.True(api.Exists, "Ben.Data.WebApi is not where this test expects it.");

        var offences = new List<string>();
        var seen = 0;

        foreach (var file in api.EnumerateFiles("*.cs", SearchOption.AllDirectories))
        {
            if (file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
             || file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
             || file.FullName.Contains($"{Path.DirectorySeparatorChar}{SeedFolder}{Path.DirectorySeparatorChar}"))
                continue;

            if (NotAccounts.Any(x => file.Name == x.File)) continue;

            var text = File.ReadAllText(file.FullName);
            foreach (Match m in Regex.Matches(text, @"\bnew AppUser\b"))
            {
                var body = UpToTheSave(text, m.Index + m.Length);
                if (body is null) continue;

                seen++;
                if (body.Contains("Handle", StringComparison.Ordinal)) continue;

                var line = text.Take(m.Index).Count(c => c == '\n') + 1;
                offences.Add($"{file.Name}:{line}");
            }
        }

        // Without this the scan could match nothing — a rename of AppUser, a move of the project —
        // and report success over an empty list.
        Assert.True(seen >= 4,
            $"Only {seen} account creations were found in the API. This test has stopped looking "
            + "at what it thinks it is looking at.");

        Assert.True(offences.Count == 0,
            $"""
             {offences.Count} account creation(s) leave Handle unset. An account with no @name
             cannot be mentioned and is invisible to the feed, and nothing on screen says why
             (C1, site evaluation 2026-09-06).

             Call UserHandleService.AllocateAsync, or — if this construction never becomes an
             account — add it to NotAccounts in this file with the reason.

               {string.Join("\n  ", offences)}
             """);
    }

    /// <summary>The named exemptions still exist, so the list cannot quietly rot.</summary>
    [Fact]
    public void The_exempt_files_are_still_there()
    {
        var api = new DirectoryInfo(Path.Combine(RepoRoot().FullName, "Ben.Data.WebApi"));
        foreach (var (name, why) in NotAccounts)
            Assert.True(api.EnumerateFiles(name, SearchOption.AllDirectories).Any(),
                $"{name} is exempt from the handle guard ({why}) but no longer exists. "
                + "Remove the exemption.");
    }
}
