using Ben.Data.Common.Enums;
using Ben.Web.Services.WebApi;
using Xunit;

namespace Ben.Data.WebApi.Client.Tests;

/// <summary>
/// What a client may honestly say about the address <c>GET api/me</c> reports.
/// </summary>
public class MeResponseTests
{
    private static MeResponse Me(EmailAddressKind kind, bool confirmed = true) =>
        new(Guid.NewGuid(), "someone@example.test", false, false, false, kind, confirmed);

    [Fact]
    public void AnOrdinaryConfirmedAddressIsTheirOwnAndReachable()
    {
        var me = Me(EmailAddressKind.Ordinary);
        Assert.True(me.EmailIsTheirOwn);
        Assert.True(me.CanBeEmailed);
    }

    /// <summary>
    /// An account created from a provider's unverified address claim holds an address it has
    /// not proved it can read. It is still the person's own — they typed it somewhere — but
    /// nothing except the confirmation link should be sent to it.
    /// </summary>
    [Fact]
    public void AnUnconfirmedAddressIsTheirOwnButNotReachable()
    {
        var me = Me(EmailAddressKind.Ordinary, confirmed: false);
        Assert.True(me.EmailIsTheirOwn);
        Assert.False(me.CanBeEmailed);
    }

    [Theory]
    [InlineData(EmailAddressKind.AppleRelay)]
    [InlineData(EmailAddressKind.Unreachable)]
    public void ARelayOrPlaceholderIsNeitherTheirOwnNorReachable(EmailAddressKind kind)
    {
        var me = Me(kind);
        Assert.False(me.EmailIsTheirOwn);
        Assert.False(me.CanBeEmailed);
    }

    /// <summary>An older server that does not send the flag reads as it always did: confirmed.</summary>
    [Fact]
    public void TheFlagDefaultsToConfirmedForAServerThatDoesNotSendIt()
    {
        var me = new MeResponse(Guid.NewGuid(), "a@b.test", false, false);
        Assert.True(me.EmailConfirmed);
        Assert.True(me.CanBeEmailed);
    }
}
