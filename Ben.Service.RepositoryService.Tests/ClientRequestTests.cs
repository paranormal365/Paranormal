using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>
/// Tests for ClientRequest, ClientRequestOrganization, and ClientRequestFile
/// entities: DB persistence, status transitions, cascade deletes,
/// org application uniqueness, and field constraints.
/// </summary>
public class ClientRequestTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory() => TestDbFactory.Create();

    private static async Task<(AppUser user, Organization org)> SeedAsync(BenDataContext db)
    {
        var user = new AppUser
        {
            Id          = Guid.NewGuid(),
            UserName    = "client@example.com",
            Email       = "client@example.com",
            DisplayName = "Test Client",
            DateCreated = DateTime.UtcNow,
        };
        db.AppUsers.Add(user);

        var org = new Organization
        {
            Id                 = Guid.NewGuid(),
            Name               = "Ghost Hunters TN",
            UrlName            = "ghost-hunters-tn",
            IsAcceptingClients = true,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = user.Id,
        };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        return (user, org);
    }

    private static ClientRequest MakeRequest(Guid userId, ClientRequestStatus status = ClientRequestStatus.Draft)
        => new ClientRequest
        {
            Id                 = Guid.NewGuid(),
            AppUserId          = userId,
            Status             = status,
            StreetAddress1     = "123 Haunted Lane",
            City               = "Nashville",
            State              = "TN",
            ZipCode            = "37201",
            Country            = "US",
            Latitude           = 36.1627m,
            Longitude          = -86.7816m,
            Gender             = ClientGender.Male,
            BirthYear          = 1985,
            Description        = "<p>Strange noises at night.</p>",
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };

    // ── ClientRequest persistence ─────────────────────────────────────────────

    [Fact]
    public async Task ClientRequest_CanBeSavedAndRetrieved()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, _) = await SeedAsync(db);

        var req = MakeRequest(user.Id);
        db.ClientRequests.Add(req);
        await db.SaveChangesAsync();

        var loaded = await db.ClientRequests.AsNoTracking().FirstAsync(r => r.Id == req.Id);
        Assert.Equal("Nashville", loaded.City);
        Assert.Equal("TN", loaded.State);
        Assert.Equal(36.1627m, loaded.Latitude);
        Assert.Equal(-86.7816m, loaded.Longitude);
        Assert.Equal(ClientRequestStatus.Draft, loaded.Status);
        Assert.Equal(ClientGender.Male, loaded.Gender);
        Assert.Equal(1985, loaded.BirthYear);
        Assert.Contains("Strange noises", loaded.Description);
    }

    [Fact]
    public async Task ClientRequest_DefaultStatus_IsDraft()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, _) = await SeedAsync(db);

        var req = new ClientRequest
        {
            Id = Guid.NewGuid(), AppUserId = user.Id,
            StreetAddress1 = "1 Main St", City = "City", State = "ST", ZipCode = "00000", Country = "US",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.ClientRequests.Add(req);
        await db.SaveChangesAsync();

        var loaded = await db.ClientRequests.AsNoTracking().FirstAsync(r => r.Id == req.Id);
        Assert.Equal(ClientRequestStatus.Draft, loaded.Status);
    }

    [Fact]
    public async Task ClientRequest_BirthYear_CanBeNull()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, _) = await SeedAsync(db);

        var req = MakeRequest(user.Id);
        req.BirthYear = null;
        db.ClientRequests.Add(req);
        await db.SaveChangesAsync();

        var loaded = await db.ClientRequests.AsNoTracking().FirstAsync(r => r.Id == req.Id);
        Assert.Null(loaded.BirthYear);
    }

    [Fact]
    public async Task ClientRequest_StatusProgression_CanBeUpdated()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, _) = await SeedAsync(db);

        var req = MakeRequest(user.Id, ClientRequestStatus.Draft);
        db.ClientRequests.Add(req);
        await db.SaveChangesAsync();

        req.Status = ClientRequestStatus.Submitted;
        req.DateUpdated = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var loaded = await db.ClientRequests.AsNoTracking().FirstAsync(r => r.Id == req.Id);
        Assert.Equal(ClientRequestStatus.Submitted, loaded.Status);
    }

    [Fact]
    public async Task ClientRequest_AllGenderValuesStoredCorrectly()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, _) = await SeedAsync(db);

        foreach (var gender in Enum.GetValues<ClientGender>())
        {
            var req = MakeRequest(user.Id);
            req.Gender = gender;
            db.ClientRequests.Add(req);
        }
        await db.SaveChangesAsync();

        foreach (var gender in Enum.GetValues<ClientGender>())
        {
            var exists = await db.ClientRequests.AsNoTracking()
                .AnyAsync(r => r.AppUserId == user.Id && r.Gender == gender);
            Assert.True(exists, $"Gender {gender} was not found");
        }
    }

    // ── ClientRequestOrganization ─────────────────────────────────────────────

    [Fact]
    public async Task ClientRequestOrganization_CanBeSavedAndLinkedToRequest()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var req = MakeRequest(user.Id, ClientRequestStatus.Submitted);
        db.ClientRequests.Add(req);
        await db.SaveChangesAsync();

        var application = new ClientRequestOrganization
        {
            Id                 = Guid.NewGuid(),
            ClientRequestId    = req.Id,
            OrganizationId     = org.Id,
            Status             = ClientOrgRequestStatus.Pending,
            DateApplied        = DateTime.UtcNow,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = user.Id,
        };
        db.ClientRequestOrganizations.Add(application);
        await db.SaveChangesAsync();

        var loaded = await db.ClientRequestOrganizations.AsNoTracking()
            .FirstAsync(a => a.Id == application.Id);
        Assert.Equal(req.Id, loaded.ClientRequestId);
        Assert.Equal(org.Id, loaded.OrganizationId);
        Assert.Equal(ClientOrgRequestStatus.Pending, loaded.Status);
    }

    [Fact]
    public async Task ClientRequestOrganization_CascadeDeletesWithRequest()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var req = MakeRequest(user.Id, ClientRequestStatus.Submitted);
        db.ClientRequests.Add(req);
        await db.SaveChangesAsync();

        db.ClientRequestOrganizations.Add(new ClientRequestOrganization
        {
            Id = Guid.NewGuid(), ClientRequestId = req.Id, OrganizationId = org.Id,
            Status = ClientOrgRequestStatus.Pending, DateApplied = DateTime.UtcNow,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        });
        await db.SaveChangesAsync();

        db.ClientRequests.Remove(req);
        await db.SaveChangesAsync();

        var remaining = await db.ClientRequestOrganizations.AsNoTracking()
            .Where(a => a.ClientRequestId == req.Id).ToListAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task ClientRequestOrganization_AllStatusValues_CanBeStored()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        foreach (var status in Enum.GetValues<ClientOrgRequestStatus>())
        {
            var req = MakeRequest(user.Id, ClientRequestStatus.Submitted);
            db.ClientRequests.Add(req);
            await db.SaveChangesAsync();

            db.ClientRequestOrganizations.Add(new ClientRequestOrganization
            {
                Id = Guid.NewGuid(), ClientRequestId = req.Id, OrganizationId = org.Id,
                Status = status, DateApplied = DateTime.UtcNow,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
            });
            await db.SaveChangesAsync();
        }

        foreach (var status in Enum.GetValues<ClientOrgRequestStatus>())
        {
            var exists = await db.ClientRequestOrganizations.AsNoTracking()
                .AnyAsync(a => a.Status == status);
            Assert.True(exists, $"Status {status} was not found");
        }
    }

    [Fact]
    public void ClientRequestOrganization_UniqueIndex_IsConfiguredOnModel()
    {
        // InMemory does not enforce unique indexes at runtime; verify the model config
        // so SQL Server enforces one application per request+org pair.
        using var db = new BenDataContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase("model-check-org-app")
                .Options);

        var entityType = db.Model.FindEntityType(typeof(ClientRequestOrganization));
        Assert.NotNull(entityType);

        var uniqueIndex = entityType!.GetIndexes()
            .FirstOrDefault(i => i.IsUnique &&
                i.Properties.Any(p => p.Name == nameof(ClientRequestOrganization.ClientRequestId)) &&
                i.Properties.Any(p => p.Name == nameof(ClientRequestOrganization.OrganizationId)));

        Assert.NotNull(uniqueIndex);
    }

    // ── ClientRequestFile ─────────────────────────────────────────────────────

    [Fact]
    public async Task ClientRequestFile_LinksUploadFileToRequest()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, _) = await SeedAsync(db);

        var req = MakeRequest(user.Id);
        db.ClientRequests.Add(req);

        // Create an UploadFileType first
        var fileType = new UploadFileType
        {
            Id = Guid.NewGuid(), Name = "Image", IsActive = true, IsPublic = true,
            AllowAllExtensions = true, SortOrder = 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.UploadFileTypes.Add(fileType);

        var upload = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = fileType.Id, AppUserId = user.Id,
            FileName = "evidence.jpg", StoredFileName = "abc.jpg", ContentType = "image/jpeg",
            FileSize = 100, IsPublic = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.UploadFiles.Add(upload);
        await db.SaveChangesAsync();

        db.ClientRequestFiles.Add(new ClientRequestFile
        {
            Id = Guid.NewGuid(), ClientRequestId = req.Id, UploadFileId = upload.Id,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        });
        await db.SaveChangesAsync();

        var files = await db.ClientRequestFiles.AsNoTracking()
            .Where(f => f.ClientRequestId == req.Id).ToListAsync();
        Assert.Single(files);
        Assert.Equal(upload.Id, files[0].UploadFileId);
    }

    [Fact]
    public async Task ClientRequestFile_CascadeDeletesWithRequest()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, _) = await SeedAsync(db);

        var req = MakeRequest(user.Id);
        db.ClientRequests.Add(req);

        var fileType = new UploadFileType
        {
            Id = Guid.NewGuid(), Name = "Doc", IsActive = true, IsPublic = true,
            AllowAllExtensions = true, SortOrder = 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.UploadFileTypes.Add(fileType);

        var upload = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = fileType.Id, AppUserId = user.Id,
            FileName = "doc.pdf", StoredFileName = "xyz.pdf", ContentType = "application/pdf",
            FileSize = 200, IsPublic = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.UploadFiles.Add(upload);
        await db.SaveChangesAsync();

        db.ClientRequestFiles.Add(new ClientRequestFile
        {
            Id = Guid.NewGuid(), ClientRequestId = req.Id, UploadFileId = upload.Id,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        });
        await db.SaveChangesAsync();

        db.ClientRequests.Remove(req);
        await db.SaveChangesAsync();

        var remaining = await db.ClientRequestFiles.AsNoTracking()
            .Where(f => f.ClientRequestId == req.Id).ToListAsync();
        Assert.Empty(remaining);
    }

    // ── ClientRequest loaded with nav properties ───────────────────────────────

    [Fact]
    public async Task ClientRequest_LoadsOrganizationApplicationsViaNavProperty()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedAsync(db);

        var req = MakeRequest(user.Id, ClientRequestStatus.Submitted);
        db.ClientRequests.Add(req);
        await db.SaveChangesAsync();

        db.ClientRequestOrganizations.Add(new ClientRequestOrganization
        {
            Id = Guid.NewGuid(), ClientRequestId = req.Id, OrganizationId = org.Id,
            Status = ClientOrgRequestStatus.Pending, DateApplied = DateTime.UtcNow,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        });
        await db.SaveChangesAsync();

        var loaded = await db.ClientRequests
            .AsNoTracking()
            .Include(r => r.OrganizationApplications)
            .FirstAsync(r => r.Id == req.Id);

        Assert.Single(loaded.OrganizationApplications);
        Assert.Equal(org.Id, loaded.OrganizationApplications.First().OrganizationId);
    }
}
