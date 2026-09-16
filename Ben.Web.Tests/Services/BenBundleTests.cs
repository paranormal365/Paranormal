using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;
using Ben.Data.WebApi.Services.FieldSessions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Reading a <c>.ben</c> session bundle: what it carries, and what it refuses.
/// </summary>
/// <remarks>
/// The bundles here are written by <see cref="StoredZip"/>, which is a deliberate re-implementation
/// of the phone's <c>ZipWriter</c> — same stored entries, same 32-bit header, same absence of a
/// data descriptor. Testing the reader against .NET's own ZipArchive would prove the reader can
/// read a file nothing in this system produces.
/// </remarks>
public class BenBundleTests
{
    [Fact]
    public async Task ReadsTheDocumentAndEveryRecording()
    {
        var document = """{"schema":"device-data-v1"}"""u8.ToArray();
        var audio = Enumerable.Range(0, 5_000).Select(i => (byte)i).ToArray();

        await using var file = StoredZip.Write(
            ("data.json", document),
            ("media/audio-001.m4a", audio));

        var bundle = await BenBundle.ReadAsync(file);

        Assert.NotNull(bundle.Document);
        Assert.Equal(document.Length, bundle.Document!.Length);
        var recording = Assert.Single(bundle.Recordings);
        Assert.Equal("media/audio-001.m4a", recording.Path);
        Assert.Equal("audio-001.m4a", recording.FileName);
        Assert.Equal(audio.Length, recording.Length);
    }

    /// <summary>
    /// The offset has to land on the member's FIRST byte. Off by the thirty bytes of a local
    /// header and every recording plays back as noise, which is the kind of bug that looks like a
    /// broken microphone.
    /// </summary>
    [Fact]
    public async Task AnEntryPointsAtItsOwnBytes()
    {
        var audio = Enumerable.Range(0, 3_000).Select(i => (byte)(i * 7 % 251)).ToArray();
        await using var file = StoredZip.Write(
            ("data.json", "{}"u8.ToArray()),
            ("media/audio-001.m4a", audio));

        var bundle = await BenBundle.ReadAsync(file);
        var entry = bundle.Find("media/audio-001.m4a")!;

        var read = new byte[entry.Length];
        file.Position = entry.Offset;
        await file.ReadExactlyAsync(read);

        Assert.Equal(audio, read);
    }

    [Fact]
    public async Task AWindowReadsOnlyItsOwnMemberAndCanSeekInsideIt()
    {
        var audio = Enumerable.Range(0, 4_096).Select(i => (byte)(i % 256)).ToArray();
        await using var file = StoredZip.Write(
            ("data.json", "{}"u8.ToArray()),
            ("media/audio-001.m4a", audio),
            ("media/photo-001.jpg", new byte[1_000]));

        var bundle = await BenBundle.ReadAsync(file);
        var entry = bundle.Find("media/audio-001.m4a")!;

        await using var window = new BundleWindowStream(
            StoredZip.Reopen(file), entry.Offset, entry.Length);

        Assert.Equal(audio.Length, window.Length);

        // Straight through: exactly the member, and not one byte of what follows it.
        var whole = new byte[audio.Length + 100];
        var read = await window.ReadAsync(whole, 0, whole.Length);
        Assert.Equal(audio.Length, read);
        Assert.Equal(audio, whole[..audio.Length]);
        Assert.Equal(0, await window.ReadAsync(whole, 0, 10));

        // And a seek into the middle — a player's scrubber — lands where it says.
        window.Position = 1_000;
        var middle = new byte[16];
        await window.ReadExactlyAsync(middle);
        Assert.Equal(audio[1_000..1_016], middle);
    }

    [Fact]
    public async Task RefusesAFileThatIsNotABundle()
    {
        await using var notAZip = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 2_000)));
        var refusal = await Assert.ThrowsAsync<BenBundleFormatException>(
            () => BenBundle.ReadAsync(notAZip));
        Assert.Contains("doesn't look like a session file", refusal.Message);
    }

    [Fact]
    public async Task RefusesABundleThatStopsHalfway()
    {
        await using var whole = StoredZip.Write(
            ("data.json", "{}"u8.ToArray()),
            ("media/audio-001.m4a", new byte[2_000]));

        // The end record survives, so it is found and believed — and then describes bytes that
        // are not there. An upload cut off by a dropped connection looks exactly like this.
        var bytes = whole.ToArray();
        var cut = new byte[bytes.Length];
        Array.Copy(bytes, cut, bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            cut.AsSpan(cut.Length - 22 + 16), (uint)(bytes.Length + 500));

        await using var truncated = new MemoryStream(cut);
        var refusal = await Assert.ThrowsAsync<BenBundleFormatException>(
            () => BenBundle.ReadAsync(truncated));
        Assert.Contains("incomplete", refusal.Message);
    }

    /// <summary>
    /// A member's bytes have to be in there unchanged for a range of the bundle to BE that member.
    /// A deflated entry would serve as compressed noise, silently.
    /// </summary>
    [Fact]
    public async Task RefusesACompressedEntry()
    {
        await using var file = StoredZip.Write(deflateFirstEntry: true,
            ("data.json", "{}"u8.ToArray()));

        var refusal = await Assert.ThrowsAsync<BenBundleFormatException>(
            () => BenBundle.ReadAsync(file));
        Assert.Contains("compressed", refusal.Message);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("/etc/passwd")]
    [InlineData("media/../../escape.m4a")]
    [InlineData(@"media\audio.m4a")]
    [InlineData("C:/windows/system32/x.dll")]
    [InlineData("media//audio.m4a")]
    public void RefusesAPathThatPointsOutsideTheBundle(string path)
    {
        // Nothing here is ever unpacked by the server — but a phone importing a bundle DOES write
        // these names to disk, and a download uses one as a file name. Checked once, here.
        Assert.NotNull(BenBundle.RefusalForPath(path));
    }

    [Theory]
    [InlineData("data.json")]
    [InlineData("media/audio-001.m4a")]
    [InlineData("media/video-002.mov")]
    public void AcceptsTheNamesASessionActuallyUses(string path)
    {
        Assert.Null(BenBundle.RefusalForPath(path));
    }

    [Fact]
    public async Task RefusesTheSameNameTwice()
    {
        // Two members with one name means one of them can never be served, and which one a reader
        // picks is arbitrary. Better refused than silently halved.
        await using var file = StoredZip.Write(
            ("data.json", "{}"u8.ToArray()),
            ("media/audio-001.m4a", new byte[10]),
            ("media/audio-001.m4a", new byte[20]));

        var refusal = await Assert.ThrowsAsync<BenBundleFormatException>(
            () => BenBundle.ReadAsync(file));
        Assert.Contains("twice", refusal.Message);
    }

    [Fact]
    public async Task ABundleWithNoDocumentHasNoDocument()
    {
        await using var file = StoredZip.Write(("media/audio-001.m4a", new byte[10]));
        var bundle = await BenBundle.ReadAsync(file);
        Assert.Null(bundle.Document);
    }

    /// <summary>
    /// The phone's ZIP writer, rewritten here so the reader is tested against the shape it will
    /// actually meet: stored entries, 32-bit sizes, no data descriptor, no Zip64.
    /// </summary>
    private static class StoredZip
    {
        public static MemoryStream Write(params (string Path, byte[] Bytes)[] entries)
            => Write(false, entries);

        public static MemoryStream Write(
            bool deflateFirstEntry, params (string Path, byte[] Bytes)[] entries)
        {
            var output = new MemoryStream();
            var directory = new MemoryStream();
            var offset = 0u;

            for (var index = 0; index < entries.Length; index++)
            {
                var (path, bytes) = entries[index];
                var name = Encoding.UTF8.GetBytes(path);
                var crc = Crc32.HashToUInt32(bytes);
                // Only the METHOD is lied about, so the reader meets a header claiming deflate
                // over bytes that are not deflated — which is the only part it can check.
                var method = (ushort)(deflateFirstEntry && index == 0 ? 8 : 0);

                var local = new byte[30];
                BinaryPrimitives.WriteUInt32LittleEndian(local, 0x0403_4B50);
                BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(4), 20);
                BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(8), method);
                BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(14), crc);
                BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(18), (uint)bytes.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(22), (uint)bytes.Length);
                BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(26), (ushort)name.Length);
                output.Write(local);
                output.Write(name);
                output.Write(bytes);

                var central = new byte[46];
                BinaryPrimitives.WriteUInt32LittleEndian(central, 0x0201_4B50);
                BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(4), 20);
                BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(6), 20);
                BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(10), method);
                BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(16), crc);
                BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(20), (uint)bytes.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(24), (uint)bytes.Length);
                BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(28), (ushort)name.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(42), offset);
                directory.Write(central);
                directory.Write(name);

                offset = (uint)output.Position;
            }

            var directoryOffset = (uint)output.Position;
            var directoryBytes = directory.ToArray();
            output.Write(directoryBytes);

            var end = new byte[22];
            BinaryPrimitives.WriteUInt32LittleEndian(end, 0x0605_4B50);
            BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(8), (ushort)entries.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(10), (ushort)entries.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(12), (uint)directoryBytes.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(16), directoryOffset);
            output.Write(end);

            output.Position = 0;
            return output;
        }

        /// <summary>A second handle on the same bytes, as storage would hand out.</summary>
        public static MemoryStream Reopen(MemoryStream original) => new(original.ToArray());
    }
}
