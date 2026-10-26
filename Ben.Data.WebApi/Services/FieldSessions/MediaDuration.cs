using System.Buffers.Binary;
using System.Text;

namespace Ben.Data.WebApi.Services.FieldSessions;

/// <summary>
/// How long a recording runs, read from the file's own header — no ffmpeg.
/// </summary>
/// <remarks>
/// <para><b>Why the server needs this.</b> A clip the phone recorded on its own is noted when it
/// ENDS, so it began its length earlier (rule M4, <c>ProjectNotes/specs/replay-semantics.md</c>).
/// Only a sound clip carries that length in the document; a video does not. The player used to
/// wait for the browser to read the file before it knew where the video sat, so the timeline
/// moved under the viewer once the metadata arrived, and every viewer's browser did that work.
/// The server reads it once.</para>
///
/// <para><b>What it reads.</b> The movie header (<c>mvhd</c>) of an ISO base media file, which is
/// what .mov, .mp4 and .m4a all are, the <c>fmt </c> and <c>data</c> chunks of a WAV, and a FLAC's
/// STREAMINFO. Boxes it
/// does not need, including the media data itself, are skipped by seeking, so a two-gigabyte video
/// costs a few small reads. The phone writes <c>moov</c> after <c>mdat</c>, which is exactly why
/// this seeks rather than reading forward.</para>
///
/// <para>Anything it cannot read answers null rather than throwing. The browser still reads the
/// file and fills the gap, so a file this does not understand is a slower timeline, never a broken
/// one.</para>
/// </remarks>
public static class MediaDuration
{
    /// <summary>How deep into nested boxes to look for <c>mvhd</c>; it lives directly in <c>moov</c>.</summary>
    private const int MaxBoxesScanned = 4096;

    public static double? Seconds(Stream stream)
    {
        if (!stream.CanRead || !stream.CanSeek) return null;
        try
        {
            stream.Position = 0;
            Span<byte> head = stackalloc byte[12];
            if (!ReadExactly(stream, head)) return null;
            stream.Position = 0;

            if (head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WAVE"u8))
                return Wav(stream);
            if (head[..4].SequenceEqual("fLaC"u8))
                return Flac(stream);
            return IsoBmff(stream, 0, stream.Length);
        }
        catch (IOException) { return null; }
        catch (NotSupportedException) { return null; }
        catch (ArgumentException) { return null; }
    }

    /// <summary>
    /// A FLAC's length from its STREAMINFO, the block the format requires first: a 20-bit sample
    /// rate and a 36-bit sample count. Clips and edits are saved as FLAC (phase 4b), and a clip of a
    /// clip needs its length before anything is decoded.
    /// </summary>
    private static double? Flac(Stream stream)
    {
        Span<byte> info = stackalloc byte[26];   // "fLaC", block header, the first 18 bytes of STREAMINFO
        stream.Position = 0;
        if (!ReadExactly(stream, info)) return null;
        if ((info[4] & 0x7F) != 0) return null;  // the first block is not STREAMINFO

        var rate    = (info[18] << 12) | (info[19] << 4) | (info[20] >> 4);
        var samples = ((long)(info[21] & 0x0F) << 32) | BinaryPrimitives.ReadUInt32BigEndian(info[22..26]);

        // A sample count of 0 means the writer did not know it, as a stream to a pipe does not.
        return rate > 0 && samples > 0 ? (double)samples / rate : null;
    }

    /// <summary>Walks the boxes between two offsets for <c>moov</c>, then <c>mvhd</c> inside it.</summary>
    private static double? IsoBmff(Stream stream, long from, long to)
    {
        Span<byte> header = stackalloc byte[16];
        var at = from;
        for (var scanned = 0; scanned < MaxBoxesScanned && at + 8 <= to; scanned++)
        {
            stream.Position = at;
            if (!ReadExactly(stream, header[..8])) return null;
            long size = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
            var type = Encoding.ASCII.GetString(header[4..8]);
            var headerLength = 8;

            if (size == 1)
            {
                // A 64-bit size follows the type: what a large mdat uses.
                if (!ReadExactly(stream, header[8..16])) return null;
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header[8..16]);
                headerLength = 16;
            }
            else if (size == 0)
            {
                size = to - at;   // "to the end of the file"
            }
            if (size < headerLength || at + size > to) return null;

            if (type == "moov")
                return IsoBmff(stream, at + headerLength, at + size);
            if (type == "mvhd")
                return MovieHeader(stream, at + headerLength);

            at += size;
        }
        return null;
    }

    /// <summary>The movie header: version, then (timescale, duration) at a version-dependent offset.</summary>
    private static double? MovieHeader(Stream stream, long at)
    {
        stream.Position = at;
        Span<byte> body = stackalloc byte[32];
        if (!ReadExactly(stream, body[..4])) return null;
        var version = body[0];

        uint timescale;
        ulong duration;
        if (version == 1)
        {
            // creation(8) modification(8) timescale(4) duration(8)
            if (!ReadExactly(stream, body[..28])) return null;
            timescale = BinaryPrimitives.ReadUInt32BigEndian(body[16..20]);
            duration  = BinaryPrimitives.ReadUInt64BigEndian(body[20..28]);
        }
        else
        {
            // creation(4) modification(4) timescale(4) duration(4)
            if (!ReadExactly(stream, body[..16])) return null;
            timescale = BinaryPrimitives.ReadUInt32BigEndian(body[8..12]);
            duration  = BinaryPrimitives.ReadUInt32BigEndian(body[12..16]);
        }

        // All ones means "unknown" in the format; zero means nothing is known either.
        if (timescale == 0 || duration == 0 || duration == uint.MaxValue || duration == ulong.MaxValue)
            return null;
        var seconds = duration / (double)timescale;
        return double.IsFinite(seconds) && seconds > 0 ? seconds : null;
    }

    /// <summary>A WAV: the data chunk's length over the format's bytes per second.</summary>
    private static double? Wav(Stream stream)
    {
        stream.Position = 12;
        Span<byte> chunk = stackalloc byte[8];
        Span<byte> format = stackalloc byte[16];
        uint? bytesPerSecond = null;

        for (var scanned = 0; scanned < MaxBoxesScanned && stream.Position + 8 <= stream.Length; scanned++)
        {
            if (!ReadExactly(stream, chunk)) return null;
            var id = Encoding.ASCII.GetString(chunk[..4]);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..8]);
            var next = stream.Position + size + (size % 2);   // chunks are padded to even lengths

            if (id == "fmt " && size >= 16)
            {
                if (!ReadExactly(stream, format)) return null;
                bytesPerSecond = BinaryPrimitives.ReadUInt32LittleEndian(format[8..12]);
            }
            else if (id == "data")
            {
                if (bytesPerSecond is not { } rate || rate == 0) return null;
                // A recorder that never finished writes a data length larger than the file.
                var available = Math.Min(size, stream.Length - stream.Position);
                var seconds = available / (double)rate;
                return seconds > 0 ? seconds : null;
            }

            stream.Position = next;
        }
        return null;
    }

    private static bool ReadExactly(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read == 0) return false;
            total += read;
        }
        return true;
    }
}
