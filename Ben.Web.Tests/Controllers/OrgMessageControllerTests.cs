using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for OrgMessageController — inbox, sent, send, view-tracking, mark-read.
/// </summary>
public class OrgMessageControllerTests
{
    // Non-pooled: Send uses FirstAsync with required Include(m.AuthorAppUser)
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SimpleFactory(opts);
    }

    private static IMapper CreateMapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<OrgMessageRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is OrgMessage msg
                ? new OrgMessageRecord { Id = msg.Id, OrganizationId = msg.OrganizationId, AuthorAppUserId = msg.AuthorAppUserId, Body = msg.Body, ChannelType = msg.ChannelType, IsPublic = msg.IsPublic, ViewCount = msg.ViewCount, DateCreated = msg.DateCreated, CreatedByAppUserId = msg.CreatedByAppUserId }
                : new OrgMessageRecord { Body = "", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.Empty });
        m.Setup(x => x.Map<IEnumerable<OrgMessageRecord>>(It.IsAny<object>()))
            .Returns<object>(o => o is IEnumerable<OrgMessage> list
                ? list.Select(msg => new OrgMessageRecord { Id = msg.Id, OrganizationId = msg.OrganizationId, AuthorAppUserId = msg.AuthorAppUserId, Body = msg.Body, ChannelType = msg.ChannelType, ViewCount = msg.ViewCount, DateCreated = msg.DateCreated, CreatedByAppUserId = msg.CreatedByAppUserId })
                : []);
        return m.Object;
    }

    private static OrgMessageController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new OrgMessageController(
            factory, CreateMapper(),
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"))
            }
        };
        return ctrl;
    }

    private static async Task<(IDbContextFactory<BenDataContext>, Guid orgId, Guid senderId, Guid recipientId)> SeedAsync()
    {
        var factory     = CreateFactory();
        var orgId       = Guid.NewGuid();
        var senderId    = Guid.NewGuid();
        var recipientId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = senderId,    UserName = "sender@t.com",    NormalizedUserName = "SENDER@T.COM",    Email = "sender@t.com",    NormalizedEmail = "SENDER@T.COM",    DateCreated = DateTime.UtcNow });
        db.Users.Add(new AppUser { Id = recipientId, UserName = "recipient@t.com", NormalizedUserName = "RECIPIENT@T.COM", Email = "recipient@t.com", NormalizedEmail = "RECIPIENT@T.COM", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = orgId, Name = "Test Org", UrlName = "test", DateCreated = DateTime.UtcNow, CreatedByAppUserId = senderId });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = senderId,    Role = OrganizationMemberRole.Manager, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = senderId });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = recipientId, Role = OrganizationMemberRole.Member,  IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = senderId });
        await db.SaveChangesAsync();
        return (factory, orgId, senderId, recipientId);
    }

    // ── GetInbox ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetInbox_ReturnsOnlyReceivedMessages()
    {
        var (factory, orgId, senderId, recipientId) = await SeedAsync();
        var sender    = Build(factory, senderId);
        var recipient = Build(factory, recipientId);

        await sender.Send(orgId, new SendOrgMessageRequest(OrgMessageChannel.DirectMessage, "Hi", "Hello there", false, null, null, [recipientId]), default);

        var ok   = Assert.IsType<OkObjectResult>((await recipient.GetInbox(orgId, default)).Result);
        var list = Assert.IsAssignableFrom<IEnumerable<OrgMessageRecord>>(ok.Value);
        Assert.Single(list);
    }

    [Fact]
    public async Task GetInbox_Sender_ReturnsEmpty()
    {
        var (factory, orgId, senderId, recipientId) = await SeedAsync();
        var sender = Build(factory, senderId);
        await sender.Send(orgId, new SendOrgMessageRequest(OrgMessageChannel.DirectMessage, null, "Hello", false, null, null, [recipientId]), default);

        var ok = Assert.IsType<OkObjectResult>((await sender.GetInbox(orgId, default)).Result);
        Assert.Empty((IEnumerable<OrgMessageRecord>)ok.Value!);
    }

    // ── GetSent ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSent_ReturnsSentMessages()
    {
        var (factory, orgId, senderId, recipientId) = await SeedAsync();
        var sender = Build(factory, senderId);
        await sender.Send(orgId, new SendOrgMessageRequest(OrgMessageChannel.DirectMessage, null, "Sent msg", false, null, null, [recipientId]), default);

        var ok   = Assert.IsType<OkObjectResult>((await sender.GetSent(orgId, default)).Result);
        var list = Assert.IsAssignableFrom<IEnumerable<OrgMessageRecord>>(ok.Value);
        Assert.Single(list);
    }

    // ── Send ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Send_DirectMessage_ReturnsCreated()
    {
        var (factory, orgId, senderId, recipientId) = await SeedAsync();
        var sender = Build(factory, senderId);
        var result = await sender.Send(orgId, new SendOrgMessageRequest(OrgMessageChannel.DirectMessage, "Subject", "Body text", false, null, null, [recipientId]), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<OrgMessageRecord>(created.Value);
        Assert.Equal("Body text", dto.Body);
        Assert.Equal(senderId, dto.AuthorAppUserId);
    }

    [Fact]
    public async Task Send_Broadcast_AutoAddsAllMembersAsRecipients()
    {
        var (factory, orgId, senderId, recipientId) = await SeedAsync();
        var sender = Build(factory, senderId);
        await sender.Send(orgId, new SendOrgMessageRequest(OrgMessageChannel.OrgBroadcast, null, "Broadcast!", false, null, null, []), default);

        await using var db = await factory.CreateDbContextAsync();
        var msg = await db.OrgMessages.FirstAsync(m => m.OrganizationId == orgId);
        var recipientCount = await db.OrgMessageRecipients.CountAsync(r => r.OrgMessageId == msg.Id);
        Assert.Equal(1, recipientCount); // recipientId (senderId excluded)
    }

    [Fact]
    public async Task Send_PersistsRecipients()
    {
        var (factory, orgId, senderId, recipientId) = await SeedAsync();
        var sender = Build(factory, senderId);
        await sender.Send(orgId, new SendOrgMessageRequest(OrgMessageChannel.DirectMessage, null, "Hi", false, null, null, [recipientId]), default);

        await using var db = await factory.CreateDbContextAsync();
        var msg = await db.OrgMessages.FirstAsync(m => m.OrganizationId == orgId);
        Assert.True(await db.OrgMessageRecipients.AnyAsync(r => r.OrgMessageId == msg.Id && r.RecipientAppUserId == recipientId));
    }

    // ── GetById (view tracking) ───────────────────────────────────────────────

    [Fact]
    public async Task GetById_RecordsViewAndMarksRead()
    {
        var (factory, orgId, senderId, recipientId) = await SeedAsync();
        var sender    = Build(factory, senderId);
        var recipient = Build(factory, recipientId);

        var msgId = ((OrgMessageRecord)((CreatedAtActionResult)(await sender.Send(orgId, new SendOrgMessageRequest(OrgMessageChannel.DirectMessage, null, "Hi", false, null, null, [recipientId]), default)).Result!).Value!).Id;

        var ok  = Assert.IsType<OkObjectResult>((await recipient.GetById(orgId, msgId, default)).Result);
        var dto = Assert.IsType<OrgMessageRecord>(ok.Value);
        Assert.True(dto.IsReadByCurrentUser);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.OrgMessageViews.AnyAsync(v => v.OrgMessageId == msgId && v.ViewerAppUserId == recipientId));
    }

    [Fact]
    public async Task GetById_SecondView_DoesNotDuplicateViewRecord()
    {
        var (factory, orgId, senderId, recipientId) = await SeedAsync();
        var sender    = Build(factory, senderId);
        var recipient = Build(factory, recipientId);
        var msgId = ((OrgMessageRecord)((CreatedAtActionResult)(await sender.Send(orgId, new SendOrgMessageRequest(OrgMessageChannel.DirectMessage, null, "Hi", false, null, null, [recipientId]), default)).Result!).Value!).Id;

        await recipient.GetById(orgId, msgId, default);
        await recipient.GetById(orgId, msgId, default);

        await using var db = await factory.CreateDbContextAsync();
        var viewCount = await db.OrgMessageViews.CountAsync(v => v.OrgMessageId == msgId && v.ViewerAppUserId == recipientId);
        Assert.Equal(1, viewCount);
    }

    [Fact]
    public async Task GetById_NeitherAuthorNorRecipient_ReturnsForbid()
    {
        // The core of the fix: this used to check only OrganizationId == orgId — any authenticated
        // user could read another org's private internal message by id, with a side effect
        // (marks read, increments ViewCount) for a "viewer" who was never a recipient.
        var (factory, orgId, senderId, recipientId) = await SeedAsync();
        var sender   = Build(factory, senderId);
        var msgId = ((OrgMessageRecord)((CreatedAtActionResult)(await sender.Send(orgId, new SendOrgMessageRequest(OrgMessageChannel.DirectMessage, null, "Private", false, null, null, [recipientId]), default)).Result!).Value!).Id;

        var outsider = Build(factory, Guid.NewGuid());
        var result = await outsider.GetById(orgId, msgId, default);

        Assert.IsType<ForbidResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        var msg = await db.OrgMessages.FirstAsync(m => m.Id == msgId);
        Assert.Equal(0, msg.ViewCount); // the Forbid'd attempt must not have side-effected the count
    }

    [Fact]
    public async Task GetById_PublicFeedMessage_AnyoneCanView()
    {
        var (factory, orgId, senderId, _) = await SeedAsync();
        var sender = Build(factory, senderId);
        var msgId = ((OrgMessageRecord)((CreatedAtActionResult)(await sender.Send(orgId, new SendOrgMessageRequest(OrgMessageChannel.PublicFeed, null, "Public post", false, null, null, []), default)).Result!).Value!).Id;

        var outsider = Build(factory, Guid.NewGuid());
        var result = await outsider.GetById(orgId, msgId, default);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    // ── Belonging to the group ───────────────────────────────────────────────

    /// <summary>
    /// A stranger cannot read a group's message board.
    /// </summary>
    /// <remarks>
    /// Found by the write-endpoint audit of 2026-08-26: this controller carried
    /// <c>[Authorize]</c> and nothing else. The organization id came from the route and the user
    /// from the token, and nothing in between asked whether the two were related — so any signed-in
    /// person could read a group's board, and post to it, by knowing its id.
    /// </remarks>
    [Fact]
    public async Task GetInbox_AStrangerToTheOrg_IsRefused()
    {
        var (factory, orgId, _, _) = await SeedAsync();

        var result = await Build(factory, Guid.NewGuid()).GetInbox(orgId, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    /// <summary>And cannot post into it — the half that writes to somebody else's records.</summary>
    [Fact]
    public async Task Send_AStrangerToTheOrg_IsRefusedAndWritesNothing()
    {
        var (factory, orgId, _, recipientId) = await SeedAsync();

        var result = await Build(factory, Guid.NewGuid()).Send(
            orgId,
            new SendOrgMessageRequest(
                OrgMessageChannel.DirectMessage, null, "I do not belong here",
                false, null, null, [recipientId]),
            default);

        Assert.IsType<ForbidResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(db.OrgMessages.Where(m => m.Body == "I do not belong here"));
    }

    /// <summary>A member still gets through — the gate refuses strangers, not everybody.</summary>
    [Fact]
    public async Task GetInbox_AMember_IsAllowed()
    {
        var (factory, orgId, _, recipientId) = await SeedAsync();

        var result = await Build(factory, recipientId).GetInbox(orgId, default);

        Assert.IsNotType<ForbidResult>(result.Result);
    }
}
