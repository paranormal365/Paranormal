using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Replacing a clip's media keeps the replacement.
/// </summary>
/// <remarks>
/// Re-linking used to write the browser's session filesystem and nothing else, so somebody who
/// patiently re-linked eight clips found all eight missing again the next time they opened the
/// project — the one repair the editor offered did not survive being used (2026-09-05 audit, F14).
/// </remarks>
public sealed class RelinkPersistenceTests
{
    private static ClipStore Store() =>
        new(Options.Create(new VideoEditorOptions { MultiTrack = true, AudioTracks = true }));

    private static VideoClip Missing(ClipStore store)
    {
        var clip = new VideoClip { Name = "porch", Duration = 5, IsMediaMissing = true };
        store.AddClip(clip);
        return clip;
    }

    [Fact]
    public void A_replaced_clip_records_where_its_media_is_stored()
    {
        var store = Store();
        var clip  = Missing(store);

        store.RelinkClip(clip.Id, "porch.mp4", ".mp4", sourceFileSize: 2048);

        Assert.False(clip.IsMediaMissing);
        Assert.Equal(".mp4", clip.OpfsExt);
        Assert.Equal(2048, clip.SourceFileSize);
    }

    /// <summary>
    /// An image clip was silently not handled: its session path was left alone while the clip was
    /// marked present, so re-linking a picture produced a clip that claimed media and had none.
    /// </summary>
    [Fact]
    public void An_image_can_be_replaced_too()
    {
        var store = Store();
        var image = new ImageClip { Name = "photo", Duration = 5, IsMediaMissing = true };
        store.AddImageClip(image);

        store.RelinkClip(image.Id, "photo.jpg", ".jpg");

        Assert.Equal("photo.jpg", image.MemFsName);
        Assert.False(image.IsMediaMissing);
    }

    /// <summary>
    /// Picking a different file gives up the server identity. Keeping it would let a later
    /// re-fetch quietly overwrite the replacement with the footage it was chosen instead of.
    /// </summary>
    [Fact]
    public void Replacing_with_a_different_file_gives_up_the_old_identity()
    {
        var store = Store();
        var clip  = Missing(store);
        clip.SourceFileId      = Guid.NewGuid();
        clip.SourceFileSize    = 1024;
        clip.SourceContentHash = "old";

        store.RelinkClip(clip.Id, "other.mp4", ".mp4",
            sourceFileId: null, sourceFileSize: 999, sourceContentHash: "new");

        Assert.Null(clip.SourceFileId);
        Assert.Equal(999, clip.SourceFileSize);
        Assert.Equal("new", clip.SourceContentHash);
    }

    /// <summary>
    /// Undo puts back everything the replacement changed, not only the session path — otherwise
    /// undoing leaves a clip pointing at the old media while claiming the new file's identity.
    /// </summary>
    [Fact]
    public void Undoing_a_replacement_restores_what_it_recorded()
    {
        var store    = Store();
        var clip     = Missing(store);
        var original = Guid.NewGuid();
        clip.SourceFileId      = original;
        clip.SourceFileSize    = 1024;
        clip.SourceContentHash = "old";
        clip.OpfsExt           = ".mov";

        store.RelinkClip(clip.Id, "other.mp4", ".mp4",
            sourceFileId: null, sourceFileSize: 999, sourceContentHash: "new");
        store.Undo();

        Assert.Equal(original, clip.SourceFileId);
        Assert.Equal(1024, clip.SourceFileSize);
        Assert.Equal("old", clip.SourceContentHash);
        Assert.Equal(".mov", clip.OpfsExt);
        Assert.True(clip.IsMediaMissing);
    }

    [Fact]
    public void And_redo_applies_it_again()
    {
        var store = Store();
        var clip  = Missing(store);

        store.RelinkClip(clip.Id, "other.mp4", ".mp4", sourceFileSize: 999);
        store.Undo();
        store.Redo();

        Assert.Equal("other.mp4", clip.MemFsName);
        Assert.Equal(".mp4", clip.OpfsExt);
        Assert.Equal(999, clip.SourceFileSize);
        Assert.False(clip.IsMediaMissing);
    }
}
