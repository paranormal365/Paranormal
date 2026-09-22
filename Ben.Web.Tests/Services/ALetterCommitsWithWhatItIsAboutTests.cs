using Ben.Data.Common;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A letter and the thing it is about commit together, or neither does (item 239b).
/// </summary>
/// <remarks>
/// <para><b>The window this closes.</b> 239a made every letter an outbox row, but
/// <c>OutboxEmailService.SendAsync</c> opens its own context and saves separately from the caller.
/// So a booking could commit while its confirmation failed to queue — a guest with a seat and no
/// letter — or a letter could be queued for a booking that then rolled back, telling somebody they
/// have a seat they do not have. Neither makes a noise, which is what makes it worth a test: the
/// retention warnings were dead for twelve days on exactly that quality.</para>
///
/// <para><b>Why SQLite and not InMemory.</b> InMemory has no transaction, so a failed
/// <c>SaveChangesAsync</c> does not roll anything back and the first test below would pass against
/// the bug it exists to catch. SQLite gives a real one.</para>
/// </remarks>
public sealed class ALetterCommitsWithWhatItIsAboutTests
{
    private static OutboxEmailService Outbox(IDbContextFactory<BenDataContext> factory)
    {
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com" });
        return new OutboxEmailService(
            factory, sender: null!, site,
            new MailComposer(factory, new MemoryCache(new MemoryCacheOptions()), site,
                             NullLogger<MailComposer>.Instance),
            NullLogger<OutboxEmailService>.Instance);
    }

    private static EmailMessage AnyLetter()
        => new("guest@benco.dev", "Your seat is confirmed", "<p>See you there.</p>");

    /// <summary>The caller's save commits the letter with it.</summary>
    [Fact]
    public async Task A_save_that_commits_leaves_exactly_one_letter()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var queue = Outbox(sqlite.Factory);

        await using (var db = await sqlite.NewContextAsync())
        {
            await queue.EnqueueAsync(db, AnyLetter());
            await db.SaveChangesAsync();
        }

        await using var read = await sqlite.NewContextAsync();
        Assert.Equal(1, await read.OutboxEmails.CountAsync());
    }

    /// <summary>
    /// And a caller that fails after queueing leaves none — the half this is really for.
    /// </summary>
    /// <remarks>
    /// The context is thrown away without saving, which is what an exception between the enqueue
    /// and the save amounts to. Against <c>SendAsync</c> this would leave a letter behind, because
    /// that method has already committed a row of its own by the time the caller gets back.
    /// </remarks>
    [Fact]
    public async Task A_caller_that_never_saves_leaves_no_letter_behind()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var queue = Outbox(sqlite.Factory);

        await using (var db = await sqlite.NewContextAsync())
        {
            await queue.EnqueueAsync(db, AnyLetter());
            // no SaveChangesAsync — the booking failed after the letter was queued
        }

        await using var read = await sqlite.NewContextAsync();
        Assert.Equal(0, await read.OutboxEmails.CountAsync());
    }

    /// <summary>
    /// Queueing alone does not write. Stated separately because it is the whole contract, and a
    /// future edit that adds a SaveChangesAsync "to be safe" would pass both tests above while
    /// quietly undoing the point of the method.
    /// </summary>
    [Fact]
    public async Task Queueing_does_not_save_by_itself()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var queue = Outbox(sqlite.Factory);

        await using var db = await sqlite.NewContextAsync();
        await queue.EnqueueAsync(db, AnyLetter());

        await using var read = await sqlite.NewContextAsync();
        Assert.Equal(0, await read.OutboxEmails.CountAsync());

        Assert.Single(db.ChangeTracker.Entries<Ben.Data.Source.Entities.OutboxEmail>());
    }

    /// <summary>A context is required: passing none is a programming error, not a silent no-op.</summary>
    [Fact]
    public async Task It_refuses_a_missing_context()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var queue = Outbox(sqlite.Factory);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => queue.EnqueueAsync(null!, AnyLetter()));
    }
}
