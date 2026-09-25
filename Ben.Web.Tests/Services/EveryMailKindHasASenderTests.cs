using System.Text.RegularExpressions;
using Ben.Data.Common.Mail;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A letter the site declares is a letter the site sends (item 246).
/// </summary>
/// <remarks>
/// <para><b>What this is for.</b> Thirty-five kinds were declared, and eleven of them were named by
/// no sender anywhere — so the template editor offered somebody a letter to write that nothing
/// would ever use, and the outbox had a category that could never fill. Nothing could tell: every
/// kind compiles, the editor lists them all, and a template written for one of the eleven simply
/// never applies, silently, forever.</para>
///
/// <para>It also caught a live one. The tour sign-up and the tour reminder are sent by the same
/// method, and it named every letter a sign-up — so reminders were filed under the wrong kind and
/// <c>TourReminder</c> was used nowhere.</para>
///
/// <para><b>A ratchet.</b> The kinds still waiting for a sender are listed below with what they are
/// waiting for. The list may only get shorter: a new kind must be sent by something, and a kind
/// that gains a sender is deleted from here by the second test.</para>
/// </remarks>
public sealed class EveryMailKindHasASenderTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    /// <summary>
    /// Kinds that nothing sends yet, and what each is waiting on.
    /// </summary>
    /// <remarks>
    /// Every one of these is a letter the site means to write and does not. They are listed rather
    /// than quietly tolerated so that the editor's promise and the code's behaviour stay a known
    /// distance apart instead of an unknown one.
    /// </remarks>
    private static readonly Dictionary<string, string> NotSentYet = new(StringComparer.Ordinal)
    {
        // Empty, and it must stay that way. Every declared letter is now sent by something; the
        // last one out was AccountMadeForYou, which waited from the day this list was written
        // (2026-09-21) for the one path that really does make an account for somebody else to
        // start telling them about it.
    };

    /// <summary>Every kind named by something that actually sends, anywhere in the API.</summary>
    private static HashSet<string> NamedBySomething()
    {
        var named = new HashSet<string>(StringComparer.Ordinal);
        var api = Path.Combine(RepoRoot().FullName, "Ben.Data.WebApi");

        foreach (var file in Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
             || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            // The declarations themselves are not uses of them.
            if (file.EndsWith("MailKinds.cs", StringComparison.Ordinal)) continue;

            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"MailKinds\.(\w+)"))
                named.Add(m.Groups[1].Value);
        }
        return named;
    }

    /// <summary>The property names of every declared kind.</summary>
    private static List<string> DeclaredKinds()
        => typeof(MailKinds)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(MailKindInfo))
            .Select(f => f.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void A_declared_letter_is_sent_by_something()
    {
        var named = NamedBySomething();

        var orphans = DeclaredKinds()
            .Where(kind => !named.Contains(kind) && !NotSentYet.ContainsKey(kind))
            .ToList();

        Assert.True(orphans.Count == 0,
            "These letters are declared and nothing sends them, so the template editor offers "
          + "somebody a letter to write that will never be used:\n  "
          + string.Join("\n  ", orphans)
          + "\nGive each one to its sender, or add it to NotSentYet saying what it waits on.");
    }

    /// <summary>
    /// The waiting list may only get shorter.
    /// </summary>
    /// <remarks>
    /// Without this, a kind that gains a sender stays listed as unsent forever and the list stops
    /// describing anything — the failure mode of every allowlist nobody reads.
    /// </remarks>
    [Fact]
    public void The_waiting_list_does_not_name_letters_that_are_now_sent()
    {
        var named = NamedBySomething();
        var stale = NotSentYet.Keys.Where(named.Contains).OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.True(stale.Count == 0,
            "These are listed as not sent yet and something does send them now — delete them from "
          + "the list:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>
    /// A tour's reminder is a reminder, not a second sign-up letter.
    /// </summary>
    /// <remarks>
    /// One method sends both, and it named every letter a sign-up — so reminders were filed under
    /// the wrong kind in the outbox and no reminder template could ever apply. Named on its own
    /// because the ratchet above would have gone green the moment TourReminder appeared anywhere,
    /// including in a comment.
    /// </remarks>
    [Fact]
    public void The_tour_reminder_is_sent_as_a_reminder()
    {
        var mailer = Path.Combine(RepoRoot().FullName,
            "Ben.Data.WebApi", "Services", "Tours", "TourGuestMailer.cs");

        Assert.True(File.Exists(mailer), $"expected the tour mailer at {mailer}");
        var source = File.ReadAllText(mailer);

        Assert.Contains("MailKinds.TourReminder.Key", source, StringComparison.Ordinal);
        Assert.Contains("MailKinds.TourSignUp.Key", source, StringComparison.Ordinal);
    }
}
