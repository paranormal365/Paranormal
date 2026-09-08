using Ben.Video.Editor.Models;
using Xunit;

namespace Ben.Video.Tests.Models;

/// <summary>
/// Telling two identically named Server-tab rows apart (V-3, site evaluation 2026-09-06).
/// </summary>
/// <remarks>
/// The tab listed seven identical <c>test-audio.mp3</c> rows — other people's uploads, reachable
/// through a shared group — with no owner and no case. A file name is not an identity, and this
/// listing is the one place in the editor that spans several people's files at once.
/// </remarks>
public class MediaLibraryFileProvenanceTests
{
    private static MediaLibraryFile File(string? owner, string? caseRef) => new()
    {
        Id = Guid.NewGuid(),
        FileName = "test-audio.mp3",
        ContentType = "audio/mpeg",
        OwnerDisplayName = owner,
        CaseReference = caseRef,
    };

    [Fact]
    public void Both_read_as_one_line()
        => Assert.Equal("Sarah Mitchell · #2026-003",
                        File("Sarah Mitchell", "#2026-003").Provenance);

    [Fact]
    public void An_owner_alone_is_enough_to_tell_two_rows_apart()
        => Assert.Equal("Sarah Mitchell", File("Sarah Mitchell", null).Provenance);

    [Fact]
    public void A_case_alone_is_enough_too()
        => Assert.Equal("#2026-003", File(null, "#2026-003").Provenance);

    /// <summary>
    /// Neither known means no line at all, not an empty one.
    /// </summary>
    /// <remarks>
    /// A host that does not supply these — an older API, or the editor running against something
    /// else entirely — should render the card it always rendered, not a card with a blank row
    /// under the file size.
    /// </remarks>
    [Fact]
    public void Neither_known_produces_nothing_to_render()
        => Assert.Null(File(null, null).Provenance);
}
