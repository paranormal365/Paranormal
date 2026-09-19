using Ben.Data.WebApi.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The one-minute, one-use codes that carry a session from the site to the standalone editor.
/// </summary>
/// <remarks>
/// A code is a credential for as long as it lives, so the rules that keep it small — one use, one
/// minute, no way to tell a wrong guess from a stale one — are the feature rather than details of
/// it (phase 12).
/// </remarks>
public sealed class EditorHandoffCodeStoreTests
{
    private static readonly Guid User  = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>A clock the test moves by hand.</summary>
    private sealed class Clock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan by) => Now += by;
    }

    private static (EditorHandoffCodeStore Store, Clock Clock) Create()
    {
        var clock = new Clock();
        return (new EditorHandoffCodeStore(() => clock.Now), clock);
    }

    [Fact]
    public void A_code_redeems_to_the_account_it_was_issued_for()
    {
        var (store, _) = Create();

        var code = store.Issue(User);

        Assert.Equal(User, store.Redeem(code));
    }

    [Fact]
    public void Two_accounts_get_two_codes()
    {
        var (store, _) = Create();

        var mine   = store.Issue(User);
        var theirs = store.Issue(Other);

        Assert.NotEqual(mine, theirs);
        Assert.Equal(Other, store.Redeem(theirs));
        Assert.Equal(User,  store.Redeem(mine));
    }

    /// <summary>
    /// One use. A code that has opened a session cannot open a second one, so a link left in a
    /// chat window or a browser history is worth nothing to whoever finds it.
    /// </summary>
    [Fact]
    public void A_code_works_once()
    {
        var (store, _) = Create();
        var code = store.Issue(User);

        Assert.Equal(User, store.Redeem(code));
        Assert.Null(store.Redeem(code));
    }

    [Fact]
    public void A_code_dies_a_minute_after_it_is_issued()
    {
        var (store, clock) = Create();
        var code = store.Issue(User);

        clock.Advance(EditorHandoffCodeStore.Lifetime + TimeSpan.FromSeconds(1));

        Assert.Null(store.Redeem(code));
    }

    /// <summary>
    /// The window has to survive a slow page load and a cold WebAssembly start.
    /// </summary>
    [Fact]
    public void A_code_still_works_most_of_the_way_through_its_minute()
    {
        var (store, clock) = Create();
        var code = store.Issue(User);

        clock.Advance(EditorHandoffCodeStore.Lifetime - TimeSpan.FromSeconds(5));

        Assert.Equal(User, store.Redeem(code));
    }

    /// <summary>
    /// An expired code is spent on the attempt, not left to be tried again later.
    /// </summary>
    [Fact]
    public void An_expired_code_is_gone_after_it_is_tried()
    {
        var (store, clock) = Create();
        var code = store.Issue(User);

        clock.Advance(EditorHandoffCodeStore.Lifetime * 2);
        store.Redeem(code);

        Assert.Equal(0, store.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not-a-code")]
    public void Nonsense_redeems_to_nothing(string? code)
    {
        var (store, _) = Create();
        store.Issue(User);

        Assert.Null(store.Redeem(code));
    }

    /// <summary>
    /// Codes are compared exactly. Anything looser turns a 256-bit secret into a smaller one.
    /// </summary>
    [Fact]
    public void A_code_in_the_wrong_case_is_not_the_code()
    {
        var (store, _) = Create();
        var code = store.Issue(User);

        // Base64url mixes case, so flipping it is always a different string.
        var flipped = new string(code.Select(c =>
            char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)).ToArray());

        if (flipped == code) return;   // vanishingly unlikely; nothing to assert

        Assert.Null(store.Redeem(flipped));
    }

    /// <summary>
    /// A code carries enough randomness to be worthless to a guesser, and no padding or slashes
    /// that a URL would have to escape.
    /// </summary>
    [Fact]
    public void A_code_is_long_and_url_safe()
    {
        var (store, _) = Create();

        var code = store.Issue(User);

        Assert.True(code.Length >= 40, $"A {code.Length}-character code is too short to be a secret.");
        Assert.DoesNotContain('+', code);
        Assert.DoesNotContain('/', code);
        Assert.DoesNotContain('=', code);
        Assert.Equal(code, Uri.EscapeDataString(code));
    }

    [Fact]
    public void Every_code_is_different()
    {
        var (store, _) = Create();

        var codes = Enumerable.Range(0, 50).Select(_ => store.Issue(User)).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(50, codes.Count);
    }

    /// <summary>
    /// Nothing else removes an unredeemed code, and a dictionary that only grows is a slow leak in
    /// a process that runs for months.
    /// </summary>
    [Fact]
    public void Codes_nobody_used_do_not_pile_up_forever()
    {
        var (store, clock) = Create();

        for (var i = 0; i < 10; i++) store.Issue(User);
        Assert.Equal(10, store.Count);

        clock.Advance(EditorHandoffCodeStore.Lifetime * 2);
        store.Issue(User);   // the sweep runs here

        Assert.Equal(1, store.Count);
    }

    /// <summary>
    /// Redeeming one code leaves every other one alone — two tabs, two links, two sessions.
    /// </summary>
    [Fact]
    public void Redeeming_one_code_does_not_spend_another()
    {
        var (store, _) = Create();
        var first  = store.Issue(User);
        var second = store.Issue(User);

        store.Redeem(first);

        Assert.Equal(User, store.Redeem(second));
    }
}
