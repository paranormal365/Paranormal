using System.Text.RegularExpressions;
using Ben.Data.Common.Enums;
using Ben.Web.Website.Library.Kit.Plans;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// What the designer offers, and to whom (item 235 phase 2).
/// </summary>
/// <remarks>
/// <para>Rendered for real through the framework's own <see cref="HtmlRenderer"/>, as
/// <c>BenPlanTests</c> does. What is worth pinning here is which controls exist in which state:
/// a Seats plan must not offer a tray of rooms, a plan with seats on it must not offer to become
/// a plan of rooms, and a room already placed must not be offered a second time. Every one of
/// those is a wrong control that leads to a refusal or a duplicate.</para>
///
/// <para><b>The dialogs and the selection bar are not reachable from here.</b> They open from
/// private state that only a gesture sets, and <c>BenModal</c> renders nothing at all while it is
/// closed. Their behaviour is pinned two other ways: the arithmetic behind them lives in
/// <see cref="PlanModel"/> and is tested in <c>PlanModelTests</c>, and the screens themselves are
/// walked by the phase's Playwright fixture at three widths. Reaching into a component to force a
/// dialog open would test the reach, not the designer.</para>
/// </remarks>
public sealed class BenPlanDesignerTests
{
    /// <summary>Interop is never reached in static rendering; this is here so injection resolves.</summary>
    private sealed class UnusedJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new InvalidOperationException("The designer must not call JavaScript while rendering.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => throw new InvalidOperationException("The designer must not call JavaScript while rendering.");
    }

    private static async Task<string> RenderAsync(
        PlanModel model,
        IReadOnlyList<PlanRoomOption>? rooms = null,
        string? error = null, bool busy = false, bool allowKindChange = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IJSRuntime, UnusedJs>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<BenPlanDesigner>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(BenPlanDesigner.Model)] = model,
                    [nameof(BenPlanDesigner.Rooms)] = rooms ?? [],
                    [nameof(BenPlanDesigner.Error)] = error,
                    [nameof(BenPlanDesigner.Busy)] = busy,
                    [nameof(BenPlanDesigner.AllowKindChange)] = allowKindChange,
                }));
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    private static PlanModel Seats(int rows = 2, int seats = 4)
    {
        var plan = new PlanModel(HostedEventLayoutKind.Seats);
        plan.AddBlock(rows, seats, section: "Stalls");
        return plan;
    }

    private static readonly Guid BlueRoom = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Suite = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static IReadOnlyList<PlanRoomOption> ThomasHouse() =>
    [
        new(BlueRoom, "The Blue Room", "First floor", 2, "One queen"),
        new(Suite, "The Suite", "Second floor", 4, "Two queens"),
    ];

    /// <summary>The whole element, from its opening angle bracket to the closing one.</summary>
    private static string? ElementWithId(string html, string id)
    {
        var at = html.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
        if (at < 0) return null;

        var open = html.LastIndexOf('<', at);
        var close = html.IndexOf('>', at);
        return open < 0 || close < 0 ? null : html[open..close];
    }

    // ── the right tools for the kind of plan ─────────────────────────────────

    [Fact]
    public async Task A_seating_plan_offers_blocks_and_aisles_and_no_tray_of_rooms()
    {
        var html = await RenderAsync(Seats(), ThomasHouse());

        Assert.Contains("id=\"plan-add-block\"", html);
        Assert.Contains("id=\"plan-insert-aisle\"", html);
        // A theatre has no rooms to place, and offering the venue's bedrooms on a seating plan is
        // an offer the server would refuse.
        Assert.DoesNotContain("id=\"plan-open-tray\"", html);
        Assert.DoesNotContain("id=\"plan-auto-arrange\"", html);
    }

    [Fact]
    public async Task A_floor_plan_offers_the_rooms_and_no_block_builder()
    {
        var html = await RenderAsync(new PlanModel(HostedEventLayoutKind.Rooms), ThomasHouse());

        Assert.Contains("id=\"plan-open-tray\"", html);
        Assert.Contains("id=\"plan-auto-arrange\"", html);
        Assert.DoesNotContain("id=\"plan-add-block\"", html);
        Assert.DoesNotContain("id=\"plan-insert-aisle\"", html);
    }

    [Fact]
    public async Task An_empty_plan_may_still_change_what_it_holds_and_a_used_one_may_not()
    {
        Assert.Contains("id=\"plan-kind-seats\"",
            await RenderAsync(new PlanModel(HostedEventLayoutKind.Rooms)));

        // Once anything is on it the model refuses the change and the server refuses it too, so
        // the control stops being offered rather than offering something that will bounce.
        Assert.DoesNotContain("id=\"plan-kind-seats\"", await RenderAsync(Seats()));
    }

    [Fact]
    public async Task A_plan_with_bookings_may_not_change_what_it_holds_even_while_empty()
    {
        var html = await RenderAsync(
            new PlanModel(HostedEventLayoutKind.Rooms), allowKindChange: false);

        Assert.DoesNotContain("id=\"plan-kind-rooms\"", html);
    }

    // ── the tray ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A room taken off the plan is in the list, not nowhere.
    /// </summary>
    /// <remarks>
    /// <para><b>The bug this is here for.</b> A room can be waiting in two different states: a unit
    /// of this event with no square yet, and one of the venue's rooms this event has never taken.
    /// The tray listed only the second. So pressing <i>Back to the list</i> unplaced the unit — it
    /// stayed a unit, so it was still excluded from the list of rooms not yet taken — and the room
    /// appeared nowhere at all. On the plan: no. In the list: no. Recoverable only by undo, and to
    /// a venue indistinguishable from having deleted it.</para>
    ///
    /// <para>Nothing caught it. The model's own tests were right, the designer's own tests were
    /// right, and the two were right about different piles. It surfaced when a Playwright fixture
    /// tried to put a room back and found no button to press.</para>
    /// </remarks>
    [Fact]
    public async Task A_room_taken_off_the_plan_goes_back_into_the_list()
    {
        var plan = new PlanModel(HostedEventLayoutKind.Rooms);
        var key = plan.AddRoom(BlueRoom, "The Blue Room", 2)!.Value;
        plan.Place(key, 0, 0);

        Assert.Contains("Rooms to place (1)", await RenderAsync(plan, ThomasHouse()));

        plan.Unplace([key]);
        var html = await RenderAsync(plan, ThomasHouse());

        // Both of them: the one just taken off, and the one never added.
        Assert.Contains("Rooms to place (2)", html);
        Assert.Contains("The Blue Room", html);
        Assert.Contains($"id=\"plan-tray-{key}\"", html);
    }

    [Fact]
    public async Task A_room_the_event_has_taken_but_not_placed_is_never_offered_twice()
    {
        // Once as the unit it is, and never again as a venue room going spare. Two buttons for one
        // room is two rooms as far as anybody pressing them is concerned.
        var plan = new PlanModel(HostedEventLayoutKind.Rooms);
        plan.AddRoom(BlueRoom, "The Blue Room", 2);

        var html = await RenderAsync(plan, ThomasHouse());

        Assert.Contains("Rooms to place (2)", html);
        Assert.Single(Regex.Matches(html, Regex.Escape("The Blue Room")));
        Assert.DoesNotContain($"id=\"plan-tray-{BlueRoom}\"", html);
    }

    [Fact]
    public async Task A_room_in_the_tray_says_what_it_sleeps_and_what_the_beds_are()
    {
        // "Will the two of us have to share a bed" is the question a host is answering while
        // deciding which rooms to offer, so the answer is on the button, not one click away.
        var html = await RenderAsync(new PlanModel(HostedEventLayoutKind.Rooms), ThomasHouse());

        Assert.Contains("sleeps 2 · One queen", html);
        Assert.Contains("First floor", html);
    }

    [Fact]
    public async Task Arranging_them_for_me_is_offered_only_when_something_is_waiting()
    {
        var empty = ElementWithId(
            await RenderAsync(new PlanModel(HostedEventLayoutKind.Rooms), ThomasHouse()),
            "plan-auto-arrange");
        Assert.Contains("disabled", empty);

        var plan = new PlanModel(HostedEventLayoutKind.Rooms);
        plan.AddRoom(BlueRoom, "The Blue Room", 2);
        var waiting = ElementWithId(await RenderAsync(plan, ThomasHouse()), "plan-auto-arrange");
        Assert.DoesNotContain("disabled", waiting);
    }

    // ── what the page is told ────────────────────────────────────────────────

    [Fact]
    public async Task A_refusal_reaches_the_page_in_the_servers_own_words()
    {
        // The sentence names the rooms or seats that stopped the save. Rewriting it here would
        // lose the names, and the names are the only part a venue can act on.
        const string refusal = "The Blue Room is booked on Friday, so it cannot be removed.";

        var html = await RenderAsync(Seats(), error: refusal);

        Assert.Contains(refusal, html);
        Assert.Contains("id=\"plan-error\"", html);
    }

    [Fact]
    public async Task An_unsaved_plan_says_so_and_a_freshly_loaded_one_does_not()
    {
        Assert.Contains("id=\"plan-unsaved\"", await RenderAsync(Seats()));

        var loaded = new PlanModel(HostedEventLayoutKind.Seats);
        loaded.AddBlock(1, 2);
        loaded.Saved(loaded.Units);
        Assert.DoesNotContain("id=\"plan-unsaved\"", await RenderAsync(loaded));
    }

    [Fact]
    public async Task Saving_says_it_is_saving_and_cannot_be_pressed_twice()
    {
        var html = await RenderAsync(Seats(), busy: true);
        var save = ElementWithId(html, "plan-save");

        Assert.Contains("disabled", save);
        Assert.Contains("Saving…", html);
    }

    // ── the phone ────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_phone_is_told_what_it_is_good_for_rather_than_left_to_find_out()
    {
        // Decision 13: it must work at iPhone width. Working does not mean pretending that
        // arranging four hundred seats through a 375-pixel window is a good afternoon.
        var html = await RenderAsync(Seats());
        var note = ElementWithId(html, "plan-phone-note");

        Assert.Contains("d-md-none", note);
        Assert.Contains("easier on a computer or an iPad", html);
    }

    [Fact]
    public async Task Nothing_in_the_toolbar_is_a_bare_icon_without_a_name_for_it()
    {
        // Undo, Redo and Tools are icons. An icon with no accessible name is a button a screen
        // reader announces as "button", which is no name at all.
        var html = await RenderAsync(Seats());

        foreach (var id in new[] { "plan-undo", "plan-redo" })
            Assert.Contains("title=", ElementWithId(html, id));

        Assert.Equal(3, Regex.Matches(html, "<title>").Count);   // Undo, Redo, More tools
    }
}
