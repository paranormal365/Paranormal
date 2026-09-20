using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// A timeline entry's body is sanitized before it is stored (2026-09-20).
/// </summary>
/// <remarks>
/// <para><b>What was open.</b> <c>CaseTimeline.razor</c> renders an entry's body with
/// <c>MarkupString</c> on the group's own case screen, and both write paths stored it with nothing
/// but a <c>Trim()</c>. A client filling in "what happened" on their own case could put markup in
/// front of an investigator; a member could put it in front of whoever the case was later
/// transferred to.</para>
///
/// <para><b>It is the same gap the case description had</b> — <c>CleanDescription</c> exists
/// because of it, and its own note says "nothing between a request body and a MarkupString on a
/// public page". Timeline entries were the other half and were missed.</para>
///
/// <para><b>Asserted against the source</b>, because the mistake is a one-word edit: putting
/// <c>request.Body?.Trim()</c> back would compile, pass every behaviour test, and reopen it.</para>
/// </remarks>
public sealed class TimelineBodyIsSanitizedTests
{
    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    public static TheoryData<string, string> BothControllers() => new()
    {
        { "MyCaseController.cs", Path.Combine("Ben.Data.WebApi", "Controllers", "MyCaseController.cs") },
        { "CaseController.cs", Path.Combine("Ben.Data.WebApi", "Controllers", "Entities", "CaseController.cs") },
    };

    [Theory]
    [MemberData(nameof(BothControllers))]
    public void No_timeline_body_is_stored_with_only_a_trim(string name, string relative)
    {
        var source = Read(relative.Split(Path.DirectorySeparatorChar));

        var raw = Regex.Matches(source, @"Body\s*=\s*request\.Body\?\.Trim\(\)")
            .Select(m => m.Value)
            .ToList();

        Assert.True(raw.Count == 0,
            $"{name} stores a timeline body with only a Trim. It is rendered as MarkupString on "
          + "the case screen, so it has to go through CleanDescription first.");
    }

    [Theory]
    [MemberData(nameof(BothControllers))]
    public void Both_the_create_and_the_update_path_clean_it(string name, string relative)
    {
        var source = Read(relative.Split(Path.DirectorySeparatorChar));

        var cleaned = Regex.Matches(source, @"Body\s*=\s*(?:Entities\.CaseController\.)?CleanDescription\(").Count;

        Assert.True(cleaned >= 2,
            $"{name} cleans {cleaned} timeline body path(s); both the create and the update path "
          + "need it, or an edit reopens what a create closed.");
    }

    /// <summary>
    /// The helper still does what these paths rely on.
    /// </summary>
    /// <remarks>
    /// Pointing four call sites at a helper is worth nothing if the helper stops sanitizing, and
    /// that would be a change nobody makes while thinking about timelines.
    /// </remarks>
    [Fact]
    public void The_helper_removes_script_and_keeps_words()
    {
        var sanitizer = new Ben.Data.WebApi.Services.CmsMarkupSanitizer();

        var clean = Ben.Data.WebApi.Controllers.Entities.CaseController.CleanDescription(
            "<p>Heard it at <strong>3am</strong>.</p><script>alert('x')</script>", sanitizer);

        Assert.NotNull(clean);
        Assert.DoesNotContain("<script", clean!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3am", clean);
        Assert.Contains("<strong>", clean);
    }

    [Fact]
    public void An_emptied_editor_stores_nothing_rather_than_an_empty_paragraph()
    {
        var sanitizer = new Ben.Data.WebApi.Services.CmsMarkupSanitizer();

        Assert.Null(Ben.Data.WebApi.Controllers.Entities.CaseController.CleanDescription("<p></p>", sanitizer));
        Assert.Null(Ben.Data.WebApi.Controllers.Entities.CaseController.CleanDescription("   ", sanitizer));
        Assert.Null(Ben.Data.WebApi.Controllers.Entities.CaseController.CleanDescription(null, sanitizer));
    }
}
