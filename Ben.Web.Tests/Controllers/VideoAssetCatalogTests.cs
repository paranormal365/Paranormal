using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The video-asset catalog served to Ben.Video.Editor. Most of these guard a contract with a
/// consumer in a different repository, which no compiler checks.
/// </summary>
public class VideoAssetCatalogTests
{
    private static readonly byte[] AssetBytes = Encoding.UTF8.GetBytes("<svg/>");

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static VideoAssetController BuildPublic(IDbContextFactory<BenDataContext> factory)
    {
        var ctrl = new VideoAssetController(factory, Mock.Of<IFileStorageService>());
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host   = new HostString("ben.example");
        ctrl.ControllerContext = new ControllerContext { HttpContext = http };
        return ctrl;
    }

    private static AdminVideoAssetController BuildAdmin(
        IDbContextFactory<BenDataContext> factory, Guid? userId)
    {
        var ctrl = new AdminVideoAssetController(
            factory, Mock.Of<IFileStorageService>(), Mock.Of<IAuditLogService>());
        var principal = userId.HasValue
            ? new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], "Bearer"))
            : new ClaimsPrincipal(new ClaimsIdentity());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
        return ctrl;
    }

    private static async Task<Guid> AddFileAsync(
        IDbContextFactory<BenDataContext> factory, string fileName = "arrow.svg",
        string contentType = "image/svg+xml")
    {
        var id = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFiles.Add(new UploadFile
        {
            Id = id, UploadFileTypeId = Guid.NewGuid(), AppUserId = Guid.NewGuid(),
            FileName = fileName, StoredFileName = fileName, ContentType = contentType,
            FileSize = AssetBytes.Length, FileData = AssetBytes, IsPublic = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<VideoAssetAdminRecord> CreateAssetAsync(
        IDbContextFactory<BenDataContext> factory, Guid adminId,
        string name = "Arrow", string fileName = "arrow.svg", string contentType = "image/svg+xml")
    {
        var fileId = await AddFileAsync(factory, fileName, contentType);
        var result = await BuildAdmin(factory, adminId).Create(
            new CreateVideoAssetRequest(fileId, name, null, "Arrows", "arrow,pointer",
                VideoAssetType.Clipart), default);
        return (VideoAssetAdminRecord)((OkObjectResult)result.Result!).Value!;
    }

    private static async Task<List<VideoAssetCatalogItemRecord>> CatalogAsync(
        IDbContextFactory<BenDataContext> factory)
    {
        var result = await BuildPublic(factory).GetCatalog(default);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsAssignableFrom<IEnumerable<VideoAssetCatalogItemRecord>>(ok.Value).ToList();
    }

    // ── Wire contract with Ben.Video.Editor ───────────────────────────────────

    [Fact]
    public void EnumValues_MatchTheEditorsCopy()
    {
        // Neither side registers JsonStringEnumConverter, so these cross the wire as integers.
        // Ben.Video.Core/Models/Assets/VideoAssetEnums.cs declares the same members in this
        // order; renumbering here silently remaps every cached asset in the editor.
        Assert.Equal(0, (int)VideoAssetType.Clipart);
        Assert.Equal(1, (int)VideoAssetType.Callout);
        Assert.Equal(2, (int)VideoAssetType.Shape);
        Assert.Equal(3, (int)VideoAssetType.Frame);
        Assert.Equal(4, (int)VideoAssetType.Texture);
        Assert.Equal(5, (int)VideoAssetType.Sticker);
        Assert.Equal(6, (int)VideoAssetType.Watermark);

        Assert.Equal(0, (int)VideoAssetFormat.Svg);
        Assert.Equal(1, (int)VideoAssetFormat.Avif);
        Assert.Equal(2, (int)VideoAssetFormat.Png);
        Assert.Equal(3, (int)VideoAssetFormat.WebP);
        Assert.Equal(4, (int)VideoAssetFormat.Gif);
        Assert.Equal(5, (int)VideoAssetFormat.Lottie);

        Assert.Equal(0, (int)AssetSource.LocalOpfs);
        Assert.Equal(1, (int)AssetSource.AccountLibrary);
        Assert.Equal(2, (int)AssetSource.SharedCatalog);
    }

    /// <summary>
    /// A local stand-in for Ben.Video's VideoAssetCatalogItem — same property names and types,
    /// declared here because that repository isn't referenced from this solution.
    /// </summary>
    private sealed record EditorSideItem
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string? Category { get; init; }
        public IReadOnlyList<string> Tags { get; init; } = [];
        public AssetSource Source { get; init; }
        public VideoAssetType Type { get; init; }
        public VideoAssetFormat Format { get; init; }
        public string ThumbnailUrl { get; init; } = "";
        public string Version { get; init; } = "";
        public long FileSizeBytes { get; init; }
        public EditorSideSettings Settings { get; init; } = new();
    }

    private sealed record EditorSideSettings
    {
        public bool AllowRecolor { get; init; }
        public bool AllowControlPoints { get; init; }
        public bool FlattenOnExport { get; init; }
    }

    [Fact]
    public void TheEditorCanDeserialiseWhatThisApiEmits()
    {
        // ASP.NET serialises with Web defaults (camelCase); System.Net.Http.Json's
        // GetFromJsonAsync — what the editor calls — deserialises with the same defaults, which
        // are case-insensitive. This round-trips through both to prove the casing difference is
        // actually bridged, rather than asserting property names and hoping.
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var emitted = JsonSerializer.Serialize(new VideoAssetCatalogItemRecord
        {
            Id = "abc", Name = "Arrow", Category = "Arrows", Tags = ["a", "b"],
            Source = AssetSource.SharedCatalog, Type = VideoAssetType.Callout,
            Format = VideoAssetFormat.Gif, ThumbnailUrl = "https://x/y", Version = "hash",
            FileSizeBytes = 42,
            Settings = new VideoAssetSettingsRecord { AllowRecolor = true, AllowControlPoints = true },
        }, web);

        Assert.Contains("\"thumbnailUrl\"", emitted);   // camelCase on the wire

        var received = JsonSerializer.Deserialize<EditorSideItem>(emitted, web)!;

        Assert.Equal("abc", received.Id);
        Assert.Equal("Arrow", received.Name);
        Assert.Equal("Arrows", received.Category);
        Assert.Equal(["a", "b"], received.Tags);
        Assert.Equal(AssetSource.SharedCatalog, received.Source);
        Assert.Equal(VideoAssetType.Callout, received.Type);
        Assert.Equal(VideoAssetFormat.Gif, received.Format);
        Assert.Equal("https://x/y", received.ThumbnailUrl);
        Assert.Equal("hash", received.Version);
        Assert.Equal(42, received.FileSizeBytes);
        Assert.True(received.Settings.AllowRecolor);
        Assert.True(received.Settings.AllowControlPoints);
        Assert.True(received.Settings.FlattenOnExport);
    }

    // ── Catalog ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Catalog_IsEmptyBeforeAnythingIsPublished()
        => Assert.Empty(await CatalogAsync(CreateFactory()));

    [Fact]
    public async Task Catalog_ExposesAPublishedAsset()
    {
        var factory = CreateFactory();
        var created = await CreateAssetAsync(factory, Guid.NewGuid());

        var item = Assert.Single(await CatalogAsync(factory));

        Assert.Equal(created.Id.ToString(), item.Id);
        Assert.Equal("Arrow", item.Name);
        Assert.Equal(AssetSource.SharedCatalog, item.Source);
        Assert.Equal(VideoAssetFormat.Svg, item.Format);
        Assert.Equal(["arrow", "pointer"], item.Tags);
    }

    [Fact]
    public async Task Catalog_VersionIsTheContentHash()
    {
        var factory = CreateFactory();
        await CreateAssetAsync(factory, Guid.NewGuid());

        var item = Assert.Single(await CatalogAsync(factory));

        // The editor treats Version as a content fingerprint and re-downloads when it changes.
        // A timestamp here would cause pointless re-downloads and miss real edits.
        var expected = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(AssetBytes)).ToLowerInvariant();
        Assert.Equal(expected, item.Version);
    }

    [Fact]
    public async Task Catalog_ThumbnailUrlIsAbsolute()
    {
        var factory = CreateFactory();
        var created = await CreateAssetAsync(factory, Guid.NewGuid());

        var item = Assert.Single(await CatalogAsync(factory));

        // The editor drops this straight into an <img src> and may not share this origin.
        Assert.Equal($"https://ben.example/api/video-assets/{created.Id}/thumbnail", item.ThumbnailUrl);
    }

    [Fact]
    public async Task RetiredAssets_LeaveTheCatalogButStayDownloadable()
    {
        var factory = CreateFactory();
        var adminId = Guid.NewGuid();
        var created = await CreateAssetAsync(factory, adminId);

        Assert.IsType<NoContentResult>(await BuildAdmin(factory, adminId).Retire(created.Id, default));

        Assert.Empty(await CatalogAsync(factory));

        // Projects reference assets by id — retiring one must not break renders that already use it.
        var file = await BuildPublic(factory).GetFile(created.Id, default);
        Assert.IsType<FileContentResult>(file);
    }

    [Fact]
    public async Task Thumbnail_FallsBackToTheAssetWhenNoneIsSet()
    {
        var factory = CreateFactory();
        var created = await CreateAssetAsync(factory, Guid.NewGuid());

        var result = await BuildPublic(factory).GetThumbnail(created.Id, default);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(AssetBytes, file.FileContents);
    }

    // ── Admin ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("a.svg",  "image/svg+xml", VideoAssetFormat.Svg)]
    [InlineData("a.png",  "image/png",     VideoAssetFormat.Png)]
    [InlineData("a.gif",  "image/gif",     VideoAssetFormat.Gif)]
    [InlineData("a.webp", "image/webp",    VideoAssetFormat.WebP)]
    [InlineData("a.avif", "image/avif",    VideoAssetFormat.Avif)]
    public async Task Create_DerivesTheFormatFromTheFile(
        string fileName, string contentType, VideoAssetFormat expected)
    {
        var factory = CreateFactory();
        var created = await CreateAssetAsync(factory, Guid.NewGuid(), "A", fileName, contentType);

        // Derived, not taken from the caller: the editor picks its decoder from this, so a
        // mislabelled asset would fail in the browser instead of being refused here.
        Assert.Equal(expected, created.Format);
    }

    [Fact]
    public async Task Create_RejectsAFormatTheEditorCannotRender()
    {
        var factory = CreateFactory();
        var fileId  = await AddFileAsync(factory, "notes.txt", "text/plain");

        var result = await BuildAdmin(factory, Guid.NewGuid()).Create(
            new CreateVideoAssetRequest(fileId, "Notes", null, null, null, VideoAssetType.Clipart), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_EnablesRecolorOnlyForSvg()
    {
        var factory = CreateFactory();
        var admin   = Guid.NewGuid();
        var svg     = await CreateAssetAsync(factory, admin, "Vector", "a.svg", "image/svg+xml");
        var png     = await CreateAssetAsync(factory, admin, "Raster", "a.png", "image/png");

        var items = await CatalogAsync(factory);
        var svgItem = items.Single(i => i.Id == svg.Id.ToString());
        var pngItem = items.Single(i => i.Id == png.Id.ToString());

        // Only SVG can be recoloured or animated per-element by the editor.
        Assert.True(svgItem.Settings.AllowRecolor);
        Assert.True(svgItem.Settings.AllowControlPoints);
        Assert.False(pngItem.Settings.AllowRecolor);
        Assert.False(pngItem.Settings.AllowControlPoints);
    }

    [Fact]
    public async Task Create_ReturnsNotFound_ForAMissingFile()
    {
        var result = await BuildAdmin(CreateFactory(), Guid.NewGuid()).Create(
            new CreateVideoAssetRequest(Guid.NewGuid(), "Ghost", null, null, null,
                VideoAssetType.Clipart), default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Update_EditsMetadataWithoutTouchingTheHash()
    {
        var factory = CreateFactory();
        var admin   = Guid.NewGuid();
        var created = await CreateAssetAsync(factory, admin);

        var result = await BuildAdmin(factory, admin).Update(created.Id,
            new UpdateVideoAssetRequest("Renamed", "desc", "Shapes", "a,b",
                VideoAssetType.Sticker, IsActive: true), default);

        var updated = Assert.IsType<VideoAssetAdminRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(VideoAssetType.Sticker, updated.Type);
        // The binary didn't change, so the editor must not be told to re-download it.
        Assert.Equal(created.ContentHash, updated.ContentHash);
    }

    [Fact]
    public async Task Create_ReturnsUnauthorized_WithoutAUserClaim()
    {
        var factory = CreateFactory();
        var fileId  = await AddFileAsync(factory);

        var result = await BuildAdmin(factory, userId: null).Create(
            new CreateVideoAssetRequest(fileId, "A", null, null, null, VideoAssetType.Clipart), default);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }
}
