using Microsoft.EntityFrameworkCore;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Cleaning what was stored before the doors started sanitising (2026-09-20).
/// </summary>
/// <remarks>
/// A backfill rewrites rows somebody else wrote, so the things worth proving are that it changes
/// what it must, leaves alone what it must not, and does nothing at all the second time.
/// </remarks>
public sealed class MarkupFieldSanitizeBackfillTests
{
    private static readonly DateTime Now = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    private static MarkupFieldSanitizeBackfillService Service(SqliteTestDb sqlite)
        => new(sqlite.Factory, new CmsMarkupSanitizer(),
               NullLogger<MarkupFieldSanitizeBackfillService>.Instance);

    /// <summary>
    /// Runs the service to completion, as the host would on start.
    /// </summary>
    /// <remarks>
    /// <para><b>Awaits ExecuteTask, NOT StopAsync.</b> BackgroundService.StopAsync cancels its own
    /// token first and waits second, so calling it cancelled the backfill before it had cleaned
    /// anything — and the service caught the cancellation quietly, as it is meant to.</para>
    ///
    /// <para>Five of the six tests below PASSED while it did nothing, because "unchanged" was what
    /// they expected. Only the one asserting a change noticed. A test that cannot tell "correct"
    /// from "did not run" is the thing to watch for here.</para>
    /// </remarks>
    private static async Task RunAsync(SqliteTestDb sqlite)
    {
        var service = Service(sqlite);
        await service.StartAsync(CancellationToken.None);

        if (service.ExecuteTask is { } running) await running;
    }

    /// <summary>
    /// A case with one timeline entry, on a database with foreign keys switched on.
    /// </summary>
    /// <remarks>
    /// The author and the case are real rows, not invented ids: SqliteTestDb enforces foreign
    /// keys, which is the point of using it rather than the in-memory provider — a backfill that
    /// only works against a database with no constraints proves nothing about the one it runs on.
    /// </remarks>
    private static async Task<SqliteTestDb> SeedAsync(string body)
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();

        var authorId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var caseId = Guid.NewGuid();

        db.Users.Add(new AppUser
        {
            Id = authorId, Email = "author@example.test", UserName = "author@example.test",
            DisplayName = "An Investigator", DateCreated = Now,
        });

        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Night Watch", UrlName = "night-watch",
            DateCreated = Now, CreatedByAppUserId = authorId,
        });

        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, Title = "The house on Del Rio Pike",
            CaseYear = 2026, OrgCaseNumber = 42,
            Status = Ben.Data.Common.Enums.CaseStatus.Active,
            StreetAddress1 = "1201 Del Rio Pike", City = "Franklin", State = "TN", ZipCode = "37064",
            DateCaseOpened = Now, DateCreated = Now, CreatedByAppUserId = authorId,
        });

        db.CaseTimelineEntries.Add(new CaseTimelineEntry
        {
            Id = Guid.NewGuid(),
            CaseId = caseId,
            AuthorAppUserId = authorId,
            EntryType = Ben.Data.Common.Enums.CaseTimelineEntryType.ClientReport,
            Body = body,
            DateCreated = Now,
            DateUpdated = Now,
            CreatedByAppUserId = authorId,
        });

        await db.SaveChangesAsync();
        return sqlite;
    }

    [Fact]
    public async Task A_script_that_was_stored_before_the_door_closed_is_cleaned()
    {
        await using var sqlite = await SeedAsync("<p>Heard it.</p><script>alert('x')</script>");

        await RunAsync(sqlite);

        await using var db = await sqlite.NewContextAsync();
        var body = (await db.CaseTimelineEntries.ToListAsync()).Single().Body;

        Assert.DoesNotContain("<script", body!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Heard it.", body!);
    }

    /// <summary>
    /// The thing a backfill most easily gets wrong.
    /// </summary>
    /// <remarks>
    /// Somebody's formatting is their work. A cleanup that flattened it would be a worse outcome
    /// than the hole it closed, and nobody would notice until a group opened an old case.
    /// </remarks>
    [Fact]
    public async Task Legitimate_formatting_is_left_exactly_as_it_was()
    {
        const string written = "<p>It was <strong>very</strong> loud, and <em>cold</em>.</p>";
        await using var sqlite = await SeedAsync(written);

        await RunAsync(sqlite);

        await using var db = await sqlite.NewContextAsync();
        Assert.Equal(written, (await db.CaseTimelineEntries.ToListAsync()).Single().Body);
    }

    [Fact]
    public async Task Plain_text_that_was_never_markup_is_untouched()
    {
        const string written = "Just words. Nothing fancy.";
        await using var sqlite = await SeedAsync(written);

        await RunAsync(sqlite);

        await using var db = await sqlite.NewContextAsync();
        Assert.Equal(written, (await db.CaseTimelineEntries.ToListAsync()).Single().Body);
    }

    /// <summary>
    /// A case's history is not rewritten to record our maintenance.
    /// </summary>
    /// <remarks>
    /// Moving DateUpdated would make every entry in every old case appear edited on the day this
    /// deployed, which is a lie told to the people who own that case.
    /// </remarks>
    [Fact]
    public async Task Cleaning_a_row_does_not_move_its_timestamp()
    {
        await using var sqlite = await SeedAsync("<p>Heard it.</p><script>alert('x')</script>");

        await RunAsync(sqlite);

        await using var db = await sqlite.NewContextAsync();
        var entry = (await db.CaseTimelineEntries.ToListAsync()).Single();

        Assert.Equal(Now, entry.DateUpdated);
        Assert.Equal(Now, entry.DateCreated);
    }

    /// <summary>
    /// Idempotent, which is what lets it run on every start with no marker to keep in step.
    /// </summary>
    [Fact]
    public async Task Running_it_twice_changes_nothing_the_second_time()
    {
        await using var sqlite = await SeedAsync("<p>Heard it.</p><script>alert('x')</script>");

        await RunAsync(sqlite);

        await using (var db = await sqlite.NewContextAsync())
        {
            var first = (await db.CaseTimelineEntries.ToListAsync()).Single();
            await RunAsync(sqlite);

            await using var after = await sqlite.NewContextAsync();
            var second = (await after.CaseTimelineEntries.ToListAsync()).Single();

            Assert.Equal(first.Body, second.Body);
            Assert.Equal(first.DateUpdated, second.DateUpdated);
        }
    }

    [Fact]
    public async Task An_empty_database_is_no_trouble()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await RunAsync(sqlite);   // must not throw
        Assert.True(true);
    }
}
