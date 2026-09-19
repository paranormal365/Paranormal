using System.Text;
using Ben.Data.WebApi.Services.LinkUnfurl;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The unfurl cache: one outbound fetch a week per working link, one a day per broken one, and
/// nothing stored that a pasted link carried in its query (canvas plan M6-09, R3, R21).
/// </summary>
/// <remarks>
/// Over SQLite rather than InMemory: the service sweeps expired rows with <c>ExecuteDeleteAsync</c>
/// and relies on the unique index on <c>UrlHash</c> when two requests race, and InMemory has neither.
/// </remarks>
public sealed class LinkUnfurlServiceTests
{
    /// <summary>A clock a test moves by hand.</summary>
    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    private sealed class StubFetcher : ISafeUrlFetcher
    {
        public int Calls;
        public Func<Uri, SafeFetchResult> Answer { get; set; } = url => Page(url);

        public Task<SafeFetchResult> FetchAsync(Uri url, string acceptPrefix, long maxBytes, CancellationToken ct)
        {
            Calls++;
            Assert.Equal("text/html", acceptPrefix);
            Assert.Equal(1_048_576, maxBytes);
            return Task.FromResult(Answer(url));
        }
    }

    private static SafeFetchResult Page(Uri url) => new(200, "text/html",
        Encoding.UTF8.GetBytes("""<html><head><meta property="og:title" content="Example Domain"><meta property="og:image" content="/i.png"></head></html>"""),
        url, null);

    private static async Task<(LinkUnfurlService Service, StubFetcher Fetcher, ManualClock Clock, SqliteTestDb Db)> BuildAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        var fetcher = new StubFetcher();
        var clock = new ManualClock(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));
        return (new LinkUnfurlService(sqlite.Factory, fetcher, clock), fetcher, clock, sqlite);
    }

    [Fact]
    public async Task A_cached_answer_is_served_within_seven_days_without_a_fetch()
    {
        var (service, fetcher, clock, db) = await BuildAsync();
        await using var _ = db;

        var first = await service.GetAsync("https://example.com/", default);
        clock.Advance(TimeSpan.FromDays(6.9));
        var second = await service.GetAsync("https://example.com/", default);

        Assert.Equal(LinkUnfurlStatus.Found, first.Status);
        Assert.Equal(LinkUnfurlStatus.Found, second.Status);
        Assert.Equal("Example Domain", second.Record!.Title);
        Assert.Equal("https://example.com/i.png", second.Record.ImageSourceUrl);
        Assert.Equal("example.com", second.Record.Host);
        Assert.Equal(1, fetcher.Calls);
    }

    [Fact]
    public async Task An_expired_row_is_refetched()
    {
        var (service, fetcher, clock, db) = await BuildAsync();
        await using var _ = db;

        await service.GetAsync("https://example.com/", default);
        clock.Advance(TimeSpan.FromDays(7.01));
        await service.GetAsync("https://example.com/", default);

        Assert.Equal(2, fetcher.Calls);
        await using var ctx = await db.NewContextAsync();
        Assert.Single(await ctx.LinkUnfurlCache.ToListAsync());   // updated in place, not duplicated
    }

    [Fact]
    public async Task A_500_is_cached_for_one_day()
    {
        var (service, fetcher, clock, db) = await BuildAsync();
        await using var _ = db;
        fetcher.Answer = url => new SafeFetchResult(500, "text/html", null, url, null);

        Assert.Equal(LinkUnfurlStatus.NotFound, (await service.GetAsync("https://example.com/down", default)).Status);
        clock.Advance(TimeSpan.FromHours(23));
        Assert.Equal(LinkUnfurlStatus.NotFound, (await service.GetAsync("https://example.com/down", default)).Status);
        Assert.Equal(1, fetcher.Calls);

        clock.Advance(TimeSpan.FromHours(2));   // +25 h
        await service.GetAsync("https://example.com/down", default);
        Assert.Equal(2, fetcher.Calls);
    }

    [Theory]
    [InlineData("https://10.0.0.1/")]
    [InlineData("http://example.com/")]
    [InlineData("not a url")]
    [InlineData("")]
    public async Task A_url_the_policy_refuses_is_refused_without_a_fetch(string url)
    {
        var (service, fetcher, _, db) = await BuildAsync();
        await using var _d = db;

        var outcome = await service.GetAsync(url, default);

        Assert.Equal(LinkUnfurlStatus.Refused, outcome.Status);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Refusal));
        Assert.Equal(0, fetcher.Calls);
        await using var ctx = await db.NewContextAsync();
        Assert.Empty(await ctx.LinkUnfurlCache.ToListAsync());
    }

    [Fact]
    public async Task A_link_whose_name_resolves_privately_is_not_found_and_cached_as_a_failure()
    {
        var (service, fetcher, _, db) = await BuildAsync();
        await using var _d = db;
        fetcher.Answer = url => new SafeFetchResult(0, null, null, url, "That site's address is on a private network, so it cannot be previewed.");

        Assert.Equal(LinkUnfurlStatus.NotFound, (await service.GetAsync("https://rebind.example/", default)).Status);
        Assert.Equal(LinkUnfurlStatus.NotFound, (await service.GetAsync("https://rebind.example/", default)).Status);

        Assert.Equal(1, fetcher.Calls);
    }

    [Fact]
    public async Task The_stored_url_has_no_query_string_but_the_cache_still_tells_queries_apart()
    {
        var (service, fetcher, _, db) = await BuildAsync();
        await using var _d = db;
        fetcher.Answer = url => new SafeFetchResult(200, "text/html",
            Encoding.UTF8.GetBytes($"<title>{url.Query}</title>"), url, null);

        var a = await service.GetAsync("https://example.com/doc?sig=SECRET-A#part", default);
        var b = await service.GetAsync("https://example.com/doc?sig=SECRET-B", default);

        Assert.Equal("?sig=SECRET-A", a.Record!.Title);
        Assert.Equal("?sig=SECRET-B", b.Record!.Title);
        Assert.Equal("https://example.com/doc?sig=SECRET-A", a.Record.Url);   // the caller's own link, fragment removed
        Assert.Equal(2, fetcher.Calls);

        await using var ctx = await db.NewContextAsync();
        var rows = await ctx.LinkUnfurlCache.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("https://example.com/doc", r.Url));
        Assert.All(rows, r => Assert.DoesNotContain("SECRET", r.Url));
        Assert.All(rows, r => Assert.Matches("^[0-9a-f]{64}$", r.UrlHash));
    }

    [Fact]
    public async Task Scheme_and_host_case_do_not_make_a_second_fetch()
    {
        var (service, fetcher, _, db) = await BuildAsync();
        await using var _d = db;

        await service.GetAsync("https://EXAMPLE.com/Path", default);
        await service.GetAsync("  https://example.com/Path  ", default);

        Assert.Equal(1, fetcher.Calls);
    }

    [Fact]
    public async Task Rows_expired_for_more_than_a_day_are_swept_when_something_is_written()
    {
        var (service, _, clock, db) = await BuildAsync();
        await using var _d = db;

        await service.GetAsync("https://example.com/old", default);
        clock.Advance(TimeSpan.FromDays(9));
        await service.GetAsync("https://example.com/new", default);

        await using var ctx = await db.NewContextAsync();
        var urls = await ctx.LinkUnfurlCache.Select(r => r.Url).ToListAsync();
        Assert.Equal(["https://example.com/new"], urls);
    }
}
