using Ben.Data.Common.Helpers;

namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// Every zone a person can say they live in, in the order somebody looks for theirs (2026-09-28).
/// </summary>
/// <remarks>
/// <para><b>The short list first.</b> <see cref="TimeZoneChoices"/> — the seven US clocks — is what
/// nearly everybody on this site answers with, so it leads. Everything the platform knows follows,
/// grouped by region, because "people choose their timezone" includes the member in Dublin.</para>
///
/// <para><b>Offsets are today's.</b> "UTC−5" in July is "UTC−6" in December; the label says what the
/// clock reads now, which is what somebody compares against their watch.</para>
/// </remarks>
public static class TimeZoneCatalog
{
    public sealed record Choice(string Id, string Label);
    public sealed record Group(string Name, IReadOnlyList<Choice> Choices);

    private static readonly Lazy<IReadOnlyList<Group>> _groups = new(Build);

    public static IReadOnlyList<Group> Groups => _groups.Value;

    /// <summary>What to call <paramref name="id"/> on screen, whether or not it is in a group.</summary>
    public static string Label(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "";
        foreach (var g in Groups)
            foreach (var c in g.Choices)
                if (c.Id == id) return c.Label;
        return id;
    }

    private static IReadOnlyList<Group> Build()
    {
        var now = DateTime.UtcNow;
        var us = TimeZoneChoices.All
            .Select(z => new Choice(z.Id, $"{z.Label} — {City(z.Id)} ({Offset(z.Id, now)})"))
            .ToList();
        var listed = us.Select(c => c.Id).ToHashSet();

        var rest = TimeZoneInfo.GetSystemTimeZones()
            .Select(Zones.IanaId)
            .Where(id => id.Contains('/') && !id.StartsWith("Etc/", StringComparison.Ordinal) && !listed.Contains(id))
            .Distinct()
            .Select(id => (Id: id, Region: id[..id.IndexOf('/')], Offset: Zones.Find(id).GetUtcOffset(now)))
            .GroupBy(z => z.Region)
            .OrderBy(g => g.Key)
            .Select(g => new Group(g.Key, g
                .OrderBy(z => z.Offset).ThenBy(z => City(z.Id))
                .Select(z => new Choice(z.Id, $"{City(z.Id)} ({Offset(z.Id, now)})"))
                .ToList()));

        return [new Group("United States", us), .. rest];
    }

    /// <summary>"America/Argentina/Buenos_Aires" → "Buenos Aires".</summary>
    private static string City(string id) => id[(id.LastIndexOf('/') + 1)..].Replace('_', ' ');

    private static string Offset(string id, DateTime utc)
    {
        var offset = Zones.Find(id).GetUtcOffset(utc);
        var sign = offset < TimeSpan.Zero ? "−" : "+";
        var abs = offset.Duration();
        return abs == TimeSpan.Zero ? "UTC"
            : abs.Minutes == 0 ? $"UTC{sign}{abs.Hours}" : $"UTC{sign}{abs.Hours}:{abs.Minutes:00}";
    }
}
