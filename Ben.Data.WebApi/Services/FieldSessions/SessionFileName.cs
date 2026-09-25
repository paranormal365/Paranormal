namespace Ben.Data.WebApi.Services.FieldSessions;

/// <summary>
/// What a session is called as a file.
/// </summary>
/// <remarks>
/// <para>
/// The same rule the phone uses when it seals a bundle (<c>DeviceDataExporter.bundleName</c>):
/// the date first so a list sorts into the order the nights happened, written year-first because
/// that is the one spelling nobody misreads, then the operator's own words.
/// </para>
/// <para>
/// Here as well as there because a session uploaded before bundles existed has no such name — its
/// stored file is literally called <c>data.json</c>, as is every other one, which is exactly what
/// a list of them looked like. Ben, 2026-09-16: "I see a bunch of data.json files. I should see
/// one row per session and it should just be the name of the file .ben". A session is one thing
/// whatever shape it arrived in, so it gets one name; what differs is whether that name is also
/// the name of a file on disk, and the list says which.
/// </para>
/// </remarks>
public static class SessionFileName
{
    /// <summary>Long enough for a real description of a room, short enough to stay a file name.</summary>
    private const int MaxLabelLength = 60;

    private static readonly char[] Forbidden = ['/', '\\', ':', '?', '%', '*', '|', '"', '<', '>', '\0'];

    /// <summary>
    /// The name to show for a session — its bundle's own name when it has one, and the name it
    /// would have had when it does not.
    /// </summary>
    public static string For(bool isBundle, string? storedFileName, DateTime startedAt, string? label)
    {
        // A bundle already carries the name the phone gave it, which may be one a person renamed.
        // Recomputing it would quietly replace what they chose.
        if (isBundle && storedFileName is { Length: > 0 } stored
            && stored.EndsWith(BenBundle.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return stored;
        }
        return Build(startedAt, label);
    }

    public static string Build(DateTime startedAt, string? label)
    {
        var stamp = startedAt.ToString("yyyy-MM-dd HHmm",
                                       System.Globalization.CultureInfo.InvariantCulture);
        return $"{stamp} {Clean(label) ?? "Field session"}{BenBundle.FileExtension}";
    }

    /// <summary>The operator's words, made safe to be a file name — or null if nothing is left.</summary>
    private static string? Clean(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;

        var stripped = new string(label.Select(c => Forbidden.Contains(c) ? ' ' : c).ToArray());
        var words = string.Join(' ', stripped.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var trimmed = words.Length > MaxLabelLength
            ? words[..MaxLabelLength].TrimEnd()
            : words;
        return trimmed.Length == 0 ? null : trimmed;
    }
}
