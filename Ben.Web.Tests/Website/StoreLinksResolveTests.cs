using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Every store address the store's own pages link to has a page behind it (storefront S2.10).
/// </summary>
/// <remarks>
/// <para>The store is built slice by slice, and the easy mistake is a link that runs ahead of its
/// page — "Place order" before the checkout exists, the dashboard's order tiles before the orders
/// page. A shopper who follows one gets "Page not found" from a button the store drew. So links
/// to a page that is not built yet are left out until it is, and this fails the moment one isn't.</para>
///
/// <para>Read from every <c>/store…</c> and <c>/admin/store…</c> string in the store's pages and
/// components — attribute values, interpolations and navigations alike — with query strings,
/// fragments and interpolation holes set aside, and matched against every <c>@page</c> route.</para>
/// </remarks>
public sealed class StoreLinksResolveTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir!.FullName, "Ben.slnx"))) dir = dir.Parent;
        return dir.FullName;
    }

    private static IEnumerable<string> RazorFiles(params string[] folders)
        => folders.Select(f => Path.Combine(Root(), f)).Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    internal static List<Regex> Routes()
        => RazorFiles("Ben.Web.Website.Library", "Ben.Web.Website")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"^@page\s+""([^""]+)""", RegexOptions.Multiline))
            .Select(m => new Regex("^" + Regex.Replace(Regex.Escape(m.Groups[1].Value.TrimEnd('/')), @"\\\{[^}]*\}", "[^/]+") + "$",
                RegexOptions.IgnoreCase))
            .ToList();

    /// <summary>Store addresses in a piece of markup, with holes, queries and fragments set aside.</summary>
    internal static IEnumerable<string> StoreLinksIn(string source)
        => Regex.Matches(source, @"""(/(?:admin/)?store(?:/[^""\s]*)?)""")
            .Select(m => m.Groups[1].Value)
            .Select(link => Regex.Replace(link, @"\{[^}]*\}", "x"))
            .Select(link => link.Split('?', '#')[0].TrimEnd('/'))
            .Where(link => link.Length > 0)
            .Distinct();

    [Fact]
    public void Every_store_link_lands_on_a_page()
    {
        var routes = Routes();
        var broken = RazorFiles(Path.Combine("Ben.Web.Website.Library", "Store"), Path.Combine("Ben.Web.Website.Library", "SuperAdmin", "Store"))
            .SelectMany(f => StoreLinksIn(Regex.Replace(File.ReadAllText(f), @"@\*.*?\*@", " ", RegexOptions.Singleline))
                .Select(link => (File: Path.GetFileName(f), Link: link)))
            .Where(x => !routes.Any(r => r.IsMatch(x.Link)))
            .Select(x => $"{x.Link}  ({x.File})")
            .ToList();

        Assert.True(broken.Count == 0,
            "These store links have no page behind them yet — a shopper following one gets 'Page not found':\n  "
          + string.Join("\n  ", broken) + "\nLeave the link out until its page exists.");
    }

    [Theory]
    [InlineData("<a href=\"/store/checkout\">Place order</a>", "/store/checkout")]
    [InlineData("<a href=\"@($\"/store/p/{p.Slug}?preview=1\")\">", "/store/p/x")]
    [InlineData("Nav.NavigateTo(\"/admin/store/orders?status=Paid\");", "/admin/store/orders")]
    public void The_scan_reads_links_however_they_are_written(string markup, string expected)
        => Assert.Contains(expected, StoreLinksIn(markup));

    [Fact]
    public void An_unbuilt_page_is_caught_and_a_built_one_is_not()
    {
        var routes = Routes();
        Assert.Contains(routes, r => r.IsMatch("/store/p/x"));
        Assert.DoesNotContain(routes, r => r.IsMatch("/store/nowhere-yet"));
    }
}
