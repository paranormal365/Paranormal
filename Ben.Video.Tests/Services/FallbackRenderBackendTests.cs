using Ben.Video.RenderService;

namespace Ben.Video.Tests.Services;

public sealed class FallbackRenderBackendTests
{
    private sealed class FakeBackend : IRenderBackend
    {
        public string Name = "fake";
        public Func<RenderJob, RenderJobResult> ResultFor = job => RenderJobResult.Ok($"seg_{job.ClipId:N}");
        public List<RenderJob> Calls = [];
        public List<string> Deleted = [];

        public Task<RenderJobResult> RenderAsync(RenderJob job, IProgress<int> progress, CancellationToken ct)
        {
            Calls.Add(job);
            return Task.FromResult(ResultFor(job));
        }

        public Task DeleteSegmentAsync(string segmentName)
        {
            Deleted.Add(segmentName);
            return Task.CompletedTask;
        }
    }

    private static RenderJob AnyJob() => new(Guid.NewGuid(), "sig", RenderPass.Fine);

    [Fact]
    public async Task RenderAsync_RoutesToPrimary_WhenPrimaryAvailableTrue()
    {
        var primary = new FakeBackend { Name = "primary" };
        var fallback = new FakeBackend { Name = "fallback" };
        var backend = new FallbackRenderBackend(primary, fallback, primaryAvailable: () => true);

        await backend.RenderAsync(AnyJob(), new Progress<int>(), CancellationToken.None);

        Assert.Single(primary.Calls);
        Assert.Empty(fallback.Calls);
    }

    [Fact]
    public async Task RenderAsync_RoutesToFallback_WhenPrimaryAvailableFalse()
    {
        var primary = new FakeBackend { Name = "primary" };
        var fallback = new FakeBackend { Name = "fallback" };
        var backend = new FallbackRenderBackend(primary, fallback, primaryAvailable: () => false);

        await backend.RenderAsync(AnyJob(), new Progress<int>(), CancellationToken.None);

        Assert.Empty(primary.Calls);
        Assert.Single(fallback.Calls);
    }

    [Fact]
    public async Task RenderAsync_ReevaluatesPrimaryAvailable_PerCall()
    {
        var primary = new FakeBackend();
        var fallback = new FakeBackend();
        var available = true;
        var backend = new FallbackRenderBackend(primary, fallback, primaryAvailable: () => available);

        await backend.RenderAsync(AnyJob(), new Progress<int>(), CancellationToken.None);
        available = false; // simulates the sidecar dying between two queue jobs
        await backend.RenderAsync(AnyJob(), new Progress<int>(), CancellationToken.None);

        Assert.Single(primary.Calls);
        Assert.Single(fallback.Calls);
    }

    [Fact]
    public async Task RenderAsync_ReturnsPrimarysFailureVerbatim_DoesNotAutoRetryOnFallback()
    {
        var primary = new FakeBackend { ResultFor = _ => RenderJobResult.Failed("transport died mid-job") };
        var fallback = new FakeBackend();
        var backend = new FallbackRenderBackend(primary, fallback, primaryAvailable: () => true);

        var result = await backend.RenderAsync(AnyJob(), new Progress<int>(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("transport died mid-job", result.ErrorMessage);
        Assert.Empty(fallback.Calls); // no same-job fallback — the NEXT job re-checks primaryAvailable()
    }

    [Fact]
    public async Task DeleteSegmentAsync_CallsBothBackends()
    {
        var primary = new FakeBackend();
        var fallback = new FakeBackend();
        var backend = new FallbackRenderBackend(primary, fallback, primaryAvailable: () => true);

        await backend.DeleteSegmentAsync("bgseg_native_abc.mp4");

        Assert.Equal(["bgseg_native_abc.mp4"], primary.Deleted);
        Assert.Equal(["bgseg_native_abc.mp4"], fallback.Deleted);
    }
}
