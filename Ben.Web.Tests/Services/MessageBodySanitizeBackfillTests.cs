using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The cleanup for message bodies written before sending sanitised them.
/// </summary>
/// <remarks>
/// Fixing the door left the room dirty: every body stored before 2026-09-04 is still rendered as
/// markup, so a payload posted last week still runs today. These pin the three things the pass has
/// to get right — it cleans, it keeps legitimate formatting, and it can be run twice.
/// </remarks>
public sealed class MessageBodySanitizeBackfillTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options)
        : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static MessageBodySanitizeBackfillService Build(IDbContextFactory<BenDataContext> factory)
        => new(factory, new CmsMarkupSanitizer(),
               NullLogger<MessageBodySanitizeBackfillService>.Instance);

    /// <summary>
    /// Starts the service and waits for the pass to finish.
    /// </summary>
    /// <remarks>
    /// <c>StopAsync</c> is not the way to wait: it signals the stopping token first, and this
    /// service checks that token between batches — so stopping it is exactly how a real shutdown
    /// cuts a pass short, and a test that used it would assert against a pass that never ran.
    /// </remarks>
    private static async Task RunAsync(MessageBodySanitizeBackfillService service)
    {
        await service.StartAsync(CancellationToken.None);
        if (service.ExecuteTask is not null) await service.ExecuteTask;
    }

    private const string Payload =
        "<p>Kickoff Friday.</p><img src=x onerror=\"window.stolen=document.cookie\">";

    private static async Task<(IDbContextFactory<BenDataContext>, Guid dirty, Guid clean, Guid notice)>
        SeedAsync()
    {
        var factory = CreateFactory();
        var dirty = Guid.NewGuid();
        var clean = Guid.NewGuid();
        var notice = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.OrgMessages.Add(new OrgMessage
        {
            Id = dirty, Body = Payload, DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = Guid.NewGuid(),
        });
        db.OrgMessages.Add(new OrgMessage
        {
            Id = clean, Body = "<p>Nothing wrong with me.</p>", DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = Guid.NewGuid(),
        });
        db.UserMessages.Add(new UserMessage
        {
            Id = notice, MessageSubject = "Membership",
            MessageBody = "Your application to join <strong>A Group</strong> was declined. "
                        + "<em><script>window.stolen=1</script></em>",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        return (factory, dirty, clean, notice);
    }

    [Fact]
    public async Task It_strips_a_handler_from_a_body_stored_before_the_fix()
    {
        var (factory, dirty, _, _) = await SeedAsync();

        await RunAsync(Build(factory));

        await using var db = await factory.CreateDbContextAsync();
        var body = await db.OrgMessages.AsNoTracking()
            .Where(m => m.Id == dirty).Select(m => m.Body).SingleAsync();

        Assert.DoesNotContain("onerror", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("document.cookie", body, StringComparison.OrdinalIgnoreCase);
        // The message still reads as the message somebody sent.
        Assert.Contains("Kickoff Friday.", body);
    }

    [Fact]
    public async Task It_cleans_notification_bodies_too_and_keeps_their_formatting()
    {
        var (factory, _, _, notice) = await SeedAsync();

        await RunAsync(Build(factory));

        await using var db = await factory.CreateDbContextAsync();
        var body = await db.UserMessages.AsNoTracking()
            .Where(m => m.Id == notice).Select(m => m.MessageBody).SingleAsync();

        Assert.DoesNotContain("<script", body, StringComparison.OrdinalIgnoreCase);
        // These notices bold the group's name on purpose; a cleanup that flattened them would
        // trade one defect for a hundred ugly messages.
        Assert.Contains("<strong>A Group</strong>", body);
    }

    [Fact]
    public async Task It_leaves_a_body_that_was_already_clean_exactly_as_it_was()
    {
        var (factory, _, clean, _) = await SeedAsync();
        const string before = "<p>Nothing wrong with me.</p>";

        await RunAsync(Build(factory));

        await using var db = await factory.CreateDbContextAsync();
        var body = await db.OrgMessages.AsNoTracking()
            .Where(m => m.Id == clean).Select(m => m.Body).SingleAsync();

        Assert.Equal(before, body);
    }

    /// <summary>
    /// Running it twice is the whole reason it carries no "done" flag: it stays registered and
    /// every later start has to be a no-op rather than a second rewrite.
    /// </summary>
    [Fact]
    public async Task Running_it_again_changes_nothing()
    {
        var (factory, dirty, _, _) = await SeedAsync();

        await RunAsync(Build(factory));

        await using (var first = await factory.CreateDbContextAsync())
        {
            var afterFirst = await first.OrgMessages.AsNoTracking()
                .Where(m => m.Id == dirty).Select(m => m.Body).SingleAsync();

            await RunAsync(Build(factory));

            await using var second = await factory.CreateDbContextAsync();
            var afterSecond = await second.OrgMessages.AsNoTracking()
                .Where(m => m.Id == dirty).Select(m => m.Body).SingleAsync();

            Assert.Equal(afterFirst, afterSecond);
        }
    }
}
