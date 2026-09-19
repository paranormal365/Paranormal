using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The one-time conversion of plain-text case notes to the HTML notes are stored as since 2026-09-14.
/// </summary>
public sealed class CaseNoteBodyHtmlBackfillTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static async Task RunAsync(IDbContextFactory<BenDataContext> factory)
    {
        var service = new CaseNoteBodyHtmlBackfillService(factory, new CmsMarkupSanitizer(),
            NullLogger<CaseNoteBodyHtmlBackfillService>.Instance);
        await service.StartAsync(CancellationToken.None);
        if (service.ExecuteTask is not null) await service.ExecuteTask;
    }

    private static CaseNote Note(string body, DateTime? updated = null) => new()
    {
        Id = Guid.NewGuid(), CaseId = Guid.NewGuid(), AuthorAppUserId = Guid.NewGuid(), CreatedByAppUserId = Guid.NewGuid(),
        DateCreated = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), DateUpdated = updated, Body = body,
    };

    [Fact]
    public async Task Plain_notes_become_paragraphs_formatted_notes_stay_byte_for_byte_and_a_second_pass_changes_nothing()
    {
        var factory = new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var plain = Note("Called the client.\nNo answer.\n\nTry again Friday & note it.");
        const string formattedBody = "<p><strong>Bold</strong> finding</p>";
        var formatted = Note(formattedBody);
        var dirty = Note("<p>Hi</p><img src=x onerror=\"steal()\">");

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CaseNotes.AddRange(plain, formatted, dirty);
            await db.SaveChangesAsync();
        }

        await RunAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal("<p>Called the client.<br>No answer.</p><p>Try again Friday &amp; note it.</p>",
                (await db.CaseNotes.FindAsync(plain.Id))!.Body);
            Assert.Equal(formattedBody, (await db.CaseNotes.FindAsync(formatted.Id))!.Body);
            var cleaned = (await db.CaseNotes.FindAsync(dirty.Id))!;
            Assert.DoesNotContain("onerror", cleaned.Body, StringComparison.OrdinalIgnoreCase);
            Assert.Null(cleaned.DateUpdated);   // maintenance is not an edit
        }

        string[] afterFirst;
        await using (var db = await factory.CreateDbContextAsync())
            afterFirst = await db.CaseNotes.OrderBy(n => n.Id).Select(n => n.Body).ToArrayAsync();

        await RunAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
            Assert.Equal(afterFirst, await db.CaseNotes.OrderBy(n => n.Id).Select(n => n.Body).ToArrayAsync());
    }
}
