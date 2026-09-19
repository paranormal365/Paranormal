namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// The zones a screen offers when something has to happen somewhere.
/// </summary>
/// <remarks>
/// <para>A short list rather than the several hundred the platform knows: every screen that asks
/// this question is asking a US business which of its own clocks a walk or a meeting runs on, and
/// a list of six is answerable where a list of six hundred is not.</para>
///
/// <para>The server does not validate against this list — it accepts any id the platform resolves
/// — so a business that needs a zone not offered here is not locked out, and adding one is one
/// line. What the list is for is making the ordinary answer a single click.</para>
/// </remarks>
public static class TimeZoneChoices
{
    public static readonly IReadOnlyList<(string Id, string Label)> All =
    [
        ("America/New_York",    "Eastern"),
        ("America/Chicago",     "Central"),
        ("America/Denver",      "Mountain"),
        ("America/Phoenix",     "Arizona (no daylight saving)"),
        ("America/Los_Angeles", "Pacific"),
        ("America/Anchorage",   "Alaska"),
        ("Pacific/Honolulu",    "Hawaii"),
    ];

    /// <summary>What to call a zone on screen, falling back to its own id.</summary>
    public static string Label(string? id) =>
        All.FirstOrDefault(z => z.Id == id).Label ?? id ?? "";
}
