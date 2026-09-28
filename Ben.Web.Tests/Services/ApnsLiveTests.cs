using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Services.Push;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Asks Apple, for real, whether it accepts a push signed with the actual APNs key (item 252) — on
/// both of its services, because the key was made for both.
/// </summary>
/// <remarks>
/// A made-up device token cannot be delivered to, and that is the point: Apple answers
/// <c>BadDeviceToken</c> only <i>after</i> it has accepted the provider token. A wrong key, team or
/// key id answers <c>InvalidProviderToken</c> (403) instead, which the sender reports as a plain
/// failure. Opt-in through the environment, because it needs the key file and the network:
/// <c>BEN_APNS_TEAM_ID</c>, <c>BEN_APNS_KEY_ID</c>, <c>BEN_APNS_KEY_PATH</c>.
/// </remarks>
public class ApnsLiveTests
{
    private static readonly string? TeamId  = Environment.GetEnvironmentVariable("BEN_APNS_TEAM_ID");
    private static readonly string? KeyId   = Environment.GetEnvironmentVariable("BEN_APNS_KEY_ID");
    private static readonly string? KeyPath = Environment.GetEnvironmentVariable("BEN_APNS_KEY_PATH");

    private static bool Configured => TeamId is { Length: > 0 } && KeyId is { Length: > 0 } && File.Exists(KeyPath ?? "");

    [SkippableTheory]
    [InlineData(PushEnvironment.Sandbox)]
    [InlineData(PushEnvironment.Production)]
    public async Task AppleAcceptsOurKeyAndRefusesOnlyTheMadeUpPhone(PushEnvironment environment)
    {
        Skip.IfNot(Configured, "Set BEN_APNS_TEAM_ID, BEN_APNS_KEY_ID and BEN_APNS_KEY_PATH to ask Apple for real.");

        using var credentials = new ApnsCredentials(
            new ApnsOptions(TeamId!, KeyId!, File.ReadAllText(KeyPath!), "com.ishaunted.ios"));
        var sender = new ApnsPushSender(new HttpClient(), credentials, NullLogger<ApnsPushSender>.Instance);

        var outcome = await sender.SendAsync(new string('0', 64), environment,
            new PushMessage("Test", "Nobody receives this.", new Dictionary<string, string>()), default);

        // The phone was the problem; the key was not.
        Assert.Equal(PushOutcome.Unregistered, outcome);
    }
}
