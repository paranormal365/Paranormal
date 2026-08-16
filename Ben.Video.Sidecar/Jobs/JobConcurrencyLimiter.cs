using Microsoft.Extensions.Options;

namespace Ben.Video.Sidecar.Jobs;

/// <summary>
/// The single ffmpeg-encode concurrency budget for the whole process — item #70 phase 159.
///
/// <para>Before this phase the semaphore was a private field of <see cref="SegmentJobRunner"/>,
/// which was correct while there was exactly one job kind. With a second encode runner
/// (<see cref="ThumbnailJobRunner"/>, and more coming in phases 160/162) a per-runner semaphore
/// would silently multiply the real process budget: two runners each admitting
/// <see cref="SidecarOptions.MaxConcurrentJobs"/> jobs means twice as many concurrent ffmpeg
/// processes as the setting says, defeating the resource-exhaustion defense (item #38 phase E
/// threat T6). Hoisting it into one shared singleton makes the setting mean what it claims for
/// every current and future job kind.</para>
///
/// <para><b>Not everything shares this.</b> <c>POST /v1/probe</c> deliberately has its own, much
/// larger limit: ffprobe is a sub-second read-only metadata call, and queueing one behind two
/// half-hour encodes would make sidecar probing strictly slower than the wasm path it replaces —
/// the exact failure this whole item exists to remove.</para>
/// </summary>
public sealed class JobConcurrencyLimiter
{
    private readonly SemaphoreSlim _semaphore;

    public JobConcurrencyLimiter(IOptions<SidecarOptions> options)
    {
        var max = options.Value.MaxConcurrentJobs;
        _semaphore = new SemaphoreSlim(max, max);
    }

    public Task WaitAsync(CancellationToken ct) => _semaphore.WaitAsync(ct);

    public void Release() => _semaphore.Release();
}
