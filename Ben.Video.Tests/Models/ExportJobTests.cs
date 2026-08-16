using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

public sealed class ExportJobTests
{
    [Fact]
    public void NewJob_State_IsPending()
    {
        var job = new ExportJob();
        Assert.Equal(ExportJobState.Pending, job.State);
    }

    [Fact]
    public void NewJob_HasUniqueId()
    {
        var a = new ExportJob();
        var b = new ExportJob();
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void NewJob_CancelRequested_IsFalse()
    {
        var job = new ExportJob();
        Assert.False(job.CancelRequested);
    }

    [Fact]
    public void Cancel_SetsCancelRequested_True()
    {
        var job = new ExportJob();
        job.Cancel();
        Assert.True(job.CancelRequested);
    }

    [Fact]
    public void NotifyProgress_RaisesOnProgress()
    {
        var job    = new ExportJob();
        var raised = false;
        job.OnProgress += () => raised = true;

        job.NotifyProgress();

        Assert.True(raised);
    }

    [Fact]
    public void Elapsed_BeforeFinish_IsPositive()
    {
        var job = new ExportJob();
        Assert.True(job.Elapsed >= TimeSpan.Zero);
    }

    [Fact]
    public void CompletedPhases_StartsEmpty()
    {
        var job = new ExportJob();
        Assert.Empty(job.CompletedPhases);
    }

    [Fact]
    public void OverallPercent_DefaultsToZero()
    {
        var job = new ExportJob();
        Assert.Equal(0, job.OverallPercent);
    }

    [Fact]
    public void PhaseLabel_DefaultsToEmpty()
    {
        var job = new ExportJob();
        Assert.Equal(string.Empty, job.PhaseLabel);
    }

    [Fact]
    public void Settings_ReturnsAssignedSnapshot()
    {
        var s   = new ExportSettings { OutputFormat = "webm", Crf = 28 };
        var job = new ExportJob { Settings = s };
        Assert.Equal("webm", job.Settings.OutputFormat);
        Assert.Equal(28, job.Settings.Crf);
    }
}
