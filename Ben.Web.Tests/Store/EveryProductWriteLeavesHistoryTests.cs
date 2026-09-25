using System.Reflection;
using System.Text.RegularExpressions;
using Ben.Data.WebApi.Controllers.Admin.Store;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// Every endpoint that changes a store item leaves a line in its history (store sellers, backlog
/// 251, P2) — the admin's product and stock controllers, and every seller product controller.
/// </summary>
/// <remarks>
/// <para>Ben, 09/24/2026: "history records who changed what". A new write that forgot its line
/// would compile, pass its own tests and quietly make the history a partial one, which is worse
/// than none: it reads as complete.</para>
///
/// <para>Source reads, like the other structural guards. A write passes when its body calls
/// <c>StoreProductHistory.</c> itself, or a private helper of the same controller that does.</para>
/// </remarks>
public sealed class EveryProductWriteLeavesHistoryTests
{
    /// <summary>Writes that leave no line, and why.</summary>
    private static readonly Dictionary<string, string> Exempt = new()
    {
        [$"{nameof(AdminStoreProductController)}.{nameof(AdminStoreProductController.Delete)}"] =
            "only a never-sold item can be deleted, and its history goes with it (the audit log keeps the deletion)",
    };

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static IEnumerable<(Type Controller, string File)> ItemControllers()
    {
        var api = Path.Combine(Root(), "Ben.Data.WebApi", "Controllers");
        yield return (typeof(AdminStoreProductController), Path.Combine(api, "Admin", "Store", "AdminStoreProductController.cs"));
        yield return (typeof(AdminStoreStockController), Path.Combine(api, "Admin", "Store", "AdminStoreStockController.cs"));
        foreach (var seller in typeof(AdminStoreProductController).Assembly.GetTypes()
                     .Where(t => t.Namespace == "Ben.Data.WebApi.Controllers.Seller" && !t.IsAbstract
                                 && t.Name.StartsWith("SellerStoreProduct", StringComparison.Ordinal) && t.Name.EndsWith("Controller")))
            yield return (seller, Path.Combine(api, "Seller", seller.Name + ".cs"));
    }

    /// <summary>A method's text: from its signature to the brace that closes it at method depth.</summary>
    private static string? Body(string source, string method)
    {
        var start = Regex.Match(source, $@"^    (public|private|internal)[^\n(]*\b{Regex.Escape(method)}\(", RegexOptions.Multiline);
        if (!start.Success) return null;
        var end = Regex.Match(source[start.Index..], @"^    }\s*$", RegexOptions.Multiline);
        return end.Success ? source.Substring(start.Index, end.Index + end.Length) : source[start.Index..];
    }

    [Fact]
    public void Every_item_write_leaves_a_history_line()
    {
        var missing = new List<string>();
        var checkedWrites = 0;

        foreach (var (controller, file) in ItemControllers())
        {
            var source = File.ReadAllText(file);
            // Helpers of this controller that write a line, so a write calling one of them counts.
            var helpers = controller.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Select(m => m.Name).Distinct()
                .Where(name => Body(source, name) is { } body && body.Contains("StoreProductHistory.", StringComparison.Ordinal))
                .ToList();

            foreach (var write in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                         .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any(a => !a.HttpMethods.Contains("GET"))))
            {
                var key = $"{controller.Name}.{write.Name}";
                if (Exempt.ContainsKey(key)) continue;
                checkedWrites++;
                var body = Body(source, write.Name);
                Assert.True(body is not null, $"Could not find {key} in {Path.GetFileName(file)} — has the guard's reader fallen behind the code?");
                if (!body!.Contains("StoreProductHistory.", StringComparison.Ordinal) && !helpers.Any(h => body.Contains(h + "(", StringComparison.Ordinal)))
                    missing.Add(key);
            }
        }

        Assert.True(checkedWrites >= 16, $"Only {checkedWrites} item writes were found; the guard is no longer reading what it should.");
        Assert.True(missing.Count == 0, "These item writes leave no history line: " + string.Join(", ", missing)
            + ". Record the change with StoreProductHistory in the same save, or add it to Exempt with the reason.");
    }

    [Fact]
    public void No_item_controller_adjusts_stock_around_the_history()
    {
        // StoreStock.AdjustManyAsync saves as it goes, so a controller calling it directly makes a
        // stock change with no line. StoreProductHistory.AdjustStockAsync wraps it with one.
        foreach (var (_, file) in ItemControllers())
            Assert.DoesNotMatch(@"StoreStock\.AdjustManyAsync", File.ReadAllText(file));
    }

    [Fact]
    public void The_exemptions_name_writes_that_exist()
    {
        var writes = ItemControllers().SelectMany(c => c.Controller.GetMethods().Select(m => $"{c.Controller.Name}.{m.Name}")).ToHashSet();
        Assert.All(Exempt.Keys, key => Assert.Contains(key, writes));
    }
}
