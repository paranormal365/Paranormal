using System.Reflection;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// Every store controller a shopper can reach is behind the store switch, or named here with the
/// reason it is not (storefront S2.2).
/// </summary>
/// <remarks>
/// <para>Controllers are discovered, not listed: anything in the public or store namespaces whose
/// name starts <c>Store</c> or <c>PublicStore</c>. A new one is either gated or added below with a
/// reason, so the switch cannot quietly stop hiding part of the shop.</para>
///
/// <para>The exceptions are the doors a buyer's money depends on — an order page, a picture of
/// what was bought — which must answer whether the shop is showing or not. S3 adds the cart, S4 the
/// checkout and the order doors, S6 the engagement controller.</para>
/// </remarks>
public sealed class StoreControllersAreGatedTests
{
    private static readonly Dictionary<string, string> NotGated = new()
    {
        ["StoreOrderController"] =
            "The order doors: a buyer who has been charged must reach the thank-you page, the emailed link and the "
          + "invoice whatever the switch says, and the webhook still fulfils with the shop hidden.",
        [nameof(PublicStoreImageController)] =
            "Pictures serve while the shop is dark: the catalogue is entered and previewed before anybody can see it, "
          + "and an order page shows what was bought whether the shop is open or not.",
    };

    private static IEnumerable<Type> StoreControllers()
        => typeof(PublicStoreController).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.Name.EndsWith("Controller", StringComparison.Ordinal))
            .Where(t => t.Namespace is "Ben.Data.WebApi.Controllers.Public" or "Ben.Data.WebApi.Controllers.Store")
            .Where(t => t.Name.StartsWith("Store", StringComparison.Ordinal) || t.Name.StartsWith("PublicStore", StringComparison.Ordinal));

    [Fact]
    public void The_discovery_finds_the_store_controllers()
    {
        Assert.Contains(StoreControllers(), t => t == typeof(PublicStoreController));
        Assert.Contains(StoreControllers(), t => t == typeof(Ben.Data.WebApi.Controllers.Store.StoreCartController));
        Assert.Contains(StoreControllers(), t => t == typeof(Ben.Data.WebApi.Controllers.Store.StoreCheckoutController));
        Assert.Contains(StoreControllers(), t => t == typeof(Ben.Data.WebApi.Controllers.Store.StoreOrderController));
    }

    [Fact]
    public void Every_store_controller_is_behind_the_switch_or_says_why_not()
    {
        var ungated = StoreControllers()
            .Where(t => t.GetCustomAttribute<FeatureGatedAttribute>()?.FeatureKey != SiteSettingKeys.FeatureStore)
            .Where(t => !NotGated.ContainsKey(t.Name))
            .Select(t => t.Name).ToList();

        Assert.True(ungated.Count == 0,
            "These store controllers answer with the store switched off:\n  " + string.Join("\n  ", ungated)
          + "\nGate them with [FeatureGated(SiteSettingKeys.FeatureStore)], or name them in NotGated with the reason.");
    }

    [Fact]
    public void Every_exception_is_a_real_ungated_controller()
    {
        var found = StoreControllers().ToDictionary(t => t.Name);
        Assert.All(NotGated.Keys, name =>
        {
            Assert.True(found.ContainsKey(name), $"{name} is excused but no longer exists.");
            Assert.Null(found[name].GetCustomAttribute<FeatureGatedAttribute>());
        });
    }

    [Fact]
    public void The_admin_store_controllers_are_superadmin_only_and_never_gated()
    {
        var admin = typeof(PublicStoreController).Assembly.GetTypes()
            .Where(t => t.Namespace == "Ben.Data.WebApi.Controllers.Admin.Store" && t.Name.EndsWith("Controller", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(7, admin.Count);
        Assert.All(admin, t =>
        {
            Assert.Equal(Ben.Data.Common.Constants.RoleNames.SuperAdmin, t.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
            Assert.Null(t.GetCustomAttribute<FeatureGatedAttribute>());
        });
    }
}
