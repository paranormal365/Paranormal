using System.Text.RegularExpressions;
using Ben.Data.Common.Enums;
using Ben.Web.Tests.Support;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Every kind of timeline entry can be chosen, and the investigation binder still offers only the three
/// that belong on the night.
/// </summary>
/// <remarks>
/// Research came off the pick list on 2026-09-14, when research pages carried their own dates and offering
/// it here made two places to write the same thing, and went back on 2026-09-16 when those pages were
/// retired for canvas boards. A board is not a dated moment, so a note about what the deeds said needs the
/// timeline again.
/// </remarks>
public sealed class CaseTimelineEntryTypesTests
{
    [Fact]
    public void Pickable_offers_every_kind_including_research()
    {
        Assert.Contains(CaseTimelineEntryType.ResearchNote, CaseTimelineEntryTypes.Pickable);
        Assert.Equal(Enum.GetValues<CaseTimelineEntryType>(), CaseTimelineEntryTypes.Pickable);
    }

    [Fact]
    public void Research_is_a_kind_with_a_name_people_can_read()
    {
        Assert.True(Enum.IsDefined(CaseTimelineEntryType.ResearchNote));
        Assert.Equal("Research", CaseTimelineEntryTypes.DisplayName(CaseTimelineEntryType.ResearchNote));
    }

    /// <summary>
    /// The binder's list is three kinds and Research is not one of them — not because research is
    /// unwritable, but because a binder row is what happened during the investigation.
    /// </summary>
    [Fact]
    public void The_binder_offers_only_what_happens_on_the_night()
    {
        var cases = Path.Combine(RepoFiles.Root().FullName, "Ben.Web.Website.Library", "Organization", "Cases");

        // The binder's list is a literal: it must not name ResearchNote at all.
        var panel = File.ReadAllText(Path.Combine(cases, "InvestigationPanel.razor"));
        var binder = Regex.Match(panel, @"_binderEntryTypes\s*=\s*\[(.*?)\];", RegexOptions.Singleline);
        Assert.True(binder.Success, "InvestigationPanel no longer declares _binderEntryTypes — update this test");
        Assert.DoesNotContain("ResearchNote", binder.Groups[1].Value);

        // The timeline's own list comes from Pickable, so the two lists can never disagree by accident.
        var timeline = File.ReadAllText(Path.Combine(cases, "CaseTimeline.razor"));
        var options = Regex.Match(timeline, @"_entryTypeOptions\s*=\s*(.*?);", RegexOptions.Singleline);
        Assert.True(options.Success, "CaseTimeline no longer declares _entryTypeOptions — update this test");
        Assert.Contains("CaseTimelineEntryTypes.Pickable", options.Groups[1].Value);
    }
}
