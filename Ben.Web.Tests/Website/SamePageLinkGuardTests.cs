using System.Text.RegularExpressions;
using Ben.Web.Tests.Support;
using Ben.Web.Website.Library.Kit;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// No link on the website is a bare <c>href="#something"</c>.
/// </summary>
/// <remarks>
/// <para><b>What went wrong.</b> The site has <c>&lt;base href="/"&gt;</c>, and a browser resolves a
/// bare fragment against the base, not the page. On a hosted event's public page the phone's
/// "Ask for a place" was <c>href="#event-act"</c>, which means <c>https://ishaunted.com/#event-act</c>,
/// so a visitor who pressed it landed on the home page instead of the request form (production,
/// 10/16/2026). Thirty-one more links across nine files had the same fault (the settings index,
/// the tour and event manage pages, the tour page, a case's comments button, the SuperAdmin
/// dashboard); the two on the home page only worked because the home page's path is
/// <c>/</c>.</para>
///
/// <para><b>The rule.</b> A link to a spot on its own page is written with
/// <see cref="JumpLink"/>, which puts the page's own path and query in front of the fragment.
/// A link to a spot on another page names that page (<c>/help/your-profile#photos</c>).
/// A bare <c>href="#"</c> on a do-nothing link with an <c>@onclick</c> is a different thing and is
/// not matched here; App.razor keeps those from leaving the page.</para>
///
/// <para>Matches <c>href=</c>, a component's <c>Href=</c> parameter, and <c>Href: "#…"</c> in C#
/// or <c>href: '#…'</c> in script, because a fact tile handed <c>"#tour-dates"</c> renders exactly
/// the same broken anchor.</para>
/// </remarks>
public sealed class SamePageLinkGuardTests
{
    /// <summary>
    /// Files allowed a bare fragment, with the text that is allowed. Empty, and it should stay
    /// that way: an entry here is a link that goes to the home page on every page but one.
    /// </summary>
    private static readonly (string File, string Text)[] Allowed = [];

    /// <summary><c>href="#x"</c>, <c>Href="#x"</c>, <c>Href: "#x"</c>, <c>href = '#x'</c> — never a lone <c>#</c>.</summary>
    private static readonly Regex BareFragment = new(
        @"\bhref\s*[=:]\s*([""'`])#[^""'`\s]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly string[] Projects = ["Ben.Web.Website", "Ben.Web.Website.Library"];

    private static readonly string[] Extensions = [".razor", ".cshtml", ".html", ".js", ".cs"];

    /// <summary>
    /// Razor, HTML, block and line comments out, so prose explaining the rule (this file, JumpLink,
    /// App.razor) is not mistaken for a link.
    /// </summary>
    /// <remarks>
    /// A block comment must start a token, so <c>accept="image/*"</c> is not taken for one and made
    /// to swallow the file; a line comment may not follow a colon or a quote, so
    /// <c>https://…</c> and <c>"//cdn…"</c> survive.
    /// </remarks>
    internal static string StripComments(string source)
    {
        var text = Regex.Replace(source, @"@\*.*?\*@", KeepLines, RegexOptions.Singleline);
        text = Regex.Replace(text, @"<!--.*?-->", KeepLines, RegexOptions.Singleline);
        text = Regex.Replace(text, @"(?<![\w""'/*])/\*.*?\*/", KeepLines, RegexOptions.Singleline);
        return Regex.Replace(text, @"(?<![:\w""'/])//[^\n]*", "");
    }

    /// <summary>A comment becomes the line breaks it spanned, so a reported line number is the file's own.</summary>
    private static string KeepLines(Match comment) => new('\n', comment.Value.Count(c => c == '\n'));

    /// <summary>Each offending line, as "file:line: text".</summary>
    internal static IEnumerable<(int Line, string Text)> Offences(string source)
    {
        var lines = StripComments(source).Split('\n');
        for (var i = 0; i < lines.Length; i++)
            if (BareFragment.IsMatch(lines[i]))
                yield return (i + 1, lines[i].Trim());
    }

    /// <summary>The website's own files: no build output, no vendored library, no minified bundle.</summary>
    private static IEnumerable<string> WebsiteFiles()
    {
        var root = RepoFiles.Root().FullName;
        var prefixes = Projects.Select(p => Path.Combine(root, p) + Path.DirectorySeparatorChar).ToArray();

        return Extensions
            .SelectMany(ext => RepoFiles.Paths("*" + ext))
            .Where(f => prefixes.Any(p => f.StartsWith(p, StringComparison.Ordinal)))
            .Where(f => !f.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsVendored(f, root))
            .Distinct()
            .Order(StringComparer.Ordinal);
    }

    /// <summary>A file under a folder that carries a VENDORED.md is somebody else's code.</summary>
    private static bool IsVendored(string file, string root)
    {
        for (var dir = Path.GetDirectoryName(file); dir is not null && dir.Length > root.Length; dir = Path.GetDirectoryName(dir))
            if (File.Exists(Path.Combine(dir, "VENDORED.md"))) return true;
        return false;
    }

    private static string Relative(string path)
        => Path.GetRelativePath(RepoFiles.Root().FullName, path).Replace(Path.DirectorySeparatorChar, '/');

    [Fact]
    public void No_website_link_is_a_bare_fragment()
    {
        var files = WebsiteFiles().ToList();
        Assert.True(files.Count > 500, $"Only {files.Count} website files were found to scan; the guard has lost its subject.");

        var offences = files
            .SelectMany(f => Offences(File.ReadAllText(f)).Select(o => (File: Relative(f), o.Line, o.Text)))
            .Where(o => !Allowed.Any(a => a.File == o.File && o.Text.Contains(a.Text, StringComparison.Ordinal)))
            .Select(o => $"{o.File}:{o.Line}: {o.Text}")
            .ToList();

        Assert.True(offences.Count == 0,
            $"""
             {offences.Count} link(s) are a bare fragment. With <base href="/"> the browser reads
             "#x" as the HOME page's #x, so these leave the page they are on. Write
             href="@JumpLink.To(Nav, "x")" for a spot on the same page, or name the page for a
             spot on another one.

               {string.Join("\n  ", offences)}
             """);
    }

    [Fact]
    public void Every_allowed_fragment_is_still_there()
    {
        foreach (var (file, text) in Allowed)
        {
            var path = Path.Combine(RepoFiles.Root().FullName, file.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"{file} is allowed a bare fragment but no longer exists. Remove it from the list.");
            Assert.True(Offences(File.ReadAllText(path)).Any(o => o.Text.Contains(text, StringComparison.Ordinal)),
                $"{file} is allowed \"{text}\" but no longer has it. Remove it from the list.");
        }
    }

    // ── the scan discriminates ───────────────────────────────────────────────

    [Theory]
    [InlineData("""<a class="btn" href="#event-act" id="hosted-sticky-ask">""")]
    [InlineData("""<a href='#org-tours'>Tours</a>""")]
    [InlineData("""<StatCard Label="x" Href="#sign-ins" />""")]
    [InlineData("""facts.Add(new BenFactItem("Pictures", "3", "from the walk", Href: "#tour-slideshow"));""")]
    [InlineData("""link.href = `#${id}`;""")]
    public void It_catches_a_bare_fragment(string line)
        => Assert.Single(Offences(line));

    [Theory]
    [InlineData("""<a href="#" @onclick="Toggle" @onclick:preventDefault>""")]
    [InlineData("""<a href="@JumpLink.To(Nav, "event-act")">""")]
    [InlineData("""<a href="/help/your-profile#photos">""")]
    [InlineData("""<a href="@($"/o/{UrlName}#org-tours")">""")]
    [InlineData("""@* this used to be href="#event-act" *@""")]
    [InlineData("""<!-- href="#event-act" -->""")]
    [InlineData("""// was Href: "#tour-dates" """)]
    [InlineData("""<button data-bs-target="#acc1-one">""")]
    public void It_leaves_everything_else_alone(string line)
        => Assert.Empty(Offences(line));

    [Fact]
    public void A_file_input_accept_list_is_not_read_as_a_comment()
    {
        var source = """
                     <input type="file" accept="image/*,audio/*" />
                     <a href="#x">jump</a>
                     <p>closing */ further down</p>
                     """;
        Assert.Single(Offences(source));
    }

    // ── the helper the rule points at ────────────────────────────────────────

    [Theory]
    [InlineData("https://ishaunted.com/o/apple-beta/events/app-review-night", "event-act",
                "/o/apple-beta/events/app-review-night#event-act")]
    [InlineData("https://ishaunted.com/o/apple-beta/events/app-review-night#hosted-room", "event-act",
                "/o/apple-beta/events/app-review-night#event-act")]
    [InlineData("https://ishaunted.com/?from=nearby", "#the-app", "/?from=nearby#the-app")]
    [InlineData("https://ishaunted.com/", "the-app", "/#the-app")]
    [InlineData("not a url", "x", "/#x")]
    public void A_jump_link_names_its_own_page(string uri, string id, string expected)
        => Assert.Equal(expected, JumpLink.To(uri, id));
}
