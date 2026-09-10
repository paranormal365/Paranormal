using Ben.Web.Website.Services;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Carrying an Apple identity token from the endpoint Apple posts to, to the page that finishes
/// the sign-in.
/// </summary>
/// <remarks>
/// It holds a live bearer credential for a couple of minutes, so the rules about spending and
/// expiring it are the whole point rather than housekeeping.
/// </remarks>
public class AppleSignInHandoffTests
{
    [Fact]
    public void AStashedTokenComesBackOnce()
    {
        var store = new AppleSignInHandoff();
        var code = store.Stash("the.id.token", "Ada Lovelace");

        var first = store.Redeem(code);

        Assert.NotNull(first);
        Assert.Equal("the.id.token", first!.IdentityToken);
        Assert.Equal("Ada Lovelace", first.DisplayName);
        Assert.Null(first.AuthorizationCode);
    }

    /// <summary>The authorization code crosses with the token, for the API to exchange (item 229).</summary>
    [Fact]
    public void TheAuthorizationCodeCrossesWithTheToken()
    {
        var store = new AppleSignInHandoff();
        var code = store.Stash("the.id.token", null, "c.abc");

        Assert.Equal("c.abc", store.Redeem(code)!.AuthorizationCode);
    }

    /// <summary>
    /// Once, and only once.
    /// </summary>
    /// <remarks>
    /// A code that survives being spent is a credential somebody else can spend. The redirect it
    /// travels in lands in browser history, so a second use is not hypothetical.
    /// </remarks>
    [Fact]
    public void ASpentCodeIsDead()
    {
        var store = new AppleSignInHandoff();
        var code = store.Stash("t", null);

        Assert.NotNull(store.Redeem(code));
        Assert.Null(store.Redeem(code));
    }

    [Fact]
    public void AnExpiredCodeIsDead()
    {
        var start = DateTimeOffset.UtcNow;
        var now = start;
        var store = new AppleSignInHandoff(() => now);

        var code = store.Stash("t", null);
        now = start.Add(AppleSignInHandoff.Lifetime).AddSeconds(1);

        Assert.Null(store.Redeem(code));
    }

    [Fact]
    public void ACodeInsideItsLifetimeStillWorks()
    {
        var start = DateTimeOffset.UtcNow;
        var now = start;
        var store = new AppleSignInHandoff(() => now);

        var code = store.Stash("t", null);
        now = start.Add(AppleSignInHandoff.Lifetime).AddSeconds(-1);

        Assert.NotNull(store.Redeem(code));
    }

    /// <summary>Unknown, spent and expired answer identically, so nothing reveals which it was.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("deadbeef")]
    public void AnUnknownCodeAnswersNothing(string? code)
        => Assert.Null(new AppleSignInHandoff().Redeem(code));

    /// <summary>Two sign-ins in flight do not collide.</summary>
    [Fact]
    public void EveryStashGetsItsOwnCode()
    {
        var store = new AppleSignInHandoff();
        var codes = Enumerable.Range(0, 100).Select(i => store.Stash($"t{i}", null)).ToHashSet();

        Assert.Equal(100, codes.Count);
    }

    /// <summary>
    /// An abandoned sign-in does not sit in memory holding a token.
    /// </summary>
    /// <remarks>
    /// Most of these are never redeemed: somebody changes their mind on Apple's page, or closes the
    /// tab. Without a sweep the process accumulates live credentials for as long as it runs.
    /// </remarks>
    [Fact]
    public void AbandonedCodesAreSweptAway()
    {
        var start = DateTimeOffset.UtcNow;
        var now = start;
        var store = new AppleSignInHandoff(() => now);

        for (var i = 0; i < 10; i++) store.Stash($"t{i}", null);
        Assert.Equal(10, store.Count);

        now = start.Add(AppleSignInHandoff.Lifetime).AddSeconds(1);
        store.Stash("fresh", null);   // any use sweeps

        Assert.Equal(1, store.Count);
    }
}
