using System.Text.RegularExpressions;
using Ben.Data.Common.Enums;
using Ben.Web.Tests.Support;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Research is no longer a kind of timeline entry a person can choose, and entries already written as research
/// still exist and still show.
/// </summary>
/// <remarks>
/// Beta feedback, 2026-09-14: research has its own tab, and its pages carry their own date and time, so a
/// "Research" choice in Add Entry made two places to write the same thing.
/// </remarks>
public sealed class CaseTimelineEntryTypesTests
{
    [Fact]
    public void Pickable_leaves_out_research_and_keeps_every_other_kind()
    {
        Assert.DoesNotContain(CaseTimelineEntryType.ResearchNote, CaseTimelineEntryTypes.Pickable);

        var others = Enum.GetValues<CaseTimelineEntryType>().Where(t => t != CaseTimelineEntryType.ResearchNote);
        Assert.Equal(others, CaseTimelineEntryTypes.Pickable);
    }

    [Fact]
    public void Research_is_still_a_kind_so_old_entries_keep_their_name()
    {
        Assert.True(Enum.IsDefined(CaseTimelineEntryType.ResearchNote));
        Assert.Equal("Research", CaseTimelineEntryTypes.DisplayName(CaseTimelineEntryType.ResearchNote));
    }

    [Fact]
    public void Neither_entry_picker_offers_research_for_a_new_entry()
    {
        var cases = Path.Combine(RepoFiles.Root().FullName, "Ben.Web.Website.Library", "Organization", "Cases");

        // The investigation binder's list is a literal: it must not name ResearchNote at all.
        var panel = File.ReadAllText(Path.Combine(cases, "InvestigationPanel.razor"));
        var binder = Regex.Match(panel, @"_binderEntryTypes\s*=\s*\[(.*?)\];", RegexOptions.Singleline);
        Assert.True(binder.Success, "InvestigationPanel no longer declares _binderEntryTypes — update this test");
        Assert.DoesNotContain("ResearchNote", binder.Groups[1].Value);

        // The timeline's list for a new entry comes from Pickable, not from every value of the enum.
        var timeline = File.ReadAllText(Path.Combine(cases, "CaseTimeline.razor"));
        var options = Regex.Match(timeline, @"_entryTypeOptions\s*=\s*(.*?);", RegexOptions.Singleline);
        Assert.True(options.Success, "CaseTimeline no longer declares _entryTypeOptions — update this test");
        Assert.Contains("CaseTimelineEntryTypes.Pickable", options.Groups[1].Value);
    }
}
