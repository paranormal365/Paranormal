using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

public sealed class VideoEditorOptionsTests
{
    [Fact]
    public void DefaultOptions_AllFeatureFlags_AreFalse()
    {
        var options = new VideoEditorOptions();

        Assert.False(options.MultiTrack);
        Assert.False(options.AudioTracks);
        Assert.False(options.Transitions);
        Assert.False(options.TextOverlays);
        Assert.True(options.InlineTrimming);  // default changed to true — trim handles on by default
    }

    [Fact]
    public void DefaultOptions_MaxTracks_HaveExpectedValues()
    {
        var options = new VideoEditorOptions();

        Assert.Equal(4, options.MaxVideoTracks);
        Assert.Equal(2, options.MaxAudioTracks);
    }

    [Fact]
    public void Options_AllFeatureFlags_CanBeEnabled()
    {
        var options = new VideoEditorOptions
        {
            MultiTrack     = true,
            AudioTracks    = true,
            Transitions    = true,
            TextOverlays   = true,
            InlineTrimming = true
        };

        Assert.True(options.MultiTrack);
        Assert.True(options.AudioTracks);
        Assert.True(options.Transitions);
        Assert.True(options.TextOverlays);
        Assert.True(options.InlineTrimming);
    }

    [Fact]
    public void DefaultOptions_DocumentUrls_AreNull()
    {
        var options = new VideoEditorOptions();

        Assert.Null(options.DocumentPostUrl);
        Assert.Null(options.DocumentSaveUrl);
        Assert.Null(options.MediaLibraryBaseUrl);
    }

    [Fact]
    public void Options_DocumentUrls_CanBeSet()
    {
        var options = new VideoEditorOptions
        {
            DocumentPostUrl    = "https://api.example.com/api/projects",
            DocumentSaveUrl    = "https://api.example.com/api/projects/1",
            MediaLibraryBaseUrl = "https://api.example.com",
        };

        Assert.Equal("https://api.example.com/api/projects",   options.DocumentPostUrl);
        Assert.Equal("https://api.example.com/api/projects/1", options.DocumentSaveUrl);
        Assert.Equal("https://api.example.com",                options.MediaLibraryBaseUrl);
    }
}
