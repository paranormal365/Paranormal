namespace Ben.Canvas.Core.Templates;

/// <summary>What the editor should open, given what the link asked for.</summary>
/// <param name="Template">
/// The template to lay out, or null to reopen the board this device had open last.
/// </param>
/// <param name="SayUnknownTemplate">
/// The id that was asked for, when this build has no such template — so the editor can say so. Null
/// when nothing needs saying.
/// </param>
public readonly record struct BoardOpenPlan(string? Template, string? SayUnknownTemplate)
{
    /// <summary>Reopen what was open last, rather than laying out a template.</summary>
    public bool RestoreDevice => Template is null;
}

/// <summary>
/// The order the three ways of arriving at the editor beat each other.
/// </summary>
/// <remarks>
/// <para>Somebody can arrive asking for a particular board (<c>doc=</c>), asking for a new board from a
/// template (<c>template=</c>), or asking for nothing — and this device may separately have a board it
/// had open last. Those can all be true at once, so the order matters:</para>
///
/// <list type="number">
///   <item><b>A board asked for by id wins.</b> It is the most specific thing anybody can ask for, and
///   laying a template over it would throw that board away. So a <c>template=</c> that arrives beside a
///   <c>doc=</c> is ignored, silently: the person got the board they clicked, which is what they
///   wanted, and a warning about a template they did not think about would only puzzle them.</item>
///   <item><b>Otherwise a template beats the device copy.</b> A template is something somebody clicked
///   a second ago; the device copy is whatever they happened to leave open, possibly days back. Letting
///   the older thing win would make "New board from a template" do nothing visible, which is the worst
///   kind of broken.</item>
///   <item><b>Otherwise reopen what was open.</b> The behaviour every load had before templates
///   existed.</item>
/// </list>
///
/// <para><b>An unknown id is still a new board, not a restore.</b> The id travels in a URL fragment, so
/// a stale link from an older version of the site can name a template this build dropped. Reopening
/// their last board instead would look like the click did nothing; a blank board plus a sentence says
/// what happened. This is why <see cref="BoardOpenPlan.Template"/> keeps the unknown id — the catalogue
/// turns it into a blank board on its own.</para>
///
/// <para>Pure, so the order above can be read back from the tests rather than inferred from a startup
/// sequence that needs a browser to run.</para>
/// </remarks>
public static class BoardOpenPolicy
{
    /// <summary>The plan for one load of the editor.</summary>
    /// <param name="documentId">The board the link asked for by id, or null.</param>
    /// <param name="templateId">The template the link asked for, or null.</param>
    public static BoardOpenPlan For(Guid? documentId, string? templateId)
    {
        if (documentId is not null) return new(null, null);

        var wanted = templateId?.Trim();
        if (string.IsNullOrEmpty(wanted)) return new(null, null);

        return new(wanted, BoardTemplates.Find(wanted) is null ? wanted : null);
    }
}
