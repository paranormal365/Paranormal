using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// Every copy of the "on sale" rule names the category's parent (storefront subcategories, 09/24).
/// </summary>
/// <remarks>
/// The rule — variant, product, category and the category's parent all active — lives once in
/// StoreCatalogue.LiveProducts, but the cart, the checkout, stock and a few admin answers need it
/// inside their own queries. A copy that checks the category and forgets the parent sells a product
/// the admin hid by hiding its parent shelf. So wherever a store query reads a category's IsActive,
/// the parent must be read within the same few lines.
/// </remarks>
public sealed class StoreSellableRuleNamesTheParentTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir!.FullName, "Ben.slnx"))) dir = dir.Parent;
        return dir.FullName;
    }

    private static readonly string[] Folders =
    [
        "Ben.Data.WebApi/Services/Store", "Ben.Data.WebApi/Controllers/Store",
        "Ben.Data.WebApi/Controllers/Public", "Ben.Data.WebApi/Controllers/Admin/Store",
    ];

    internal static IEnumerable<string> Offences(string file, string source)
        => Regex.Matches(source, @"(?<!Parent)Category!?\.IsActive")
            .Where(m => !source.Substring(m.Index, Math.Min(300, source.Length - m.Index)).Contains("ParentCategory"))
            .Select(m => $"{file}:{source[..m.Index].Count(c => c == '\n') + 1}");

    [Fact]
    public void Every_category_active_check_also_checks_the_parent()
    {
        var offences = Folders.Select(f => Path.Combine(Root(), f)).Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .SelectMany(f => Offences(Path.GetFileName(f), File.ReadAllText(f)))
            .ToList();

        Assert.True(offences.Count == 0,
            "These read a category's IsActive without its parent's — a product under a hidden parent would still sell:\n  "
          + string.Join("\n  ", offences));
    }

    [Fact]
    public void The_scan_sees_a_forgotten_parent()
    {
        Assert.Single(Offences("x.cs", "q.Where(p => p.IsActive && p.Category.IsActive && p.Variants.Any())"));
        Assert.Empty(Offences("x.cs", "q.Where(p => p.Category.IsActive\n && (p.Category.ParentCategoryId == null || p.Category.ParentCategory!.IsActive))"));
    }
}
