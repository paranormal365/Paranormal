using Ben.Service.RepositoryService.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Asks Apple's Maps Server API for real, with the actual Maps key. Opt-in through the
/// environment: <c>BEN_APPLE_TEAM_ID</c>, <c>BEN_APPLE_MAPS_KEY_ID</c>, <c>BEN_APPLE_MAPS_KEY_PATH</c>.
/// Two service calls against the daily quota.
/// </summary>
public class AppleMapsLiveTests
{
    private static readonly string? TeamId  = Environment.GetEnvironmentVariable("BEN_APPLE_TEAM_ID");
    private static readonly string? KeyId   = Environment.GetEnvironmentVariable("BEN_APPLE_MAPS_KEY_ID");
    private static readonly string? KeyPath = Environment.GetEnvironmentVariable("BEN_APPLE_MAPS_KEY_PATH");
    private static bool Configured => TeamId is { Length: > 0 } && KeyId is { Length: > 0 } && File.Exists(KeyPath ?? "");

    [SkippableFact]
    public async Task AppleGeocodesNashvilleAndNamesItBack()
    {
        Skip.IfNot(Configured, "Set BEN_APPLE_TEAM_ID, BEN_APPLE_MAPS_KEY_ID and BEN_APPLE_MAPS_KEY_PATH to ask Apple for real.");
        var geocoder = new AppleMapsGeocoder(TeamId!, KeyId!, File.ReadAllText(KeyPath!));

        var forward = await geocoder.ResolveFromQueryAsync("Nashville, TN", default);
        Assert.InRange(forward.Latitude ?? 0, 36.0m, 36.4m);
        Assert.InRange(forward.Longitude ?? 0, -87.0m, -86.5m);

        var reverse = await geocoder.ReverseAsync((double)forward.Latitude!, (double)forward.Longitude!, default);
        Assert.Equal("Nashville", reverse.City);
        Assert.Equal("TN", reverse.State);
    }
}
