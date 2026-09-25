using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Text;

namespace Ben.Canvas.Core.Templates;

/// <summary>A board somebody can start from instead of starting from nothing.</summary>
/// <param name="Id">What travels in a link. Lower case, hyphenated, and permanent once shipped.</param>
/// <param name="Name">What the person choosing sees.</param>
/// <param name="Summary">One line saying who it is for.</param>
/// <param name="IconName">A symbol in the site's Feather sprite, checked by a guard.</param>
/// <param name="Build">Lays the board out. Pure: the same board every time.</param>
public sealed record BoardTemplate(
    string Id,
    string Name,
    string Summary,
    string IconName,
    Action<BoardBuilder> Build);

/// <summary>
/// The five boards on offer when a new one is made.
/// </summary>
/// <remarks>
/// <para><b>Where these came from.</b> Ben sent four boards as pictures on 2026-09-18 — a moodboard of
/// coloured sections with a cluster of theme bubbles, a research plan of titled tiles holding a quadrant
/// and a calendar grid, a family tree of boxes joined at right angles with a legend, and a presentation
/// deck of slide frames. Each picture showed the same board twice: blank, and filled in. What is built
/// here is the blank half — the frames, the labels and the lines, with the words left to whoever opens
/// it. M9-01 to M9-07 built every piece these need (tables, shapes, panel groups, elbow and dashed
/// connectors, diamond ends); this is the assembly.</para>
///
/// <para><b>A template is a pure function, and deliberately not a server concept.</b> The server stores
/// whatever document the client posts, and a board is not stored at all until its first edit, so a
/// template is <c>id → CanvasDocument</c> and needs no endpoint, no table and no migration. The price of
/// that is that editing a template changes every FUTURE board made from it and no existing one, which is
/// the right way round: nobody's board is rewritten under them by a deploy.</para>
///
/// <para><b>Nothing here names a colour.</b> Every block and panel takes a palette key, because a literal
/// would be right in one theme and wrong in the other — and the palette exists so that cannot happen.
/// Likewise nothing here carries a board link: a template ships to every case, and a card pointing at
/// some other case's board would be a dead link on arrival and would refuse to publish.</para>
/// </remarks>
public static partial class BoardTemplates
{
    /// <summary>The id of the board that starts empty; also what an unknown id falls back to.</summary>
    public const string BlankId = "blank";

    public static IReadOnlyList<BoardTemplate> All { get; } =
    [
        new(BlankId, "Blank board", "Start with nothing and build it your own way.", "file", _ => { }),
        new("moodboard", "Moodboard", "Coloured sections for pictures and feel, with a cluster of themes.", "layout", Moodboard),
        new("research-plan", "Research plan", "What to ask, where to look, what matters most, and when.", "clipboard", ResearchPlan),
        new("family-tree", "Family tree", "Who is related to whom, in right angles, with a legend.", "git-merge", FamilyTree),
        new("deck", "Presentation deck", "Slide frames you can walk somebody through.", "monitor", Deck),
    ];

    /// <summary>The template with this id, or null.</summary>
    public static BoardTemplate? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Builds a new board. An id this build does not know opens a blank board rather than throwing,
    /// because the id arrives in an address fragment where anything can turn up and a stale link must
    /// not be a broken editor.
    /// </summary>
    public static CanvasDocument Create(string? id, Guid? caseId)
    {
        var template = Find(id);

        // A blank board is called what a blank board has always been called; the others are named for
        // the template, because "Family tree" is a better first title than "Untitled board".
        var title = template is null || template.Id == BlankId
            ? CanvasCopy.Titles.DefaultBoardTitle
            : template.Name;

        var builder = new BoardBuilder(title);
        template?.Build(builder);
        return builder.Finish(caseId);
    }
}
