namespace Ben.Data.Common.Helpers;

/// <summary>
/// Matches a file extension against an <see cref="Ben.Data.Source.Entities.UploadFileTypeExtension"/> pattern.
///
/// Pattern rules (patterns are stored lowercase):
///   • Exact  — ".txt"  matches only ".txt"
///   • Suffix wildcard — ".tx*"  matches any extension whose lowercase value starts with ".tx"
///     (e.g. ".txa", ".txb", ".txzzzz")
///   • The wildcard character '*' is only supported as the final character of a pattern.
///     Interior or leading wildcards are treated as literals.
///
/// Comparison is always case-insensitive.
/// </summary>
public static class FileExtensionPatternMatcher
{
    /// <summary>
    /// Returns <c>true</c> if <paramref name="fileExtension"/> is matched by
    /// <paramref name="pattern"/>.
    /// </summary>
    /// <param name="pattern">
    ///   An extension pattern, e.g. ".txt", ".tx*".  May include or omit the leading dot.
    /// </param>
    /// <param name="fileExtension">
    ///   The actual file extension to test, e.g. ".txt" or "txt".
    /// </param>
    public static bool Matches(string pattern, string fileExtension)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(fileExtension))
            return false;

        var p = pattern.Trim().ToLowerInvariant();
        var ext = fileExtension.Trim().ToLowerInvariant();

        // Normalise: ensure both start with a dot
        if (!p.StartsWith('.')) p = "." + p;
        if (!ext.StartsWith('.')) ext = "." + ext;

        if (p.EndsWith('*'))
        {
            // Suffix-wildcard match: ".tx*" matches anything that starts with ".tx"
            var prefix = p[..^1]; // everything before the '*'
            return ext.StartsWith(prefix, StringComparison.Ordinal);
        }

        return ext == p;
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="fileExtension"/> is permitted by the given
    /// collection of patterns.
    /// </summary>
    /// <param name="patterns">The allowed pattern strings (from UploadFileTypeExtension.Pattern).</param>
    /// <param name="fileExtension">The actual file extension to test.</param>
    public static bool IsAllowedByPatterns(IEnumerable<string> patterns, string fileExtension)
    {
        foreach (var pattern in patterns)
        {
            if (Matches(pattern, fileExtension))
                return true;
        }
        return false;
    }
}
