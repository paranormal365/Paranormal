using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Data.WebApi.Services.Places;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Places;

/// <summary>
/// The place catalogue: listing, demoting, and refusing to delete something in use (C8).
/// </summary>
/// <remarks>
/// <para><b>The delete is the part worth testing.</b> Twelve tables carry a PlaceId and the
/// database would refuse most of them on its own — but a refusal arriving from SQL after somebody
/// has pressed a button is not an answer, it is a stack trace. The rule is enforced in the
/// controller from <see cref="PlaceUsageCensus"/>, and the same function answers the question the
/// screen asks first, so the warning and the rule cannot drift apart. Item 220's person purge
/// drifted exactly that way and promised a removal the database then refused.</para>
///
/// <para><b>Demotion has to leave everything alone.</b> It is the repair for a home entered as a
/// public landmark, and a "repair" that threw away the evidence attached to the record would be
/// worse than the fault. So the demote test counts the evidence afterwards.</para>
/// </remarks>
public sealed class AdminPlaceCatalogTests
{
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly Guid FileTypeId = new("70000000-0000-0000-0000-000000000002");

    private static async Task<(SqliteTestDb Db, Guid PlaceId)> SeedAsync(
        PlaceKind kind = PlaceKind.PublicLocation, string? street = null)
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.Factory.CreateDbContextAsync();

        db.AppUsers.Add(new AppUser
        { Id = Admin, DisplayName = "A site admin", DateCreated = DateTime.UtcNow });
        db.UploadFileTypes.Add(new UploadFileType
        { Id = FileTypeId, Name = "Feed media", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Admin });

        var placeId = Guid.NewGuid();
        db.Places.Add(new Place
        {
            Id = placeId, Name = "Cragfont", City = "Castalian Springs", State = "TN",
            StreetAddress1 = street, Kind = kind,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Admin,
        });

        await db.SaveChangesAsync();
        return (sqlite, placeId);
    }

    private static void Contribute(BenDataContext db, Guid placeId)
    {
        var fileId = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, UploadFileTypeId = FileTypeId, AppUserId = Admin,
            FileName = "a.jpg", StoredFileName = $"{fileId}.jpg", ContentType = "image/jpeg",
            FileSize = 10, StoragePath = $"x/{fileId}.jpg",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Admin,
        });
        db.PlaceEvidence.Add(new PlaceEvidence
        {
            Id = Guid.NewGuid(), PlaceId = placeId, UploadFileId = fileId,
            AddedByAppUserId = Admin, Caption = "A window",
            ReviewState = FeedMediaReviewState.Approved,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Admin,
        });
    }

    private static AdminPlaceCatalogController Controller(SqliteTestDb sqlite)
    {
        var audit = new Mock<IAuditLogService>();
        audit.Setup(a => a.LogDeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(),
                                          It.IsAny<Guid>(), It.IsAny<string>()))
             .Returns(Task.CompletedTask);
        audit.Setup(a => a.LogUpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(),
                                          It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>()))
             .Returns(Task.CompletedTask);

        return new AdminPlaceCatalogController(
            sqlite.Factory, audit.Object, NullLogger<AdminPlaceCatalogController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, Admin.ToString())], "Bearer")),
                },
            },
        };
    }

    private static T Body<T>(ActionResult<T> result) where T : class
        => (T)((ObjectResult)result.Result!).Value!;

    private static string Refusal<T>(ActionResult<T> result)
        => (string)((BadRequestObjectResult)result.Result!).Value!;

    // ── The catalogue ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListsPlacesWithWhatIsAgainstThem()
    {
        var (sqlite, placeId) = await SeedAsync();
        await using var _ = sqlite;

        await using (var db = await sqlite.Factory.CreateDbContextAsync())
        {
            Contribute(db, placeId);
            await db.SaveChangesAsync();
        }

        var page = Body(await Controller(sqlite).List(null, null, 1, 50, default));

        var row = Assert.Single(page.Places);
        Assert.Equal("Cragfont", row.Name);
        Assert.Equal(PlaceKind.PublicLocation, row.Kind);
        Assert.Equal(1, row.Evidence);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task NarrowsByKind()
    {
        var (sqlite, _) = await SeedAsync(PlaceKind.PublicLocation);
        await using var __ = sqlite;

        var publicOnly = Body(await Controller(sqlite).List(null, PlaceKind.PublicLocation, 1, 50, default));
        var homesOnly = Body(await Controller(sqlite).List(null, PlaceKind.PrivateResidence, 1, 50, default));

        Assert.Single(publicOnly.Places);
        Assert.Empty(homesOnly.Places);
    }

    // ── Deleting ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletesAPlaceNothingPointsAt()
    {
        var (sqlite, placeId) = await SeedAsync();
        await using var _ = sqlite;

        var result = await Controller(sqlite).Delete(placeId, default);
        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await sqlite.Factory.CreateDbContextAsync();
        Assert.Empty(db.Places.Where(p => p.Id == placeId));
    }

    /// <summary>
    /// The refusal names what is in the way.
    /// </summary>
    /// <remarks>
    /// "This place is in use" leaves somebody with nowhere to go. The sentence has to say what to
    /// move, which is why the census returns words rather than a number — and why this asserts on
    /// the words rather than just on the status code.
    /// </remarks>
    [Fact]
    public async Task RefusesToDeleteAPlaceSomethingPointsAt()
    {
        var (sqlite, placeId) = await SeedAsync();
        await using var _ = sqlite;

        await using (var db = await sqlite.Factory.CreateDbContextAsync())
        {
            Contribute(db, placeId);
            await db.SaveChangesAsync();
        }

        var refusal = Refusal(await Controller(sqlite).Delete(placeId, default));
        Assert.Contains("1 piece of evidence", refusal);

        await using var after = await sqlite.Factory.CreateDbContextAsync();
        Assert.Single(after.Places.Where(p => p.Id == placeId));
    }

    /// <summary>
    /// What the screen is told before the button, and what the rule enforces after it, agree.
    /// </summary>
    /// <remarks>
    /// The item 220 shape: a preview that promised a removal the database refused. Asserting both
    /// halves in one test is the only way to catch the two drifting apart, because each half on its
    /// own looks correct.
    /// </remarks>
    [Fact]
    public async Task ThePreviewAndTheRuleAgree()
    {
        var (sqlite, placeId) = await SeedAsync();
        await using var _ = sqlite;

        await using (var db = await sqlite.Factory.CreateDbContextAsync())
        {
            Contribute(db, placeId);
            await db.SaveChangesAsync();
        }

        var usage = Body(await Controller(sqlite).Usage(placeId, default));
        Assert.False(usage.CanDelete);
        Assert.Equal(1, usage.Total);

        // The rule must reach the same verdict the preview showed.
        Assert.IsType<BadRequestObjectResult>((await Controller(sqlite).Delete(placeId, default)).Result);
    }

    // ── Kind ─────────────────────────────────────────────────────────────────

    /// <summary>Demoting takes it off the public map and destroys nothing.</summary>
    [Fact]
    public async Task DemotingKeepsEverythingOnThePlace()
    {
        var (sqlite, placeId) = await SeedAsync();
        await using var _ = sqlite;

        await using (var db = await sqlite.Factory.CreateDbContextAsync())
        {
            Contribute(db, placeId);
            await db.SaveChangesAsync();
        }

        var row = Body(await Controller(sqlite)
            .SetKind(placeId, new SetPlaceKindRequest(PlaceKind.PrivateResidence), default));

        Assert.Equal(PlaceKind.PrivateResidence, row.Kind);

        await using var after = await sqlite.Factory.CreateDbContextAsync();
        Assert.Equal(1, after.PlaceEvidence.Count(e => e.PlaceId == placeId));
    }

    /// <summary>
    /// A place with a street number cannot be promoted to a public location.
    /// </summary>
    /// <remarks>
    /// The rule that matters most on this screen. A public location gets a page strangers read and
    /// contribute to; turning somebody's home into one from a list of fifty rows is the single
    /// worst thing this page could do, so the promotion is refused rather than confirmed.
    /// </remarks>
    [Fact]
    public async Task RefusesToPromoteSomethingWithAStreetNumber()
    {
        var (sqlite, placeId) = await SeedAsync(PlaceKind.PrivateResidence, street: "114 Elm Street");
        await using var _ = sqlite;

        var refusal = Refusal(await Controller(sqlite)
            .SetKind(placeId, new SetPlaceKindRequest(PlaceKind.PublicLocation), default));

        Assert.Contains("street address", refusal);

        await using var after = await sqlite.Factory.CreateDbContextAsync();
        Assert.Equal(PlaceKind.PrivateResidence,
            after.Places.Single(p => p.Id == placeId).Kind);
    }

    /// <summary>A landmark with no street address promotes normally.</summary>
    [Fact]
    public async Task PromotesALandmarkWithNoStreetAddress()
    {
        var (sqlite, placeId) = await SeedAsync(PlaceKind.PrivateResidence);
        await using var _ = sqlite;

        var row = Body(await Controller(sqlite)
            .SetKind(placeId, new SetPlaceKindRequest(PlaceKind.PublicLocation), default));

        Assert.Equal(PlaceKind.PublicLocation, row.Kind);
    }

    // ── The census ───────────────────────────────────────────────────────────

    /// <summary>
    /// Every table that can hold a place is counted.
    /// </summary>
    /// <remarks>
    /// A reflection check rather than twelve fixtures. What this is really guarding is the day
    /// somebody adds a thirteenth table with a PlaceId and does not add it here — the delete would
    /// then offer to remove a place that table still names, and nothing else in the suite would
    /// notice.
    /// </remarks>
    [Fact]
    public void TheCensusCountsEveryTableThatCanHoldAPlace()
    {
        var carriers = typeof(Place).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(Place).Namespace)
            .Where(t => t.GetProperty("PlaceId") is not null)
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

        var counted = typeof(PlaceUsageCensus.Usage)
            .GetConstructors().Single()
            .GetParameters().Length;

        Assert.Equal(carriers.Count, counted);
    }
}
