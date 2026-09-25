using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// Every store admin endpoint has a call in the website's client (storefront S1.9).
/// </summary>
/// <remarks>
/// <para><c>EveryApiRouteHasACallerTests</c> needs one literal per controller prefix, so a
/// controller whose list is called and whose <c>activate</c> is not passes it — and an endpoint
/// nothing calls is a door no screen can open. This one reads every action's template.</para>
///
/// <para>Templates are matched with their parameters as wildcards:
/// <c>{id:guid}/variants/{variantId:guid}/stock</c> is satisfied by
/// <c>$"/api/admin/store/products/{productId}/variants/{variantId}/stock"</c>.</para>
/// </remarks>
public sealed class StoreClientRoutesTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return dir!.FullName;
    }

    internal static IEnumerable<(string Controller, string Verb, string Route)> AdminStoreRoutes()
    {
        var folder = Path.Combine(Root(), "Ben.Data.WebApi", "Controllers", "Admin", "Store");
        foreach (var file in Directory.EnumerateFiles(folder, "*.cs"))
        {
            var source = File.ReadAllText(file);
            var prefix = Regex.Match(source, @"\[Route\(""([^""]+)""\)\]").Groups[1].Value.Trim('/');
            foreach (Match m in Regex.Matches(source, @"\[Http(Get|Post|Put|Delete)(?:\(""([^""]*)""\))?\]"))
            {
                var tail = m.Groups[2].Value;
                yield return (Path.GetFileNameWithoutExtension(file), m.Groups[1].Value.ToUpperInvariant(),
                              tail.Length == 0 ? prefix : $"{prefix}/{tail}");
            }
        }
    }

    [Fact]
    public void The_guard_reads_every_store_admin_controller()
    {
        var controllers = AdminStoreRoutes().Select(r => r.Controller).Distinct().Order().ToList();
        Assert.Equal([
            "AdminStoreCategoryController", "AdminStoreCouponController", "AdminStoreDashboardController",
            "AdminStoreOrderController", "AdminStoreProductController", "AdminStoreReviewController", "AdminStoreSettingsController",
            "AdminStoreStockController",
        ], controllers);
        Assert.True(AdminStoreRoutes().Count() >= 40, "Far fewer actions than the store has — the pattern is reading the wrong thing.");
    }

    [Fact]
    public void Every_store_admin_action_is_called_by_the_client()
    {
        var client = File.ReadAllText(Path.Combine(Root(), "Ben.Web.Services", "WebApi", "BenAdminClientAdapter.StoreAdmin.cs"));

        var uncalled = AdminStoreRoutes()
            .Where(r => !Regex.IsMatch(client, "\"/" + Pattern(r.Route) + @"(\?[^""]*)?"""))
            .Select(r => $"{r.Verb} {r.Route}  ({r.Controller})")
            .ToList();

        Assert.True(uncalled.Count == 0,
            "These store admin endpoints have no call in BenAdminClientAdapter.StoreAdmin.cs, so no "
          + "screen can reach them:\n  " + string.Join("\n  ", uncalled));
    }

    /// <summary>The seller workspace's actions (store sellers, backlog 251), read from Controllers/Seller.</summary>
    internal static IEnumerable<(string Controller, string Verb, string Route)> SellerRoutes()
    {
        var folder = Path.Combine(Root(), "Ben.Data.WebApi", "Controllers", "Seller");
        foreach (var file in Directory.EnumerateFiles(folder, "*.cs"))
        {
            var source = File.ReadAllText(file);
            foreach (Match route in Regex.Matches(source, @"\[Route\(""([^""]+)""\)\]"))
            {
                // A file may hold more than one controller; each action belongs to the Route above it.
                var prefix = route.Groups[1].Value.Trim('/');
                var end = source.IndexOf("[Route(", route.Index + 1, StringComparison.Ordinal);
                var body = source[route.Index..(end < 0 ? source.Length : end)];
                foreach (Match m in Regex.Matches(body, @"\[Http(Get|Post|Put|Delete)(?:\(""([^""]*)""\))?\]"))
                    yield return (Path.GetFileNameWithoutExtension(file), m.Groups[1].Value.ToUpperInvariant(),
                                  m.Groups[2].Value.Length == 0 ? prefix : $"{prefix}/{m.Groups[2].Value}");
            }
        }
    }

    [Fact]
    public void Every_seller_action_is_called_by_the_seller_client()
    {
        var client = File.ReadAllText(Path.Combine(Root(), "Ben.Web.Services", "WebApi", "BenAdminClientAdapter.StoreSeller.cs"));
        Assert.NotEmpty(SellerRoutes());

        var uncalled = SellerRoutes()
            .Where(r => !Regex.IsMatch(client, "\"/" + Pattern(r.Route) + @"(\?[^""]*)?"""))
            .Select(r => $"{r.Verb} {r.Route}  ({r.Controller})")
            .ToList();

        Assert.True(uncalled.Count == 0,
            "These seller endpoints have no call in BenAdminClientAdapter.StoreSeller.cs, so no "
          + "screen can reach them:\n  " + string.Join("\n  ", uncalled));
    }

    /// <summary>The public store's actions, read from its controller.</summary>
    internal static IEnumerable<(string Verb, string Route)> PublicStoreRoutes()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "Ben.Data.WebApi", "Controllers", "Public", "PublicStoreController.cs"));
        var prefix = Regex.Match(source, @"\[Route\(""([^""]+)""\)\]").Groups[1].Value.Trim('/');
        foreach (Match m in Regex.Matches(source, @"\[Http(Get|Post|Put|Delete)(?:\(""([^""]*)""\))?\]"))
            yield return (m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value.Length == 0 ? prefix : $"{prefix}/{m.Groups[2].Value}");
    }

    [Fact]
    public void Every_public_store_action_is_called_by_the_client()
    {
        var client = File.ReadAllText(Path.Combine(Root(), "Ben.Web.Services", "WebApi", "BenAdminClientAdapter.Store.cs"));
        var routes = PublicStoreRoutes().ToList();
        Assert.True(routes.Count >= 8, "Far fewer actions than the public store has — the pattern is reading the wrong thing.");

        var uncalled = routes
            .Where(r => !Regex.IsMatch(client, "\"/" + Pattern(r.Route) + @"(\?[^""]*)?"""))
            .Select(r => $"{r.Verb} {r.Route}").ToList();
        Assert.True(uncalled.Count == 0,
            "These public store endpoints have no call in BenAdminClientAdapter.Store.cs:\n  " + string.Join("\n  ", uncalled));
    }

    /// <summary>A route template as a regex over a C# string literal or interpolation.</summary>
    private static string Pattern(string route)
        => string.Join("/", route.Split('/').Select(segment =>
               segment.StartsWith('{') ? @"\{[^}]+\}" : Regex.Escape(segment)));
}
