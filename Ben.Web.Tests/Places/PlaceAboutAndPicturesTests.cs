using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Places;

/// <summary>
/// A picture of the building is not evidence (item 250).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-21: <i>"images of it — not evidence but the property and photos inside to
/// show it."</i> The distinction is the whole design. A daylight photograph of a staircase taken
/// to show a reader what the house looks like, and a photograph of the same staircase somebody
/// thinks has a figure on it, are different claims — mixed together every count on the page is
/// wrong and a reader cannot tell what anybody is asserting.</para>
///
/// <para>What keeps them apart is that every query takes the kind as a REQUIRED argument, so
/// there is no call that returns both by accident. <see cref="A_picture_of_the_building_is_in_no_evidence_count"/>
/// is the test that would catch a filter going missing.</para>
/// </remarks>
public sealed class PlaceAboutAndPicturesTests
{
    private static readonly Guid Contributor = Guid.NewGuid();
    private static readonly Guid FileTypeId = new("70000000-0000-0000-0000-000000000001");

    private static async Task<(SqliteTestDb Db, Guid PlaceId)> SeedAsync(
        PlaceKind kind = PlaceKind.PublicLocation)
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.Factory.CreateDbContextAsync();

        db.AppUsers.Add(new AppUser
        { Id = Contributor, DisplayName = "A visitor", DateCreated = DateTime.UtcNow });
        db.UploadFileTypes.Add(new UploadFileType
        { Id = FileTypeId, Name = "Feed media", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Contributor });

        var placeId = Guid.NewGuid();
        db.Places.Add(new Place
        {
            Id = placeId, Name = "Cragfont", Kind = kind,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Contributor,
        });
        await db.SaveChangesAsync();
        return (sqlite, placeId);
    }

    private static Guid Attach(BenDataContext db, Guid placeId, PlaceMediaKind kind)
    {
        var fileId = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, UploadFileTypeId = FileTypeId, AppUserId = Contributor,
            FileName = "a.jpg", StoredFileName = $"{fileId}.jpg", ContentType = "image/jpeg",
            FileSize = 1, StoragePath = $"x/{fileId}.jpg",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Contributor,
        });
        db.PlaceEvidence.Add(new PlaceEvidence
        {
            Id = Guid.NewGuid(), PlaceId = placeId, UploadFileId = fileId,
            AddedByAppUserId = Contributor, MediaKind = kind,
            ReviewState = FeedMediaReviewState.Approved,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Contributor,
        });
        return fileId;
    }

    private static PlaceController Controller(SqliteTestDb sqlite, Guid userId)
        => new(sqlite.Factory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
                },
            },
        };

    [Fact]
    public async Task A_picture_of_the_building_is_in_no_evidence_count()
    {
        var (sqlite, placeId) = await SeedAsync();
        await using var _ = sqlite;

        await using (var db = await sqlite.Factory.CreateDbContextAsync())
        {
            Attach(db, placeId, PlaceMediaKind.Evidence);
            Attach(db, placeId, PlaceMediaKind.AboutThePlace);
            Attach(db, placeId, PlaceMediaKind.AboutThePlace);
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.Factory.CreateDbContextAsync())
        {
            // One piece of evidence, not three. Counting the property photographs would inflate
            // every figure on the page with pictures nobody claimed anything about.
            Assert.Equal(1, (await PlaceEvidenceTally.ForPlaceAsync(db, placeId, default)).EvidenceCount);

            Assert.Single(await PlaceEvidencePublication
                .Showable(db, placeId, PlaceMediaKind.Evidence).ToListAsync());
            Assert.Equal(2, await PlaceEvidencePublication
                .Showable(db, placeId, PlaceMediaKind.AboutThePlace).CountAsync());
        }
    }

    [Fact]
    public async Task Both_kinds_serve_their_bytes()
    {
        var (sqlite, placeId) = await SeedAsync();
        await using var _ = sqlite;

        Guid evidenceRow, pictureRow;
        await using (var db = await sqlite.Factory.CreateDbContextAsync())
        {
            Attach(db, placeId, PlaceMediaKind.Evidence);
            Attach(db, placeId, PlaceMediaKind.AboutThePlace);
            await db.SaveChangesAsync();

            evidenceRow = await db.PlaceEvidence
                .Where(e => e.MediaKind == PlaceMediaKind.Evidence).Select(e => e.Id).FirstAsync();
            pictureRow = await db.PlaceEvidence
                .Where(e => e.MediaKind == PlaceMediaKind.AboutThePlace).Select(e => e.Id).FirstAsync();
        }

        await using (var db = await sqlite.Factory.CreateDbContextAsync())
        {
            // The kind decides which LIST a row is in and whether it counts — never whether its
            // bytes may be read. A photograph of the frontage the page shows and the door refuses
            // is a broken frame.
            Assert.True(await PlaceEvidencePublication.MayServeAsync(db, placeId, evidenceRow, default));
            Assert.True(await PlaceEvidencePublication.MayServeAsync(db, placeId, pictureRow, default));
        }
    }

    [Fact]
    public async Task Anybody_signed_in_can_describe_a_public_location()
    {
        var (sqlite, placeId) = await SeedAsync();
        await using var _ = sqlite;

        var result = await Controller(sqlite, Contributor).SetDescription(
            placeId, new SetPlaceDescriptionRequest("Built in 1802 by General James Winchester."),
            default);

        var place = Assert.IsType<PlaceRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Built in 1802 by General James Winchester.", place.Description);
    }

    [Fact]
    public async Task Markup_is_stripped_rather_than_refused()
    {
        var (sqlite, placeId) = await SeedAsync();
        await using var _ = sqlite;

        var result = await Controller(sqlite, Contributor).SetDescription(
            placeId,
            new SetPlaceDescriptionRequest("<script>alert(1)</script>A house <b>with</b> a history."),
            default);

        var place = Assert.IsType<PlaceRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        // Cleaned, not rejected: somebody describing a house should not have to know what an angle
        // bracket does. But nothing that could carry a script or a layout survives onto a page any
        // stranger reads.
        Assert.DoesNotContain("<", place.Description);
        Assert.Contains("a history", place.Description);

        // A script's CONTENT goes with it. Stripping tags alone leaves "alert(1)" sitting in the
        // description as words nobody typed — harmless, since this renders as text, and still
        // wrong.
        Assert.DoesNotContain("alert", place.Description);

        // And the prose inside ordinary tags survives, which is the reason tags are stripped
        // rather than their contents.
        Assert.Contains("with", place.Description);
    }

    [Fact]
    public async Task A_home_has_no_page_to_describe()
    {
        var (sqlite, placeId) = await SeedAsync(PlaceKind.PrivateResidence);
        await using var _ = sqlite;

        var result = await Controller(sqlite, Contributor).SetDescription(
            placeId, new SetPlaceDescriptionRequest("Somebody lives here."), default);

        var refusal = Assert.IsType<string>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Contains("somebody's home", refusal);

        await using var db = await sqlite.Factory.CreateDbContextAsync();
        Assert.Null((await db.Places.FirstAsync(p => p.Id == placeId)).Description);
    }
}
