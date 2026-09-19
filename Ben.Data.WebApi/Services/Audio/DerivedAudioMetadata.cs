using Ben.Data.Source.Entities;
using NAudio.Wave;

namespace Ben.Data.WebApi.Services.Audio;

/// <summary>
/// The metadata row a derived audio file gets, measured from the bytes that were just produced.
/// </summary>
/// <remarks>
/// <para>Derived audio carried no duration at all. <c>DeriveMetadataAsync</c> copies where and when
/// a recording was captured from the source's metadata row, and returns <c>null</c> when the source
/// has none — which is every audio upload, because nothing extracts duration for audio on any path.
/// So an edited file had no duration, no sample rate and no channel count, and the mixer, which
/// draws a clip's width from its duration, drew every clip the same width (2026-09-06 audio walk,
/// finding 11).</para>
///
/// <para>This fills in the part that can be measured. The inherited part — capture time, GPS, the
/// device — still comes from the source when the source has it, and is simply absent when it does
/// not. A row that says "44.1 kHz, stereo, 12.4 seconds" and nothing about where it was recorded is
/// honest; no row at all is not.</para>
/// </remarks>
internal static class DerivedAudioMetadata
{
    /// <summary>
    /// Returns the metadata row to add for <paramref name="derivedUploadFileId"/>, measuring
    /// duration and format from <paramref name="producedBytes"/>.
    /// </summary>
    /// <param name="inherited">
    /// What <c>IMediaIngestService.DeriveMetadataAsync</c> carried over from the source, or null
    /// when the source had nothing to carry.
    /// </param>
    /// <param name="durationOverride">
    /// Used instead of the produced file's own length where the caller knows better — a clip's
    /// requested range, which is what the person asked for even when the bytes are a hair shorter.
    /// </param>
    public static UploadFileMetadata? For(
        Guid derivedUploadFileId, byte[] producedBytes, UploadFileMetadata? inherited,
        double? durationOverride = null)
    {
        var measured = Measure(producedBytes);
        if (measured is null && inherited is null) return null;

        var row = inherited ?? new UploadFileMetadata
        {
            Id             = Guid.NewGuid(),
            UploadFileId   = derivedUploadFileId,
            MediaKind      = "Audio",
            ExtractedAtUtc = DateTime.UtcNow,
        };

        if (measured is { } m)
        {
            row.DurationSeconds = durationOverride ?? m.Seconds;
            row.SampleRateHz    = m.SampleRate;
            row.Channels        = m.Channels;
            row.AudioCodec      = "PCM";
        }
        else if (durationOverride is { } given)
        {
            row.DurationSeconds = given;
        }

        return row;
    }

    /// <summary>
    /// Reads the WAV header of what was just written. Null when it cannot be read at all, which
    /// would mean the edit produced something no decoder accepts — recorded as "no measurement"
    /// rather than as a failed request, since the bytes are already the caller's result.
    /// </summary>
    private static (double Seconds, int SampleRate, int Channels)? Measure(byte[] wavBytes)
    {
        try
        {
            using var ms     = new MemoryStream(wavBytes);
            using var reader = new WaveFileReader(ms);
            return (reader.TotalTime.TotalSeconds, reader.WaveFormat.SampleRate, reader.WaveFormat.Channels);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or EndOfStreamException or ArgumentException)
        {
            return null;
        }
    }
}
