using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

public sealed class ExportPresetsTests
{
    // ── All ───────────────────────────────────────────────────────────────

    [Fact]
    public void All_ContainsFivePresets()
    {
        Assert.Equal(5, ExportPresets.All.Count);
    }

    [Fact]
    public void All_EachFactory_ReturnsNonNullSettings()
    {
        foreach (var (_, factory) in ExportPresets.All)
            Assert.NotNull(factory());
    }

    [Fact]
    public void All_Labels_AreUnique()
    {
        var labels = ExportPresets.All.Select(p => p.Label).ToList();
        Assert.Equal(labels.Count, labels.Distinct().Count());
    }

    // ── WebHd ─────────────────────────────────────────────────────────────

    [Fact]
    public void WebHd_Format_IsMp4()
        => Assert.Equal("mp4", ExportPresets.WebHd().OutputFormat);

    [Fact]
    public void WebHd_VideoCodec_IsLibx264()
        => Assert.Equal("libx264", ExportPresets.WebHd().VideoCodec);

    [Fact]
    public void WebHd_Resolution_Is1080p()
        => Assert.Equal("1920x1080", ExportPresets.WebHd().Resolution);

    [Fact]
    public void WebHd_UseCrf_IsTrue()
        => Assert.True(ExportPresets.WebHd().UseCrf);

    [Fact]
    public void WebHd_Crf_Is23()
        => Assert.Equal(23, ExportPresets.WebHd().Crf);

    [Fact]
    public void WebHd_AudioBitrate_Is192()
        => Assert.Equal(192, ExportPresets.WebHd().AudioBitrate);

    // ── HighQuality1080p ─────────────────────────────────────────────────

    [Fact]
    public void HighQuality1080p_Crf_Is18()
        => Assert.Equal(18, ExportPresets.HighQuality1080p().Crf);

    [Fact]
    public void HighQuality1080p_AudioBitrate_Is320()
        => Assert.Equal(320, ExportPresets.HighQuality1080p().AudioBitrate);

    [Fact]
    public void HighQuality1080p_Resolution_Is1080p()
        => Assert.Equal("1920x1080", ExportPresets.HighQuality1080p().Resolution);

    // ── Standard720p ─────────────────────────────────────────────────────

    [Fact]
    public void Standard720p_Resolution_Is720p()
        => Assert.Equal("1280x720", ExportPresets.Standard720p().Resolution);

    [Fact]
    public void Standard720p_AudioBitrate_Is128()
        => Assert.Equal(128, ExportPresets.Standard720p().AudioBitrate);

    [Fact]
    public void Standard720p_Format_IsMp4()
        => Assert.Equal("mp4", ExportPresets.Standard720p().OutputFormat);

    // ── Mobile ────────────────────────────────────────────────────────────

    [Fact]
    public void Mobile_Resolution_Is480p()
        => Assert.Equal("854x480", ExportPresets.Mobile().Resolution);

    [Fact]
    public void Mobile_Crf_Is28()
        => Assert.Equal(28, ExportPresets.Mobile().Crf);

    [Fact]
    public void Mobile_AudioBitrate_Is96()
        => Assert.Equal(96, ExportPresets.Mobile().AudioBitrate);

    // ── WebM ─────────────────────────────────────────────────────────────

    [Fact]
    public void WebM_Format_IsWebm()
        => Assert.Equal("webm", ExportPresets.WebM().OutputFormat);

    [Fact]
    public void WebM_VideoCodec_IsVp9()
        => Assert.Equal("libvpx-vp9", ExportPresets.WebM().VideoCodec);

    [Fact]
    public void WebM_AudioCodec_IsOpus()
        => Assert.Equal("libopus", ExportPresets.WebM().AudioCodec);

    [Fact]
    public void WebM_AudioBitrate_Is128()
        => Assert.Equal(128, ExportPresets.WebM().AudioBitrate);

    // ── IncludeAudio default ──────────────────────────────────────────────

    [Theory]
    [InlineData("WebHd")]
    [InlineData("HighQuality1080p")]
    [InlineData("Standard720p")]
    [InlineData("Mobile")]
    [InlineData("WebM")]
    public void AllPresets_IncludeAudio_IsTrue(string presetName)
    {
        var settings = presetName switch
        {
            "WebHd"           => ExportPresets.WebHd(),
            "HighQuality1080p"=> ExportPresets.HighQuality1080p(),
            "Standard720p"    => ExportPresets.Standard720p(),
            "Mobile"          => ExportPresets.Mobile(),
            "WebM"            => ExportPresets.WebM(),
            _                 => throw new ArgumentOutOfRangeException(presetName)
        };
        Assert.True(settings.IncludeAudio);
    }

    // ── OutputFilename ────────────────────────────────────────────────────

    [Fact]
    public void AllPresets_OutputFilename_IsNotEmpty()
    {
        foreach (var (_, factory) in ExportPresets.All)
            Assert.False(string.IsNullOrWhiteSpace(factory().OutputFilename));
    }
}
