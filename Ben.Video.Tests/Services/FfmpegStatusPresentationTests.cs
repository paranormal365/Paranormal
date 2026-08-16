using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #71. The crux of this fix is <see cref="FfmpegStatusPresentation.IsBusyButNotProcessing"/>:
/// before it, the toolbar badge never read <c>FfmpegService.IsWorkerBusy</c> at all, so it showed
/// "Ready" the entire time a lighter worker call (GetMetadataAsync/WriteFileAsync/
/// ExtractThumbnailsAsync — all heavily used during import) held the worker lock. These tests lock
/// in the correct distinction so a future change can't silently reintroduce that gap.
/// </summary>
public sealed class FfmpegStatusPresentationTests
{
    [Fact]
    public void IsBusyButNotProcessing_ReadyAndWorkerBusy_IsTrue()
    {
        // The exact case the old badge got wrong.
        Assert.True(FfmpegStatusPresentation.IsBusyButNotProcessing(FfmpegState.Ready, isWorkerBusy: true));
    }

    [Fact]
    public void IsBusyButNotProcessing_ReadyAndWorkerNotBusy_IsFalse()
    {
        Assert.False(FfmpegStatusPresentation.IsBusyButNotProcessing(FfmpegState.Ready, isWorkerBusy: false));
    }

    [Theory]
    [InlineData(FfmpegState.Idle)]
    [InlineData(FfmpegState.LoadingCore)]
    [InlineData(FfmpegState.Processing)]
    [InlineData(FfmpegState.Error)]
    public void IsBusyButNotProcessing_OnlyAppliesToReady_NeverOtherStates(FfmpegState state)
    {
        // Every other state already has its own correct, distinct signal (Processing shows a real
        // percent, Error shows the error, etc) — this flag exists only to cover Ready's blind spot.
        Assert.False(FfmpegStatusPresentation.IsBusyButNotProcessing(state, isWorkerBusy: true));
    }

    [Fact]
    public void Label_ReadyAndWorkerBusy_ShowsBusyNotReady()
    {
        var label = FfmpegStatusPresentation.Label(FfmpegState.Ready, isWorkerBusy: true, 0, null, null);
        Assert.Equal("Busy…", label);
    }

    [Fact]
    public void Label_ReadyAndWorkerIdle_ShowsReady()
    {
        var label = FfmpegStatusPresentation.Label(FfmpegState.Ready, isWorkerBusy: false, 0, null, null);
        Assert.Equal("Ready", label);
    }

    [Fact]
    public void Label_Processing_StillShowsThePercent()
    {
        // isWorkerBusy is irrelevant here — Processing already has a real, more specific signal.
        var label = FfmpegStatusPresentation.Label(FfmpegState.Processing, isWorkerBusy: true, 42, null, null);
        Assert.Equal("Processing… 42%", label);
    }

    [Fact]
    public void Label_LoadingCore_PrefersDownloadLabelOverGeneric()
    {
        var label = FfmpegStatusPresentation.Label(FfmpegState.LoadingCore, false, 0, "Downloading core (61%)", null);
        Assert.Equal("Downloading core (61%)", label);
    }

    [Fact]
    public void Label_LoadingCore_FallsBackWhenNoDownloadLabel()
    {
        var label = FfmpegStatusPresentation.Label(FfmpegState.LoadingCore, false, 0, null, null);
        Assert.Equal("Loading ffmpeg…", label);
    }

    [Fact]
    public void Label_Error_IncludesTheMessage()
    {
        var label = FfmpegStatusPresentation.Label(FfmpegState.Error, false, 0, null, "worker crashed");
        Assert.Equal("Error: worker crashed", label);
    }

    [Fact]
    public void CssModifier_RealProcessing_IsBusy()
    {
        Assert.Equal("busy", FfmpegStatusPresentation.CssModifier(FfmpegState.Processing, isWorkerBusy: false));
    }

    [Fact]
    public void CssModifier_ReadyButWorkerBusy_IsAlsoBusy()
    {
        // The whole point: both busy shapes get IDENTICAL styling, not two different-looking
        // badges that both mean "not available right now".
        Assert.Equal("busy", FfmpegStatusPresentation.CssModifier(FfmpegState.Ready, isWorkerBusy: true));
    }

    [Theory]
    [InlineData(FfmpegState.Idle, "idle")]
    [InlineData(FfmpegState.LoadingCore, "loadingcore")]
    [InlineData(FfmpegState.Error, "error")]
    public void CssModifier_OtherStates_AreUnaffectedByWorkerBusy(FfmpegState state, string expected)
    {
        Assert.Equal(expected, FfmpegStatusPresentation.CssModifier(state, isWorkerBusy: true));
        Assert.Equal(expected, FfmpegStatusPresentation.CssModifier(state, isWorkerBusy: false));
    }

    [Fact]
    public void CssModifier_ReadyAndNotBusy_IsReady()
    {
        Assert.Equal("ready", FfmpegStatusPresentation.CssModifier(FfmpegState.Ready, isWorkerBusy: false));
    }

    [Fact]
    public void Tooltip_BusyButNotProcessing_ExplainsTheNoPercentCase()
    {
        // Distinct from the plain label: a user who notices "Busy…" with no percent and no visible
        // progress bar movement reason needs the tooltip to say WHY, not just repeat the label.
        var tooltip = FfmpegStatusPresentation.Tooltip(FfmpegState.Ready, isWorkerBusy: true, 0, null, null);
        Assert.Contains("doesn't report a percent", tooltip);
    }

    [Fact]
    public void Tooltip_OtherStates_MatchesTheLabel()
    {
        var tooltip = FfmpegStatusPresentation.Tooltip(FfmpegState.Processing, isWorkerBusy: false, 55, null, null);
        Assert.Equal("Processing… 55%", tooltip);
    }
}
