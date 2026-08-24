using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Item 182: a group that took a case on a plan without privacy protections, and has since
/// upgraded, can have them applied after the fact. The tests here are as much about what the
/// retrofit REFUSES to do as what it does — it never rewrites somebody's prose, and it never
/// pretends publication can be undone.
/// </summary>
public sealed class CasePrivacyRetrofitTests
{
    private static BenDataContext NewDb() =>
        new(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>Storage where every file already has its stripped copy, so file work is a no-op.</summary>
    private static Mock<IFileStorageService> StorageWithCleanCopies()
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.Exists(It.IsAny<string>())).Returns(true);
        return storage;
    }

    private static CasePrivacyRetrofit Retrofit(Mock<IFileStorageService>? storage = null)
        => new((storage ?? StorageWithCleanCopies()).Object,
               new MediaSanitizationService(),
               TestMedia.Stripper(),
               NullLogger<CasePrivacyRetrofit>.Instance);

    private static async Task<(BenDataContext Db, Guid OrgId, Guid CaseId, Guid UserId)> SeedAsync(
        bool isPublic = true, bool withClient = true, string title = "The Farmhouse")
    {
        var db = NewDb();
        Guid orgId = Guid.NewGuid(), caseId = Guid.NewGuid(), userId = Guid.NewGuid();

        db.Users.Add(new AppUser { Id = userId, UserName = "u@t", Email = "u@t", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Night Watch", UrlName = "nw",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });

        Guid? requestId = null;
        if (withClient)
        {
            var clientId = Guid.NewGuid();
            requestId = Guid.NewGuid();
            db.Users.Add(new AppUser
            {
                Id = clientId, UserName = "c@t", Email = "c@t",
                FirstName = "Daniel", LastName = "Park", DisplayName = "Daniel Park",
                DateCreated = DateTime.UtcNow,
            });
            db.ClientRequests.Add(new ClientRequest
            {
                Id = requestId.Value, AppUserId = clientId, Status = ClientRequestStatus.Assigned,
                StreetAddress1 = "1428 Elm Street", City = "Nashville", State = "TN", ZipCode = "37201",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
            });
        }

        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, Title = title, Status = CaseStatus.Active,
            ClientRequestId = requestId, IsPublic = isPublic,
            Latitude = 36.1043m, Longitude = -86.7930m,
            StreetAddress1 = "1428 Elm Street", City = "Nashville", State = "TN",
            ZipCode = "37201", Country = "US",
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return (db, orgId, caseId, userId);
    }

    // ── The mechanical parts ─────────────────────────────────────────────────

    [Fact]
    public async Task A_public_case_becomes_private_and_loses_its_exact_coordinates()
    {
        var (db, orgId, caseId, userId) = await SeedAsync(isPublic: true);

        var result = await Retrofit().ApplyAsync(db, orgId, caseId, userId, default);

        Assert.NotNull(result);
        Assert.True(result!.MadePrivate);
        Assert.True(result.LocationGeneralized);

        var row = await db.Cases.SingleAsync(c => c.Id == caseId);
        Assert.False(row.IsPublic);
        Assert.Null(row.Latitude);
        Assert.Null(row.Longitude);

        // The address itself stays — the group still needs to know where they are going.
        Assert.Equal("1428 Elm Street", row.StreetAddress1);
    }

    [Fact]
    public async Task An_already_private_case_reports_no_change_rather_than_claiming_one()
    {
        var (db, orgId, caseId, userId) = await SeedAsync(isPublic: false);
        var result = await Retrofit().ApplyAsync(db, orgId, caseId, userId, default);

        Assert.False(result!.MadePrivate);
        Assert.False(result.WasEverPublic);
    }

    [Fact]
    public async Task A_case_that_was_public_says_so_because_that_cannot_be_undone()
    {
        var (db, orgId, caseId, userId) = await SeedAsync(isPublic: true);
        var result = await Retrofit().ApplyAsync(db, orgId, caseId, userId, default);

        // The honest half of the report: a group must not be left believing that making a case
        // private now recalls what visitors, search engines and scrapers already took.
        Assert.True(result!.WasEverPublic);
    }

    [Fact]
    public async Task A_case_in_another_organization_is_not_found()
    {
        var (db, _, caseId, userId) = await SeedAsync();
        Assert.Null(await Retrofit().ApplyAsync(db, Guid.NewGuid(), caseId, userId, default));
    }

    // ── Files ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Files_that_already_have_a_stripped_copy_are_counted_not_rebuilt()
    {
        var (db, orgId, caseId, userId) = await SeedAsync();
        var fileId = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, AppUserId = userId, UploadFileTypeId = Guid.NewGuid(),
            FileName = "a.jpg", StoredFileName = "a.jpg", ContentType = "image/jpeg",
            FileSize = 10, StoragePath = "cases/x/a.jpg",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.CaseFiles.Add(new CaseFile
        {
            Id = Guid.NewGuid(), CaseId = caseId, UploadFileId = fileId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();

        var storage = StorageWithCleanCopies();   // says every derivative exists
        var result = await Retrofit(storage).ApplyAsync(db, orgId, caseId, userId, default);

        Assert.Equal(1, result!.FilesAlreadyClean);
        Assert.Equal(0, result.FilesStripped);
        storage.Verify(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never, "a file that already had a stripped copy must not be rebuilt");
    }

    [Fact]
    public async Task A_file_with_no_stripped_copy_gets_one_built_from_the_kept_original()
    {
        var (db, orgId, caseId, userId) = await SeedAsync();
        var fileId = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, AppUserId = userId, UploadFileTypeId = Guid.NewGuid(),
            FileName = "b.jpg", StoredFileName = "b.jpg", ContentType = "image/jpeg",
            FileSize = 10, StoragePath = "cases/x/b.jpg",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.CaseFiles.Add(new CaseFile
        {
            Id = Guid.NewGuid(), CaseId = caseId, UploadFileId = fileId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();

        // Nothing exists yet, and the original is a real decodable JPEG — the whole reason the
        // original is kept untouched is so this is possible later.
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.Exists(It.IsAny<string>())).Returns(false);
        storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(() => new MemoryStream(RealJpeg()));

        var result = await Retrofit(storage).ApplyAsync(db, orgId, caseId, userId, default);

        Assert.Equal(1, result!.FilesStripped);
        storage.Verify(s => s.WriteAsync(
            It.Is<string>(p => p.EndsWith(".clean.jpg", StringComparison.Ordinal)),
            It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Prose: found, never rewritten ────────────────────────────────────────

    [Fact]
    public async Task The_clients_name_in_prose_is_reported_and_left_exactly_as_written()
    {
        var (db, orgId, caseId, userId) = await SeedAsync(title: "Park, Nashville TN");
        var entryId = Guid.NewGuid();
        db.CaseTimelineEntries.Add(new CaseTimelineEntry
        {
            Id = entryId, CaseId = caseId, AuthorAppUserId = userId,
            EntryType = CaseTimelineEntryType.InvestigatorNote,
            Title = "Second visit",
            Body = "Mr Park met us at the door and showed us the upstairs landing.",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();

        var result = await Retrofit().ApplyAsync(db, orgId, caseId, userId, default);

        Assert.Contains(result!.NameOccurrences, o => o.Kind == "Case" && o.Field == "Title");
        Assert.Contains(result.NameOccurrences, o => o.Kind == "CaseTimelineEntry" && o.Field == "Body");
        Assert.Contains(result.NameOccurrences, o => o.Matched == "Park");

        // The point: an investigator's account of a night is theirs. A find-and-replace through it
        // can change meaning or break a quotation, so the retrofit finds and reports, never edits.
        var entry = await db.CaseTimelineEntries.SingleAsync(e => e.Id == entryId);
        Assert.Equal("Mr Park met us at the door and showed us the upstairs landing.", entry.Body);
        Assert.Equal("Park, Nashville TN", (await db.Cases.SingleAsync(c => c.Id == caseId)).Title);
    }

    [Fact]
    public async Task Prose_that_merely_resembles_the_name_is_not_reported()
    {
        // Whole words only, three characters minimum — the same rule as the publish-time check,
        // so a Parkway and a Parker are not the Park family.
        var (db, orgId, caseId, userId) = await SeedAsync(title: "The Parker Farmhouse on Old Parkway");
        var result = await Retrofit().ApplyAsync(db, orgId, caseId, userId, default);
        Assert.Empty(result!.NameOccurrences);
    }

    [Fact]
    public async Task A_case_with_no_client_has_no_names_to_find()
    {
        var (db, orgId, caseId, userId) = await SeedAsync(withClient: false, title: "Park, Nashville TN");
        var result = await Retrofit().ApplyAsync(db, orgId, caseId, userId, default);

        // Internally raised cases have no client, so a place called Park is just a place.
        Assert.Empty(result!.NameOccurrences);
    }

    private static byte[] RealJpeg()
    {
        using var bitmap = new SkiaSharp.SKBitmap(2, 2);
        bitmap.SetPixel(0, 0, SkiaSharp.SKColors.Red);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data  = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }
}
