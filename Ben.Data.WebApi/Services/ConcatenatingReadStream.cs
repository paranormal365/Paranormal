namespace Ben.Data.WebApi.Services;

/// <summary>
/// A read-only stream that presents a sequence of source streams as one continuous stream.
/// </summary>
/// <remarks>
/// <para>Built for chunked-upload assembly: the chunks sit on disk as separate files, and the
/// storage abstraction (<see cref="Ben.Data.Common.Interfaces.IFileStorageService.WriteAsync"/>)
/// takes a single source stream. Concatenating lazily — each source is opened only when the
/// previous one is exhausted, and disposed as soon as it is drained — means assembling a
/// multi-gigabyte upload holds one open chunk and one 80 KB copy buffer, never the whole file.</para>
///
/// <para>Factories rather than streams so that nothing is open before it is needed; a failure
/// mid-assembly leaves at most one stream to dispose.</para>
/// </remarks>
public sealed class ConcatenatingReadStream : Stream
{
    private readonly IReadOnlyList<Func<CancellationToken, Task<Stream>>> _sources;
    private Stream? _current;
    private int _nextIndex;

    public ConcatenatingReadStream(IReadOnlyList<Func<CancellationToken, Task<Stream>>> sources)
        => _sources = sources;

    public override bool CanRead  => true;
    public override bool CanSeek  => false;
    public override bool CanWrite => false;
    public override long Length   => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        while (true)
        {
            if (_current is null)
            {
                if (_nextIndex >= _sources.Count) return 0;   // every source drained
                _current = await _sources[_nextIndex++](ct);
            }

            var read = await _current.ReadAsync(buffer, ct);
            if (read > 0) return read;

            await _current.DisposeAsync();
            _current = null;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _current?.Dispose();
        _current = null;
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_current is not null) await _current.DisposeAsync();
        _current = null;
        await base.DisposeAsync();
    }
}
