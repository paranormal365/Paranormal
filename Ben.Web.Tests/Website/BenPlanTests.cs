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
/// The plan as a person meets it: squares, states, edges and the legend (item 235 phase 2).
/// </summary>
/// <remarks>
/// <para>Rendered for real through the framework's own <see cref="HtmlRenderer"/>, as
/// <c>BenItemStateTests</c> does, because the claims worth pinning are about what reaches the page
/// — that a taken seat carries a word and not only a colour, that an aisle is a gap and not a
/// seat, that a read-only plan answers nothing. A source scan cannot tell any of those.</para>
///
/// <para>The pointer module never runs here: static rendering has no after-render pass, which is
/// exactly the arrangement that lets the keyboard and the markup be tested without a browser.</para>
/// </remarks>
public sealed class BenPlanTests
{
    /// <summary>Interop is never reached in static rendering; this is here so injection resolves.</summary>
    private sealed class UnusedJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new InvalidOperationException("The plan must not call JavaScript while rendering.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => throw new InvalidOperationException("The plan must not call JavaScript while rendering.");
    }

    private static async Task<string> RenderAsync(
        PlanModel model, PlanMode mode = PlanMode.Edit,
        IReadOnlyDictionary<Guid, PlanCellState>? occupancy = null,
        IReadOnlySet<Guid>? selection = null, bool compact = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IJSRuntime, UnusedJs>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<BenPlan>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(BenPlan.Model)] = model,
                    [nameof(BenPlan.Mode)] = mode,
                    [nameof(BenPlan.Occupancy)] = occupancy,
                    [nameof(BenPlan.Selection)] = selection ?? new HashSet<Guid>(),
                    [nameof(BenPlan.Compact)] = compact,
                }));
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    private static int Count(string html, string needle)
        => Regex.Matches(html, Regex.Escape(needle)).Count;

    private static PlanModel Stalls(int rows = 3, int seats = 4, string? section = "Stalls", decimal? price = null)
    {
        var plan = new PlanModel(HostedEventLayoutKind.Seats);
        plan.AddBlock(rows, seats, section: section, price: price);
        return plan;
    }

    // ── the squares ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Every_seat_in_a_block_reaches_the_page()
    {
        var html = await RenderAsync(Stalls(13, 20));

        Assert.Equal(260, Count(html, "class=\"plan__unit\""));
        Assert.Contains("A1", html);
        Assert.Contains("N20", html);
    }

    [Fact]
    public async Task A_row_and_a_column_put_a_seat_in_its_own_square()
    {
        // The whole model is two integers per unit; if they do not reach the style attribute the
        // grid collapses into one stack and nothing else in this component matters.
        var html = await RenderAsync(Stalls(2, 2));

        // Row 0, column 0 sits at grid row 2 and column 2 — the first row and column belong to the
        // headings.
        Assert.Contains("grid-row:2;grid-column:2", html);
        Assert.Contains("grid-row:3;grid-column:3", html);
    }

    [Fact]
    public async Task An_aisle_is_a_gap_and_not_a_seat()
    {
        var plan = Stalls(1, 4);
        plan.InsertColumn(before: 2);

        var html = await RenderAsync(plan, PlanMode.Read);

        // Four seats, and nothing at all in the column they were pushed apart by. In Read mode
        // empty squares are not drawn either, so an aisle reads as an aisle.
        Assert.Equal(4, Count(html, "class=\"plan__unit\""));
        Assert.DoesNotContain("grid-row:2;grid-column:4\"", html);
    }

    [Fact]
    public async Task While_arranging_an_empty_square_is_somewhere_to_put_something()
    {
        var html = await RenderAsync(Stalls(1, 2), PlanMode.Edit);

        // A spare row and column, so a plan can always grow.
        Assert.Contains("plan__empty", html);
        Assert.Contains("Empty square, row B, 1", html);
    }

    // ── what a square says about itself ──────────────────────────────────────

    [Fact]
    public async Task A_state_is_a_colour_and_a_glyph_and_a_word()
    {
        // About one man in twelve cannot separate the red from the green, and a plan that tells
        // him nothing is one that sells him somebody else's seat.
        var plan = Stalls(1, 3);
        var keys = plan.Units.Select(u => u.Key).ToList();
        var html = await RenderAsync(plan, PlanMode.Pick, new Dictionary<Guid, PlanCellState>
        {
            [keys[0]] = PlanCellState.Taken,
            [keys[1]] = PlanCellState.Pending,
            [keys[2]] = PlanCellState.Free,
        });

        Assert.Contains("data-state=\"taken\"", html);
        Assert.Contains("data-state=\"pending\"", html);
        // The whole sentence, section included: a square is a square to a screen reader, so
        // everything it needs has to be in the one string.
        Assert.Contains("A1, row A, 1, Stalls, taken", html);
        Assert.Contains("A2, row A, 2, Stalls, held by somebody else", html);
        Assert.Contains("A3, row A, 3, Stalls, free", html);
        // The glyph, so the state survives a black-and-white printout.
        Assert.Contains("#x", html);
        Assert.Contains("#clock", html);
    }

    [Fact]
    public async Task A_guest_can_press_a_free_seat_and_not_a_taken_one()
    {
        var plan = Stalls(1, 2);
        var keys = plan.Units.Select(u => u.Key).ToList();

        var html = await RenderAsync(plan, PlanMode.Pick, new Dictionary<Guid, PlanCellState>
        {
            [keys[0]] = PlanCellState.Taken,
        });

        Assert.Equal(1, Count(html, "disabled"));
    }

    [Fact]
    public async Task A_read_only_plan_answers_nothing_at_all()
    {
        // The board and the public page show how full a night is; neither is a place to choose.
        var html = await RenderAsync(Stalls(1, 3), PlanMode.Read);

        Assert.Equal(3, Count(html, "disabled"));
        Assert.Contains("role=\"gridcell\"", html);
        Assert.DoesNotContain("aria-checked", html);
    }

    [Fact]
    public async Task A_room_says_how_many_it_holds()
    {
        var plan = new PlanModel(HostedEventLayoutKind.Rooms);
        var key = plan.AddRoom(Guid.NewGuid(), "The Suite", capacity: 4)!.Value;
        plan.Place(key, 0, 0);

        var html = await RenderAsync(plan);

        Assert.Contains("The Suite", html);
        Assert.Contains("plan__unit-holds", html);
        Assert.Contains("holds 4", html);
    }

    // ── the edges and the legend ─────────────────────────────────────────────

    [Fact]
    public async Task Rows_are_lettered_and_a_uniform_row_is_captioned_with_its_section()
    {
        // This is what gives a hotel its floor captions without a column of its own.
        var plan = new PlanModel(HostedEventLayoutKind.Seats);
        plan.AddBlock(1, 2, section: "Stalls");
        plan.AddBlock(1, 2, section: "Balcony");

        var html = await RenderAsync(plan, PlanMode.Read);

        Assert.Contains("plan__row-letter", html);
        Assert.Contains(">Stalls<", html);
        Assert.Contains(">Balcony<", html);
    }

    [Fact]
    public async Task The_legend_prints_the_price_once_per_section_rather_than_in_every_square()
    {
        // Four hundred squares each printing a price is four hundred things to read; one line is
        // the same fact once, and it is where a venue checks it.
        var plan = new PlanModel(HostedEventLayoutKind.Seats);
        plan.AddBlock(2, 3, section: "Stalls", price: 24m);
        plan.AddBlock(1, 3, section: "Balcony", price: 18m);

        var html = await RenderAsync(plan, PlanMode.Read);

        Assert.Contains("Stalls · 6 seats", html);
        Assert.Contains("Balcony · 3 seats", html);
        Assert.Contains("24", html);
        Assert.Contains("18", html);
    }

    [Fact]
    public async Task A_section_priced_two_ways_prints_no_price_rather_than_a_wrong_one()
    {
        var plan = new PlanModel(HostedEventLayoutKind.Seats);
        plan.AddBlock(1, 2, section: "Stalls", price: 24m);
        plan.AddBlock(1, 2, section: "Stalls", price: 30m);

        var html = await RenderAsync(plan, PlanMode.Read);

        Assert.Contains("Stalls · 4 seats", html);
        Assert.DoesNotContain("Stalls · 4 seats ·", html);
    }

    [Fact]
    public async Task An_empty_plan_says_so_instead_of_drawing_nothing()
    {
        var html = await RenderAsync(new PlanModel(HostedEventLayoutKind.Rooms), PlanMode.Read);

        Assert.Contains("plan-empty", html);
        Assert.Contains("Nothing on the plan yet.", html);
    }

    // ── the keyboard's own affordances ───────────────────────────────────────

    [Fact]
    public async Task Exactly_one_square_is_in_the_tab_order()
    {
        // A roving tabindex: tab reaches the plan once, and the arrows move within it. Four
        // hundred tab stops would make the rest of the page unreachable by keyboard.
        var html = await RenderAsync(Stalls(3, 4));

        Assert.Equal(1, Count(html, "tabindex=\"0\""));
    }

    [Fact]
    public async Task The_plan_says_out_loud_what_was_chosen()
    {
        var plan = Stalls(1, 2);
        var html = await RenderAsync(plan, PlanMode.Edit, selection: new HashSet<Guid> { plan.Units[0].Key });

        Assert.Contains("aria-live=\"polite\"", html);
        Assert.Contains("data-selected=\"true\"", html);
    }

    [Fact]
    public async Task Compact_asks_for_smaller_squares_without_changing_what_is_on_them()
    {
        var html = await RenderAsync(Stalls(2, 2), compact: true);

        Assert.Contains("plan--compact", html);
        Assert.Equal(4, Count(html, "class=\"plan__unit\""));
    }
}
