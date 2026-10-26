using Ben.Service.Models.Entities;

namespace Ben.Web.Website.Library.Manage.Audio;

/// <summary>
/// The sentence a clip or an edit is saved with: which format it went in, how big it is, and what
/// the other format would have cost (phase 4b, 10/26/2026).
/// </summary>
/// <remarks>
/// Ben asked for the size difference to be shown and for the reassurance to be true. Both formats
/// keep every sample, so "nothing is lost" is said of FLAC, and nothing is said about MP3, which
/// is never offered.
/// </remarks>
public static class AudioSaveSummary
{
    /// <summary>What to tell the person about <paramref name="saved"/>, given what they asked for.</summary>
    public static string For(UploadFileRecord saved, AudioSaveFormat asked)
    {
        var size = AudioFormatUtils.FormatSizeCompact(saved.FileSize);

        if (saved.ContentType == "audio/flac")
            return saved.WavEquivalentBytes is { } wav && wav > saved.FileSize
                ? $"Saved as FLAC, {size} (as WAV it would be {AudioFormatUtils.FormatSizeCompact(wav)}). "
                  + "Nothing is lost: it plays back exactly the same."
                : $"Saved as FLAC, {size}. Nothing is lost: it plays back exactly the same.";

        return asked == AudioSaveFormat.Flac
            ? $"Saved as WAV, {size}. This server cannot save FLAC yet, so it kept WAV, which loses nothing either."
            : $"Saved as WAV, {size}.";
    }
}
