using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

public sealed class ExportSettingsTests
{
    [Fact]
    public void DefaultSettings_UseCrf_IsTrue()
    {
        var s = new ExportSettings();
        Assert.True(s.UseCrf);
    }

    [Fact]
    public void DefaultSettings_Crf_Is23()
    {
        var s = new ExportSettings();
        Assert.Equal(23, s.Crf);
    }

    [Fact]
    public void DefaultSettings_IncludeAudio_IsTrue()
    {
        var s = new ExportSettings();
        Assert.True(s.IncludeAudio);
    }

    [Fact]
    public void DefaultSettings_Format_IsMp4()
    {
        var s = new ExportSettings();
        Assert.Equal("mp4", s.OutputFormat);
    }

    [Fact]
    public void DefaultSettings_PixelFormat_IsYuv420p()
    {
        var s = new ExportSettings();
        Assert.Equal("yuv420p", s.PixelFormat);
    }

    [Fact]
    public void DefaultSettings_Preset_IsMedium()
    {
        var s = new ExportSettings();
        Assert.Equal("medium", s.Preset);
    }

    [Fact]
    public void DefaultSettings_OutputFilename_IsOutput()
    {
        var s = new ExportSettings();
        Assert.Equal("output", s.OutputFilename);
    }

    [Fact]
    public void Settings_CanDisableAudio()
    {
        var s = new ExportSettings { IncludeAudio = false };
        Assert.False(s.IncludeAudio);
    }

    [Fact]
    public void Settings_CanSetWebmFormat()
    {
        var s = new ExportSettings { OutputFormat = "webm", VideoCodec = "libvpx-vp9" };
        Assert.Equal("webm", s.OutputFormat);
        Assert.Equal("libvpx-vp9", s.VideoCodec);
    }
}
