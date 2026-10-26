using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services.Audio;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>The detector's trace, packed for the database and thinned for the page (phase 8).</summary>
public class EvpTraceCodecTests
{
    private static EvpTrace Sample(int buckets = 600) => new(
        0.1,
        Enumerable.Range(0, buckets).Select(i => -60f + (i == 123 ? 40 : i % 7)).ToArray(),
        Enumerable.Range(0, buckets).Select(i => -55f + i % 5).ToArray(),
        Enumerable.Range(0, buckets).Select(i => -62f + (i % 3) / 2f).ToArray(),
        [(12.31, 12.58), (40.0, 41.25)]);

    [Fact]
    public void A_trace_comes_back_as_it_was_to_a_hundredth_of_a_decibel()
    {
        var trace = Sample();

        var back = EvpTraceCodec.Decode(EvpTraceCodec.Encode(trace))!;

        Assert.Equal(trace.BucketSeconds, back.BucketSeconds, 3);
        Assert.All(trace.BandDb.Zip(back.BandDb), p => Assert.Equal(p.First, p.Second, 2));
        Assert.All(trace.FloorDb.Zip(back.FloorDb), p => Assert.Equal(p.First, p.Second, 2));
        Assert.Equal(trace.GateRuns, back.GateRuns);
    }

    [Fact]
    public void Ninety_minutes_packs_into_about_a_third_of_a_megabyte()
        => Assert.InRange(EvpTraceCodec.Encode(Sample(54_000)).Length, 300_000, 340_000);

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[] { (byte)'E', (byte)'V', (byte)'P', (byte)'T', 9, 0, 0, 0, 0, 0, 0, 0, 0 })]
    public void Anything_else_reads_as_no_trace(byte[]? bytes) => Assert.Null(EvpTraceCodec.Decode(bytes));

    /// <summary>Thinned for a long recording, a short loud sound must still show at full height.</summary>
    [Fact]
    public void Thinning_keeps_the_loudest_moment()
    {
        var trace = Sample();

        var (band, _, _, bucket) = AudioMarkerController.Thin(trace, 100);

        Assert.True(band.Length <= 100);
        Assert.Equal(trace.BandDb.Max(), band.Max());
        Assert.Equal(0.6, bucket, 6);
    }
}
