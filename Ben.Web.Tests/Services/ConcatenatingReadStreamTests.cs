using System.Text;
using Ben.Data.WebApi.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The stream that assembles chunk files into one upload: order preserved, sources opened
/// lazily, each disposed as soon as it is drained.
/// </summary>
public class ConcatenatingReadStreamTests
{
    private sealed class TrackingStream : MemoryStream
    {
        public bool Disposed { get; private set; }
        public TrackingStream(byte[] bytes) : base(bytes, writable: false) { }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    [Fact]
    public async Task Reads_AllSources_InOrder_AsOneStream()
    {
        var concat = new ConcatenatingReadStream(
        [
            ct => Task.FromResult<Stream>(new MemoryStream("AAA"u8.ToArray())),
            ct => Task.FromResult<Stream>(new MemoryStream(""u8.ToArray())),      // empty mid-source
            ct => Task.FromResult<Stream>(new MemoryStream("BBCC"u8.ToArray())),
        ]);

        using var result = new MemoryStream();
        await concat.CopyToAsync(result);
        Assert.Equal("AAABBCC", Encoding.UTF8.GetString(result.ToArray()));
    }

    [Fact]
    public async Task Sources_AreOpenedLazily_AndDisposedWhenDrained()
    {
        var first = new TrackingStream("AA"u8.ToArray());
        var opened = new List<int>();

        var concat = new ConcatenatingReadStream(
        [
            ct => { opened.Add(0); return Task.FromResult<Stream>(first); },
            ct => { opened.Add(1); return Task.FromResult<Stream>(new MemoryStream("BB"u8.ToArray())); },
        ]);

        // The COUNT matters here, not just the call: a stream that returns fewer bytes than asked
        // is legal, and ignoring the return (CA2022) would hide a short read behind an assertion
        // about which sources are open. Asserting it also states what this stream promises —
        // one source per read, never a silent straddle.
        var buffer = new byte[2];
        Assert.Equal(2, await concat.ReadAsync(buffer));
        Assert.Equal([0], opened);          // the second source is not open yet
        Assert.False(first.Disposed);

        Assert.Equal(2, await concat.ReadAsync(buffer));   // drains first, rolls into second
        Assert.True(first.Disposed);
        Assert.Equal([0, 1], opened);
    }

    [Fact]
    public async Task EmptySourceList_IsAnEmptyStream()
    {
        var concat = new ConcatenatingReadStream([]);
        using var result = new MemoryStream();
        await concat.CopyToAsync(result);
        Assert.Equal(0, result.Length);
    }
}
