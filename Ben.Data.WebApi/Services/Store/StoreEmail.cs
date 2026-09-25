namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// The one way the store compares email addresses (storefront).
/// </summary>
/// <remarks>
/// Trimmed and upper-cased, the way ASP.NET Identity normalises <c>AppUsers.NormalizedEmail</c> —
/// so "Sarah@Example.com " at checkout, the per-buyer coupon cap and the order-lookup page all
/// agree that it is the same buyer, and a guest order can be matched to an account made later.
/// </remarks>
public static class StoreEmail
{
    public static string Normalize(string? email) => (email ?? string.Empty).Trim().ToUpperInvariant();
}
