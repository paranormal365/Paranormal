using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// A dialog's commit button is never disabled by a field the browser already knows about.
/// </summary>
/// <remarks>
/// <para><b>W-A12 of the 2026-09-06 evaluation.</b> "In dialogs the first click on the primary
/// button after typing did nothing and the second identical click worked" — reported on Verify
/// Address, Look Up, Create, Add and Make Public, with a note that it needed a human repro before
/// anyone fixed anything, and W-S6 named as the suspect.</para>
///
/// <para>W-S6 was not it: this build opens one circuit and keeps it across navigations. The cause
/// is that these buttons were <c>disabled</c> whenever their field looked empty, and the field
/// binds on <c>oninput</c> — so "empty" is the server's opinion, one circuit round trip behind the
/// keyboard. A click landing in that window is discarded by the browser, silently, because a
/// disabled button dispatches nothing. Reproduced through Playwright's trusted input, where it
/// failed intermittently, which is what a race looks like.</para>
///
/// <para><b>The rule is narrow on purpose.</b> A search box whose button is disabled until you
/// type something has the same race and does no harm — you click again and search. The harm is in
/// a dialog, where the click IS the commit and the person walks away believing they saved. So this
/// guards the four commit buttons the evaluation named, by id, rather than every button on the
/// site that gates on a field: there are around fifty of those, and turning each one into a
/// spoken refusal is a judgement per site, not a sweep.</para>
///
/// <para>The fifth thing the evaluation named, Make Public, is a checkbox and not this mechanism.
/// It is not covered here.</para>
/// </remarks>
public sealed class CommitButtonGateGuardTests
{
    /// <summary>Each commit button, by id, and the file it lives in.</summary>
    private static readonly (string Id, string File)[] CommitButtons =
    [
        ("report-create",      "ReportBuilder.razor"),
        ("report-add-section", "ReportBuilder.razor"),
        ("req-verify-address", "ClientRequestWizard.razor"),
        ("area-look-up",       "AreaOfOperationDialog.razor"),
    ];

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    /// <summary>The whole element, from its opening angle bracket to the closing one.</summary>
    private static string? ElementWithId(string markup, string id)
    {
        var at = markup.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
        if (at < 0) return null;

        var open = markup.LastIndexOf('<', at);
        var close = markup.IndexOf('>', at);
        return open < 0 || close < 0 ? null : markup[open..close];
    }

    [Fact]
    public void No_commit_button_is_gated_on_a_field_the_browser_owns()
    {
        var library = new DirectoryInfo(Path.Combine(RepoRoot().FullName, "Ben.Web.Website.Library"));
        var offences = new List<string>();

        foreach (var (id, fileName) in CommitButtons)
        {
            var file = library.EnumerateFiles(fileName, SearchOption.AllDirectories).FirstOrDefault();
            Assert.True(file is not null,
                $"{fileName} is guarded here but no longer exists. Update this list.");

            var element = ElementWithId(File.ReadAllText(file!.FullName), id);
            Assert.True(element is not null,
                $"No element with id=\"{id}\" in {fileName}. The guard has lost its subject — "
                + "either the id was renamed, in which case rename it here, or the button is gone.");

            // A `disabled` that mentions a field's emptiness is the fault. A `disabled` on a busy
            // flag is fine: that flag is set by the handler AFTER the click, so it is not a state
            // anybody can race into.
            if (Regex.IsMatch(element!, @"disabled\s*=\s*""[^""]*IsNullOrWhiteSpace"))
                offences.Add($"{fileName}: #{id}");
        }

        Assert.True(offences.Count == 0,
            $"""
             {offences.Count} commit button(s) are disabled while a field looks empty. That
             judgement is the server's, one circuit round trip behind the keyboard, and a click in
             that window is discarded without reaching anything and without a word (W-A12).

             Gate on the busy flag alone and refuse in the handler, where it can say what is
             missing.

               {string.Join("\n  ", offences)}
             """);
    }
}
