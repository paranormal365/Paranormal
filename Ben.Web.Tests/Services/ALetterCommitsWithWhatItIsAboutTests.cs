using Ben.Data.Common;
using Ben.Data.Common.Interfaces;
using Ben.Data.Common.Mail;
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

    /// <summary>
    /// Queueing inside the caller's transaction opens no connection of its own — and somebody who
    /// switched the letter off still does not get it.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a count and not a lock.</b> The hazard is a SECOND connection inside the
    /// caller's transaction: the opt-out query joins AppUsers, which a booking writes, so where
    /// readers wait on writers it stalls to the command timeout, and the preference lookup's catch
    /// — correct for SendAsync — then sends the letter anyway. This harness cannot show that
    /// stall: every context here shares one SQLite connection, so a "second" context is really the
    /// same one, reads straight through, and gets the right answer. An earlier version of this test
    /// asserted only the opt-out and passed against the broken code for exactly that reason.</para>
    ///
    /// <para>So it pins the property the fix actually rests on: the outbox asks its factory for
    /// nothing while queueing. The template lookup is given a separate factory, because the
    /// composer's read is of EmailTemplates, which no caller writes, and is not what this is
    /// about.</para>
    ///
    /// <para>Tour reminders, because the preference check only runs for a letter a person may
    /// decline. The booking acknowledgement 239b queues is not one — which is why nothing was
    /// broken yet, and why the next caller to queue a declinable letter this way would be.</para>
    /// </remarks>
    [Fact]
    public async Task Queueing_inside_a_transaction_opens_no_connection_of_its_own()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var outboxFactory = new CountingFactory(sqlite.Factory);
        var site = Options.Create(new SiteIdentity { Name = "IsHaunted.com" });
        var queue = new OutboxEmailService(
            outboxFactory, sender: null!, site,
            new MailComposer(sqlite.Factory, new MemoryCache(new MemoryCacheOptions()), site,
                             NullLogger<MailComposer>.Instance),
            NullLogger<OutboxEmailService>.Instance);

        var userId = Guid.NewGuid();
        var email  = $"{userId:N}@example.com";
        await using (var seed = await sqlite.NewContextAsync())
        {
            seed.Users.Add(new Ben.Data.Source.Entities.AppUser
            {
                Id = userId, Email = email, UserName = email,
                DisplayName = "Declined", DateCreated = DateTime.UtcNow,
            });
            seed.UserEmailOptOuts.Add(new Ben.Data.Source.Entities.UserEmailOptOut
            {
                Id = Guid.NewGuid(), AppUserId = userId, Kind = MailKinds.TourReminder.Key,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            await seed.SaveChangesAsync();
        }

        await using var db = await sqlite.NewContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        await queue.EnqueueAsync(db, new EmailMessage(
            email, "Your tour is soon", "<p>See you there.</p>", Kind: MailKinds.TourReminder.Key));

        Assert.Equal(0, outboxFactory.Created);
        Assert.Empty(db.ChangeTracker.Entries<Ben.Data.Source.Entities.OutboxEmail>());
    }

    /// <summary>A factory that counts what it hands out.</summary>
    private sealed class CountingFactory(IDbContextFactory<BenDataContext> inner)
        : IDbContextFactory<BenDataContext>
    {
        public int Created { get; private set; }

        public BenDataContext CreateDbContext()
        {
            Created++;
            return inner.CreateDbContext();
        }

        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default)
        {
            Created++;
            return inner.CreateDbContextAsync(ct);
        }
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
