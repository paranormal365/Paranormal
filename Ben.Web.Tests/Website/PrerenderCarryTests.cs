using Ben.Web.Website.Library.Kit;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// A page carries its server-fetched data into its live copy only when it fits the connection's
/// first message; over budget, it carries nothing and loads again (a blink, never a dead page).
/// </summary>
public sealed class PrerenderCarryTests
{
    private sealed record Row(string Name, string Description);

    [Fact]
    public void A_short_list_is_carried()
        => Assert.True(PrerenderCarry.Fits(Enumerable.Range(0, 20).Select(i => new Row($"Event {i}", "A night at the lighthouse.")).ToList()));

    [Fact]
    public void A_long_list_is_not_carried()
        => Assert.False(PrerenderCarry.Fits(Enumerable.Range(0, 400).Select(i => new Row($"Event {i}", new string('x', 80))).ToList()));

    [Fact]
    public void Nothing_is_not_carried() => Assert.False(PrerenderCarry.Fits<List<Row>?>(null));

    /// <summary>12 KB of JSON is ~16 KB once the page encodes it — half the 32 KB SignalR default.</summary>
    [Fact]
    public void The_budget_leaves_room_in_a_32_KB_message() => Assert.True(PrerenderCarry.MaxBytes * 4 / 3 <= 16 * 1024);
}
