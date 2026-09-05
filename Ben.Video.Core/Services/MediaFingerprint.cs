namespace Ben.Video.Editor.Services;

/// <summary>
/// Whether a file the editor just fetched is the file the project was saved against.
/// </summary>
/// <remarks>
/// <para>Re-fetching a clip's media from the server is only safe if the editor can tell that what
/// came back is the same thing. A file can be replaced on the server between saving a project and
/// opening it, and silently editing against different footage is worse than saying the media is
/// missing (2026-09-05 audit, F14).</para>
///
/// <para>Two checks, deliberately unequal. Size is always recorded and costs nothing. A hash is
/// stronger and is <b>not</b> always available: the browser's digest has no streaming form, so
/// hashing means holding the whole file in memory, and session footage here runs to hundreds of
/// megabytes. Above <see cref="MaximumHashableBytes"/> no hash is taken and none is expected.</para>
///
/// <para>Pure, because "is this the right file" is the question whose wrong answer replaces
/// somebody's footage.</para>
/// </remarks>
public static class MediaFingerprint
{
    /// <summary>
    /// The largest file worth hashing, in bytes.
    /// </summary>
    /// <remarks>
    /// 64 MB. Hashing that much takes well under a second and holds one copy in memory; the 538 MB
    /// clips this editor is expected to handle would cost seconds and a second full copy of the
    /// file, on the one thread the whole editor runs on.
    /// </remarks>
    public const long MaximumHashableBytes = 64L * 1024 * 1024;

    /// <summary>Whether a file of this size should have a hash taken.</summary>
    public static bool ShouldHash(long sizeBytes) =>
        sizeBytes > 0 && sizeBytes <= MaximumHashableBytes;

    /// <summary>
    /// What a comparison concluded.
    /// </summary>
    public enum Verdict
    {
        /// <summary>Everything recorded about the file matches.</summary>
        Matches,

        /// <summary>Nothing was recorded to compare against.</summary>
        Unknown,

        /// <summary>Something recorded about the file does not match.</summary>
        Differs,
    }

    /// <summary>
    /// Compares what was recorded when the project was saved with what came back now.
    /// </summary>
    /// <param name="expectedSize">The size recorded on the clip, or null.</param>
    /// <param name="expectedHash">The hash recorded on the clip, or null.</param>
    /// <param name="actualSize">The size of the file now in hand.</param>
    /// <param name="actualHash">Its hash, when one was taken.</param>
    /// <remarks>
    /// <para>A missing hash on either side is not a mismatch. The project may predate hashing, the
    /// file may be above the ceiling, or the caller may have chosen not to pay for one — and
    /// treating "not taken" as "did not match" would refuse perfectly good media.</para>
    ///
    /// <para><see cref="Verdict.Unknown"/> is separate from <see cref="Verdict.Matches"/> on
    /// purpose, so the caller can decide what an unverifiable file deserves rather than being told
    /// it is fine.</para>
    /// </remarks>
    public static Verdict Compare(
        long? expectedSize, string? expectedHash, long? actualSize, string? actualHash)
    {
        var compared = false;

        if (expectedSize is { } wantSize && actualSize is { } gotSize)
        {
            if (wantSize != gotSize) return Verdict.Differs;
            compared = true;
        }

        if (!string.IsNullOrEmpty(expectedHash) && !string.IsNullOrEmpty(actualHash))
        {
            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                return Verdict.Differs;
            compared = true;
        }

        return compared ? Verdict.Matches : Verdict.Unknown;
    }
}
