using System.Net;
using System.Text;
using Ben.Data.Common.Interfaces;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.LinkPreviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.LinkPreviews;

public sealed class LinkPreviewServiceTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private readonly Mock<IFileStorageService> _storage = new();
    private readonly List<Uri> _asked = [];
    private Func<Uri, HttpResponseMessage> _answer = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public async Task InitializeAsync() => _sqlite = await SqliteTestDb.CreateAsync();
    public async Task DisposeAsync() => await _sqlite.DisposeAsync();

    private sealed class Pages(Func<Uri, HttpResponseMessage> answer, List<Uri> asked) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            asked.Add(request.RequestUri!);
            return Task.FromResult(answer(request.RequestUri!));
        }
    }

    /// <summary>A clock that stands still, so a per-minute budget is tested without racing the minute.</summary>
    private sealed class StoppedClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }

    private TimeProvider _clock = new StoppedClock(new DateTimeOffset(2026, 9, 14, 12, 0, 30, TimeSpan.Zero));

    private LinkPreviewService Service() => new(
        _sqlite.Factory,
        new OpenGraphFetcher(new HttpClient(new Pages(u => _answer(u), _asked))),
        _storage.Object, new MediaSanitizationService(), new MemoryCache(new MemoryCacheOptions()),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AppBaseUrl"] = "https://ishaunted.com" }).Build(),
        NullLogger<LinkPreviewService>.Instance, _clock);

    private static HttpResponseMessage Html(string html) =>
        new(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") };

    [Fact]
    public async Task A_link_is_fetched_once_and_its_picture_is_copied_small_onto_our_storage()
    {
        _answer = u => u.AbsolutePath == "/og.jpg"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TestImages.Jpeg(1200, 900)) { Headers = { ContentType = new("image/jpeg") } } }
            : Html("""<meta property="og:title" content="Rest Haven"><meta property="og:image" content="https://example.com/og.jpg">""");
        var service = Service();
        var user = Guid.NewGuid();

        var first = await service.GetOrFetchAsync("https://Example.com/grave#top", user, refresh: false, default);
        var second = await service.GetOrFetchAsync("https://example.com/grave", user, refresh: false, default);

        Assert.NotNull(first);
        Assert.True(first!.Fetched);
        Assert.Equal("Rest Haven", first.Title);
        Assert.Equal(first.Id, second!.Id);
        Assert.Equal(2, _asked.Count);   // the page and its picture, once
        Assert.Equal(LinkPreviewService.StoragePathFor(first.Id), first.ThumbnailStoragePath);
        _storage.Verify(s => s.WriteAsync(LinkPreviewService.StoragePathFor(first.Id), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_expired_preview_is_fetched_again()
    {
        _answer = _ => Html("<title>Old</title>");
        var service = Service();
        var kept = await service.GetOrFetchAsync("https://example.com/", Guid.NewGuid(), false, default);
        // Eight days on.
        _clock = new StoppedClock(new DateTimeOffset(2026, 9, 22, 12, 0, 30, TimeSpan.Zero));
        _answer = _ => Html("<title>New</title>");
        var again = await Service().GetOrFetchAsync("https://example.com/", Guid.NewGuid(), false, default);
        Assert.Equal(kept!.Id, again!.Id);
        Assert.Equal("New", again.Title);
    }

    [Fact]
    public async Task Our_own_addresses_are_never_fetched()
    {
        Assert.Null(await Service().GetOrFetchAsync("https://ishaunted.com/o/paranormal365", Guid.NewGuid(), false, default));
        Assert.Empty(_asked);
    }

    [Fact]
    public async Task A_page_that_cannot_be_read_is_kept_unfetched_so_it_is_not_tried_again_for_a_week()
    {
        var service = Service();
        var kept = await service.GetOrFetchAsync("https://example.com/missing", Guid.NewGuid(), false, default);
        Assert.NotNull(kept);
        Assert.False(kept!.Fetched);
        Assert.NotNull(kept.FailureReason);

        await service.GetOrFetchAsync("https://example.com/missing", Guid.NewGuid(), false, default);
        Assert.Single(_asked);
    }

    [Fact]
    public async Task One_person_cannot_make_the_server_fetch_without_limit()
    {
        _answer = _ => Html("<title>x</title>");
        var service = Service();
        var user = Guid.NewGuid();
        for (var i = 0; i < LinkPreviewService.FetchesPerMinute + 5; i++)
            await service.GetOrFetchAsync($"https://example.com/{i}", user, false, default);

        Assert.Equal(LinkPreviewService.FetchesPerMinute, _asked.Count);

        // Somebody else still has their own budget.
        await service.GetOrFetchAsync("https://example.com/other", Guid.NewGuid(), false, default);
        Assert.Equal(LinkPreviewService.FetchesPerMinute + 1, _asked.Count);
    }

    [Theory]
    [InlineData("see https://example.com/a and https://example.org/b", null, 2)]
    [InlineData(null, "<p><a href=\"https://example.com/a\">a</a></p>", 1)]
    [InlineData("https://example.com/1 https://example.com/2 https://example.com/3 https://example.com/4", null, 3)]
    [InlineData("no links here", null, 0)]
    public void A_post_names_at_most_three_links(string? text, string? html, int expected) =>
        Assert.Equal(expected, LinkPreviewWarmer.LinksIn(text, html).Count);
}
