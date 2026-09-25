using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Until the store is switched on, no menu shows a way into it (Ben, 2026-09-25).
/// </summary>
/// <remarks>
/// <para>The store's pages already answer 404 with <c>features.store</c> off; a menu entry that
/// still showed would announce a shop that is not open and lead to a dead end. The seller's
/// "Selling" menu was the one that did — gated on the Seller role alone.</para>
///
/// <para><b>One exception, on purpose:</b> "My Orders" for somebody who already has an order, so a
/// buyer's money is never out of reach (Ben's dark-launch rule of 09/24). The admin "Store" menu
/// is the back office, where the shop is set up before it opens, and lives under
/// <c>/admin/store</c>, which this does not cover.</para>
/// </remarks>
public sealed class StoreMenusStayDarkTests
{
    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, Path.Combine(parts)));
    }

    private static string WithoutComments(string source)
    {
        source = Regex.Replace(source, @"@\*.*?\*@", " ", RegexOptions.Singleline);
        source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(source, @"//[^\n]*", " ");
    }

    [Fact]
    public void Every_menu_link_into_the_shop_is_behind_the_store_switch()
    {
        var lines = WithoutComments(Read("Ben.Web.Website", "Components", "Layout", "BenNav.razor")).Split('\n');
        var ungated = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            if (!Regex.IsMatch(lines[i], @"""/store(/|"")")) continue;

            // The condition this entry is drawn under: the nearest `if (` or `else if (` above it.
            var guard = Enumerable.Range(0, i).Reverse().Select(j => lines[j].Trim())
                .FirstOrDefault(l => l.StartsWith("if (") || l.StartsWith("else if (")) ?? "";

            var theOrdersException = guard.StartsWith("else if (_hasStoreOrders)") && lines[i].Contains("\"/store/orders\"");
            if (!guard.Contains("SiteFeatures.Store") && !theOrdersException)
                ungated.Add($"line {i + 1}: {lines[i].Trim()}   (drawn under: {guard})");
        }

        Assert.True(ungated.Count == 0,
            "These menu entries lead into the shop without checking the store switch:\n" + string.Join("\n", ungated));
    }

    [Theory]
    [InlineData("BenCartButton.razor")]
    [InlineData("BenFavouritesButton.razor")]
    public void The_headers_store_buttons_draw_nothing_while_the_store_is_off(string file)
    {
        var markup = WithoutComments(Read("Ben.Web.Website", "Components", "Layout", file));
        var gate = markup.IndexOf("@if (Features.IsOn(SiteFeatures.Store))", StringComparison.Ordinal);
        var firstLink = markup.IndexOf("<a ", StringComparison.Ordinal);
        var firstButton = markup.IndexOf("<button", StringComparison.Ordinal);
        var firstDrawn = new[] { firstLink, firstButton }.Where(x => x >= 0).DefaultIfEmpty(int.MaxValue).Min();

        Assert.True(gate >= 0 && gate < firstDrawn, $"{file} draws something before checking the store switch.");
    }
}
