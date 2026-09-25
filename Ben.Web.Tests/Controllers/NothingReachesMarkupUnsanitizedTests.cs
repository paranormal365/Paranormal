using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Nothing drawn as markup is stored with only a Trim (2026-09-20).
/// </summary>
/// <remarks>
/// <para><b>The rule, in the words of the helper that already existed:</b> "nothing between a
/// request body and a MarkupString on a public page". <c>CleanDescription</c> was written for the
/// case description when beta feedback gave it an editor. The sentence was right and the sweep was
/// never done, so the same gap sat open in four more places.</para>
///
/// <para><b>What the sweep found on 2026-09-20</b>, after Ben asked for it:</para>
/// <list type="bullet">
///   <item>A case's timeline entries — a client's own words, drawn on the investigator's screen.</item>
///   <item>A report's <c>Summary</c> and <c>Conclusion</c> — drawn on
///   <c>/o/{UrlName}/cases/{CaseRef}</c>, which needs no sign-in. The worst of them.</item>
///   <item>An investigation's <c>Notes</c>.</item>
///   <item>A region note's <c>NoteHtml</c>, stored exactly as sent — not even trimmed.</item>
/// </list>
///
/// <para><b>This checks the SHAPE, not a list of fields.</b> A list would go stale the first time
/// somebody added a field; the shape — a property assigned straight from a request with nothing
/// but a Trim, whose name the website hands to MarkupString — is what went wrong every time.</para>
/// </remarks>
public sealed class NothingReachesMarkupUnsanitizedTests
{
    /// <summary>
    /// Assignments that are allowed to be raw, keyed by "file:Property", and why.
    /// </summary>
    /// <remarks>
    /// Keyed by the FILE as well as the name, because the name alone cannot say what is meant: a
    /// case's Description is drawn as markup and a clipart asset's is not, and they are the same
    /// word. Five of the six entries below are exactly that — a field sharing a name with one that
    /// is drawn as markup somewhere else entirely.
    /// </remarks>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── "Description" is the most reused word in the codebase, and exactly one of them is
        // drawn as markup: a CASE's. Each of these was opened and checked on 2026-09-20; all are
        // drawn as text. A controller NOT on this list that writes a Description still fails,
        // which is the coverage worth having — that is how the public request form was found.
        ["AdminUploadFileTypeController.cs:Description"] =
            "What a kind of file is for, on the SuperAdmin file-types screen. Drawn as text.",

        ["ChunkedUploadController.cs:Description"] =
            "A caption travelling with an upload. Drawn as text wherever a file is listed.",

        ["ClientRequestController.cs:Description"] =
            "The GROUP's own copy of a request it already holds. The public door that takes one "
          + "from a stranger is PublicClientRequestController, which sanitizes since 2026-09-20.",

        ["EquipmentCatalogController.cs:Description"] =
            "A piece of gear in the shared catalogue. Drawn as text on the equipment screens.",

        ["ExperienceCategoryController.cs:Description"] =
            "A taxonomy entry — what a kind of experience means. Drawn as text.",

        ["OrgExperienceTypeController.cs:Description"] =
            "A group's own experience type. Drawn as text beside its name.",

        ["InvestigationController.cs:Description"] =
            "An investigation's own description, drawn as text. Its NOTES are the field drawn as "
          + "markup, and those are sanitized.",

        ["OrgInvestigationsController.cs:Description"] =
            "An investigation's description again, reached through the group's own door rather than "
          + "the case screen. Drawn as text in both places; its Notes are the markup field.",

        ["OrganizationFileController.cs:Description"] =
            "A caption on a file a group keeps. Drawn as text in the file list.",

        ["OrganizationRoleController.cs:Description"] =
            "What a custom role is for — \"can accept requests and close cases\" — drawn as text beside "
          + "the role name in the editor and in the members list.",

        ["UploadFileController.cs:Description"] =
            "A caption somebody types when uploading a file. Drawn as text in the media library, in the "
          + "picker and on a case's files tab.",

        ["ScheduleProposalController.cs:Notes"] =
            "A note on a proposed visit time — \"we could do Thursday instead\" — drawn as text "
          + "on the scheduling panel. The Notes drawn as markup belongs to the INVESTIGATION.",

        ["MyCaseController.cs:Notes"] =
            "A person on a case — who they are and what they saw. The Notes drawn as markup is an "
          + "INVESTIGATION's, on InvestigationPanel; a case person's notes are drawn as text.",

        ["AdminBillingController.cs:Notes"] =
            "A tax rule's working note, on a SuperAdmin billing screen. Nothing renders it as "
          + "markup; it shares a word with an investigation's notes.",

        ["OrgPublicationController.cs:Description"] =
            "A publication's blurb. The publication reader renders BodyHtml, not Description, and "
          + "no screen draws this one as markup.",

        ["OrgMemberGroupController.cs:Description"] =
            "What a group of members is for — drawn as text on the members screen. It shares a "
          + "word with a case's description, which is the one drawn as markup.",

        ["AdminVideoAssetController.cs:Description"] =
            "A clipart item's caption in the SuperAdmin library, drawn as text. Same word, "
          + "different field.",

        ["OrgCmsPageController.cs:PageHtml"] =
            "The CMS page builder, whose whole purpose is authoring HTML. It is drawn only in the "
          + "group's own editor and preview (OrgCmsPageEdit, OrgCmsEditor) — a published page is "
          + "rendered from ContentJson, which OrgCmsPageController DOES sanitize. Checked "
          + "2026-09-20: PageHtml appears on no public surface.",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Property names the website hands to the browser as markup.</summary>
    private static HashSet<string> DrawnAsMarkup()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in new[] { "Ben.Web.Website.Library", "Ben.Web.Website" })
        {
            var root = Path.Combine(RepoRoot(), project);
            if (!Directory.Exists(root)) continue;

            foreach (var file in Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

                foreach (Match m in Regex.Matches(File.ReadAllText(file),
                                                  @"MarkupString\)\s*\(?\s*[\w_\.\?]*?\.(\w+)"))
                    names.Add(m.Groups[1].Value);
            }
        }

        return names;
    }

    [Fact]
    public void No_controller_stores_a_markup_field_straight_from_the_request()
    {
        var markup = DrawnAsMarkup();
        Assert.NotEmpty(markup);

        var offenders = new List<string>();
        var controllers = Path.Combine(RepoRoot(), "Ben.Data.WebApi", "Controllers");

        foreach (var file in Directory.EnumerateFiles(controllers, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                // "X.Foo = request.Foo?.Trim();" or "Foo = request.Foo," — raw, from the caller.
                // A local is not a stored value — "var body = request.Body?.Trim();" is usually
                // one line above a sanitizing call, and counting it buried the real findings in
                // noise the first time this ran.
                if (Regex.IsMatch(lines[i], @"^\s*(var|string\??)\s+\w+\s*=")) continue;

                var m = Regex.Match(lines[i],
                    @"\b(\w+)\s*=\s*request\.\w+(\?\.Trim\(\))?\s*[,;]");
                if (!m.Success) continue;

                var property = m.Groups[1].Value;
                if (!markup.Contains(property)) continue;
                if (Allowed.ContainsKey($"{Path.GetFileName(file)}:{property}")) continue;

                offenders.Add($"{Path.GetFileName(file)}:{i + 1}  {lines[i].Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These store a value the website later draws with MarkupString, straight from the "
          + "request:" + Environment.NewLine + "  "
          + string.Join(Environment.NewLine + "  ", offenders)
          + Environment.NewLine + Environment.NewLine
          + "Put it through CaseController.CleanDescription, or record in "
          + $"{nameof(Allowed)} why raw markup is the point there.");
    }

    [Fact]
    public void Nothing_is_allowed_that_is_no_longer_drawn_as_markup()
    {
        var markup = DrawnAsMarkup();
        var stale = Allowed.Keys
            .Where(k => !markup.Contains(k[(k.IndexOf(':') + 1)..]))
            .ToList();

        Assert.True(stale.Count == 0,
            "These are excused but nothing draws them as markup any more:" + Environment.NewLine
          + "  " + string.Join(Environment.NewLine + "  ", stale));
    }

    [Fact]
    public void Every_exception_says_what_was_checked()
        => Assert.All(Allowed, pair =>
            Assert.True(pair.Value.Length > 60,
                $"{pair.Key}'s note is too short to say what was actually checked."));
}
