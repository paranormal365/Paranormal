using System.Net;
using System.Text;
using Ben.Service.RepositoryService.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Geocoding through the Apple Maps Server API (item 230), against responses captured from the
/// real API — see <c>Fixtures/AppleMaps/README.md</c>.
/// </summary>
public class AppleMapsGeocoderTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AppleMaps", name));

    /// <summary>Answers by path: the token endpoint, then whichever lookup fixture the test scripted.</summary>
    private sealed class AppleStub : HttpMessageHandler
    {
        public HttpStatusCode TokenStatus = HttpStatusCode.OK;
        public string LookupFixture = "geocode-address-200.json";
        public HttpStatusCode LookupStatus = HttpStatusCode.OK;
        public int TokenCalls, LookupCalls;
        public readonly List<string> Paths = new();
        public readonly List<string?> Bearers = new();
        public Func<int, HttpStatusCode>? LookupStatusByCall;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.PathAndQuery;
            Paths.Add(path);
            Bearers.Add(request.Headers.Authorization?.Parameter);
            if (path.StartsWith("/v1/token"))
            {
                TokenCalls++;
                return Task.FromResult(new HttpResponseMessage(TokenStatus)
                    { Content = new StringContent(Fixture("token-200.json"), Encoding.UTF8, "application/json") });
            }
            LookupCalls++;
            var status = LookupStatusByCall?.Invoke(LookupCalls) ?? LookupStatus;
            return Task.FromResult(new HttpResponseMessage(status)
                { Content = new StringContent(Fixture(LookupFixture), Encoding.UTF8, "application/json") });
        }
    }

    private static (AppleMapsGeocoder Geocoder, AppleStub Stub) Build(Func<DateTimeOffset>? now = null)
    {
        var stub = new AppleStub();
        var pem = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem();
        return (new AppleMapsGeocoder("5778H75249", "623JTDWHAQ", pem, handler: stub, now: now), stub);
    }

    [Fact]
    public async Task AnAddressResolvesToRooftopCoordinatesThroughAnAccessToken()
    {
        var (geocoder, stub) = Build();

        var result = await geocoder.ResolveAsync("430 Keysburg Rd", null, "Adams", "TN", "37010", "US", default);

        Assert.Equal(36.5907774m, result.Latitude);
        Assert.Equal(-87.0567635m, result.Longitude);
        Assert.Equal("rooftop", result.ResultType);
        Assert.Contains("\"structuredAddress\"", result.RawResponseJson);
        // First the auth token buys an access token; the lookup then carries THAT, not the JWT we signed.
        Assert.Equal(1, stub.TokenCalls);
        Assert.StartsWith("/v1/geocode?q=430%20Keysburg%20Rd%2C%20Adams%2C%20TN%2C%2037010", stub.Paths[1]);
        Assert.Contains("limitToCountries=US", stub.Paths[1]);
        Assert.Equal("REDACTED.access.token", stub.Bearers[1]);
        Assert.NotEqual(stub.Bearers[0], stub.Bearers[1]);
    }

    [Fact]
    public async Task ATownIsAPlaceAndAQueryNeedsNoParts()
    {
        var (geocoder, stub) = Build();
        stub.LookupFixture = "geocode-query-200.json";

        var result = await geocoder.ResolveFromQueryAsync("Nashville, TN", default);

        Assert.Equal(36.1660667m, result.Latitude);
        Assert.Equal("place", result.ResultType);
    }

    /// <summary>Apple says 200 with no results for nonsense; the caller gets nulls and the raw answer, as before.</summary>
    [Fact]
    public async Task NothingFoundIsNullsWithTheRawAnswerNotAFailure()
    {
        var (geocoder, stub) = Build();
        stub.LookupFixture = "geocode-nothing-200.json";

        var result = await geocoder.ResolveFromQueryAsync("zzqx qqzx nowhere 99999", default);

        Assert.Null(result.Latitude);
        Assert.Equal("{\"results\":[]}", result.RawResponseJson);
    }

    [Fact]
    public async Task ReverseReadsTheStructuredAddressIntoTheFormsFields()
    {
        var (geocoder, stub) = Build();
        stub.LookupFixture = "reverse-200.json";

        var result = await geocoder.ReverseAsync(36.1627, -86.7816, default);

        Assert.Equal("600 Church St", result.StreetAddress1);
        Assert.Equal("Nashville", result.City);
        Assert.Equal("TN", result.State);
        Assert.Equal("37219", result.ZipCode);
        Assert.Equal("US", result.Country);
        Assert.True(result.HasData);
        Assert.StartsWith("/v1/reverseGeocode?loc=36.1627%2C-86.7816", stub.Paths[1]);
    }

    /// <summary>One access token serves many lookups; a fresh one is fetched only as it nears expiry.</summary>
    [Fact]
    public async Task TheAccessTokenIsReusedUntilItNearsExpiry()
    {
        var clock = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        var (geocoder, stub) = Build(() => clock);

        await geocoder.ResolveFromQueryAsync("Nashville, TN", default);
        await geocoder.ResolveFromQueryAsync("Franklin, TN", default);
        Assert.Equal(1, stub.TokenCalls);

        clock = clock.AddMinutes(29.5);   // expiresInSeconds is 1800; a minute of slack
        await geocoder.ResolveFromQueryAsync("Adams, TN", default);
        Assert.Equal(2, stub.TokenCalls);
    }

    /// <summary>Apple can revoke early. One 401 buys a fresh token and one more try; two is an answer of nothing.</summary>
    [Fact]
    public async Task A401RefreshesTheTokenOnceAndRetries()
    {
        var (geocoder, stub) = Build();
        stub.LookupStatusByCall = call => call == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK;

        var result = await geocoder.ResolveFromQueryAsync("Nashville, TN", default);

        Assert.NotNull(result.Latitude);
        Assert.Equal(2, stub.TokenCalls);
        Assert.Equal(2, stub.LookupCalls);
    }

    [Fact]
    public async Task ATokenAppleRefusesMeansNothingIsLookedUp()
    {
        var (geocoder, stub) = Build();
        stub.TokenStatus = HttpStatusCode.Unauthorized;

        Assert.Equal(AddressGeocodingService.GeocodingLookupResult.Empty, await geocoder.ResolveFromQueryAsync("Nashville, TN", default));
        Assert.Equal(0, stub.LookupCalls);
    }

    [Fact]
    public async Task AnIncompleteAddressIsNotSentAtAll()
    {
        var (geocoder, stub) = Build();
        Assert.Equal(AddressGeocodingService.GeocodingLookupResult.Empty,
            await geocoder.ResolveAsync("430 Keysburg Rd", null, "", "TN", "37010", "US", default));
        Assert.Empty(stub.Paths);
    }

    [Theory]
    [InlineData("US", "US")]
    [InlineData("us", "US")]
    [InlineData("USA", "US")]
    [InlineData("United States", "US")]
    [InlineData("Canada", null)]
    public void CountriesBecomeTheCodesAppleWants(string given, string? expected) =>
        Assert.Equal(expected, AppleMapsGeocoder.CountryCode(given));

    /// <summary>The static door answers nothing until a geocoder is installed, exactly as before.</summary>
    [Fact]
    public async Task TheStaticDoorIsSilentWhenUnconfigured()
    {
        AddressGeocodingService.Configure(null);
        Assert.False(AddressGeocodingService.IsConfigured);
        Assert.Equal(AddressGeocodingService.GeocodingLookupResult.Empty, await AddressGeocodingService.TryResolveFromQueryAsync("Nashville", default));
        Assert.Equal(AddressGeocodingService.ReverseGeocodingResult.Empty, await AddressGeocodingService.ReverseGeocodeAsync(36, -86, default));
    }
}
