using Ben.Web.Services.WebApi;
using Ben.Web.Tests.Support;
using Ben.Web.Website.Library.Kit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// One record, four honest states — the single-object counterpart to <c>BenListState</c> (item 235, phase 1).
/// </summary>
/// <remarks>
/// <para>The rule these pin is the hosted events plan's: <b>a 403 must never read as "not found"</b>.
/// A member refused the dietary sheet is not looking at an event with no guests, and a page that
/// renders its content fragment against a failed result — or renders nothing at all — tells that
/// untruth in the reassuring voice it uses for genuinely empty things.</para>
///
/// <para>These render the component for real, through the framework's own <see cref="HtmlRenderer"/>,
/// rather than reading the markup as text: the claim under test is what a person sees for each
/// outcome, and a source scan cannot tell that the failed branch comes before the content branch
/// from the branches merely both existing. The test project does not reference bUnit, and does
/// not need to — <c>HtmlRenderer</c> ships with the framework the project already references.</para>
/// </remarks>
public sealed class BenItemStateTests
{
    private sealed record Thing(string Title);

    /// <summary>Enough of a NavigationManager for the sign-in link to be built from.</summary>
    private sealed class FakeNav : NavigationManager
    {
        public FakeNav() => Initialize("https://unit.test/", "https://unit.test/manage/events/abc/plan");
    }

    private static async Task<string> RenderAsync(ItemResult<Thing> result, bool loading = false, bool withRetry = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<NavigationManager, FakeNav>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = new Dictionary<string, object?>
            {
                [nameof(BenItemState<Thing>.Result)]  = result,
                [nameof(BenItemState<Thing>.Loading)] = loading,
                [nameof(BenItemState<Thing>.ChildContent)] =
                    (RenderFragment<Thing>)(t => b => b.AddContent(0, "CONTENT:" + t.Title)),
            };
            if (withRetry)
                parameters[nameof(BenItemState<Thing>.OnRetry)] =
                    EventCallback.Factory.Create(new object(), () => { });

            var output = await renderer.RenderComponentAsync<BenItemState<Thing>>(
                ParameterView.FromDictionary(parameters));

            // Decoded, so the assertions read as the words a person sees. The framework's encoder
            // writes "You've" as You&#x27;ve and the ellipsis as &#x2026;, and a test that spells
            // those out is a test of the encoder, not of the component.
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    [Fact]
    public async Task A_failed_result_never_renders_the_content()
    {
        // The case the plan names: a 403. It arrives as Failed with no sentence, exactly like a 404
        // or an unreachable API, and the page must not pretend the record is simply absent.
        var html = await RenderAsync(ItemResult<Thing>.Failure(), withRetry: true);

        Assert.DoesNotContain("CONTENT:", html);
        Assert.Contains("Couldn't load this", html);
        Assert.Contains("Try again", html);
        Assert.Contains("not a missing record", html);   // the generic line, since the server said nothing
    }

    [Fact]
    public async Task A_failed_result_with_a_sentence_shows_the_sentence()
    {
        var html = await RenderAsync(ItemResult<Thing>.Failure("The kitchen's sheet is for people who decide bookings."));

        Assert.Contains("The kitchen's sheet is for people who decide bookings.", html);
        Assert.DoesNotContain("not a missing record", html);
        Assert.DoesNotContain("CONTENT:", html);
    }

    [Fact]
    public async Task A_session_expired_result_renders_the_sign_in_link_carrying_the_return_url()
    {
        var html = await RenderAsync(ItemResult<Thing>.SessionEnded());

        Assert.Contains("You've been signed out", html);
        Assert.Contains("href=\"/login?returnUrl=%2Fmanage%2Fevents%2Fabc%2Fplan\"", html);
        Assert.DoesNotContain("Try again", html);   // a retry on a dead session is a button that cannot work
        Assert.DoesNotContain("CONTENT:", html);
    }

    [Fact]
    public async Task A_success_renders_the_fragment_with_the_item()
    {
        var html = await RenderAsync(ItemResult<Thing>.Ok(new Thing("The Blue Room")));

        Assert.Contains("CONTENT:The Blue Room", html);
        Assert.DoesNotContain("Couldn't load this", html);
        Assert.DoesNotContain("signed out", html);
    }

    [Fact]
    public async Task Loading_wins_over_everything_else()
    {
        // A page that has a stale failure from its last load and is fetching again must show the
        // spinner, not the old yellow card — the same precedence BenListState keeps.
        var html = await RenderAsync(ItemResult<Thing>.Failure("stale"), loading: true);

        Assert.DoesNotContain("Couldn't load this", html);
        Assert.DoesNotContain("CONTENT:", html);
        Assert.Contains("Loading…", html);
    }

    [Fact]
    public void The_component_lives_in_the_shared_kit_beside_BenListState()
    {
        // In Kit, not beside its first caller: the next hosted page should find it where every
        // other shared control lives, rather than importing from a feature folder.
        var found = RepoFiles.Paths("*.razor")
            .Where(p => Path.GetFileName(p) == "BenItemState.razor")
            .ToList();

        Assert.Single(found);
        Assert.Contains($"Kit{Path.DirectorySeparatorChar}BenItemState.razor", found[0]);
    }
}
