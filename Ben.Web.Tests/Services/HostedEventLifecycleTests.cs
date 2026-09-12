using System.Text.RegularExpressions;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// One column, one answer: the state groupings and the migration that made them (item 235 phase 3).
/// </summary>
/// <remarks>
/// <para><b>What went wrong before.</b> An event's state was a published flag plus a cancelled
/// timestamp plus an archived timestamp, and six screens each combined them differently. The
/// entitlement count and the public list disagreed about a cancelled event, and the email door
/// went on taking bookings for one. Those are not three bugs; they are one shape.</para>
///
/// <para>These tests hold the replacement in the two places it can still drift: the arrays the
/// database is queried with must agree with the properties loaded rows are asked, and the
/// migration must backfill before it drops.</para>
/// </remarks>
public sealed class HostedEventLifecycleTests
{
    private static HostedEvent With(HostedEventLifecycleState state) => new()
    {
        Id = Guid.NewGuid(), Name = "x", UrlName = "x", LifecycleState = state,
    };

    [Theory]
    [InlineData(HostedEventLifecycleState.Draft)]
    [InlineData(HostedEventLifecycleState.Published)]
    [InlineData(HostedEventLifecycleState.Live)]
    [InlineData(HostedEventLifecycleState.Ended)]
    [InlineData(HostedEventLifecycleState.Archived)]
    [InlineData(HostedEventLifecycleState.Cancelled)]
    [InlineData(HostedEventLifecycleState.VenueWithdrawn)]
    public void What_a_query_asks_and_what_a_loaded_row_answers_are_the_same_question(
        HostedEventLifecycleState state)
    {
        // The arrays exist because a C# property cannot go inside an IQueryable; the properties
        // exist because writing the array out at every call site would be worse. Two spellings of
        // one rule is exactly the shape that drifts, so it is asserted rather than trusted.
        var hosted = With(state);

        Assert.Equal(HostedEventStates.OnThePublicSite.Contains(state), hosted.IsOnThePublicSite);
        Assert.Equal(HostedEventStates.TakingBookings.Contains(state), hosted.IsTakingBookings);
        Assert.Equal(state != HostedEventLifecycleState.Archived, hosted.IsActive);
    }

    [Fact]
    public void Every_state_is_accounted_for_by_the_groupings()
    {
        // A state added later and put in no group at all would be invisible: not on the public
        // site, not taking bookings, not called off, and nobody would notice until an event in it
        // vanished from a list.
        var grouped = HostedEventStates.OnThePublicSite
            .Concat(HostedEventStates.CalledOff)
            .Append(HostedEventLifecycleState.Draft)
            .Append(HostedEventLifecycleState.Archived)
            .Distinct()
            .ToHashSet();

        var all = Enum.GetValues<HostedEventLifecycleState>();

        Assert.Equal(all.Length, grouped.Count);
        foreach (var state in all)
            Assert.Contains(state, grouped);
    }

    [Fact]
    public void A_called_off_event_is_neither_public_nor_taking_bookings()
    {
        // The specific bug: a cancelled event went on counting against the band and went on
        // letting the email door in, because the two readers combined the old flags differently.
        foreach (var state in HostedEventStates.CalledOff)
        {
            var hosted = With(state);
            Assert.False(hosted.IsOnThePublicSite);
            Assert.False(hosted.IsTakingBookings);
        }
    }

    [Fact]
    public void An_event_that_is_over_is_readable_and_closed()
    {
        // Ended is on the public site and not taking bookings, which is the pair of answers no
        // single boolean could give. A request arriving after the night is somebody who has
        // misread the date, and taking it would put them on a list nobody will answer.
        var over = With(HostedEventLifecycleState.Ended);

        Assert.True(over.IsOnThePublicSite);
        Assert.False(over.IsTakingBookings);
    }

    /// <summary>
    /// Nothing decides what an event IS by reading one of its timestamps.
    /// </summary>
    /// <remarks>
    /// <para><b>The whole point of the column, as a rule.</b> The stamps say WHEN something
    /// happened and are still written; the state says WHAT the event is. The moment a second reader
    /// starts deriving the second from the first, the two can disagree — which is precisely how a
    /// cancelled event went on counting against a price band and how the email door went on letting
    /// people in.</para>
    ///
    /// <para>A source scan, because the fault is a shape rather than a behaviour: each individual
    /// reader was correct in isolation, and no test of any one of them could see the problem. Half
    /// the readers migrated is worse than none, and this is what refuses that state. Assignments
    /// are allowed — the endpoints have to write the stamps — so only reads are counted.</para>
    /// </remarks>
    [Fact]
    public void No_hosted_event_decides_what_it_is_from_a_timestamp()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Ben.slnx")))
            root = root.Parent;
        Assert.NotNull(root);

        // Where the question gets asked. Migrations write the stamps by definition, and the entity
        // itself is where the derived properties live.
        var searched = new[]
        {
            Path.Combine(root!.FullName, "Ben.Data.WebApi"),
            Path.Combine(root.FullName, "Ben.Web.Website.Library"),
        };

        var offences = new List<string>();

        foreach (var dir in searched)
        foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
        {
            if (Path.GetExtension(file) is not (".cs" or ".razor")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
             || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

            var text = File.ReadAllText(file);
            foreach (var (line, number) in text.Split('\n').Select((l, i) => (l, i + 1)))
            {
                // An assignment is fine — something has to stamp them.
                if (Regex.IsMatch(line, @"(CancelledAtUtc|ArchivedAtUtc|LiveAtUtc|EndedAtUtc)\s*=[^=]")) continue;

                // A read used as a condition is the fault: "is null", "is not null", "!= null".
                if (Regex.IsMatch(line,
                        @"(CancelledAtUtc|ArchivedAtUtc|LiveAtUtc|EndedAtUtc)\s*(is\s+(not\s+)?null|[!=]=\s*null)"))
                {
                    offences.Add($"{Path.GetFileName(file)}:{number} {line.Trim()}");
                }
            }
        }

        Assert.True(offences.Count == 0,
            "These decide what an event is from a timestamp. LifecycleState is the answer; the "
            + "stamps only say when.\n  " + string.Join("\n  ", offences));
    }

    // ── the migration ────────────────────────────────────────────────────────

    private static string MigrationSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var file = new DirectoryInfo(Path.Combine(dir!.FullName, "Ben.Data.Source", "Migrations"))
            .EnumerateFiles("*_HostedEventLifecycle.cs")
            .FirstOrDefault();
        Assert.True(file is not null, "The HostedEventLifecycle migration is gone. Update this guard.");
        return File.ReadAllText(file!.FullName);
    }

    /// <summary>
    /// The published flag is backfilled into the state before it is dropped, and never renamed.
    /// </summary>
    /// <remarks>
    /// <para>EF scaffolded this migration as a RENAME of <c>IsPublished</c> to
    /// <c>GoNoGoWeekReminderSent</c> — it had two bit columns, one going and one arriving, and
    /// matched them. Every published event would have become one whose week reminder had been
    /// sent, and whether it was published would have been gone. Run, that is silent: no error, no
    /// failed test, just every live event quietly a draft.</para>
    ///
    /// <para>Which is why the migration is read as text. Nothing else in the suite can see the
    /// difference between a rename and a backfill, because by the time a test has a database both
    /// have already happened.</para>
    /// </remarks>
    [Fact]
    public void The_migration_backfills_the_state_before_it_drops_the_flag()
    {
        var source = MigrationSource();

        Assert.DoesNotContain("RenameColumn", source);

        var backfill = source.IndexOf("SET [LifecycleState] =", StringComparison.Ordinal);
        var drop = source.IndexOf("DropColumn(name: \"IsPublished\"", StringComparison.Ordinal);

        Assert.True(backfill >= 0, "the migration no longer backfills LifecycleState");
        Assert.True(drop >= 0, "the migration no longer drops IsPublished");
        Assert.True(backfill < drop,
            "the state is backfilled AFTER the flag is dropped, which reads every live event as a draft");
    }

    /// <summary>
    /// The hold default is a legal one, and the constraint that judges it is added afterwards.
    /// </summary>
    /// <remarks>
    /// The scaffold gave <c>HoldMinutes</c> a default of 0 and then demanded 15 to 20160 of it.
    /// Every existing row would have failed the constraint and the whole migration would have
    /// rolled back — loud rather than silent, but only on a database that already had an event in
    /// it, which is every database except the one a developer tests on.
    /// </remarks>
    [Fact]
    public void The_hold_default_satisfies_the_constraint_that_is_added_after_it()
    {
        var source = MigrationSource();

        var column = Regex.Match(source,
            @"name:\s*""HoldMinutes"".*?defaultValue:\s*(\d+)", RegexOptions.Singleline);
        Assert.True(column.Success, "HoldMinutes no longer has a default in the migration");

        var minutes = int.Parse(column.Groups[1].Value);
        Assert.InRange(minutes, 15, 20160);

        var added = source.IndexOf(@"name: ""HoldMinutes""", StringComparison.Ordinal);
        var constrained = source.IndexOf("CK_HostedEvents_HoldMinutes", StringComparison.Ordinal);
        Assert.True(added < constrained,
            "the check constraint is added before the column it judges");
    }
}
