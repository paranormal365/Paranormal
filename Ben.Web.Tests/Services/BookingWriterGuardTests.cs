using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Only one file writes a booking's status, its held nights or its umbrella row
/// (item 235 phase 4).
/// </summary>
/// <remarks>
/// <para><b>What went wrong, five times.</b> The organizer's board, the public booking endpoint,
/// the email door, the phone's RSVP path and the calendar's own attendee screen each wrote these
/// things, and each combined them differently. The phone wrote an Accepted attendee with no booking
/// behind it. Cancelling through the calendar left a confirmed booking pointing at a row that had
/// gone. Those are not five bugs, they are one shape — and a single writer is the only version of
/// this that does not have it.</para>
///
/// <para><b>A source scan, because the fault is a shape.</b> Each of the five was correct in
/// isolation and no test of any one of them could see the problem. What is being held is "there is
/// exactly one place this happens", and only something that reads all the places can hold it.</para>
///
/// <para><b>Comments are stripped before matching.</b> A file that merely explains why it does not
/// write a status is not a violation, and an earlier guard in this codebase accused one that
/// mentioned a class name in prose.</para>
/// </remarks>
public sealed class BookingWriterGuardTests
{
    /// <summary>The one file allowed to write these, plus the migrations that backfill them.</summary>
    private static readonly string[] Allowed =
    [
        "BookingTransitions.cs",
    ];

    /// <summary>
    /// Assignments that are the booking's own truth, and must move together.
    /// </summary>
    /// <remarks>
    /// <c>IsHolding</c> is here for the same reason as the rest: it is the parent's status copied
    /// onto the night row so the database's arbiter index can read it, and a second writer would
    /// mean either a room nobody can book or two parties in one bed.
    /// </remarks>
    private static readonly (string Pattern, string What)[] Guarded =
    [
        (@"\.Status\s*=\s*HostedEventBookingStatus\.", "a booking's status"),
        (@"\.IsHolding\s*=", "whether a night holds its unit"),
        (@"\.ReleasedUtc\s*=", "when a night went back"),
        (@"\.UmbrellaAttendeeId\s*=", "the umbrella attendee link"),
    ];

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    /// <summary>The file with its comments removed, so prose about a rule is not the rule.</summary>
    private static string WithoutComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(source, @"//.*?$", "", RegexOptions.Multiline);
    }

    [Fact]
    public void Nothing_but_BookingTransitions_moves_a_booking()
    {
        var api = new DirectoryInfo(Path.Combine(RepoRoot().FullName, "Ben.Data.WebApi"));
        var offences = new List<string>();

        foreach (var file in api.EnumerateFiles("*.cs", SearchOption.AllDirectories))
        {
            if (Allowed.Contains(file.Name)) continue;
            // Migrations write these columns by definition — that is what a backfill is.
            if (file.FullName.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}")) continue;
            if (file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

            var text = WithoutComments(File.ReadAllText(file.FullName));

            foreach (var (pattern, what) in Guarded)
            {
                if (Regex.IsMatch(text, pattern))
                    offences.Add($"{file.Name} writes {what}");
            }
        }

        Assert.True(offences.Count == 0,
            "Only BookingTransitions may write a booking's status, its nights' holdings or its "
            + "umbrella row — they are one fact expressed three ways, and anything that writes one "
            + "without the others makes the site disagree with itself.\n  "
            + string.Join("\n  ", offences.Distinct()));
    }

    [Fact]
    public void The_guard_is_pointed_at_a_file_that_exists()
    {
        // A guard whose subject has been renamed passes for ever while guarding nothing.
        var api = new DirectoryInfo(Path.Combine(RepoRoot().FullName, "Ben.Data.WebApi"));

        foreach (var name in Allowed)
            Assert.True(api.EnumerateFiles(name, SearchOption.AllDirectories).Any(),
                $"{name} no longer exists. Update this guard rather than deleting it.");
    }
}
