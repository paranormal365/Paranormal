using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The product description is styled by a GLOBAL stylesheet (storefront S2.4/S2.9).
/// </summary>
/// <remarks>
/// The description is HTML an admin wrote, rendered as markup. A component's scoped stylesheet
/// (<c>*.razor.css</c>) only reaches elements that component's own markup drew, so a scoped prose
/// class styles none of it — the publications reader's <c>ben-article-body</c> is exactly that, and
/// borrowing it would leave every paragraph jammed together. This reads the class the product page
/// puts on the description and requires a rule for it in a stylesheet that is not scoped.
/// </remarks>
public sealed class StoreProductProseClassIsGlobalTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir!.FullName, "Ben.slnx"))) dir = dir.Parent;
        return dir.FullName;
    }

    [Fact]
    public void The_description_class_has_a_rule_outside_any_scoped_stylesheet()
    {
        var page = File.ReadAllText(Path.Combine(Root(), "Ben.Web.Website.Library", "Store", "Shared", "StoreProductView.razor"));
        var element = Regex.Match(page, "<div class=\"([^\"]+)\" data-testid=\"product-description\">");
        Assert.True(element.Success, "StoreProductView.razor no longer marks its description with data-testid=\"product-description\".");
        var classes = element.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var global = new[] { "Ben.Web.Website.Library", "Ben.Web.Website" }
            .Select(p => Path.Combine(Root(), p))
            .SelectMany(d => Directory.EnumerateFiles(d, "*.css", SearchOption.AllDirectories))
            .Where(f => !f.EndsWith(".razor.css", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText)
            .ToList();

        var styled = classes.Where(c => global.Any(css => Regex.IsMatch(css, $@"\.{Regex.Escape(c)}\s+p\b"))).ToList();
        Assert.True(styled.Count > 0,
            $"The description's classes ({string.Join(", ", classes)}) have no global rule for its paragraphs, so the "
          + "admin's HTML is drawn unstyled. Use ben-store-description (ben-store.css), not a scoped class.");
    }
}
