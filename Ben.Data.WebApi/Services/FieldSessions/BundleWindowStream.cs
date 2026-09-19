namespace Ben.Data.WebApi.Services.FieldSessions;

/// <summary>
/// One member of a <c>.ben</c> bundle, as a stream of its own.
/// </summary>
/// <remarks>
/// <para>
/// A window onto the bundle: reads are clamped to the member's bytes and positions are relative to
/// its start, so nothing downstream needs to know it is looking at part of a larger file. It stays
/// SEEKABLE on purpose — that is what lets ASP.NET answer a browser's Range request for the middle
/// of a recording by seeking, rather than by reading and discarding everything before it. Dragging
/// a player's scrubber to 40 minutes should cost one seek, not 40 minutes of I/O.
/// </para>
/// <para>
/// Disposing this disposes the bundle's stream: the window owns it, because nobody else is holding
/// it open once it has been handed to a response.
/// </para>
/// </remarks>
public sealed class BundleWindowStream(Stream inner, long offset, long length) : Stream
{
    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int start, int count)
        => Read(buffer.AsSpan(start, count));

    public override int Read(Span<byte> buffer)
    {
        var allowed = Clamp(buffer.Length);
        if (allowed == 0) return 0;
        inner.Position = offset + _position;
        var read = inner.Read(buffer[..allowed]);
        _position += read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken ct = default)
    {
        var allowed = Clamp(buffer.Length);
        if (allowed == 0) return 0;
        inner.Position = offset + _position;
        var read = await inner.ReadAsync(buffer[..allowed], ct);
        _position += read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int start, int count, CancellationToken ct)
        => ReadAsync(buffer.AsMemory(start, count), ct).AsTask();

    public override long Seek(long to, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => to,
            SeekOrigin.Current => _position + to,
            SeekOrigin.End => length + to,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        // Clamped rather than thrown: a range request that asks past the end of a recording is
        // answered with the end of it, which is what every other file stream does.
        _position = Math.Clamp(target, 0, length);
        return _position;
    }

    private int Clamp(int wanted) => (int)Math.Max(0, Math.Min(wanted, length - _position));

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int start, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync();
        await base.DisposeAsync();
    }
}
