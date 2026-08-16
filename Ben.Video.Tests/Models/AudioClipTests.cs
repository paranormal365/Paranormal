using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

public sealed class AudioClipTests
{
    // ── Default values ────────────────────────────────────────────────────────

    [Fact]
    public void NewAudioClip_HasUniqueId()
    {
        var a = new AudioClip();
        var b = new AudioClip();
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void NewAudioClip_DefaultVolume_IsOne()
    {
        var clip = new AudioClip();
        Assert.Equal(1.0, clip.Volume);
    }

    [Fact]
    public void NewAudioClip_DefaultFadeIn_IsZero()
    {
        var clip = new AudioClip();
        Assert.Equal(0.0, clip.FadeInSeconds);
    }

    [Fact]
    public void NewAudioClip_DefaultFadeOut_IsZero()
    {
        var clip = new AudioClip();
        Assert.Equal(0.0, clip.FadeOutSeconds);
    }

    [Fact]
    public void NewAudioClip_MemFsName_IsNull()
    {
        var clip = new AudioClip();
        Assert.Null(clip.MemFsName);
    }

    [Fact]
    public void NewAudioClip_BlobUrl_IsNull()
    {
        var clip = new AudioClip();
        Assert.Null(clip.BlobUrl);
    }

    [Fact]
    public void NewAudioClip_WaveformPeaks_IsNull()
    {
        var clip = new AudioClip();
        Assert.Null(clip.WaveformPeaks);
    }

    [Fact]
    public void AudioClip_IsAssignableFrom_TrackItem()
    {
        var clip = new AudioClip();
        Assert.IsAssignableFrom<TrackItem>(clip);
    }

    // ── Trim semantics ────────────────────────────────────────────────────────

    [Fact]
    public void StartTrim_And_EndTrim_DefaultToZero()
    {
        var clip = new AudioClip();
        Assert.Equal(0.0, clip.StartTrim);
        Assert.Equal(0.0, clip.EndTrim);
    }

    [Fact]
    public void AudioClip_CanSetTrimPoints()
    {
        var clip = new AudioClip { Duration = 60, StartTrim = 5, EndTrim = 55 };
        Assert.Equal(5.0, clip.StartTrim);
        Assert.Equal(55.0, clip.EndTrim);
    }

    // ── Mutability ────────────────────────────────────────────────────────────

    [Fact]
    public void AudioClip_BlobUrl_CanBeSet()
    {
        var clip = new AudioClip { BlobUrl = "blob:http://localhost/abc-123" };
        Assert.Equal("blob:http://localhost/abc-123", clip.BlobUrl);
    }

    [Fact]
    public void AudioClip_WaveformPeaks_CanBeSet()
    {
        var peaks = new float[] { 0.1f, 0.5f, 0.9f };
        var clip  = new AudioClip { WaveformPeaks = peaks };
        Assert.Equal(peaks, clip.WaveformPeaks);
    }

    [Fact]
    public void AudioClip_Volume_CanBeChanged()
    {
        var clip = new AudioClip { Volume = 0.5 };
        Assert.Equal(0.5, clip.Volume);
    }

    // ── Record semantics ──────────────────────────────────────────────────────

    [Fact]
    public void AudioClip_WithExpression_CopiesPropertiesExceptChanged()
    {
        var original = new AudioClip { Name = "original.mp3", Duration = 30, Volume = 0.8 };
        var copy     = original with { Name = "copy.mp3" };

        Assert.Equal("copy.mp3", copy.Name);
        Assert.Equal(30.0, copy.Duration);
        Assert.Equal(0.8, copy.Volume);
        // record `with` preserves the Id (init-only — not re-generated)
        Assert.Equal(original.Id, copy.Id);
    }

    [Fact]
    public void AudioClip_RecordEquality_SameId_AreEqual()
    {
        var id   = Guid.NewGuid();
        var a    = new AudioClip { Name = "a.mp3" };
        var b    = a with { };  // shallow copy — same Id

        Assert.Equal(a.Id, b.Id);
    }
}
