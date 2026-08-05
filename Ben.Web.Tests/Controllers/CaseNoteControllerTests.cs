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
/// Tests for CaseNoteController — internal org notes on a case.
/// </summary>
public class CaseNoteControllerTests
{
    // Non-pooled: Create uses Reference().LoadAsync which can trigger include-filter issue
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
        m.Setup(x => x.Map<CaseNoteRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is CaseNote n
                ? new CaseNoteRecord { Id = n.Id, CaseId = n.CaseId, AuthorAppUserId = n.AuthorAppUserId, Title = n.Title, Body = n.Body, IsPinned = n.IsPinned, DateCreated = n.DateCreated }
                : new CaseNoteRecord { Body = "", DateCreated = DateTime.UtcNow });
        m.Setup(x => x.Map<IEnumerable<CaseNoteRecord>>(It.IsAny<object>()))
            .Returns<object>(o => o is IEnumerable<CaseNote> list
                ? list.Select(n => new CaseNoteRecord { Id = n.Id, CaseId = n.CaseId, AuthorAppUserId = n.AuthorAppUserId, Title = n.Title, Body = n.Body, IsPinned = n.IsPinned, DateCreated = n.DateCreated })
                : []);
        return m.Object;
    }

    private static CaseNoteController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new CaseNoteController(factory, CreateMapper());
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

    private static async Task<(IDbContextFactory<BenDataContext>, Guid orgId, Guid caseId, Guid userId)> SeedAsync()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var caseId  = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = userId, UserName = "u@t.com", NormalizedUserName = "U@T.COM", Email = "u@t.com", NormalizedEmail = "U@T.COM", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = orgId, Name = "Test Org", UrlName = "test", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = OrganizationMemberRole.Manager, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, Title = "Test Case",
            CaseYear = 2026, OrgCaseNumber = 1,
            StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return (factory, orgId, caseId, userId);
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_NonMember_ReturnsForbid()
    {
        var (factory, orgId, caseId, _) = await SeedAsync();
        Assert.IsType<ForbidResult>((await Build(factory, Guid.NewGuid()).GetAll(orgId, caseId, default)).Result);
    }

    [Fact]
    public async Task GetAll_Member_ReturnsEmptyList()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ok   = Assert.IsType<OkObjectResult>((await Build(factory, userId).GetAll(orgId, caseId, default)).Result);
        Assert.Empty((IEnumerable<CaseNoteRecord>)ok.Value!);
    }

    [Fact]
    public async Task GetAll_PinnedNotesFirst()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        await ctrl.Create(orgId, caseId, new UpsertCaseNoteRequest(null, "Unpinned note", false), default);
        await ctrl.Create(orgId, caseId, new UpsertCaseNoteRequest("Pinned", "Pinned content", true), default);

        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetAll(orgId, caseId, default)).Result);
        var list = ((IEnumerable<CaseNoteRecord>)ok.Value!).ToList();
        Assert.Equal(2, list.Count);
        Assert.True(list[0].IsPinned);
        Assert.False(list[1].IsPinned);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidNote_ReturnsCreated()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var result = await Build(factory, userId).Create(orgId, caseId,
            new UpsertCaseNoteRequest("Background", "Client has prior history.", false), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<CaseNoteRecord>(created.Value);
        Assert.Equal("Background", dto.Title);
        Assert.Equal("Client has prior history.", dto.Body);
        Assert.False(dto.IsPinned);
        Assert.Equal(userId, dto.AuthorAppUserId);
    }

    [Fact]
    public async Task Create_Pinned_SetsPinnedFlag()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var result = await Build(factory, userId).Create(orgId, caseId,
            new UpsertCaseNoteRequest("Important", "Must read.", true), default);
        var dto = (CaseNoteRecord)((CreatedAtActionResult)result.Result!).Value!;
        Assert.True(dto.IsPinned);
    }

    [Fact]
    public async Task Create_EmptyBody_ReturnsBadRequest()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        Assert.IsType<BadRequestObjectResult>((await Build(factory, userId).Create(orgId, caseId,
            new UpsertCaseNoteRequest(null, "   ", false), default)).Result);
    }

    [Fact]
    public async Task Create_CaseNotFound_ReturnsNotFound()
    {
        var (factory, orgId, _, userId) = await SeedAsync();
        Assert.IsType<NotFoundObjectResult>((await Build(factory, userId).Create(orgId, Guid.NewGuid(),
            new UpsertCaseNoteRequest(null, "Body", false), default)).Result);
    }

    [Fact]
    public async Task Create_NonMember_ReturnsForbid()
    {
        var (factory, orgId, caseId, _) = await SeedAsync();
        Assert.IsType<ForbidResult>((await Build(factory, Guid.NewGuid()).Create(orgId, caseId,
            new UpsertCaseNoteRequest(null, "Body", false), default)).Result);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_Author_UpdatesNote()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl   = Build(factory, userId);
        var noteId = ((CaseNoteRecord)((CreatedAtActionResult)(await ctrl.Create(orgId, caseId,
            new UpsertCaseNoteRequest("Old", "Old body", false), default)).Result!).Value!).Id;

        var result = await ctrl.Update(orgId, caseId, noteId,
            new UpsertCaseNoteRequest("Updated", "New body", true), default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<CaseNoteRecord>(ok.Value);
        Assert.Equal("Updated", dto.Title);
        Assert.Equal("New body", dto.Body);
        Assert.True(dto.IsPinned);
    }

    [Fact]
    public async Task Update_NonAuthorNonAdmin_ReturnsForbid()
    {
        var (factory, orgId, caseId, authorId) = await SeedAsync();
        var otherUserId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = otherUserId, UserName = "o@t.com", NormalizedUserName = "O@T.COM", Email = "o@t.com", NormalizedEmail = "O@T.COM", DateCreated = DateTime.UtcNow });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = otherUserId, Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = authorId });
        await db.SaveChangesAsync();

        var author = Build(factory, authorId);
        var noteId = ((CaseNoteRecord)((CreatedAtActionResult)(await author.Create(orgId, caseId,
            new UpsertCaseNoteRequest(null, "Original", false), default)).Result!).Value!).Id;

        var other = Build(factory, otherUserId);
        Assert.IsType<ForbidResult>((await other.Update(orgId, caseId, noteId,
            new UpsertCaseNoteRequest(null, "Hacked", false), default)).Result);
    }

    [Fact]
    public async Task Update_MissingNote_ReturnsNotFound()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        Assert.IsType<NotFoundResult>((await Build(factory, userId).Update(orgId, caseId, Guid.NewGuid(),
            new UpsertCaseNoteRequest(null, "Body", false), default)).Result);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_Author_DeletesNote()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl   = Build(factory, userId);
        var noteId = ((CaseNoteRecord)((CreatedAtActionResult)(await ctrl.Create(orgId, caseId,
            new UpsertCaseNoteRequest(null, "To delete", false), default)).Result!).Value!).Id;

        Assert.IsType<NoContentResult>(await ctrl.Delete(orgId, caseId, noteId, default));
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.CaseNotes.AnyAsync(n => n.Id == noteId));
    }

    [Fact]
    public async Task Delete_MissingNote_ReturnsNotFound()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        Assert.IsType<NotFoundResult>(await Build(factory, userId).Delete(orgId, caseId, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Delete_NonAuthorNonAdmin_ReturnsForbid()
    {
        var (factory, orgId, caseId, authorId) = await SeedAsync();
        var otherUserId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = otherUserId, UserName = "o@t.com", NormalizedUserName = "O@T.COM", Email = "o@t.com", NormalizedEmail = "O@T.COM", DateCreated = DateTime.UtcNow });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = otherUserId, Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = authorId });
        await db.SaveChangesAsync();

        var author = Build(factory, authorId);
        var noteId = ((CaseNoteRecord)((CreatedAtActionResult)(await author.Create(orgId, caseId,
            new UpsertCaseNoteRequest(null, "Original", false), default)).Result!).Value!).Id;

        Assert.IsType<ForbidResult>(await Build(factory, otherUserId).Delete(orgId, caseId, noteId, default));
    }
}
