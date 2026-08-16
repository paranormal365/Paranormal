using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

public sealed class ErrorLogServiceTests
{
    [Fact]
    public void Log_AddsEntry()
    {
        var svc = new ErrorLogService();

        svc.Log("TestSource", "Something failed");

        Assert.Single(svc.Entries);
        Assert.Equal("TestSource", svc.Entries[0].Source);
        Assert.Equal("Something failed", svc.Entries[0].Message);
    }

    [Fact]
    public void Log_WithException_StoresMessageAndDetail()
    {
        var svc = new ErrorLogService();
        var ex  = new InvalidOperationException("boom");

        svc.Log("Src", ex);

        Assert.Equal("boom", svc.Entries[0].Message);
        Assert.Contains("InvalidOperationException", svc.Entries[0].Detail);
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var svc = new ErrorLogService();
        svc.Log("A", "msg1");
        svc.Log("B", "msg2");

        svc.Clear();

        Assert.Empty(svc.Entries);
        Assert.False(svc.HasEntries);
    }

    [Fact]
    public void HasEntries_FalseWhenEmpty()
    {
        var svc = new ErrorLogService();

        Assert.False(svc.HasEntries);
    }

    [Fact]
    public void ExportText_ReturnsNoErrorsMessageWhenEmpty()
    {
        var svc  = new ErrorLogService();
        var text = svc.ExportText();

        Assert.Contains("No errors", text);
    }

    [Fact]
    public void ExportText_ContainsSourceAndMessage()
    {
        var svc = new ErrorLogService();
        svc.Log("ffmpeg", "NaN progress");

        var text = svc.ExportText();

        Assert.Contains("ffmpeg", text);
        Assert.Contains("NaN progress", text);
    }
}
