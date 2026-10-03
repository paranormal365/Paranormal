using System.Net;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services.Apple;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The app's "check for a new version": which version Apple says is live (Ben, 10/03/2026).
/// </summary>
/// <remarks>
/// The fixture is Apple's real answer for this app, fetched on 10/03/2026 — the morning 1.1.2 went
/// live — not one written by hand: an invented fixture once shipped a feature that could not read
/// the real response.
/// </remarks>
public sealed class AppStoreVersionLookupTests
{
    private static string RealLookup =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AppStore", "lookup-ishaunted.json"));

    private sealed class FakeApple(Func<HttpResponseMessage> answer) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastUri = request.RequestUri;
            return Task.FromResult(answer());
        }
    }

    private static (AppStoreVersionLookup Lookup, FakeApple Apple) Build(Func<HttpResponseMessage> answer)
    {
        var apple = new FakeApple(answer);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var lookup = new AppStoreVersionLookup(
            new HttpClient(apple), new MemoryCache(new MemoryCacheOptions()), config,
            NullLogger<AppStoreVersionLookup>.Instance);
        return (lookup, apple);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    [Fact]
    public void Apple_s_real_answer_reads_as_the_live_release()
    {
        var release = AppStoreVersionLookup.Parse(RealLookup, "com.ishaunted.ios");

        Assert.NotNull(release);
        Assert.Equal("1.1.2", release!.Version);
        Assert.Equal(new DateTime(2026, 10, 3, 1, 42, 13, DateTimeKind.Utc), release.ReleasedUtc);
        Assert.StartsWith("A new look that matches the website", release.ReleaseNotes);
        Assert.StartsWith("https://apps.apple.com/", release.StoreUrl);
        Assert.Equal("18.0", release.MinimumOsVersion);
    }

    [Fact]
    public void Another_app_s_answer_is_never_read_as_ours()
    {
        Assert.Null(AppStoreVersionLookup.Parse(RealLookup, "com.example.other"));
    }

    [Fact]
    public void An_app_not_in_this_storefront_has_no_release()
    {
        Assert.Null(AppStoreVersionLookup.Parse("""{"resultCount":0,"results":[]}""", "com.ishaunted.ios"));
    }

    [Fact]
    public async Task Asks_Apple_by_bundle_id_once_an_hour_not_once_a_phone()
    {
        var (lookup, apple) = Build(() => Json(RealLookup));

        var first = await lookup.LatestAsync(default);
        var second = await lookup.LatestAsync(default);

        Assert.Equal("1.1.2", first?.Version);
        Assert.Equal("1.1.2", second?.Version);
        Assert.Equal(1, apple.Calls);
        Assert.Contains("bundleId=com.ishaunted.ios", apple.LastUri!.Query);
    }

    [Fact]
    public async Task Apple_unavailable_is_no_release_rather_than_an_error()
    {
        var (lookup, _) = Build(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        Assert.Null(await lookup.LatestAsync(default));
    }

    [Fact]
    public async Task A_network_failure_is_no_release_rather_than_an_error()
    {
        var (lookup, _) = Build(() => throw new HttpRequestException("no route"));

        Assert.Null(await lookup.LatestAsync(default));
    }

    [Fact]
    public async Task The_endpoint_answers_with_the_live_release()
    {
        var (lookup, _) = Build(() => Json(RealLookup));

        var result = await new PublicAppVersionController(lookup).Ios(default);

        var record = Assert.IsType<AppVersionRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("ios", record.Platform);
        Assert.Equal("1.1.2", record.LatestVersion);
        Assert.StartsWith("https://apps.apple.com/", record.StoreUrl);
    }

    [Fact]
    public async Task The_endpoint_says_in_a_sentence_when_it_cannot_check()
    {
        var (lookup, _) = Build(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await new PublicAppVersionController(lookup).Ios(default);

        var refusal = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(503, refusal.StatusCode);
        Assert.Contains("Couldn't reach the App Store", refusal.Value as string);
    }
}
