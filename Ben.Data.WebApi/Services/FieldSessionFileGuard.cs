namespace Ben.Data.WebApi.Services;

/// <summary>
/// A session file is one of a handful of kinds the app produces, and its bytes must be that kind.
/// </summary>
/// <remarks>
/// <para>The name says ".m4a" and the request says "audio/mp4"; neither is evidence. The first
/// bytes are: every container the app writes announces itself in its header, and a file that does
/// not is either damaged in a way the digest cannot see (the bytes ARE what was sent) or was never
/// a recording. Both are refused with a sentence rather than stored as a row a player will fail on.</para>
///
/// <para>A simulator's fake-sensor placeholder — two kilobytes of zeros named audio-001.m4a — is
/// the honest test of this rule and is refused by it, which is why the app's fake sensors were
/// changed to write a real header.</para>
/// </remarks>
public static class FieldSessionFileGuard
{
    /// <summary>Generous: a night of video from a phone is a few gigabytes at most.</summary>
    public const long MaxFileBytes = 8L * 1024 * 1024 * 1024;
    public const int MaxFilesPerSession = 500;
    /// <summary>Enough to identify every container below.</summary>
    public const int HeaderBytes = 16;

    private static readonly Dictionary<string, string[]> ContentTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".m4a"] = ["audio/mp4", "audio/m4a", "audio/x-m4a", "audio/aac"],
        [".mp3"] = ["audio/mpeg", "audio/mp3"],
        [".wav"] = ["audio/wav", "audio/x-wav", "audio/wave", "audio/vnd.wave"],
        [".mov"] = ["video/quicktime"],
        [".mp4"] = ["video/mp4"],
        [".jpg"] = ["image/jpeg"],
        [".jpeg"] = ["image/jpeg"],
        [".png"] = ["image/png"],
        [".heic"] = ["image/heic", "image/heif"],
    };

    public static string? Refusal(string relativePath, string? contentType, long length, ReadOnlySpan<byte> header, int filesAlready)
    {
        if (filesAlready >= MaxFilesPerSession)
            return $"a session can carry at most {MaxFilesPerSession} files.";
        if (length > MaxFileBytes)
            return $"that file is {length / (1024.0 * 1024 * 1024):0.#} GB; a session file can be at most {MaxFileBytes / (1024 * 1024 * 1024)} GB.";

        var extension = Path.GetExtension(relativePath);
        if (string.IsNullOrEmpty(extension) || !ContentTypesByExtension.TryGetValue(extension, out var types))
            return $"a session cannot carry a \"{extension}\" file — only the recordings and photos the Field Kit makes.";

        var declared = (contentType ?? "").Split(';')[0].Trim();
        if (declared.Length > 0 && !types.Contains(declared, StringComparer.OrdinalIgnoreCase))
            return $"the file is named {extension} but sent as {declared}.";

        if (!HeaderMatches(extension, header))
            return $"the bytes of {Path.GetFileName(relativePath)} are not a {extension.TrimStart('.').ToUpperInvariant()} file.";

        return null;
    }

    /// <summary>The container signatures, as bytes rather than as anybody's word.</summary>
    public static bool HeaderMatches(string extension, ReadOnlySpan<byte> h)
    {
        switch (extension.ToLowerInvariant())
        {
            case ".m4a": case ".mp4": case ".mov": case ".heic":
                // ISO base media: a 4-byte box size then "ftyp".
                return h.Length >= 8 && h[4] == (byte)'f' && h[5] == (byte)'t' && h[6] == (byte)'y' && h[7] == (byte)'p';
            case ".mp3":
                // An ID3 tag, or a raw MPEG audio frame sync (11 set bits).
                return h.Length >= 3 && ((h[0] == (byte)'I' && h[1] == (byte)'D' && h[2] == (byte)'3')
                                       || (h[0] == 0xFF && (h[1] & 0xE0) == 0xE0));
            case ".wav":
                return h.Length >= 12 && h[0] == (byte)'R' && h[1] == (byte)'I' && h[2] == (byte)'F' && h[3] == (byte)'F'
                                      && h[8] == (byte)'W' && h[9] == (byte)'A' && h[10] == (byte)'V' && h[11] == (byte)'E';
            case ".jpg": case ".jpeg":
                return h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF;
            case ".png":
                return h.Length >= 8 && h[0] == 0x89 && h[1] == (byte)'P' && h[2] == (byte)'N' && h[3] == (byte)'G'
                                     && h[4] == 0x0D && h[5] == 0x0A && h[6] == 0x1A && h[7] == 0x0A;
            default:
                return false;
        }
    }
}
