using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Which missing clips the editor may offer to fetch back.
/// </summary>
/// <remarks>
/// Offering to re-fetch a clip imported straight off somebody's disk would be a promise the editor
/// cannot keep — that file exists only where they put it (2026-09-05 audit, F14).
/// </remarks>
public sealed class MediaRelinkCandidateTests
{
    private static VideoClip Missing(Guid? sourceFileId, string? ext = ".mp4", long? size = 1024) =>
        new()
        {
            Name           = "clip",
            IsMediaMissing = true,
            OpfsExt        = ext,
            SourceFileId   = sourceFileId,
            SourceFileSize = size,
        };

    [Fact]
    public void A_missing_clip_from_the_server_can_be_fetched_back()
    {
        var fileId = Guid.NewGuid();
        var clip   = Missing(fileId);

        var candidate = Assert.Single(MediaRelinkService.Candidates([clip]));

        Assert.Equal(clip.Id, candidate.ClipId);
        Assert.Equal(fileId, candidate.SourceFileId);
        Assert.Equal(".mp4", candidate.Ext);
        Assert.Equal(1024, candidate.SizeBytes);
    }

    [Fact]
    public void A_clip_imported_from_somebodys_own_machine_cannot()
        => Assert.Empty(MediaRelinkService.Candidates([Missing(sourceFileId: null)]));

    /// <summary>
    /// The extension is where the fetch lands, and the restore looks there. Without it there is
    /// nowhere to put the file.
    /// </summary>
    [Fact]
    public void Nor_can_one_with_no_stored_extension()
        => Assert.Empty(MediaRelinkService.Candidates([Missing(Guid.NewGuid(), ext: null)]));

    [Fact]
    public void A_clip_whose_media_is_already_here_is_left_alone()
    {
        var clip = Missing(Guid.NewGuid());
        clip.IsMediaMissing = false;

        Assert.Empty(MediaRelinkService.Candidates([clip]));
    }

    /// <summary>
    /// An unrecorded size is carried through rather than dropped, because the plan treats it as a
    /// reason to ask rather than as nothing.
    /// </summary>
    [Fact]
    public void An_unrecorded_size_is_reported_as_unknown_not_as_zero()
    {
        var candidate = Assert.Single(
            MediaRelinkService.Candidates([Missing(Guid.NewGuid(), size: null)]));

        Assert.Null(candidate.SizeBytes);
    }

    [Fact]
    public void Audio_and_images_come_back_too()
    {
        var audio = new AudioClip
        {
            Name = "evp", IsMediaMissing = true, OpfsExt = ".m4a", SourceFileId = Guid.NewGuid(),
        };
        var image = new ImageClip
        {
            Name = "photo", IsMediaMissing = true, OpfsExt = ".jpg", SourceFileId = Guid.NewGuid(),
        };

        Assert.Equal(2, MediaRelinkService.Candidates([audio, image]).Count);
    }
}
