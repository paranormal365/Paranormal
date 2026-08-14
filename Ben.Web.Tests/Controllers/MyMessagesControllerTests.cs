using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for <see cref="MyMessagesController"/> — the recipient-scoped view of the platform message
/// system. The rule that matters most here is that a caller only ever sees or touches their own
/// <c>UserMessageTo</c> rows: the pre-existing controllers over these tables return every row
/// unfiltered and are SuperAdmin-gated for exactly that reason.
/// </summary>
public class MyMessagesControllerTests
{
    private static readonly DateTime Older = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Newer = new(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static MyMessagesController Build(IDbContextFactory<BenDataContext> factory, Guid? userId)
    {
        var ctrl = new MyMessagesController(factory);
        var claims = userId.HasValue
            ? new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())
              ], "Bearer"))
            : new ClaimsPrincipal(new ClaimsIdentity());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };
        return ctrl;
    }

    private static async Task<List<MyMessageRecord>> GetMineAsync(
        IDbContextFactory<BenDataContext> factory, Guid userId, bool unreadOnly = false)
    {
        var result = await Build(factory, userId).GetMine(unreadOnly, default);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<List<MyMessageRecord>>(ok.Value);
    }

    /// <summary>
    /// Seeds a message of <paramref name="typeId"/> from <paramref name="senderId"/> addressed to
    /// each recipient, and returns the recipient rows' ids in the order given.
    /// </summary>
    private static async Task<Guid[]> SeedMessageAsync(
        IDbContextFactory<BenDataContext> factory,
        Guid senderId, Guid typeId, DateTime sentAt, string subject,
        params (Guid UserId, DateTime? ReadAt)[] recipients)
    {
        await using var db = await factory.CreateDbContextAsync();

        if (!await db.UserMessageTypes.AnyAsync(t => t.Id == typeId))
            db.UserMessageTypes.Add(new UserMessageType
            {
                Id = typeId, Name = "Audit Record", IconClass = "k-i-bell", ColorClass = "info",
                IsActive = true, DateCreated = Older, CreatedByAppUserId = senderId,
            });

        if (!await db.AppUsers.AnyAsync(u => u.Id == senderId))
            db.AppUsers.Add(new AppUser
            {
                Id = senderId, Email = "sender@benco.dev", DisplayName = "The Sender",
            });

        var messageId = Guid.NewGuid();
        db.UserMessages.Add(new UserMessage
        {
            Id = messageId, UserMessageTypeId = typeId, MessageSubject = subject,
            MessageBody = $"<p>{subject}</p>", DateCreated = sentAt, CreatedByAppUserId = senderId,
        });

        var ids = new Guid[recipients.Length];
        for (var i = 0; i < recipients.Length; i++)
        {
            ids[i] = Guid.NewGuid();
            db.UserMessageTos.Add(new UserMessageTo
            {
                Id = ids[i], MessageId = messageId,
                ToAppUserId = recipients[i].UserId, DateLastRead = recipients[i].ReadAt,
            });
        }

        await db.SaveChangesAsync();
        return ids;
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMine_ReturnsUnauthorized_WhenNoUserClaim()
    {
        var result = await Build(CreateFactory(), userId: null).GetMine(false, default);
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task MarkRead_ReturnsUnauthorized_WhenNoUserClaim()
    {
        var result = await Build(CreateFactory(), userId: null).MarkRead(Guid.NewGuid(), default);
        Assert.IsType<UnauthorizedResult>(result);
    }

    // ── Reading ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMine_ReturnsEmpty_WhenNothingWasSentToMe()
    {
        var factory = CreateFactory();
        await SeedMessageAsync(factory, Guid.NewGuid(), Guid.NewGuid(), Newer, "For someone else",
            (Guid.NewGuid(), null));

        Assert.Empty(await GetMineAsync(factory, Guid.NewGuid()));
    }

    [Fact]
    public async Task GetMine_ReturnsOnlyMyRowOfASharedMessage()
    {
        // One message, two recipients: each must see their own read state, not the other's.
        var factory = CreateFactory();
        var me      = Guid.NewGuid();
        var them    = Guid.NewGuid();
        var ids = await SeedMessageAsync(factory, Guid.NewGuid(), Guid.NewGuid(), Newer, "Broadcast",
            (me, null), (them, Newer));

        var mine = await GetMineAsync(factory, me);

        var only = Assert.Single(mine);
        Assert.Equal(ids[0], only.Id);
        Assert.Null(only.ReadUtc);
    }

    [Fact]
    public async Task GetMine_FlattensSenderAndTypeOntoTheRecord()
    {
        var factory = CreateFactory();
        var me = Guid.NewGuid();
        await SeedMessageAsync(factory, Guid.NewGuid(), Guid.NewGuid(), Newer, "Case updated", (me, null));

        var only = Assert.Single(await GetMineAsync(factory, me));

        Assert.Equal("Case updated", only.Subject);
        Assert.Equal("<p>Case updated</p>", only.Body);
        Assert.Equal("Audit Record", only.TypeName);
        Assert.Equal("k-i-bell", only.TypeIconClass);
        Assert.Equal("The Sender", only.SentByDisplayName);
        Assert.Equal(Newer, only.SentUtc);
    }

    [Fact]
    public async Task GetMine_OrdersNewestFirst()
    {
        var factory = CreateFactory();
        var me   = Guid.NewGuid();
        var type = Guid.NewGuid();
        await SeedMessageAsync(factory, Guid.NewGuid(), type, Older, "Old", (me, null));
        await SeedMessageAsync(factory, Guid.NewGuid(), type, Newer, "New", (me, null));

        var mine = await GetMineAsync(factory, me);

        Assert.Equal(["New", "Old"], mine.Select(m => m.Subject));
    }

    [Fact]
    public async Task GetMine_WithUnreadOnly_ExcludesAlreadyRead()
    {
        var factory = CreateFactory();
        var me   = Guid.NewGuid();
        var type = Guid.NewGuid();
        await SeedMessageAsync(factory, Guid.NewGuid(), type, Older, "Already read", (me, Newer));
        await SeedMessageAsync(factory, Guid.NewGuid(), type, Newer, "Still unread",  (me, null));

        var unread = await GetMineAsync(factory, me, unreadOnly: true);

        Assert.Equal("Still unread", Assert.Single(unread).Subject);
        Assert.Equal(2, (await GetMineAsync(factory, me)).Count);
    }

    // ── Marking read ──────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkRead_StampsTheTimestampAndCountsTheOpen()
    {
        var factory = CreateFactory();
        var me  = Guid.NewGuid();
        var ids = await SeedMessageAsync(factory, Guid.NewGuid(), Guid.NewGuid(), Newer, "Hello", (me, null));

        Assert.IsType<NoContentResult>(await Build(factory, me).MarkRead(ids[0], default));

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.UserMessageTos.SingleAsync(t => t.Id == ids[0]);
        Assert.NotNull(row.DateLastRead);
        Assert.Equal(1, row.LastReadCount);
    }

    [Fact]
    public async Task MarkRead_IsIdempotentAndBumpsTheOpenCount()
    {
        var factory = CreateFactory();
        var me  = Guid.NewGuid();
        var ids = await SeedMessageAsync(factory, Guid.NewGuid(), Guid.NewGuid(), Newer, "Hello", (me, null));

        await Build(factory, me).MarkRead(ids[0], default);
        Assert.IsType<NoContentResult>(await Build(factory, me).MarkRead(ids[0], default));

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(2, (await db.UserMessageTos.SingleAsync(t => t.Id == ids[0])).LastReadCount);
    }

    [Fact]
    public async Task MarkRead_OnSomeoneElsesRow_LeavesItUnreadAndReportsNotFound()
    {
        // NotFound rather than Forbid: confirming an id exists would itself leak information.
        var factory = CreateFactory();
        var them = Guid.NewGuid();
        var ids  = await SeedMessageAsync(factory, Guid.NewGuid(), Guid.NewGuid(), Newer, "Theirs", (them, null));

        Assert.IsType<NotFoundResult>(await Build(factory, Guid.NewGuid()).MarkRead(ids[0], default));

        await using var db = await factory.CreateDbContextAsync();
        Assert.Null((await db.UserMessageTos.SingleAsync(t => t.Id == ids[0])).DateLastRead);
    }

    [Fact]
    public async Task MarkRead_ReturnsNotFound_ForAnUnknownId()
    {
        var result = await Build(CreateFactory(), Guid.NewGuid()).MarkRead(Guid.NewGuid(), default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task MarkAllRead_ClearsOnlyMyUnreadAndReportsTheCount()
    {
        var factory = CreateFactory();
        var me   = Guid.NewGuid();
        var them = Guid.NewGuid();
        var type = Guid.NewGuid();
        await SeedMessageAsync(factory, Guid.NewGuid(), type, Older, "Mine, unread",   (me, null));
        await SeedMessageAsync(factory, Guid.NewGuid(), type, Newer, "Mine, read",     (me, Newer));
        var theirs = await SeedMessageAsync(factory, Guid.NewGuid(), type, Newer, "Theirs", (them, null));

        var result = await Build(factory, me).MarkAllRead(default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(1, ok.Value);                                   // the already-read one isn't re-counted
        Assert.Empty(await GetMineAsync(factory, me, unreadOnly: true));

        await using var db = await factory.CreateDbContextAsync();
        Assert.Null((await db.UserMessageTos.SingleAsync(t => t.Id == theirs[0])).DateLastRead);
    }
}
