using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.LinkUnfurl;
using Ben.Service.Models.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The link-unfurl endpoint and its image proxy (canvas plan M6-10, reviews R1 and R21).
/// </summary>
/// <remarks>
/// <para>This is the one place the server fetches an address somebody pasted, so the tests are
/// about who may make it do that, how often, and what comes back: a record naming the page's own
/// picture (never a proxy address), and from the proxy only our own re-encoded JPEG — never a third
/// party's bytes, never a picture big enough to exhaust memory when decoded.</para>
/// </remarks>
public sealed class LinkUnfurlControllerTests
{
    private sealed class StubFetcher : ISafeUrlFetcher
    {
        public List<(Uri Url, string Accept, long Max)> Calls { get; } = [];
        public Func<Uri, SafeFetchResult> Answer { get; set; } = url => new(200, "text/html",
            Encoding.UTF8.GetBytes("""<title>Example Domain</title><meta property="og:image" content="https://cdn.example.com/card.png">"""), url, null);

        public Task<SafeFetchResult> FetchAsync(Uri url, string acceptPrefix, long maxBytes, CancellationToken ct)
        {
            Calls.Add((url, acceptPrefix, maxBytes));
            return Task.FromResult(Answer(url));
        }
    }

    private sealed record World(IDbContextFactory<BenDataContext> Security, Guid CreatorId, Guid ReaderId, Guid LonerId);

    private static async Task<World> SeedAsync()
    {
        var factory = new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var orgId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var readerId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization { Id = orgId, Name = "G", UrlName = $"g-{orgId:N}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            foreach (var member in new[] { creatorId, readerId })
                db.OrganizationUserMemberships.Add(new OrganizationUserMembership
                {
                    Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = member, Role = OrganizationMemberRole.Member,
                    IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
                });
            await db.SaveChangesAsync();
        }
        await TestSeeds.BridgeAsync(factory, orgId, TestSeeds.ReadOnly);
        await TestSeeds.GrantAsync(factory, orgId, creatorId, OrganizationSecurityTable.Case, TestSeeds.CaseWork);
        return new World(factory, creatorId, readerId, Guid.NewGuid());
    }

    private sealed record Built(LinkUnfurlController Controller, StubFetcher Fetcher, Mock<IMediaSanitizationService> Sanitizer, SqliteTestDb Cache);

    private static async Task<Built> BuildAsync(World w, Guid? userId, LinkUnfurlImageCeiling? ceiling = null)
    {
        var cache = await SqliteTestDb.CreateAsync();
        var fetcher = new StubFetcher();
        var sanitizer = new Mock<IMediaSanitizationService>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<byte[]>(), It.IsAny<int>())).Returns([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]);

        var ctrl = new LinkUnfurlController(
            new LinkUnfurlService(cache.Factory, fetcher, TimeProvider.System), fetcher, sanitizer.Object,
            w.Security, new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.Security),
            ceiling ?? new LinkUnfurlImageCeiling());
        var claims = userId is { } id ? new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()) } : [];
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, userId is null ? null : "Bearer")) },
        };
        return new Built(ctrl, fetcher, sanitizer, cache);
    }

    private static byte[] Png(int width = 4, int height = 3)
    {
        using var bitmap = new SkiaSharp.SKBitmap(width, height);
        bitmap.Erase(SkiaSharp.SKColors.Teal);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>A real PNG whose header claims <paramref name="width"/> x <paramref name="height"/> — a decompression bomb's shape.</summary>
    private static byte[] PngClaiming(int width, int height)
    {
        var png = Png(1, 1);
        // IHDR data starts at byte 16 (8 signature + 4 length + 4 type); CRC follows the 13 data bytes.
        WriteBigEndian(png, 16, (uint)width);
        WriteBigEndian(png, 20, (uint)height);
        WriteBigEndian(png, 29, Crc32(png.AsSpan(12, 17)));
        return png;
    }

    private static void WriteBigEndian(byte[] b, int at, uint v)
    {
        b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var x in data)
        {
            crc ^= x;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }

    private static SafeFetchResult ImageAnswer(Uri url, byte[] body, string type = "image/png") => new(200, type, body, url, null);

    // ── the door ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Sign-in is the door. The canvas flag that used to gate this went with the flag itself on
    /// 2026-09-16, when boards became the only way research is written and an off switch would have
    /// meant no research at all.
    /// </summary>
    [Fact]
    public void Controller_requires_sign_in_and_is_behind_no_feature_switch()
    {
        Assert.NotNull(typeof(LinkUnfurlController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(typeof(LinkUnfurlController).GetCustomAttribute<Ben.Data.WebApi.Services.FeatureGatedAttribute>(inherit: true));
    }

    [Theory]
    [InlineData(nameof(LinkUnfurlController.Get), RateLimiting.LinkUnfurlPolicy)]
    [InlineData(nameof(LinkUnfurlController.Image), RateLimiting.LinkUnfurlImagePolicy)]
    public void Rate_limit_policy_names_are_declared(string action, string policy)
    {
        var limit = typeof(LinkUnfurlController).GetMethod(action)!.GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.NotNull(limit);
        Assert.Equal(policy, limit!.PolicyName);
        Assert.Equal(30, RateLimiting.DefaultLinkUnfurlPerMinute);
        Assert.Equal(120, RateLimiting.DefaultLinkUnfurlImagePerMinute);
    }

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var w = await SeedAsync();
        var built = await BuildAsync(w, userId: null);
        await using var _ = built.Cache;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => built.Controller.Get("https://example.com/", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => built.Controller.Image("https://example.com/a.png", default));
        Assert.Empty(built.Fetcher.Calls);
    }

    /// <summary>R21: only somebody who can open a case somewhere may make this server fetch anything.</summary>
    [Fact]
    public async Task A_caller_who_cannot_create_a_case_in_any_group_is_refused()
    {
        var w = await SeedAsync();

        foreach (var who in new[] { w.ReaderId, w.LonerId })
        {
            var built = await BuildAsync(w, who);
            await using var _ = built.Cache;
            Assert.IsType<ForbidResult>((await built.Controller.Get("https://example.com/", default)).Result);
            Assert.IsType<ForbidResult>(await built.Controller.Image("https://cdn.example.com/card.png", default));
            Assert.Empty(built.Fetcher.Calls);
        }
    }

    // ── unfurl ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://192.168.1.71/")]
    [InlineData("https://localhost/")]
    [InlineData("not a url")]
    [InlineData("")]
    public async Task A_refused_url_is_400_and_nothing_is_fetched(string url)
    {
        var w = await SeedAsync();
        var built = await BuildAsync(w, w.CreatorId);
        await using var _ = built.Cache;

        var bad = Assert.IsType<BadRequestObjectResult>((await built.Controller.Get(url, default)).Result);
        Assert.IsType<string>(bad.Value);
        Assert.Empty(built.Fetcher.Calls);
    }

    [Fact]
    public async Task A_found_link_returns_the_image_source_not_a_proxy_address()
    {
        var w = await SeedAsync();
        var built = await BuildAsync(w, w.CreatorId);
        await using var _ = built.Cache;

        var ok = Assert.IsType<OkObjectResult>((await built.Controller.Get("https://example.com/", default)).Result);
        var record = Assert.IsType<LinkUnfurlRecord>(ok.Value);

        Assert.Equal("Example Domain", record.Title);
        Assert.Equal("https://cdn.example.com/card.png", record.ImageSourceUrl);
        Assert.DoesNotContain("link-unfurl", record.ImageSourceUrl);
        Assert.Equal("example.com", record.Host);
        Assert.Equal("private, max-age=86400", built.Controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task A_link_that_cannot_be_read_is_404()
    {
        var w = await SeedAsync();
        var built = await BuildAsync(w, w.CreatorId);
        await using var _ = built.Cache;
        built.Fetcher.Answer = url => new SafeFetchResult(503, null, null, url, null);

        Assert.IsType<NotFoundResult>((await built.Controller.Get("https://example.com/down", default)).Result);
    }

    // ── the image proxy ──────────────────────────────────────────────────────

    [Fact]
    public async Task The_image_proxy_re_encodes_through_the_sanitiser_at_800_and_caps_at_two_megabytes()
    {
        var w = await SeedAsync();
        var built = await BuildAsync(w, w.CreatorId);
        await using var _ = built.Cache;
        var png = Png();
        built.Fetcher.Answer = url => ImageAnswer(url, png);

        var file = Assert.IsType<FileContentResult>(await built.Controller.Image("https://cdn.example.com/card.png", default));

        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 }, file.FileContents);   // ours, not theirs
        built.Sanitizer.Verify(s => s.Sanitize(png, 800), Times.Once);
        var call = Assert.Single(built.Fetcher.Calls);
        Assert.Equal("image/", call.Accept);
        Assert.Equal(2_097_152, call.Max);
        Assert.Equal("private, max-age=604800", built.Controller.Response.Headers.CacheControl.ToString());
    }

    [Theory]
    [InlineData("http://cdn.example.com/a.png")]
    [InlineData("https://10.0.0.1/a.png")]
    [InlineData("nope")]
    public async Task The_image_proxy_refuses_an_address_the_policy_refuses(string url)
    {
        var w = await SeedAsync();
        var built = await BuildAsync(w, w.CreatorId);
        await using var _ = built.Cache;

        Assert.IsType<BadRequestObjectResult>(await built.Controller.Image(url, default));
        Assert.Empty(built.Fetcher.Calls);
    }

    [Fact]
    public async Task A_non_image_body_is_404()
    {
        var w = await SeedAsync();
        var built = await BuildAsync(w, w.CreatorId);
        await using var _ = built.Cache;
        built.Fetcher.Answer = url => ImageAnswer(url, "<svg onload=alert(1)></svg>"u8.ToArray(), "image/svg+xml");

        Assert.IsType<NotFoundResult>(await built.Controller.Image("https://cdn.example.com/x.svg", default));
        built.Sanitizer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_fetch_that_failed_is_404()
    {
        var w = await SeedAsync();
        var built = await BuildAsync(w, w.CreatorId);
        await using var _ = built.Cache;
        built.Fetcher.Answer = url => new SafeFetchResult(0, null, null, url, "The page is too large to preview.");

        Assert.IsType<NotFoundResult>(await built.Controller.Image("https://cdn.example.com/huge.png", default));
        built.Sanitizer.VerifyNoOtherCalls();
    }

    /// <summary>
    /// R21: a small file whose header claims an enormous picture is refused from the header, before
    /// anything decodes it — decoding 50,000 x 50,000 pixels would take ten gigabytes.
    /// </summary>
    [Fact]
    public async Task A_decompression_bomb_is_refused_before_decoding()
    {
        var w = await SeedAsync();
        var built = await BuildAsync(w, w.CreatorId);
        await using var _ = built.Cache;
        var bomb = PngClaiming(50_000, 50_000);
        Assert.True(bomb.Length < 2_000, "the bomb itself is tiny; that is what makes it a bomb");
        built.Fetcher.Answer = url => ImageAnswer(url, bomb);

        Assert.IsType<NotFoundResult>(await built.Controller.Image("https://cdn.example.com/bomb.png", default));
        built.Sanitizer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_picture_at_the_pixel_limit_is_still_accepted()
    {
        var w = await SeedAsync();
        var built = await BuildAsync(w, w.CreatorId);
        await using var _ = built.Cache;
        var atLimit = PngClaiming(8_000, 5_000);   // exactly 40,000,000 pixels
        built.Fetcher.Answer = url => ImageAnswer(url, atLimit);

        Assert.IsType<FileContentResult>(await built.Controller.Image("https://cdn.example.com/big.png", default));
    }

    [Fact]
    public async Task An_image_the_sanitiser_cannot_read_is_404()
    {
        var w = await SeedAsync();
        var built = await BuildAsync(w, w.CreatorId);
        await using var _ = built.Cache;
        built.Fetcher.Answer = url => ImageAnswer(url, Png());
        built.Sanitizer.Setup(s => s.Sanitize(It.IsAny<byte[]>(), It.IsAny<int>())).Throws(new UnreadableImageException("no"));

        Assert.IsType<NotFoundResult>(await built.Controller.Image("https://cdn.example.com/card.png", default));
    }

    /// <summary>R21: the image proxy has one ceiling for everybody, not only one per person.</summary>
    [Fact]
    public async Task The_image_proxy_has_a_ceiling_shared_by_every_caller()
    {
        var w = await SeedAsync();
        var shared = new LinkUnfurlImageCeiling(perMinute: 2);
        var png = Png();

        async Task<IActionResult> AsSomebodyNew()
        {
            // A different person each time: the per-person policy would never notice.
            var creator = Guid.NewGuid();
            await TestSeeds.GrantAsync(w.Security, (await OrgOf(w)), creator, OrganizationSecurityTable.Case, TestSeeds.CaseWork);
            await using (var db = await w.Security.CreateDbContextAsync())
            {
                db.OrganizationUserMemberships.Add(new OrganizationUserMembership
                {
                    Id = Guid.NewGuid(), OrganizationId = await OrgOf(w), AppUserId = creator, Role = OrganizationMemberRole.Member,
                    IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = creator,
                });
                await db.SaveChangesAsync();
            }
            var built = await BuildAsync(w, creator, shared);
            await using var _ = built.Cache;
            built.Fetcher.Answer = url => ImageAnswer(url, png);
            return await built.Controller.Image("https://cdn.example.com/card.png", default);
        }

        Assert.IsType<FileContentResult>(await AsSomebodyNew());
        Assert.IsType<FileContentResult>(await AsSomebodyNew());
        var third = Assert.IsType<StatusCodeResult>(await AsSomebodyNew());
        Assert.Equal(429, third.StatusCode);
    }

    private static async Task<Guid> OrgOf(World w)
    {
        await using var db = await w.Security.CreateDbContextAsync();
        return await db.Organizations.Select(o => o.Id).FirstAsync();
    }
}
