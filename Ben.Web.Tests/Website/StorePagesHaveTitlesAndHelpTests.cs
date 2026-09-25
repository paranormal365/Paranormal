using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Every store page, the shop's and the back office's, names itself in the browser tab and links
/// to the help that explains it; the cart, checkout and thank-you pages each mark their step
/// (storefront S7.3).
/// </summary>
/// <remarks>
/// <para><b>Titles.</b> A page without <c>&lt;PageTitle&gt;</c> keeps whatever the last page
/// set, so a buyer's tab says "My cart — Store" while they read their invoice.</para>
///
/// <para><b>Help links.</b> The help documents are written for these pages; a page with no
/// <c>HelpLink</c> hides them from the one person looking at the thing they explain. A page may
/// draw its link through a component it renders — the product page's is in
/// <c>StoreProductView</c>, which the editor's preview shares — so a component named in the page
/// counts, one level deep.</para>
///
/// <para>Source reads, like the other page guards: whether the markup is there is a fact about
/// the file. The directive is matched as <c>@page "</c> at a line's start, because the reviews
/// block's <c>@pages</c> is not a route.</para>
/// </remarks>
public sealed class StorePagesHaveTitlesAndHelpTests
{
    private static readonly string Library = Path.Combine(Root(), "Ben.Web.Website.Library");

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static readonly Regex Route = new(@"^@page\s+""", RegexOptions.Multiline);

    /// <summary>The routable pages under Store/ and SuperAdmin/Store/, relative to the library.</summary>
    private static List<string> PageFiles() =>
        new[] { "Store", Path.Combine("SuperAdmin", "Store") }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(Library, folder), "*.razor", SearchOption.AllDirectories))
            .Where(f => Route.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(Library, f)).Order().ToList();

    public static TheoryData<string> Pages()
    {
        var data = new TheoryData<string>();
        foreach (var page in PageFiles()) data.Add(page);
        return data;
    }

    private static string WithoutComments(string source) => Regex.Replace(source, @"@\*.*?\*@", " ", RegexOptions.Singleline);

    [Fact]
    public void The_store_has_the_pages_this_guard_expects()
    {
        // A guard that finds nothing passes; this one must at least see the shop and the desk.
        var pages = PageFiles();
        Assert.True(pages.Count >= 20, $"Only {pages.Count} store pages found — has the folder moved?");
        Assert.Contains(pages, p => p.EndsWith("StoreFavourites.razor"));
        Assert.Contains(pages, p => p.EndsWith("AdminStoreOrderDetail.razor"));
        Assert.DoesNotContain(pages, p => p.EndsWith("StoreReviewsBlock.razor"));
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void Every_store_page_names_itself(string page)
    {
        var source = WithoutComments(File.ReadAllText(Path.Combine(Library, page)));
        Assert.True(Regex.IsMatch(source, @"<PageTitle>[^<]*\S[^<]*</PageTitle>"),
            $"{page} has no <PageTitle> — the browser tab keeps the previous page's name.");
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void Every_store_page_links_to_its_help(string page)
    {
        var source = WithoutComments(File.ReadAllText(Path.Combine(Library, page)));
        if (source.Contains("<HelpLink", StringComparison.Ordinal)) return;

        // One level down: a component this page draws, found by its tag, that carries the link.
        var drawn = Regex.Matches(source, @"<(Store\w+|AdminStore\w+)\b").Select(m => m.Groups[1].Value).Distinct();
        var carriers = drawn
            .Select(name => Directory.EnumerateFiles(Library, name + ".razor", SearchOption.AllDirectories).FirstOrDefault())
            .Where(file => file is not null && WithoutComments(File.ReadAllText(file!)).Contains("<HelpLink", StringComparison.Ordinal))
            .ToList();
        Assert.True(carriers.Count > 0,
            $"{page} renders no HelpLink, itself or through a component it draws — its help is out of reach from it.");
    }

    [Theory]
    [InlineData("Store/Cart/StoreCartPage.razor", "StoreCheckoutStep.Cart")]
    [InlineData("Store/Checkout/StoreCheckoutPage.razor", "Current=\"Step\"")]
    [InlineData("Store/Checkout/StoreCheckoutCompletePage.razor", "StoreCheckoutStep.Complete")]
    public void The_buying_pages_mark_their_step(string page, string marks)
    {
        var source = WithoutComments(File.ReadAllText(Path.Combine(Library, page)));
        var steps = Regex.Match(source, @"<StoreCheckoutSteps\b[^>]*>");
        Assert.True(steps.Success, $"{page} no longer draws the steps Cart → Place order → Payment → Complete.");
        Assert.Contains(marks, steps.Value);
    }
}
