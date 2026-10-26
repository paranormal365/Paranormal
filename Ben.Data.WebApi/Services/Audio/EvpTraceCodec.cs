using System.Buffers.Binary;

namespace Ben.Data.WebApi.Services.Audio;

/// <summary>
/// Packs an <see cref="EvpTrace"/> for <c>EvpScans.TraceBlob</c> (phase 8, 10/26/2026).
/// </summary>
/// <remarks>
/// <para>"EVPT", a version byte, the bucket length in milliseconds, then the three levels as
/// hundredths of a dB in 16 bits each, then the gate's openings as pairs of milliseconds. Ninety
/// minutes is about 324 KB.</para>
/// <para><b>Kept, not worked out again.</b> Redrawing a 90-minute recording's view by decoding it
/// again is the cost the scan was moved to the background to avoid.</para>
/// <para>Little-endian throughout. Anything that doesn't start "EVPT" version 1 reads as no trace.</para>
/// </remarks>
public static class EvpTraceCodec
{
    private static ReadOnlySpan<byte> Magic => "EVPT"u8;
    private const byte Version = 1;

    public static byte[] Encode(EvpTrace trace)
    {
        var count = trace.BandDb.Length;
        var size  = 4 + 1 + 4 + 4 + 3 * 2 * count + 4 + 8 * trace.GateRuns.Count;
        var bytes = new byte[size];
        var span  = bytes.AsSpan();

        Magic.CopyTo(span);
        span[4] = Version;
        var at = 5;
        BinaryPrimitives.WriteInt32LittleEndian(span[at..], (int)Math.Round(trace.BucketSeconds * 1000)); at += 4;
        BinaryPrimitives.WriteInt32LittleEndian(span[at..], count); at += 4;
        foreach (var levels in new[] { trace.BandDb, trace.FullDb, trace.FloorDb })
            foreach (var db in levels)
            {
                BinaryPrimitives.WriteInt16LittleEndian(span[at..], Hundredths(db));
                at += 2;
            }
        BinaryPrimitives.WriteInt32LittleEndian(span[at..], trace.GateRuns.Count); at += 4;
        foreach (var (start, end) in trace.GateRuns)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span[at..], (int)Math.Round(start * 1000)); at += 4;
            BinaryPrimitives.WriteInt32LittleEndian(span[at..], (int)Math.Round(end * 1000));   at += 4;
        }
        return bytes;
    }

    /// <returns>The trace, or null when <paramref name="bytes"/> is not one this version wrote.</returns>
    public static EvpTrace? Decode(byte[]? bytes)
    {
        if (bytes is null || bytes.Length < 13 || !bytes.AsSpan(0, 4).SequenceEqual(Magic) || bytes[4] != Version)
            return null;
        try
        {
            var span   = bytes.AsSpan();
            var at     = 5;
            var bucket = BinaryPrimitives.ReadInt32LittleEndian(span[at..]) / 1000.0; at += 4;
            var count  = BinaryPrimitives.ReadInt32LittleEndian(span[at..]);          at += 4;
            if (count < 0 || 13 + 6L * count + 4 > bytes.Length) return null;

            var levels = new float[3][];
            for (var l = 0; l < 3; l++)
            {
                levels[l] = new float[count];
                for (var i = 0; i < count; i++, at += 2)
                    levels[l][i] = BinaryPrimitives.ReadInt16LittleEndian(span[at..]) / 100f;
            }

            var runCount = BinaryPrimitives.ReadInt32LittleEndian(span[at..]); at += 4;
            if (runCount < 0 || at + 8L * runCount > bytes.Length) return null;
            var runs = new List<(double, double)>(runCount);
            for (var r = 0; r < runCount; r++, at += 8)
                runs.Add((BinaryPrimitives.ReadInt32LittleEndian(span[at..]) / 1000.0,
                          BinaryPrimitives.ReadInt32LittleEndian(span[(at + 4)..]) / 1000.0));

            return new EvpTrace(bucket, levels[0], levels[1], levels[2], runs);
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static short Hundredths(float db)
        => (short)Math.Clamp(Math.Round(db * 100), short.MinValue, short.MaxValue);
}
