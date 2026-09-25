using System.Reflection;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// Every write a shopper can make carries one of the four store limits (storefront S3.3).
/// </summary>
/// <remarks>
/// <para>Discovered, not listed: every POST, PUT, PATCH and DELETE on a store controller a shopper
/// can reach (the same discovery as <see cref="StoreControllersAreGatedTests"/>). The policy is the
/// action's own, or its class's.</para>
///
/// <para><b>Exactly the four store policies.</b> They are keyed by account or visitor address,
/// never by a value the caller sends; a borrowed policy — the hosted-booking one, say — would put
/// the store under somebody else's numbers, and changing those for their own reasons would quietly
/// change the store's.</para>
/// </remarks>
public sealed class StorePublicWritesAreRateLimitedTests
{
    private static readonly HashSet<string> StorePolicies = new(RateLimiting.StorePoliciesPerMinute.Keys);

    private static IEnumerable<(Type Controller, MethodInfo Action)> Writes()
        => typeof(PublicStoreController).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.Name.EndsWith("Controller", StringComparison.Ordinal))
            .Where(t => t.Namespace is "Ben.Data.WebApi.Controllers.Public" or "Ben.Data.WebApi.Controllers.Store")
            .Where(t => t.Name.StartsWith("Store", StringComparison.Ordinal) || t.Name.StartsWith("PublicStore", StringComparison.Ordinal))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>()
                    .Any(h => h.HttpMethods.Any(v => v is "POST" or "PUT" or "PATCH" or "DELETE")))
                .Select(m => (t, m)));

    [Fact]
    public void The_discovery_finds_the_cart_writes()
        => Assert.Contains(Writes(), w => w.Controller.Name == "StoreCartController" && w.Action.Name == "Add");

    [Fact]
    public void Every_store_write_carries_a_store_limit()
    {
        var wrong = Writes()
            .Select(w => (w.Controller, w.Action,
                Policy: (w.Action.GetCustomAttribute<EnableRateLimitingAttribute>()
                         ?? w.Controller.GetCustomAttribute<EnableRateLimitingAttribute>())?.PolicyName))
            .Where(w => w.Policy is null || !StorePolicies.Contains(w.Policy))
            .Select(w => $"{w.Controller.Name}.{w.Action.Name} → {w.Policy ?? "(none)"}")
            .ToList();

        Assert.True(wrong.Count == 0,
            "These store writes do not carry one of the four store limits:\n  " + string.Join("\n  ", wrong));
    }
}
