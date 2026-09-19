using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Which stored media files nothing refers to any more.
/// </summary>
/// <remarks>
/// Nothing ever freed a source. Every import writes a copy of the file into the browser's own
/// storage so the project can be reopened, and removing the clip, deleting the project, or simply
/// closing the tab left that copy behind forever. A few sessions with large footage fill the quota,
/// at which point saving starts failing (2026-09-05 audit, media-2 and persistence-12).
/// </remarks>
public sealed class OpfsGarbageCollectorTests
{
    private static readonly Guid Used   = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Orphan = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void A_file_no_project_mentions_is_an_orphan()
    {
        var orphans = OpfsGarbageCollector.FindOrphans([Used, Orphan], [Used]);

        Assert.Equal([Orphan], orphans);
    }

    [Fact]
    public void A_file_a_project_still_uses_is_left_alone()
    {
        Assert.Empty(OpfsGarbageCollector.FindOrphans([Used], [Used]));
    }

    /// <summary>
    /// One reference anywhere is enough — a file shared by two projects is used by both.
    /// </summary>
    [Fact]
    public void A_file_shared_by_two_projects_survives_one_of_them_being_deleted()
    {
        Assert.Empty(OpfsGarbageCollector.FindOrphans([Used], [Used, Used]));
    }

    [Fact]
    public void Storage_with_nothing_in_it_yields_nothing_to_do()
    {
        Assert.Empty(OpfsGarbageCollector.FindOrphans([], [Used]));
    }

    [Fact]
    public void The_same_file_listed_twice_is_reported_once()
    {
        Assert.Single(OpfsGarbageCollector.FindOrphans([Orphan, Orphan], []));
    }

    // ── The refusal that stops it destroying anything ─────────────────────────

    /// <summary>
    /// A failure to read the project list is not evidence that there are no projects.
    /// </summary>
    /// <remarks>
    /// This is the check that turns housekeeping into something that cannot destroy anything: with
    /// the index unread, every stored file looks unreferenced, and sweeping on that basis would
    /// delete the media for every project the person has.
    /// </remarks>
    [Fact]
    public void Nothing_is_swept_when_the_project_list_could_not_be_read()
    {
        Assert.False(OpfsGarbageCollector.CanSweep(
            projectIndexWasRead: false, knownProjectCount: 0, storedFileCount: 40));
    }

    /// <summary>
    /// Storage full of files and not one project to explain them says the index is wrong, not that
    /// forty files are all garbage.
    /// </summary>
    [Fact]
    public void Nothing_is_swept_when_the_numbers_do_not_add_up()
    {
        Assert.False(OpfsGarbageCollector.CanSweep(
            projectIndexWasRead: true, knownProjectCount: 0, storedFileCount: 40));
    }

    [Fact]
    public void Sweeping_is_allowed_once_there_is_a_project_to_reconcile_against()
    {
        Assert.True(OpfsGarbageCollector.CanSweep(
            projectIndexWasRead: true, knownProjectCount: 1, storedFileCount: 40));
    }

    /// <summary>
    /// No projects and no files is a genuinely empty state, not a suspicious one.
    /// </summary>
    [Fact]
    public void An_empty_editor_is_not_treated_as_suspicious()
    {
        Assert.True(OpfsGarbageCollector.CanSweep(
            projectIndexWasRead: true, knownProjectCount: 0, storedFileCount: 0));
    }
}
