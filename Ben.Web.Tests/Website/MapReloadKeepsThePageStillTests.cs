using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Moving the map re-asks for the cases in the new view without taking the page apart.
/// </summary>
/// <remarks>
/// <para><b>Ben, 2026-09-18, on a Mac in Safari:</b> "When you zoom out of the map, it causes the page
/// to jump… I let the map zoom out and about 1 second to .5 seconds later the page jumps." He sent a
/// recording: the page scrolled from the map all the way up to the feed, about a second after the
/// zoom.</para>
///
/// <para><b>What it was.</b> <c>_loading</c> guarded the WHOLE section — heading, map and list — and
/// every viewport change set it. So a pan or a zoom took the 420-pixel map out of the document and put
/// a small spinner in its place. Measured in a browser: the page fell from 1926 pixels to 1022, the
/// scroll position was clamped from 1235 to 331, and the section came back a moment later underneath
/// a reader who had been moved 904 pixels. Chrome puts the scroll back afterwards because it
/// implements scroll anchoring; <b>Safari does not implement it at all</b>, which is why this was a
/// Safari report about a fault that was in the markup for everyone.</para>
///
/// <para>So the full-section loader belongs to the FIRST load, which has nothing worth keeping on
/// screen. Every later load says so quietly and leaves the page alone. Measured after: the map is
/// never unmounted, the page never shrinks, and the scroll position does not move on any frame.</para>
/// </remarks>
public class MapReloadKeepsThePageStillTests
{
    private static string Markup()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName,
            "Ben.Web.Website.Library", "Shared", "PublicCaseDiscovery.razor"));
    }

    /// <summary>
    /// A reload for a new view never raises the flag that unmounts the section.
    /// </summary>
    [Fact]
    public void Only_the_first_load_may_take_the_section_over()
    {
        Assert.Matches(@"if\s*\(_loadedOnce\)\s*_refreshing\s*=\s*true;\s*else\s*_loading\s*=\s*true;", Markup());
    }

    /// <summary>
    /// And nothing RAISES it again. A second assignment anywhere would put the collapse back without
    /// touching the line above.
    /// </summary>
    /// <remarks>
    /// The field's own initialiser is not a raise — it is the state the component starts in, before
    /// anything has been drawn — so it is excluded rather than counted.
    /// </remarks>
    [Fact]
    public void Nothing_else_raises_the_full_section_loader()
    {
        var raises = Regex.Matches(Markup(), @"(?<!bool\s{1,8})\b_loading\s*=\s*true")
            .Count(m => !m.Value.Contains("bool"));

        Assert.Equal(1, raises);
    }

    /// <summary>The map lives inside the section the loader replaces, which is why this mattered.</summary>
    [Fact]
    public void The_map_is_inside_the_section_the_loader_stands_in_for()
    {
        var markup = Markup();
        var loader = markup.IndexOf("@if (_loading)", StringComparison.Ordinal);
        var map = markup.IndexOf("<BenMap", StringComparison.Ordinal);

        Assert.True(loader >= 0 && map > loader,
            "the map is no longer below the loader; if the section was restructured, re-check that a "
          + "reload cannot unmount it");
    }

    /// <summary>A reload still tells the reader something is happening, without moving anything.</summary>
    [Fact]
    public void A_reload_says_so_without_replacing_the_section() =>
        Assert.Matches(@"@if\s*\(_refreshing\)", Markup());
}
