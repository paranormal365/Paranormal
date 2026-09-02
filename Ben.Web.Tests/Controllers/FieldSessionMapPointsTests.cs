using System.Text;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Where a person's own sessions get plotted, and — more importantly — where they do not.
/// </summary>
/// <remarks>
/// <para>The coordinate is not on the row: it lives inside the session document, so this endpoint
/// opens a file per session and reads the first reading that carries a position. That makes two
/// things worth pinning down. A session with no fix must be left out rather than pinned at zero,
/// which is the Gulf of Guinea; and a session belonging to somebody else must never appear,
/// because a coordinate is the most sensitive thing a session carries.</para>
///
/// <para>The second is the one Ben asked about twice while this was being built, and it is the
/// test that matters: the query is scoped to the caller, and if that ever changes this fails.</para>
/// </remarks>
public class FieldSessionMapPointsTests
{
    /// <summary>Storage that hands back whatever document was filed under a path.</summary>
    private sealed class DictionaryStorage(Dictionary<string, string> documents) : IFileStorageService
    {
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
            => documents.TryGetValue(relativePath, out var body)
                ? Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(body)))
                : throw new FileNotFoundException(relativePath);

        public Task WriteAsync(string relativePath, Stream data, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string relativePath, CancellationToken ct = default) => Task.CompletedTask;
        public bool Exists(string relativePath) => documents.ContainsKey(relativePath);
        public IReadOnlyList<string> ListFiles(string relativeDirectory) => [];
        public string UserFilePath(Guid userId, string storedFileName) => $"users/{userId}/{storedFileName}";
        public string OrgFilePath(Guid orgId, string storedFileName) => $"orgs/{orgId}/{storedFileName}";
        public string CaseFilePath(Guid caseId, string storedFileName) => $"cases/{caseId}/{storedFileName}";
    }

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    /// <summary>
    /// A session document. When a coordinate is given it sits on the SECOND reading, with a
    /// different one later, so "first fix wins" is actually exercised rather than assumed.
    /// </summary>
    private static string Document(double? latitude, double? longitude)
    {
        const string head =
            "{\"format_version\":\"1.0.0\"," +
            "\"device\":{\"manufacturer\":\"Apple\",\"model\":\"iPhone17,1\"}," +
            "\"session\":{\"started_at\":\"2026-09-01T21:00:00Z\"},\"readings\":[" +
            "{\"at\":\"2026-09-01T21:00:00Z\"}";

        if (latitude is null) return head + "]}";

        var fix = System.Globalization.CultureInfo.InvariantCulture;
        return head
            + ",{\"at\":\"2026-09-01T21:00:05Z\",\"position\":{\"latitude\":"
            + latitude.Value.ToString(fix) + ",\"longitude\":" + longitude!.Value.ToString(fix) + "}}"
            + ",{\"at\":\"2026-09-01T21:00:10Z\",\"position\":{\"latitude\":1.0,\"longitude\":1.0}}"
            + "]}";
    }

    private static Guid SeedSession(
        BenDataContext db, Dictionary<string, string> storage, Guid owner, string label, string document)
    {
        var path = $"orgs/x/field-sessions/{Guid.NewGuid()}.json";
        storage[path] = document;

        var file = new UploadFile
        {
            Id = Guid.NewGuid(), FileName = "data.json", ContentType = "application/json",
            StoragePath = path, DateCreated = DateTime.UtcNow,
        };
        var session = new FieldSessionUpload
        {
            Id = Guid.NewGuid(), DeviceSessionId = Guid.NewGuid(), DeviceModel = "iPhone17,1",
            LocationLabel = label, StartedAt = DateTime.UtcNow.AddHours(-1),
            SubmittedByAppUserId = owner,
            DocumentUploadFileId = file.Id, DocumentUploadFile = file,
            ReadingCount = 3, MarkerCount = 1, DateCreated = DateTime.UtcNow,
        };
        db.UploadFiles.Add(file);
        db.FieldSessionUploads.Add(session);
        return session.Id;
    }

    private static FieldSessionUploadController Build(
        IDbContextFactory<BenDataContext> factory, Dictionary<string, string> storage, Guid userId)
    {
        // mediaIngest is only used by the upload paths; the map endpoint never touches it.
        var controller = new FieldSessionUploadController(
            factory, new DictionaryStorage(storage),
            mediaIngest: null!, NullLogger<FieldSessionUploadController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"))
            }
        };
        return controller;
    }

    [Fact]
    public async Task A_session_is_pinned_at_its_first_fix()
    {
        var factory = CreateFactory();
        var storage = new Dictionary<string, string>();
        var me = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            SeedSession(db, storage, me, "the cellar", Document(36.5824, -87.0625));
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, storage, me).GetMyMapPoints(default);
        var points = Assert.IsAssignableFrom<IEnumerable<FieldSessionMapPoint>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();

        var pin = Assert.Single(points);
        Assert.Equal("the cellar", pin.Title);
        // The FIRST fix, not the last: a later reading in the document sits at 1,1.
        Assert.Equal(36.5824m, pin.Latitude);
        Assert.Equal(-87.0625m, pin.Longitude);
    }

    [Fact]
    public async Task A_session_that_never_got_a_fix_is_left_off_rather_than_pinned_at_zero()
    {
        var factory = CreateFactory();
        var storage = new Dictionary<string, string>();
        var me = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            SeedSession(db, storage, me, "indoors, no fix", Document(null, null));
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, storage, me).GetMyMapPoints(default);
        var points = Assert.IsAssignableFrom<IEnumerable<FieldSessionMapPoint>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Empty(points);
    }

    /// <summary>
    /// The one that matters. Somebody else's session is not the caller's to place on a map, and a
    /// coordinate is the most sensitive thing a session carries.
    /// </summary>
    [Fact]
    public async Task Another_persons_session_is_never_pinned()
    {
        var factory = CreateFactory();
        var storage = new Dictionary<string, string>();
        var me = Guid.NewGuid();
        var somebodyElse = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            SeedSession(db, storage, me, "mine", Document(36.58, -87.06));
            SeedSession(db, storage, somebodyElse, "theirs", Document(40.71, -74.00));
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, storage, me).GetMyMapPoints(default);
        var points = Assert.IsAssignableFrom<IEnumerable<FieldSessionMapPoint>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();

        var pin = Assert.Single(points);
        Assert.Equal("mine", pin.Title);
    }

    [Fact]
    public async Task A_session_whose_document_is_missing_is_left_off()
    {
        var factory = CreateFactory();
        var storage = new Dictionary<string, string>();   // nothing filed: every read throws
        var me = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            SeedSession(db, storage, me, "orphan", Document(36.58, -87.06));
            storage.Clear();
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, storage, me).GetMyMapPoints(default);
        var points = Assert.IsAssignableFrom<IEnumerable<FieldSessionMapPoint>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Empty(points);
    }

    [Fact]
    public async Task A_signed_out_caller_gets_nothing()
    {
        var result = await Build(CreateFactory(), [], Guid.Empty).GetMyMapPoints(default);
        Assert.IsType<UnauthorizedResult>(result.Result);
    }
}
