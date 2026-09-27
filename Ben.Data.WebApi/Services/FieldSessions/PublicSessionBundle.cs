using System.Buffers.Binary;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ben.Data.WebApi.Services.FieldSessions;

/// <summary>
/// The public copy of a published field session: a <c>.ben</c> anybody may download and play.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-27: "be able to have a server lookup to see if there are any available public
/// .ben files from nearby", and "be able to look up .ben files based on looking up locations". He
/// chose a <b>sanitized public copy</b> over handing out the original, because the original carries
/// exactly what the archive promises visitors never get.</para>
///
/// <para><b>What changes.</b> Every position in the reading document — the walked path, where each
/// mark and photograph was taken — is replaced by the place's PUBLIC point (the approximate one the
/// place's page already shows), with an accuracy that says so. The seal is rewritten, because the
/// document it described has changed: it keeps the recorder's account id (the archive already names
/// them), drops the device id (which identifies a phone, and nobody browsing needs it), and says it
/// is a public copy. Recordings go in only when the session's media is approved; the phone strips
/// photographs' metadata before it seals a bundle, so their bytes are copied as they are.</para>
///
/// <para><b>What does not.</b> Readings, marks, rooms, notes, times: the evidence is the point.
/// The original bundle on disk is never touched.</para>
///
/// <para>Written with the same rules as the phone's writer — stored members, no Zip64 — so the
/// phone's reader opens it exactly as it opens one it made.</para>
/// </remarks>
public static class PublicSessionBundle
{
    /// <summary>How far off the published point may be, in metres: the public-coordinate cell's radius.</summary>
    public const double PublicAccuracyMeters = 6.0 * 1609.344;

    /// <summary>Writes the public copy of <paramref name="source"/> to <paramref name="destination"/>.</summary>
    /// <param name="source">The original bundle. Must be seekable.</param>
    /// <param name="publicLatitude">The place's public latitude, or null to remove positions entirely.</param>
    /// <param name="publicLongitude">The place's public longitude.</param>
    /// <param name="includeRecordings">False while the session's media is held or not yet approved.</param>
    /// <param name="recordedByAccountId">Who the archive credits, carried into the new seal.</param>
    /// <param name="sessionId">What the phone keys its copy on. The SERVER's id for the session, never
    /// the recorder's device id: the recorder downloading the public copy of their own night would
    /// otherwise have it land on top of their private original.</param>
    public static async Task WriteAsync(
        Stream source, Stream destination,
        double? publicLatitude, double? publicLongitude,
        bool includeRecordings, Guid? recordedByAccountId, Guid sessionId,
        DateTime sealedAtUtc, CancellationToken ct = default)
    {
        var bundle = await BenBundle.ReadAsync(source, ct);
        var document = bundle.Document
            ?? throw new BenBundleFormatException("This session file has no readings document.");

        var writer = new StoredZipWriter(destination);
        var sealEntries = new List<(string Path, string Sha256, long Bytes)>();

        var sanitized = Sanitize(await ReadAllAsync(source, document, ct), publicLatitude, publicLongitude);
        await writer.AddAsync(BenBundle.DocumentEntryPath, sanitized, ct);
        sealEntries.Add((BenBundle.DocumentEntryPath, Sha256Hex(sanitized), sanitized.Length));

        if (includeRecordings)
        {
            foreach (var entry in bundle.Recordings.OrderBy(e => e.Path, StringComparer.Ordinal))
            {
                var hash = await writer.AddFromAsync(entry.Path, source, entry.Offset, entry.Length, ct);
                sealEntries.Add((entry.Path, hash, entry.Length));
            }
        }

        var seal = Seal(sealEntries, recordedByAccountId, sessionId, sealedAtUtc);
        await writer.AddAsync(BenBundle.SealEntryPath, seal, ct);
        await writer.FinishAsync(ct);
    }

    /// <summary>
    /// The reading document with every position replaced by the public point, or removed.
    /// </summary>
    /// <remarks>
    /// Walks the whole tree rather than naming the places positions live, so a position added to
    /// the format later is covered without anybody remembering to come back here. Any object that
    /// carries a latitude or a longitude is a position.
    /// </remarks>
    public static byte[] Sanitize(byte[] json, double? publicLatitude, double? publicLongitude)
    {
        var root = JsonNode.Parse(json)
            ?? throw new BenBundleFormatException("The readings document is empty.");
        Walk(root, publicLatitude, publicLongitude);
        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    private static void Walk(JsonNode node, double? latitude, double? longitude)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.ContainsKey("latitude") || obj.ContainsKey("longitude"))
                {
                    if (latitude is double lat && longitude is double lon)
                    {
                        obj["latitude"] = lat;
                        obj["longitude"] = lon;
                        // A published point claiming ten metres would be a lie about where it came from.
                        if (obj.ContainsKey("accuracy_meters")) obj["accuracy_meters"] = PublicAccuracyMeters;
                    }
                    else
                    {
                        obj.Remove("latitude");
                        obj.Remove("longitude");
                        obj.Remove("accuracy_meters");
                    }
                }
                foreach (var (_, child) in obj.ToList())
                    if (child is not null) Walk(child, latitude, longitude);
                break;
            case JsonArray array:
                foreach (var child in array)
                    if (child is not null) Walk(child, latitude, longitude);
                break;
        }
    }

    /// <summary>
    /// A seal in the phone's format (<c>SessionSeal.swift</c>): the same entry list and the same
    /// digest, so the phone checks it exactly as it checks its own.
    /// </summary>
    internal static byte[] Seal(IEnumerable<(string Path, string Sha256, long Bytes)> entries,
                                Guid? recordedByAccountId, Guid sessionId, DateTime sealedAtUtc)
    {
        var ordered = entries.OrderBy(e => e.Path, StringComparer.Ordinal).ToList();
        var seal = new JsonObject
        {
            ["version"] = 1,
            ["recorded_by_account_id"] = recordedByAccountId?.ToString().ToUpperInvariant(),
            ["device_id"] = null,
            ["session_id"] = sessionId.ToString().ToUpperInvariant(),
            ["sealed_at"] = sealedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            ["public_archive_copy"] = true,
            ["entries"] = new JsonArray(ordered.Select(e => (JsonNode)new JsonObject
            {
                ["path"] = e.Path, ["sha256"] = e.Sha256, ["byte_count"] = e.Bytes,
            }).ToArray()),
            ["digest"] = Digest(ordered),
        };
        return JsonSerializer.SerializeToUtf8Bytes(seal);
    }

    /// <summary>One line per entry, in path order, hashed — <c>SessionSeal.digest(of:)</c> exactly.</summary>
    internal static string Digest(IEnumerable<(string Path, string Sha256, long Bytes)> entries)
    {
        var text = new StringBuilder();
        foreach (var e in entries.OrderBy(e => e.Path, StringComparer.Ordinal))
            text.Append(e.Path).Append('\n').Append(e.Sha256).Append('\n').Append(e.Bytes).Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static async Task<byte[]> ReadAllAsync(Stream source, BenBundleEntry entry, CancellationToken ct)
    {
        if (entry.Length > 64 * 1024 * 1024)
            throw new BenBundleFormatException("The readings document is too large to publish.");
        var buffer = new byte[entry.Length];
        source.Seek(entry.Offset, SeekOrigin.Begin);
        await source.ReadExactlyAsync(buffer, ct);
        return buffer;
    }

    /// <summary>
    /// A zip of stored members, written forward with sizes known up front: no data descriptors,
    /// no Zip64. The phone's and the server's readers accept nothing else, on purpose.
    /// </summary>
    private sealed class StoredZipWriter(Stream output)
    {
        private readonly MemoryStream _directory = new();
        private int _count;

        public async Task AddAsync(string path, byte[] bytes, CancellationToken ct)
        {
            var offset = CheckedOffset();
            var crc = Crc32.HashToUInt32(bytes);
            await WriteLocalHeaderAsync(path, crc, (uint)bytes.Length, ct);
            await output.WriteAsync(bytes, ct);
            AddCentral(path, crc, (uint)bytes.Length, offset);
        }

        /// <summary>Copies one member straight across, hashing it on the way; returns its SHA-256.</summary>
        public async Task<string> AddFromAsync(string path, Stream source, long sourceOffset, long length,
                                               CancellationToken ct)
        {
            if (length > uint.MaxValue) throw new BenBundleFormatException($"{path} is too large to publish.");

            // Two passes over the member: the CRC goes in the header before the bytes, and the
            // writer does not seek backwards (the destination need not be seekable).
            var crc = new Crc32();
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                await CopyRangeAsync(source, sourceOffset, length, chunk => { crc.Append(chunk.Span); sha.AppendData(chunk.Span); }, null, ct);
                var offset = CheckedOffset();
                var crcValue = crc.GetCurrentHashAsUInt32();
                await WriteLocalHeaderAsync(path, crcValue, (uint)length, ct);
                await CopyRangeAsync(source, sourceOffset, length, null, output, ct);
                AddCentral(path, crcValue, (uint)length, offset);
                return Convert.ToHexStringLower(sha.GetHashAndReset());
            }
        }

        public async Task FinishAsync(CancellationToken ct)
        {
            var directoryOffset = CheckedOffset();
            var directory = _directory.ToArray();
            await output.WriteAsync(directory, ct);

            var end = new byte[22];
            BinaryPrimitives.WriteUInt32LittleEndian(end, 0x0605_4B50);
            BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(8), (ushort)_count);
            BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(10), (ushort)_count);
            BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(12), (uint)directory.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(16), directoryOffset);
            await output.WriteAsync(end, ct);
            await output.FlushAsync(ct);
        }

        private long _written;

        private uint CheckedOffset()
        {
            if (_written > uint.MaxValue)
                throw new BenBundleFormatException("The public copy would be larger than a session file can be.");
            return (uint)_written;
        }

        private async Task WriteLocalHeaderAsync(string path, uint crc, uint size, CancellationToken ct)
        {
            var name = Encoding.UTF8.GetBytes(path);
            var local = new byte[30];
            BinaryPrimitives.WriteUInt32LittleEndian(local, 0x0403_4B50);
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(4), 20);
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(6), 0x0800);   // UTF-8 names
            BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(14), crc);
            BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(18), size);
            BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(22), size);
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(26), (ushort)name.Length);
            await output.WriteAsync(local, ct);
            await output.WriteAsync(name, ct);
            _written += local.Length + name.Length + size;
        }

        private void AddCentral(string path, uint crc, uint size, uint offset)
        {
            var name = Encoding.UTF8.GetBytes(path);
            var central = new byte[46];
            BinaryPrimitives.WriteUInt32LittleEndian(central, 0x0201_4B50);
            BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(4), 20);
            BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(6), 20);
            BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(8), 0x0800);
            BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(16), crc);
            BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(20), size);
            BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(24), size);
            BinaryPrimitives.WriteUInt16LittleEndian(central.AsSpan(28), (ushort)name.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(42), offset);
            _directory.Write(central);
            _directory.Write(name);
            _count++;
            if (_count > 520) throw new BenBundleFormatException("Too many recordings to publish in one file.");
        }

        private static async Task CopyRangeAsync(Stream source, long offset, long length,
                                                 Action<ReadOnlyMemory<byte>>? observe, Stream? to,
                                                 CancellationToken ct)
        {
            var buffer = new byte[81_920];
            source.Seek(offset, SeekOrigin.Begin);
            var left = length;
            while (left > 0)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), ct);
                if (read == 0) throw new BenBundleFormatException("The session file ended early.");
                observe?.Invoke(buffer.AsMemory(0, read));
                if (to is not null) await to.WriteAsync(buffer.AsMemory(0, read), ct);
                left -= read;
            }
        }
    }
}
