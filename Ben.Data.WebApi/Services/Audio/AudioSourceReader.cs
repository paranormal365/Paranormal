using NAudio.Wave;
using NLayer.NAudioSupport;

namespace Ben.Data.WebApi.Services.Audio;

/// <summary>
/// Opens stored audio for reading, in the one place every server-side audio feature goes through.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> NAudio's <see cref="Mp3FileReader"/> defaults to the ACM codec,
/// which is <c>Msacm32.dll</c> — a Windows system library. On macOS and Linux every MP3 read threw
/// <see cref="DllNotFoundException"/> at construction, so <c>AudioEditor</c>, <c>AudioClipper</c>,
/// <c>AudioMixer</c> and EVP detection all failed on any MP3 with a 500. WAV worked, which is why
/// this went unnoticed.</para>
///
/// <para>The fix is NLayer's fully-managed MP3 frame decompressor, which behaves identically on
/// every platform. Passing it explicitly also removes the hidden dependency on which OS the API
/// happens to be deployed to.</para>
/// </remarks>
internal static class AudioSourceReader
{
    /// <summary>
    /// Opens <paramref name="stream"/> as a <see cref="WaveStream"/>, choosing a reader from
    /// <paramref name="contentType"/>. The caller owns the returned stream.
    /// </summary>
    /// <exception cref="NotSupportedException">The content type isn't a format we decode.</exception>
    public static WaveStream Open(Stream stream, string? contentType)
    {
        if (stream.CanSeek) stream.Position = 0;

        var type = (contentType ?? string.Empty).ToLowerInvariant();

        // Mp3FileReaderBase, not Mp3FileReader: only the base exposes the decompressor-builder
        // overload, and the derived type's constructors always pick the ACM (Windows) decoder.
        if (type.Contains("mpeg") || type.Contains("mp3"))
            return new Mp3FileReaderBase(stream, wf => new Mp3FrameDecompressor(wf));

        if (type.Contains("wav") || type.Contains("wave"))
            return new WaveFileReader(stream);

        // Anything else is rejected rather than guessed at, so callers keep turning it into a
        // 400 with a message the user can act on instead of a decoder-internal failure.
        throw new NotSupportedException(
            $"Audio is supported for WAV and MP3. Received content-type: '{contentType}'.");
    }
}
