using System.Reflection;
using System.Text.RegularExpressions;
using Ben.Data.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The Selling workspace stays a seller's own (store sellers, backlog 251): every seller endpoint
/// takes the Seller policy and starts from the caller's items, and every seller page can reach
/// only the seller client.
/// </summary>
/// <remarks>
/// <para>Ben, 09/24/2026: "a seller only has control over their individual items in the store,
/// not the admin part or pricing." The server check is the boundary; the page check keeps a
/// seller page from growing an admin control that the server would then have to refuse.</para>
///
/// <para>Source reads, like the other structural guards. A seller controller that forgot its
/// policy would still compile and still pass every test written against it by a seller.</para>
/// </remarks>
public sealed class SellerWorkspaceGuardTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static IReadOnlyList<Type> SellerControllers() => typeof(Ben.Data.WebApi.Controllers.Seller.SellerStoreControllerBase).Assembly
        .GetTypes()
        .Where(t => t.Namespace == "Ben.Data.WebApi.Controllers.Seller" && !t.IsAbstract && t.Name.EndsWith("Controller"))
        .ToList();

    [Fact]
    public void Every_seller_controller_takes_the_Seller_policy_and_starts_from_the_callers_items()
    {
        var controllers = SellerControllers();
        Assert.NotEmpty(controllers);

        foreach (var controller in controllers)
        {
            var policy = controller.GetCustomAttribute<AuthorizeAttribute>()?.Policy;
            Assert.True(policy == AuthPolicyNames.Seller, $"{controller.Name} is not behind the Seller policy (it has '{policy}').");
            Assert.True(typeof(Ben.Data.WebApi.Controllers.Seller.SellerStoreControllerBase).IsAssignableFrom(controller),
                $"{controller.Name} does not derive from SellerStoreControllerBase, so it has no Mine() to start from.");
            Assert.Null(controller.GetCustomAttribute<Ben.Data.WebApi.Services.FeatureGatedAttribute>());
        }
    }

    [Fact]
    public void No_seller_controller_reads_products_except_through_Mine()
    {
        // db.StoreProducts is every product; Mine(db) is the caller's. A seller endpoint that
        // reaches for the whole table has stepped outside its seller.
        var folder = Path.Combine(Root(), "Ben.Data.WebApi", "Controllers", "Seller");
        var offenders = Directory.EnumerateFiles(folder, "*.cs")
            .Where(f => Path.GetFileName(f) != "SellerStoreControllerBase.cs" && Regex.IsMatch(File.ReadAllText(f), @"db\.StoreProducts\b"))
            .Select(Path.GetFileName).ToList();
        Assert.Contains("db.StoreProducts", File.ReadAllText(Path.Combine(folder, "SellerStoreControllerBase.cs")));
        Assert.True(offenders.Count == 0, "Seller controllers read db.StoreProducts directly: " + string.Join(", ", offenders)
            + " — start from Mine(db), which is the one place that reads it.");
    }

    [Fact]
    public void Seller_pages_inject_only_the_seller_client()
    {
        var folder = Path.Combine(Root(), "Ben.Web.Website.Library", "Store", "Selling");
        var pages = Directory.EnumerateFiles(folder, "*.razor", SearchOption.AllDirectories).ToList();
        Assert.NotEmpty(pages);

        foreach (var page in pages)
        {
            var source = File.ReadAllText(page);
            Assert.DoesNotMatch(@"@inject\s+(Ben\.Web\.Services\.)?IBen(Admin|StoreAdmin)Client\b", source);
        }
    }
}
