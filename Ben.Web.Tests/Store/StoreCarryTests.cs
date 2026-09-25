using Ben.Web.Website.Library.Store.Shared;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>A store page carries its data into the live page only while it fits the connection (StoreCarry).</summary>
public sealed class StoreCarryTests
{
    private sealed record Page(IReadOnlyList<string> Rows);

    [Fact]
    public void A_small_page_is_carried_and_a_large_one_is_not()
    {
        var small = new Page(Enumerable.Range(0, 10).Select(i => $"shelf {i}").ToList());
        var large = new Page(Enumerable.Range(0, 3_000).Select(i => $"a shelf with a long enough name {i}").ToList());

        Assert.Same(small, StoreCarry.IfItFits(small));
        Assert.Null(StoreCarry.IfItFits(large));
        Assert.Null(StoreCarry.IfItFits<Page>(null));
    }
}
