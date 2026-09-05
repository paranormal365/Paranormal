using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// A form a person types into must not exist until the circuit does.
/// </summary>
/// <remarks>
/// <para>Every page is prerendered as static HTML, then taken over by the Blazor circuit. A form
/// in the static HTML looks ready and is not: the first interactive render sets each input from
/// component state — empty — and whatever was typed in between is discarded. On 2026-09-03 the
/// browser suite hit this three times on a loaded machine: the start-a-group wizard answered
/// "Give your group a name first." to a name it had just been given, sign-in said the email was
/// required, and the reset-link form did nothing. A slow connection does the same to a real
/// person. The cure is <c>BenInteractiveOnly</c>, which renders a placeholder during prerender
/// and the form only once it can be used.</para>
/// <para>This reads the source rather than rendering, like the other prerender guards: the
/// property that matters — "does the markup reach the browser before the circuit?" — is a fact
/// about the file, not about any one render.</para>
/// </remarks>
public sealed class PrerenderedFormsTests
{
    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, relative);
    }

    /// <summary>The pages where a person types before anything else has happened on the site —
    /// nothing has warmed the circuit for them, so these are the ones the gap bites.</summary>
    public static IEnumerable<object[]> TypedEntryPages =>
    [
        ["Ben.Web.Website/Components/Pages/Login.razor",                    "<EditForm"],
        ["Ben.Web.Website/Components/Pages/ForgotPassword.razor",           "<EditForm"],
        ["Ben.Web.Website.Library/Organization/StartGroupPage.razor",        "<BenWizard"],
    ];

    [Theory]
    [MemberData(nameof(TypedEntryPages))]
    public void The_form_waits_for_the_circuit(string page, string formTag)
    {
        var source = File.ReadAllText(RepoFile(page));
        var form = source.IndexOf(formTag, StringComparison.Ordinal);
        Assert.True(form >= 0, $"{page} no longer contains {formTag} — move this guard to wherever the form went.");

        var gate = source.LastIndexOf("<BenInteractiveOnly", form, StringComparison.Ordinal);
        Assert.True(gate >= 0,
            $"{page}: {formTag} is rendered during prerender. Wrap it in <BenInteractiveOnly> so a "
            + "person's typing cannot be discarded when the circuit attaches.");

        // The gate must still be open where the form starts: no closing tag between the two.
        var closedBefore = source.IndexOf("</BenInteractiveOnly>", gate, StringComparison.Ordinal);
        Assert.True(closedBefore < 0 || closedBefore > form,
            $"{page}: the <BenInteractiveOnly> before {formTag} closes again before the form begins.");
    }

    [Fact]
    public void The_gate_itself_decides_on_the_renderer_and_nothing_else()
    {
        // The whole component is one question. If it ever grows a second condition — a feature
        // flag, an auth check — a form could stay hidden for a reason nobody can see.
        var source = File.ReadAllText(RepoFile("Ben.Web.Website.Library/Kit/BenInteractiveOnly.razor"));
        Assert.Contains("@if (RendererInfo.IsInteractive)", source);
        Assert.Single(Regex.Matches(source, @"@if\s*\("));
    }
}
