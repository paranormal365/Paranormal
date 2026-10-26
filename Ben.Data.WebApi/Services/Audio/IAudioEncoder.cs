using Ben.Service.Models.Entities;

namespace Ben.Data.WebApi.Services.Audio;

/// <summary>
/// Saves the WAV a clip or an edit produced as FLAC: the same samples, about half the bytes.
/// </summary>
/// <remarks>
/// ffmpeg does the encoding (<see cref="FfmpegAudioDecoder"/>). Every WAV this server writes is
/// 16-bit PCM, which FLAC holds exactly, so nothing is lost on the way.
/// </remarks>
public interface IAudioEncoder
{
    /// <summary>Whether FLAC can be written here at all.</summary>
    bool CanEncodeFlac { get; }

    /// <summary>The FLAC of <paramref name="wav"/>, or null when ffmpeg could not make it (logged).</summary>
    Task<byte[]?> TryWavToFlacAsync(byte[] wav, CancellationToken ct);
}

/// <summary>The last step of every clip and edit: the format the person asked for, when it can be made.</summary>
public static class AudioSave
{
    /// <summary>
    /// <paramref name="wav"/> as <paramref name="format"/>, with how big it was as WAV when that
    /// differs. When FLAC cannot be made it stays WAV, which loses nothing either, and the content
    /// type says so; the page tells the person.
    /// </summary>
    public static async Task<(byte[] Bytes, string ContentType, string Extension, long? WavBytes)> AsAsync(
        (byte[] Bytes, string ContentType, string Extension) wav, AudioSaveFormat format,
        IAudioEncoder encoder, CancellationToken ct)
    {
        if (format != AudioSaveFormat.Flac || wav.ContentType != "audio/wav" || !encoder.CanEncodeFlac)
            return (wav.Bytes, wav.ContentType, wav.Extension, null);

        return await encoder.TryWavToFlacAsync(wav.Bytes, ct) is { Length: > 0 } flac
            ? (flac, "audio/flac", ".flac", wav.Bytes.LongLength)
            : (wav.Bytes, wav.ContentType, wav.Extension, null);
    }
}
