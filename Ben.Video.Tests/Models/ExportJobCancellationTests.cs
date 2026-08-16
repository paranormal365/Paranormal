using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

/// <summary>
/// Audit #1 — <see cref="ExportJob"/>'s cancellation surface.
///
/// <para>Before this, the pipeline's only cancellation signal was the <see cref="ExportJob.CancelRequested"/>
/// bool, checked between the ~6 pipeline <i>phases</i>. Every long-running call underneath
/// (20 <c>ExecAsync</c> sites, plus the native encoder/assembler) received
/// <c>CancellationToken.None</c>, so Cancel could not take effect until an entire phase finished.
/// These tests pin the token's contract so a future change can't quietly go back to a bool.</para>
///
/// <para><b>What this deliberately does NOT claim</b>: cancelling cannot abort an ffmpeg.wasm
/// command that is already executing. That worker is synchronous with no abort channel — the only
/// lever is <c>terminate()</c>, which destroys the worker and every cached MEMFS segment, and is
/// deliberately not wired to Cancel (phase 143's standing rule: never kill an in-flight export
/// without consent). The token's real value is stopping at the next <i>command</i> boundary
/// instead of the next <i>phase</i> boundary.</para>
/// </summary>
public sealed class ExportJobCancellationTests
{
    [Fact]
    public void NewJob_TokenIsNotCancelled()
    {
        var job = new ExportJob();
        Assert.False(job.CancelRequested);
        Assert.False(job.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_SignalsBothTheBoolAndTheToken()
    {
        // The bool is still what the UI binds to for its "Cancelling…" state, and the token is what
        // the pipeline awaits on — they must never disagree.
        var job = new ExportJob();

        job.Cancel();

        Assert.True(job.CancelRequested);
        Assert.True(job.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_IsIdempotent()
    {
        var job = new ExportJob();
        job.Cancel();
        job.Cancel(); // a double-click on the Cancel button must not throw
        Assert.True(job.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_AfterDisposal_DoesNotThrow()
    {
        // The real ordering this guards: ExportAsync's finally disposes the CTS as soon as the job
        // reaches a terminal state, but the dialog can still be showing a Cancel button bound to
        // that job for a frame or two afterwards. A late click must be a no-op, not a crash.
        var job = new ExportJob();
        job.DisposeCancellation();

        var ex = Record.Exception(job.Cancel);

        Assert.Null(ex);
        Assert.True(job.CancelRequested);
    }

    [Fact]
    public void DisposeCancellation_IsIdempotent()
    {
        var job = new ExportJob();
        job.DisposeCancellation();
        Assert.Null(Record.Exception(job.DisposeCancellation));
    }

    [Fact]
    public void CancelledToken_ThrowsAtAnAwaitPoint()
    {
        // The property the whole change rests on: once cancelled, any pipeline step that observes
        // the token stops the run, rather than the pipeline having to reach the next explicit
        // between-phase check.
        var job = new ExportJob();
        job.Cancel();

        Assert.Throws<OperationCanceledException>(job.CancellationToken.ThrowIfCancellationRequested);
    }
}
