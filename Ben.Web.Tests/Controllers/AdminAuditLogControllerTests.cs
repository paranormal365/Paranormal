using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for <see cref="AdminAuditLogController"/> — verifies filtering, paging,
/// distinct entity types, and the send-message flow.
/// </summary>
public class AdminAuditLogControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static AdminAuditLogController Build(IDbContextFactory<BenDataContext> factory, Guid? userId = null)
    {
        var id = userId ?? Guid.NewGuid();
        var ctrl = new AdminAuditLogController(factory);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, id.ToString()),
                     new Claim(ClaimTypes.Role, "SuperAdmin")], "Bearer"))
            }
        };
        return ctrl;
    }

    private static AuditLog MakeLog(string entityType, AuditAction action, DateTime? at = null) => new()
    {
        Id         = Guid.NewGuid(),
        UserId     = Guid.NewGuid(),
        Action     = action,
        EntityType = entityType,
        EntityId   = Guid.NewGuid(),
        Source     = "WebApi",
        OccurredAt = at ?? DateTime.UtcNow,
        ChangesJson = "{}"
    };

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_WhenEmpty_ReturnsTotalCountZero()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory);

        var result  = await ctrl.GetAll(ct: CancellationToken.None);
        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var paged   = Assert.IsType<AuditLogPagedResponse>(ok.Value);

        Assert.Equal(0, paged.TotalCount);
        Assert.Empty(paged.Items);
    }

    [Fact]
    public async Task GetAll_ReturnsAllRecordsOrderedByOccurredAtDescending()
    {
        var factory = CreateFactory();
        var now     = DateTime.UtcNow;

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AuditLogs.AddRange(
                MakeLog("Organization", AuditAction.Create, now.AddMinutes(-5)),
                MakeLog("AppUser",      AuditAction.Update, now.AddMinutes(-2)),
                MakeLog("UploadFile",   AuditAction.Delete, now.AddMinutes(-1)));
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory);
        var result = await ctrl.GetAll(ct: CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var paged  = Assert.IsType<AuditLogPagedResponse>(ok.Value);

        Assert.Equal(3, paged.TotalCount);
        Assert.Equal(3, paged.Items.Count);
        // Most recent first
        Assert.Equal("UploadFile", paged.Items[0].EntityType);
    }

    [Fact]
    public async Task GetAll_EntityTypeFilter_ReturnsOnlyMatchingType()
    {
        var factory = CreateFactory();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AuditLogs.AddRange(
                MakeLog("Organization", AuditAction.Create),
                MakeLog("Organization", AuditAction.Update),
                MakeLog("AppUser",      AuditAction.Create));
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory);
        var result = await ctrl.GetAll(entityType: "Organization", ct: CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var paged  = Assert.IsType<AuditLogPagedResponse>(ok.Value);

        Assert.Equal(2, paged.TotalCount);
        Assert.All(paged.Items, r => Assert.Equal("Organization", r.EntityType));
    }

    [Fact]
    public async Task GetAll_ActionFilter_ReturnsOnlyMatchingAction()
    {
        var factory = CreateFactory();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AuditLogs.AddRange(
                MakeLog("Org", AuditAction.Create),
                MakeLog("Org", AuditAction.Create),
                MakeLog("Org", AuditAction.Delete));
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory);
        var result = await ctrl.GetAll(action: (int)AuditAction.Create, ct: CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var paged  = Assert.IsType<AuditLogPagedResponse>(ok.Value);

        Assert.Equal(2, paged.TotalCount);
        Assert.All(paged.Items, r => Assert.Equal(AuditAction.Create, r.Action));
    }

    [Fact]
    public async Task GetAll_UserIdFilter_ReturnsOnlyThatUsersLogs()
    {
        var factory    = CreateFactory();
        var targetUser = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var log1 = MakeLog("Org", AuditAction.Create);
            var log2 = MakeLog("Org", AuditAction.Update);
            log1.UserId = targetUser;
            db.AuditLogs.AddRange(log1, log2);
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory);
        var result = await ctrl.GetAll(userId: targetUser, ct: CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var paged  = Assert.IsType<AuditLogPagedResponse>(ok.Value);

        Assert.Equal(1, paged.TotalCount);
        Assert.Equal(targetUser, paged.Items[0].UserId);
    }

    [Fact]
    public async Task GetAll_DateRangeFilter_ReturnsOnlyLogsInRange()
    {
        var factory = CreateFactory();
        var now     = DateTime.UtcNow;

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AuditLogs.AddRange(
                MakeLog("Org", AuditAction.Create, now.AddDays(-10)), // outside
                MakeLog("Org", AuditAction.Create, now.AddDays(-3)),  // inside
                MakeLog("Org", AuditAction.Create, now.AddDays(-1))); // inside
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory);
        var result = await ctrl.GetAll(dateFrom: now.AddDays(-5), ct: CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var paged  = Assert.IsType<AuditLogPagedResponse>(ok.Value);

        Assert.Equal(2, paged.TotalCount);
    }

    [Fact]
    public async Task GetAll_Pagination_RespectsPageSizeAndPage()
    {
        var factory = CreateFactory();

        await using (var db = await factory.CreateDbContextAsync())
        {
            for (var i = 0; i < 10; i++)
                db.AuditLogs.Add(MakeLog("Org", AuditAction.Create,
                    DateTime.UtcNow.AddSeconds(-i)));
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory);
        var result = await ctrl.GetAll(page: 2, pageSize: 3, ct: CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var paged  = Assert.IsType<AuditLogPagedResponse>(ok.Value);

        Assert.Equal(10, paged.TotalCount); // total unchanged
        Assert.Equal(3,  paged.Items.Count); // page size respected
    }

    [Fact]
    public async Task GetAll_InvalidAction_IsIgnored()
    {
        var factory = CreateFactory();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AuditLogs.Add(MakeLog("Org", AuditAction.Create));
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory);
        var result = await ctrl.GetAll(action: 999, ct: CancellationToken.None); // 999 not in enum
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var paged  = Assert.IsType<AuditLogPagedResponse>(ok.Value);

        // Invalid action value → filter not applied → all records returned
        Assert.Equal(1, paged.TotalCount);
    }

    // ── GetEntityTypes ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetEntityTypes_ReturnsDistinctTypesSorted()
    {
        var factory = CreateFactory();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AuditLogs.AddRange(
                MakeLog("Organization", AuditAction.Create),
                MakeLog("Organization", AuditAction.Update),
                MakeLog("AppUser",      AuditAction.Create),
                MakeLog("UploadFile",   AuditAction.Delete));
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory);
        var result = await ctrl.GetEntityTypes(CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var types  = Assert.IsAssignableFrom<IReadOnlyList<string>>(ok.Value);

        Assert.Equal(3, types.Count);
        Assert.Equal(["AppUser", "Organization", "UploadFile"], types); // alphabetical
    }

    [Fact]
    public async Task GetEntityTypes_WhenEmpty_ReturnsEmptyList()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory);

        var result = await ctrl.GetEntityTypes(CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var types  = Assert.IsAssignableFrom<IReadOnlyList<string>>(ok.Value);

        Assert.Empty(types);
    }

    // ── SendMessage ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SendMessage_ValidRequest_CreatesMessageAndRecipients()
    {
        var factory     = CreateFactory();
        var senderId    = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var auditLogId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = senderId,    UserName = "sender",    Email = "s@test.com" });
            db.AppUsers.Add(new AppUser { Id = recipientId, UserName = "recipient", Email = "r@test.com" });
            db.AuditLogs.Add(new AuditLog
            {
                Id = auditLogId, UserId = senderId, Action = AuditAction.Create,
                EntityType = "Organization", EntityId = Guid.NewGuid(),
                Source = "WebApi", OccurredAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, senderId);
        var result = await ctrl.SendMessage(
            new SendAuditLogMessageRequest(auditLogId, [recipientId], "Test Subject", "Test Body"),
            CancellationToken.None);

        Assert.IsType<OkResult>(result);

        await using var verify = await factory.CreateDbContextAsync();
        var message = await verify.UserMessages.FirstOrDefaultAsync();
        Assert.NotNull(message);
        Assert.Equal("Test Subject", message!.MessageSubject);
        Assert.Equal("Test Body",    message.MessageBody);

        var to = await verify.UserMessageTos.FirstOrDefaultAsync();
        Assert.NotNull(to);
        Assert.Equal(recipientId, to!.ToAppUserId);
    }

    [Fact]
    public async Task SendMessage_EmptyRecipients_ReturnsBadRequest()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory);

        var result = await ctrl.SendMessage(
            new SendAuditLogMessageRequest(Guid.NewGuid(), [], "Subject", "Body"),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SendMessage_CreatesSystemNotificationTypeIfMissing()
    {
        var factory     = CreateFactory();
        var senderId    = Guid.NewGuid();
        var recipientId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = senderId,    UserName = "s", Email = "s@test.com" });
            db.AppUsers.Add(new AppUser { Id = recipientId, UserName = "r", Email = "r@test.com" });
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory, senderId);
        await ctrl.SendMessage(
            new SendAuditLogMessageRequest(Guid.NewGuid(), [recipientId], "Subj", "Body"),
            CancellationToken.None);

        await using var verify = await factory.CreateDbContextAsync();
        var msgType = await verify.UserMessageTypes.FirstOrDefaultAsync(t => t.Name == "System Notification");
        Assert.NotNull(msgType);
        Assert.True(msgType!.IsActive);
    }

    [Fact]
    public async Task SendMessage_ReusesExistingSystemNotificationType()
    {
        var factory     = CreateFactory();
        var senderId    = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var existingTypeId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = senderId,    UserName = "s", Email = "s@test.com" });
            db.AppUsers.Add(new AppUser { Id = recipientId, UserName = "r", Email = "r@test.com" });
            db.UserMessageTypes.Add(new UserMessageType
            {
                Id = existingTypeId, Name = "System Notification",
                IsActive = true, IsPublic = false, SortOrder = 999,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = senderId
            });
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory, senderId);
        await ctrl.SendMessage(
            new SendAuditLogMessageRequest(Guid.NewGuid(), [recipientId], "Subj", "Body"),
            CancellationToken.None);

        await using var verify = await factory.CreateDbContextAsync();
        // Should still be exactly one System Notification type
        var count = await verify.UserMessageTypes.CountAsync(t => t.Name == "System Notification");
        Assert.Equal(1, count);

        var message = await verify.UserMessages.FirstAsync();
        Assert.Equal(existingTypeId, message.UserMessageTypeId);
    }

    [Fact]
    public async Task SendMessage_SkipsNonExistentRecipients()
    {
        var factory     = CreateFactory();
        var senderId    = Guid.NewGuid();
        var realId      = Guid.NewGuid();
        var ghostId     = Guid.NewGuid(); // not in DB

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = senderId, UserName = "s", Email = "s@test.com" });
            db.AppUsers.Add(new AppUser { Id = realId,   UserName = "r", Email = "r@test.com" });
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory, senderId);
        var result = await ctrl.SendMessage(
            new SendAuditLogMessageRequest(Guid.NewGuid(), [realId, ghostId], "S", "B"),
            CancellationToken.None);

        Assert.IsType<OkResult>(result);

        await using var verify = await factory.CreateDbContextAsync();
        // Only one recipient row — the ghost was skipped
        var tos = await verify.UserMessageTos.ToListAsync();
        Assert.Single(tos);
        Assert.Equal(realId, tos[0].ToAppUserId);
    }

    [Fact]
    public async Task SendMessage_DeduplicatesRecipientIds()
    {
        var factory     = CreateFactory();
        var senderId    = Guid.NewGuid();
        var recipientId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = senderId,    UserName = "s", Email = "s@test.com" });
            db.AppUsers.Add(new AppUser { Id = recipientId, UserName = "r", Email = "r@test.com" });
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory, senderId);
        await ctrl.SendMessage(
            new SendAuditLogMessageRequest(Guid.NewGuid(), [recipientId, recipientId], "S", "B"),
            CancellationToken.None);

        await using var verify = await factory.CreateDbContextAsync();
        var tos = await verify.UserMessageTos.ToListAsync();
        Assert.Single(tos); // deduplicated
    }
}
