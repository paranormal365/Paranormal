using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Arranging an event, deciding who comes and spending money are three different jobs
/// (item 235 phase 4).
/// </summary>
/// <remarks>
/// <para><b>Where this came from.</b> Every hosted endpoint asked one question — "may you change
/// this group's settings?" — because that key was the only one wired up when the feature began. Two
/// things followed. The person who arranges the rooms had to be somebody who could also change the
/// group's billing, which no hotel would accept. And the booking board, which carries guests'
/// names, email addresses and dietary notes, was readable by ANY member of the group.</para>
///
/// <para><b>Publishing is the exception, and only publishing.</b> It is the single act that spends
/// a credit, so it genuinely is a money question and genuinely does belong to whoever holds the
/// settings key. Everything else has its own.</para>
///
/// <para>A source scan because the rule is about which key a file reaches for, and a behaviour test
/// of any one endpoint cannot see that the whole family reached for the wrong one.</para>
/// </remarks>
public sealed class HostedEventAccessGuardTests
{
    /// <summary>The hosted controllers this rule covers.</summary>
    private static readonly string[] Hosted =
    [
        "HostedEventBookingController.cs",
        "HostedEventMenuController.cs",
    ];

    /// <summary>
    /// The one file allowed to read the settings key, and the endpoints in it that may.
    /// </summary>
    /// <remarks>
    /// Publishing spends a credit and un-publishing, archiving and cancelling all change whether it
    /// is spending one — so the event's own lifecycle stays with the money key. What must never
    /// come back is the BOOKING side asking it.
    /// </remarks>
    private const string LifecycleController = "HostedEventController.cs";

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    private static string WithoutComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(source, @"//.*?$", "", RegexOptions.Multiline);
    }

    private static FileInfo Find(string name)
    {
        var api = new DirectoryInfo(Path.Combine(RepoRoot().FullName, "Ben.Data.WebApi"));
        var file = api.EnumerateFiles(name, SearchOption.AllDirectories).FirstOrDefault();
        Assert.True(file is not null, $"{name} is guarded here but no longer exists. Update this list.");
        return file!;
    }

    [Fact]
    public void No_booking_endpoint_asks_whether_you_may_change_the_groups_settings()
    {
        var offences = new List<string>();

        foreach (var name in Hosted)
        {
            var text = WithoutComments(File.ReadAllText(Find(name).FullName));

            if (text.Contains("OrganizationSecurityTable.OrganizationSettings", StringComparison.Ordinal))
                offences.Add($"{name} gates on the group's settings key");
        }

        Assert.True(offences.Count == 0,
            "Deciding who comes to an event is not the same job as changing a group's billing. "
            + "Use HostedEventAccess.\n  " + string.Join("\n  ", offences));
    }

    [Fact]
    public void Reading_the_board_needs_more_than_being_a_member()
    {
        // The hole this closed. The board carries guests' names, addresses and dietary notes — a
        // dietary note being a health disclosure somebody made to a venue so they would not be
        // poisoned — and every member of the group could read it.
        var text = WithoutComments(File.ReadAllText(Find("HostedEventBookingController.cs").FullName));

        Assert.Contains("CanReadBookingsAsync", text);
        Assert.DoesNotContain("if (!await IsMemberAsync(db, orgId, userId.Value, ct)) return Forbid();", text);
    }

    [Fact]
    public void Publishing_still_belongs_to_whoever_holds_the_money()
    {
        // The deliberate exception, asserted so that "tidying" it away is a failing test rather
        // than a quiet widening of who can spend ninety-nine dollars.
        var text = WithoutComments(File.ReadAllText(Find(LifecycleController).FullName));

        Assert.Contains("OrganizationSecurityTable.OrganizationSettings", text);
    }

    [Fact]
    public void Every_job_has_a_question_of_its_own()
    {
        // Four jobs, four keys. A file with one method would mean the split had been undone.
        var access = WithoutComments(File.ReadAllText(Find("HostedEventAccess.cs").FullName));

        foreach (var question in new[]
                 {
                     "CanReadEventAsync", "CanEditEventAsync",
                     "CanReadBookingsAsync", "CanDecideBookingsAsync", "CanRunTheDoorAsync",
                 })
            Assert.Contains(question, access);

        // And none of them is the money key.
        Assert.DoesNotContain("OrganizationSettings", access);
    }
}
