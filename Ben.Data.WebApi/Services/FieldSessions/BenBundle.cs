using System.Buffers.Binary;
using System.Text;

namespace Ben.Data.WebApi.Services.FieldSessions;

/// <summary>
/// A <c>.ben</c> file is one field session: its <c>data.json</c> and every recording it made,
/// in a single ZIP.
/// </summary>
/// <remarks>
/// <para>
/// Ben, 2026-09-16: "I specifically asked that we zip the whole session and unzip it after upload,
/// but want to reference it as a single file and not a bunch of data.json files and separate audio
/// and video files." Before this, one night arrived as a <c>data.json</c> upload plus an upload per
/// clip, and that is exactly what a file list then showed — a wall of files called data.json, none
/// of which was a session anybody could point at.
/// </para>
/// <para>
/// Nothing is unpacked. The phone's writer stores entries rather than deflating them — the payload
/// is already H.264, AAC and JPEG, so compressing it would spend a battery to make the file
/// slightly larger — which means every member sits contiguously inside the bundle and can be
/// served as a byte range of it. One file on disk, and the file being referenced IS the file.
/// </para>
/// <para>
/// This reads the central directory only: a few kilobytes at the end of the file, whatever the
/// bundle weighs. It never reads a member's bytes, and it never writes anything anywhere.
/// </para>
/// </remarks>
public sealed class BenBundle
{
    /// <summary>The document every bundle must carry, describing the session.</summary>
    public const string DocumentEntryPath = "data.json";

    /// <summary>What a session bundle is called, and what it is.</summary>
    public const string FileExtension = ".ben";
    public const string ContentType = "application/vnd.ishaunted.field-session";

    /// <summary>
    /// More than any session could honestly hold — <see cref="FieldSessionFileGuard.MaxFilesPerSession"/>
    /// recordings, the document, and room for a manifest we have not invented yet. A bundle
    /// claiming more is refused before a single name is read.
    /// </summary>
    private const int MaxEntries = 520;

    /// <summary>A path inside a bundle is a file name and maybe a folder, not an essay.</summary>
    private const int MaxEntryPathLength = 512;

    /// <summary>How far back from the end to look for the end-of-central-directory record.</summary>
    private const int MaxEndRecordSearch = 66_000;

    private const uint EndOfCentralDirectorySignature = 0x0605_4B50;
    private const uint CentralHeaderSignature = 0x0201_4B50;
    private const uint LocalHeaderSignature = 0x0403_4B50;
    private const ushort StoredMethod = 0;

    private readonly Dictionary<string, BenBundleEntry> _entries;

    private BenBundle(Dictionary<string, BenBundleEntry> entries) => _entries = entries;

    /// <summary>Every member, by the path it carries inside the bundle.</summary>
    public IReadOnlyDictionary<string, BenBundleEntry> Entries => _entries;

    /// <summary>The session document's place in the bundle, or null if it has none.</summary>
    public BenBundleEntry? Document =>
        _entries.TryGetValue(DocumentEntryPath, out var entry) ? entry : null;

    /// <summary>Everything that is not the document — the recordings.</summary>
    public IEnumerable<BenBundleEntry> Recordings =>
        _entries.Values.Where(e => e.Path != DocumentEntryPath).OrderBy(e => e.Path, StringComparer.Ordinal);

    public BenBundleEntry? Find(string path) =>
        _entries.TryGetValue(path, out var entry) ? entry : null;

    /// <summary>
    /// Indexes a bundle. Throws <see cref="BenBundleFormatException"/> with a sentence a person
    /// can act on — these messages reach somebody who has just waited for an upload.
    /// </summary>
    public static async Task<BenBundle> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        if (!stream.CanSeek)
        {
            // Every storage backend the app has can seek, and a ranged read of a member is the
            // whole point of this format. A backend that cannot must serve ranges its own way
            // rather than have this pretend to.
            throw new BenBundleFormatException(
                "This session file can't be read where it is stored.");
        }

        var length = stream.Length;
        if (length < 22) throw new BenBundleFormatException("That file is too small to be a session.");
        if (length > uint.MaxValue)
        {
            // The writer on the phone emits 32-bit sizes and offsets — no Zip64 — so a bundle
            // this large could not have been written by it and cannot be addressed by it either.
            throw new BenBundleFormatException(
                "That session is larger than 4 GB, which this format can't carry. Send it in parts.");
        }

        var end = await ReadEndRecordAsync(stream, length, ct);
        if (end.EntryCount > MaxEntries)
            throw new BenBundleFormatException($"That session claims {end.EntryCount} files, which is more than one could hold.");
        if (end.DirectoryOffset + end.DirectorySize > length)
            throw new BenBundleFormatException("That session file is incomplete — it may not have finished sending.");

        var directory = new byte[end.DirectorySize];
        stream.Position = end.DirectoryOffset;
        await stream.ReadExactlyAsync(directory, ct);

        var entries = new Dictionary<string, BenBundleEntry>(StringComparer.Ordinal);
        var cursor = 0;
        for (var index = 0; index < end.EntryCount; index++)
        {
            if (cursor + 46 > directory.Length)
                throw new BenBundleFormatException("That session file is incomplete — it may not have finished sending.");

            var record = directory.AsSpan(cursor);
            if (BinaryPrimitives.ReadUInt32LittleEndian(record) != CentralHeaderSignature)
                throw new BenBundleFormatException("That doesn't look like a session file.");

            var method = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
            var compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(record[20..]);
            var uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(record[24..]);
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(record[28..]);
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(record[30..]);
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(record[32..]);
            var localHeaderOffset = BinaryPrimitives.ReadUInt32LittleEndian(record[42..]);

            var headerLength = 46 + nameLength + extraLength + commentLength;
            if (cursor + headerLength > directory.Length)
                throw new BenBundleFormatException("That session file is incomplete — it may not have finished sending.");

            if (method != StoredMethod)
            {
                // Not a limitation to work around: serving a member as a byte range of the bundle
                // is only possible because its bytes are in there unchanged.
                throw new BenBundleFormatException(
                    "That session file is compressed, and a session is stored uncompressed so it can be played from inside.");
            }
            if (compressedSize != uncompressedSize)
                throw new BenBundleFormatException("That session file's contents don't match what it says they are.");
            if (compressedSize == uint.MaxValue || localHeaderOffset == uint.MaxValue)
                throw new BenBundleFormatException("That session file uses a ZIP extension this format doesn't carry.");

            var name = Encoding.UTF8.GetString(record.Slice(46, nameLength));
            if (RefusalForPath(name) is { } refusal) throw new BenBundleFormatException(refusal);

            // Where the member's bytes actually start: past its own local header, whose extra
            // field is allowed to differ in length from the central one's. Reading it is the only
            // way to know, and skipping that read is the classic way to serve 30 bytes of header
            // as if it were the start of an audio file.
            var dataOffset = await DataOffsetAsync(stream, localHeaderOffset, nameLength, ct);
            if (dataOffset + compressedSize > length)
                throw new BenBundleFormatException("That session file is incomplete — it may not have finished sending.");

            if (!entries.TryAdd(name, new BenBundleEntry(name, dataOffset, compressedSize)))
                throw new BenBundleFormatException($"That session file carries \"{name}\" twice.");

            cursor += headerLength;
        }

        return new BenBundle(entries);
    }

    /// <summary>
    /// Why a path inside a bundle is unacceptable, or null when it is fine.
    /// </summary>
    /// <remarks>
    /// These names are never used to open a file — nothing is unpacked — but they are used as
    /// lookup keys, appear in a download's file name, and are handed back to a phone that WILL
    /// write them to disk when it imports the bundle. A name is checked once, here, rather than
    /// trusted differently by each of those.
    /// </remarks>
    public static string? RefusalForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "That session file has an entry with no name.";
        if (path.Length > MaxEntryPathLength) return "That session file has an entry with an absurdly long name.";
        if (path.EndsWith('/')) return "That session file contains a folder, and a session is a flat list of files.";
        if (path.Contains('\\')) return $"That session file has an entry with a backslash in its name: \"{path}\".";
        if (path.StartsWith('/')) return $"That session file has an entry with an absolute path: \"{path}\".";
        if (path.Contains('\0')) return "That session file has an entry with a broken name.";
        if (path.Length > 1 && path[1] == ':') return $"That session file has an entry with a drive letter: \"{path}\".";

        foreach (var segment in path.Split('/'))
        {
            if (segment is ".." or ".") return $"That session file has an entry that points outside itself: \"{path}\".";
            if (segment.Length == 0) return $"That session file has an entry with an empty folder in its name: \"{path}\".";
        }
        return null;
    }

    private static async Task<long> DataOffsetAsync(
        Stream stream, long localHeaderOffset, ushort centralNameLength, CancellationToken ct)
    {
        if (localHeaderOffset + 30 > stream.Length)
            throw new BenBundleFormatException("That session file is incomplete — it may not have finished sending.");

        var header = new byte[30];
        stream.Position = localHeaderOffset;
        await stream.ReadExactlyAsync(header, ct);

        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != LocalHeaderSignature)
            throw new BenBundleFormatException("That doesn't look like a session file.");

        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26));
        var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
        if (nameLength != centralNameLength)
            throw new BenBundleFormatException("That session file disagrees with itself about one of its entries.");

        return localHeaderOffset + 30 + nameLength + extraLength;
    }

    private static async Task<EndRecord> ReadEndRecordAsync(Stream stream, long length, CancellationToken ct)
    {
        var searchLength = (int)Math.Min(length, MaxEndRecordSearch);
        var tail = new byte[searchLength];
        stream.Position = length - searchLength;
        await stream.ReadExactlyAsync(tail, ct);

        // Backwards, because the archive comment that follows this record is allowed to contain
        // anything at all — including these four bytes.
        for (var index = tail.Length - 22; index >= 0; index--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(index)) != EndOfCentralDirectorySignature)
                continue;

            var record = tail.AsSpan(index);
            return new EndRecord(
                EntryCount: BinaryPrimitives.ReadUInt16LittleEndian(record[10..]),
                DirectorySize: BinaryPrimitives.ReadUInt32LittleEndian(record[12..]),
                DirectoryOffset: BinaryPrimitives.ReadUInt32LittleEndian(record[16..]));
        }

        throw new BenBundleFormatException("That doesn't look like a session file.");
    }

    private readonly record struct EndRecord(int EntryCount, long DirectorySize, long DirectoryOffset);
}

/// <summary>One member of a bundle, and where its bytes are inside it.</summary>
public sealed record BenBundleEntry(string Path, long Offset, long Length)
{
    /// <summary>The name on its own, for a download's file name.</summary>
    public string FileName => Path[(Path.LastIndexOf('/') + 1)..];
}

/// <summary>
/// A bundle that could not be read, in words that reach the person who sent it.
/// </summary>
public sealed class BenBundleFormatException(string message) : Exception(message);
