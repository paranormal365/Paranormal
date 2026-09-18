using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Persistence;
using Ben.Canvas.Core.Templates;
using Ben.Canvas.Tests.Services;

namespace Ben.Canvas.Tests.Templates;

/// <summary>
/// Starting a board from a template through the device store — the thing the editor's startup does, at
/// the level where it can be run without a browser.
/// </summary>
public sealed class NewBoardFromTemplateTests
{
    [Fact]
    public async Task A_new_board_from_a_template_opens_with_its_frames()
    {
        var rig = await new DeviceRig().StartedAsync();

        rig.Documents.New(caseId: null, templateId: "deck");

        Assert.Equal(BoardTemplates.Create("deck", null).Nodes.Count, rig.Store.Document.Nodes.Count);
        Assert.Equal(4, rig.Store.Document.Groups.Count);
    }

    [Fact]
    public async Task A_new_board_with_no_template_is_still_empty()
    {
        var rig = await new DeviceRig().StartedAsync();

        rig.Documents.New(caseId: null);

        Assert.Empty(rig.Store.Document.Nodes);
    }

    [Fact]
    public async Task A_template_board_belongs_to_the_case_it_was_started_from()
    {
        var caseId = Guid.NewGuid();
        var rig = await new DeviceRig().StartedAsync();

        rig.Documents.New(caseId, "family-tree");

        Assert.Equal(caseId, rig.Store.Document.CaseId);
    }

    /// <summary>
    /// A template board is stored as it opens, unlike a blank one, and comes back on the next load.
    /// </summary>
    /// <remarks>
    /// "Not stored until its first edit" is right for an EMPTY board — there is nothing to lose. A
    /// template already holds what somebody asked for, so closing the tab before typing must not lose
    /// the frames and hand them a blank board the next time they open the editor. The editor's startup
    /// saves it; this pins that the save is enough to bring it back.
    /// </remarks>
    [Fact]
    public async Task A_template_board_saved_as_it_opens_comes_back_on_the_next_load()
    {
        var first = await new DeviceRig().StartedAsync();
        first.Documents.New(caseId: null, templateId: "moodboard");
        Assert.True(await first.Documents.SaveAsync());

        var second = await new DeviceRig(first.Storage).StartedAsync();
        var (restored, problem) = await second.Documents.RestoreLastActiveAsync();

        Assert.True(restored);
        Assert.Null(problem);
        Assert.Equal(first.Documents.CurrentLocalId, second.Documents.CurrentLocalId);
        Assert.Equal(4, second.Store.Document.Groups.Count);
        Assert.Equal(4, second.Store.Document.Nodes.Count(n => n.Type == CanvasNodeType.Shape));
    }

    /// <summary>
    /// Laying out a template is not an edit of its own: the board opens with a clean slate so the very
    /// first Ctrl+Z does not unbuild the template somebody just chose.
    /// </summary>
    [Fact]
    public async Task Laying_out_a_template_is_not_something_undo_can_reverse()
    {
        var rig = await new DeviceRig().StartedAsync();

        rig.Documents.New(caseId: null, templateId: "research-plan");

        Assert.False(rig.Store.CanUndo);
        Assert.False(rig.Documents.IsDirty);
        Assert.Equal(SaveStateKind.Clean, rig.Documents.State.Kind);
    }

    /// <summary>Each board from a template is its own board, not a second view of one.</summary>
    [Fact]
    public async Task Two_boards_from_one_template_are_two_boards()
    {
        var rig = await new DeviceRig().StartedAsync();

        rig.Documents.New(caseId: null, templateId: "deck");
        var first = rig.Documents.CurrentLocalId;
        var firstDocument = rig.Store.Document.Id;

        rig.Documents.New(caseId: null, templateId: "deck");

        Assert.NotEqual(first, rig.Documents.CurrentLocalId);
        Assert.NotEqual(firstDocument, rig.Store.Document.Id);
    }

    /// <summary>
    /// An id this build does not know starts an empty board rather than throwing inside startup, where
    /// the failure would be a blank screen instead of a board.
    /// </summary>
    [Fact]
    public async Task An_unknown_template_starts_an_empty_board_rather_than_throwing()
    {
        var rig = await new DeviceRig().StartedAsync();

        rig.Documents.New(caseId: null, templateId: "moodboard-v2");

        Assert.Empty(rig.Store.Document.Nodes);
    }
}
