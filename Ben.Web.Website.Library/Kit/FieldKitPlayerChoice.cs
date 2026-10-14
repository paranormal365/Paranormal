using Microsoft.Extensions.Configuration;

namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// Which session player a page shows: the engine player (the browser keeps time, 10/14/2026) unless
/// <c>FieldKit:Player</c> is set to <c>legacy</c>, the way back to the first player for one release.
/// </summary>
public static class FieldKitPlayerChoice
{
    public static bool UseLegacy(IConfiguration configuration)
        => string.Equals(configuration["FieldKit:Player"], "legacy", StringComparison.OrdinalIgnoreCase);
}
