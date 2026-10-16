using Ben.Data.Common.Enums;
using Ben.Web.Services;
using Ben.Web.Services.WebApi;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The state behind the three place-search boxes: the calendar's public event, a new hosted event,
/// and moving a hosted event to another venue (10/16/2026).
/// </summary>
/// <remarks>
/// <para>Found on production. Typing "Bell Witch Cave" and pressing Search showed nothing useful;
/// pressing it again showed the cave. Each box set "searched" before the answer came back, and
/// Blazor draws the page as soon as a handler first waits. So "No places found" was on screen for
/// as long as the request took, and the calendar form's full-name search read as finding nothing.</para>
///
/// <para>These tests hold the request open with a <see cref="TaskCompletionSource{TResult}"/> and
/// look at what the box would show in the meantime.</para>
/// </remarks>
public sealed class PlaceSearchStateTests
{
    private static readonly PlaceCandidate Cave = new(
        Guid.NewGuid(), "Bell Witch Cave", "430 Keysburg Rd", "Adams", "TN",
        PlaceKind.PublicLocation, null, 1);

    private static readonly PlaceCandidate Mansion = new(
        Guid.NewGuid(), "Belmont Mansion", "1900 Belmont Blvd", "Nashville", "TN",
        PlaceKind.PublicLocation, null, 0);

    [Fact]
    public async Task While_the_answer_is_on_its_way_it_says_searching_and_not_that_nothing_matched()
    {
        var search = new PlaceSearch();
        var answer = new TaskCompletionSource<LoadResult<PlaceCandidate>>();

        var running = search.SearchAsync("Bell Witch Cave", _ => answer.Task);

        // This is what was on screen on 10/16: the request still out, and the page already drawn.
        Assert.True(search.IsSearching);
        Assert.False(search.FoundNothing);
        Assert.Null(search.Error);

        answer.SetResult(LoadResult<PlaceCandidate>.Ok([Cave]));
        await running;

        Assert.False(search.IsSearching);
        Assert.Equal("Bell Witch Cave", Assert.Single(search.Results).Name);
        Assert.False(search.FoundNothing);
    }

    [Fact]
    public async Task What_was_typed_is_what_is_searched_for()
    {
        var search = new PlaceSearch();
        string? asked = null;

        await search.SearchAsync("  Bell Witch  ", text =>
        {
            asked = text;
            return Task.FromResult(LoadResult<PlaceCandidate>.Ok([Cave]));
        });

        Assert.Equal("Bell Witch", asked);
    }

    [Fact]
    public async Task Nothing_matched_is_only_said_once_a_search_comes_back_empty()
    {
        var search = new PlaceSearch();

        await search.SearchAsync("Nowhere", _ => Task.FromResult(LoadResult<PlaceCandidate>.Ok([])));

        Assert.True(search.FoundNothing);
        Assert.Null(search.Error);
        Assert.Empty(search.Results);
    }

    [Fact]
    public async Task A_failed_search_says_so_and_does_not_claim_nothing_matched()
    {
        var search = new PlaceSearch();

        await search.SearchAsync("Bell Witch", _ => Task.FromResult(LoadResult<PlaceCandidate>.Failure("500")), "venues");

        Assert.Equal("Couldn't search venues just now. Try again in a moment.", search.Error);
        Assert.False(search.FoundNothing);
        Assert.False(search.IsSearching);
    }

    [Fact]
    public async Task A_search_that_throws_is_a_failure_too()
    {
        var search = new PlaceSearch();

        await search.SearchAsync("Bell Witch", _ => throw new HttpRequestException("unreachable"));

        Assert.Equal("Couldn't search places just now. Try again in a moment.", search.Error);
        Assert.False(search.FoundNothing);
    }

    [Fact]
    public async Task A_signed_out_person_is_told_to_sign_in()
    {
        var search = new PlaceSearch();

        await search.SearchAsync("Bell Witch", _ => Task.FromResult(LoadResult<PlaceCandidate>.SessionEnded()));

        Assert.Equal("You've been signed out. Sign in again to search places.", search.Error);
        Assert.False(search.FoundNothing);
    }

    [Fact]
    public async Task A_slow_older_answer_does_not_replace_a_newer_one()
    {
        var search = new PlaceSearch();
        var slow = new TaskCompletionSource<LoadResult<PlaceCandidate>>();

        var first = search.SearchAsync("Bel", _ => slow.Task);
        await search.SearchAsync("Bell Witch", _ => Task.FromResult(LoadResult<PlaceCandidate>.Ok([Cave])));

        // The first press's answer arrives last. Showing it would put the wrong list under what
        // the person typed second.
        slow.SetResult(LoadResult<PlaceCandidate>.Ok([Mansion]));
        await first;

        Assert.Equal("Bell Witch Cave", Assert.Single(search.Results).Name);
        Assert.False(search.IsSearching);
    }

    /// <summary>
    /// The three forms that choose a place for an event search with the real search, through this
    /// state, and none of them asks the duplicate matcher (which needs the exact name).
    /// </summary>
    [Theory]
    [InlineData("Ben.Web.Website.Library/Manage/Calendar/OrgScheduler.razor")]
    [InlineData("Ben.Web.Website.Library/Manage/Events/OrgEvents.razor")]
    [InlineData("Ben.Web.Website.Library/Manage/Events/OrgEventPage.razor")]
    public void Each_event_place_box_uses_the_search(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);

        var source = File.ReadAllText(Path.Combine(dir!.FullName, relativePath));

        // Comments out first, so a remark that names the old call can't pass or fail this.
        source = System.Text.RegularExpressions.Regex.Replace(
            source, @"@\*.*?\*@", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        source = string.Join('\n', source.Split('\n').Select(line =>
            line.TrimStart().StartsWith("//", StringComparison.Ordinal) ? "" : line));

        Assert.DoesNotContain("FindPlaceCandidatesAsync", source);
        Assert.Matches(@"PlaceSearch\s+_\w+\s*=\s*new", source);
        Assert.Matches(@"\.SearchAsync\([^;]*AdminClient\.SearchPublicPlacesAsync", source);
    }

    [Fact]
    public async Task An_answer_that_arrives_after_the_box_was_cleared_is_ignored()
    {
        var search = new PlaceSearch();
        var slow = new TaskCompletionSource<LoadResult<PlaceCandidate>>();

        var running = search.SearchAsync("Bell Witch", _ => slow.Task);
        search.Clear();

        slow.SetResult(LoadResult<PlaceCandidate>.Ok([Cave]));
        await running;

        Assert.Empty(search.Results);
        Assert.False(search.IsSearching);
        Assert.False(search.FoundNothing);
    }
}
