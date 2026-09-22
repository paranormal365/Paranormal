using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.Feed;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using System.Text;
using Xunit;

namespace Ben.Web.Tests.Places;

/// <summary>
/// Adding a file straight to a public place (item 250).
/// </summary>
/// <remarks>
/// <para>The two that carry the feature are <see cref="A_private_residence_refuses_evidence"/> and
/// <see cref="Something_held_is_not_shown_to_anybody"/>. The door is open to anybody signed in,
/// which Ben chose deliberately, so the only things standing between a public page and whatever
/// somebody uploads are the place's kind and the screener's verdict.</para>
/// </remarks>
public sealed class PlaceEvidenceTests
{
    private static readonly Guid Contributor = Guid.NewGuid();

    private static IFormFile AFile(string name = "corridor.jpg", string type = "image/jpeg")
        => new FormFile(new MemoryStream(Encoding.UTF8.GetBytes("bytes")), 0, 5, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = type,
        };

    private static async Task<(SqliteTestDb Db, Guid PlaceId)> SeedAsync(PlaceKind kind)
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.Factory.CreateDbContextAsync();

        db.AppUsers.Add(new AppUser
        { Id = Contributor, DisplayName = "A visitor", Handle = "visitor", DateCreated = DateTime.UtcNow });

        var placeId = Guid.NewGuid();
        db.Places.Add(new Place
        {
            Id = placeId, Name = "Cragfont", Kind = kind,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Contributor,
        });

        db.UploadFileTypes.Add(new UploadFileType
        {
            Id = new Guid("70000000-0000-0000-0000-000000000001"),
            Name = "Feed media", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Contributor,
        });

        await db.SaveChangesAsync();
        return (sqlite, placeId);
    }

    private static PlaceEvidenceController Controller(
        SqliteTestDb sqlite, FeedMediaReviewState verdict = FeedMediaReviewState.Approved)
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.UserFilePath(It.IsAny<Guid>(), It.IsAny<string>()))
               .Returns<Guid, string>((u, n) => $"users/{u}/{n}");

        var ingest = new Mock<IMediaIngestService>();
        ingest.Setup(m => m.IngestAsync(It.IsAny<IFormFile>(), It.IsAny<string>(),
                                        It.IsAny<Guid>(), It.IsAny<CancellationToken>(), true))
              .ReturnsAsync((IFormFile f, string path, Guid id, CancellationToken _, bool __) =>
                  new IngestedMedia(
                      new UploadFileMetadata { Id = Guid.NewGuid(), UploadFileId = id },
                      f.Length, f.ContentType, WasSanitized: false));

        var screener = new Mock<IFeedMediaScreener>();
        screener.Setup(s => s.ScreenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FeedMediaVerdict(verdict, verdict == FeedMediaReviewState.Held ? "Held." : null));

        return new PlaceEvidenceController(
            sqlite.Factory, storage.Object, ingest.Object, screener.Object,
            NullLogger<PlaceEvidenceController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, Contributor.ToString())], "Bearer")),
                },
            },
        };
    }

    [Fact]
    public async Task Anybody_signed_in_can_add_to_a_public_location()
    {
        var (sqlite, placeId) = await SeedAsync(PlaceKind.PublicLocation);
        await using var _ = sqlite;

        // The contributor belongs to no group and has investigated nowhere. That is the point:
        // the person with the photograph of Cragfont is rarely a member of anything.
        var result = await Controller(sqlite).Add(placeId, AFile(), "The upstairs corridor", default);

        var added = Assert.IsType<PlaceEvidenceAdded>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(added.Showing);

        await using var db = await sqlite.Factory.CreateDbContextAsync();
        var row = Assert.Single(await db.PlaceEvidence.ToListAsync());
        Assert.Equal("The upstairs corridor", row.Caption);
        Assert.Equal(FeedMediaReviewState.Approved, row.ReviewState);
    }

    [Fact]
    public async Task What_somebody_adds_to_a_place_counts_against_their_allowance()
    {
        var (sqlite, placeId) = await SeedAsync(PlaceKind.PublicLocation);
        await using var _ = sqlite;

        await Controller(sqlite).Add(placeId, AFile(), "The corridor", default);

        await using var db = await sqlite.Factory.CreateDbContextAsync();

        // The guard's own remarks said a second kind of personal upload would come and that one
        // place would decide what "kept to yourself" means. Item 250 added that second kind and
        // for a day it was stored under the person and counted by nothing (C1).
        var used = await AccountStorageGuard.UsedBytesAsync(db, Contributor, default);
        Assert.True(used > 0, "evidence added to a place is stored under the person and must count");

        var stored = await db.UploadFiles.SumAsync(f => f.FileSize);
        Assert.Equal(stored, used);
    }

    [Fact]
    public async Task A_private_residence_refuses_evidence()
    {
        var (sqlite, placeId) = await SeedAsync(PlaceKind.PrivateResidence);
        await using var _ = sqlite;

        var result = await Controller(sqlite).Add(placeId, AFile(), null, default);

        // Somebody's home. The whole safety story of the archive is that its bytes can only ever
        // be attached to a place that is not one — and the refusal is a sentence, because a reader
        // has no way to know what kind of row sits behind a page.
        var refusal = Assert.IsType<string>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Contains("public location", refusal);

        await using var db = await sqlite.Factory.CreateDbContextAsync();
        Assert.Empty(await db.PlaceEvidence.ToListAsync());
        // And no orphan file either — nothing was stored against a refusal.
        Assert.Empty(await db.UploadFiles.ToListAsync());
    }

    [Fact]
    public async Task Something_held_is_not_shown_to_anybody()
    {
        var (sqlite, placeId) = await SeedAsync(PlaceKind.PublicLocation);
        await using var _ = sqlite;

        var result = await Controller(sqlite, FeedMediaReviewState.Held)
            .Add(placeId, AFile(), null, default);

        var added = Assert.IsType<PlaceEvidenceAdded>(Assert.IsType<OkObjectResult>(result.Result).Value);

        // Held is not a failure, and the uploader is told the truth rather than "added".
        Assert.False(added.Showing);
        Assert.Contains("look at this", added.Says);

        await using var db = await sqlite.Factory.CreateDbContextAsync();
        Assert.Empty(await PlaceEvidencePublication.Showable(db, placeId, PlaceMediaKind.Evidence).ToListAsync());
    }

    [Fact]
    public async Task A_place_corrected_to_a_residence_takes_its_evidence_down_with_it()
    {
        var (sqlite, placeId) = await SeedAsync(PlaceKind.PublicLocation);
        await using var _ = sqlite;

        await Controller(sqlite).Add(placeId, AFile(), null, default);

        await using (var db = await sqlite.Factory.CreateDbContextAsync())
        {
            Assert.Single(await PlaceEvidencePublication.Showable(db, placeId, PlaceMediaKind.Evidence).ToListAsync());

            var place = await db.Places.FirstAsync(p => p.Id == placeId);
            place.Kind = PlaceKind.PrivateResidence;
            await db.SaveChangesAsync();
        }

        // The rule is re-asked, never remembered. Nothing to migrate, no page to go and edit —
        // the same discipline the field-session archive is built on.
        await using (var db = await sqlite.Factory.CreateDbContextAsync())
        {
            Assert.Empty(await PlaceEvidencePublication.Showable(db, placeId, PlaceMediaKind.Evidence).ToListAsync());
            Assert.False(await PlaceEvidencePublication.MayServeAsync(
                db, placeId, (await db.PlaceEvidence.FirstAsync()).Id, default));
        }
    }

    [Fact]
    public async Task Only_the_person_who_added_it_can_take_it_back()
    {
        var (sqlite, placeId) = await SeedAsync(PlaceKind.PublicLocation);
        await using var _ = sqlite;

        await Controller(sqlite).Add(placeId, AFile(), null, default);

        Guid evidenceId;
        await using (var db = await sqlite.Factory.CreateDbContextAsync())
            evidenceId = (await db.PlaceEvidence.FirstAsync()).Id;

        var somebodyElse = Controller(sqlite);
        somebodyElse.ControllerContext.HttpContext!.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Bearer"));

        // Taking somebody else's contribution off a public page is moderation, which is a separate
        // power with a separate screen — not something every signed-in reader holds.
        Assert.IsType<ForbidResult>((await somebodyElse.Remove(placeId, evidenceId, default)).Result);

        Assert.IsType<OkObjectResult>(
            (await Controller(sqlite).Remove(placeId, evidenceId, default)).Result);
    }
}
