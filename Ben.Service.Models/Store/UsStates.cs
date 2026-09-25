namespace Ben.Service.Models.Store;

/// <summary>
/// The places the store ships to: the fifty states and the District of Columbia (storefront).
/// </summary>
/// <remarks>
/// Ship-to is US only in v1. Territories and military addresses (PR, GU, AA/AE/AP) are left out on
/// purpose — flat-rate shipping and Stripe Tax registrations were decided for the fifty states and
/// DC, and an address the store cannot price must be refused at the form, not at the carrier.
/// </remarks>
public static class UsStates
{
    public static readonly IReadOnlyList<(string Code, string Name)> All =
    [
        ("AL", "Alabama"), ("AK", "Alaska"), ("AZ", "Arizona"), ("AR", "Arkansas"), ("CA", "California"),
        ("CO", "Colorado"), ("CT", "Connecticut"), ("DE", "Delaware"), ("DC", "District of Columbia"),
        ("FL", "Florida"), ("GA", "Georgia"), ("HI", "Hawaii"), ("ID", "Idaho"), ("IL", "Illinois"),
        ("IN", "Indiana"), ("IA", "Iowa"), ("KS", "Kansas"), ("KY", "Kentucky"), ("LA", "Louisiana"),
        ("ME", "Maine"), ("MD", "Maryland"), ("MA", "Massachusetts"), ("MI", "Michigan"), ("MN", "Minnesota"),
        ("MS", "Mississippi"), ("MO", "Missouri"), ("MT", "Montana"), ("NE", "Nebraska"), ("NV", "Nevada"),
        ("NH", "New Hampshire"), ("NJ", "New Jersey"), ("NM", "New Mexico"), ("NY", "New York"),
        ("NC", "North Carolina"), ("ND", "North Dakota"), ("OH", "Ohio"), ("OK", "Oklahoma"), ("OR", "Oregon"),
        ("PA", "Pennsylvania"), ("RI", "Rhode Island"), ("SC", "South Carolina"), ("SD", "South Dakota"),
        ("TN", "Tennessee"), ("TX", "Texas"), ("UT", "Utah"), ("VT", "Vermont"), ("VA", "Virginia"),
        ("WA", "Washington"), ("WV", "West Virginia"), ("WI", "Wisconsin"), ("WY", "Wyoming"),
    ];

    private static readonly Dictionary<string, string> ByCode =
        All.ToDictionary(s => s.Code, s => s.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The upper-case code for a code typed in any case; null when it is not one we ship to.</summary>
    public static string? Normalize(string? code)
    {
        var trimmed = code?.Trim();
        return trimmed is { Length: 2 } && ByCode.ContainsKey(trimmed) ? trimmed.ToUpperInvariant() : null;
    }

    public static bool IsValid(string? code) => Normalize(code) is not null;

    public static string? NameOf(string? code) => Normalize(code) is { } c ? ByCode[c] : null;
}
