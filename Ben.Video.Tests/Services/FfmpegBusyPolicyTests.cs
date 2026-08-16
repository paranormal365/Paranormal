using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #67 fix — <see cref="FfmpegBusyPolicy.ImmediateFailureMessage"/> is the crux of the fix:
/// before this phase, <c>ClipBrowser</c> hard-rejected a Server-tab import whenever ffmpeg wasn't
/// exactly <see cref="FfmpegState.Ready"/>, including <see cref="FfmpegState.Processing"/> — a
/// perfectly healthy, if slow, state. These tests lock in the correct distinction (only
/// Idle/LoadingCore/Error fail immediately; Ready and Processing both return null, meaning "don't
/// reject") so a future change can't silently reintroduce the original bug.
/// </summary>
public sealed class FfmpegBusyPolicyTests
{
    [Fact]
    public void ImmediateFailureMessage_Ready_ReturnsNull()
    {
        Assert.Null(FfmpegBusyPolicy.ImmediateFailureMessage(FfmpegState.Ready));
    }

    [Fact]
    public void ImmediateFailureMessage_Processing_ReturnsNull()
    {
        // This is the exact case the old gate got wrong — Processing is not a failure state.
        Assert.Null(FfmpegBusyPolicy.ImmediateFailureMessage(FfmpegState.Processing));
    }

    [Fact]
    public void ImmediateFailureMessage_Idle_ReturnsInitializeMessage()
    {
        Assert.Equal(FfmpegBusyPolicy.NotInitializedMessage, FfmpegBusyPolicy.ImmediateFailureMessage(FfmpegState.Idle));
    }

    [Fact]
    public void ImmediateFailureMessage_LoadingCore_ReturnsInitializeMessage()
    {
        Assert.Equal(FfmpegBusyPolicy.NotInitializedMessage, FfmpegBusyPolicy.ImmediateFailureMessage(FfmpegState.LoadingCore));
    }

    [Fact]
    public void ImmediateFailureMessage_Error_ReturnsErrorMessage()
    {
        Assert.Equal(FfmpegBusyPolicy.ErrorMessage, FfmpegBusyPolicy.ImmediateFailureMessage(FfmpegState.Error));
    }

    [Fact]
    public void AllMessages_AreDistinctAndNonEmpty()
    {
        var messages = new[]
        {
            FfmpegBusyPolicy.NotInitializedMessage,
            FfmpegBusyPolicy.ErrorMessage,
            FfmpegBusyPolicy.WedgedMessage,
            FfmpegBusyPolicy.TimedOutWaitingMessage,
        };

        Assert.All(messages, m => Assert.False(string.IsNullOrWhiteSpace(m)));
        Assert.Equal(messages.Length, messages.Distinct().Count());
    }
}
