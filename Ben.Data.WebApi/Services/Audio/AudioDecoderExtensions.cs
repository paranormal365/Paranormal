namespace Ben.Data.WebApi.Services.Audio;

/// <summary>Getting any recording into the shape the edits and the mixer read.</summary>
public static class AudioDecoderExtensions
{
    /// <summary>
    /// <paramref name="source"/> as MP3 or WAV: as it is when it already is one, otherwise (the
    /// phone's .m4a, a FLAC clip) decoded to WAV at its own rate and channels.
    /// </summary>
    /// <remarks>
    /// <para>The edit length limit is checked from the header before anything is decoded, with the
    /// sentence a long MP3 gets, so an hour of .m4a is refused before a gigabyte of WAV is built.</para>
    ///
    /// <para>Takes ownership of <paramref name="source"/>: the stream returned is either it or its
    /// replacement, and the caller disposes that one. A stream that cannot seek is buffered first,
    /// because the length and the samples are two separate reads.</para>
    /// </remarks>
    /// <exception cref="AudioTooLargeException">Longer than the edit limit.</exception>
    /// <exception cref="AudioDecodeFailedException">Its length or its samples could not be read.</exception>
    public static async Task<(Stream Stream, string ContentType)> AsReadableAsync(
        this IAudioDecoder decoder, Stream source, string contentType, string? fileName, CancellationToken ct)
    {
        if (AudioSourceReader.Reads(contentType)) return (source, contentType);

        await using (source)
        {
            var seekable = source;
            if (!source.CanSeek)
            {
                seekable = new MemoryStream();
                await source.CopyToAsync(seekable, ct);
            }

            await using (seekable)
            {
                var seconds = await decoder.DurationSecondsAsync(seekable, contentType, fileName, ct)
                    ?? throw new AudioDecodeFailedException("its length could not be read.");
                AudioSourceReader.EnsureEditable(TimeSpan.FromSeconds(seconds));

                var wav = await decoder.DecodeRangeToWavAsync(seekable, contentType, fileName, 0, seconds, ct);
                return (new MemoryStream(wav), "audio/wav");
            }
        }
    }
}
