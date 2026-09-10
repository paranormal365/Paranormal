using Ben.Data.WebApi.Services.Apple;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Asks Apple, for real, whether it accepts a client secret signed with the actual key.
/// </summary>
/// <remarks>
/// A bogus authorization code cannot succeed, and that is the point: Apple answers
/// <c>invalid_grant</c> for a bad code only <i>after</i> it has accepted the client secret. A
/// wrong key, team, key id or signature encoding answers <c>invalid_client</c> instead. Opt-in
/// through the environment, because it needs the private key file and the network:
/// <c>BEN_APPLE_TEAM_ID</c>, <c>BEN_APPLE_KEY_ID</c>, <c>BEN_APPLE_KEY_PATH</c>.
/// </remarks>
public class AppleTokenLiveTests
{
    private static readonly string? TeamId  = Environment.GetEnvironmentVariable("BEN_APPLE_TEAM_ID");
    private static readonly string? KeyId   = Environment.GetEnvironmentVariable("BEN_APPLE_KEY_ID");
    private static readonly string? KeyPath = Environment.GetEnvironmentVariable("BEN_APPLE_KEY_PATH");

    private static bool Configured => TeamId is { Length: > 0 } && KeyId is { Length: > 0 } && File.Exists(KeyPath ?? "");

    [SkippableFact]
    public async Task AppleAcceptsOurClientSecretAndRefusesOnlyTheCode()
    {
        Skip.IfNot(Configured, "Set BEN_APPLE_TEAM_ID, BEN_APPLE_KEY_ID and BEN_APPLE_KEY_PATH to ask Apple for real.");

        var options = new AppleSigningOptions(TeamId!, KeyId!, File.ReadAllText(KeyPath!));
        using var secret = new AppleClientSecret(options);
        var client = new AppleTokenClient(new HttpClient(), secret, NullLogger<AppleTokenClient>.Instance);

        var result = await client.ExchangeCodeAsync("not-a-real-code", "com.ishaunted.ios", default);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_grant", result.Error);   // the code was the problem; the secret was not
    }
}
