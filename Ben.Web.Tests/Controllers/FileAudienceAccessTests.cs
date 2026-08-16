using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;
using Ben.Data.WebApi.Services.Access;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for <see cref="FileAudienceAccess"/> — the shared audience-membership and
/// file-visibility logic used by both UploadFileCommentController and CaseFileController.Link.
/// </summary>
public class FileAudienceAccessTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static UploadFile MakeFile(Guid ownerId, bool isPublic = false) => new()
    {
        Id = Guid.NewGuid(), UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
        FileName = "f.jpg", StoredFileName = "f-stored.jpg", ContentType = "image/jpeg",
        FileSize = 1, IsPublic = isPublic,
        DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
    };

    // ── GetMembershipAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetMembershipAsync_Owner_IsOwnerTrue()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var file = MakeFile(ownerId);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(file);
            await db.SaveChangesAsync();
        }

        await using var readDb = await factory.CreateDbContextAsync();
        var membership = await FileAudienceAccess.GetMembershipAsync(readDb, file.Id, ownerId, default);

        Assert.True(membership.IsOwner);
        Assert.False(membership.IsInvestigationTeamMember);
    }

    [Fact]
    public async Task GetMembershipAsync_InvestigationTeamShare_AttendeeMatches()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var attendeeId = Guid.NewGuid();
        var investigationId = Guid.NewGuid();
        var file = MakeFile(ownerId);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(file);
            db.InvestigationAttendees.Add(new InvestigationAttendee
            { Id = Guid.NewGuid(), InvestigationId = investigationId, AppUserId = attendeeId });
            db.UploadFileShares.Add(new UploadFileShare
            {
                Id = Guid.NewGuid(), UploadFileId = file.Id, TargetType = ShareTargetType.InvestigationTeam,
                TargetInvestigationId = investigationId, SharedByAppUserId = ownerId, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        await using var readDb = await factory.CreateDbContextAsync();
        var membership = await FileAudienceAccess.GetMembershipAsync(readDb, file.Id, attendeeId, default);

        Assert.True(membership.IsInvestigationTeamMember);
    }

    [Fact]
    public async Task GetMembershipAsync_InvestigationTeamShare_NonAttendeeDoesNotMatch()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var investigationId = Guid.NewGuid();
        var file = MakeFile(ownerId);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(file);
            db.UploadFileShares.Add(new UploadFileShare
            {
                Id = Guid.NewGuid(), UploadFileId = file.Id, TargetType = ShareTargetType.InvestigationTeam,
                TargetInvestigationId = investigationId, SharedByAppUserId = ownerId, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        await using var readDb = await factory.CreateDbContextAsync();
        var membership = await FileAudienceAccess.GetMembershipAsync(readDb, file.Id, Guid.NewGuid(), default);

        Assert.False(membership.IsInvestigationTeamMember);
    }

    [Fact]
    public async Task GetMembershipAsync_ClientViaCaseFile_Matches()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var clientRequestId = Guid.NewGuid();
        var file = MakeFile(ownerId);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(file);
            db.Organizations.Add(new Organization { Id = orgId, Name = "Org", UrlName = "org", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            db.ClientRequests.Add(new ClientRequest
            {
                Id = clientRequestId, AppUserId = clientId, StreetAddress1 = "1 Main", City = "N", State = "TN",
                ZipCode = "37201", Country = "US", DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
            });
            db.Cases.Add(new Case
            {
                Id = caseId, OrganizationId = orgId, ClientRequestId = clientRequestId, Title = "Case",
                CaseYear = 2026, OrgCaseNumber = 1, StreetAddress1 = "1 Main", City = "N", State = "TN",
                ZipCode = "37201", Country = "US", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.CaseFiles.Add(new CaseFile
            {
                Id = Guid.NewGuid(), CaseId = caseId, UploadFileId = file.Id,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        await using var readDb = await factory.CreateDbContextAsync();
        var membership = await FileAudienceAccess.GetMembershipAsync(readDb, file.Id, clientId, default);

        Assert.True(membership.IsClient);
    }

    [Fact]
    public async Task GetMembershipAsync_OrganizationShare_ActiveMemberMatches()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var file = MakeFile(ownerId);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(file);
            db.Organizations.Add(new Organization { Id = orgId, Name = "Org", UrlName = "org", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = memberId,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.UploadFileShares.Add(new UploadFileShare
            {
                Id = Guid.NewGuid(), UploadFileId = file.Id, TargetType = ShareTargetType.Organization,
                TargetOrganizationId = orgId, SharedByAppUserId = ownerId, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        await using var readDb = await factory.CreateDbContextAsync();
        var membership = await FileAudienceAccess.GetMembershipAsync(readDb, file.Id, memberId, default);

        Assert.True(membership.IsOrganizationMember);
    }

    [Fact]
    public async Task GetMembershipAsync_OrgAdminsOnlyTieredShare_RegularMemberDoesNotMatch()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var file = MakeFile(ownerId);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(file);
            db.Organizations.Add(new Organization { Id = orgId, Name = "Org", UrlName = "org", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = memberId,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.UploadFileOrganizationShares.Add(new UploadFileOrganizationShare
            {
                Id = Guid.NewGuid(), UploadFileId = file.Id, OrganizationId = orgId,
                Visibility = FileShareVisibility.OrgAdminsOnly, SharedByAppUserId = ownerId, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        await using var readDb = await factory.CreateDbContextAsync();
        var membership = await FileAudienceAccess.GetMembershipAsync(readDb, file.Id, memberId, default);

        Assert.False(membership.IsOrganizationMember);
    }

    [Fact]
    public async Task GetMembershipAsync_PublicFile_IsPublicCommenterTrue()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var file = MakeFile(ownerId, isPublic: true);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(file);
            await db.SaveChangesAsync();
        }

        await using var readDb = await factory.CreateDbContextAsync();
        var membership = await FileAudienceAccess.GetMembershipAsync(readDb, file.Id, Guid.NewGuid(), default);

        Assert.True(membership.IsPublicCommenter);
    }

    // ── CanViewFileAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task CanViewFileAsync_Owner_True()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var file = MakeFile(ownerId);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(file);
            await db.SaveChangesAsync();
        }

        await using var readDb = await factory.CreateDbContextAsync();
        Assert.True(await FileAudienceAccess.CanViewFileAsync(readDb, file.Id, ownerId, default));
    }

    [Fact]
    public async Task CanViewFileAsync_NoAccessAtAll_False()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var file = MakeFile(ownerId);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(file);
            await db.SaveChangesAsync();
        }

        await using var readDb = await factory.CreateDbContextAsync();
        Assert.False(await FileAudienceAccess.CanViewFileAsync(readDb, file.Id, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task CanViewFileAsync_DirectPersonShare_True()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var file = MakeFile(ownerId);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(file);
            db.UploadFileShares.Add(new UploadFileShare
            {
                Id = Guid.NewGuid(), UploadFileId = file.Id, TargetType = ShareTargetType.Person,
                TargetAppUserId = targetUserId, SharedByAppUserId = ownerId, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        await using var readDb = await factory.CreateDbContextAsync();
        Assert.True(await FileAudienceAccess.CanViewFileAsync(readDb, file.Id, targetUserId, default));
    }
}
