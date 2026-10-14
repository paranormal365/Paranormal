using System.Buffers.Binary;
using System.Text;
using Ben.Data.WebApi.Services.FieldSessions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A recording's length, read from its own header — what lets the server place a video clip on
/// the timeline without waiting for every viewer's browser to read the file.
/// </summary>
public sealed class MediaDurationTests
{
    [Fact]
    public void A_wav_is_its_data_over_its_byte_rate()
    {
        using var wav = new MemoryStream(Wav(seconds: 3, rate: 8000));
        Assert.Equal(3.0, MediaDuration.Seconds(wav)!.Value, 6);
    }

    [Fact]
    public void A_movie_with_its_header_after_the_media_is_read_by_seeking_past_the_media()
    {
        // The phone writes mdat first and moov last: reading forward would mean reading the video.
        using var movie = new MemoryStream(Movie(version: 0, timescale: 600, duration: 600 * 12, mdatBytes: 50_000));
        Assert.Equal(12.0, MediaDuration.Seconds(movie)!.Value, 6);
    }

    [Fact]
    public void A_version_one_movie_header_is_read_too()
    {
        using var movie = new MemoryStream(Movie(version: 1, timescale: 44_100, duration: 44_100L * 90 + 22_050, mdatBytes: 10));
        Assert.Equal(90.5, MediaDuration.Seconds(movie)!.Value, 6);
    }

    [Fact]
    public void A_large_media_box_with_a_64_bit_size_is_skipped()
    {
        using var movie = new MemoryStream(Movie(version: 0, timescale: 1000, duration: 4_250, mdatBytes: 64, largeSize: true));
        Assert.Equal(4.25, MediaDuration.Seconds(movie)!.Value, 6);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[] { 0, 0, 0, 8, (byte)'f', (byte)'r', (byte)'e', (byte)'e', 0, 0, 0, 255 })]
    public void Anything_it_cannot_read_answers_nothing_rather_than_throwing(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        Assert.Null(MediaDuration.Seconds(stream));
    }

    [Fact]
    public void A_movie_whose_duration_is_marked_unknown_answers_nothing()
    {
        using var movie = new MemoryStream(Movie(version: 0, timescale: 600, duration: uint.MaxValue, mdatBytes: 8));
        Assert.Null(MediaDuration.Seconds(movie));
    }

    // ── Building files ──────────────────────────────────────────────────────────

    private static byte[] Wav(int seconds, int rate)
    {
        var data = new byte[seconds * rate * 2];
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data.Length); w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(data.Length); w.Write(data);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>ftyp, then mdat (the media), then moov holding mvhd — the order the phone writes.</summary>
    private static byte[] Movie(int version, uint timescale, ulong duration, int mdatBytes, bool largeSize = false)
    {
        using var ms = new MemoryStream();
        Box(ms, "ftyp", Encoding.ASCII.GetBytes("qt  \0\0\0\0qt  "));

        if (largeSize)
        {
            Span<byte> header = stackalloc byte[16];
            BinaryPrimitives.WriteUInt32BigEndian(header[..4], 1);
            Encoding.ASCII.GetBytes("mdat", header[4..8]);
            BinaryPrimitives.WriteUInt64BigEndian(header[8..16], (ulong)(16 + mdatBytes));
            ms.Write(header);
            ms.Write(new byte[mdatBytes]);
        }
        else
        {
            Box(ms, "mdat", new byte[mdatBytes]);
        }

        var mvhd = new MemoryStream();
        mvhd.WriteByte((byte)version); mvhd.Write(new byte[3]);   // version, flags
        Span<byte> field = stackalloc byte[8];
        if (version == 1)
        {
            mvhd.Write(new byte[16]);                               // creation, modification
            BinaryPrimitives.WriteUInt32BigEndian(field[..4], timescale); mvhd.Write(field[..4]);
            BinaryPrimitives.WriteUInt64BigEndian(field, duration); mvhd.Write(field);
        }
        else
        {
            mvhd.Write(new byte[8]);
            BinaryPrimitives.WriteUInt32BigEndian(field[..4], timescale); mvhd.Write(field[..4]);
            BinaryPrimitives.WriteUInt32BigEndian(field[..4], (uint)duration); mvhd.Write(field[..4]);
        }
        mvhd.Write(new byte[80]);                                   // rate, volume, matrix… unread

        var moov = new MemoryStream();
        Box(moov, "mvhd", mvhd.ToArray());
        Box(ms, "moov", moov.ToArray());
        return ms.ToArray();
    }

    private static void Box(Stream into, string type, byte[] body)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header[..4], (uint)(8 + body.Length));
        Encoding.ASCII.GetBytes(type, header[4..8]);
        into.Write(header);
        into.Write(body);
    }
}
