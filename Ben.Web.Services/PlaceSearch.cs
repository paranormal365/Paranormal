using Ben.Web.Services.WebApi;

namespace Ben.Web.Services;

/// <summary>
/// The state behind a "search for a place" box: what was found, whether it is still looking, and
/// what to say when it couldn't look (10/16/2026).
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Three forms (the calendar's public event, a new hosted event, and
/// moving a hosted event to another venue) each kept this state by hand, and all three got the
/// same thing wrong. They set "searched" <i>before</i> waiting for the answer. Blazor draws the page
/// as soon as the handler first waits, so "No places found" appeared the moment Search was
/// pressed and stayed there for as long as the request took. On production that read as the
/// answer: "Bell Witch Cave" seemed to find nothing, and a second click (or simply waiting) showed
/// it.</para>
///
/// <para>Now "nothing matched" is only said about a search that has finished, the box says
/// "Searching…" while it waits, and an answer to an older search is dropped if a newer one has
/// started, so a slow first request cannot overwrite the second.</para>
/// </remarks>
public sealed class PlaceSearch
{
    private int _latest;

    /// <summary>What the last finished search found.</summary>
    public IReadOnlyList<PlaceCandidate> Results { get; private set; } = [];

    /// <summary>A search is waiting for its answer.</summary>
    public bool IsSearching { get; private set; }

    /// <summary>The sentence to show when the last search couldn't run, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>
    /// The last search finished, worked, and found nothing. The only time "no places match" is true.
    /// </summary>
    public bool FoundNothing { get; private set; }

    /// <summary>
    /// Runs one search. Returns once it has finished or been overtaken by a newer one.
    /// </summary>
    /// <param name="text">What was typed.</param>
    /// <param name="search">The call that does the searching.</param>
    /// <param name="noun">What is being searched for, in the sentences shown ("places", "venues").</param>
    public async Task SearchAsync(
        string text, Func<string, Task<LoadResult<PlaceCandidate>>> search, string noun = "places")
    {
        var mine = ++_latest;

        IsSearching = true;
        Error = null;
        FoundNothing = false;
        Results = [];

        LoadResult<PlaceCandidate> found;
        try
        {
            found = await search(text.Trim());
        }
        catch (Exception)
        {
            found = LoadResult<PlaceCandidate>.Failure();
        }

        // A newer search has started since this one; its answer is the one to show.
        if (mine != _latest) return;

        IsSearching = false;
        Error = !found.Failed ? null
            : found.SessionExpired ? $"You've been signed out. Sign in again to search {noun}."
            : $"Couldn't search {noun} just now. Try again in a moment.";
        Results = found.Items;
        FoundNothing = !found.Failed && found.Items.Count == 0;
    }

    /// <summary>Back to an empty box, and any search still waiting is ignored when it answers.</summary>
    public void Clear()
    {
        _latest++;
        IsSearching = false;
        Error = null;
        FoundNothing = false;
        Results = [];
    }
}
